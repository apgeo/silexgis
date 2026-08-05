using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentContentIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:ltree", ",,")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:ltree", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:unaccent", ",,");

            migrationBuilder.AddColumn<string>(
                name: "language",
                table: "documents",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "text_search_languages",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    configuration = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_text_search_languages", x => x.code);
                });

            // ---------------------------------------------------------------------------
            // Everything below is hand-written and is NOT reproduced by scaffolding a fresh
            // initial migration. If these statements are ever consolidated into one, they must
            // be re-added, in this order: the configurations before the rows that name them,
            // the functions before the triggers that call them, the column before its index.
            // ---------------------------------------------------------------------------

            // An accent-folding text-search configuration per language. Folding is done by a
            // dictionary inside the parser rather than by unaccenting the text before it is
            // indexed, and the difference is visible to a reader: with the dictionary the stored
            // text keeps the diacritics its author wrote, so a highlighted snippet says
            // "Peștera" while a search for "pestera" still finds it. Unaccenting the text first
            // matches just as well and then quotes the archive back with its accents removed.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION create_unaccent_search_config(
                    config_name text, base_config text, stem_dictionary text)
                RETURNS void
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    -- The catalogue is asked directly because PostgreSQL has no to_regconfig:
                    -- the to_reg* family covers classes, types and roles but not text-search
                    -- configurations, so naming one that does not exist is an error rather than
                    -- a null.
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_ts_config c
                          JOIN pg_namespace n ON n.oid = c.cfgnamespace
                         WHERE c.cfgname = config_name AND n.nspname = 'public')
                    THEN
                        EXECUTE format(
                            'CREATE TEXT SEARCH CONFIGURATION public.%I (COPY = pg_catalog.%I)',
                            config_name, base_config);
                    END IF;

                    -- Only the token types that can carry an accent are remapped; an ASCII word
                    -- has nothing to fold and reaches the same stem either way.
                    EXECUTE format(
                        'ALTER TEXT SEARCH CONFIGURATION public.%I ALTER MAPPING FOR '
                        || 'hword, hword_part, word WITH public.unaccent, pg_catalog.%I',
                        config_name, stem_dictionary);
                END;
                $function$;
                """);

            migrationBuilder.Sql(
                """
                SELECT create_unaccent_search_config('simple_unaccent', 'simple', 'simple');
                SELECT create_unaccent_search_config('romanian_unaccent', 'romanian', 'romanian_stem');
                SELECT create_unaccent_search_config('english_unaccent', 'english', 'english_stem');
                """);

            // Two rows, and teaching the archive a third language later is a row plus one call to
            // the function above - not a release. PostgreSQL ships around two dozen Snowball
            // configurations and every one of them is reachable from this table.
            migrationBuilder.Sql(
                """
                INSERT INTO text_search_languages (code, configuration) VALUES
                    ('ro', 'romanian_unaccent'),
                    ('en', 'english_unaccent')
                ON CONFLICT (code) DO NOTHING;
                """);

            // A code nobody has mapped, and a row naming a configuration this server does not
            // have, both land on language-neutral folding rather than failing: degraded stemming
            // is a worse search, a missing configuration would be no search at all.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION document_search_config(language_code text)
                RETURNS regconfig
                LANGUAGE sql
                STABLE
                AS $function$
                    SELECT coalesce(
                        (SELECT c.oid::regconfig
                           FROM text_search_languages l
                           JOIN pg_ts_config c ON c.cfgname = l.configuration
                                              AND pg_ts_config_is_visible(c.oid)
                          WHERE l.code = lower(language_code)
                          LIMIT 1),
                        'simple_unaccent'::regconfig);
                $function$;
                """);

            // A page may hold up to two million characters and a tsvector may not exceed one
            // megabyte, so a whole book stored as a single page would fail the write that
            // extracted it. Indexing a bounded prefix keeps extraction succeeding; the text
            // itself is stored and served in full either way.
            //
            // The bound has one home because two things depend on it agreeing: what is indexed
            // and what a highlighted snippet is cut from. Highlight a longer stretch than was
            // indexed and the snippet is slow for no gain; a shorter one and a genuine match can
            // fall outside it, leaving a result quoting an opening paragraph that has nothing to
            // do with the search.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION document_indexed_text(page_text text)
                RETURNS text
                LANGUAGE sql
                IMMUTABLE
                PARALLEL SAFE
                AS $function$
                    SELECT left(coalesce(page_text, ''), 800000);
                $function$;
                """);

            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION document_page_search_vector(page_text text, config regconfig)
                RETURNS tsvector
                LANGUAGE sql
                IMMUTABLE
                PARALLEL SAFE
                AS $function$
                    SELECT to_tsvector(config, document_indexed_text(page_text));
                $function$;
                """);

            // Not a generated column, and not by preference. to_tsvector(regconfig, text) is
            // immutable, but casting a text column to regconfig is a catalogue lookup and only
            // stable, so a generated column whose configuration comes from another column is
            // rejected outright. Verified against the PostgreSQL 17 this project runs on.
            //
            // Not written by the extraction job either: page rows are written by an upload, by a
            // re-reading and by a backfill sweep, and a vector maintained in application code
            // would have to be got right in each of them. The trigger is right in all three, and
            // in anything added later.
            migrationBuilder.Sql("ALTER TABLE document_pages ADD COLUMN search_vector tsvector;");

            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION document_pages_search_vector_trigger()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    config regconfig;
                BEGIN
                    IF new.text IS NULL THEN
                        new.search_vector := NULL;
                        RETURN new;
                    END IF;

                    SELECT document_search_config(d.language)
                      INTO config
                      FROM files f
                      JOIN document_versions v ON v.id = f.document_version_id
                      JOIN documents d ON d.id = v.document_id
                     WHERE f.id = new.file_id;

                    new.search_vector := document_page_search_vector(
                        new.text, coalesce(config, 'simple_unaccent'::regconfig));
                    RETURN new;
                END;
                $function$;
                """);

            // Fired for the text column only: re-reading a file rewrites the extractor stamp on
            // every page whether the words changed or not, and re-deriving an identical vector
            // for each of them would be work with no result. A page whose text genuinely changed
            // is rewritten here, so a re-extraction updates the index rather than leaving it
            // describing bytes that are gone.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER document_pages_search_vector
                BEFORE INSERT OR UPDATE OF text, file_id ON document_pages
                FOR EACH ROW
                EXECUTE FUNCTION document_pages_search_vector_trigger();
                """);

            // Correcting a document's language is the one change that invalidates every page
            // vector under it without touching a single page row, so it re-derives them here.
            // Writing search_vector directly does not re-enter the trigger above, which watches
            // the text column.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION documents_language_reindex_trigger()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    UPDATE document_pages p
                       SET search_vector = document_page_search_vector(
                               p.text, document_search_config(new.language))
                      FROM files f
                      JOIN document_versions v ON v.id = f.document_version_id
                     WHERE p.file_id = f.id
                       AND v.document_id = new.id
                       AND p.text IS NOT NULL;
                    RETURN NULL;
                END;
                $function$;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER documents_language_reindex
                AFTER UPDATE OF language ON documents
                FOR EACH ROW
                WHEN (old.language IS DISTINCT FROM new.language)
                EXECUTE FUNCTION documents_language_reindex_trigger();
                """);

            // Text already read out of the archive is indexed here rather than by re-reading
            // every file: the words are already in the database, and asking the extractors again
            // would cost hours to arrive at the same rows.
            migrationBuilder.Sql(
                """
                UPDATE document_pages p
                   SET search_vector = document_page_search_vector(
                           p.text, document_search_config(d.language))
                  FROM files f
                  JOIN document_versions v ON v.id = f.document_version_id
                  JOIN documents d ON d.id = v.document_id
                 WHERE p.file_id = f.id
                   AND p.text IS NOT NULL;
                """);

            // Built after the backfill, which is cheaper than maintaining it through one.
            migrationBuilder.Sql(
                """
                CREATE INDEX ix_document_pages_search_vector
                    ON document_pages USING gin (search_vector);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS documents_language_reindex ON documents;
                DROP TRIGGER IF EXISTS document_pages_search_vector ON document_pages;
                DROP FUNCTION IF EXISTS documents_language_reindex_trigger();
                DROP FUNCTION IF EXISTS document_pages_search_vector_trigger();
                DROP INDEX IF EXISTS ix_document_pages_search_vector;
                ALTER TABLE document_pages DROP COLUMN IF EXISTS search_vector;
                DROP FUNCTION IF EXISTS document_page_search_vector(text, regconfig);
                DROP FUNCTION IF EXISTS document_indexed_text(text);
                DROP FUNCTION IF EXISTS document_search_config(text);
                DROP TEXT SEARCH CONFIGURATION IF EXISTS public.romanian_unaccent;
                DROP TEXT SEARCH CONFIGURATION IF EXISTS public.english_unaccent;
                DROP TEXT SEARCH CONFIGURATION IF EXISTS public.simple_unaccent;
                DROP FUNCTION IF EXISTS create_unaccent_search_config(text, text, text);
                """);

            migrationBuilder.DropTable(
                name: "text_search_languages");

            migrationBuilder.DropColumn(
                name: "language",
                table: "documents");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:ltree", ",,")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:ltree", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:unaccent", ",,");
        }
    }
}
