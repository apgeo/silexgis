using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CavePotentialResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cave_potential_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    model_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    method = table.Column<short>(type: "smallint", nullable: false),
                    definition = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    area_feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cell_meters = table.Column<double>(type: "double precision", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ran_as_full_administrator = table.Column<bool>(type: "boolean", nullable: false),
                    uses_positions = table.Column<bool>(type: "boolean", nullable: false),
                    evidence_feature_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false, defaultValueSql: "'{}'::uuid[]"),
                    protected_feature_count = table.Column<int>(type: "integer", nullable: false),
                    summary = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    processing_job_id = table.Column<long>(type: "bigint", nullable: true),
                    path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    west = table.Column<double>(type: "double precision", nullable: true),
                    south = table.Column<double>(type: "double precision", nullable: true),
                    east = table.Column<double>(type: "double precision", nullable: true),
                    north = table.Column<double>(type: "double precision", nullable: true),
                    columns = table.Column<int>(type: "integer", nullable: true),
                    rows = table.Column<int>(type: "integer", nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cave_potential_results", x => x.id);
                    table.CheckConstraint("ck_cave_potential_results_cell_meters", "cell_meters > 0");
                    table.ForeignKey(
                        name: "fk_cave_potential_results_cave_potential_models_model_id",
                        column: x => x.model_id,
                        principalTable: "cave_potential_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_cave_potential_results_features_area_feature_id",
                        column: x => x.area_feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cave_potential_results_processing_jobs_processing_job_id",
                        column: x => x.processing_job_id,
                        principalTable: "processing_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_cave_potential_results_users_requested_by_user_id",
                        column: x => x.requested_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cave_potential_results_area_feature_id",
                table: "cave_potential_results",
                column: "area_feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_cave_potential_results_model_id",
                table: "cave_potential_results",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "ix_cave_potential_results_processing_job_id",
                table: "cave_potential_results",
                column: "processing_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_cave_potential_results_requested_by_user_id",
                table: "cave_potential_results",
                column: "requested_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cave_potential_results");
        }
    }
}
