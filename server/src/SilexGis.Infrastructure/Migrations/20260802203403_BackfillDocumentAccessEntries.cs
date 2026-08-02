using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillDocumentAccessEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Uploading is now a Create right in the documents domain (19) rather than in
            // the file-store domain (5). The seeder cannot deliver that on its own: it is
            // create-only by slug, so a database whose well-known groups already exist
            // never gains an entry for a domain introduced afterwards — and every editor
            // and administrator on such an installation would silently lose the upload.
            //
            // A backfill here cannot undo an operator's policy, because at the moment it
            // runs no policy about documents can exist: the domain is new, so nothing has
            // ever been written against it. That is why this is a one-shot data migration
            // rather than a reconciliation pass in the seeder, which would re-add the entry
            // on every restart and so overrule a deliberate removal.
            //
            // Each well-known group gets over documents exactly what it holds over the file
            // store domain-wide — the rights that decided uploads until now — which lands an
            // upgraded installation on the same rules a freshly seeded one gets. Groups an
            // operator authored are left alone: they never asked for the new domain, and
            // failing closed there refuses an upload rather than disclosing anything.
            // Anything already written against documents means the question has been
            // answered for that group, so it is skipped entirely.
            migrationBuilder.Sql(
                """
                INSERT INTO access_entries (
                    permission_group_id, effect, domain, actions, scope_kind,
                    granted_by, created_at, updated_at)
                SELECT store.permission_group_id, store.effect, 19, store.actions, 0,
                       store.granted_by, now(), now()
                FROM access_entries store
                JOIN permission_groups g ON g.id = store.permission_group_id
                WHERE g.slug IN ('administrators', 'editors', 'reviewers')
                  AND store.domain = 5
                  AND store.scope_kind = 0
                  AND NOT EXISTS (
                      SELECT 1 FROM access_entries existing
                      WHERE existing.permission_group_id = store.permission_group_id
                        AND existing.domain = 19);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty. The rows this adds are ordinary, editable access entries
            // that an operator may since have adjusted or come to depend on, and nothing
            // distinguishes them from ones written by hand — so reverting would revoke
            // rights rather than restore a shape. They are also harmless to leave in place:
            // a domain value the older code does not know is inert everywhere it is read.
        }
    }
}
