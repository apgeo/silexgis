using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SurveyStationsAndShots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "dropped_shot_count",
                table: "survey_models",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "merged_station_count",
                table: "survey_models",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "survey_shots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    survey_model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_station_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    to_station_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    survey_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    geom = table.Column<LineString>(type: "geometry(LineStringZ, 4326)", nullable: false),
                    length_m = table.Column<double>(type: "double precision", nullable: false),
                    flags = table.Column<int>(type: "integer", nullable: false),
                    raw_flags = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_shots", x => x.id);
                    table.CheckConstraint("ck_survey_shots_two_points", "st_npoints(geom) = 2");
                    table.ForeignKey(
                        name: "fk_survey_shots_survey_models_survey_model_id",
                        column: x => x.survey_model_id,
                        principalTable: "survey_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "survey_stations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    survey_model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    survey_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    file_station_id = table.Column<long>(type: "bigint", nullable: true),
                    position = table.Column<Point>(type: "geometry(PointZ, 4326)", nullable: false),
                    flags = table.Column<int>(type: "integer", nullable: false),
                    raw_flags = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_stations", x => x.id);
                    table.ForeignKey(
                        name: "fk_survey_stations_survey_models_survey_model_id",
                        column: x => x.survey_model_id,
                        principalTable: "survey_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_survey_shots_geom",
                table: "survey_shots",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_survey_shots_survey_model_id",
                table: "survey_shots",
                column: "survey_model_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_stations_position",
                table: "survey_stations",
                column: "position")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_survey_stations_survey_model_id_name",
                table: "survey_stations",
                columns: new[] { "survey_model_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "survey_shots");

            migrationBuilder.DropTable(
                name: "survey_stations");

            migrationBuilder.DropColumn(
                name: "dropped_shot_count",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "merged_station_count",
                table: "survey_models");
        }
    }
}
