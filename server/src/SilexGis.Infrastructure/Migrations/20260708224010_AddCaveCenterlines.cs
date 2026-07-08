using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCaveCenterlines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Geometry>(
                name: "geom",
                table: "geofile_features",
                type: "geometry",
                nullable: false,
                oldClrType: typeof(Geometry),
                oldType: "geometry(Geometry, 4326)");

            migrationBuilder.CreateTable(
                name: "cave_centerlines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cave_id = table.Column<Guid>(type: "uuid", nullable: false),
                    survey_model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    geom = table.Column<MultiLineString>(type: "geometry(MultiLineStringZ, 4326)", nullable: false),
                    length_m = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    source = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cave_centerlines", x => x.id);
                    table.ForeignKey(
                        name: "fk_cave_centerlines_caves_cave_id",
                        column: x => x.cave_id,
                        principalTable: "caves",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cave_centerlines_survey_models_survey_model_id",
                        column: x => x.survey_model_id,
                        principalTable: "survey_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_geofile_features_geom_srid",
                table: "geofile_features",
                sql: "st_srid(geom) = 4326");

            migrationBuilder.CreateIndex(
                name: "ix_cave_centerlines_cave_id",
                table: "cave_centerlines",
                column: "cave_id");

            migrationBuilder.CreateIndex(
                name: "ix_cave_centerlines_geom",
                table: "cave_centerlines",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_cave_centerlines_survey_model_id",
                table: "cave_centerlines",
                column: "survey_model_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cave_centerlines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_geofile_features_geom_srid",
                table: "geofile_features");

            migrationBuilder.AlterColumn<Geometry>(
                name: "geom",
                table: "geofile_features",
                type: "geometry(Geometry, 4326)",
                nullable: false,
                oldClrType: typeof(Geometry),
                oldType: "geometry");
        }
    }
}
