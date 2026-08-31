using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MapLayerCatalogFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "api_key_name",
                table: "map_layers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "group_name",
                table: "map_layers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_zoom",
                table: "map_layers",
                type: "integer",
                nullable: false,
                defaultValue: 19);

            migrationBuilder.AddColumn<int>(
                name: "min_zoom",
                table: "map_layers",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "api_key_name",
                table: "map_layers");

            migrationBuilder.DropColumn(
                name: "group_name",
                table: "map_layers");

            migrationBuilder.DropColumn(
                name: "max_zoom",
                table: "map_layers");

            migrationBuilder.DropColumn(
                name: "min_zoom",
                table: "map_layers");
        }
    }
}
