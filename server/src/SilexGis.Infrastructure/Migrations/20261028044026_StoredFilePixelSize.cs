using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StoredFilePixelSize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "pixel_height",
                table: "files",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pixel_width",
                table: "files",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pixel_height",
                table: "files");

            migrationBuilder.DropColumn(
                name: "pixel_width",
                table: "files");
        }
    }
}
