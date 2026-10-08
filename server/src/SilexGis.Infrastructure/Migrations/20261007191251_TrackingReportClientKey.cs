using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TrackingReportClientKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "client_key",
                table: "trip_position_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_trip_position_events_client_key",
                table: "trip_position_events",
                columns: new[] { "trip_log_id", "client_key", "caver_id" },
                unique: true,
                filter: "client_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_trip_position_events_client_key",
                table: "trip_position_events");

            migrationBuilder.DropColumn(
                name: "client_key",
                table: "trip_position_events");
        }
    }
}
