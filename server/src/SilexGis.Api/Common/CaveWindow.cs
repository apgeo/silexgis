// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Common;

/// <summary>
/// Which caves are "in view" of a window: the one rule the cave registry's list, its total and the
/// files exported from it share, so the list, its count and its file always hold the same caves.
/// </summary>
/// <remarks>
/// <para>
/// <b>Any entrance the reader is shown.</b> The map draws every entrance of a cave, so a cave whose
/// main entrance lies just outside the view while a second one is inside is on the screen — and a
/// list that called it out of view would contradict the map beside it. A cave is therefore in the
/// window when its own point is, or when any of its entrances the reader may read is. The cave's
/// own point is the cache of its main entrance and stays a test of its own, so a cave that has a
/// position but no entrance rows keeps the answer it always had.
/// </para>
/// <para>
/// <b>Decided on shown points only.</b> Each point is tested where this reader is shown it: an
/// unprotected entrance, or one the reader may place exactly, by its stored point; a protected one
/// they may not place by its snapped point — the window-membership rule's own question, asked of
/// its single home. An entrance the reader may not read at all is not among the candidates, so it
/// can neither bring its cave into a window nor, by its absence, say where it is.
/// </para>
/// </remarks>
public static class CaveWindow
{
    /// <summary>Narrows a query over cave feature rows to the caves in the window.</summary>
    public static async Task<IQueryable<Feature>> NarrowAsync(
        IQueryable<Feature> caves,
        SilexGisDbContext db,
        AccessContext ctx,
        Bbox box,
        double gridMeters,
        CancellationToken ct)
    {
        var polygon = box.ToPolygon();
        var protectedCaves = await WindowMembershipSql.ProtectedMembersAsync(
            db, ctx, box.West, box.South, box.East, box.North, gridMeters, [FeatureKind.Cave], ct);
        // The caves an entrance the reader is shown brings into the window, asked once over the
        // entrances' own index rather than per candidate cave.
        var byEntrance = await WindowMembershipSql.CavesWithShownEntrancesAsync(
            db, ctx, box.West, box.South, box.East, box.North, gridMeters, ct);

        return caves.Where(f =>
            (f.Geom != null
                && ((!f.IsProtectedEffective && f.Geom.Intersects(polygon)) || protectedCaves.Contains(f.Id)))
            || byEntrance.Contains(f.Id));
    }
}
