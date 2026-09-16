using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <summary>
    /// A moment of a tracked trip became something a resource-link member can point at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Deliberately empty, and this note is why — so nobody has to prove it again.</b> The
    /// anchor kind is stored as a smallint through a value conversion, and the members table
    /// enumerates no values in any constraint: its payload check is the generic one ("kind zero
    /// carries no payload, every other kind carries one"), which the new kind satisfies without
    /// being named. The payload itself is jsonb, and the read that finds these members — the ones
    /// naming one trip — is already served by the index on (entity_type, entity_id, res_link_id).
    /// So the schema genuinely does not move, and an empty migration is the honest record of that.
    /// </para>
    /// <para>
    /// A partial index over the new kind was considered and left out: a trip carries tens of links,
    /// not thousands, and an index sized for a load nothing produces is a cost paid on every write
    /// for a read that was never slow.
    /// </para>
    /// <para>
    /// What is <em>not</em> optional is the value itself. Anchor-kind numbers are a stored schema
    /// contract: append only, never renumbered, because the rows already written mean whatever
    /// their number meant when it was written.
    /// </para>
    /// </remarks>
    public partial class TripMomentAnchor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
