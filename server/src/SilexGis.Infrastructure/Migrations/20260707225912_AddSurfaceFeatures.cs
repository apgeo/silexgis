using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using NpgsqlTypes;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSurfaceFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "surface_features",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    feature_type_id = table.Column<long>(type: "bigint", nullable: false),
                    geom = table.Column<Geometry>(type: "geometry(Geometry, 4326)", nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    properties = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    cave_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('simple', immutable_unaccent(coalesce(name, '') || ' ' || coalesce(description, '')))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_surface_features", x => x.id);
                    table.ForeignKey(
                        name: "fk_surface_features_caves_cave_id",
                        column: x => x.cave_id,
                        principalTable: "caves",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_surface_features_feature_types_feature_type_id",
                        column: x => x.feature_type_id,
                        principalTable: "feature_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_surface_features_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_surface_features_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_surface_features_cave_id",
                table: "surface_features",
                column: "cave_id");

            migrationBuilder.CreateIndex(
                name: "ix_surface_features_feature_type_id",
                table: "surface_features",
                column: "feature_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_surface_features_geom",
                table: "surface_features",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_surface_features_owner_user_id",
                table: "surface_features",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_surface_features_search_vector",
                table: "surface_features",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_surface_features_team_id",
                table: "surface_features",
                column: "team_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "surface_features");
        }
    }
}
