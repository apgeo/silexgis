using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncUploadBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "sync_batch_id",
                table: "import_batches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sync_result",
                table: "import_batches",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_import_batches_user_sync_batch",
                table: "import_batches",
                columns: new[] { "confirmed_by_user_id", "sync_batch_id" },
                unique: true,
                filter: "sync_batch_id is not null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_import_batches_user_sync_batch",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "sync_batch_id",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "sync_result",
                table: "import_batches");
        }
    }
}
