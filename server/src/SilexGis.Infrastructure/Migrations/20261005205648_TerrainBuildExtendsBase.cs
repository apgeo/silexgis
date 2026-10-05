using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TerrainBuildExtendsBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "base_build_id",
                table: "terrain_builds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "inherited",
                table: "terrain_build_sources",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_terrain_builds_base_build_id",
                table: "terrain_builds",
                column: "base_build_id");

            migrationBuilder.AddForeignKey(
                name: "fk_terrain_builds_terrain_builds_base_build_id",
                table: "terrain_builds",
                column: "base_build_id",
                principalTable: "terrain_builds",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_terrain_builds_terrain_builds_base_build_id",
                table: "terrain_builds");

            migrationBuilder.DropIndex(
                name: "ix_terrain_builds_base_build_id",
                table: "terrain_builds");

            migrationBuilder.DropColumn(
                name: "base_build_id",
                table: "terrain_builds");

            migrationBuilder.DropColumn(
                name: "inherited",
                table: "terrain_build_sources");
        }
    }
}
