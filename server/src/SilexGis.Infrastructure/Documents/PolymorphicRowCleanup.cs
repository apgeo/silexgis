// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Documents;

/// <summary>
/// Removes the rows that point at a record by kind and id with no foreign key to follow — files
/// attached to it, tags on it, and its place in a relation — when the record itself is deleted.
/// </summary>
/// <remarks>
/// <para>
/// Nothing removes these rows unless the delete path does: they name their target by a
/// discriminator and an id, so no cascade reaches them. Left behind they are exactly what the
/// integrity check reports as orphans, a tag on a record that no longer exists keeps counting
/// towards that tag's use, and an attachment row keeps a file listed against a page nothing can
/// open any more.
/// </para>
/// <para>
/// One body for every kind that carries such rows, because the rule is about the rows and not
/// about the record: a camp, an event and the occurrences of a series lose the same three kinds in
/// the same way, and stated once per caller the rule would eventually be stated differently. A trip
/// has rules of its own on top — the links typed with a trip role and the links anchored to a
/// moment of it go whole — and keeps its own delete for that reason.
/// </para>
/// <para>
/// A relation the record merely joined keeps whatever it still relates and goes only when one
/// member is left: an association with one end is a thing no surface offers and no later edit
/// would be accepted for. A directed relation the record was the distinguished member of goes
/// whatever is left of it: a directed link reads from its main member, and one with none is a
/// state the link rules refuse, so leaving it would leave a relation that renders on every other
/// member's panel and that no later edit — not even one appointing a new main — would be accepted
/// for. Remaining members cascade with the link.
/// </para>
/// <para>
/// Set-based deletes, deliberately: none of these rows is a rule about who may reach what, so
/// nothing here needs the change tracker. The access rules anchored on the record are the caller's
/// to withdraw, through the tracker, because that withdrawal is recorded.
/// </para>
/// </remarks>
public static class PolymorphicRowCleanup
{
    /// <summary>Removes every attachment, tagging and relation membership naming any of the records.</summary>
    public static async Task RemoveRowsPointingAtAsync(
        SilexGisDbContext db,
        AttachedEntityType entityType,
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken ct)
    {
        if (entityIds.Count == 0)
        {
            return;
        }

        await db.Attachments
            .Where(a => a.EntityType == entityType && a.EntityId != null && entityIds.Contains(a.EntityId.Value))
            .ExecuteDeleteAsync(ct);
        await db.Taggings
            .Where(t => t.EntityType == entityType && t.EntityId != null && entityIds.Contains(t.EntityId.Value))
            .ExecuteDeleteAsync(ct);

        var memberships = await db.ResLinkMembers
            .Where(m => m.EntityType == entityType && m.EntityId != null && entityIds.Contains(m.EntityId.Value))
            .Select(m => new { m.ResLinkId, m.IsMain })
            .ToListAsync(ct);
        if (memberships.Count == 0)
        {
            return;
        }

        var linkIds = memberships.Select(m => m.ResLinkId).Distinct().ToList();
        var mainOfIds = memberships.Where(m => m.IsMain).Select(m => m.ResLinkId).Distinct().ToList();
        var headlessIds = mainOfIds.Count == 0
            ? []
            : await db.ResLinks
                .Where(l => mainOfIds.Contains(l.Id)
                    && db.ResLinkRelationTypes.Any(t => t.Id == l.RelationTypeId && t.Directed))
                .Select(l => l.Id)
                .ToListAsync(ct);

        await db.ResLinkMembers
            .Where(m => m.EntityType == entityType && m.EntityId != null && entityIds.Contains(m.EntityId.Value))
            .ExecuteDeleteAsync(ct);
        await db.ResLinks
            .Where(l => headlessIds.Contains(l.Id)
                || (linkIds.Contains(l.Id) && db.ResLinkMembers.Count(m => m.ResLinkId == l.Id) < 2))
            .ExecuteDeleteAsync(ct);
    }
}
