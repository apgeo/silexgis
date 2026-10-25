using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SurveyUploadArtefacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "output",
                table: "survey_upload_files",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "survey_model_id",
                table: "survey_compilations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_survey_compilations_survey_model_id",
                table: "survey_compilations",
                column: "survey_model_id");

            migrationBuilder.AddForeignKey(
                name: "fk_survey_compilations_survey_models_survey_model_id",
                table: "survey_compilations",
                column: "survey_model_id",
                principalTable: "survey_models",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_survey_compilations_survey_models_survey_model_id",
                table: "survey_compilations");

            migrationBuilder.DropIndex(
                name: "ix_survey_compilations_survey_model_id",
                table: "survey_compilations");

            migrationBuilder.DropColumn(
                name: "output",
                table: "survey_upload_files");

            migrationBuilder.DropColumn(
                name: "survey_model_id",
                table: "survey_compilations");
        }
    }
}
