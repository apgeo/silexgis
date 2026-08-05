using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <summary>
    /// Removes the trigram extension. It was installed for a fuzzy-matching arm of document
    /// search that was never built: nothing declares an index with it, and no query uses its
    /// operators or its similarity function. An extension present but unused is a claim the
    /// database makes about itself that is not true, and the cost of undoing that claim is one
    /// statement now against a schema nobody has to explain later.
    /// </summary>
    /// <remarks>
    /// The scaffolded parts of this migration only ever add extensions, so the removal is
    /// written out. It is not cascaded on purpose: if an installation has built something of
    /// its own on trigrams in this database, the upgrade must stop and say so rather than
    /// quietly take that work with it.
    /// </remarks>
    public partial class DropUnusedTrigramExtension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:ltree", ",,")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:ltree", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:unaccent", ",,");

            migrationBuilder.Sql("DROP EXTENSION IF EXISTS pg_trgm;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:ltree", ",,")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:ltree", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:unaccent", ",,");
        }
    }
}
