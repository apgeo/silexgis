// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// The entrance altitudes under one area, as the caller is allowed to know them.
/// </summary>
/// <param name="Altitudes">Every entrance altitude the caller may place, ascending.</param>
/// <param name="SpringAltitudes">The subset of those that belong to a spring cave, ascending and
/// without repeats. They are the heights water leaves the massif at, which is what a reader sets
/// the rest of the distribution against.</param>
/// <param name="EntranceCount">How many entrances contributed. This counts what was measured, and
/// what was measured is what the caller may place — it is deliberately not a count of the entrances
/// in the area, because the difference between the two is a statement about which caves are
/// protected here.</param>
public sealed record AreaEntranceAltitudes(
    IReadOnlyList<double> Altitudes,
    IReadOnlyList<double> SpringAltitudes,
    int EntranceCount);

/// <summary>
/// Reads the altitudes of the cave entrances that sit under one area of the containment hierarchy.
///
/// <para>
/// <b>An altitude is a coordinate.</b> It places an entrance on a hillside as surely as a pair of
/// degrees does, and a histogram of altitudes is observable one bin at a time: a reader who can see
/// a bin appear when a cave is added has read that cave's height off the answer. So an entrance
/// whose position is closed to the caller contributes to <em>nothing</em> here — not to a bin, not
/// to a count, not to the total the fractions are taken over. It is dropped before any arithmetic
/// runs, which is the only policy under which the same question asked with and without a protected
/// entrance gives the same answer.
/// </para>
/// <para>
/// This is the withhold-entirely rule the shape and line-work queries already use, rather than the
/// snap-into-an-aggregate rule the map clusters use. Snapping has no meaning for a scalar height:
/// there is no grid to round it onto that would not still say which contour the entrance sits on.
/// </para>
/// <para>
/// <b>The area is resolved to rows, never to a filter word.</b> The caller names an area by id and
/// receives the entrances beneath it that they may already see; the subtree is walked inside the
/// visibility filter, so a cave kept from this reader contributes nothing and cannot be detected by
/// asking about its parent. The area itself must be readable or the whole question is refused.
/// </para>
/// </summary>
public static class AreaElevations
{
    /// <summary>
    /// The entrance altitudes under <paramref name="areaFeatureId"/>, or null when the area itself
    /// is not readable by this caller — which covers an area that does not exist and one being kept
    /// from them, on purpose and identically.
    /// </summary>
    public static async Task<AreaEntranceAltitudes?> ForAreaAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        Guid areaFeatureId,
        CancellationToken ct)
    {
        var visible = db.Features.AsNoTracking().VisibleTo(ctx, db.Features, db.FeatureSetMembers);

        if (!await visible.AnyAsync(f => f.Id == areaFeatureId, ct))
        {
            return null;
        }

        var springTypeId = await db.CaveTypes.AsNoTracking()
            .Where(t => t.Code == CaveTypeSeeds.SpringCave)
            .Select(t => (long?)t.Id)
            .FirstOrDefaultAsync(ct);

        // The subtree is walked from the containment closure at read time rather than from a
        // pointer written when the entrance was created: it covers any depth and it follows a cave
        // being re-parented immediately. Descendants this caller may not read never enter the join,
        // so a private cave cannot be detected through the area that contains it.
        var rows = await (
            from entrance in db.CaveEntrances.AsNoTracking()
            join feature in visible on entrance.Id equals feature.Id
            join cave in db.Caves.AsNoTracking() on entrance.CaveFeatureId equals cave.Id
            where entrance.Altitude != null
                && db.FeatureAncestors.Any(a =>
                    a.AncestorId == areaFeatureId && a.FeatureId == entrance.Id)
            select new Row(
                feature.Id,
                feature.IsProtectedEffective,
                entrance.Altitude!.Value,
                springTypeId != null && cave.CaveTypeId == springTypeId))
            .ToListAsync(ct);

        // Only the protected rows are put to the access walk; the flag is a stored column and the
        // walk is the expensive half.
        var exact = await protection.ExactViewIdsAsync(
            ctx, [.. rows.Where(r => r.IsProtectedEffective).Select(r => r.FeatureId)], ct);

        var placeable = rows
            .Where(r => !r.IsProtectedEffective || exact.Contains(r.FeatureId))
            .ToList();

        return new AreaEntranceAltitudes(
            [.. placeable.Select(r => (double)r.AltitudeM).Order()],
            [.. placeable.Where(r => r.IsSpring).Select(r => (double)r.AltitudeM).Distinct().Order()],
            placeable.Count);
    }

    private sealed record Row(Guid FeatureId, bool IsProtectedEffective, decimal AltitudeM, bool IsSpring);
}
