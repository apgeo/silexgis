using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGeoreferencedMaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "georeferenced_maps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    map_kind = table.Column<short>(type: "smallint", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    processing_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    bbox = table.Column<Polygon>(type: "geometry(Polygon, 4326)", nullable: true),
                    min_zoom = table.Column<int>(type: "integer", nullable: true),
                    max_zoom = table.Column<int>(type: "integer", nullable: true),
                    attribution = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    default_opacity = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false),
                    cave_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_georeferenced_maps", x => x.id);
                    table.ForeignKey(
                        name: "fk_georeferenced_maps_caves_cave_id",
                        column: x => x.cave_id,
                        principalTable: "caves",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_georeferenced_maps_stored_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_georeferenced_maps_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_georeferenced_maps_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_georeferenced_maps_bbox",
                table: "georeferenced_maps",
                column: "bbox")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_georeferenced_maps_cave_id",
                table: "georeferenced_maps",
                column: "cave_id");

            migrationBuilder.CreateIndex(
                name: "ix_georeferenced_maps_file_id",
                table: "georeferenced_maps",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_georeferenced_maps_owner_user_id",
                table: "georeferenced_maps",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_georeferenced_maps_team_id",
                table: "georeferenced_maps",
                column: "team_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "georeferenced_maps");
        }
    }
}
