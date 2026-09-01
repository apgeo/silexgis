using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpeologieCatalogueIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A cave taken from the Romanian community catalogue keeps that catalogue's own
            // identifier in its property document, and two questions are asked of it constantly:
            // has this entry already been imported, and which cave did it become. Both are an
            // equality test on a value inside a jsonb column, and without an index they are a
            // scan of every feature in the installation — once per row of every search result.
            //
            // Written out rather than modelled, because an index over an expression is not
            // something the object model can describe; nothing maps this key, so no later model
            // change will propose dropping it. It is partial on the same expression the lookup
            // compares, so the planner can prove an equality on that expression implies the
            // predicate and use the index — and so the index holds only the caves that came from
            // the catalogue rather than every feature on the installation.
            //
            // The expression is repeated verbatim from the query that uses it. An expression
            // index that stops matching its query does not fail; it silently stops being used.
            migrationBuilder.Sql(
                """
                CREATE INDEX ix_features_speologie_id
                    ON features ((properties->>'speologieId'))
                    WHERE (properties->>'speologieId') IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_features_speologie_id;");
        }
    }
}
