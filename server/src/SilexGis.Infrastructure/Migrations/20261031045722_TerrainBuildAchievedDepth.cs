using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TerrainBuildAchievedDepth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "achieved_max_depth",
                table: "terrain_builds",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "pyramid_bytes",
                table: "terrain_builds",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "achieved_max_depth",
                table: "terrain_builds");

            migrationBuilder.DropColumn(
                name: "pyramid_bytes",
                table: "terrain_builds");
        }
    }
}
