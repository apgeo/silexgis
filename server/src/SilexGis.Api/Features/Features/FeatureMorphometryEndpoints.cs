// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Features;

/// <summary>
/// The measured shape of a drawn outline — a doline's area, how round it is, how long and how
/// wide, and which way it lies.
///
/// <para>
/// <b>Everything here is metric and none of it is computed from stored degrees.</b> An area in
/// degrees squared is not an area, and a bearing read off raw coordinate differences is out by the
/// cosine of the latitude. The measuring happens in the installation's working system, which is
/// configuration, so no query here spells a projection code itself.
/// </para>
/// <para>
/// <b>Who is in the answer.</b> An outline's shape is a position. Its centroid is one exactly, and
/// its size and alignment place it as surely as a coordinate once the outline is known. So the
/// whole answer is gated on exact placement rather than only the centroid, and a caller who may
/// read a feature but not place it is answered as though there were no such feature. That is the
/// same floor the display rules already set — a protected feature carrying anything but a plain
/// point is withheld entirely rather than snapped, because a shape leaks its own position — so
/// there is one rule and not a second, weaker one.
/// </para>
/// </summary>
public static class FeatureMorphometryEndpoints
{
    public static RouteGroupBuilder MapFeatureMorphometryEndpoints(this RouteGroupBuilder api)
    {
        var features = api.MapGroup("/features").WithTags("Features");

        features.MapGet("/morphometry", TableAsync)
            .WithValidation<FeatureMorphometryTableRequest>()
            .WithSummary(
                "The measured shape of every outline in a bounding box, largest first. Only "
                + "outlines the caller may place exactly are in it.");

        features.MapGet("/{id:guid}/morphometry", ForFeatureAsync)
            .WithValidation<FeatureMorphometryRequest>()
            .WithSummary(
                "Area, perimeter, circularity, axes, elongation, long-axis bearing and centroid "
                + "of one drawn outline, measured in metres.");

        return api;
    }

    private static async Task<Results<Ok<FeatureMorphometryDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        ForFeatureAsync(
            [AsParameters] FeatureMorphometryRequest request,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            IOptions<SpatialOptions> spatial,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var (sql, parameters) =
            PolygonMorphometrySql.BuildForFeature(ctx, request.Id, spatial.Value.WorkingSrid);
        var row = await db.Database.GetDbConnection().QuerySingleOrDefaultAsync<PolygonMorphometryRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        // No row covers four different situations on purpose — no such feature, one this caller
        // may not read, one they may read but not place, and one that is not an outline at all.
        // Telling them apart would say that a protected doline exists at this id.
        return row is null ? ApiProblems.NotFound("feature.not_found") : TypedResults.Ok(ToDto(row));
    }

    private static async Task<Results<Ok<FeatureMorphometryTableDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        TableAsync(
            [AsParameters] FeatureMorphometryTableRequest request,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            IOptions<SpatialOptions> spatial,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var (sql, parameters) = PolygonMorphometrySql.BuildForArea(
            ctx,
            request.West,
            request.South,
            request.East,
            request.North,
            request.FeatureTypeId,
            request.Limit ?? FeatureMorphometryLimits.DefaultRows,
            spatial.Value.WorkingSrid);

        var rows = await db.Database.GetDbConnection().QueryAsync<PolygonMorphometryRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        return TypedResults.Ok(new FeatureMorphometryTableDto([.. rows.Select(ToDto)]));
    }

    private static FeatureMorphometryDto ToDto(PolygonMorphometryRow row) => new(
        row.FeatureId,
        row.Name,
        row.GeometryValid,
        row.AreaM2,
        row.PerimeterM,
        row.Circularity,
        row.LongAxisM,
        row.ShortAxisM,
        row.Elongation,
        row.LongAxisAzimuthDegrees,
        row.CentroidLongitude,
        row.CentroidLatitude);
}
