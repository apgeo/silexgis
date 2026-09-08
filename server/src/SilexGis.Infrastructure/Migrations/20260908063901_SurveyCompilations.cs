using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SurveyCompilations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "survey_compilations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cave_feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    survey_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    log_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    log_version_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    read_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    outcome = table.Column<short>(type: "smallint", nullable: true),
                    compiler_version = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    compiler_release_date = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    incomplete_stage = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    compilation_seconds = table.Column<int>(type: "integer", nullable: true),
                    error_count = table.Column<int>(type: "integer", nullable: true),
                    warning_count = table.Column<int>(type: "integer", nullable: true),
                    loop_count = table.Column<int>(type: "integer", nullable: true),
                    average_loop_error_percent = table.Column<double>(type: "double precision", nullable: true),
                    total_length_m = table.Column<double>(type: "double precision", nullable: true),
                    total_length_adjusted_m = table.Column<double>(type: "double precision", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_compilations", x => x.id);
                    table.ForeignKey(
                        name: "fk_survey_compilations_caves_cave_feature_id",
                        column: x => x.cave_feature_id,
                        principalTable: "caves",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_survey_compilations_survey_sources_survey_source_id",
                        column: x => x.survey_source_id,
                        principalTable: "survey_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "survey_compilation_loops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    survey_compilation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    relative_error_percent = table.Column<double>(type: "double precision", nullable: false),
                    absolute_error_m = table.Column<double>(type: "double precision", nullable: false),
                    total_length_m = table.Column<double>(type: "double precision", nullable: false),
                    station_count = table.Column<int>(type: "integer", nullable: false),
                    error_xm = table.Column<double>(type: "double precision", nullable: false),
                    error_ym = table.Column<double>(type: "double precision", nullable: false),
                    error_zm = table.Column<double>(type: "double precision", nullable: false),
                    stations = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_compilation_loops", x => x.id);
                    table.ForeignKey(
                        name: "fk_survey_compilation_loops_survey_compilations_survey_compila",
                        column: x => x.survey_compilation_id,
                        principalTable: "survey_compilations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_survey_compilation_loops_survey_compilation_id_ordinal",
                table: "survey_compilation_loops",
                columns: new[] { "survey_compilation_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_survey_compilations_cave_feature_id",
                table: "survey_compilations",
                column: "cave_feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_compilations_survey_source_id",
                table: "survey_compilations",
                column: "survey_source_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "survey_compilation_loops");

            migrationBuilder.DropTable(
                name: "survey_compilations");
        }
    }
}
