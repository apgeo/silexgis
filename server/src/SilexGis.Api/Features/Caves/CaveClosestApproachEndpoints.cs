// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// How close two caves come to touching, and which pairs in a piece of country come closest.
///
/// <para>
/// <b>This is the strictest disclosure in the application and the gate is built for that.</b> A
/// distance and a bearing between two named caves places each of them from the other. Anyone
/// holding one position — an open cave, a cave they own, a cave somebody published — would have
/// the second to the metre. So both caves must be readable <i>and</i> placeable, and a caller who
/// may read a cave but may not place it is given no distance at all. Not a rounded one, not one
/// snapped to a grid: nothing. A snapped answer is still an answer, and the same pair asked from
/// two vantage points would triangulate away whatever the snapping was hiding.
/// </para>
/// <para>
/// The refusal is spelled "no such cave" and never "you may not", for the reason the rest of this
/// area already gives: a 403 would confirm both that a hidden cave exists and that it has been
/// surveyed. A cave nobody may place, a cave that was never created, and a cave outside the
/// caller's reading rights all answer identically.
/// </para>
/// <para>
/// <b>Every figure is metric and none of it is computed from stored degrees.</b> Distance in three
/// dimensions is cartesian — it adds the ordinates as they are given — so over stored longitude,
/// latitude and metres of altitude it returns a number that is mostly the height difference and is
/// not a distance at all. The measuring happens in the installation's working system.
/// </para>
/// </summary>
public static class CaveClosestApproachEndpoints
{
    public static RouteGroupBuilder MapCaveClosestApproachEndpoints(this RouteGroupBuilder api)
    {
        var caves = api.MapGroup("/caves").WithTags("Caves");

        caves.MapGet("/{id:guid}/closest-approach/{other:guid}", PairAsync)
            .WithValidation<CaveClosestApproachRequest>()
            .WithSummary(
                "The shortest three-dimensional line between two caves' line work, with its "
                + "horizontal and vertical parts and its bearing. Withheld entirely unless the "
                + "caller may place both caves exactly.");

        caves.MapGet("/closest-approaches", TableAsync)
            .WithValidation<ClosestApproachTableRequest>()
            .WithSummary(
                "The closest pairs of caves in a bounding box, nearest first. Built only over "
                + "pairs both of whose caves the caller may place exactly.");

        return api;
    }

    private static async Task<Results<Ok<ClosestApproachDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        PairAsync(
            [AsParameters] CaveClosestApproachRequest request,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            IAccessService access,
            FeatureProtection protection,
            IOptions<SpatialOptions> spatial,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Both caves are put through the whole gate before anything is measured, and either one
        // failing ends the request the same way. Measuring first and withholding afterwards would
        // put the answer in memory next to a decision not to give it, which is one refactoring
        // away from leaking it.
        var first = await PlaceableCaveAsync(db, access, protection, ctx, request.Id, ct);
        var second = await PlaceableCaveAsync(db, access, protection, ctx, request.Other, ct);
        if (first is null || second is null)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        // Lowest id first, however the route was asked, so the same pair is the same answer.
        var (a, b) = first.Id < second.Id ? (first, second) : (second, first);

        var (sql, parameters) =
            ClosestApproachSql.BuildForPair(ctx, a.Id, b.Id, spatial.Value.WorkingSrid);
        var row = await db.Database.GetDbConnection().QuerySingleOrDefaultAsync<ClosestApproachRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        return TypedResults.Ok(row is null
            ? new ClosestApproachDto(
                a.Id, a.Name, b.Id, b.Name,
                ClosestApproachAbsence.NoLineWork,
                null, null, null, null, null, null)
            : ToDto(row));
    }

    private static async Task<Results<Ok<ClosestApproachTableDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        TableAsync(
            [AsParameters] ClosestApproachTableRequest request,
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

        var maxDistance = request.MaxDistanceM ?? ClosestApproachLimits.DefaultDistanceM;

        // No per-cave gate is run here and none is needed: the query itself admits only caves this
        // caller may read and place, on both the cave row and its line work, so a pair that would
        // be refused one at a time is never formed in the first place.
        var (sql, parameters) = ClosestApproachSql.BuildForArea(
            ctx,
            request.West,
            request.South,
            request.East,
            request.North,
            maxDistance,
            request.Limit ?? ClosestApproachLimits.DefaultRows,
            spatial.Value.WorkingSrid);

        var rows = await db.Database.GetDbConnection().QueryAsync<ClosestApproachRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        return TypedResults.Ok(new ClosestApproachTableDto([.. rows.Select(ToDto)], maxDistance));
    }

    private static ClosestApproachDto ToDto(ClosestApproachRow row) => row.HasAltitudes
        ? new ClosestApproachDto(
            row.CaveAId,
            row.CaveAName,
            row.CaveBId,
            row.CaveBName,
            ClosestApproachAbsence.None,
            row.DistanceM,
            row.HorizontalDistanceM,
            row.VerticalDistanceM,
            row.BearingDegrees,
            Point(row.FromLongitude, row.FromLatitude, row.FromAltitude),
            Point(row.ToLongitude, row.ToLatitude, row.ToAltitude))
        : new ClosestApproachDto(
            row.CaveAId,
            row.CaveAName,
            row.CaveBId,
            row.CaveBName,
            ClosestApproachAbsence.NoAltitudes,
            null, null, null, null, null, null);

    private static ClosestApproachPointDto? Point(double? longitude, double? latitude, double? altitude) =>
        longitude is { } x && latitude is { } y && altitude is { } z
            ? new ClosestApproachPointDto(x, y, z)
            : null;

    /// <summary>
    /// The cave one end of a measurement may be taken from, or null when it may not be — which
    /// covers a cave that does not exist, one the caller may not read, and one the caller may read
    /// but not place exactly. All three are one answer on purpose: the caller turns null into "no
    /// such cave", and distinguishing them would say which caves are being kept from whom.
    /// </summary>
    private static async Task<Feature?> PlaceableCaveAsync(
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        AccessContext ctx,
        Guid id,
        CancellationToken ct)
    {
        var feature = await db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id && f.Kind == FeatureKind.Cave, ct);

        return feature is not null
            && await SurveyModelAccess.VisibleAsync(access, protection, ctx, feature, ct)
                ? feature
                : null;
    }
}
