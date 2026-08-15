using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TerrainBuilds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "terrain_builds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    extent = table.Column<Polygon>(type: "geometry(Polygon, 4326)", nullable: false),
                    requested_max_depth = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    phase = table.Column<short>(type: "smallint", nullable: false),
                    progress = table.Column<int>(type: "integer", nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    log_tail = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    pyramid_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    height_datum = table.Column<short>(type: "smallint", nullable: false),
                    geoid_height_m = table.Column<double>(type: "double precision", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terrain_builds", x => x.id);
                    table.CheckConstraint("ck_terrain_builds_progress_range", "progress >= 0 and progress <= 100");
                });

            migrationBuilder.CreateTable(
                name: "terrain_build_sources",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    terrain_build_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    attribution = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    licence = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terrain_build_sources", x => x.id);
                    table.ForeignKey(
                        name: "fk_terrain_build_sources_terrain_builds_terrain_build_id",
                        column: x => x.terrain_build_id,
                        principalTable: "terrain_builds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_terrain_build_sources_terrain_build_id_id",
                table: "terrain_build_sources",
                columns: new[] { "terrain_build_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_terrain_builds_created_at",
                table: "terrain_builds",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_terrain_builds_extent",
                table: "terrain_builds",
                column: "extent")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_terrain_builds_status",
                table: "terrain_builds",
                column: "status",
                filter: "status in (0, 1)");

            migrationBuilder.CreateIndex(
                name: "ux_terrain_builds_active",
                table: "terrain_builds",
                column: "is_active",
                unique: true,
                filter: "is_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "terrain_build_sources");

            migrationBuilder.DropTable(
                name: "terrain_builds");
        }
    }
}
