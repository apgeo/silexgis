using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ResLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateIndex(
                name: "ix_res_link_members_added_by",
                table: "res_link_members",
                column: "added_by");

            migrationBuilder.CreateIndex(
                name: "ix_res_link_members_anchor_file_id",
                table: "res_link_members",
                column: "anchor_file_id");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "res_link_members");

            migrationBuilder.DropTable(
                name: "res_links");

            migrationBuilder.DropTable(
                name: "res_link_relation_types");
        }
    }
}
