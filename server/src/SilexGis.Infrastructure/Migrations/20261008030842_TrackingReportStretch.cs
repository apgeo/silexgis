using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TrackingReportStretch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "viewer_to_station_name",
                table: "trip_position_events",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_trip_position_events_stretch",
                table: "trip_position_events",
                sql: "viewer_to_station_name IS NULL OR (kind = 1 AND viewer_station_name IS NOT NULL AND viewer_to_station_name <> viewer_station_name)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_trip_position_events_stretch",
                table: "trip_position_events");

            migrationBuilder.DropColumn(
                name: "viewer_to_station_name",
                table: "trip_position_events");
        }
    }
}
