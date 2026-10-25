using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TripRecordingReportIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_trip_recordings_converted_trip_log_id",
                table: "trip_recordings");

            migrationBuilder.CreateIndex(
                name: "ix_trip_recordings_converted_trip_log_id",
                table: "trip_recordings",
                column: "converted_trip_log_id",
                filter: "converted_trip_log_id is not null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_trip_recordings_converted_trip_log_id",
                table: "trip_recordings");

            migrationBuilder.CreateIndex(
                name: "ux_trip_recordings_converted_trip_log_id",
                table: "trip_recordings",
                column: "converted_trip_log_id",
                unique: true,
                filter: "converted_trip_log_id is not null");
        }
    }
}
