using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SurveyModelPlacementAdvice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "placement_off_by_km",
                table: "survey_models",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "suggested_off_by_km",
                table: "survey_models",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "suggested_source_epsg",
                table: "survey_models",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "suggests_local_metres",
                table: "survey_models",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "placement_off_by_km",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "suggested_off_by_km",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "suggested_source_epsg",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "suggests_local_metres",
                table: "survey_models");
        }
    }
}
