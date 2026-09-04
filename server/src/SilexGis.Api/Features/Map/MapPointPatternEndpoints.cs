// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Map;

/// <summary>
/// Whether cave entrances in a window are arranged more thickly, or more evenly, than chance would
/// arrange them — a nearest-neighbour index with its test, and a Ripley curve with the envelope
/// repeated random scatters of the same points drew around it.
///
/// <para>
/// Both are computed over the entrances the caller may read <i>and</i> place exactly, and the
/// answer states that count and the ground it used. A protected entrance is absent rather than
/// approximated: its published position is a lattice intersection, and a spacing statistic built
/// from lattice intersections measures the lattice.
/// </para>
/// </summary>
public static class MapPointPatternEndpoints
{
    /// <summary>Refused: more entrances in the window than one answer compares every pair of.</summary>
    private const string TooManyPointsCode = "point_pattern.too_many_points";

    /// <summary>Refused: the window encloses no ground to measure over.</summary>
    private const string EmptyWindowCode = "point_pattern.empty_window";

    /// <summary>Refused: the named study area is not an outline, so it encloses no ground.</summary>
    private const string StudyAreaNotAnOutlineCode = "point_pattern.study_area_not_an_outline";

    /// <summary>Refused: points, radii and simulations are each permitted but their product is not.</summary>
    private const string TooMuchWorkCode = "point_pattern.too_much_work";

    public static RouteGroupBuilder MapMapPointPatternEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/map/point-pattern", PointPatternAsync)
            .WithTags("Map")
            .WithValidation<MapPointPatternRequest>()
            .WithSummary(
                "Clark-Evans nearest-neighbour index, Ripley's L with a seeded Monte-Carlo "
                + "envelope, and the rose of bearings between pairs of entrances, over the cave "
                + "entrances in a window that the caller may place exactly.");
        return api;
    }

    private static async Task<Results<Ok<PointPatternDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        PointPatternAsync(
            [AsParameters] MapPointPatternRequest request,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            IOptions<SpatialOptions> spatial,
            IOptions<AccessOptions> accessOptions,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Parsed and believed are two things. A box of four finite numbers can still span a
        // thousand globes, and everything below measures work per unit of window — cells to
        // enumerate, edges to segmentise — so an unchecked box is a way to ask for centuries of
        // database time in one request. The limits are checked before any of that is sized.
        if (!Bbox.TryParse(request.Bbox, out var box) || !box.IsWithinWorld)
        {
            return ApiProblems.BadRequest(
                "map.invalid_bbox",
                "bbox must be 'west,south,east,north' within -180..180 and -90..90, with west "
                + "less than east and south less than north.");
        }

        if (request.AreaId is { } areaId)
        {
            var area = await db.Features.AsNoTracking()
                .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                .Where(f => f.Id == areaId)
                .Select(f => new { f.Geom })
                .FirstOrDefaultAsync(ct);

            if (area is null)
            {
                return ApiProblems.NotFound("feature.not_found");
            }

            // Measuring inside an outline draws its edges, so the window is only available to a
            // caller who may place it exactly. To everybody else it answers as an outline that is
            // not there — the same answer as for one that does not exist, so the refusal itself
            // discloses nothing.
            if (!(await protection.ExactViewIdsAsync(ctx, [areaId], ct)).Contains(areaId))
            {
                return ApiProblems.NotFound("feature.not_found");
            }

            if (area.Geom is not (Polygon or MultiPolygon))
            {
                return ApiProblems.BadRequest(
                    StudyAreaNotAnOutlineCode,
                    "the study area must be an outline; a point or a line encloses no ground to "
                    + "measure a spacing over.");
            }
        }

        var workingSrid = spatial.Value.WorkingSrid;
        var windowRow = await PointPatternSql.WindowAsync(
            db, box.West, box.South, box.East, box.North, workingSrid, request.AreaId, ct);

        if (windowRow.WindowWkt is null || !(windowRow.AreaM2 > 0d))
        {
            return ApiProblems.BadRequest(
                EmptyWindowCode,
                "that window encloses no ground — the outline and the bbox do not overlap.");
        }

        var window = new WKTReader().Read(windowRow.WindowWkt);
        window.SRID = workingSrid;

        // One more than the ceiling, so a set too large to compute over is recognised as too large
        // rather than quietly measured as a statistic about its first thousand rows.
        var rows = await PointPatternSql.PointsAsync(
            db, ctx, box.West, box.South, box.East, box.North, workingSrid, request.AreaId,
            MapPointPatternLimits.MaxPoints + 1, ct);

        if (rows.Count > MapPointPatternLimits.MaxPoints)
        {
            return ApiProblems.BadRequest(
                TooManyPointsCode,
                $"more than {MapPointPatternLimits.MaxPoints} entrances in that window; both "
                + "statistics compare every pair. Ask about a smaller window.");
        }

        var points = rows.Select(r => new PlanarPoint(r.X, r.Y)).ToList();
        var studyAreaKm2 = window.Area / 1_000_000d;

        // Too few points is not an error: the count is the answer, and a caller whose window holds
        // three caves they can place has been told exactly what there was to tell.
        if (points.Count < MapPointPatternLimits.MinPoints)
        {
            return TypedResults.Ok(new PointPatternDto(
                points.Count, MapPointPatternLimits.MinPoints, studyAreaKm2, request.AreaId,
                null, null, null));
        }

        var clarkEvans = PointPattern.ClarkEvans(points, window.Area);

        var envelope = window.EnvelopeInternal;
        var maxRadius = request.MaxRadiusMetres
            ?? Math.Max(1d, Math.Min(envelope.Width, envelope.Height) / 4d);
        var steps = request.Steps ?? MapPointPatternLimits.DefaultSteps;
        var simulations = request.Simulations ?? MapPointPatternLimits.DefaultSimulations;
        var seed = request.Seed ?? MapPointPatternLimits.DefaultSeed;

        // Each of the three is inside its own limit by now, and that is not the same as the request
        // being inside any limit: the cost is their product, since every simulation walks every
        // pair at every radius. Refused with its own code rather than served, because the
        // alternative is one authenticated caller holding a core for minutes at a time.
        var work = MapPointPatternLimits.WorkUnits(points.Count, steps, simulations);
        if (work > MapPointPatternLimits.MaxWorkUnits)
        {
            return ApiProblems.BadRequest(
                TooMuchWorkCode,
                $"{points.Count} entrances at {steps} radii with {simulations} simulations is more "
                + "work than one request may ask for. Reduce the simulations, the radii, or the "
                + "window.");
        }

        var ripley = PointPattern.RipleyL(points, window, maxRadius, steps, simulations, seed, ct);

        // The directional half of the reading. Spacing statistics cannot see a direction at all, so
        // entrances strung along a fault and entrances in a round cluster are the same answer to
        // both of them; the bearings of the lines joining pairs are what tells those apart, and
        // they go through the same rose a cave's passage trends do rather than a second one.
        var maxSeparation = request.MaxPairSeparationMetres ?? maxRadius;
        var minSeparation = request.MinPairSeparationMetres ?? accessOptions.Value.LocationGridMeters;
        var pairs = PointPattern.PairAzimuths(points, maxSeparation, minSeparation, ct);
        var alignment = pairs.Count == 0
            ? null
            : new PairAlignmentDto(
                pairs.Count, minSeparation, maxSeparation, OrientationStatistics.Summarize(pairs));

        return TypedResults.Ok(new PointPatternDto(
            points.Count,
            MapPointPatternLimits.MinPoints,
            studyAreaKm2,
            request.AreaId,
            clarkEvans is null
                ? null
                : new ClarkEvansDto(
                    clarkEvans.MeanNearestNeighbourM,
                    clarkEvans.ExpectedMeanM,
                    clarkEvans.Index,
                    clarkEvans.ZScore,
                    clarkEvans.PValue),
            ripley is null
                ? null
                : new RipleyDto(
                    ripley.Simulations,
                    ripley.Seed,
                    maxRadius,
                    [.. ripley.Steps.Select(s => new RipleyStepDto(s.RadiusM, s.ObservedL, s.LowerL, s.UpperL))]),
            alignment));
    }
}
