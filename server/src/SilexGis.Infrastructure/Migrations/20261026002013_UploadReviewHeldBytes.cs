using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UploadReviewHeldBytes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "held_upload_session_id",
                table: "import_batch_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_batch_items_held_upload_session_id",
                table: "import_batch_items",
                column: "held_upload_session_id",
                filter: "held_upload_session_id is not null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_import_batch_items_held_upload_session_id",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "held_upload_session_id",
                table: "import_batch_items");
        }
    }
}
