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
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:ltree", ",,")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,");

            // Generated tsvector columns require IMMUTABLE expressions; the two-argument
            // unaccent form qualifies. Must exist before the tables whose computed
            // search_vector columns call it.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION immutable_unaccent(text)
                RETURNS text
                LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
                RETURN public.unaccent('public.unaccent'::regdictionary, $1);
                """);

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
                    is_base = table.Column<bool>(type: "boolean", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
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
                    channel = table.Column<short>(type: "smallint", nullable: false),
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
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processing_jobs", x => x.id);
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
                name: "teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    website = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    logo_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teams", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    bio = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    caving_club = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    avatar_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    avatar_preset = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    pending_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    pending_email_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pending_phone_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    pending_phone_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    notify_email_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    notify_digest = table.Column<short>(type: "smallint", nullable: false),
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
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                        name: "fk_features_feature_types_feature_type_id",
                        column: x => x.feature_type_id,
                        principalTable: "feature_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_features_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_features_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
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
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    version_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    document_date = table.Column<DateOnly>(type: "date", nullable: true),
                    geom = table.Column<Point>(type: "geometry(Point, 4326)", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_files", x => x.id);
                    table.ForeignKey(
                        name: "fk_files_users_uploaded_by",
                        column: x => x.uploaded_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
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
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_map_views", x => x.id);
                    table.ForeignKey(
                        name: "fk_map_views_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
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
                name: "notification_outbox",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<short>(type: "smallint", nullable: false),
                    template_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    placeholders = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    not_before = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_outbox", x => x.id);
                    table.ForeignKey(
                        name: "fk_notification_outbox_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "team_members",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_team_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_team_members_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_team_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trip_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    type = table.Column<short>(type: "smallint", nullable: true),
                    trip_date = table.Column<DateOnly>(type: "date", nullable: false),
                    trip_date_end = table.Column<DateOnly>(type: "date", nullable: true),
                    entry_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    exit_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    results = table.Column<string>(type: "text", nullable: true),
                    weather_conditions = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    location_text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    organizing_club = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    geom = table.Column<Geometry>(type: "geometry(Geometry, 4326)", nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_trip_logs_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
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
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_notification_preferences", x => x.id);
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
                name: "object_acl",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    feature_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entity_type = table.Column<short>(type: "smallint", nullable: true),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_kind = table.Column<short>(type: "smallint", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permissions = table.Column<int>(type: "integer", nullable: false),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_object_acl", x => x.id);
                    table.CheckConstraint("ck_object_acl_one_target", "(feature_id IS NOT NULL AND entity_type IS NULL AND entity_id IS NULL) OR (feature_id IS NULL AND entity_type IS NOT NULL AND entity_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_object_acl_features_feature_id",
                        column: x => x.feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_object_acl_users_granted_by",
                        column: x => x.granted_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
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
                name: "geofiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    format = table.Column<short>(type: "smallint", nullable: false),
                    srid = table.Column<int>(type: "integer", nullable: true),
                    import_status = table.Column<short>(type: "smallint", nullable: false),
                    import_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    feature_count = table.Column<int>(type: "integer", nullable: false),
                    bbox = table.Column<Polygon>(type: "geometry(Polygon, 4326)", nullable: true),
                    style = table.Column<string>(type: "jsonb", nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_geofiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_geofiles_stored_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_geofiles_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_geofiles_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trip_log_participants",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    trip_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name_text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_log_participants", x => x.id);
                    table.CheckConstraint("ck_trip_log_participants_one_identity", "(user_id IS NULL) <> (name_text IS NULL)");
                    table.ForeignKey(
                        name: "fk_trip_log_participants_trip_logs_trip_log_id",
                        column: x => x.trip_log_id,
                        principalTable: "trip_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trip_log_participants_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
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
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                        name: "fk_georeferenced_maps_stored_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_georeferenced_maps_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_georeferenced_maps_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
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
                        name: "fk_survey_models_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trip_log_caves",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    trip_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cave_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_log_caves", x => x.id);
                    table.ForeignKey(
                        name: "fk_trip_log_caves_caves_cave_id",
                        column: x => x.cave_id,
                        principalTable: "caves",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trip_log_caves_trip_logs_trip_log_id",
                        column: x => x.trip_log_id,
                        principalTable: "trip_logs",
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

            migrationBuilder.CreateIndex(
                name: "ix_account_data_exports_user_id_created_at",
                table: "account_data_exports",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_added_by",
                table: "attachments",
                column: "added_by");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_entity_type_entity_id",
                table: "attachments",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_feature_id",
                table: "attachments",
                column: "feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_file_id",
                table: "attachments",
                column: "file_id");

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
                name: "ix_cave_types_code",
                table: "cave_types",
                column: "code",
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
                name: "ix_entrance_types_code",
                table: "entrance_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_feature_ancestors_ancestor_id",
                table: "feature_ancestors",
                column: "ancestor_id");

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
                name: "ix_features_category",
                table: "features",
                column: "category");

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
                name: "ix_features_team_id",
                table: "features",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_files_geom",
                table: "files",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_files_sha256",
                table: "files",
                column: "sha256");

            migrationBuilder.CreateIndex(
                name: "ix_files_uploaded_by",
                table: "files",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "ix_files_version_group_id_version_number",
                table: "files",
                columns: new[] { "version_group_id", "version_number" },
                unique: true);

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
                name: "ix_geofiles_file_id",
                table: "geofiles",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_geofiles_owner_user_id",
                table: "geofiles",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_geofiles_team_id",
                table: "geofiles",
                column: "team_id");

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
                name: "ix_georeferenced_maps_file_id",
                table: "georeferenced_maps",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_georeferenced_maps_owner_user_id",
                table: "georeferenced_maps",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_georeferenced_maps_team_id",
                table: "georeferenced_maps",
                column: "team_id");

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
                name: "ix_map_views_team_id",
                table: "map_views",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_templates_key_locale",
                table: "message_templates",
                columns: new[] { "key", "locale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_outbox_status_not_before_id",
                table: "notification_outbox",
                columns: new[] { "status", "not_before", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_outbox_user_id_status",
                table: "notification_outbox",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_object_acl_entity_type_entity_id_subject_kind_subject_id",
                table: "object_acl",
                columns: new[] { "entity_type", "entity_id", "subject_kind", "subject_id" },
                unique: true,
                filter: "entity_type IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_object_acl_feature_id_subject_kind_subject_id",
                table: "object_acl",
                columns: new[] { "feature_id", "subject_kind", "subject_id" },
                unique: true,
                filter: "feature_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_object_acl_granted_by",
                table: "object_acl",
                column: "granted_by");

            migrationBuilder.CreateIndex(
                name: "ix_object_acl_subject_kind_subject_id",
                table: "object_acl",
                columns: new[] { "subject_kind", "subject_id" });

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
                name: "ix_processing_jobs_status_id",
                table: "processing_jobs",
                columns: new[] { "status", "id" });

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
                name: "ix_survey_models_cave_feature_id",
                table: "survey_models",
                column: "cave_feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_models_file_id",
                table: "survey_models",
                column: "file_id");

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
                name: "ix_team_members_team_id_user_id",
                table: "team_members",
                columns: new[] { "team_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_team_members_user_id",
                table: "team_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_teams_name",
                table: "teams",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_teams_slug",
                table: "teams",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trip_log_caves_cave_id",
                table: "trip_log_caves",
                column: "cave_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_log_caves_trip_log_id_cave_id",
                table: "trip_log_caves",
                columns: new[] { "trip_log_id", "cave_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trip_log_participants_trip_log_id",
                table: "trip_log_participants",
                column: "trip_log_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_log_participants_user_id",
                table: "trip_log_participants",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_logs_geom",
                table: "trip_logs",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_trip_logs_owner_user_id",
                table: "trip_logs",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_logs_team_id",
                table: "trip_logs",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_logs_trip_date",
                table: "trip_logs",
                column: "trip_date");

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
                name: "ix_user_notification_preferences_user_id_category",
                table: "user_notification_preferences",
                columns: new[] { "user_id", "category" },
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
                name: "UserNameIndex",
                table: "users",
                column: "normalized_user_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_data_exports");

            migrationBuilder.DropTable(
                name: "app_settings");

            migrationBuilder.DropTable(
                name: "attachments");

            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "cave_entrances");

            migrationBuilder.DropTable(
                name: "centerlines");

            migrationBuilder.DropTable(
                name: "feature_ancestors");

            migrationBuilder.DropTable(
                name: "feature_hierarchy_edges");

            migrationBuilder.DropTable(
                name: "feature_links");

            migrationBuilder.DropTable(
                name: "feature_shares");

            migrationBuilder.DropTable(
                name: "geofile_features");

            migrationBuilder.DropTable(
                name: "georeferenced_maps");

            migrationBuilder.DropTable(
                name: "hierarchy_memberships");

            migrationBuilder.DropTable(
                name: "map_layers");

            migrationBuilder.DropTable(
                name: "map_views");

            migrationBuilder.DropTable(
                name: "message_templates");

            migrationBuilder.DropTable(
                name: "notification_outbox");

            migrationBuilder.DropTable(
                name: "object_acl");

            migrationBuilder.DropTable(
                name: "OpenIddictScopes");

            migrationBuilder.DropTable(
                name: "OpenIddictTokens");

            migrationBuilder.DropTable(
                name: "processing_jobs");

            migrationBuilder.DropTable(
                name: "role_claims");

            migrationBuilder.DropTable(
                name: "taggings");

            migrationBuilder.DropTable(
                name: "team_members");

            migrationBuilder.DropTable(
                name: "trip_log_caves");

            migrationBuilder.DropTable(
                name: "trip_log_participants");

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
                name: "entrance_types");

            migrationBuilder.DropTable(
                name: "survey_models");

            migrationBuilder.DropTable(
                name: "link_kinds");

            migrationBuilder.DropTable(
                name: "geofiles");

            migrationBuilder.DropTable(
                name: "hierarchies");

            migrationBuilder.DropTable(
                name: "OpenIddictAuthorizations");

            migrationBuilder.DropTable(
                name: "tags");

            migrationBuilder.DropTable(
                name: "trip_logs");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "caves");

            migrationBuilder.DropTable(
                name: "files");

            migrationBuilder.DropTable(
                name: "OpenIddictApplications");

            migrationBuilder.DropTable(
                name: "cave_types");

            migrationBuilder.DropTable(
                name: "features");

            migrationBuilder.DropTable(
                name: "rock_types");

            migrationBuilder.DropTable(
                name: "feature_types");

            migrationBuilder.DropTable(
                name: "teams");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS immutable_unaccent(text);");
        }
    }
}
