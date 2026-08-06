using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_files_users_uploaded_by",
                table: "files");

            migrationBuilder.DropIndex(
                name: "ix_files_uploaded_by",
                table: "files");

            migrationBuilder.DropIndex(
                name: "ix_files_version_group_id_version_number",
                table: "files");

            migrationBuilder.DropCheckConstraint(
                name: "ck_access_entries_scope_anchor",
                table: "access_entries");

            // Scaffolding turns the two columns below into renames of version_group_id and
            // uploaded_by, because the shapes happen to line up. They must not be renames: a
            // revision id is not a version-group id, and a converted-from file id is certainly
            // not a user id. Both are added empty, filled from the columns they replace where
            // there is anything to fill, and the old columns dropped afterwards.
            migrationBuilder.AddColumn<Guid>(
                name: "document_version_id",
                table: "files",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "converted_from_file_id",
                table: "files",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "author",
                table: "files",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "codec",
                table: "files",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "content_created_at",
                table: "files",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "content_modified_at",
                table: "files",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "conversion",
                table: "files",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<double>(
                name: "duration_seconds",
                table: "files",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "page_count",
                table: "files",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "producer",
                table: "files",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "text_extraction",
                table: "files",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<string>(
                name: "text_extraction_error",
                table: "files",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cabinets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ancestor_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false, defaultValueSql: "'{}'::uuid[]"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    path = table.Column<string>(type: "ltree", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cabinets", x => x.id);
                    table.CheckConstraint("ck_cabinets_no_self_parent", "parent_id <> id");
                    table.ForeignKey(
                        name: "fk_cabinets_cabinets_parent_id",
                        column: x => x.parent_id,
                        principalTable: "cabinets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_pages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_number = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: true),
                    extractor = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    extractor_version = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_pages", x => x.id);
                    table.CheckConstraint("ck_document_pages_number", "page_number >= 1");
                    table.ForeignKey(
                        name: "fk_document_pages_stored_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document_types",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    metadata_schema = table.Column<string>(type: "jsonb", nullable: true),
                    metadata_schema_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "file_access_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_access_log", x => x.id);
                });

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

            migrationBuilder.CreateTable(
                name: "document_type_schemas",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    document_type_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    schema = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_type_schemas", x => x.id);
                    table.CheckConstraint("ck_document_type_schemas_version", "version >= 1");
                    table.ForeignKey(
                        name: "fk_document_type_schemas_document_types_document_type_id",
                        column: x => x.document_type_id,
                        principalTable: "document_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    document_type_id = table.Column<long>(type: "bigint", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    metadata_schema_version = table.Column<int>(type: "integer", nullable: true),
                    language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caving_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_documents_caving_groups_caving_group_id",
                        column: x => x.caving_group_id,
                        principalTable: "caving_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_documents_document_types_document_type_id",
                        column: x => x.document_type_id,
                        principalTable: "document_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_documents_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cabinet_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cabinet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cabinet_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_cabinet_documents_cabinets_cabinet_id",
                        column: x => x.cabinet_id,
                        principalTable: "cabinets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cabinet_documents_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document_comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: true),
                    anchor_kind = table.Column<short>(type: "smallint", nullable: false),
                    anchor = table.Column<string>(type: "jsonb", nullable: true),
                    anchor_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_comments", x => x.id);
                    table.CheckConstraint("ck_document_comments_anchor_payload", "(anchor_kind = 0 AND anchor IS NULL AND anchor_file_id IS NULL) OR (anchor_kind <> 0 AND anchor IS NOT NULL)");
                    table.CheckConstraint("ck_document_comments_body", "length(btrim(body)) > 0");
                    table.ForeignKey(
                        name: "fk_document_comments_document_comments_parent_id",
                        column: x => x.parent_id,
                        principalTable: "document_comments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_document_comments_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_document_comments_stored_files_anchor_file_id",
                        column: x => x.anchor_file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_document_comments_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "document_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    change_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    document_date = table.Column<DateOnly>(type: "date", nullable: true),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_versions", x => x.id);
                    table.CheckConstraint("ck_document_versions_number", "version_number >= 1");
                    table.ForeignKey(
                        name: "fk_document_versions_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_document_versions_users_uploaded_by",
                        column: x => x.uploaded_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            // ---- carry the existing file-level version chains over ----
            // One document per chain, keeping the chain's own identity (it already meant
            // "stable document id", so anything anchored on it keeps working), and one
            // revision per file row, keeping that file's id. Files whose uploader is gone
            // fall back to the installation's oldest account so the owner column can be
            // required; ids are time-ordered, so that is the account created first. A
            // database holding files but no accounts cannot arise, and would fail loudly.
            // On an empty database every statement below is a no-op.
            migrationBuilder.Sql("""
                INSERT INTO documents (id, title, owner_user_id, caving_group_id, visibility, metadata, created_at, updated_at)
                SELECT head.version_group_id,
                       left(head.original_name, 300),
                       coalesce(head.uploaded_by, (SELECT u.id FROM users u ORDER BY u.id LIMIT 1)),
                       NULL,
                       0,
                       '{}'::jsonb,
                       head.created_at,
                       head.updated_at
                FROM files head
                WHERE head.version_number = (
                    SELECT max(f.version_number) FROM files f
                    WHERE f.version_group_id = head.version_group_id);
                """);

            migrationBuilder.Sql("""
                INSERT INTO document_versions (
                    id, document_id, version_number, is_current, label, change_note,
                    document_date, uploaded_by, created_at, updated_at)
                SELECT f.id,
                       f.version_group_id,
                       f.version_number,
                       f.version_number = (
                           SELECT max(h.version_number) FROM files h
                           WHERE h.version_group_id = f.version_group_id),
                       NULL,
                       NULL,
                       f.document_date,
                       f.uploaded_by,
                       f.created_at,
                       f.updated_at
                FROM files f;
                """);

            migrationBuilder.Sql("UPDATE files SET document_version_id = id;");

            // An image is one page by definition; paged formats wait for text extraction,
            // which is the only thing that knows their real count.
            migrationBuilder.Sql("""
                INSERT INTO document_pages (file_id, page_number)
                SELECT id, 1 FROM files WHERE kind = 0;
                """);

            // Page count is a column rather than derived data, so files whose pages are already
            // known get the count they always had. Taken from the page rows themselves rather
            // than assumed, so the column and the rows agree from the start.
            migrationBuilder.Sql("""
                UPDATE files f
                SET page_count = counted.pages
                FROM (SELECT file_id, count(*) AS pages FROM document_pages GROUP BY file_id) counted
                WHERE counted.file_id = f.id AND f.page_count IS NULL;
                """);

            // Every stored file belongs to a document revision, so the column can be required
            // now that the rows carrying one have it.
            migrationBuilder.AlterColumn<Guid>(
                name: "document_version_id",
                table: "files",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            // The version detail now lives on the revision rows, so the file columns that held
            // it go — after the carry above, never before it.
            migrationBuilder.DropColumn(
                name: "document_date",
                table: "files");

            migrationBuilder.DropColumn(
                name: "uploaded_by",
                table: "files");

            migrationBuilder.DropColumn(
                name: "version_group_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "version_number",
                table: "files");

            migrationBuilder.CreateIndex(
                name: "ix_files_converted_from_file_id",
                table: "files",
                column: "converted_from_file_id",
                unique: true,
                filter: "converted_from_file_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_files_document_version_id",
                table: "files",
                column: "document_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_files_text_extraction",
                table: "files",
                column: "text_extraction");

            migrationBuilder.AddCheckConstraint(
                name: "ck_files_duration_seconds",
                table: "files",
                sql: "duration_seconds is null or duration_seconds >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_files_page_count",
                table: "files",
                sql: "page_count is null or page_count >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_access_entries_scope_anchor",
                table: "access_entries",
                sql: "(scope_kind IN (0, 1) AND scope_feature_id IS NULL AND scope_id IS NULL) OR (scope_kind IN (2, 4, 6) AND scope_feature_id IS NULL AND scope_id IS NOT NULL) OR (scope_kind = 3 AND scope_feature_id IS NOT NULL AND scope_id IS NULL) OR (scope_kind = 5 AND ((domain = 0 AND scope_feature_id IS NOT NULL AND scope_id IS NULL) OR (domain <> 0 AND scope_feature_id IS NULL AND scope_id IS NOT NULL)))");

            migrationBuilder.CreateIndex(
                name: "ix_cabinet_documents_cabinet_id_document_id",
                table: "cabinet_documents",
                columns: new[] { "cabinet_id", "document_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cabinet_documents_document_id",
                table: "cabinet_documents",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_cabinets_ancestor_ids",
                table: "cabinets",
                column: "ancestor_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_cabinets_parent_id",
                table: "cabinets",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_cabinets_parent_name",
                table: "cabinets",
                columns: new[] { "parent_id", "name" },
                unique: true,
                filter: "parent_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cabinets_path",
                table: "cabinets",
                column: "path")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_cabinets_root_name",
                table: "cabinets",
                column: "name",
                unique: true,
                filter: "parent_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_document_comments_anchor_file_id",
                table: "document_comments",
                column: "anchor_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_comments_author_id",
                table: "document_comments",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_comments_document_id_created_at",
                table: "document_comments",
                columns: new[] { "document_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_document_comments_parent_id",
                table: "document_comments",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_pages_file_id_page_number",
                table: "document_pages",
                columns: new[] { "file_id", "page_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_type_schemas_document_type_id_version",
                table: "document_type_schemas",
                columns: new[] { "document_type_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_types_code",
                table: "document_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_versions_current",
                table: "document_versions",
                column: "document_id",
                unique: true,
                filter: "is_current");

            migrationBuilder.CreateIndex(
                name: "ix_document_versions_document_id_version_number",
                table: "document_versions",
                columns: new[] { "document_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_versions_uploaded_by",
                table: "document_versions",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "ix_documents_caving_group_id",
                table: "documents",
                column: "caving_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_document_type_id",
                table: "documents",
                column: "document_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_owner_user_id",
                table: "documents",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_file_access_log_at",
                table: "file_access_log",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_file_access_log_document_id_at",
                table: "file_access_log",
                columns: new[] { "document_id", "at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_file_access_log_user_id_at",
                table: "file_access_log",
                columns: new[] { "user_id", "at" },
                descending: new[] { false, true });

            migrationBuilder.AddForeignKey(
                name: "fk_files_document_versions_document_version_id",
                table: "files",
                column: "document_version_id",
                principalTable: "document_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_files_files_converted_from_file_id",
                table: "files",
                column: "converted_from_file_id",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            // ---------------------------------------------------------------------------
            // Everything below is hand-written and is NOT reproduced by scaffolding. If this
            // migration is ever regenerated, these statements must be re-added, in this order:
            // the configurations before the rows that name them, the functions before the
            // triggers that call them, the column before its index.
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

            migrationBuilder.Sql(
                """
                CREATE INDEX ix_document_pages_search_vector
                    ON document_pages USING gin (search_vector);
                """);

            // Every file that was already stored gets the resting extraction state, which says
            // the format carries no text at all — true of a photograph and false of every
            // document an installation has been collecting until now, and it is a sentence shown
            // to whoever opens the document. Only the readers know which formats are which, so
            // rather than restate that list in SQL this queues the one sweep that asks them, and
            // it queues it once: the sweep is idempotent, so a database that has already been
            // swept simply finds nothing to do. An installation that starts here has no files to
            // sweep, and gets no job.
            migrationBuilder.Sql(
                """
                INSERT INTO processing_jobs (kind, payload, status, attempts, created_at)
                SELECT 'text-extraction-backfill', '{}'::jsonb, 0, 0, now()
                WHERE EXISTS (SELECT 1 FROM files);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The triggers go with their tables, but the functions, the search configurations
            // and the index do not, so they are named here. Dropped before anything else,
            // because the fold-back below writes to files and would otherwise re-enter them.
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS documents_language_reindex ON documents;
                DROP TRIGGER IF EXISTS document_pages_search_vector ON document_pages;
                DROP FUNCTION IF EXISTS documents_language_reindex_trigger();
                DROP FUNCTION IF EXISTS document_pages_search_vector_trigger();
                DROP INDEX IF EXISTS ix_document_pages_search_vector;
                DROP FUNCTION IF EXISTS document_page_search_vector(text, regconfig);
                DROP FUNCTION IF EXISTS document_indexed_text(text);
                DROP FUNCTION IF EXISTS document_search_config(text);
                DROP TEXT SEARCH CONFIGURATION IF EXISTS public.romanian_unaccent;
                DROP TEXT SEARCH CONFIGURATION IF EXISTS public.english_unaccent;
                DROP TEXT SEARCH CONFIGURATION IF EXISTS public.simple_unaccent;
                DROP FUNCTION IF EXISTS create_unaccent_search_config(text, text, text);
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "version_group_id",
                table: "files",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "version_number",
                table: "files",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateOnly>(
                name: "document_date",
                table: "files",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "uploaded_by",
                table: "files",
                type: "uuid",
                nullable: true);

            // Fold the version detail back onto the file rows before the tables holding it go.
            migrationBuilder.Sql("""
                UPDATE files f
                SET version_group_id = v.document_id,
                    version_number = v.version_number,
                    document_date = v.document_date,
                    uploaded_by = v.uploaded_by
                FROM document_versions v
                WHERE v.id = f.document_version_id;
                """);

            // A derived copy has no place in a schema that does not know about conversion, and
            // its original keeps its own row; the copy's file row is removed rather than
            // stranded, which is what the forward direction would have produced from it.
            migrationBuilder.Sql("DELETE FROM files WHERE converted_from_file_id IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "fk_files_document_versions_document_version_id",
                table: "files");

            migrationBuilder.DropForeignKey(
                name: "fk_files_files_converted_from_file_id",
                table: "files");

            migrationBuilder.DropTable(
                name: "cabinet_documents");

            migrationBuilder.DropTable(
                name: "document_comments");

            migrationBuilder.DropTable(
                name: "document_pages");

            migrationBuilder.DropTable(
                name: "document_type_schemas");

            migrationBuilder.DropTable(
                name: "document_versions");

            migrationBuilder.DropTable(
                name: "file_access_log");

            migrationBuilder.DropTable(
                name: "text_search_languages");

            migrationBuilder.DropTable(
                name: "cabinets");

            migrationBuilder.DropTable(
                name: "documents");

            migrationBuilder.DropTable(
                name: "document_types");

            migrationBuilder.DropIndex(
                name: "ix_files_converted_from_file_id",
                table: "files");

            migrationBuilder.DropIndex(
                name: "ix_files_document_version_id",
                table: "files");

            migrationBuilder.DropIndex(
                name: "ix_files_text_extraction",
                table: "files");

            migrationBuilder.DropCheckConstraint(
                name: "ck_files_duration_seconds",
                table: "files");

            migrationBuilder.DropCheckConstraint(
                name: "ck_files_page_count",
                table: "files");

            migrationBuilder.DropCheckConstraint(
                name: "ck_access_entries_scope_anchor",
                table: "access_entries");

            migrationBuilder.DropColumn(
                name: "author",
                table: "files");

            migrationBuilder.DropColumn(
                name: "codec",
                table: "files");

            migrationBuilder.DropColumn(
                name: "content_created_at",
                table: "files");

            migrationBuilder.DropColumn(
                name: "content_modified_at",
                table: "files");

            migrationBuilder.DropColumn(
                name: "conversion",
                table: "files");

            migrationBuilder.DropColumn(
                name: "duration_seconds",
                table: "files");

            migrationBuilder.DropColumn(
                name: "page_count",
                table: "files");

            migrationBuilder.DropColumn(
                name: "producer",
                table: "files");

            migrationBuilder.DropColumn(
                name: "text_extraction",
                table: "files");

            migrationBuilder.DropColumn(
                name: "text_extraction_error",
                table: "files");

            migrationBuilder.DropColumn(
                name: "document_version_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "converted_from_file_id",
                table: "files");

            migrationBuilder.CreateIndex(
                name: "ix_files_uploaded_by",
                table: "files",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "ix_files_version_group_id_version_number",
                table: "files",
                columns: new[] { "version_group_id", "version_number" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_access_entries_scope_anchor",
                table: "access_entries",
                sql: "(scope_kind IN (0, 1) AND scope_feature_id IS NULL AND scope_id IS NULL) OR (scope_kind IN (2, 4) AND scope_feature_id IS NULL AND scope_id IS NOT NULL) OR (scope_kind = 3 AND scope_feature_id IS NOT NULL AND scope_id IS NULL) OR (scope_kind = 5 AND ((domain = 0 AND scope_feature_id IS NOT NULL AND scope_id IS NULL) OR (domain <> 0 AND scope_feature_id IS NULL AND scope_id IS NOT NULL)))");

            migrationBuilder.AddForeignKey(
                name: "fk_files_users_uploaded_by",
                table: "files",
                column: "uploaded_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
