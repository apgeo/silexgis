using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MarkShippedChecklistSeeded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_seeded",
                table: "checklists",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // The seeder now recognises the shipped checklist by this mark rather than by its
            // title. An installation that already has it gets the mark on the oldest list still
            // carrying the shipped title, so the next start does not seed a second one. A list
            // renamed before this update is not recognised and a fresh one is seeded beside it;
            // no row is changed but the mark.
            migrationBuilder.Sql("""
                UPDATE checklists SET is_seeded = true
                WHERE id = (SELECT id FROM checklists WHERE title = 'Before a trip' ORDER BY created_at, id LIMIT 1);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_seeded",
                table: "checklists");
        }
    }
}
