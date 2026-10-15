// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Permissions;

/// <summary>
/// Removes access rows that name a row which no longer exists, and says how many went of each
/// kind. The safety net under <see cref="AccessAnchorCleanup"/>: what it finds is either older than
/// that rule or came through a path that bypassed it.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the rest of the integrity check, which reports and never repairs, this repairs — and
/// the difference is in what is being repaired. A stale ancestor cache or a broken version
/// sequence has more than one possible correct state, and choosing one silently would hide which
/// write path went wrong. A rule whose object is gone has exactly one: it grants and denies nothing
/// already, so removing it changes nobody's access. Left in place it cannot be repaired by anybody
/// else either — its object's permissions tab no longer exists — so reporting it alone would only
/// repeat the same report every night for ever.
/// </para>
/// <para>
/// The evidence a silent repair would destroy is kept on purpose. Each removed row goes through
/// the change tracker, so the audit trail holds the whole rule as it stood; each is logged with its
/// row id and the id it named; and the caller reports the count per kind as a failure of the pass,
/// so the delete path that left it behind is found rather than papered over.
/// </para>
/// <para>
/// It only removes what <see cref="AccessOrphanSql"/> can prove dangling against the tables
/// themselves, soft-deleted rows included, so a binned object never loses the rules its restore
/// needs. A kind of reference the list does not know is never searched, and so never removed.
/// </para>
/// </remarks>
public sealed class AccessOrphanSweep(SilexGisDbContext db, ILogger<AccessOrphanSweep> logger)
{
    /// <summary>Removes every orphaned access row and answers how many went, per arm name.</summary>
    public async Task<IReadOnlyDictionary<string, int>> RemoveAsync(CancellationToken ct = default)
    {
        var orphans = await AccessOrphanSql.FindAsync(db, ct);
        if (orphans.Count == 0)
        {
            return new Dictionary<string, int>();
        }

        var memberIds = orphans.Where(o => o.Arm.Column == AccessReferenceColumn.PermissionMember)
            .Select(o => o.RowId).ToArray();
        var entryIds = orphans.Where(o => o.Arm.Column != AccessReferenceColumn.PermissionMember)
            .Select(o => o.RowId).Distinct().ToArray();

        db.AccessEntries.RemoveRange(await db.AccessEntries.Where(e => entryIds.Contains(e.Id)).ToListAsync(ct));
        db.PermissionGroupMembers.RemoveRange(
            await db.PermissionGroupMembers.Where(m => memberIds.Contains(m.Id)).ToListAsync(ct));
        await db.SaveChangesAsync(ct);

        foreach (var orphan in orphans)
        {
            logger.LogWarning(
                "Removed access row {RowId} ({Arm}): it named {AnchorId}, which no longer exists",
                orphan.RowId, orphan.Arm.Name, orphan.AnchorId);
        }

        return orphans
            .GroupBy(o => o.Arm.Name)
            .ToDictionary(g => g.Key, g => g.Count());
    }
}
