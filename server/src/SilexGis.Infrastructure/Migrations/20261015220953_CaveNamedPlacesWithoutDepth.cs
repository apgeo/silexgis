using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CaveNamedPlacesWithoutDepth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "depth_m",
                table: "cave_depth_places",
                type: "numeric(7,1)",
                precision: 7,
                scale: 1,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(7,1)",
                oldPrecision: 7,
                oldScale: 1);

            migrationBuilder.AddColumn<bool>(
                name: "is_landmark",
                table: "cave_depth_places",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "note",
                table: "cave_depth_places",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_cave_depth_places_cave_station_depthless",
                table: "cave_depth_places",
                columns: new[] { "cave_feature_id", "viewer_station_name" },
                unique: true,
                filter: "depth_m IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_cave_depth_places_cave_station_depthless",
                table: "cave_depth_places");

            migrationBuilder.DropColumn(
                name: "is_landmark",
                table: "cave_depth_places");

            migrationBuilder.DropColumn(
                name: "note",
                table: "cave_depth_places");

            // The earlier shape has no way to hold a place named without a depth: the column goes
            // back to not null, and a row it cannot hold would make that step fail rather than be
            // carried across.
            migrationBuilder.Sql("DELETE FROM cave_depth_places WHERE depth_m IS NULL;");

            migrationBuilder.AlterColumn<decimal>(
                name: "depth_m",
                table: "cave_depth_places",
                type: "numeric(7,1)",
                precision: 7,
                scale: 1,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(7,1)",
                oldPrecision: 7,
                oldScale: 1,
                oldNullable: true);
        }
    }
}
