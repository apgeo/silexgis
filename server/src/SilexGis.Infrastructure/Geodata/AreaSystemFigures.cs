// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// The vertical figures of a cave system, or of any area, taken across the caves filed under it.
/// </summary>
/// <param name="CaveCount">How many member caves the caller may place exactly and so contributed.
/// This counts what was measured, never what the area holds: the difference between the two would
/// say which caves are protected here.</param>
/// <param name="SurveyedCaveCount">How many of those carried line work with real altitudes.</param>
/// <param name="Truncated">True when more placeable caves exist than are measured here; the figures
/// then describe the first <see cref="AreaSystemFigures.MaxCaves"/> of them in a fixed order.</param>
/// <param name="IsApproximation">True when at least one cave's survey half came from the
/// shape-based reduction rather than from parsed survey legs.</param>
/// <param name="HasAltitudes">Whether any member's line work carried altitudes.</param>
/// <param name="Figures">The same summary a single cave gets, over the union of the members.</param>
/// <param name="SurveyHighestM">Highest surveyed point among the members, metres.</param>
/// <param name="SurveyLowestM">Lowest surveyed point among the members, metres.</param>
public sealed record AreaSystemFiguresResult(
    int CaveCount,
    int SurveyedCaveCount,
    bool Truncated,
    bool IsApproximation,
    bool HasAltitudes,
    CaveSystemFiguresSummary Figures,
    double? SurveyHighestM,
    double? SurveyLowestM);

/// <summary>
/// Gathers what the single-cave system figures take from one cave across every member cave of an
/// area, and hands the union to the same arithmetic.
///
/// <para>
/// <b>Membership is the declared hierarchy</b> — the caves somebody filed under the area — for the
/// reason the area statistics give: an outline placed over a cave nobody filed there would make the
/// system figures answer a question about geometry. <b>Only what the caller may place exactly
/// counts.</b> A member cave whose exact position is closed to the caller is dropped before any
/// figure, entrance count or survey extreme is taken, and so are its entrances; an entrance of a
/// placeable cave whose own branch is protected is dropped the same way. Every figure here is an
/// altitude, and an altitude places an entrance on a hillside as surely as a pair of degrees, so a
/// figure that moved when a closed cave was added would read that cave's height off the answer.
/// </para>
/// <para>
/// The resurgence level is the lowest spring altitude the caller may place under the same area,
/// gathered by the area reader under its own withhold-entirely rule.
/// </para>
/// </summary>
public static class AreaSystemFigures
{
    /// <summary>
    /// The most member caves one answer measures. Each cave's survey is a query of its own, and a
    /// region-sized area would turn one request into thousands. Caves are taken in id order, which
    /// does not depend on where any of them is.
    /// </summary>
    public const int MaxCaves = 300;

    /// <summary>
    /// The figures for the caves under <paramref name="areaFeatureId"/>. The caller has already
    /// decided that this caller may use the area; this reads nothing about the area itself.
    /// </summary>
    public static async Task<AreaSystemFiguresResult> ForAreaAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        Guid areaFeatureId,
        CancellationToken ct)
    {
        var visible = db.Features.AsNoTracking().VisibleTo(ctx, db.Features, db.FeatureSetMembers);

        // Member caves the caller may read; the exact-view walk runs on the protected ones only.
        var members = await (
            from feature in visible
            where feature.Kind == FeatureKind.Cave
                && db.FeatureAncestors.Any(a =>
                    a.AncestorId == areaFeatureId && a.FeatureId == feature.Id && a.FeatureId != a.AncestorId)
            orderby feature.Id
            select new { feature.Id, feature.IsProtectedEffective })
            .ToListAsync(ct);

        var placeableIds = await protection.PlaceableIdsAsync(
            ctx, [.. members.Select(m => (m.Id, m.IsProtectedEffective))], ct);

        var caveIds = members.Where(m => placeableIds.Contains(m.Id)).Select(m => m.Id).ToList();
        var truncated = caveIds.Count > MaxCaves;
        if (truncated)
        {
            caveIds = caveIds.Take(MaxCaves).ToList();
        }

        var entrances = caveIds.Count == 0
            ? []
            : await (
                from entrance in db.CaveEntrances.AsNoTracking()
                join feature in visible on entrance.Id equals feature.Id
                where caveIds.Contains(entrance.CaveFeatureId)
                select new { entrance.Id, entrance.Altitude, feature.IsProtectedEffective })
                .ToListAsync(ct);

        var entranceIds = await protection.PlaceableIdsAsync(
            ctx, [.. entrances.Select(e => (e.Id, e.IsProtectedEffective))], ct);
        var placeableEntrances = entrances.Where(e => entranceIds.Contains(e.Id)).ToList();

        double? highest = null;
        double? lowest = null;
        var surveyed = 0;
        var approximate = false;
        foreach (var caveId in caveIds)
        {
            var set = await SurveySegmentSource.ForCaveAsync(db, ctx, caveId, ct);
            var indices = CaveSurveyIndices.Compute(SurveySegments.AsPassage(set.Segments));
            if (!indices.HasAltitudes)
            {
                continue;
            }

            surveyed++;
            approximate |= set.Basis == SurveySegmentBasis.SkeletonHeuristic;
            if (indices.HighestZM is { } top)
            {
                highest = highest is { } h ? Math.Max(h, top) : top;
            }

            if (indices.LowestZM is { } bottom)
            {
                lowest = lowest is { } l ? Math.Min(l, bottom) : bottom;
            }
        }

        var springs = await AreaElevations.ForAreaAsync(db, protection, ctx, areaFeatureId, ct);

        var figures = CaveSystemFigures.Compute(
            [.. placeableEntrances.Where(e => e.Altitude is not null).Select(e => (double)e.Altitude!.Value)],
            placeableEntrances.Count,
            highest,
            lowest,
            springs is { SpringAltitudes.Count: > 0 } ? springs.SpringAltitudes[0] : null);

        return new AreaSystemFiguresResult(
            caveIds.Count,
            surveyed,
            truncated,
            approximate,
            surveyed > 0,
            figures,
            highest,
            lowest);
    }
}
