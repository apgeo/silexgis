using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCaveDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "caves",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    other_toponyms = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    identification_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    cave_type_id = table.Column<long>(type: "bigint", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    website = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    region = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    hydrographic_basin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    valley = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tributary_river = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    closest_address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    land_registry_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    location_notes = table.Column<string>(type: "text", nullable: true),
                    rock_type_id = table.Column<long>(type: "bigint", nullable: true),
                    rock_age = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    surveyed_length = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    estimated_length = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    real_extension = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    projected_extension = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    depth = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    positive_depth = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    negative_depth = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    potential_depth = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    altitude = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    volume = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    area = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    ramification_index = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: true),
                    cave_age = table.Column<int>(type: "integer", nullable: true),
                    exploration_status = table.Column<short>(type: "smallint", nullable: false),
                    protection_class = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_show_cave = table.Column<bool>(type: "boolean", nullable: false),
                    show_cave_length = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    discovery_date = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    discoverer = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    location_protected = table.Column<bool>(type: "boolean", nullable: false),
                    entrance_count = table.Column<int>(type: "integer", nullable: false),
                    main_geom = table.Column<Point>(type: "geometry(Point, 4326)", nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_caves", x => x.id);
                    table.ForeignKey(
                        name: "fk_caves_cave_types_cave_type_id",
                        column: x => x.cave_type_id,
                        principalTable: "cave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_caves_rock_types_rock_type_id",
                        column: x => x.rock_type_id,
                        principalTable: "rock_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_caves_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_caves_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "map_layers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    layer_kind = table.Column<short>(type: "smallint", nullable: false),
                    url_template = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    options = table.Column<string>(type: "jsonb", nullable: true),
                    attribution = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_base = table.Column<bool>(type: "boolean", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_map_layers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cave_entrances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cave_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    entrance_type_id = table.Column<long>(type: "bigint", nullable: false),
                    is_main = table.Column<bool>(type: "boolean", nullable: false),
                    geom = table.Column<Point>(type: "geometry(Point, 4326)", nullable: false),
                    altitude = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    position_quality = table.Column<short>(type: "smallint", nullable: false),
                    surveyed_at = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cave_entrances", x => x.id);
                    table.ForeignKey(
                        name: "fk_cave_entrances_caves_cave_id",
                        column: x => x.cave_id,
                        principalTable: "caves",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cave_entrances_entrance_types_entrance_type_id",
                        column: x => x.entrance_type_id,
                        principalTable: "entrance_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cave_entrances_cave_id",
                table: "cave_entrances",
                column: "cave_id");

            migrationBuilder.CreateIndex(
                name: "ix_cave_entrances_entrance_type_id",
                table: "cave_entrances",
                column: "entrance_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_cave_entrances_geom",
                table: "cave_entrances",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_caves_cave_type_id",
                table: "caves",
                column: "cave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_caves_deleted_at",
                table: "caves",
                column: "deleted_at",
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_caves_main_geom",
                table: "caves",
                column: "main_geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_caves_name",
                table: "caves",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_caves_owner_user_id",
                table: "caves",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_caves_region",
                table: "caves",
                column: "region");

            migrationBuilder.CreateIndex(
                name: "ix_caves_rock_type_id",
                table: "caves",
                column: "rock_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_caves_team_id",
                table: "caves",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_map_layers_name",
                table: "map_layers",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cave_entrances");

            migrationBuilder.DropTable(
                name: "map_layers");

            migrationBuilder.DropTable(
                name: "caves");
        }
    }
}
