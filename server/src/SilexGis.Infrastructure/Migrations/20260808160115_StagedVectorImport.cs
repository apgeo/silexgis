using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StagedVectorImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_options",
                table: "geofiles",
                type: "jsonb",
                nullable: true);

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
                name: "import_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    geofile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    term_rule_set_id = table.Column<Guid>(type: "uuid", nullable: true),
                    term_rule_set_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<short>(type: "smallint", nullable: false),
                    options = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_count = table.Column<int>(type: "integer", nullable: false),
                    attached_count = table.Column<int>(type: "integer", nullable: false),
                    skipped_count = table.Column<int>(type: "integer", nullable: false),
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
                        name: "fk_import_batches_users_confirmed_by_user_id",
                        column: x => x.confirmed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
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
                    source_feature_id = table.Column<long>(type: "bigint", nullable: true),
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
                });

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "geofile_import_sessions");

            migrationBuilder.DropTable(
                name: "import_batch_items");

            migrationBuilder.DropTable(
                name: "import_batches");

            migrationBuilder.DropTable(
                name: "term_rule_sets");

            migrationBuilder.DropColumn(
                name: "source_options",
                table: "geofiles");
        }
    }
}
