using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TripCountedFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "depth_reached_m",
                table: "trip_logs",
                type: "numeric(7,1)",
                precision: 7,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "had_incident",
                table: "trip_logs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "length_surveyed_m",
                table: "trip_logs",
                type: "numeric(9,1)",
                precision: 9,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "rope_metres",
                table: "trip_logs",
                type: "numeric(7,1)",
                precision: 7,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "survey_stations",
                table: "trip_logs",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "depth_reached_m",
                table: "trip_logs");

            migrationBuilder.DropColumn(
                name: "had_incident",
                table: "trip_logs");

            migrationBuilder.DropColumn(
                name: "length_surveyed_m",
                table: "trip_logs");

            migrationBuilder.DropColumn(
                name: "rope_metres",
                table: "trip_logs");

            migrationBuilder.DropColumn(
                name: "survey_stations",
                table: "trip_logs");
        }
    }
}
