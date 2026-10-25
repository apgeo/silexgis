using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TripRecordings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "trip_recording_id",
                table: "import_batch_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "trip_recording_point_id",
                table: "import_batch_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "trip_recordings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cave_feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    log = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_device_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caving_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    converted_trip_log_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_recordings", x => x.id);
                    table.ForeignKey(
                        name: "fk_trip_recordings_caving_groups_caving_group_id",
                        column: x => x.caving_group_id,
                        principalTable: "caving_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_trip_recordings_features_cave_feature_id",
                        column: x => x.cave_feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_trip_recordings_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trip_recording_points",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recording_id = table.Column<Guid>(type: "uuid", nullable: false),
                    place_feature_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scanned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_by_device_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_recording_points", x => x.id);
                    table.ForeignKey(
                        name: "fk_trip_recording_points_features_place_feature_id",
                        column: x => x.place_feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_trip_recording_points_trip_recordings_recording_id",
                        column: x => x.recording_id,
                        principalTable: "trip_recordings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_import_batch_items_trip_recording_id",
                table: "import_batch_items",
                column: "trip_recording_id",
                filter: "trip_recording_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_import_batch_items_trip_recording_point_id",
                table: "import_batch_items",
                column: "trip_recording_point_id",
                filter: "trip_recording_point_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_trip_recording_points_place_feature_id",
                table: "trip_recording_points",
                column: "place_feature_id",
                filter: "place_feature_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_trip_recording_points_recording_updated",
                table: "trip_recording_points",
                columns: new[] { "recording_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_trip_recordings_cave_feature_id",
                table: "trip_recordings",
                column: "cave_feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_recordings_caving_group_id",
                table: "trip_recordings",
                column: "caving_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_recordings_deleted_at",
                table: "trip_recordings",
                column: "deleted_at",
                filter: "deleted_at is not null");

            migrationBuilder.CreateIndex(
                name: "ix_trip_recordings_owner_user_id",
                table: "trip_recordings",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_trip_recordings_converted_trip_log_id",
                table: "trip_recordings",
                column: "converted_trip_log_id",
                unique: true,
                filter: "converted_trip_log_id is not null");

            migrationBuilder.AddForeignKey(
                name: "fk_import_batch_items_trip_recording_points_trip_recording_poi",
                table: "import_batch_items",
                column: "trip_recording_point_id",
                principalTable: "trip_recording_points",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_import_batch_items_trip_recordings_trip_recording_id",
                table: "import_batch_items",
                column: "trip_recording_id",
                principalTable: "trip_recordings",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_import_batch_items_trip_recording_points_trip_recording_poi",
                table: "import_batch_items");

            migrationBuilder.DropForeignKey(
                name: "fk_import_batch_items_trip_recordings_trip_recording_id",
                table: "import_batch_items");

            migrationBuilder.DropTable(
                name: "trip_recording_points");

            migrationBuilder.DropTable(
                name: "trip_recordings");

            migrationBuilder.DropIndex(
                name: "ix_import_batch_items_trip_recording_id",
                table: "import_batch_items");

            migrationBuilder.DropIndex(
                name: "ix_import_batch_items_trip_recording_point_id",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "trip_recording_id",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "trip_recording_point_id",
                table: "import_batch_items");
        }
    }
}
