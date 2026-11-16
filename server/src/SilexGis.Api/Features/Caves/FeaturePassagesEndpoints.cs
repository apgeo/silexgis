// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// Which caves have passage beneath a doline, a sinkhole or any outline, and how much rock there is
/// between the two — the collapse-doline question, and the same query read the other way as "which
/// passages run under this polygon".
///
/// <para>
/// <b>This is cave position data and is gated as such.</b> The feature and every cave must be
/// placeable exactly by the caller, and that gate runs inside the query, before the list is
/// ordered, counted or cut. Terrain Read is required as well, for the same reason the overburden
/// route requires it: every figure carries a ground height read out of the installation's rasters.
/// The ground is read once per listed cave, at the station of that cave's own profile nearest the
/// feature, so the number is one the cave's own panel shows.
/// </para>
/// </summary>
public static class FeaturePassagesEndpoints
{
    private const string FeatureNotFoundCode = "feature.not_found";
    private const string ForbiddenCode = "access.forbidden";

    public static RouteGroupBuilder MapFeaturePassagesEndpoints(this RouteGroupBuilder api)
    {
        api.MapGroup("/features").WithTags("Caves")
            .MapGet("/{id:guid}/passages-beneath", PassagesAsync)
            .WithValidation<FeaturePassagesRequest>()
            .WithSummary(
                "The caves whose passage lies beneath or near a doline, sinkhole or outline, with "
                + "the rock overhead and the plan offset. Requires Read on the Terrain domain; only "
                + "caves and features the caller may place exactly are considered.");

        return api;
    }

    private static async Task<Results<Ok<FeaturePassagesDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        PassagesAsync(
            [AsParameters] FeaturePassagesRequest request,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            IOptions<SpatialOptions> spatial,
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

        var distance = request.DistanceM ?? FeaturePassagesLimits.DefaultDistanceM;
        var connection = db.Database.GetDbConnection();

        // One answer for a feature that is missing, unreadable, not placeable or not a point or an
        // outline: a different one for each would say that a protected feature is here.
        var (subjectSql, subjectParameters) = FeaturePassagesSql.BuildSubject(ctx, request.Id);
        var subject = await connection.QuerySingleOrDefaultAsync<FeaturePassagesSubjectRow>(
            new CommandDefinition(subjectSql, subjectParameters, cancellationToken: ct));
        if (subject is null)
        {
            return ApiProblems.NotFound(FeatureNotFoundCode);
        }

        // Resolved even with nothing to measure: "no terrain built" and "terrain does not reach
        // here" are different answers.
        var coverage = await index.ActiveCoverageAsync(ct);

        // One more than is listed, so a cut is known to be a cut. The extra row is taken from the
        // already-gated set, so the flag cannot tell a reader about a cave they could not place.
        var (sql, parameters) = FeaturePassagesSql.BuildPassages(
            ctx,
            request.Id,
            distance,
            FeaturePassagesLimits.MaxCaves + 1,
            FeaturePassagesLimits.MaxCandidates,
            spatial.Value.WorkingSrid);
        var found = (await connection.QueryAsync<FeaturePassageRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct))).ToList();
        var truncated = found.Count > FeaturePassagesLimits.MaxCaves;
        var rows = found.Take(FeaturePassagesLimits.MaxCaves).ToList();

        var picks = new List<(FeaturePassageRow Row, SurveySegmentSet Set, OverburdenStation? Station)>(rows.Count);
        foreach (var row in rows)
        {
            var set = await SurveySegmentSource.ForCaveAsync(db, ctx, row.CaveId, ct);
            var (stations, _) = OverburdenProfile.Stations(SurveySegments.AsPassage(set.Segments));
            var nearest = OverburdenProfile.NearestStation(stations, row.ClosestLongitude, row.ClosestLatitude);
            picks.Add((row, set, nearest?.Station));
        }

        var asked = picks.Where(p => p.Station is not null).ToList();
        var ground = await sampler.SampleAsync(
            coverage,
            [.. asked.Select(p => new DemSamplePoint(p.Station!.Value.Longitude, p.Station.Value.Latitude))],
            ct);
        var groundByCave = asked
            .Select((p, i) => (p.Row.CaveId, Read: ground[i]))
            .ToDictionary(x => x.CaveId, x => x.Read);

        var passages = new List<FeaturePassageDto>(picks.Count);
        foreach (var (row, set, station) in picks)
        {
            DemSample? read = groundByCave.TryGetValue(row.CaveId, out var r) ? r : null;
            double? overburden = read?.ElevationM is { } groundM && station is { } st
                ? groundM - st.PassageAltitudeM
                : null;

            passages.Add(new FeaturePassageDto(
                row.CaveId,
                row.CaveName,
                row.LateralOffsetM,
                set.Basis,
                set.Basis == SurveySegmentBasis.SkeletonHeuristic,
                set.HasZ,
                station?.Longitude,
                station?.Latitude,
                station?.PassageAltitudeM,
                read?.Outcome,
                read?.ElevationM,
                overburden));
        }

        return TypedResults.Ok(new FeaturePassagesDto(
            request.Id,
            subject.Name,
            subject.IsPolygon,
            distance,
            coverage.Rasters.Count > 0,
            truncated,
            passages));
    }
}
