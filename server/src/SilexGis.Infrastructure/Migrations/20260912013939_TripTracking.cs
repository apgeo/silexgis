using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TripTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trip_teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_teams", x => x.id);
                    table.ForeignKey(
                        name: "fk_trip_teams_trip_logs_trip_log_id",
                        column: x => x.trip_log_id,
                        principalTable: "trip_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trip_tracking",
                columns: table => new
                {
                    trip_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    survey_model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cave_feature_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reference_station_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    depth_filter = table.Column<string[]>(type: "text[]", nullable: false),
                    armed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_tracking", x => x.trip_log_id);
                    table.ForeignKey(
                        name: "fk_trip_tracking_features_cave_feature_id",
                        column: x => x.cave_feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_trip_tracking_survey_models_survey_model_id",
                        column: x => x.survey_model_id,
                        principalTable: "survey_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_trip_tracking_trip_logs_trip_log_id",
                        column: x => x.trip_log_id,
                        principalTable: "trip_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trip_position_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    survey_model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cave_feature_id = table.Column<Guid>(type: "uuid", nullable: true),
                    station_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    depth_entered_m = table.Column<decimal>(type: "numeric(7,1)", precision: 7, scale: 1, nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_position_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_trip_position_events_cavers_caver_id",
                        column: x => x.caver_id,
                        principalTable: "cavers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_trip_position_events_features_cave_feature_id",
                        column: x => x.cave_feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_trip_position_events_survey_models_survey_model_id",
                        column: x => x.survey_model_id,
                        principalTable: "survey_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_trip_position_events_trip_logs_trip_log_id",
                        column: x => x.trip_log_id,
                        principalTable: "trip_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trip_position_events_trip_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "trip_teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_trip_position_events_users_recorded_by_user_id",
                        column: x => x.recorded_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_trip_position_events_cave_feature_id",
                table: "trip_position_events",
                column: "cave_feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_position_events_caver_id",
                table: "trip_position_events",
                column: "caver_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_position_events_recorded_by_user_id",
                table: "trip_position_events",
                column: "recorded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_position_events_survey_model_id",
                table: "trip_position_events",
                column: "survey_model_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_position_events_team_id",
                table: "trip_position_events",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_position_events_trip_log_id_caver_id_recorded_at",
                table: "trip_position_events",
                columns: new[] { "trip_log_id", "caver_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_trip_position_events_trip_log_id_recorded_at",
                table: "trip_position_events",
                columns: new[] { "trip_log_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_trip_teams_trip_log_id",
                table: "trip_teams",
                column: "trip_log_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_tracking_cave_feature_id",
                table: "trip_tracking",
                column: "cave_feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_tracking_survey_model_id",
                table: "trip_tracking",
                column: "survey_model_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trip_position_events");

            migrationBuilder.DropTable(
                name: "trip_tracking");

            migrationBuilder.DropTable(
                name: "trip_teams");
        }
    }
}
