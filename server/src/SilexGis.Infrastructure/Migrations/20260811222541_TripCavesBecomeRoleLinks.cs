using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TripCavesBecomeRoleLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trip_log_caves");

            migrationBuilder.CreateIndex(
                name: "ix_res_link_members_entity_link",
                table: "res_link_members",
                columns: new[] { "entity_type", "entity_id", "res_link_id" },
                filter: "entity_type IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_res_link_members_feature_link",
                table: "res_link_members",
                columns: new[] { "feature_id", "res_link_id" },
                filter: "feature_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_res_link_members_entity_link",
                table: "res_link_members");

            migrationBuilder.DropIndex(
                name: "ix_res_link_members_feature_link",
                table: "res_link_members");

            migrationBuilder.CreateTable(
                name: "trip_log_caves",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cave_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_log_id = table.Column<Guid>(type: "uuid", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "ix_trip_log_caves_cave_id",
                table: "trip_log_caves",
                column: "cave_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_log_caves_trip_log_id_cave_id",
                table: "trip_log_caves",
                columns: new[] { "trip_log_id", "cave_id" },
                unique: true);
        }
    }
}
