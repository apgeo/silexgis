using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TerrainDerivativeRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "terrain_derivative_layers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    terrain_build_id = table.Column<Guid>(type: "uuid", nullable: false),
                    derivative = table.Column<short>(type: "smallint", nullable: false),
                    settings = table.Column<string>(type: "jsonb", nullable: false),
                    settings_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    processing_job_id = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terrain_derivative_layers", x => x.id);
                    table.CheckConstraint("ck_terrain_derivative_layers_version", "version >= 0");
                    table.ForeignKey(
                        name: "fk_terrain_derivative_layers_processing_jobs_processing_job_id",
                        column: x => x.processing_job_id,
                        principalTable: "processing_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_terrain_derivative_layers_terrain_builds_terrain_build_id",
                        column: x => x.terrain_build_id,
                        principalTable: "terrain_builds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "terrain_derivative_rasters",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    terrain_derivative_layer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    pixel_size_degrees = table.Column<double>(type: "double precision", nullable: false),
                    footprint = table.Column<Polygon>(type: "geometry(Polygon, 4326)", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terrain_derivative_rasters", x => x.id);
                    table.ForeignKey(
                        name: "fk_terrain_derivative_rasters_terrain_derivative_layers_terrai",
                        column: x => x.terrain_derivative_layer_id,
                        principalTable: "terrain_derivative_layers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_terrain_derivative_layers_processing_job_id",
                table: "terrain_derivative_layers",
                column: "processing_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_terrain_derivative_layers_status",
                table: "terrain_derivative_layers",
                column: "status",
                filter: "status in (0, 1)");

            migrationBuilder.CreateIndex(
                name: "ux_terrain_derivative_layers_request",
                table: "terrain_derivative_layers",
                columns: new[] { "terrain_build_id", "derivative", "settings_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_terrain_derivative_rasters_footprint",
                table: "terrain_derivative_rasters",
                column: "footprint")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_terrain_derivative_rasters_terrain_derivative_layer_id_id",
                table: "terrain_derivative_rasters",
                columns: new[] { "terrain_derivative_layer_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_terrain_derivative_rasters_terrain_derivative_layer_id_path",
                table: "terrain_derivative_rasters",
                columns: new[] { "terrain_derivative_layer_id", "path" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "terrain_derivative_rasters");

            migrationBuilder.DropTable(
                name: "terrain_derivative_layers");
        }
    }
}
