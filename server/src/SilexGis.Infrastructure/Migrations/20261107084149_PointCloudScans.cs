using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PointCloudScans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_survey_models_current_line_plot",
                table: "survey_models");

            migrationBuilder.AddColumn<string>(
                name: "point_cloud",
                table: "survey_models",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "scan_artifacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    survey_upload_id = table.Column<Guid>(type: "uuid", nullable: false),
                    survey_model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    format = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    step_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tool = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tool_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    params_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    input_artifact_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false, defaultValueSql: "'{}'::uuid[]"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scan_artifacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_scan_artifacts_stored_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_scan_artifacts_survey_models_survey_model_id",
                        column: x => x.survey_model_id,
                        principalTable: "survey_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_scan_artifacts_survey_uploads_survey_upload_id",
                        column: x => x.survey_upload_id,
                        principalTable: "survey_uploads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_survey_models_current_line_plot",
                table: "survey_models",
                column: "cave_feature_id",
                unique: true,
                filter: "is_current AND format IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "ix_survey_models_current_point_cloud",
                table: "survey_models",
                column: "cave_feature_id",
                unique: true,
                filter: "is_current AND format IN (3, 4)");

            migrationBuilder.CreateIndex(
                name: "ix_scan_artifacts_file_id",
                table: "scan_artifacts",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_scan_artifacts_survey_model_id",
                table: "scan_artifacts",
                column: "survey_model_id");

            migrationBuilder.CreateIndex(
                name: "ix_scan_artifacts_upload_step_params",
                table: "scan_artifacts",
                columns: new[] { "survey_upload_id", "step_id", "params_hash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scan_artifacts");

            migrationBuilder.DropIndex(
                name: "ix_survey_models_current_line_plot",
                table: "survey_models");

            migrationBuilder.DropIndex(
                name: "ix_survey_models_current_point_cloud",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "point_cloud",
                table: "survey_models");

            migrationBuilder.CreateIndex(
                name: "ix_survey_models_current_line_plot",
                table: "survey_models",
                column: "cave_feature_id",
                unique: true,
                filter: "is_current AND format <> 2");
        }
    }
}
