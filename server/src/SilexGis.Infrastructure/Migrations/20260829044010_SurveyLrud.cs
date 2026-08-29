using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SurveyLrud : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "survey_lrud",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    survey_model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    shot_id = table.Column<long>(type: "bigint", nullable: true),
                    section = table.Column<short>(type: "smallint", nullable: true),
                    left_m = table.Column<double>(type: "double precision", nullable: true),
                    right_m = table.Column<double>(type: "double precision", nullable: true),
                    up_m = table.Column<double>(type: "double precision", nullable: true),
                    down_m = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_lrud", x => x.id);
                    table.CheckConstraint("ck_survey_lrud_measured", "left_m is not null or right_m is not null or up_m is not null or down_m is not null");
                    table.CheckConstraint("ck_survey_lrud_non_negative", "(left_m is null or left_m >= 0) and (right_m is null or right_m >= 0) and (up_m is null or up_m >= 0) and (down_m is null or down_m >= 0)");
                    table.ForeignKey(
                        name: "fk_survey_lrud_survey_models_survey_model_id",
                        column: x => x.survey_model_id,
                        principalTable: "survey_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_survey_lrud_survey_shots_shot_id",
                        column: x => x.shot_id,
                        principalTable: "survey_shots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_survey_lrud_shot_id",
                table: "survey_lrud",
                column: "shot_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_lrud_survey_model_id_station_name",
                table: "survey_lrud",
                columns: new[] { "survey_model_id", "station_name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "survey_lrud");
        }
    }
}
