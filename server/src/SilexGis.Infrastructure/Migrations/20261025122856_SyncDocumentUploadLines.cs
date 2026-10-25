using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncDocumentUploadLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "document_id",
                table: "import_batch_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "document_version_id",
                table: "import_batch_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_batch_items_document_id",
                table: "import_batch_items",
                column: "document_id",
                filter: "document_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_import_batch_items_document_version_id",
                table: "import_batch_items",
                column: "document_version_id",
                filter: "document_version_id is not null");

            migrationBuilder.AddForeignKey(
                name: "fk_import_batch_items_document_versions_document_version_id",
                table: "import_batch_items",
                column: "document_version_id",
                principalTable: "document_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_import_batch_items_documents_document_id",
                table: "import_batch_items",
                column: "document_id",
                principalTable: "documents",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_import_batch_items_document_versions_document_version_id",
                table: "import_batch_items");

            migrationBuilder.DropForeignKey(
                name: "fk_import_batch_items_documents_document_id",
                table: "import_batch_items");

            migrationBuilder.DropIndex(
                name: "ix_import_batch_items_document_id",
                table: "import_batch_items");

            migrationBuilder.DropIndex(
                name: "ix_import_batch_items_document_version_id",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "document_id",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "document_version_id",
                table: "import_batch_items");
        }
    }
}
