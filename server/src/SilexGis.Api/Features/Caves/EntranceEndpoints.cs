// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Permissions;
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
/// Entrances inherit the cave's access control: every operation
/// checks the parent cave. Derived cave fields (entrance_count, main_geom) are maintained
/// here — the single write path for entrances.
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
        IUserContextAccessor userAccessor,
        IOptions<AccessOptions> access,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var cave = await db.Caves.AsNoTracking().FirstOrDefaultAsync(c => c.Id == caveId, ct);
        if (cave is null || !PermissionEvaluator.Can(user, cave, ObjectPermission.Read))
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        var exact = LocationProtection.CanViewExactLocation(user, cave);
        var entrances = await db.CaveEntrances.AsNoTracking()
            .Where(e => e.CaveId == caveId)
            .OrderByDescending(e => e.IsMain).ThenBy(e => e.CreatedAt)
            .ToListAsync(ct);

        return TypedResults.Ok(entrances
            .Select(e => ToDto(e, exact, access.Value.LocationGridMeters))
            .ToList());
    }

    private static async Task<Results<Created<EntranceDto>, ProblemHttpResult>> CreateAsync(
        Guid caveId,
        EntranceWriteRequest request,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        IOptions<AccessOptions> access,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var cave = await db.Caves.FirstOrDefaultAsync(c => c.Id == caveId, ct);
        if (cave is null || user is null || !PermissionEvaluator.Can(user, cave, ObjectPermission.Read))
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        if (!PermissionEvaluator.Can(user, cave, ObjectPermission.Write))
        {
            return ApiProblems.Forbidden();
        }

        var entrance = new CaveEntrance
        {
            CaveId = caveId,
            EntranceTypeId = request.EntranceTypeId,
            Geom = request.Geom.ToPoint(),
        };
        Apply(request, entrance);
        db.CaveEntrances.Add(entrance);

        await RecomputeDerivedAsync(db, cave, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created(
            $"/api/v1/cave-entrances/{entrance.Id}",
            ToDto(entrance, exact: true, access.Value.LocationGridMeters));
    }

    private static async Task<Results<Ok<EntranceDto>, ProblemHttpResult>> UpdateAsync(
        Guid id,
        EntranceWriteRequest request,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        IOptions<AccessOptions> access,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var entrance = await db.CaveEntrances.FirstOrDefaultAsync(e => e.Id == id, ct);
        var cave = entrance is null ? null : await db.Caves.FirstOrDefaultAsync(c => c.Id == entrance.CaveId, ct);
        if (entrance is null || cave is null || !PermissionEvaluator.Can(user, cave, ObjectPermission.Read))
        {
            return ApiProblems.NotFound("entrance.not_found");
        }

        if (!PermissionEvaluator.Can(user, cave, ObjectPermission.Write))
        {
            return ApiProblems.Forbidden();
        }

        Apply(request, entrance);
        entrance.Geom = request.Geom.ToPoint();

        await RecomputeDerivedAsync(db, cave, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToDto(entrance, exact: true, access.Value.LocationGridMeters));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var entrance = await db.CaveEntrances.FirstOrDefaultAsync(e => e.Id == id, ct);
        var cave = entrance is null ? null : await db.Caves.FirstOrDefaultAsync(c => c.Id == entrance.CaveId, ct);
        if (entrance is null || cave is null || !PermissionEvaluator.Can(user, cave, ObjectPermission.Read))
        {
            return ApiProblems.NotFound("entrance.not_found");
        }

        if (!PermissionEvaluator.Can(user, cave, ObjectPermission.Write))
        {
            return ApiProblems.Forbidden();
        }

        db.CaveEntrances.Remove(entrance);
        await RecomputeDerivedAsync(db, cave, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static void Apply(EntranceWriteRequest request, CaveEntrance entrance)
    {
        entrance.Name = request.Name;
        entrance.EntranceTypeId = request.EntranceTypeId;
        entrance.IsMain = request.IsMain;
        entrance.Altitude = request.Altitude
            ?? (request.Geom.Coordinates.Length == 3 ? (decimal)request.Geom.Coordinates[2] : null);
        entrance.Description = request.Description;
        entrance.PositionQuality = request.PositionQuality;
        entrance.SurveyedAt = request.SurveyedAt;
    }

    /// <summary>
    /// Keeps caves.entrance_count and caves.main_geom in sync, and enforces a single main
    /// entrance (last one marked wins). Works on tracked state, before SaveChanges.
    /// </summary>
    private static async Task RecomputeDerivedAsync(SilexGisDbContext db, Cave cave, CancellationToken ct)
    {
        // Bring all of the cave's entrances into the change tracker (new/removed included).
        await db.CaveEntrances.Where(e => e.CaveId == cave.Id).LoadAsync(ct);
        var entrances = db.ChangeTracker.Entries<CaveEntrance>()
            .Where(e => e.State != EntityState.Deleted && e.Entity.CaveId == cave.Id)
            .Select(e => e.Entity)
            .ToList();

        var mains = entrances.Where(e => e.IsMain).ToList();
        if (mains.Count > 1)
        {
            var keep = mains[^1];
            foreach (var other in mains.Where(m => m != keep))
            {
                other.IsMain = false;
            }
        }

        if (entrances.Count > 0 && !entrances.Any(e => e.IsMain))
        {
            entrances[0].IsMain = true;
        }

        cave.EntranceCount = entrances.Count;
        cave.MainGeom = entrances.FirstOrDefault(e => e.IsMain)?.Geom ?? entrances.FirstOrDefault()?.Geom;
    }

    private static EntranceDto ToDto(CaveEntrance e, bool exact, double gridMeters) => new(
        e.Id, e.CaveId, e.Name, e.EntranceTypeId, e.IsMain,
        GeoJsonPoint.From(exact ? e.Geom : LocationProtection.Snap(e.Geom, gridMeters)),
        exact ? e.Altitude : null,
        e.Description,
        exact ? e.PositionQuality : PositionQuality.Unknown,
        e.SurveyedAt,
        ApproximateLocation: !exact);
}
