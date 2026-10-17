using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:ltree", ",,")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,");

            // What the model cannot describe, written by hand when it was first needed and kept here
            // as the earlier migrations left it: the unaccenting text-search configurations and the
            // functions behind document search. Function bodies are checked when they are first run
            // rather than now, as a schema dump restores them, because they name objects made below.
            migrationBuilder.Sql(""""
                SET LOCAL check_function_bodies = false;
                CREATE FUNCTION public.create_unaccent_search_config(config_name text, base_config text, stem_dictionary text) RETURNS void
                    LANGUAGE plpgsql
                    AS $$
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
                $$;

                CREATE FUNCTION public.document_indexed_text(page_text text) RETURNS text
                    LANGUAGE sql IMMUTABLE PARALLEL SAFE
                    AS $$
                    SELECT left(coalesce(page_text, ''), 800000);
                $$;

                CREATE FUNCTION public.document_page_search_vector(page_text text, config regconfig) RETURNS tsvector
                    LANGUAGE sql IMMUTABLE PARALLEL SAFE
                    AS $$
                    SELECT to_tsvector(config, document_indexed_text(page_text));
                $$;

                CREATE FUNCTION public.document_pages_search_vector_trigger() RETURNS trigger
                    LANGUAGE plpgsql
                    AS $$
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
                $$;

                CREATE FUNCTION public.document_search_config(language_code text) RETURNS regconfig
                    LANGUAGE sql STABLE
                    AS $$
                    SELECT coalesce(
                        (SELECT c.oid::regconfig
                           FROM text_search_languages l
                           JOIN pg_ts_config c ON c.cfgname = l.configuration
                                              AND pg_ts_config_is_visible(c.oid)
                          WHERE l.code = lower(language_code)
                          LIMIT 1),
                        'simple_unaccent'::regconfig);
                $$;

                CREATE FUNCTION public.documents_language_reindex_trigger() RETURNS trigger
                    LANGUAGE plpgsql
                    AS $$
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
                $$;

                CREATE FUNCTION public.immutable_unaccent(text) RETURNS text
                    LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE
                    RETURN public.unaccent('public.unaccent'::regdictionary, $1);

                CREATE TEXT SEARCH CONFIGURATION public.english_unaccent (
                    PARSER = pg_catalog."default" );

                ALTER TEXT SEARCH CONFIGURATION public.english_unaccent
                    ADD MAPPING FOR asciiword WITH english_stem;

                ALTER TEXT SEARCH CONFIGURATION public.english_unaccent
                    ADD MAPPING FOR word WITH public.unaccent, english_stem;

                ALTER TEXT SEARCH CONFIGURATION public.english_unaccent
                    ADD MAPPING FOR numword WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.english_unaccent
                    ADD MAPPING FOR email WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.english_unaccent
                    ADD MAPPING FOR url WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.english_unaccent
                    ADD MAPPING FOR host WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.english_unaccent
                    ADD MAPPING FOR sfloat WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.english_unaccent
                    ADD MAPPING FOR version WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.english_unaccent
                    ADD MAPPING FOR hword_numpart WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.english_unaccent
                    ADD MAPPING FOR hword_part WITH public.unaccent, english_stem;

                ALTER TEXT SEARCH CONFIGURATION public.english_unaccent
                    ADD MAPPING FOR hword_asciipart WITH english_stem;

                ALTER TEXT SEARCH CONFIGURATION public.english_unaccent
                    ADD MAPPING FOR numhword WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.english_unaccent
                    ADD MAPPING FOR asciihword WITH english_stem;

                ALTER TEXT SEARCH CONFIGURATION public.english_unaccent
                    ADD MAPPING FOR hword WITH public.unaccent, english_stem;

                ALTER TEXT SEARCH CONFIGURATION public.english_unaccent
                    ADD MAPPING FOR url_path WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.english_unaccent
                    ADD MAPPING FOR file WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.english_unaccent
                    ADD MAPPING FOR "float" WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.english_unaccent
                    ADD MAPPING FOR "int" WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.english_unaccent
                    ADD MAPPING FOR uint WITH simple;

                CREATE TEXT SEARCH CONFIGURATION public.romanian_unaccent (
                    PARSER = pg_catalog."default" );

                ALTER TEXT SEARCH CONFIGURATION public.romanian_unaccent
                    ADD MAPPING FOR asciiword WITH romanian_stem;

                ALTER TEXT SEARCH CONFIGURATION public.romanian_unaccent
                    ADD MAPPING FOR word WITH public.unaccent, romanian_stem;

                ALTER TEXT SEARCH CONFIGURATION public.romanian_unaccent
                    ADD MAPPING FOR numword WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.romanian_unaccent
                    ADD MAPPING FOR email WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.romanian_unaccent
                    ADD MAPPING FOR url WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.romanian_unaccent
                    ADD MAPPING FOR host WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.romanian_unaccent
                    ADD MAPPING FOR sfloat WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.romanian_unaccent
                    ADD MAPPING FOR version WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.romanian_unaccent
                    ADD MAPPING FOR hword_numpart WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.romanian_unaccent
                    ADD MAPPING FOR hword_part WITH public.unaccent, romanian_stem;

                ALTER TEXT SEARCH CONFIGURATION public.romanian_unaccent
                    ADD MAPPING FOR hword_asciipart WITH romanian_stem;

                ALTER TEXT SEARCH CONFIGURATION public.romanian_unaccent
                    ADD MAPPING FOR numhword WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.romanian_unaccent
                    ADD MAPPING FOR asciihword WITH romanian_stem;

                ALTER TEXT SEARCH CONFIGURATION public.romanian_unaccent
                    ADD MAPPING FOR hword WITH public.unaccent, romanian_stem;

                ALTER TEXT SEARCH CONFIGURATION public.romanian_unaccent
                    ADD MAPPING FOR url_path WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.romanian_unaccent
                    ADD MAPPING FOR file WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.romanian_unaccent
                    ADD MAPPING FOR "float" WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.romanian_unaccent
                    ADD MAPPING FOR "int" WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.romanian_unaccent
                    ADD MAPPING FOR uint WITH simple;

                CREATE TEXT SEARCH CONFIGURATION public.simple_unaccent (
                    PARSER = pg_catalog."default" );

                ALTER TEXT SEARCH CONFIGURATION public.simple_unaccent
                    ADD MAPPING FOR asciiword WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.simple_unaccent
                    ADD MAPPING FOR word WITH public.unaccent, simple;

                ALTER TEXT SEARCH CONFIGURATION public.simple_unaccent
                    ADD MAPPING FOR numword WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.simple_unaccent
                    ADD MAPPING FOR email WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.simple_unaccent
                    ADD MAPPING FOR url WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.simple_unaccent
                    ADD MAPPING FOR host WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.simple_unaccent
                    ADD MAPPING FOR sfloat WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.simple_unaccent
                    ADD MAPPING FOR version WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.simple_unaccent
                    ADD MAPPING FOR hword_numpart WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.simple_unaccent
                    ADD MAPPING FOR hword_part WITH public.unaccent, simple;

                ALTER TEXT SEARCH CONFIGURATION public.simple_unaccent
                    ADD MAPPING FOR hword_asciipart WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.simple_unaccent
                    ADD MAPPING FOR numhword WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.simple_unaccent
                    ADD MAPPING FOR asciihword WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.simple_unaccent
                    ADD MAPPING FOR hword WITH public.unaccent, simple;

                ALTER TEXT SEARCH CONFIGURATION public.simple_unaccent
                    ADD MAPPING FOR url_path WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.simple_unaccent
                    ADD MAPPING FOR file WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.simple_unaccent
                    ADD MAPPING FOR "float" WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.simple_unaccent
                    ADD MAPPING FOR "int" WITH simple;

                ALTER TEXT SEARCH CONFIGURATION public.simple_unaccent
                    ADD MAPPING FOR uint WITH simple;
                """");

            migrationBuilder.CreateTable(
                name: "app_settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_settings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    entity_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    root_entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    root_entity_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    changes = table.Column<string>(type: "jsonb", nullable: true),
                    ip = table.Column<IPAddress>(type: "inet", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cave_types",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cave_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "caving_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<short>(type: "smallint", nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    website = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    logo_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_caving_groups", x => x.id);
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
                name: "duplicate_upload_records",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    new_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    existing_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_duplicate_upload_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "entrance_types",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entrance_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "expedition_roster_roles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expedition_roster_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "feature_sets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_sets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "feature_types",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    category = table.Column<short>(type: "smallint", nullable: false),
                    accepted_geometry_classes = table.Column<short[]>(type: "smallint[]", nullable: false),
                    protected_display = table.Column<short>(type: "smallint", nullable: false),
                    requires_parent = table.Column<bool>(type: "boolean", nullable: false),
                    symbol_file = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    style = table.Column<string>(type: "jsonb", nullable: true),
                    properties_schema = table.Column<string>(type: "jsonb", nullable: true),
                    properties_schema_version = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_types", x => x.id);
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
                name: "hierarchies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hierarchies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "link_kinds",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    locating = table.Column<bool>(type: "boolean", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_link_kinds", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "map_layers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    layer_kind = table.Column<short>(type: "smallint", nullable: false),
                    url_template = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    options = table.Column<string>(type: "jsonb", nullable: true),
                    attribution = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    group_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    min_zoom = table.Column<int>(type: "integer", nullable: false),
                    max_zoom = table.Column<int>(type: "integer", nullable: false),
                    api_key_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_base = table.Column<bool>(type: "boolean", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    in_documents = table.Column<bool>(type: "boolean", nullable: false),
                    in_documents_choice = table.Column<bool>(type: "boolean", nullable: true),
                    on_shared_links = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_map_layers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "message_templates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    body = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "OpenIddictApplications",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    application_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    client_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    client_secret = table.Column<string>(type: "text", nullable: true),
                    client_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    consent_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    display_names = table.Column<string>(type: "text", nullable: true),
                    json_web_key_set = table.Column<string>(type: "text", nullable: true),
                    permissions = table.Column<string>(type: "text", nullable: true),
                    post_logout_redirect_uris = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    redirect_uris = table.Column<string>(type: "text", nullable: true),
                    requirements = table.Column<string>(type: "text", nullable: true),
                    settings = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_open_iddict_applications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "OpenIddictScopes",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    descriptions = table.Column<string>(type: "text", nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    display_names = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    resources = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_open_iddict_scopes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "permission_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_protected = table.Column<bool>(type: "boolean", nullable: false),
                    is_seeded = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permission_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "processing_jobs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    not_before = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    progress_done = table.Column<int>(type: "integer", nullable: true),
                    progress_total = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processing_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "res_link_relation_types",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    directed = table.Column<bool>(type: "boolean", nullable: false),
                    inverse_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_res_link_relation_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rock_types",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rock_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tags",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "terrain_builds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    extent = table.Column<Polygon>(type: "geometry(Polygon, 4326)", nullable: false),
                    requested_max_depth = table.Column<int>(type: "integer", nullable: false),
                    base_build_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    phase = table.Column<short>(type: "smallint", nullable: false),
                    progress = table.Column<int>(type: "integer", nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    log_tail = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    pyramid_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    height_datum = table.Column<short>(type: "smallint", nullable: false),
                    geoid_height_m = table.Column<double>(type: "double precision", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    activation_was_automatic = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terrain_builds", x => x.id);
                    table.CheckConstraint("ck_terrain_builds_progress_range", "progress >= 0 and progress <= 100");
                    table.ForeignKey(
                        name: "fk_terrain_builds_terrain_builds_base_build_id",
                        column: x => x.base_build_id,
                        principalTable: "terrain_builds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
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
                name: "trip_participant_roles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_participant_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "caving_group_announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    caving_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    caving_group_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sender_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    recipient_count = table.Column<int>(type: "integer", nullable: false),
                    segments_per_copy = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expanded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_caving_group_announcements", x => x.id);
                    table.ForeignKey(
                        name: "fk_caving_group_announcements_caving_groups_caving_group_id",
                        column: x => x.caving_group_id,
                        principalTable: "caving_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    bio = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    caving_club_id = table.Column<Guid>(type: "uuid", nullable: true),
                    avatar_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    avatar_preset = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    storage_quota_bytes = table.Column<long>(type: "bigint", nullable: true),
                    pending_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    pending_email_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pending_phone_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    two_factor_authenticator_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_email_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_sms_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    preferred_two_factor_method = table.Column<short>(type: "smallint", nullable: true),
                    real_name_visibility = table.Column<short>(type: "smallint", nullable: false),
                    bio_visibility = table.Column<short>(type: "smallint", nullable: false),
                    email_visibility = table.Column<short>(type: "smallint", nullable: false),
                    phone_visibility = table.Column<short>(type: "smallint", nullable: false),
                    caving_club_visibility = table.Column<short>(type: "smallint", nullable: false),
                    address_visibility = table.Column<short>(type: "smallint", nullable: false),
                    address_point_visibility = table.Column<short>(type: "smallint", nullable: false),
                    ui_preferences = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_one_avatar_source", "avatar_file_id IS NULL OR avatar_preset IS NULL");
                    table.ForeignKey(
                        name: "fk_users_caving_groups_caving_club_id",
                        column: x => x.caving_club_id,
                        principalTable: "caving_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "cabinets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ancestor_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false, defaultValueSql: "'{}'::uuid[]"),
                    default_document_type_id = table.Column<long>(type: "bigint", nullable: true),
                    default_visibility = table.Column<short>(type: "smallint", nullable: true),
                    default_tag_ids = table.Column<long[]>(type: "bigint[]", nullable: false, defaultValueSql: "'{}'::bigint[]"),
                    required_metadata_keys = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
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
                    table.ForeignKey(
                        name: "fk_cabinets_document_types_default_document_type_id",
                        column: x => x.default_document_type_id,
                        principalTable: "document_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
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
                name: "OpenIddictAuthorizations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    application_id = table.Column<string>(type: "text", nullable: true),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    creation_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    scopes = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_open_iddict_authorizations", x => x.id);
                    table.ForeignKey(
                        name: "fk_open_iddict_authorizations_open_iddict_applications_application",
                        column: x => x.application_id,
                        principalTable: "OpenIddictApplications",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "permission_group_members",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    permission_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_kind = table.Column<short>(type: "smallint", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permission_group_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_permission_group_members_permission_groups_permission_group",
                        column: x => x.permission_group_id,
                        principalTable: "permission_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_claims_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "terrain_build_rasters",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    terrain_build_id = table.Column<Guid>(type: "uuid", nullable: false),
                    path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    pixel_size_degrees = table.Column<double>(type: "double precision", nullable: false),
                    footprint = table.Column<Polygon>(type: "geometry(Polygon, 4326)", nullable: false),
                    void_value = table.Column<double>(type: "double precision", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    described_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terrain_build_rasters", x => x.id);
                    table.ForeignKey(
                        name: "fk_terrain_build_rasters_terrain_builds_terrain_build_id",
                        column: x => x.terrain_build_id,
                        principalTable: "terrain_builds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "terrain_build_sources",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    terrain_build_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    attribution = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    licence = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    inherited = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terrain_build_sources", x => x.id);
                    table.ForeignKey(
                        name: "fk_terrain_build_sources_terrain_builds_terrain_build_id",
                        column: x => x.terrain_build_id,
                        principalTable: "terrain_builds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "terrain_derivative_layers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    terrain_build_id = table.Column<Guid>(type: "uuid", nullable: false),
                    derivative = table.Column<short>(type: "smallint", nullable: false),
                    settings = table.Column<string>(type: "jsonb", nullable: false),
                    settings_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    processing_job_id = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    method_revision = table.Column<int>(type: "integer", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terrain_derivative_layers", x => x.id);
                    table.CheckConstraint("ck_terrain_derivative_layers_version", "version >= 0");
                    table.ForeignKey(
                        name: "fk_terrain_derivative_layers_processing_jobs_processing_job_id",
                        column: x => x.processing_job_id,
                        principalTable: "processing_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_terrain_derivative_layers_terrain_builds_terrain_build_id",
                        column: x => x.terrain_build_id,
                        principalTable: "terrain_builds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "account_data_exports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    storage_path = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_data_exports", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_data_exports_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "calendar_feed_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_feed_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_calendar_feed_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cavers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    short_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cavers", x => x.id);
                    table.ForeignKey(
                        name: "fk_cavers_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "checklists",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caving_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checklists", x => x.id);
                    table.ForeignKey(
                        name: "fk_checklists_caving_groups_caving_group_id",
                        column: x => x.caving_group_id,
                        principalTable: "caving_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_checklists_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    place = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caving_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    plan_reminder_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    max_participants = table.Column<int>(type: "integer", nullable: true),
                    series_id = table.Column<Guid>(type: "uuid", nullable: true),
                    series_rule = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    series_frequency = table.Column<short>(type: "smallint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_events", x => x.id);
                    table.CheckConstraint("ck_events_dates", "end_date IS NULL OR end_date > start_date");
                    table.CheckConstraint("ck_events_series_frequency", "series_frequency IS NULL OR series_id IS NOT NULL");
                    table.CheckConstraint("ck_events_series_rule", "series_rule IS NULL OR series_id IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_events_caving_groups_caving_group_id",
                        column: x => x.caving_group_id,
                        principalTable: "caving_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_events_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expeditions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    public_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    geom = table.Column<Geometry>(type: "geometry(Geometry, 4326)", nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caving_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expeditions", x => x.id);
                    table.CheckConstraint("ck_expeditions_dates", "end_date IS NULL OR end_date > start_date");
                    table.ForeignKey(
                        name: "fk_expeditions_caving_groups_caving_group_id",
                        column: x => x.caving_group_id,
                        principalTable: "caving_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_expeditions_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "features",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    feature_type_id = table.Column<long>(type: "bigint", nullable: true),
                    category = table.Column<short>(type: "smallint", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    geom = table.Column<Geometry>(type: "geometry", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    properties_schema_version = table.Column<int>(type: "integer", nullable: true),
                    location_protected = table.Column<bool>(type: "boolean", nullable: false),
                    is_protected_effective = table.Column<bool>(type: "boolean", nullable: false),
                    ancestor_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false, defaultValueSql: "'{}'::uuid[]"),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caving_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    client_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('simple', immutable_unaccent(coalesce(name, '') || ' ' || coalesce(description, '')))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_features", x => x.id);
                    table.UniqueConstraint("ak_features_id_kind", x => new { x.id, x.kind });
                    table.CheckConstraint("ck_features_cave_geom", "kind <> 1 OR geom IS NULL OR geometrytype(geom) = 'POINT'");
                    table.CheckConstraint("ck_features_centerline_geom", "kind <> 3 OR (geom IS NOT NULL AND geometrytype(geom) = 'MULTILINESTRING')");
                    table.CheckConstraint("ck_features_entrance_geom", "kind <> 2 OR (geom IS NOT NULL AND geometrytype(geom) = 'POINT')");
                    table.CheckConstraint("ck_features_generic_type", "(kind = 0) = (feature_type_id IS NOT NULL)");
                    table.CheckConstraint("ck_features_geom_srid", "geom IS NULL OR st_srid(geom) = 4326");
                    table.ForeignKey(
                        name: "fk_features_caving_groups_caving_group_id",
                        column: x => x.caving_group_id,
                        principalTable: "caving_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_features_feature_types_feature_type_id",
                        column: x => x.feature_type_id,
                        principalTable: "feature_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_features_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "map_views",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    config = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    share_token = table.Column<Guid>(type: "uuid", nullable: true),
                    is_home = table.Column<bool>(type: "boolean", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caving_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_map_views", x => x.id);
                    table.ForeignKey(
                        name: "fk_map_views_caving_groups_caving_group_id",
                        column: x => x.caving_group_id,
                        principalTable: "caving_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_map_views_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<short>(type: "smallint", nullable: false),
                    template_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    placeholders = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    target_kind = table.Column<short>(type: "smallint", nullable: true),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    segments_per_copy = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    routed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.CheckConstraint("ck_notifications_target_pair", "(target_kind IS NULL) = (target_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_notifications_users_recipient_user_id",
                        column: x => x.recipient_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "photo_import_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_ids = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    options = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    decisions = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_photo_import_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_photo_import_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "res_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    short_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    relation_type_id = table.Column<long>(type: "bigint", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_res_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_res_links_res_link_relation_types_relation_type_id",
                        column: x => x.relation_type_id,
                        principalTable: "res_link_relation_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_res_links_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "saved_filters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    document = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caving_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_filters", x => x.id);
                    table.ForeignKey(
                        name: "fk_saved_filters_caving_groups_caving_group_id",
                        column: x => x.caving_group_id,
                        principalTable: "caving_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_saved_filters_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sync_sets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caving_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    upload_visibility = table.Column<short>(type: "smallint", nullable: false),
                    settings = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_sets", x => x.id);
                    table.CheckConstraint("ck_sync_sets_revision_positive", "revision >= 1");
                    table.ForeignKey(
                        name: "fk_sync_sets_caving_groups_caving_group_id",
                        column: x => x.caving_group_id,
                        principalTable: "caving_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_sync_sets_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "term_rule_sets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    scope = table.Column<short>(type: "smallint", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    caving_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    rules = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    is_seeded = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_term_rule_sets", x => x.id);
                    table.CheckConstraint("ck_term_rule_sets_group", "(scope = 1 and caving_group_id is not null) or (scope <> 1 and caving_group_id is null)");
                    table.ForeignKey(
                        name: "fk_term_rule_sets_caving_groups_caving_group_id",
                        column: x => x.caving_group_id,
                        principalTable: "caving_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_term_rule_sets_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "user_addresses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    address_text = table.Column<string>(type: "text", nullable: true),
                    geom = table.Column<Point>(type: "geometry(Point, 4326)", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_addresses", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_addresses_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claims_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_user_logins_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_notification_preferences",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<short>(type: "smallint", nullable: false),
                    channel = table.Column<short>(type: "smallint", nullable: false),
                    choice = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_notification_preferences", x => x.id);
                    table.CheckConstraint("ck_user_notification_preferences_one_channel", "channel > 0 AND (channel & (channel - 1)) = 0");
                    table.ForeignKey(
                        name: "fk_user_notification_preferences_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "upload_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    started_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cabinet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tag_id = table.Column<long>(type: "bigint", nullable: true),
                    source_description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    total_count = table.Column<int>(type: "integer", nullable: false),
                    stored_count = table.Column<int>(type: "integer", nullable: false),
                    skipped_count = table.Column<int>(type: "integer", nullable: false),
                    failed_count = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_upload_batches", x => x.id);
                    table.ForeignKey(
                        name: "fk_upload_batches_cabinets_cabinet_id",
                        column: x => x.cabinet_id,
                        principalTable: "cabinets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_upload_batches_tags_tag_id",
                        column: x => x.tag_id,
                        principalTable: "tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_upload_batches_users_started_by_user_id",
                        column: x => x.started_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpenIddictTokens",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    application_id = table.Column<string>(type: "text", nullable: true),
                    authorization_id = table.Column<string>(type: "text", nullable: true),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    creation_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expiration_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    payload = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    redemption_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reference_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_open_iddict_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_open_iddict_tokens_open_iddict_applications_application_id",
                        column: x => x.application_id,
                        principalTable: "OpenIddictApplications",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_open_iddict_tokens_open_iddict_authorizations_authorization_id",
                        column: x => x.authorization_id,
                        principalTable: "OpenIddictAuthorizations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "terrain_derivative_rasters",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    terrain_derivative_layer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    pixel_size_degrees = table.Column<double>(type: "double precision", nullable: false),
                    footprint = table.Column<Polygon>(type: "geometry(Polygon, 4326)", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terrain_derivative_rasters", x => x.id);
                    table.ForeignKey(
                        name: "fk_terrain_derivative_rasters_terrain_derivative_layers_terrai",
                        column: x => x.terrain_derivative_layer_id,
                        principalTable: "terrain_derivative_layers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "caving_group_memberships",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    caver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caving_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_caving_group_memberships", x => x.id);
                    table.ForeignKey(
                        name: "fk_caving_group_memberships_cavers_caver_id",
                        column: x => x.caver_id,
                        principalTable: "cavers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_caving_group_memberships_caving_groups_caving_group_id",
                        column: x => x.caving_group_id,
                        principalTable: "caving_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "checklist_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    checklist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checklist_items", x => x.id);
                    table.UniqueConstraint("ak_checklist_items_checklist_id_id", x => new { x.checklist_id, x.id });
                    table.ForeignKey(
                        name: "fk_checklist_items_checklists_checklist_id",
                        column: x => x.checklist_id,
                        principalTable: "checklists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trip_types",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    field_data_schema = table.Column<string>(type: "jsonb", nullable: true),
                    field_data_schema_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    logistics_schema = table.Column<string>(type: "jsonb", nullable: true),
                    logistics_schema_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    safety_schema = table.Column<string>(type: "jsonb", nullable: true),
                    safety_schema_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    default_checklist_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_types", x => x.id);
                    table.ForeignKey(
                        name: "fk_trip_types_checklists_default_checklist_id",
                        column: x => x.default_checklist_id,
                        principalTable: "checklists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "expedition_roster",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    expedition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<long>(type: "bigint", nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expedition_roster", x => x.id);
                    table.CheckConstraint("ck_expedition_roster_dates", "to_date IS NULL OR to_date >= from_date");
                    table.ForeignKey(
                        name: "fk_expedition_roster_cavers_caver_id",
                        column: x => x.caver_id,
                        principalTable: "cavers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expedition_roster_expedition_roster_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "expedition_roster_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expedition_roster_expeditions_expedition_id",
                        column: x => x.expedition_id,
                        principalTable: "expeditions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "access_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    permission_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_kind = table.Column<short>(type: "smallint", nullable: true),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    effect = table.Column<short>(type: "smallint", nullable: false),
                    domain = table.Column<short>(type: "smallint", nullable: false),
                    actions = table.Column<int>(type: "integer", nullable: false),
                    scope_kind = table.Column<short>(type: "smallint", nullable: false),
                    scope_feature_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: true),
                    feature_kind = table.Column<short>(type: "smallint", nullable: true),
                    feature_type_id = table.Column<long>(type: "bigint", nullable: true),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    granted_via_expedition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_entries", x => x.id);
                    table.CheckConstraint("ck_access_entries_granted_via_expedition", "granted_via_expedition_id IS NULL OR (domain = 1 AND scope_kind = 5)");
                    table.CheckConstraint("ck_access_entries_narrowing", "(feature_kind IS NULL AND feature_type_id IS NULL) OR (domain = 0 AND scope_kind IN (0, 1) AND (feature_kind IS NULL OR feature_type_id IS NULL))");
                    table.CheckConstraint("ck_access_entries_one_home", "(permission_group_id IS NOT NULL AND subject_kind IS NULL AND subject_id IS NULL) OR (permission_group_id IS NULL AND subject_kind IS NOT NULL AND subject_id IS NOT NULL)");
                    table.CheckConstraint("ck_access_entries_scope_anchor", "(scope_kind IN (0, 1) AND scope_feature_id IS NULL AND scope_id IS NULL) OR (scope_kind IN (2, 4, 6) AND scope_feature_id IS NULL AND scope_id IS NOT NULL) OR (scope_kind = 3 AND scope_feature_id IS NOT NULL AND scope_id IS NULL) OR (scope_kind = 5 AND ((domain = 0 AND scope_feature_id IS NOT NULL AND scope_id IS NULL) OR (domain <> 0 AND scope_feature_id IS NULL AND scope_id IS NOT NULL)))");
                    table.ForeignKey(
                        name: "fk_access_entries_expeditions_granted_via_expedition_id",
                        column: x => x.granted_via_expedition_id,
                        principalTable: "expeditions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_entries_feature_types_feature_type_id",
                        column: x => x.feature_type_id,
                        principalTable: "feature_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_entries_features_scope_feature_id",
                        column: x => x.scope_feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_entries_permission_groups_permission_group_id",
                        column: x => x.permission_group_id,
                        principalTable: "permission_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_access_entries_users_granted_by",
                        column: x => x.granted_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "cave_depth_places",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cave_feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    depth_m = table.Column<decimal>(type: "numeric(7,1)", precision: 7, scale: 1, nullable: true),
                    viewer_station_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    place_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_landmark = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cave_depth_places", x => x.id);
                    table.ForeignKey(
                        name: "fk_cave_depth_places_features_cave_feature_id",
                        column: x => x.cave_feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cave_level_bands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cave_feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    bands = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cave_level_bands", x => x.id);
                    table.ForeignKey(
                        name: "fk_cave_level_bands_features_cave_feature_id",
                        column: x => x.cave_feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cave_level_bands_users_confirmed_by",
                        column: x => x.confirmed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cave_qr_publications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    published_by = table.Column<Guid>(type: "uuid", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cave_qr_publications", x => x.id);
                    table.ForeignKey(
                        name: "fk_cave_qr_publications_features_feature_id",
                        column: x => x.feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cave_qr_publications_users_published_by",
                        column: x => x.published_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "caves",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    other_toponyms = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    identification_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    cave_type_id = table.Column<long>(type: "bigint", nullable: false),
                    website = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    region = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    hydrographic_basin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    valley = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tributary_river = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    closest_address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    land_registry_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    location_notes = table.Column<string>(type: "text", nullable: true),
                    rock_type_id = table.Column<long>(type: "bigint", nullable: true),
                    rock_age = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    surveyed_length = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    estimated_length = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    real_extension = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    projected_extension = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    depth = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    positive_depth = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    negative_depth = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    potential_depth = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    altitude = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    volume = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    area = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    ramification_index = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: true),
                    cave_age = table.Column<int>(type: "integer", nullable: true),
                    exploration_status = table.Column<short>(type: "smallint", nullable: false),
                    protection_class = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_show_cave = table.Column<bool>(type: "boolean", nullable: false),
                    show_cave_length = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    discovery_date = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    discoverer = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    entrance_count = table.Column<int>(type: "integer", nullable: false),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('simple', immutable_unaccent(coalesce(other_toponyms, '') || ' ' || coalesce(identification_code, '')))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_caves", x => x.id);
                    table.CheckConstraint("ck_caves_kind", "kind = 1");
                    table.ForeignKey(
                        name: "fk_caves_cave_types_cave_type_id",
                        column: x => x.cave_type_id,
                        principalTable: "cave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_caves_features_id_kind",
                        columns: x => new { x.id, x.kind },
                        principalTable: "features",
                        principalColumns: new[] { "id", "kind" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_caves_rock_types_rock_type_id",
                        column: x => x.rock_type_id,
                        principalTable: "rock_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "feature_ancestors",
                columns: table => new
                {
                    feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ancestor_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_ancestors", x => new { x.feature_id, x.ancestor_id });
                    table.ForeignKey(
                        name: "fk_feature_ancestors_features_ancestor_id",
                        column: x => x.ancestor_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_feature_ancestors_features_feature_id",
                        column: x => x.feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "feature_external_ids",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    system = table.Column<short>(type: "smallint", nullable: false),
                    value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_external_ids", x => x.id);
                    table.ForeignKey(
                        name: "fk_feature_external_ids_features_feature_id",
                        column: x => x.feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "feature_hierarchy_edges",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    child_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_hierarchy_edges", x => x.id);
                    table.CheckConstraint("ck_feature_hierarchy_edges_no_self", "parent_id <> child_id");
                    table.ForeignKey(
                        name: "fk_feature_hierarchy_edges_features_child_id",
                        column: x => x.child_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_feature_hierarchy_edges_features_parent_id",
                        column: x => x.parent_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "feature_links",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    from_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_id = table.Column<Guid>(type: "uuid", nullable: false),
                    link_kind_id = table.Column<long>(type: "bigint", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_links", x => x.id);
                    table.CheckConstraint("ck_feature_links_no_self", "from_id <> to_id");
                    table.ForeignKey(
                        name: "fk_feature_links_features_from_id",
                        column: x => x.from_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_feature_links_features_to_id",
                        column: x => x.to_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_feature_links_link_kinds_link_kind_id",
                        column: x => x.link_kind_id,
                        principalTable: "link_kinds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "feature_set_members",
                columns: table => new
                {
                    feature_set_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_set_members", x => new { x.feature_set_id, x.feature_id });
                    table.ForeignKey(
                        name: "fk_feature_set_members_feature_sets_feature_set_id",
                        column: x => x.feature_set_id,
                        principalTable: "feature_sets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_feature_set_members_features_feature_id",
                        column: x => x.feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "feature_shares",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    mode = table.Column<short>(type: "smallint", nullable: false),
                    include_subtree = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_shares", x => x.id);
                    table.ForeignKey(
                        name: "fk_feature_shares_features_feature_id",
                        column: x => x.feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_feature_shares_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hierarchy_memberships",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    hierarchy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_feature_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    path = table.Column<string>(type: "ltree", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hierarchy_memberships", x => x.id);
                    table.ForeignKey(
                        name: "fk_hierarchy_memberships_features_feature_id",
                        column: x => x.feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_hierarchy_memberships_features_parent_feature_id",
                        column: x => x.parent_feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_hierarchy_memberships_hierarchies_hierarchy_id",
                        column: x => x.hierarchy_id,
                        principalTable: "hierarchies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "taggings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tag_id = table.Column<long>(type: "bigint", nullable: false),
                    feature_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entity_type = table.Column<short>(type: "smallint", nullable: true),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    added_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_taggings", x => x.id);
                    table.CheckConstraint("ck_taggings_one_target", "(feature_id IS NOT NULL AND entity_type IS NULL AND entity_id IS NULL) OR (feature_id IS NULL AND entity_type IS NOT NULL AND entity_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_taggings_features_feature_id",
                        column: x => x.feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_taggings_tags_tag_id",
                        column: x => x.tag_id,
                        principalTable: "tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_taggings_users_added_by",
                        column: x => x.added_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "notification_deliveries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    notification_id = table.Column<long>(type: "bigint", nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    not_before = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    segments = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "fk_notification_deliveries_notifications_notification_id",
                        column: x => x.notification_id,
                        principalTable: "notifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sync_set_members",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sync_set_id = table.Column<Guid>(type: "uuid", nullable: false),
                    root_feature_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_set_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_sync_set_members_features_root_feature_id",
                        column: x => x.root_feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sync_set_members_sync_sets_sync_set_id",
                        column: x => x.sync_set_id,
                        principalTable: "sync_sets",
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
                    upload_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                        name: "fk_documents_upload_batches_upload_batch_id",
                        column: x => x.upload_batch_id,
                        principalTable: "upload_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_documents_users_deleted_by_user_id",
                        column: x => x.deleted_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_documents_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "upload_batch_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    upload_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_path = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    outcome = table.Column<short>(type: "smallint", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cabinet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    duplicate_of_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_upload_batch_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_upload_batch_items_upload_batches_upload_batch_id",
                        column: x => x.upload_batch_id,
                        principalTable: "upload_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "upload_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    declared_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    received_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    cabinet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    upload_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attach_entity_type = table.Column<short>(type: "smallint", nullable: true),
                    attach_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attach_feature_id = table.Column<Guid>(type: "uuid", nullable: true),
                    relative_path = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_upload_sessions", x => x.id);
                    table.CheckConstraint("ck_upload_sessions_received_within_declared", "received_bytes >= 0 and received_bytes <= declared_size_bytes");
                    table.ForeignKey(
                        name: "fk_upload_sessions_cabinets_cabinet_id",
                        column: x => x.cabinet_id,
                        principalTable: "cabinets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_upload_sessions_upload_batches_upload_batch_id",
                        column: x => x.upload_batch_id,
                        principalTable: "upload_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_upload_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    trip_type_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_report_templates_trip_types_trip_type_id",
                        column: x => x.trip_type_id,
                        principalTable: "trip_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "trip_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    trip_type_id = table.Column<long>(type: "bigint", nullable: true),
                    trip_date = table.Column<DateOnly>(type: "date", nullable: false),
                    trip_date_end = table.Column<DateOnly>(type: "date", nullable: true),
                    entry_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    exit_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    results = table.Column<string>(type: "text", nullable: true),
                    weather_conditions = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    location_text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    depth_reached_m = table.Column<decimal>(type: "numeric(7,1)", precision: 7, scale: 1, nullable: true),
                    length_surveyed_m = table.Column<decimal>(type: "numeric(9,1)", precision: 9, scale: 1, nullable: true),
                    survey_stations = table.Column<int>(type: "integer", nullable: true),
                    rope_metres = table.Column<decimal>(type: "numeric(7,1)", precision: 7, scale: 1, nullable: true),
                    had_incident = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    field_data = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    field_data_schema_version = table.Column<int>(type: "integer", nullable: true),
                    logistics = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    logistics_schema_version = table.Column<int>(type: "integer", nullable: true),
                    safety = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    safety_schema_version = table.Column<int>(type: "integer", nullable: true),
                    organizing_caving_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    max_participants = table.Column<int>(type: "integer", nullable: true),
                    expected_return_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    callout_alarm_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    callout_state = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    plan_reminder_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geom = table.Column<Geometry>(type: "geometry(Geometry, 4326)", nullable: true),
                    meeting_geom = table.Column<Geometry>(type: "geometry(Geometry, 4326)", nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caving_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_logs", x => x.id);
                    table.CheckConstraint("ck_trip_logs_dates", "trip_date_end IS NULL OR trip_date_end > trip_date");
                    table.ForeignKey(
                        name: "fk_trip_logs_caving_groups_caving_group_id",
                        column: x => x.caving_group_id,
                        principalTable: "caving_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_trip_logs_caving_groups_organizing_caving_group_id",
                        column: x => x.organizing_caving_group_id,
                        principalTable: "caving_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_trip_logs_trip_types_trip_type_id",
                        column: x => x.trip_type_id,
                        principalTable: "trip_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_trip_logs_users_deleted_by_user_id",
                        column: x => x.deleted_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_trip_logs_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trip_type_schemas",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    trip_type_id = table.Column<long>(type: "bigint", nullable: false),
                    section = table.Column<short>(type: "smallint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    schema = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_type_schemas", x => x.id);
                    table.CheckConstraint("ck_trip_type_schemas_version", "version >= 1");
                    table.ForeignKey(
                        name: "fk_trip_type_schemas_trip_types_trip_type_id",
                        column: x => x.trip_type_id,
                        principalTable: "trip_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cave_entrances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)2),
                    cave_feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entrance_type_id = table.Column<long>(type: "bigint", nullable: false),
                    is_main = table.Column<bool>(type: "boolean", nullable: false),
                    altitude = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    position_quality = table.Column<short>(type: "smallint", nullable: false),
                    surveyed_at = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cave_entrances", x => x.id);
                    table.CheckConstraint("ck_cave_entrances_kind", "kind = 2");
                    table.ForeignKey(
                        name: "fk_cave_entrances_caves_cave_feature_id",
                        column: x => x.cave_feature_id,
                        principalTable: "caves",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cave_entrances_entrance_types_entrance_type_id",
                        column: x => x.entrance_type_id,
                        principalTable: "entrance_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cave_entrances_features_id_kind",
                        columns: x => new { x.id, x.kind },
                        principalTable: "features",
                        principalColumns: new[] { "id", "kind" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "albums",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    cover_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_entity_type = table.Column<short>(type: "smallint", nullable: true),
                    subject_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_feature_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caving_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_albums", x => x.id);
                    table.CheckConstraint("ck_albums_subject", "(subject_feature_id is null and subject_entity_type is null and subject_entity_id is null) or (subject_feature_id is not null and subject_entity_type is null and subject_entity_id is null) or (subject_feature_id is null and subject_entity_type is not null and subject_entity_id is not null)");
                    table.ForeignKey(
                        name: "fk_albums_caving_groups_caving_group_id",
                        column: x => x.caving_group_id,
                        principalTable: "caving_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_albums_documents_cover_document_id",
                        column: x => x.cover_document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_albums_features_subject_feature_id",
                        column: x => x.subject_feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_albums_users_owner_user_id",
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

            migrationBuilder.CreateTable(
                name: "photo_details",
                columns: table => new
                {
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    photographer_caver_id = table.Column<Guid>(type: "uuid", nullable: true),
                    photographer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    caption = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    licence_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    place_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    in_public_gallery = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_photo_details", x => x.document_id);
                    table.ForeignKey(
                        name: "fk_photo_details_cavers_photographer_caver_id",
                        column: x => x.photographer_caver_id,
                        principalTable: "cavers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_photo_details_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "survey_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cave_feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_sources", x => x.id);
                    table.ForeignKey(
                        name: "fk_survey_sources_caves_cave_feature_id",
                        column: x => x.cave_feature_id,
                        principalTable: "caves",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_survey_sources_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expedition_trips",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    expedition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expedition_trips", x => x.id);
                    table.ForeignKey(
                        name: "fk_expedition_trips_expeditions_expedition_id",
                        column: x => x.expedition_id,
                        principalTable: "expeditions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_expedition_trips_trip_logs_trip_log_id",
                        column: x => x.trip_log_id,
                        principalTable: "trip_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trip_checklist_ticks",
                columns: table => new
                {
                    trip_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    checklist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ticked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_checklist_ticks", x => new { x.trip_log_id, x.item_id });
                    table.ForeignKey(
                        name: "fk_trip_checklist_ticks_checklist_items_checklist_id_item_id",
                        columns: x => new { x.checklist_id, x.item_id },
                        principalTable: "checklist_items",
                        principalColumns: new[] { "checklist_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trip_checklist_ticks_trip_logs_trip_log_id",
                        column: x => x.trip_log_id,
                        principalTable: "trip_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trip_checklist_ticks_users_ticked_by_user_id",
                        column: x => x.ticked_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "trip_invitations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    trip_log_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    caver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    response = table.Column<short>(type: "smallint", nullable: false),
                    invited_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    responded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    selected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_invitations", x => x.id);
                    table.CheckConstraint("ck_trip_invitations_one_subject", "(trip_log_id IS NOT NULL AND event_id IS NULL) OR (trip_log_id IS NULL AND event_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_trip_invitations_cavers_caver_id",
                        column: x => x.caver_id,
                        principalTable: "cavers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trip_invitations_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trip_invitations_trip_logs_trip_log_id",
                        column: x => x.trip_log_id,
                        principalTable: "trip_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trip_invitations_users_invited_by_user_id",
                        column: x => x.invited_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_trip_invitations_users_responded_by_user_id",
                        column: x => x.responded_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "trip_log_participants",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    trip_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<long>(type: "bigint", nullable: false),
                    caver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    exit_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_log_participants", x => x.id);
                    table.ForeignKey(
                        name: "fk_trip_log_participants_cavers_caver_id",
                        column: x => x.caver_id,
                        principalTable: "cavers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_trip_log_participants_trip_logs_trip_log_id",
                        column: x => x.trip_log_id,
                        principalTable: "trip_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trip_log_participants_trip_participant_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "trip_participant_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trip_party_numbers",
                columns: table => new
                {
                    trip_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    caver_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_party_numbers", x => new { x.trip_log_id, x.number });
                    table.CheckConstraint("ck_trip_party_numbers_number_from_one", "number >= 1");
                    table.ForeignKey(
                        name: "fk_trip_party_numbers_cavers_caver_id",
                        column: x => x.caver_id,
                        principalTable: "cavers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_trip_party_numbers_trip_logs_trip_log_id",
                        column: x => x.trip_log_id,
                        principalTable: "trip_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trip_teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_teams", x => x.id);
                    table.ForeignKey(
                        name: "fk_trip_teams_trip_logs_trip_log_id",
                        column: x => x.trip_log_id,
                        principalTable: "trip_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trip_tracking",
                columns: table => new
                {
                    trip_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    survey_model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cave_feature_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reference_station_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    depth_filter = table.Column<string[]>(type: "text[]", nullable: false),
                    armed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    first_armed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_tracking", x => x.trip_log_id);
                    table.ForeignKey(
                        name: "fk_trip_tracking_features_cave_feature_id",
                        column: x => x.cave_feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_trip_tracking_trip_logs_trip_log_id",
                        column: x => x.trip_log_id,
                        principalTable: "trip_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trip_tracking_participants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_tracking_participants", x => x.id);
                    table.ForeignKey(
                        name: "fk_trip_tracking_participants_cavers_caver_id",
                        column: x => x.caver_id,
                        principalTable: "cavers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trip_tracking_participants_trip_logs_trip_log_id",
                        column: x => x.trip_log_id,
                        principalTable: "trip_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trip_tracking_shares",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_tracking_shares", x => x.id);
                    table.ForeignKey(
                        name: "fk_trip_tracking_shares_trip_logs_trip_log_id",
                        column: x => x.trip_log_id,
                        principalTable: "trip_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trip_tracking_shares_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "album_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    album_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caption = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    added_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_album_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_album_items_albums_album_id",
                        column: x => x.album_id,
                        principalTable: "albums",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_album_items_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "album_shares",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    album_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    mode = table.Column<short>(type: "smallint", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_album_shares", x => x.id);
                    table.ForeignKey(
                        name: "fk_album_shares_albums_album_id",
                        column: x => x.album_id,
                        principalTable: "albums",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    original_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(127)", maxLength: 127, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    document_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    geom = table.Column<Point>(type: "geometry(Point, 4326)", nullable: true),
                    position_source = table.Column<short>(type: "smallint", nullable: false),
                    altitude_meters = table.Column<double>(type: "double precision", nullable: true),
                    direction_degrees = table.Column<double>(type: "double precision", nullable: true),
                    direction_is_magnetic = table.Column<bool>(type: "boolean", nullable: false),
                    position_dop = table.Column<double>(type: "double precision", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    page_count = table.Column<int>(type: "integer", nullable: true),
                    text_extraction = table.Column<short>(type: "smallint", nullable: false),
                    text_extraction_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    conversion = table.Column<short>(type: "smallint", nullable: false),
                    converted_from_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    author = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    producer = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    content_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    content_modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    duration_seconds = table.Column<double>(type: "double precision", nullable: true),
                    codec = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    orientation_quarter_turns = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_files", x => x.id);
                    table.CheckConstraint("ck_files_duration_seconds", "duration_seconds is null or duration_seconds >= 0");
                    table.CheckConstraint("ck_files_orientation", "orientation_quarter_turns between 0 and 3");
                    table.CheckConstraint("ck_files_page_count", "page_count is null or page_count >= 0");
                    table.ForeignKey(
                        name: "fk_files_document_versions_document_version_id",
                        column: x => x.document_version_id,
                        principalTable: "document_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_files_files_converted_from_file_id",
                        column: x => x.converted_from_file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "survey_compilations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cave_feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    survey_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    log_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    log_version_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    read_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    outcome = table.Column<short>(type: "smallint", nullable: true),
                    compiler_version = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    compiler_release_date = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    incomplete_stage = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    compilation_seconds = table.Column<int>(type: "integer", nullable: true),
                    error_count = table.Column<int>(type: "integer", nullable: true),
                    warning_count = table.Column<int>(type: "integer", nullable: true),
                    loop_count = table.Column<int>(type: "integer", nullable: true),
                    average_loop_error_percent = table.Column<double>(type: "double precision", nullable: true),
                    total_length_m = table.Column<double>(type: "double precision", nullable: true),
                    total_length_adjusted_m = table.Column<double>(type: "double precision", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_compilations", x => x.id);
                    table.ForeignKey(
                        name: "fk_survey_compilations_caves_cave_feature_id",
                        column: x => x.cave_feature_id,
                        principalTable: "caves",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_survey_compilations_survey_sources_survey_source_id",
                        column: x => x.survey_source_id,
                        principalTable: "survey_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trip_position_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caver_id = table.Column<Guid>(type: "uuid", nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    source = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    survey_model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cave_feature_id = table.Column<Guid>(type: "uuid", nullable: true),
                    viewer_station_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    viewer_to_station_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    depth_entered_m = table.Column<decimal>(type: "numeric(7,1)", precision: 7, scale: 1, nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    activity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    removed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_key = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_position_events", x => x.id);
                    table.CheckConstraint("ck_trip_position_events_cave_note", "(kind = 5) = (caver_id IS NULL) AND (kind <> 5 OR (note IS NOT NULL AND btrim(note) <> '' AND depth_entered_m IS NULL AND team_id IS NULL))");
                    table.CheckConstraint("ck_trip_position_events_stretch", "viewer_to_station_name IS NULL OR (kind = 1 AND viewer_station_name IS NOT NULL AND viewer_to_station_name <> viewer_station_name)");
                    table.ForeignKey(
                        name: "fk_trip_position_events_cavers_caver_id",
                        column: x => x.caver_id,
                        principalTable: "cavers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_trip_position_events_features_cave_feature_id",
                        column: x => x.cave_feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_trip_position_events_trip_logs_trip_log_id",
                        column: x => x.trip_log_id,
                        principalTable: "trip_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trip_position_events_trip_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "trip_teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_trip_position_events_users_recorded_by_user_id",
                        column: x => x.recorded_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_trip_position_events_users_removed_by_user_id",
                        column: x => x.removed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entity_type = table.Column<short>(type: "smallint", nullable: true),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    role = table.Column<short>(type: "smallint", nullable: false),
                    caption = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    added_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attachments", x => x.id);
                    table.CheckConstraint("ck_attachments_one_target", "(feature_id IS NOT NULL AND entity_type IS NULL AND entity_id IS NULL) OR (feature_id IS NULL AND entity_type IS NOT NULL AND entity_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_attachments_features_feature_id",
                        column: x => x.feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_attachments_stored_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_attachments_users_added_by",
                        column: x => x.added_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
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
                name: "geofiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    format = table.Column<short>(type: "smallint", nullable: false),
                    srid = table.Column<int>(type: "integer", nullable: true),
                    source_options = table.Column<string>(type: "jsonb", nullable: true),
                    import_status = table.Column<short>(type: "smallint", nullable: false),
                    import_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    feature_count = table.Column<int>(type: "integer", nullable: false),
                    bbox = table.Column<Polygon>(type: "geometry(Polygon, 4326)", nullable: true),
                    style = table.Column<string>(type: "jsonb", nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caving_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_geofiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_geofiles_caving_groups_caving_group_id",
                        column: x => x.caving_group_id,
                        principalTable: "caving_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_geofiles_stored_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_geofiles_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "georeferenced_maps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    map_kind = table.Column<short>(type: "smallint", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    processing_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    bbox = table.Column<Polygon>(type: "geometry(Polygon, 4326)", nullable: true),
                    min_zoom = table.Column<int>(type: "integer", nullable: true),
                    max_zoom = table.Column<int>(type: "integer", nullable: true),
                    attribution = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    default_opacity = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false),
                    cave_feature_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caving_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_georeferenced_maps", x => x.id);
                    table.ForeignKey(
                        name: "fk_georeferenced_maps_caves_cave_feature_id",
                        column: x => x.cave_feature_id,
                        principalTable: "caves",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_georeferenced_maps_caving_groups_caving_group_id",
                        column: x => x.caving_group_id,
                        principalTable: "caving_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_georeferenced_maps_stored_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_georeferenced_maps_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "res_link_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    res_link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entity_type = table.Column<short>(type: "smallint", nullable: true),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_main = table.Column<bool>(type: "boolean", nullable: false),
                    anchor_kind = table.Column<short>(type: "smallint", nullable: false),
                    anchor = table.Column<string>(type: "jsonb", nullable: true),
                    anchor_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    added_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_res_link_members", x => x.id);
                    table.CheckConstraint("ck_res_link_members_anchor_payload", "(anchor_kind = 0 AND anchor IS NULL) OR (anchor_kind <> 0 AND anchor IS NOT NULL)");
                    table.CheckConstraint("ck_res_link_members_one_target", "(feature_id IS NOT NULL AND entity_type IS NULL AND entity_id IS NULL) OR (feature_id IS NULL AND entity_type IS NOT NULL AND entity_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_res_link_members_features_feature_id",
                        column: x => x.feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_res_link_members_res_links_res_link_id",
                        column: x => x.res_link_id,
                        principalTable: "res_links",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_res_link_members_stored_files_anchor_file_id",
                        column: x => x.anchor_file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_res_link_members_users_added_by",
                        column: x => x.added_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "speleoloc_import_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stored_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    options = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    decisions = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_speleoloc_import_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_speleoloc_import_sessions_stored_files_stored_file_id",
                        column: x => x.stored_file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_speleoloc_import_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "survey_models",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cave_feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    format = table.Column<short>(type: "smallint", nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    surveyed_at = table.Column<DateOnly>(type: "date", nullable: true),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    processing_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    converted_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    anchor = table.Column<Point>(type: "geometry(Point, 4326)", nullable: true),
                    anchor_height_m = table.Column<double>(type: "double precision", nullable: true),
                    source_epsg = table.Column<int>(type: "integer", nullable: true),
                    applied_rotation_deg = table.Column<double>(type: "double precision", nullable: true),
                    triangle_count = table.Column<int>(type: "integer", nullable: true),
                    source_precision_lost = table.Column<bool>(type: "boolean", nullable: false),
                    dropped_shot_count = table.Column<int>(type: "integer", nullable: true),
                    merged_station_count = table.Column<int>(type: "integer", nullable: true),
                    anonymous_station_count = table.Column<int>(type: "integer", nullable: true),
                    root_survey_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_models", x => x.id);
                    table.ForeignKey(
                        name: "fk_survey_models_caves_cave_feature_id",
                        column: x => x.cave_feature_id,
                        principalTable: "caves",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_survey_models_files_converted_file_id",
                        column: x => x.converted_file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_survey_models_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trip_import_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stored_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    options = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    decisions = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_import_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_trip_import_sessions_files_stored_file_id",
                        column: x => x.stored_file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trip_import_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "survey_compilation_loops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    survey_compilation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    relative_error_percent = table.Column<double>(type: "double precision", nullable: false),
                    absolute_error_m = table.Column<double>(type: "double precision", nullable: false),
                    total_length_m = table.Column<double>(type: "double precision", nullable: false),
                    station_count = table.Column<int>(type: "integer", nullable: false),
                    error_xm = table.Column<double>(type: "double precision", nullable: false),
                    error_ym = table.Column<double>(type: "double precision", nullable: false),
                    error_zm = table.Column<double>(type: "double precision", nullable: false),
                    stations = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_compilation_loops", x => x.id);
                    table.ForeignKey(
                        name: "fk_survey_compilation_loops_survey_compilations_survey_compila",
                        column: x => x.survey_compilation_id,
                        principalTable: "survey_compilations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "geofile_features",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    geofile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    geom = table.Column<Geometry>(type: "geometry", nullable: false),
                    properties = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_geofile_features", x => x.id);
                    table.CheckConstraint("ck_geofile_features_geom_srid", "st_srid(geom) = 4326");
                    table.ForeignKey(
                        name: "fk_geofile_features_geofiles_geofile_id",
                        column: x => x.geofile_id,
                        principalTable: "geofiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "geofile_import_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    geofile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    options = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    decisions = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_geofile_import_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_geofile_import_sessions_geofiles_geofile_id",
                        column: x => x.geofile_id,
                        principalTable: "geofiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_geofile_import_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<short>(type: "smallint", nullable: false),
                    geofile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trip_log_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sync_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sync_result = table.Column<string>(type: "jsonb", nullable: true),
                    term_rule_set_id = table.Column<Guid>(type: "uuid", nullable: true),
                    term_rule_set_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<short>(type: "smallint", nullable: false),
                    options = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_count = table.Column<int>(type: "integer", nullable: false),
                    attached_count = table.Column<int>(type: "integer", nullable: false),
                    skipped_count = table.Column<int>(type: "integer", nullable: false),
                    failures = table.Column<string>(type: "jsonb", nullable: true),
                    reverted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reverted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_batches", x => x.id);
                    table.ForeignKey(
                        name: "fk_import_batches_geofiles_geofile_id",
                        column: x => x.geofile_id,
                        principalTable: "geofiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_import_batches_term_rule_sets_term_rule_set_id",
                        column: x => x.term_rule_set_id,
                        principalTable: "term_rule_sets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_import_batches_trip_logs_trip_log_id",
                        column: x => x.trip_log_id,
                        principalTable: "trip_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_import_batches_users_confirmed_by_user_id",
                        column: x => x.confirmed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "centerlines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)3),
                    cave_feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    survey_model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    skeleton = table.Column<MultiLineString>(type: "geometry(MultiLineString, 4326)", nullable: true),
                    path_count = table.Column<int>(type: "integer", nullable: false),
                    skeleton_path_count = table.Column<int>(type: "integer", nullable: true),
                    length_m = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    source = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_centerlines", x => x.id);
                    table.CheckConstraint("ck_centerlines_kind", "kind = 3");
                    table.ForeignKey(
                        name: "fk_centerlines_caves_cave_feature_id",
                        column: x => x.cave_feature_id,
                        principalTable: "caves",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_centerlines_features_id_kind",
                        columns: x => new { x.id, x.kind },
                        principalTable: "features",
                        principalColumns: new[] { "id", "kind" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_centerlines_survey_models_survey_model_id",
                        column: x => x.survey_model_id,
                        principalTable: "survey_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "survey_shots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    survey_model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_station_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    to_station_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    survey_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    geom = table.Column<LineString>(type: "geometry(LineStringZ, 4326)", nullable: false),
                    length_m = table.Column<double>(type: "double precision", nullable: false),
                    flags = table.Column<int>(type: "integer", nullable: false),
                    raw_flags = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_shots", x => x.id);
                    table.CheckConstraint("ck_survey_shots_two_points", "st_npoints(geom) = 2");
                    table.ForeignKey(
                        name: "fk_survey_shots_survey_models_survey_model_id",
                        column: x => x.survey_model_id,
                        principalTable: "survey_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "survey_stations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    survey_model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    survey_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    file_station_id = table.Column<long>(type: "bigint", nullable: true),
                    position = table.Column<Point>(type: "geometry(PointZ, 4326)", nullable: false),
                    flags = table.Column<int>(type: "integer", nullable: false),
                    raw_flags = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_stations", x => x.id);
                    table.ForeignKey(
                        name: "fk_survey_stations_survey_models_survey_model_id",
                        column: x => x.survey_model_id,
                        principalTable: "survey_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "survey_topology",
                columns: table => new
                {
                    survey_model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_count = table.Column<int>(type: "integer", nullable: false),
                    edge_count = table.Column<int>(type: "integer", nullable: false),
                    component_count = table.Column<int>(type: "integer", nullable: false),
                    reduced_node_count = table.Column<int>(type: "integer", nullable: false),
                    reduced_edge_count = table.Column<int>(type: "integer", nullable: false),
                    reduced_component_count = table.Column<int>(type: "integer", nullable: false),
                    cyclomatic_number = table.Column<int>(type: "integer", nullable: false),
                    extremity_count = table.Column<int>(type: "integer", nullable: false),
                    junction_count = table.Column<int>(type: "integer", nullable: false),
                    alpha = table.Column<double>(type: "double precision", nullable: true),
                    beta = table.Column<double>(type: "double precision", nullable: true),
                    gamma = table.Column<double>(type: "double precision", nullable: true),
                    mean_degree = table.Column<double>(type: "double precision", nullable: true),
                    degree_standard_deviation = table.Column<double>(type: "double precision", nullable: true),
                    degree_coefficient_of_variation = table.Column<double>(type: "double precision", nullable: true),
                    correlation_of_vertex_degree = table.Column<double>(type: "double precision", nullable: true),
                    branch_count = table.Column<int>(type: "integer", nullable: false),
                    looping_branch_count = table.Column<int>(type: "integer", nullable: false),
                    mean_branch_length_m = table.Column<double>(type: "double precision", nullable: true),
                    branch_length_coefficient_of_variation = table.Column<double>(type: "double precision", nullable: true),
                    min_branch_length_m = table.Column<double>(type: "double precision", nullable: true),
                    max_branch_length_m = table.Column<double>(type: "double precision", nullable: true),
                    length_entropy = table.Column<double>(type: "double precision", nullable: true),
                    orientation_entropy = table.Column<double>(type: "double precision", nullable: true),
                    mean_tortuosity = table.Column<double>(type: "double precision", nullable: true),
                    average_shortest_path_length = table.Column<double>(type: "double precision", nullable: true),
                    central_point_dominance = table.Column<double>(type: "double precision", nullable: true),
                    average_clustering_coefficient = table.Column<double>(type: "double precision", nullable: true),
                    computed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_topology", x => x.survey_model_id);
                    table.ForeignKey(
                        name: "fk_survey_topology_survey_models_survey_model_id",
                        column: x => x.survey_model_id,
                        principalTable: "survey_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_batch_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attached_to_feature_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trip_log_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trip_position_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_feature_id = table.Column<long>(type: "bigint", nullable: true),
                    source_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attachment_ids = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    rule_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    rule_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    action = table.Column<short>(type: "smallint", nullable: false),
                    source_properties = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_batch_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_import_batch_items_features_attached_to_feature_id",
                        column: x => x.attached_to_feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_import_batch_items_features_feature_id",
                        column: x => x.feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_import_batch_items_import_batches_import_batch_id",
                        column: x => x.import_batch_id,
                        principalTable: "import_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_import_batch_items_stored_files_source_file_id",
                        column: x => x.source_file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_import_batch_items_trip_logs_trip_log_id",
                        column: x => x.trip_log_id,
                        principalTable: "trip_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_import_batch_items_trip_position_events_trip_position_event",
                        column: x => x.trip_position_event_id,
                        principalTable: "trip_position_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "survey_lrud",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    survey_model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    shot_id = table.Column<long>(type: "bigint", nullable: true),
                    section = table.Column<short>(type: "smallint", nullable: true),
                    left_m = table.Column<double>(type: "double precision", nullable: true),
                    right_m = table.Column<double>(type: "double precision", nullable: true),
                    up_m = table.Column<double>(type: "double precision", nullable: true),
                    down_m = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_lrud", x => x.id);
                    table.CheckConstraint("ck_survey_lrud_measured", "left_m is not null or right_m is not null or up_m is not null or down_m is not null");
                    table.CheckConstraint("ck_survey_lrud_non_negative", "(left_m is null or left_m >= 0) and (right_m is null or right_m >= 0) and (up_m is null or up_m >= 0) and (down_m is null or down_m >= 0)");
                    table.ForeignKey(
                        name: "fk_survey_lrud_survey_models_survey_model_id",
                        column: x => x.survey_model_id,
                        principalTable: "survey_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_survey_lrud_survey_shots_shot_id",
                        column: x => x.shot_id,
                        principalTable: "survey_shots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_access_entries_feature_type_id",
                table: "access_entries",
                column: "feature_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_entries_granted_by",
                table: "access_entries",
                column: "granted_by");

            migrationBuilder.CreateIndex(
                name: "ix_access_entries_granted_via_expedition_id",
                table: "access_entries",
                column: "granted_via_expedition_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_entries_permission_group_id",
                table: "access_entries",
                column: "permission_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_entries_scope_feature_id",
                table: "access_entries",
                column: "scope_feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_entries_scope_id",
                table: "access_entries",
                column: "scope_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_entries_subject_kind_subject_id",
                table: "access_entries",
                columns: new[] { "subject_kind", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ix_account_data_exports_user_id_created_at",
                table: "account_data_exports",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_album_items_album_id_document_id",
                table: "album_items",
                columns: new[] { "album_id", "document_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_album_items_album_id_sort_order",
                table: "album_items",
                columns: new[] { "album_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_album_items_document_id",
                table: "album_items",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_album_shares_album_id",
                table: "album_shares",
                column: "album_id");

            migrationBuilder.CreateIndex(
                name: "ix_album_shares_token_hash",
                table: "album_shares",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_albums_caving_group_id",
                table: "albums",
                column: "caving_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_albums_cover_document_id",
                table: "albums",
                column: "cover_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_albums_owner_user_id",
                table: "albums",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_albums_subject_entity_type_subject_entity_id",
                table: "albums",
                columns: new[] { "subject_entity_type", "subject_entity_id" },
                filter: "subject_entity_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_albums_subject_feature_id",
                table: "albums",
                column: "subject_feature_id",
                filter: "subject_feature_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_added_by",
                table: "attachments",
                column: "added_by");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_file_id",
                table: "attachments",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ux_attachments_primary_entity",
                table: "attachments",
                columns: new[] { "entity_type", "entity_id" },
                unique: true,
                filter: "is_primary and entity_id is not null");

            migrationBuilder.CreateIndex(
                name: "ux_attachments_primary_feature",
                table: "attachments",
                column: "feature_id",
                unique: true,
                filter: "is_primary and feature_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_at",
                table: "audit_log",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_entity_type_entity_id",
                table: "audit_log",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_root_entity_type_root_entity_id_id",
                table: "audit_log",
                columns: new[] { "root_entity_type", "root_entity_id", "id" },
                descending: new[] { false, false, true },
                filter: "root_entity_type IS NOT NULL");

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
                name: "ix_cabinets_default_document_type_id",
                table: "cabinets",
                column: "default_document_type_id");

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
                name: "ix_calendar_feed_tokens_token_hash",
                table: "calendar_feed_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_calendar_feed_tokens_user_id",
                table: "calendar_feed_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_cave_depth_places_cave_depth",
                table: "cave_depth_places",
                columns: new[] { "cave_feature_id", "depth_m" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_cave_depth_places_cave_station_depthless",
                table: "cave_depth_places",
                columns: new[] { "cave_feature_id", "viewer_station_name" },
                unique: true,
                filter: "depth_m IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cave_entrances_cave_feature_id",
                table: "cave_entrances",
                column: "cave_feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_cave_entrances_entrance_type_id",
                table: "cave_entrances",
                column: "entrance_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_cave_entrances_id_kind",
                table: "cave_entrances",
                columns: new[] { "id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cave_level_bands_cave",
                table: "cave_level_bands",
                column: "cave_feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_cave_level_bands_confirmed_by",
                table: "cave_level_bands",
                column: "confirmed_by");

            migrationBuilder.CreateIndex(
                name: "ux_cave_level_bands_current",
                table: "cave_level_bands",
                column: "cave_feature_id",
                unique: true,
                filter: "superseded_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cave_qr_publications_feature",
                table: "cave_qr_publications",
                column: "feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_cave_qr_publications_published_by",
                table: "cave_qr_publications",
                column: "published_by");

            migrationBuilder.CreateIndex(
                name: "ux_cave_qr_publications_live",
                table: "cave_qr_publications",
                column: "feature_id",
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cave_types_code",
                table: "cave_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cavers_full_name",
                table: "cavers",
                column: "full_name");

            migrationBuilder.CreateIndex(
                name: "ix_cavers_user_id",
                table: "cavers",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_caves_cave_type_id",
                table: "caves",
                column: "cave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_caves_id_kind",
                table: "caves",
                columns: new[] { "id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_caves_region",
                table: "caves",
                column: "region");

            migrationBuilder.CreateIndex(
                name: "ix_caves_rock_type_id",
                table: "caves",
                column: "rock_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_caves_search_vector",
                table: "caves",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_caving_group_announcements_caving_group_id",
                table: "caving_group_announcements",
                column: "caving_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_caving_group_announcements_pending",
                table: "caving_group_announcements",
                column: "created_at",
                filter: "expanded_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_caving_group_memberships_caver_id_caving_group_id",
                table: "caving_group_memberships",
                columns: new[] { "caver_id", "caving_group_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_caving_group_memberships_caving_group_id",
                table: "caving_group_memberships",
                column: "caving_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_caving_groups_name",
                table: "caving_groups",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_caving_groups_slug",
                table: "caving_groups",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_centerlines_default_per_cave",
                table: "centerlines",
                column: "cave_feature_id",
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ix_centerlines_id_kind",
                table: "centerlines",
                columns: new[] { "id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_centerlines_survey_model_id",
                table: "centerlines",
                column: "survey_model_id");

            migrationBuilder.CreateIndex(
                name: "ix_checklist_items_checklist_id_sort_order_id",
                table: "checklist_items",
                columns: new[] { "checklist_id", "sort_order", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_checklists_caving_group_id",
                table: "checklists",
                column: "caving_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_checklists_owner_user_id",
                table: "checklists",
                column: "owner_user_id");

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
                name: "ix_documents_deleted_at",
                table: "documents",
                column: "deleted_at",
                filter: "deleted_at is not null");

            migrationBuilder.CreateIndex(
                name: "ix_documents_deleted_by_user_id",
                table: "documents",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_document_type_id",
                table: "documents",
                column: "document_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_owner_user_id",
                table: "documents",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_upload_batch_id",
                table: "documents",
                column: "upload_batch_id",
                filter: "upload_batch_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_duplicate_upload_records_created_at",
                table: "duplicate_upload_records",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_duplicate_upload_records_sha256",
                table: "duplicate_upload_records",
                column: "sha256");

            migrationBuilder.CreateIndex(
                name: "ix_entrance_types_code",
                table: "entrance_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_events_caving_group_id",
                table: "events",
                column: "caving_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_events_owner_user_id",
                table: "events",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_events_series_id_start_date",
                table: "events",
                columns: new[] { "series_id", "start_date" },
                filter: "series_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_events_start_date",
                table: "events",
                column: "start_date");

            migrationBuilder.CreateIndex(
                name: "ix_expedition_roster_caver_id",
                table: "expedition_roster",
                column: "caver_id");

            migrationBuilder.CreateIndex(
                name: "ix_expedition_roster_expedition_id",
                table: "expedition_roster",
                column: "expedition_id");

            migrationBuilder.CreateIndex(
                name: "ix_expedition_roster_role_id",
                table: "expedition_roster",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_expedition_roster_roles_code",
                table: "expedition_roster_roles",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expedition_trips_expedition_id",
                table: "expedition_trips",
                column: "expedition_id");

            migrationBuilder.CreateIndex(
                name: "ix_expedition_trips_trip_log_id",
                table: "expedition_trips",
                column: "trip_log_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expeditions_caving_group_id",
                table: "expeditions",
                column: "caving_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_expeditions_geom",
                table: "expeditions",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_expeditions_owner_user_id",
                table: "expeditions",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_expeditions_start_date",
                table: "expeditions",
                column: "start_date");

            migrationBuilder.CreateIndex(
                name: "ix_feature_ancestors_ancestor_id",
                table: "feature_ancestors",
                column: "ancestor_id");

            migrationBuilder.CreateIndex(
                name: "ix_feature_external_ids_feature_id_system",
                table: "feature_external_ids",
                columns: new[] { "feature_id", "system" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_feature_external_ids_system_value",
                table: "feature_external_ids",
                columns: new[] { "system", "value" });

            migrationBuilder.CreateIndex(
                name: "ix_feature_hierarchy_edges_parent_id_child_id",
                table: "feature_hierarchy_edges",
                columns: new[] { "parent_id", "child_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_feature_hierarchy_edges_primary",
                table: "feature_hierarchy_edges",
                column: "child_id",
                unique: true,
                filter: "is_primary");

            migrationBuilder.CreateIndex(
                name: "ix_feature_links_from_id_to_id_link_kind_id",
                table: "feature_links",
                columns: new[] { "from_id", "to_id", "link_kind_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_feature_links_link_kind_id",
                table: "feature_links",
                column: "link_kind_id");

            migrationBuilder.CreateIndex(
                name: "ix_feature_links_to_id",
                table: "feature_links",
                column: "to_id");

            migrationBuilder.CreateIndex(
                name: "ix_feature_set_members_feature_id",
                table: "feature_set_members",
                column: "feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_feature_sets_name",
                table: "feature_sets",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_feature_sets_slug",
                table: "feature_sets",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_feature_shares_created_by",
                table: "feature_shares",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_feature_shares_feature_id",
                table: "feature_shares",
                column: "feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_feature_shares_token_hash",
                table: "feature_shares",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_feature_types_code",
                table: "feature_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_features_ancestor_ids",
                table: "features",
                column: "ancestor_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_features_category",
                table: "features",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_features_caving_group_id",
                table: "features",
                column: "caving_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_features_deleted_at",
                table: "features",
                column: "deleted_at",
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_features_feature_type_id",
                table: "features",
                column: "feature_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_features_geom",
                table: "features",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_features_geom_entrances",
                table: "features",
                column: "geom",
                filter: "kind = 2")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_features_kind",
                table: "features",
                column: "kind");

            migrationBuilder.CreateIndex(
                name: "ix_features_name",
                table: "features",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_features_owner_user_id",
                table: "features",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_features_search_vector",
                table: "features",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

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
                name: "ix_files_geom",
                table: "files",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_files_kind_created_at",
                table: "files",
                columns: new[] { "kind", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_files_sha256",
                table: "files",
                column: "sha256");

            migrationBuilder.CreateIndex(
                name: "ix_files_text_extraction",
                table: "files",
                column: "text_extraction");

            migrationBuilder.CreateIndex(
                name: "ix_geofile_features_geofile_id",
                table: "geofile_features",
                column: "geofile_id");

            migrationBuilder.CreateIndex(
                name: "ix_geofile_features_geom",
                table: "geofile_features",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_geofile_import_sessions_geofile_id_user_id",
                table: "geofile_import_sessions",
                columns: new[] { "geofile_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_geofile_import_sessions_user_id",
                table: "geofile_import_sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_geofiles_caving_group_id",
                table: "geofiles",
                column: "caving_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_geofiles_file_id",
                table: "geofiles",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_geofiles_owner_user_id",
                table: "geofiles",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_georeferenced_maps_bbox",
                table: "georeferenced_maps",
                column: "bbox")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_georeferenced_maps_cave_feature_id",
                table: "georeferenced_maps",
                column: "cave_feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_georeferenced_maps_caving_group_id",
                table: "georeferenced_maps",
                column: "caving_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_georeferenced_maps_file_id",
                table: "georeferenced_maps",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_georeferenced_maps_owner_user_id",
                table: "georeferenced_maps",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_hierarchies_name",
                table: "hierarchies",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hierarchies_slug",
                table: "hierarchies",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hierarchy_memberships_feature_id",
                table: "hierarchy_memberships",
                column: "feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_hierarchy_memberships_hierarchy_id_feature_id",
                table: "hierarchy_memberships",
                columns: new[] { "hierarchy_id", "feature_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hierarchy_memberships_parent_feature_id",
                table: "hierarchy_memberships",
                column: "parent_feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_hierarchy_memberships_path",
                table: "hierarchy_memberships",
                column: "path")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_import_batch_items_attached_to_feature_id",
                table: "import_batch_items",
                column: "attached_to_feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_batch_items_feature_id",
                table: "import_batch_items",
                column: "feature_id",
                filter: "feature_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_import_batch_items_import_batch_id",
                table: "import_batch_items",
                column: "import_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_batch_items_source_file_id",
                table: "import_batch_items",
                column: "source_file_id",
                filter: "source_file_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_import_batch_items_trip_log_id",
                table: "import_batch_items",
                column: "trip_log_id",
                filter: "trip_log_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_import_batch_items_trip_position_event_id",
                table: "import_batch_items",
                column: "trip_position_event_id",
                filter: "trip_position_event_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_import_batches_confirmed_by_user_id",
                table: "import_batches",
                column: "confirmed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_batches_geofile_id",
                table: "import_batches",
                column: "geofile_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_batches_term_rule_set_id",
                table: "import_batches",
                column: "term_rule_set_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_batches_trip_log_id",
                table: "import_batches",
                column: "trip_log_id");

            migrationBuilder.CreateIndex(
                name: "ux_import_batches_user_sync_batch",
                table: "import_batches",
                columns: new[] { "confirmed_by_user_id", "sync_batch_id" },
                unique: true,
                filter: "sync_batch_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_link_kinds_code",
                table: "link_kinds",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_map_layers_name",
                table: "map_layers",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_map_views_caving_group_id",
                table: "map_views",
                column: "caving_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_map_views_owner_user_id",
                table: "map_views",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_map_views_share_token",
                table: "map_views",
                column: "share_token",
                unique: true,
                filter: "share_token IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_message_templates_key_locale",
                table: "message_templates",
                columns: new[] { "key", "locale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_channel_created_at",
                table: "notification_deliveries",
                columns: new[] { "channel", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_notification_id_channel",
                table: "notification_deliveries",
                columns: new[] { "notification_id", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_pending_created_at",
                table: "notification_deliveries",
                column: "created_at",
                filter: "status = 0");

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_recipient_user_id_status",
                table: "notification_deliveries",
                columns: new[] { "recipient_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_status_not_before_id",
                table: "notification_deliveries",
                columns: new[] { "status", "not_before", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_created_at",
                table: "notifications",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient_user_id_id",
                table: "notifications",
                columns: new[] { "recipient_user_id", "id" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_unread",
                table: "notifications",
                column: "recipient_user_id",
                filter: "read_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_unrouted",
                table: "notifications",
                column: "id",
                filter: "routed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_open_iddict_applications_client_id",
                table: "OpenIddictApplications",
                column: "client_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_open_iddict_authorizations_application_id_status_subject_type",
                table: "OpenIddictAuthorizations",
                columns: new[] { "application_id", "status", "subject", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_open_iddict_scopes_name",
                table: "OpenIddictScopes",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_open_iddict_tokens_application_id_status_subject_type",
                table: "OpenIddictTokens",
                columns: new[] { "application_id", "status", "subject", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_open_iddict_tokens_authorization_id",
                table: "OpenIddictTokens",
                column: "authorization_id");

            migrationBuilder.CreateIndex(
                name: "ix_open_iddict_tokens_reference_id",
                table: "OpenIddictTokens",
                column: "reference_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_permission_group_members_member_kind_member_id",
                table: "permission_group_members",
                columns: new[] { "member_kind", "member_id" });

            migrationBuilder.CreateIndex(
                name: "ix_permission_group_members_permission_group_id_member_kind_me",
                table: "permission_group_members",
                columns: new[] { "permission_group_id", "member_kind", "member_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_permission_groups_name",
                table: "permission_groups",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_permission_groups_slug",
                table: "permission_groups",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_photo_details_in_public_gallery",
                table: "photo_details",
                column: "in_public_gallery",
                filter: "in_public_gallery");

            migrationBuilder.CreateIndex(
                name: "ix_photo_details_photographer_caver_id",
                table: "photo_details",
                column: "photographer_caver_id",
                filter: "photographer_caver_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_photo_import_sessions_user_id",
                table: "photo_import_sessions",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_processing_jobs_kind_completed_at",
                table: "processing_jobs",
                columns: new[] { "kind", "completed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_processing_jobs_status_id",
                table: "processing_jobs",
                columns: new[] { "status", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_report_templates_default",
                table: "report_templates",
                columns: new[] { "kind", "is_default" },
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ux_report_templates_trip_type",
                table: "report_templates",
                column: "trip_type_id",
                unique: true,
                filter: "trip_type_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_res_link_members_added_by",
                table: "res_link_members",
                column: "added_by");

            migrationBuilder.CreateIndex(
                name: "ix_res_link_members_anchor_file_id",
                table: "res_link_members",
                column: "anchor_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_res_link_members_entity_link",
                table: "res_link_members",
                columns: new[] { "entity_type", "entity_id", "res_link_id" },
                filter: "entity_type IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_res_link_members_entity_type_entity_id",
                table: "res_link_members",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_res_link_members_feature_id",
                table: "res_link_members",
                column: "feature_id",
                filter: "feature_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_res_link_members_feature_link",
                table: "res_link_members",
                columns: new[] { "feature_id", "res_link_id" },
                filter: "feature_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_res_link_members_main",
                table: "res_link_members",
                column: "res_link_id",
                unique: true,
                filter: "is_main");

            migrationBuilder.CreateIndex(
                name: "ix_res_link_members_res_link_id",
                table: "res_link_members",
                column: "res_link_id");

            migrationBuilder.CreateIndex(
                name: "ix_res_link_members_whole_entity",
                table: "res_link_members",
                columns: new[] { "res_link_id", "entity_type", "entity_id" },
                unique: true,
                filter: "entity_type IS NOT NULL AND anchor_kind = 0");

            migrationBuilder.CreateIndex(
                name: "ix_res_link_members_whole_feature",
                table: "res_link_members",
                columns: new[] { "res_link_id", "feature_id" },
                unique: true,
                filter: "feature_id IS NOT NULL AND anchor_kind = 0");

            migrationBuilder.CreateIndex(
                name: "ix_res_link_relation_types_code",
                table: "res_link_relation_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_res_links_created_by",
                table: "res_links",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_res_links_relation_type_id",
                table: "res_links",
                column: "relation_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_res_links_short_code",
                table: "res_links",
                column: "short_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rock_types_code",
                table: "rock_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_role_claims_role_id",
                table: "role_claims",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "roles",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_saved_filters_caving_group_id",
                table: "saved_filters",
                column: "caving_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_saved_filters_owner_user_id",
                table: "saved_filters",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_speleoloc_import_sessions_stored_file_id_user_id",
                table: "speleoloc_import_sessions",
                columns: new[] { "stored_file_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_speleoloc_import_sessions_user_id",
                table: "speleoloc_import_sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_compilation_loops_survey_compilation_id_ordinal",
                table: "survey_compilation_loops",
                columns: new[] { "survey_compilation_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_survey_compilations_cave_feature_id",
                table: "survey_compilations",
                column: "cave_feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_compilations_survey_source_id",
                table: "survey_compilations",
                column: "survey_source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_survey_lrud_shot_id",
                table: "survey_lrud",
                column: "shot_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_lrud_survey_model_id_station_name",
                table: "survey_lrud",
                columns: new[] { "survey_model_id", "station_name" });

            migrationBuilder.CreateIndex(
                name: "ix_survey_models_cave_feature_id",
                table: "survey_models",
                column: "cave_feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_models_converted_file_id",
                table: "survey_models",
                column: "converted_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_models_current_line_plot",
                table: "survey_models",
                column: "cave_feature_id",
                unique: true,
                filter: "is_current AND format <> 2");

            migrationBuilder.CreateIndex(
                name: "ix_survey_models_current_wall_mesh",
                table: "survey_models",
                column: "cave_feature_id",
                unique: true,
                filter: "is_current AND format = 2");

            migrationBuilder.CreateIndex(
                name: "ix_survey_models_file_id",
                table: "survey_models",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_shots_geom",
                table: "survey_shots",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_survey_shots_survey_model_id",
                table: "survey_shots",
                column: "survey_model_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_sources_cave_feature_id",
                table: "survey_sources",
                column: "cave_feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_sources_document_id",
                table: "survey_sources",
                column: "document_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_survey_stations_position",
                table: "survey_stations",
                column: "position")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_survey_stations_survey_model_id_name",
                table: "survey_stations",
                columns: new[] { "survey_model_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_survey_topology_cyclomatic_number",
                table: "survey_topology",
                column: "cyclomatic_number");

            migrationBuilder.CreateIndex(
                name: "ix_sync_set_members_root_feature_id",
                table: "sync_set_members",
                column: "root_feature_id");

            migrationBuilder.CreateIndex(
                name: "ux_sync_set_members_set_root",
                table: "sync_set_members",
                columns: new[] { "sync_set_id", "root_feature_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sync_sets_caving_group_id",
                table: "sync_sets",
                column: "caving_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_sync_sets_owner_user_id",
                table: "sync_sets",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_taggings_added_by",
                table: "taggings",
                column: "added_by");

            migrationBuilder.CreateIndex(
                name: "ix_taggings_entity_type_entity_id",
                table: "taggings",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_taggings_feature_id",
                table: "taggings",
                column: "feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_taggings_tag_id_entity_type_entity_id",
                table: "taggings",
                columns: new[] { "tag_id", "entity_type", "entity_id" },
                unique: true,
                filter: "entity_type IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_taggings_tag_id_feature_id",
                table: "taggings",
                columns: new[] { "tag_id", "feature_id" },
                unique: true,
                filter: "feature_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tags_name",
                table: "tags",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tags_slug",
                table: "tags",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_term_rule_sets_is_seeded",
                table: "term_rule_sets",
                column: "is_seeded",
                filter: "is_seeded");

            migrationBuilder.CreateIndex(
                name: "ux_term_rule_sets_group_default",
                table: "term_rule_sets",
                column: "caving_group_id",
                unique: true,
                filter: "is_default and scope = 1");

            migrationBuilder.CreateIndex(
                name: "ux_term_rule_sets_installation_default",
                table: "term_rule_sets",
                column: "scope",
                unique: true,
                filter: "is_default and scope = 0");

            migrationBuilder.CreateIndex(
                name: "ux_term_rule_sets_user_default",
                table: "term_rule_sets",
                column: "owner_user_id",
                unique: true,
                filter: "is_default and scope = 2");

            migrationBuilder.CreateIndex(
                name: "ix_terrain_build_rasters_footprint",
                table: "terrain_build_rasters",
                column: "footprint")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_terrain_build_rasters_terrain_build_id_id",
                table: "terrain_build_rasters",
                columns: new[] { "terrain_build_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_terrain_build_rasters_terrain_build_id_path",
                table: "terrain_build_rasters",
                columns: new[] { "terrain_build_id", "path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_terrain_build_sources_terrain_build_id_id",
                table: "terrain_build_sources",
                columns: new[] { "terrain_build_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_terrain_builds_base_build_id",
                table: "terrain_builds",
                column: "base_build_id");

            migrationBuilder.CreateIndex(
                name: "ix_terrain_builds_created_at",
                table: "terrain_builds",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_terrain_builds_extent",
                table: "terrain_builds",
                column: "extent")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_terrain_builds_status",
                table: "terrain_builds",
                column: "status",
                filter: "status in (0, 1)");

            migrationBuilder.CreateIndex(
                name: "ux_terrain_builds_active",
                table: "terrain_builds",
                column: "is_active",
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_terrain_derivative_layers_processing_job_id",
                table: "terrain_derivative_layers",
                column: "processing_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_terrain_derivative_layers_status",
                table: "terrain_derivative_layers",
                column: "status",
                filter: "status in (0, 1)");

            migrationBuilder.CreateIndex(
                name: "ux_terrain_derivative_layers_request",
                table: "terrain_derivative_layers",
                columns: new[] { "terrain_build_id", "derivative", "settings_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_terrain_derivative_rasters_footprint",
                table: "terrain_derivative_rasters",
                column: "footprint")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_terrain_derivative_rasters_terrain_derivative_layer_id_id",
                table: "terrain_derivative_rasters",
                columns: new[] { "terrain_derivative_layer_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_terrain_derivative_rasters_terrain_derivative_layer_id_path",
                table: "terrain_derivative_rasters",
                columns: new[] { "terrain_derivative_layer_id", "path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trip_checklist_ticks_checklist_id_item_id",
                table: "trip_checklist_ticks",
                columns: new[] { "checklist_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_trip_checklist_ticks_ticked_by_user_id",
                table: "trip_checklist_ticks",
                column: "ticked_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_checklist_ticks_trip_log_id_checklist_id",
                table: "trip_checklist_ticks",
                columns: new[] { "trip_log_id", "checklist_id" });

            migrationBuilder.CreateIndex(
                name: "ix_trip_import_sessions_stored_file_id_user_id",
                table: "trip_import_sessions",
                columns: new[] { "stored_file_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trip_import_sessions_user_id",
                table: "trip_import_sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_invitations_caver_id",
                table: "trip_invitations",
                column: "caver_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_invitations_event_id",
                table: "trip_invitations",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_invitations_event_id_caver_id",
                table: "trip_invitations",
                columns: new[] { "event_id", "caver_id" },
                unique: true,
                filter: "event_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_trip_invitations_invited_by_user_id",
                table: "trip_invitations",
                column: "invited_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_invitations_responded_by_user_id",
                table: "trip_invitations",
                column: "responded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_invitations_trip_log_id",
                table: "trip_invitations",
                column: "trip_log_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_invitations_trip_log_id_caver_id",
                table: "trip_invitations",
                columns: new[] { "trip_log_id", "caver_id" },
                unique: true,
                filter: "trip_log_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_trip_log_participants_caver_id",
                table: "trip_log_participants",
                column: "caver_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_log_participants_role_id",
                table: "trip_log_participants",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_log_participants_trip_log_id",
                table: "trip_log_participants",
                column: "trip_log_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_log_participants_trip_log_id_role_id_caver_id",
                table: "trip_log_participants",
                columns: new[] { "trip_log_id", "role_id", "caver_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trip_logs_callout_state_callout_alarm_at",
                table: "trip_logs",
                columns: new[] { "callout_state", "callout_alarm_at" });

            migrationBuilder.CreateIndex(
                name: "ix_trip_logs_caving_group_id",
                table: "trip_logs",
                column: "caving_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_logs_deleted_at",
                table: "trip_logs",
                column: "deleted_at",
                filter: "deleted_at is not null");

            migrationBuilder.CreateIndex(
                name: "ix_trip_logs_deleted_by_user_id",
                table: "trip_logs",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_logs_geom",
                table: "trip_logs",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_trip_logs_meeting_geom",
                table: "trip_logs",
                column: "meeting_geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_trip_logs_organizing_caving_group_id",
                table: "trip_logs",
                column: "organizing_caving_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_logs_owner_user_id",
                table: "trip_logs",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_logs_state",
                table: "trip_logs",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_trip_logs_trip_date",
                table: "trip_logs",
                column: "trip_date");

            migrationBuilder.CreateIndex(
                name: "ix_trip_logs_trip_type_id",
                table: "trip_logs",
                column: "trip_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_participant_roles_code",
                table: "trip_participant_roles",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trip_party_numbers_caver_id",
                table: "trip_party_numbers",
                column: "caver_id");

            migrationBuilder.CreateIndex(
                name: "ux_trip_party_numbers_trip_caver",
                table: "trip_party_numbers",
                columns: new[] { "trip_log_id", "caver_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trip_position_events_cave_feature_id",
                table: "trip_position_events",
                column: "cave_feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_position_events_caver_id",
                table: "trip_position_events",
                column: "caver_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_position_events_recorded_by_user_id",
                table: "trip_position_events",
                column: "recorded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_position_events_removed_by_user_id",
                table: "trip_position_events",
                column: "removed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_position_events_survey_model_id",
                table: "trip_position_events",
                column: "survey_model_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_position_events_team_id",
                table: "trip_position_events",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_position_events_trip_log_id_caver_id_recorded_at",
                table: "trip_position_events",
                columns: new[] { "trip_log_id", "caver_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_trip_position_events_trip_log_id_recorded_at",
                table: "trip_position_events",
                columns: new[] { "trip_log_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ux_trip_position_events_client_key",
                table: "trip_position_events",
                columns: new[] { "trip_log_id", "client_key", "caver_id" },
                unique: true,
                filter: "client_key IS NOT NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_trip_teams_trip_log_id",
                table: "trip_teams",
                column: "trip_log_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_tracking_cave_feature_id",
                table: "trip_tracking",
                column: "cave_feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_tracking_survey_model_id",
                table: "trip_tracking",
                column: "survey_model_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_tracking_participants_caver_id",
                table: "trip_tracking_participants",
                column: "caver_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_tracking_participants_trip_log_id_caver_id",
                table: "trip_tracking_participants",
                columns: new[] { "trip_log_id", "caver_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trip_tracking_shares_created_by",
                table: "trip_tracking_shares",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_trip_tracking_shares_token_hash",
                table: "trip_tracking_shares",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trip_tracking_shares_trip_log_id",
                table: "trip_tracking_shares",
                column: "trip_log_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_type_schemas_trip_type_id_section_version",
                table: "trip_type_schemas",
                columns: new[] { "trip_type_id", "section", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trip_types_code",
                table: "trip_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trip_types_default_checklist_id",
                table: "trip_types",
                column: "default_checklist_id");

            migrationBuilder.CreateIndex(
                name: "ix_upload_batch_items_document_id",
                table: "upload_batch_items",
                column: "document_id",
                filter: "document_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_upload_batch_items_upload_batch_id_id",
                table: "upload_batch_items",
                columns: new[] { "upload_batch_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_upload_batches_cabinet_id",
                table: "upload_batches",
                column: "cabinet_id");

            migrationBuilder.CreateIndex(
                name: "ix_upload_batches_started_by_user_id_created_at",
                table: "upload_batches",
                columns: new[] { "started_by_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_upload_batches_status",
                table: "upload_batches",
                column: "status",
                filter: "status in (0, 1)");

            migrationBuilder.CreateIndex(
                name: "ix_upload_batches_tag_id",
                table: "upload_batches",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_upload_sessions_cabinet_id",
                table: "upload_sessions",
                column: "cabinet_id");

            migrationBuilder.CreateIndex(
                name: "ix_upload_sessions_expires_at",
                table: "upload_sessions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_upload_sessions_upload_batch_id",
                table: "upload_sessions",
                column: "upload_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_upload_sessions_user_id",
                table: "upload_sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_addresses_geom",
                table: "user_addresses",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_user_addresses_user_id",
                table: "user_addresses",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_claims_user_id",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_user_id",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_notification_preferences_user_id_category_channel",
                table: "user_notification_preferences",
                columns: new[] { "user_id", "category", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "ix_users_avatar_file_id",
                table: "users",
                column: "avatar_file_id",
                filter: "avatar_file_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_users_caving_club_id",
                table: "users",
                column: "caving_club_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_phone_number",
                table: "users",
                column: "phone_number",
                unique: true,
                filter: "phone_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "users",
                column: "normalized_user_name",
                unique: true);

            // Also beyond the model: a page's search vector, which a trigger keeps and nothing reads
            // through the model, and the expression indexes the device and catalogue lookups use;
            // then the two triggers that keep a page's search vector and its document's language in
            // step.
            migrationBuilder.Sql(""""
                ALTER TABLE public.document_pages ADD COLUMN search_vector tsvector;
                CREATE INDEX ix_document_pages_search_vector ON public.document_pages USING gin (search_vector);
                CREATE INDEX ix_features_speleoloc_pci_lower ON public.features USING btree (lower((properties ->> 'speleolocPci'::text))) WHERE (lower((properties ->> 'speleolocPci'::text)) IS NOT NULL);
                CREATE INDEX ix_features_speleoloc_qcri_lower ON public.features USING btree (lower((properties ->> 'speleolocQcri'::text))) WHERE (lower((properties ->> 'speleolocQcri'::text)) IS NOT NULL);
                CREATE INDEX ix_features_speologie_id ON public.features USING btree (((properties ->> 'speologieId'::text))) WHERE ((properties ->> 'speologieId'::text) IS NOT NULL);
                -- The two languages document search knows; a third is a row here plus a configuration above.
                INSERT INTO text_search_languages (code, configuration) VALUES ('ro', 'romanian_unaccent'), ('en', 'english_unaccent') ON CONFLICT (code) DO NOTHING;
                -- The column defaults the earlier migrations left on NOT NULL columns they added. The model
                -- declares none, but a raw insert that leaves such a column out relies on them.
                ALTER TABLE public.attachments ALTER COLUMN is_primary SET DEFAULT false;
                ALTER TABLE public.cave_depth_places ALTER COLUMN is_landmark SET DEFAULT false;
                ALTER TABLE public.caving_group_announcements ALTER COLUMN segments_per_copy SET DEFAULT 0;
                ALTER TABLE public.files ALTER COLUMN conversion SET DEFAULT 0;
                ALTER TABLE public.files ALTER COLUMN direction_is_magnetic SET DEFAULT false;
                ALTER TABLE public.files ALTER COLUMN orientation_quarter_turns SET DEFAULT 0;
                ALTER TABLE public.files ALTER COLUMN position_source SET DEFAULT 0;
                ALTER TABLE public.files ALTER COLUMN text_extraction SET DEFAULT 0;
                ALTER TABLE public.import_batches ALTER COLUMN source SET DEFAULT 0;
                ALTER TABLE public.map_layers ALTER COLUMN in_documents SET DEFAULT false;
                ALTER TABLE public.map_layers ALTER COLUMN max_zoom SET DEFAULT 19;
                ALTER TABLE public.map_layers ALTER COLUMN min_zoom SET DEFAULT 0;
                ALTER TABLE public.map_layers ALTER COLUMN on_shared_links SET DEFAULT false;
                ALTER TABLE public.notification_deliveries ALTER COLUMN segments SET DEFAULT 0;
                ALTER TABLE public.notifications ALTER COLUMN segments_per_copy SET DEFAULT 0;
                ALTER TABLE public.report_templates ALTER COLUMN kind SET DEFAULT 0;
                ALTER TABLE public.survey_models ALTER COLUMN is_current SET DEFAULT false;
                ALTER TABLE public.survey_models ALTER COLUMN source_precision_lost SET DEFAULT false;
                ALTER TABLE public.survey_models ALTER COLUMN status SET DEFAULT 0;
                ALTER TABLE public.terrain_builds ALTER COLUMN activation_was_automatic SET DEFAULT false;
                ALTER TABLE public.terrain_derivative_layers ALTER COLUMN method_revision SET DEFAULT 0;
                ALTER TABLE public.trip_logs ALTER COLUMN state SET DEFAULT 0;
                ALTER TABLE public.trip_tracking_shares ALTER COLUMN expires_at SET DEFAULT '-infinity'::timestamp with time zone;
                ALTER TABLE public.user_notification_preferences ALTER COLUMN channel SET DEFAULT 0;
                ALTER TABLE public.user_notification_preferences ALTER COLUMN choice SET DEFAULT 0;
                CREATE TRIGGER document_pages_search_vector BEFORE INSERT OR UPDATE OF text, file_id ON public.document_pages FOR EACH ROW EXECUTE FUNCTION public.document_pages_search_vector_trigger();
                CREATE TRIGGER documents_language_reindex AFTER UPDATE OF language ON public.documents FOR EACH ROW WHEN (((old.language)::text IS DISTINCT FROM (new.language)::text)) EXECUTE FUNCTION public.documents_language_reindex_trigger();
                """");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_entries");

            migrationBuilder.DropTable(
                name: "account_data_exports");

            migrationBuilder.DropTable(
                name: "album_items");

            migrationBuilder.DropTable(
                name: "album_shares");

            migrationBuilder.DropTable(
                name: "app_settings");

            migrationBuilder.DropTable(
                name: "attachments");

            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "cabinet_documents");

            migrationBuilder.DropTable(
                name: "calendar_feed_tokens");

            migrationBuilder.DropTable(
                name: "cave_depth_places");

            migrationBuilder.DropTable(
                name: "cave_entrances");

            migrationBuilder.DropTable(
                name: "cave_level_bands");

            migrationBuilder.DropTable(
                name: "cave_qr_publications");

            migrationBuilder.DropTable(
                name: "caving_group_announcements");

            migrationBuilder.DropTable(
                name: "caving_group_memberships");

            migrationBuilder.DropTable(
                name: "centerlines");

            migrationBuilder.DropTable(
                name: "document_comments");

            migrationBuilder.DropTable(
                name: "document_pages");

            migrationBuilder.DropTable(
                name: "document_type_schemas");

            migrationBuilder.DropTable(
                name: "duplicate_upload_records");

            migrationBuilder.DropTable(
                name: "expedition_roster");

            migrationBuilder.DropTable(
                name: "expedition_trips");

            migrationBuilder.DropTable(
                name: "feature_ancestors");

            migrationBuilder.DropTable(
                name: "feature_external_ids");

            migrationBuilder.DropTable(
                name: "feature_hierarchy_edges");

            migrationBuilder.DropTable(
                name: "feature_links");

            migrationBuilder.DropTable(
                name: "feature_set_members");

            migrationBuilder.DropTable(
                name: "feature_shares");

            migrationBuilder.DropTable(
                name: "file_access_log");

            migrationBuilder.DropTable(
                name: "geofile_features");

            migrationBuilder.DropTable(
                name: "geofile_import_sessions");

            migrationBuilder.DropTable(
                name: "georeferenced_maps");

            migrationBuilder.DropTable(
                name: "hierarchy_memberships");

            migrationBuilder.DropTable(
                name: "import_batch_items");

            migrationBuilder.DropTable(
                name: "map_layers");

            migrationBuilder.DropTable(
                name: "map_views");

            migrationBuilder.DropTable(
                name: "message_templates");

            migrationBuilder.DropTable(
                name: "notification_deliveries");

            migrationBuilder.DropTable(
                name: "OpenIddictScopes");

            migrationBuilder.DropTable(
                name: "OpenIddictTokens");

            migrationBuilder.DropTable(
                name: "permission_group_members");

            migrationBuilder.DropTable(
                name: "photo_details");

            migrationBuilder.DropTable(
                name: "photo_import_sessions");

            migrationBuilder.DropTable(
                name: "report_templates");

            migrationBuilder.DropTable(
                name: "res_link_members");

            migrationBuilder.DropTable(
                name: "role_claims");

            migrationBuilder.DropTable(
                name: "saved_filters");

            migrationBuilder.DropTable(
                name: "speleoloc_import_sessions");

            migrationBuilder.DropTable(
                name: "survey_compilation_loops");

            migrationBuilder.DropTable(
                name: "survey_lrud");

            migrationBuilder.DropTable(
                name: "survey_stations");

            migrationBuilder.DropTable(
                name: "survey_topology");

            migrationBuilder.DropTable(
                name: "sync_set_members");

            migrationBuilder.DropTable(
                name: "taggings");

            migrationBuilder.DropTable(
                name: "terrain_build_rasters");

            migrationBuilder.DropTable(
                name: "terrain_build_sources");

            migrationBuilder.DropTable(
                name: "terrain_derivative_rasters");

            migrationBuilder.DropTable(
                name: "text_search_languages");

            migrationBuilder.DropTable(
                name: "trip_checklist_ticks");

            migrationBuilder.DropTable(
                name: "trip_import_sessions");

            migrationBuilder.DropTable(
                name: "trip_invitations");

            migrationBuilder.DropTable(
                name: "trip_log_participants");

            migrationBuilder.DropTable(
                name: "trip_party_numbers");

            migrationBuilder.DropTable(
                name: "trip_tracking");

            migrationBuilder.DropTable(
                name: "trip_tracking_participants");

            migrationBuilder.DropTable(
                name: "trip_tracking_shares");

            migrationBuilder.DropTable(
                name: "trip_type_schemas");

            migrationBuilder.DropTable(
                name: "upload_batch_items");

            migrationBuilder.DropTable(
                name: "upload_sessions");

            migrationBuilder.DropTable(
                name: "user_addresses");

            migrationBuilder.DropTable(
                name: "user_claims");

            migrationBuilder.DropTable(
                name: "user_logins");

            migrationBuilder.DropTable(
                name: "user_notification_preferences");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "user_tokens");

            migrationBuilder.DropTable(
                name: "albums");

            migrationBuilder.DropTable(
                name: "entrance_types");

            migrationBuilder.DropTable(
                name: "expedition_roster_roles");

            migrationBuilder.DropTable(
                name: "expeditions");

            migrationBuilder.DropTable(
                name: "link_kinds");

            migrationBuilder.DropTable(
                name: "feature_sets");

            migrationBuilder.DropTable(
                name: "hierarchies");

            migrationBuilder.DropTable(
                name: "import_batches");

            migrationBuilder.DropTable(
                name: "trip_position_events");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "OpenIddictAuthorizations");

            migrationBuilder.DropTable(
                name: "permission_groups");

            migrationBuilder.DropTable(
                name: "res_links");

            migrationBuilder.DropTable(
                name: "survey_compilations");

            migrationBuilder.DropTable(
                name: "survey_shots");

            migrationBuilder.DropTable(
                name: "sync_sets");

            migrationBuilder.DropTable(
                name: "terrain_derivative_layers");

            migrationBuilder.DropTable(
                name: "checklist_items");

            migrationBuilder.DropTable(
                name: "events");

            migrationBuilder.DropTable(
                name: "trip_participant_roles");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "geofiles");

            migrationBuilder.DropTable(
                name: "term_rule_sets");

            migrationBuilder.DropTable(
                name: "cavers");

            migrationBuilder.DropTable(
                name: "trip_teams");

            migrationBuilder.DropTable(
                name: "OpenIddictApplications");

            migrationBuilder.DropTable(
                name: "res_link_relation_types");

            migrationBuilder.DropTable(
                name: "survey_sources");

            migrationBuilder.DropTable(
                name: "survey_models");

            migrationBuilder.DropTable(
                name: "processing_jobs");

            migrationBuilder.DropTable(
                name: "terrain_builds");

            migrationBuilder.DropTable(
                name: "trip_logs");

            migrationBuilder.DropTable(
                name: "caves");

            migrationBuilder.DropTable(
                name: "files");

            migrationBuilder.DropTable(
                name: "trip_types");

            migrationBuilder.DropTable(
                name: "cave_types");

            migrationBuilder.DropTable(
                name: "features");

            migrationBuilder.DropTable(
                name: "rock_types");

            migrationBuilder.DropTable(
                name: "document_versions");

            migrationBuilder.DropTable(
                name: "checklists");

            migrationBuilder.DropTable(
                name: "feature_types");

            migrationBuilder.DropTable(
                name: "documents");

            migrationBuilder.DropTable(
                name: "upload_batches");

            migrationBuilder.DropTable(
                name: "cabinets");

            migrationBuilder.DropTable(
                name: "tags");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "document_types");

            migrationBuilder.DropTable(
                name: "caving_groups");
        }
    }
}
