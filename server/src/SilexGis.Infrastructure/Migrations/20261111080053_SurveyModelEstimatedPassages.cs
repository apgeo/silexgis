using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SurveyModelEstimatedPassages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "estimate_passages",
                table: "survey_models",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "estimated_leg_count",
                table: "survey_models",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "estimated_passage_color",
                table: "survey_models",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#e8833a");

            migrationBuilder.AddColumn<double>(
                name: "estimated_passage_diameter_m",
                table: "survey_models",
                type: "double precision",
                nullable: false,
                defaultValue: 1.0);

            migrationBuilder.AddColumn<int>(
                name: "unsized_leg_count",
                table: "survey_models",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "unsized_passage_m",
                table: "survey_models",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "estimate_passages",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "estimated_leg_count",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "estimated_passage_color",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "estimated_passage_diameter_m",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "unsized_leg_count",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "unsized_passage_m",
                table: "survey_models");
        }
    }
}
