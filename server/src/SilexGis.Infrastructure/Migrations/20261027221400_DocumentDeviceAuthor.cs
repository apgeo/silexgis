using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentDeviceAuthor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "created_by_device_user_id",
                table: "documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_documents_created_by_device_user_id",
                table: "documents",
                column: "created_by_device_user_id",
                filter: "created_by_device_user_id is not null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_documents_created_by_device_user_id",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "created_by_device_user_id",
                table: "documents");
        }
    }
}
