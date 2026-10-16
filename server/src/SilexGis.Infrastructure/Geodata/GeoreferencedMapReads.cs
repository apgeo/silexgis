// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// Which georeferenced maps a reader is shown: the one rule the map list and the map filter both
/// draw from.
/// </summary>
/// <remarks>
/// <para>
/// A map linked to a cave is drawn over the cave's ground, so its footprint is the cave's location
/// by another name. A reader who may not place that cave exactly is not shown the map at all — not
/// listed without its footprint, and not counted — so the cut is made here, before anybody counts
/// or pages, rather than over a page that has already been counted.
/// </para>
/// <para>
/// The exact-view rule cannot be written into a query, so it is asked once of every cave linked
/// from a map the walk admits, and what goes back into the query is the caves it admits — never
/// the ones it refused. The answer is read by a second query, and a cave map committed between
/// the two must be left out rather than let through because the refused set was taken before it
/// existed.
/// </para>
/// </remarks>
public static class GeoreferencedMapReads
{
    public static async Task<IQueryable<GeoreferencedMap>> ShownAsync(
        SilexGisDbContext db, FeatureProtection protection, AccessContext caller, CancellationToken ct)
    {
        var visible = db.GeoreferencedMaps.AsNoTracking().VisibleTo(caller, AccessDomain.GeoreferencedMaps);

        var linked = await visible
            .Where(m => m.CaveFeatureId != null)
            .Select(m => m.CaveFeatureId!.Value)
            .Distinct()
            .ToListAsync(ct);
        Guid[] exactIds = linked.Count == 0
            ? []
            : [.. await protection.ExactViewIdsAsync(caller, linked, ct)];

        return visible.Where(m => m.CaveFeatureId == null || exactIds.Contains(m.CaveFeatureId.Value));
    }
}
