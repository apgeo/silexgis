// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Features.Map;

/// <summary>
/// The passage a map window holds, coloured by how much rock lies over it.
///
/// <para>
/// <b>This draws cave passage, so it is gated as the centreline layer is.</b> A centreline is the
/// cave's exact location; a cave is drawn here only when the caller may place it exactly, decided
/// by the same allow-list the centreline layer builds (visible centrelines in the window that
/// pass the exact-view rule) and then again by the cave-level rule the overburden route asks. A
/// cave that fails either is absent: no line, no slot in the cap, and no part of the count or the
/// truncation flag, which are computed over the gated set only.
/// </para>
/// <para>
/// Terrain Read is required, as for every route that reads ground heights, because each colour is
/// a ground height less a passage altitude. The cover is the one the cave's own overburden panel
/// shows at the same station, sampled on that panel's stations (fewer where several caves share
/// the budget), so it is a sampled figure, not the true cover between samples.
/// </para>
/// </summary>
public static class MapPassageDepthEndpoints
{
    private const string ForbiddenCode = "access.forbidden";

    public static RouteGroupBuilder MapMapPassageDepthEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/map/passage-depth", PassageDepthAsync)
            .WithTags("Map")
            .WithValidation<MapPassageDepthRequest>()
            .WithSummary(
                "Cave passage in a bbox, as lines coloured by the rock cover over them. Requires "
                + "Read on the Terrain domain; only caves the caller may place exactly are drawn.");
        return api;
    }

    private static async Task<Results<Ok<PassageDepthWindowDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        PassageDepthAsync(
            [AsParameters] MapPassageDepthRequest request,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            IAccessService access,
            FeatureProtection protection,
            TerrainRasterIndex index,
            IDemSampleService sampler,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Terrain, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden(ForbiddenCode);
        }

        if (MapEndpoints.WindowProblem(request.Bbox, out var box) is { } windowProblem)
        {
            return windowProblem;
        }

        var edges = PassageDepth.ClassEdgesM;
        var coverage = await index.ActiveCoverageAsync(ct);
        if (coverage.Rasters.Count == 0)
        {
            return TypedResults.Ok(new PassageDepthWindowDto(
                false, edges, [], 0, false, PassageDepthLimits.MaxCaves));
        }

        // The centreline layer's allow-list: only what passes is looked at again.
        var idsInView = await CenterlineMapSql.CenterlineIdsInViewAsync(db, ctx, box, ct);
        var exactViewIds = await protection.ExactViewIdsAsync(ctx, idsInView, ct);
        var eligible = idsInView.Where(exactViewIds.Contains).ToList();
        var caveIds = eligible.Count == 0
            ? []
            : await db.Centerlines.AsNoTracking()
                .Where(c => eligible.Contains(c.Id))
                .Select(c => c.CaveFeatureId)
                .Distinct()
                .OrderBy(id => id)
                .ToListAsync(ct);

        // The cave-level rule the overburden route asks, applied before anything is counted.
        var gated = new List<Guid>(caveIds.Count);
        foreach (var caveId in caveIds)
        {
            if (await SurveyModelAccess.MeasurableCaveAsync(db, access, protection, ctx, caveId, ct) is not null)
            {
                gated.Add(caveId);
            }
        }

        var truncated = gated.Count > PassageDepthLimits.MaxCaves;
        var shown = gated.Take(PassageDepthLimits.MaxCaves).ToList();
        var perCave = Math.Clamp(
            PassageDepthLimits.StationBudget / Math.Max(shown.Count, 1),
            PassageDepthLimits.MinStationsPerCave,
            OverburdenProfile.MaxStations);

        var profiles = new List<(Guid CaveId, IReadOnlyList<OverburdenStation> Stations, double LengthM, List<int> Asked)>();
        var points = new List<DemSamplePoint>();
        foreach (var caveId in shown)
        {
            var set = await SurveySegmentSource.ForCaveAsync(db, ctx, caveId, ct);
            var (stations, lengthM) = OverburdenProfile.Stations(SurveySegments.AsPassage(set.Segments), perCave);
            if (stations.Count < 2)
            {
                continue;
            }

            // Only the stations a drawn stretch can touch are read: those in the window, padded by
            // the longest join a stretch is allowed (one and a half station spacings).
            var padMetres = 1.5 * lengthM / (stations.Count - 1);
            var padLat = padMetres / 111_320d;
            var asked = new List<int>();
            for (var i = 0; i < stations.Count; i++)
            {
                var s = stations[i];
                var padLon = padLat / Math.Max(Math.Cos(s.Latitude * Math.PI / 180d), 0.05);
                if (s.Longitude >= box.West - padLon && s.Longitude <= box.East + padLon
                    && s.Latitude >= box.South - padLat && s.Latitude <= box.North + padLat)
                {
                    asked.Add(i);
                    points.Add(new DemSamplePoint(s.Longitude, s.Latitude));
                }
            }

            if (asked.Count > 0)
            {
                profiles.Add((caveId, stations, lengthM, asked));
            }
        }

        var ground = points.Count == 0 ? [] : await sampler.SampleAsync(coverage, points, ct);

        var lines = new List<PassageDepthLineDto>();
        var offset = 0;
        foreach (var (caveId, stations, lengthM, asked) in profiles)
        {
            // A station not asked about, or asked about and not read, is a gap and never nought.
            var cover = new double?[stations.Count];
            for (var k = 0; k < asked.Count; k++)
            {
                if (ground[offset + k].ElevationM is { } groundM)
                {
                    cover[asked[k]] = groundM - stations[asked[k]].PassageAltitudeM;
                }
            }

            offset += asked.Count;
            var runs = PassageDepth.Runs(stations, cover, lengthM);
            foreach (var byClass in runs.GroupBy(r => r.DepthClass).OrderBy(g => g.Key))
            {
                lines.Add(new PassageDepthLineDto(
                    caveId,
                    byClass.Key,
                    [.. byClass.Select(r => (IReadOnlyList<double[]>)[.. r.Points.Select(p => new[] { p.Longitude, p.Latitude })])]));
            }
        }

        return TypedResults.Ok(new PassageDepthWindowDto(
            true, edges, lines, profiles.Count, truncated, PassageDepthLimits.MaxCaves));
    }
}
