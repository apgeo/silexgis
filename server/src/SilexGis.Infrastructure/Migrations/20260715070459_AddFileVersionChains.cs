using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFileVersionChains : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "version_number",
                table: "files",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // Add nullable, backfill each existing file as the head of its own single-version
            // chain (group id = its own id), then enforce NOT NULL — otherwise every existing row
            // would share the zero-guid group and collide on the unique index below.
            migrationBuilder.AddColumn<Guid>(
                name: "version_group_id",
                table: "files",
                type: "uuid",
                nullable: true);
            migrationBuilder.Sql("UPDATE files SET version_group_id = id WHERE version_group_id IS NULL;");
            migrationBuilder.Sql("ALTER TABLE files ALTER COLUMN version_group_id SET NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "ix_files_version_group_id_version_number",
                table: "files",
                columns: new[] { "version_group_id", "version_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_files_version_group_id_version_number",
                table: "files");

            migrationBuilder.DropColumn(
                name: "version_group_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "version_number",
                table: "files");
        }
    }
}
