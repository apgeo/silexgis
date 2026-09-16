using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TrackingViewerStationNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "station_name",
                table: "trip_position_events",
                newName: "viewer_station_name");

            // The same rename inside the history rows that recorded changes to the old column.
            // Those rows key each changed field by the property's own name, and the timeline drops
            // a position report's station name by matching that key exactly — so a rename that
            // moved the column and left the history alone would go on storing history under a key
            // nothing recognises, and every station name recorded before this migration would
            // start being shown to readers the live surfaces withhold it from. It is the same
            // field under its new name, so it is renamed rather than dropped.
            //
            // jsonb_exists() rather than the `?` operator it spells: a question mark in a command
            // string is what more than one driver reads as a parameter placeholder, and a migration
            // is not the place to find out which.
            migrationBuilder.Sql(
                """
                UPDATE audit_log
                SET changes = (changes - 'StationName')
                    || jsonb_build_object('ViewerStationName', changes -> 'StationName')
                WHERE entity_type = 'TripPositionEvent' AND jsonb_exists(changes, 'StationName');
                """);

            migrationBuilder.AddColumn<string>(
                name: "root_survey_name",
                table: "survey_models",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "root_survey_name",
                table: "survey_models");

            migrationBuilder.Sql(
                """
                UPDATE audit_log
                SET changes = (changes - 'ViewerStationName')
                    || jsonb_build_object('StationName', changes -> 'ViewerStationName')
                WHERE entity_type = 'TripPositionEvent' AND jsonb_exists(changes, 'ViewerStationName');
                """);

            migrationBuilder.RenameColumn(
                name: "viewer_station_name",
                table: "trip_position_events",
                newName: "station_name");
        }
    }
}
