// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// Entrance altitudes against the ground under them: a report that ranks the entrances whose recorded
/// altitude disagrees most with the elevation model, and an action that fills one that has no
/// altitude at all. Derived on every request; nothing is stored apart from what the fill writes.
///
/// <para>
/// <b>An entrance contributes only if the caller may place it exactly.</b> An altitude is a position
/// and the ground height beside it says which hillside an entrance is on, so every count, every
/// figure and the rank order are taken over the placeable set and nothing else. A report asked with
/// and without a protected entrance in the scope is identical to the digit; that is what keeps the
/// order and the totals from saying which entrances are withheld. No position is returned for any
/// row — the report names entrances, it does not place them.
/// </para>
/// <para>
/// <b>Reading elevation still needs the right to read elevation.</b> Every modelled altitude is a
/// ground height from the installation's prepared rasters, the same number the free-form probe
/// answers with under the same right, so a caller without that right is refused before anything is
/// looked up. The two gates are necessary together and neither is sufficient alone.
/// </para>
/// <para>
/// <b>The fill never overwrites.</b> An altitude somebody recorded in the field is evidence and an
/// elevation model is an estimate; the action fills an empty altitude and refuses to replace a
/// recorded one. Replacing is an ordinary edit of the entrance, made by a person who has typed the
/// number they mean.
/// </para>
/// </summary>
public static class EntranceAltitudeEndpoints
{
    private const string CaveNotFoundCode = "cave.not_found";
    private const string AreaNotFoundCode = "feature.not_found";
    private const string EntranceNotFoundCode = "entrance.not_found";
    private const string ForbiddenCode = "access.forbidden";

    /// <summary>How many rows the report carries; the counts are over the whole placeable set.</summary>
    public const int MaxRows = 200;

    /// <summary>
    /// Places read per call to the elevation reader. It serialises access to the raster library's
    /// handles, so the size of a call is the length of somebody else's wait.
    /// </summary>
    private const int SampleChunk = 500;

    public static RouteGroupBuilder MapEntranceAltitudeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGroup("/caves").WithTags("Caves")
            .MapGet("/{id:guid}/altitude-disagreement", CaveReportAsync)
            .WithValidation<AltitudeDisagreementRequest>()
            .WithSummary(
                "One cave's entrance altitudes against the elevation model. Requires Read on the "
                + "Terrain domain, and is withheld from a caller who may not place the cave exactly.");

        api.MapGroup("/features").WithTags("Features")
            .MapGet("/{id:guid}/altitude-disagreement", AreaReportAsync)
            .WithValidation<AltitudeDisagreementRequest>()
            .WithSummary(
                "The entrances under one area ranked by how far their recorded altitude is from the "
                + "elevation model. Requires Read on the Terrain domain and counts only entrances "
                + "this caller may place.");

        api.MapGroup("/cave-entrances").WithTags("CaveEntrances")
            .MapPost("/{id:guid}/fill-altitude", FillAsync)
            .WithValidation<FillEntranceAltitudeRequest>()
            .WithSummary(
                "Fills an empty entrance altitude from the elevation model (Write on the cave, "
                + "Read on the Terrain domain). Never replaces a recorded altitude.");

        return api;
    }

    private static async Task<Results<Ok<AltitudeDisagreementDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        CaveReportAsync(
            [AsParameters] AltitudeDisagreementRequest request,
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

        if (await SurveyModelAccess.MeasurableCaveAsync(db, access, protection, ctx, request.Id, ct) is null)
        {
            return ApiProblems.NotFound(CaveNotFoundCode);
        }

        // Readability is decided on the cave alone, as the entrance list does: an entrance carries
        // no grants of its own, so re-filtering its rows would drop those of a cave read through one.
        var entrances = db.CaveEntrances.AsNoTracking().Where(e => e.CaveFeatureId == request.Id);

        return TypedResults.Ok(await ReportAsync(
            db, protection, index, sampler, ctx, request.Id, entrances, db.Features.AsNoTracking(), ct));
    }

    private static async Task<Results<Ok<AltitudeDisagreementDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        AreaReportAsync(
            [AsParameters] AltitudeDisagreementRequest request,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
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

        var visible = db.Features.AsNoTracking().VisibleTo(ctx, db.Features, db.FeatureSetMembers);
        if (!await visible.AnyAsync(f => f.Id == request.Id, ct))
        {
            return ApiProblems.NotFound(AreaNotFoundCode);
        }

        // The subtree is walked from the containment closure inside the visibility filter, so a
        // cave kept from this reader contributes nothing and cannot be found by asking its parent.
        var entrances = db.CaveEntrances.AsNoTracking()
            .Where(e => visible.Any(f => f.Id == e.Id)
                && db.FeatureAncestors.Any(a => a.AncestorId == request.Id && a.FeatureId == e.Id));

        // The cave a row names is read through the same visibility filter: an entrance readable
        // through a grant of its own must not disclose the name or id of a cave the caller cannot read.
        var readableCaves = db.Features.AsNoTracking().VisibleTo(ctx, db.Features, db.FeatureSetMembers);

        return TypedResults.Ok(await ReportAsync(
            db, protection, index, sampler, ctx, request.Id, entrances, readableCaves, ct));
    }

    private static async Task<AltitudeDisagreementDto> ReportAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        TerrainRasterIndex index,
        IDemSampleService sampler,
        AccessContext ctx,
        Guid scopeId,
        IQueryable<CaveEntrance> entrances,
        IQueryable<Feature> readableCaves,
        CancellationToken ct)
    {
        var rows = await (
            from entrance in entrances
            join feature in db.Features.AsNoTracking() on entrance.Id equals feature.Id
            join cave in readableCaves on entrance.CaveFeatureId equals cave.Id
            where feature.Geom != null
            select new Row(
                entrance.Id,
                entrance.CaveFeatureId,
                cave.Name,
                feature.Name,
                feature.IsProtectedEffective,
                feature.Geom!,
                entrance.Altitude))
            .ToListAsync(ct);

        // The whole gate runs before a single pixel is read.
        var placeableIds = await protection.PlaceableIdsAsync(
            ctx, [.. rows.Select(r => (r.EntranceId, r.IsProtectedEffective))], ct);
        var placeable = rows.Where(r => placeableIds.Contains(r.EntranceId)).OrderBy(r => r.EntranceId).ToList();

        var coverage = await index.ActiveCoverageAsync(ct);

        // Sampled in chunks of the probe's own size, so one request never holds the elevation
        // reader for longer than a probe is allowed to.
        var compared = new List<(Row Row, EntranceAltitudeComparison Comparison)>(placeable.Count);
        foreach (var chunk in placeable.Chunk(SampleChunk))
        {
            var samples = await sampler.SampleAsync(
                coverage,
                [.. chunk.Select(r => new DemSamplePoint(r.Geom.Centroid.X, r.Geom.Centroid.Y))],
                ct);
            for (var i = 0; i < chunk.Length; i++)
            {
                compared.Add((chunk[i], EntranceAltitudes.Compare(chunk[i].AltitudeM, samples[i].ElevationM)));
            }
        }

        // Widest gap first, ties by id: the order depends on the placeable set and on nothing else.
        var ranked = compared
            .OrderByDescending(c => c.Comparison.AbsoluteDifferenceM is not null)
            .ThenByDescending(c => c.Comparison.AbsoluteDifferenceM ?? 0d)
            .ThenBy(c => c.Row.EntranceId)
            .ToList();

        var dtos = ranked.Take(MaxRows).Select(c => new EntranceAltitudeRowDto(
            c.Row.EntranceId,
            c.Row.CaveId,
            c.Row.CaveName,
            c.Row.EntranceName,
            c.Comparison.RecordedM,
            c.Comparison.ModelledM,
            c.Comparison.DifferenceM,
            c.Comparison.Verdict,
            c.Comparison.Verdict == EntranceAltitudeVerdict.NotRecorded && c.Comparison.ModelledM is not null))
            .ToList();

        return new AltitudeDisagreementDto(
            scopeId,
            coverage.Rasters.Count > 0,
            placeable.Count,
            compared.Count(c => c.Comparison.DifferenceM is not null),
            compared.Count(c => c.Comparison.Disagrees),
            compared.Count(c => c.Comparison.Verdict == EntranceAltitudeVerdict.NotRecorded),
            compared.Count(c => c.Comparison.Verdict == EntranceAltitudeVerdict.NotModelled),
            EntranceAltitudes.ToleranceM,
            ranked.Count > MaxRows,
            dtos);
    }

    private static async Task<Results<Ok<EntranceDto>, UnauthorizedHttpResult, ProblemHttpResult>> FillAsync(
        [AsParameters] FillEntranceAltitudeRequest request,
        HttpContext http,
        SilexGisDbContext db,
        FeatureWriteService writer,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        IOptions<AccessOptions> accessOptions,
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

        var entrance = await db.CaveEntrances.Include(e => e.Feature)
            .FirstOrDefaultAsync(e => e.Id == request.Id, ct);
        var cave = entrance is null
            ? null
            : await db.Features.FirstOrDefaultAsync(f => f.Id == entrance.CaveFeatureId, ct);
        if (entrance is null || cave is null
            || !(await access.DecideAsync(ctx, AccessAction.Read, cave, ct)).Allowed)
        {
            return ApiProblems.NotFound(EntranceNotFoundCode);
        }

        // An entrance the caller may read but not place is answered as absent: its ground height
        // would be a statement about where it is, and a refusal that named the reason would say
        // that it is protected.
        if (!(await protection.ExactViewIdsAsync(ctx, [entrance.Id], ct)).Contains(entrance.Id))
        {
            return ApiProblems.NotFound(EntranceNotFoundCode);
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, cave, ct)).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.Features, entrance.Id, ct) is { } stale)
        {
            return stale;
        }

        if (entrance.Altitude is not null)
        {
            return ApiProblems.Conflict(
                "entrance.altitude_recorded",
                "This entrance already has a recorded altitude, and the elevation model never replaces one. "
                + "Edit the entrance to change it.");
        }

        var feature = entrance.Feature;
        var point = (Point)feature.Geom!;
        var coverage = await index.ActiveCoverageAsync(ct);
        var read = (await sampler.SampleAsync(coverage, [new DemSamplePoint(point.X, point.Y)], ct))[0];
        if (read.ElevationM is not { } groundM || !double.IsFinite(groundM))
        {
            return ApiProblems.Conflict(
                "entrance.altitude_unavailable",
                "The elevation data has no ground height at this entrance.");
        }

        // One decimal: the model is not good to more, and a fill that looks surveyed to the
        // centimetre would be claiming a precision it does not have.
        var altitude = Math.Round((decimal)groundM, 1, MidpointRounding.AwayFromZero);

        // The read above can wait behind other elevation requests, and an edit of this entrance may
        // be saved meanwhile. The row is read again and the check repeated against what is stored
        // now, so a recorded altitude or a moved position is never overwritten by a stale fill.
        await db.Entry(entrance).ReloadAsync(ct);
        await db.Entry(feature).ReloadAsync(ct);
        if (entrance.Altitude is not null)
        {
            return ApiProblems.Conflict(
                "entrance.altitude_recorded",
                "This entrance already has a recorded altitude, and the elevation model never replaces one. "
                + "Edit the entrance to change it.");
        }

        if (feature.Geom is not Point current || current.X != point.X || current.Y != point.Y)
        {
            return ApiProblems.Conflict(
                "entrance.moved",
                "The entrance was moved while its ground height was being read. Try the fill again.");
        }

        entrance.Altitude = altitude;
        feature.Geom = new Point(new CoordinateZ(current.X, current.Y, (double)altitude)) { SRID = 4326 };

        await writer.SyncCaveMirrorAsync(cave.Id, ct);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(EntranceEndpoints.ToDto(
            feature, entrance, exact: true, accessOptions.Value.LocationGridMeters));
    }

    private sealed record Row(
        Guid EntranceId,
        Guid CaveId,
        string? CaveName,
        string? EntranceName,
        bool IsProtectedEffective,
        Geometry Geom,
        decimal? AltitudeM);
}
