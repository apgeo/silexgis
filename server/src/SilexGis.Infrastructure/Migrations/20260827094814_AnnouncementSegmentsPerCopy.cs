using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AnnouncementSegmentsPerCopy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Zero on every row that already exists, and zero is not "free": it means nobody
            // weighed this one, and what reads the column charges its own floor for such a row
            // rather than nothing. Backfilling a number here would be inventing what a message
            // whose text was never rendered would have cost.
            migrationBuilder.AddColumn<int>(
                name: "segments_per_copy",
                table: "notifications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "segments_per_copy",
                table: "caving_group_announcements",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "segments_per_copy",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "segments_per_copy",
                table: "caving_group_announcements");
        }
    }
}
