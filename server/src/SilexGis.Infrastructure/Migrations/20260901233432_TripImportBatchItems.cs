using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TripImportBatchItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "trip_log_id",
                table: "import_batch_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_batch_items_trip_log_id",
                table: "import_batch_items",
                column: "trip_log_id",
                filter: "trip_log_id is not null");

            migrationBuilder.AddForeignKey(
                name: "fk_import_batch_items_trip_logs_trip_log_id",
                table: "import_batch_items",
                column: "trip_log_id",
                principalTable: "trip_logs",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_import_batch_items_trip_logs_trip_log_id",
                table: "import_batch_items");

            migrationBuilder.DropIndex(
                name: "ix_import_batch_items_trip_log_id",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "trip_log_id",
                table: "import_batch_items");
        }
    }
}
