// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Common;

/// <summary>
/// Batch evaluation of <see cref="LocationProtection.ShouldRedactCaveLink"/> for
/// records that carry an optional cave link (surface features on lists/maps).
/// </summary>
public static class CaveLinkRedaction
{
    /// <summary>Returns the subset of <paramref name="caveIds"/> whose link must be hidden from the caller.</summary>
    public static async Task<HashSet<Guid>> RedactedCaveIdsAsync(
        SilexGisDbContext db, UserContext? user, IEnumerable<Guid> caveIds, CancellationToken ct)
    {
        var ids = caveIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        // Only protected caves can require redaction; the caller-specific check runs in memory.
        var caves = await db.Caves.AsNoTracking()
            .Where(c => ids.Contains(c.Id) && c.LocationProtected)
            .ToListAsync(ct);

        var exactGrants = user is null
            ? new HashSet<Guid>()
            : await new AclPermissionService(db).CaveExactLocationGrantsAsync(user, ct);
        return [.. caves
            .Where(c => LocationProtection.ShouldRedactCaveLink(
                user, c, exactGrants.Contains(c.Id) ? ObjectPermission.ViewExactLocation : ObjectPermission.None))
            .Select(c => c.Id)];
    }

    /// <summary>Single-cave variant for detail endpoints.</summary>
    public static async Task<bool> ShouldRedactAsync(
        SilexGisDbContext db, UserContext? user, Guid? caveId, CancellationToken ct)
    {
        if (caveId is null)
        {
            return false;
        }

        var redacted = await RedactedCaveIdsAsync(db, user, [caveId.Value], ct);
        return redacted.Count > 0;
    }
}
