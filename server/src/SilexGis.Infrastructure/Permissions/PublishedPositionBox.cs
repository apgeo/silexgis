// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;

namespace SilexGis.Infrastructure.Permissions;

/// <summary>
/// A bounding box over features, tested against the position each row is published at.
/// </summary>
/// <remarks>
/// <para>
/// A caller who may not place a protected feature exactly is shown it on the protection grid, so
/// the honest box test is against that same snapped point: the row is in the answer exactly when the
/// point printed for it is inside the box. Tested against the stored coordinate instead, the box
/// becomes a probe — membership flips when a caller-chosen edge crosses the true position, so a
/// count or a list bisects a protected cave onto the map to any precision, while the snapped point
/// moves only in whole grid steps and gives nothing finer away. Exactly-placeable rows are narrowed
/// by their true geometry: for them position is not a secret.
/// </para>
/// <para>
/// Only a plain point has a published position at all; a protected line or polygon the caller may
/// not place is never inside any box, since nothing about where it is will be printed.
/// </para>
/// </remarks>
public static class PublishedPositionBox
{
    public static async Task<IQueryable<Feature>> NarrowAsync(
        IQueryable<Feature> query,
        AccessContext ctx,
        FeatureProtection protection,
        Polygon polygon,
        double gridMeters,
        CancellationToken ct)
    {
        var candidates = await query
            .Where(f => f.IsProtectedEffective)
            .Select(f => new { f.Id, f.Geom })
            .ToListAsync(ct);
        var placeable = candidates.Count == 0
            ? new HashSet<Guid>()
            : await protection.ExactViewIdsAsync(ctx, [.. candidates.Select(c => c.Id)], ct);

        var publishedInside = candidates
            .Where(c => !placeable.Contains(c.Id) && c.Geom is Point)
            .Where(c => polygon.Intersects(LocationProtection.Snap((Point)c.Geom!, gridMeters)))
            .Select(c => c.Id)
            .ToArray();

        return query.Where(f =>
            ((!f.IsProtectedEffective || placeable.Contains(f.Id)) && f.Geom != null && f.Geom.Intersects(polygon))
            || publishedInside.Contains(f.Id));
    }
}
