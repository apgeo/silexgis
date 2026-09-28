using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CaveDepthPlaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cave_depth_places",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cave_feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    depth_m = table.Column<decimal>(type: "numeric(7,1)", precision: 7, scale: 1, nullable: false),
                    viewer_station_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    place_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cave_depth_places", x => x.id);
                    table.ForeignKey(
                        name: "fk_cave_depth_places_features_cave_feature_id",
                        column: x => x.cave_feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_cave_depth_places_cave_depth",
                table: "cave_depth_places",
                columns: new[] { "cave_feature_id", "depth_m" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cave_depth_places");
        }
    }
}
