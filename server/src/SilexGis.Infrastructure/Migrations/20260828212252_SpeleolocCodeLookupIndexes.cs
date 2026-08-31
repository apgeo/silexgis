using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpeleolocCodeLookupIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The two codes a cave place carries from the device that surveyed it live in the
            // feature's property document, and resolving a printed one is an equality test on a
            // value inside that document, compared without regard to case. No index of any kind
            // stood over that column before this, so an unindexed lookup would read every
            // feature on the installation for every scan of a label.
            //
            // Written out rather than modelled: an index over an expression is not something the
            // object model can describe, and nothing maps these, which is also why no later
            // model change will propose dropping them. They are partial on the same expression
            // the lookup compares, so the planner can prove an equality on it implies the
            // predicate and use them — and so the index holds only the features that actually
            // carry a code, which is a small minority of them.
            migrationBuilder.Sql(
                """
                CREATE INDEX ix_features_speleoloc_qcri_lower
                    ON features (lower(properties->>'speleolocQcri'))
                    WHERE lower(properties->>'speleolocQcri') IS NOT NULL;
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX ix_features_speleoloc_pci_lower
                    ON features (lower(properties->>'speleolocPci'))
                    WHERE lower(properties->>'speleolocPci') IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_features_speleoloc_pci_lower;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_features_speleoloc_qcri_lower;");
        }
    }
}
