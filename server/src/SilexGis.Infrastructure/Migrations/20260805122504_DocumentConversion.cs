using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentConversion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "conversion",
                table: "files",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<Guid>(
                name: "converted_from_file_id",
                table: "files",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_files_converted_from_file_id",
                table: "files",
                column: "converted_from_file_id",
                unique: true,
                filter: "converted_from_file_id is not null");

            migrationBuilder.AddForeignKey(
                name: "fk_files_files_converted_from_file_id",
                table: "files",
                column: "converted_from_file_id",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_files_files_converted_from_file_id",
                table: "files");

            migrationBuilder.DropIndex(
                name: "ix_files_converted_from_file_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "conversion",
                table: "files");

            migrationBuilder.DropColumn(
                name: "converted_from_file_id",
                table: "files");
        }
    }
}
