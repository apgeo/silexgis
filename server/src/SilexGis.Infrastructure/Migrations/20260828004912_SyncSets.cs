using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sync_set_members");

            migrationBuilder.DropTable(
                name: "sync_sets");
        }
    }
}
