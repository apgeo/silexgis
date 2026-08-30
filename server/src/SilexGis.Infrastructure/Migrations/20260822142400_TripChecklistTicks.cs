using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TripChecklistTicks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "default_checklist_id",
                table: "trip_types",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_checklist_items_checklist_id_id",
                table: "checklist_items",
                columns: new[] { "checklist_id", "id" });

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

            migrationBuilder.CreateIndex(
                name: "ix_trip_types_default_checklist_id",
                table: "trip_types",
                column: "default_checklist_id");

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

            migrationBuilder.AddForeignKey(
                name: "fk_trip_types_checklists_default_checklist_id",
                table: "trip_types",
                column: "default_checklist_id",
                principalTable: "checklists",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_trip_types_checklists_default_checklist_id",
                table: "trip_types");

            migrationBuilder.DropTable(
                name: "trip_checklist_ticks");

            migrationBuilder.DropIndex(
                name: "ix_trip_types_default_checklist_id",
                table: "trip_types");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_checklist_items_checklist_id_id",
                table: "checklist_items");

            migrationBuilder.DropColumn(
                name: "default_checklist_id",
                table: "trip_types");
        }
    }
}
