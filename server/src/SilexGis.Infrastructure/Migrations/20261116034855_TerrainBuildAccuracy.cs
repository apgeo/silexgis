using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TerrainBuildAccuracy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "accuracy_source",
                table: "terrain_builds",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "source_resolution_m",
                table: "terrain_builds",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "vertical_accuracy_m",
                table: "terrain_builds",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "vertical_accuracy_statistic",
                table: "terrain_builds",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_terrain_builds_accuracy",
                table: "terrain_builds",
                sql: "(source_resolution_m is null or source_resolution_m > 0) and (vertical_accuracy_m is null or vertical_accuracy_m > 0) and ((vertical_accuracy_m is null) = (vertical_accuracy_statistic is null))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_terrain_builds_accuracy",
                table: "terrain_builds");

            migrationBuilder.DropColumn(
                name: "accuracy_source",
                table: "terrain_builds");

            migrationBuilder.DropColumn(
                name: "source_resolution_m",
                table: "terrain_builds");

            migrationBuilder.DropColumn(
                name: "vertical_accuracy_m",
                table: "terrain_builds");

            migrationBuilder.DropColumn(
                name: "vertical_accuracy_statistic",
                table: "terrain_builds");
        }
    }
}
