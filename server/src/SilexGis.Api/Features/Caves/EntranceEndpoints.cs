// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Caves;

public sealed record EntranceDto(
    Guid Id,
    Guid CaveId,
    string? Name,
    long EntranceTypeId,
    bool IsMain,
    GeoJsonPoint Geom,
    decimal? Altitude,
    string? Description,
    PositionQuality PositionQuality,
    DateOnly? SurveyedAt,
    bool ApproximateLocation);

public sealed record EntranceWriteRequest(
    string? Name,
    long EntranceTypeId,
    bool IsMain,
    GeoJsonPoint Geom,
    decimal? Altitude,
    string? Description,
    PositionQuality PositionQuality,
    DateOnly? SurveyedAt);

public sealed class EntranceWriteRequestValidator : AbstractValidator<EntranceWriteRequest>
{
    public EntranceWriteRequestValidator()
    {
        RuleFor(x => x.Name).MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.EntranceTypeId).GreaterThan(0);
        RuleFor(x => x.PositionQuality).IsInEnum();
        RuleFor(x => x.Geom).NotNull();
        RuleFor(x => x.Geom.Type).Equal("Point").When(x => x.Geom is not null);
        RuleFor(x => x.Geom.Coordinates).Must(c => c is { Length: 2 or 3 })
            .WithMessage("Coordinates must be [longitude, latitude] or [longitude, latitude, altitude].")
            .When(x => x.Geom is not null);
        RuleFor(x => x.Geom.Coordinates[0]).InclusiveBetween(-180, 180)
            .When(x => x.Geom?.Coordinates is { Length: >= 2 });
        RuleFor(x => x.Geom.Coordinates[1]).InclusiveBetween(-90, 90)
            .When(x => x.Geom?.Coordinates is { Length: >= 2 });
    }
}

/// <summary>
/// Entrances are features: the feature row carries the name, the description and the
/// entrance point (a PointZ whose Z mirrors the altitude), the subtype row the
/// entrance-specific attributes. An entrance is a child of its cave and has no access
/// control of its own — every operation here is authorized against the cave feature, and
/// every change to the entrance set goes through the feature write service, which owns the
/// cave's derived mirror (entrance count and representative point).
/// </summary>
public static class EntranceEndpoints
{
    public static RouteGroupBuilder MapEntranceEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/caves/{caveId:guid}/entrances", ListAsync)
            .WithTags("CaveEntrances")
            .WithSummary("Entrances of a cave; coordinates obfuscated without ViewExactLocation.");
        api.MapPost("/caves/{caveId:guid}/entrances", CreateAsync)
            .WithValidation<EntranceWriteRequest>()
            .WithTags("CaveEntrances")
            .WithSummary("Adds an entrance (Write on the cave).");
        api.MapPut("/cave-entrances/{id:guid}", UpdateAsync)
            .WithValidation<EntranceWriteRequest>()
            .WithTags("CaveEntrances")
            .WithSummary("Updates an entrance (Write on the cave).");
        api.MapDelete("/cave-entrances/{id:guid}", DeleteAsync)
            .WithTags("CaveEntrances")
            .WithSummary("Deletes an entrance (Write on the cave).");

        return api;
    }

    private static async Task<Results<Ok<List<EntranceDto>>, ProblemHttpResult>> ListAsync(
        Guid caveId,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        IOptions<AccessOptions> accessOptions,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var cave = await db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == caveId && f.Kind == FeatureKind.Cave, ct);
        if (cave is null || !(await access.DecideAsync(ctx, AccessAction.Read, cave, ct)).Allowed)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        // Readability is decided on the cave alone. Re-filtering the entrance rows would be
        // wrong, not merely redundant: ACL grants are keyed to the row they were made on, so
        // a caller who reads a private cave through a grant holds none on its entrances.
        var rows = await db.CaveEntrances.AsNoTracking()
            .Include(e => e.Feature)
            .Where(e => e.CaveFeatureId == caveId)
            .OrderByDescending(e => e.IsMain).ThenBy(e => e.Feature.CreatedAt)
            .ToListAsync(ct);

        var exact = await protection.ExactViewIdsAsync(ctx, [.. rows.Select(e => e.Id)], ct);
        return TypedResults.Ok(rows
            .Select(e => ToDto(e.Feature, e, exact.Contains(e.Id), accessOptions.Value.LocationGridMeters))
            .ToList());
    }

    private static async Task<Results<Created<EntranceDto>, ProblemHttpResult>> CreateAsync(
        Guid caveId,
        EntranceWriteRequest request,
        SilexGisDbContext db,
        FeatureWriteService writer,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IOptions<AccessOptions> accessOptions,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var cave = await db.Features.FirstOrDefaultAsync(f => f.Id == caveId && f.Kind == FeatureKind.Cave, ct);
        if (cave is null || !(await access.DecideAsync(ctx, AccessAction.Read, cave, ct)).Allowed)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, cave, ct)).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        // A cave's first entrance is its representative point, so it starts out as the main one.
        var isFirst = !await db.CaveEntrances.AnyAsync(e => e.CaveFeatureId == caveId, ct);
        var altitude = AltitudeOf(request);
        var feature = new Feature
        {
            Name = request.Name,
            Description = request.Description,
            Geom = ToPoint(request.Geom, altitude),
        };
        var entrance = new CaveEntrance
        {
            CaveFeatureId = caveId,
            EntranceTypeId = request.EntranceTypeId,
            IsMain = request.IsMain || isFirst,
            Altitude = altitude,
            PositionQuality = request.PositionQuality,
            SurveyedAt = request.SurveyedAt,
        };

        try
        {
            // Owner and caving-group binding default from the cave in the write service, and
            // read visibility cascades from it: an entrance is never more (or less) visible
            // than the cave it belongs to.
            await writer.CreateEntranceAsync(feature, entrance, ct);
            if (entrance.IsMain)
            {
                await writer.SetMainEntranceAsync(caveId, feature.Id, ct);
            }
        }
        catch (FeatureWriteException ex)
        {
            return ApiProblems.BadRequest(ex.Code, string.Join("; ", ex.Errors));
        }

        await db.SaveChangesAsync(ct);

        // Adding an entrance is unrestricted even for callers without exact-location access:
        // the coordinates are theirs, so echoing them back discloses nothing.
        return TypedResults.Created(
            $"/api/v1/cave-entrances/{feature.Id}",
            ToDto(feature, entrance, exact: true, accessOptions.Value.LocationGridMeters));
    }

    private static async Task<Results<Ok<EntranceDto>, ProblemHttpResult>> UpdateAsync(
        Guid id,
        EntranceWriteRequest request,
        HttpContext http,
        SilexGisDbContext db,
        FeatureWriteService writer,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        IOptions<AccessOptions> accessOptions,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var entrance = await db.CaveEntrances.Include(e => e.Feature).FirstOrDefaultAsync(e => e.Id == id, ct);
        var cave = entrance is null
            ? null
            : await db.Features.FirstOrDefaultAsync(f => f.Id == entrance.CaveFeatureId, ct);
        if (entrance is null || cave is null || !(await access.DecideAsync(ctx, AccessAction.Read, cave, ct)).Allowed)
        {
            return ApiProblems.NotFound("entrance.not_found");
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, cave, ct)).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        // The feature row is the aggregate's version token — one ETag for name, geometry and
        // the entrance attributes alike.
        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.Features, entrance.Id, ct) is { } stale)
        {
            return stale;
        }

        var canViewExact = (await protection.ExactViewIdsAsync(ctx, [entrance.Id], ct)).Contains(entrance.Id);
        var feature = entrance.Feature;
        feature.Name = request.Name;
        feature.Description = request.Description;
        entrance.EntranceTypeId = request.EntranceTypeId;
        entrance.SurveyedAt = request.SurveyedAt;

        // Write-path protection guard: an editor without exact-location access is shown only
        // snapped coordinates, no altitude and an unknown position quality, so a normal save
        // must not write that obfuscated echo back over the precise stored values.
        if (canViewExact)
        {
            entrance.Altitude = AltitudeOf(request);
            entrance.PositionQuality = request.PositionQuality;
            feature.Geom = ToPoint(request.Geom, entrance.Altitude);
        }

        if (request.IsMain && !entrance.IsMain)
        {
            // Promotion demotes the previous main entrance and refreshes the cave's mirror.
            await writer.SetMainEntranceAsync(cave.Id, entrance.Id, ct);
        }
        else
        {
            entrance.IsMain = request.IsMain;
            await writer.SyncCaveMirrorAsync(cave.Id, ct);
        }

        await db.SaveChangesAsync(ct);

        // Mask the response to the caller's own view (mirrors the list rule): after the guard
        // restored the precise stored values, returning them exact would leak them.
        return TypedResults.Ok(ToDto(feature, entrance, canViewExact, accessOptions.Value.LocationGridMeters));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        FeatureWriteService writer,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);

        // Deliberately untracked: the soft delete stamps the rows in the database, and a
        // tracked copy still carrying the pre-delete state would make the cave mirror below
        // count this entrance as present.
        var entrance = await db.CaveEntrances.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        var cave = entrance is null
            ? null
            : await db.Features.FirstOrDefaultAsync(f => f.Id == entrance.CaveFeatureId, ct);
        if (entrance is null || cave is null || !(await access.DecideAsync(ctx, AccessAction.Read, cave, ct)).Allowed)
        {
            return ApiProblems.NotFound("entrance.not_found");
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, cave, ct)).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.Features, entrance.Id, ct) is { } stale)
        {
            return stale;
        }

        await writer.SoftDeleteAsync(entrance.Id, ct);
        await writer.SyncCaveMirrorAsync(cave.Id, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>Altitude as given, or lifted from the body's third coordinate.</summary>
    private static decimal? AltitudeOf(EntranceWriteRequest request) =>
        request.Altitude
        ?? (request.Geom.Coordinates.Length == 3 ? (decimal)request.Geom.Coordinates[2] : null);

    /// <summary>
    /// The entrance point as stored on the feature row: Z mirrors the altitude whenever one
    /// is known, so the map payload and the attribute agree without a second lookup.
    /// </summary>
    private static Point ToPoint(GeoJsonPoint geom, decimal? altitude)
    {
        double x = geom.Coordinates[0];
        double y = geom.Coordinates[1];
        Coordinate coordinate = altitude is null ? new Coordinate(x, y) : new CoordinateZ(x, y, (double)altitude.Value);
        return new Point(coordinate) { SRID = 4326 };
    }

    // Entrance features always carry a point geometry (enforced by the write service and a
    // database check constraint), so the cast is total.
    private static EntranceDto ToDto(Feature feature, CaveEntrance entrance, bool exact, double gridMeters)
    {
        var point = (Point)feature.Geom!;
        return new EntranceDto(
            feature.Id,
            entrance.CaveFeatureId,
            feature.Name,
            entrance.EntranceTypeId,
            entrance.IsMain,
            GeoJsonPoint.From(exact ? point : LocationProtection.Snap(point, gridMeters)),
            exact ? entrance.Altitude : null,
            feature.Description,
            exact ? entrance.PositionQuality : PositionQuality.Unknown,
            entrance.SurveyedAt,
            ApproximateLocation: !exact);
    }
}
