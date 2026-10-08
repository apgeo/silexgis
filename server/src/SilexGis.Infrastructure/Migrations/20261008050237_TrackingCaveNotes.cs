using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TrackingCaveNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_trip_position_events_client_key",
                table: "trip_position_events");

            migrationBuilder.AlterColumn<Guid>(
                name: "caver_id",
                table: "trip_position_events",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "ux_trip_position_events_client_key",
                table: "trip_position_events",
                columns: new[] { "trip_log_id", "client_key", "caver_id" },
                unique: true,
                filter: "client_key IS NOT NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_trip_position_events_cave_note",
                table: "trip_position_events",
                sql: "(kind = 5) = (caver_id IS NULL) AND (kind <> 5 OR (note IS NOT NULL AND btrim(note) <> '' AND depth_entered_m IS NULL AND team_id IS NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_trip_position_events_client_key",
                table: "trip_position_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_trip_position_events_cave_note",
                table: "trip_position_events");

            migrationBuilder.AlterColumn<Guid>(
                name: "caver_id",
                table: "trip_position_events",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_trip_position_events_client_key",
                table: "trip_position_events",
                columns: new[] { "trip_log_id", "client_key", "caver_id" },
                unique: true,
                filter: "client_key IS NOT NULL");
        }
    }
}
