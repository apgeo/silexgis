using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TerrainBuildRasters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "terrain_build_rasters",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    terrain_build_id = table.Column<Guid>(type: "uuid", nullable: false),
                    path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    pixel_size_degrees = table.Column<double>(type: "double precision", nullable: false),
                    footprint = table.Column<Polygon>(type: "geometry(Polygon, 4326)", nullable: false),
                    void_value = table.Column<double>(type: "double precision", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    described_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terrain_build_rasters", x => x.id);
                    table.ForeignKey(
                        name: "fk_terrain_build_rasters_terrain_builds_terrain_build_id",
                        column: x => x.terrain_build_id,
                        principalTable: "terrain_builds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_terrain_build_rasters_footprint",
                table: "terrain_build_rasters",
                column: "footprint")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_terrain_build_rasters_terrain_build_id_id",
                table: "terrain_build_rasters",
                columns: new[] { "terrain_build_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_terrain_build_rasters_terrain_build_id_path",
                table: "terrain_build_rasters",
                columns: new[] { "terrain_build_id", "path" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "terrain_build_rasters");
        }
    }
}
