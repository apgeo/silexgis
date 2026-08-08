using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PhotoDerivedFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "source",
                table: "import_batches",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<Guid>(
                name: "trip_log_id",
                table: "import_batches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attachment_ids",
                table: "import_batch_items",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<Guid>(
                name: "source_file_id",
                table: "import_batch_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "altitude_meters",
                table: "files",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "direction_degrees",
                table: "files",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "direction_is_magnetic",
                table: "files",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "position_dop",
                table: "files",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "position_source",
                table: "files",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            // Everything already placed was placed by reading the camera's own tags — that was
            // the only thing that could place a picture before now. Saying so explicitly matters
            // twice: the catch-up pass skips files that already have an answer, and a person
            // about to drag one of these onto the map is warned that they are replacing a fix
            // the camera recorded. Left at the default, both behaviours would be wrong.
            migrationBuilder.Sql(
                "update files set position_source = 1 where geom is not null and position_source = 0;");

            migrationBuilder.CreateTable(
                name: "photo_import_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_ids = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    options = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    decisions = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_photo_import_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_photo_import_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_import_batches_trip_log_id",
                table: "import_batches",
                column: "trip_log_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_batch_items_source_file_id",
                table: "import_batch_items",
                column: "source_file_id",
                filter: "source_file_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_photo_import_sessions_user_id",
                table: "photo_import_sessions",
                column: "user_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_import_batch_items_stored_files_source_file_id",
                table: "import_batch_items",
                column: "source_file_id",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_import_batches_trip_logs_trip_log_id",
                table: "import_batches",
                column: "trip_log_id",
                principalTable: "trip_logs",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_import_batch_items_stored_files_source_file_id",
                table: "import_batch_items");

            migrationBuilder.DropForeignKey(
                name: "fk_import_batches_trip_logs_trip_log_id",
                table: "import_batches");

            migrationBuilder.DropTable(
                name: "photo_import_sessions");

            migrationBuilder.DropIndex(
                name: "ix_import_batches_trip_log_id",
                table: "import_batches");

            migrationBuilder.DropIndex(
                name: "ix_import_batch_items_source_file_id",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "source",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "trip_log_id",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "attachment_ids",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "source_file_id",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "altitude_meters",
                table: "files");

            migrationBuilder.DropColumn(
                name: "direction_degrees",
                table: "files");

            migrationBuilder.DropColumn(
                name: "direction_is_magnetic",
                table: "files");

            migrationBuilder.DropColumn(
                name: "position_dop",
                table: "files");

            migrationBuilder.DropColumn(
                name: "position_source",
                table: "files");
        }
    }
}
