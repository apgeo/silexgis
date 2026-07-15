using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFilePhotoGeom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Point>(
                name: "geom",
                table: "files",
                type: "geometry(Point, 4326)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_files_geom",
                table: "files",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_files_geom",
                table: "files");

            migrationBuilder.DropColumn(
                name: "geom",
                table: "files");
        }
    }
}
