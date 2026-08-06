using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SurveyMeshes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Point>(
                name: "anchor",
                table: "survey_models",
                type: "geometry(Point, 4326)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "anchor_height_m",
                table: "survey_models",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "applied_rotation_deg",
                table: "survey_models",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "converted_file_id",
                table: "survey_models",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "processing_error",
                table: "survey_models",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_epsg",
                table: "survey_models",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "source_precision_lost",
                table: "survey_models",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<short>(
                name: "status",
                table: "survey_models",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<int>(
                name: "triangle_count",
                table: "survey_models",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_survey_models_converted_file_id",
                table: "survey_models",
                column: "converted_file_id");

            migrationBuilder.AddForeignKey(
                name: "fk_survey_models_files_converted_file_id",
                table: "survey_models",
                column: "converted_file_id",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_survey_models_files_converted_file_id",
                table: "survey_models");

            migrationBuilder.DropIndex(
                name: "ix_survey_models_converted_file_id",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "anchor",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "anchor_height_m",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "applied_rotation_deg",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "converted_file_id",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "processing_error",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "source_epsg",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "source_precision_lost",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "status",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "triangle_count",
                table: "survey_models");
        }
    }
}
