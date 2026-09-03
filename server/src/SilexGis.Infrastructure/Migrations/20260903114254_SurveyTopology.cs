using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SurveyTopology : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "survey_topology",
                columns: table => new
                {
                    survey_model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_count = table.Column<int>(type: "integer", nullable: false),
                    edge_count = table.Column<int>(type: "integer", nullable: false),
                    component_count = table.Column<int>(type: "integer", nullable: false),
                    reduced_node_count = table.Column<int>(type: "integer", nullable: false),
                    reduced_edge_count = table.Column<int>(type: "integer", nullable: false),
                    reduced_component_count = table.Column<int>(type: "integer", nullable: false),
                    cyclomatic_number = table.Column<int>(type: "integer", nullable: false),
                    extremity_count = table.Column<int>(type: "integer", nullable: false),
                    junction_count = table.Column<int>(type: "integer", nullable: false),
                    alpha = table.Column<double>(type: "double precision", nullable: true),
                    beta = table.Column<double>(type: "double precision", nullable: true),
                    gamma = table.Column<double>(type: "double precision", nullable: true),
                    mean_degree = table.Column<double>(type: "double precision", nullable: true),
                    degree_standard_deviation = table.Column<double>(type: "double precision", nullable: true),
                    degree_coefficient_of_variation = table.Column<double>(type: "double precision", nullable: true),
                    correlation_of_vertex_degree = table.Column<double>(type: "double precision", nullable: true),
                    branch_count = table.Column<int>(type: "integer", nullable: false),
                    looping_branch_count = table.Column<int>(type: "integer", nullable: false),
                    mean_branch_length_m = table.Column<double>(type: "double precision", nullable: true),
                    branch_length_coefficient_of_variation = table.Column<double>(type: "double precision", nullable: true),
                    min_branch_length_m = table.Column<double>(type: "double precision", nullable: true),
                    max_branch_length_m = table.Column<double>(type: "double precision", nullable: true),
                    length_entropy = table.Column<double>(type: "double precision", nullable: true),
                    orientation_entropy = table.Column<double>(type: "double precision", nullable: true),
                    mean_tortuosity = table.Column<double>(type: "double precision", nullable: true),
                    average_shortest_path_length = table.Column<double>(type: "double precision", nullable: true),
                    central_point_dominance = table.Column<double>(type: "double precision", nullable: true),
                    average_clustering_coefficient = table.Column<double>(type: "double precision", nullable: true),
                    computed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_topology", x => x.survey_model_id);
                    table.ForeignKey(
                        name: "fk_survey_topology_survey_models_survey_model_id",
                        column: x => x.survey_model_id,
                        principalTable: "survey_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_survey_topology_cyclomatic_number",
                table: "survey_topology",
                column: "cyclomatic_number");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "survey_topology");
        }
    }
}
