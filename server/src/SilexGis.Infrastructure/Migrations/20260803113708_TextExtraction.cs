using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TextExtraction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "text_extraction",
                table: "files",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<string>(
                name: "text_extraction_error",
                table: "files",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "extractor",
                table: "document_pages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "extractor_version",
                table: "document_pages",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_files_text_extraction",
                table: "files",
                column: "text_extraction");

            // Every file that was already stored gets the resting state above, which says the
            // format carries no text at all — true of a photograph and false of every document
            // an installation has been collecting until now, and it is a sentence shown to
            // whoever opens the document. Only the readers know which formats are which, so
            // rather than restate that list in SQL this queues the one sweep that asks them,
            // and it queues it once: the sweep is idempotent, so a database that has already
            // been swept simply finds nothing to do.
            migrationBuilder.Sql(
                """
                INSERT INTO processing_jobs (kind, payload, status, attempts, created_at)
                VALUES ('text-extraction-backfill', '{}'::jsonb, 0, 0, now());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_files_text_extraction",
                table: "files");

            migrationBuilder.DropColumn(
                name: "text_extraction",
                table: "files");

            migrationBuilder.DropColumn(
                name: "text_extraction_error",
                table: "files");

            migrationBuilder.DropColumn(
                name: "extractor",
                table: "document_pages");

            migrationBuilder.DropColumn(
                name: "extractor_version",
                table: "document_pages");
        }
    }
}
