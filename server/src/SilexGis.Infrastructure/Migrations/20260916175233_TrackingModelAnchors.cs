using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TrackingModelAnchors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_trip_position_events_survey_models_survey_model_id",
                table: "trip_position_events");

            migrationBuilder.DropForeignKey(
                name: "fk_trip_tracking_survey_models_survey_model_id",
                table: "trip_tracking");

            migrationBuilder.DropIndex(
                name: "ix_trip_tracking_survey_model_id",
                table: "trip_tracking");

            migrationBuilder.DropIndex(
                name: "ix_trip_position_events_survey_model_id",
                table: "trip_position_events");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_trip_tracking_survey_model_id",
                table: "trip_tracking",
                column: "survey_model_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_position_events_survey_model_id",
                table: "trip_position_events",
                column: "survey_model_id");

            migrationBuilder.AddForeignKey(
                name: "fk_trip_position_events_survey_models_survey_model_id",
                table: "trip_position_events",
                column: "survey_model_id",
                principalTable: "survey_models",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_trip_tracking_survey_models_survey_model_id",
                table: "trip_tracking",
                column: "survey_model_id",
                principalTable: "survey_models",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
