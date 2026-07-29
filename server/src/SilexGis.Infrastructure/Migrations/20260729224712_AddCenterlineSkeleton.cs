using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCenterlineSkeleton : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "path_count",
                table: "cave_centerlines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<MultiLineString>(
                name: "skeleton",
                table: "cave_centerlines",
                type: "geometry(MultiLineString, 4326)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "skeleton_path_count",
                table: "cave_centerlines",
                type: "integer",
                nullable: true);

            // The component count is cheap to derive here. The display skeleton is a graph
            // computation, so it is filled in by the startup backfill, which treats a null
            // skeleton_path_count as "not built yet".
            migrationBuilder.Sql("UPDATE cave_centerlines SET path_count = ST_NumGeometries(geom)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "path_count",
                table: "cave_centerlines");

            migrationBuilder.DropColumn(
                name: "skeleton",
                table: "cave_centerlines");

            migrationBuilder.DropColumn(
                name: "skeleton_path_count",
                table: "cave_centerlines");
        }
    }
}
