using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TripMeetingPoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Geometry>(
                name: "meeting_geom",
                table: "trip_logs",
                type: "geometry(Geometry, 4326)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_trip_logs_meeting_geom",
                table: "trip_logs",
                column: "meeting_geom")
                .Annotation("Npgsql:IndexMethod", "gist");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_trip_logs_meeting_geom",
                table: "trip_logs");

            migrationBuilder.DropColumn(
                name: "meeting_geom",
                table: "trip_logs");
        }
    }
}
