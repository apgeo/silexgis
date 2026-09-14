using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpeleolocTripImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "source",
                table: "trip_position_events",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<Guid>(
                name: "trip_position_event_id",
                table: "import_batch_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "speleoloc_import_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stored_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    options = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    decisions = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_speleoloc_import_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_speleoloc_import_sessions_stored_files_stored_file_id",
                        column: x => x.stored_file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_speleoloc_import_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_import_batch_items_trip_position_event_id",
                table: "import_batch_items",
                column: "trip_position_event_id",
                filter: "trip_position_event_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_speleoloc_import_sessions_stored_file_id_user_id",
                table: "speleoloc_import_sessions",
                columns: new[] { "stored_file_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_speleoloc_import_sessions_user_id",
                table: "speleoloc_import_sessions",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_import_batch_items_trip_position_events_trip_position_event",
                table: "import_batch_items",
                column: "trip_position_event_id",
                principalTable: "trip_position_events",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_import_batch_items_trip_position_events_trip_position_event",
                table: "import_batch_items");

            migrationBuilder.DropTable(
                name: "speleoloc_import_sessions");

            migrationBuilder.DropIndex(
                name: "ix_import_batch_items_trip_position_event_id",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "source",
                table: "trip_position_events");

            migrationBuilder.DropColumn(
                name: "trip_position_event_id",
                table: "import_batch_items");
        }
    }
}
