using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SurveyModelCurrent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_current",
                table: "survey_models",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_survey_models_current_line_plot",
                table: "survey_models",
                column: "cave_feature_id",
                unique: true,
                filter: "is_current AND format <> 2");

            migrationBuilder.CreateIndex(
                name: "ix_survey_models_current_wall_mesh",
                table: "survey_models",
                column: "cave_feature_id",
                unique: true,
                filter: "is_current AND format = 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_survey_models_current_line_plot",
                table: "survey_models");

            migrationBuilder.DropIndex(
                name: "ix_survey_models_current_wall_mesh",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "is_current",
                table: "survey_models");
        }
    }
}
