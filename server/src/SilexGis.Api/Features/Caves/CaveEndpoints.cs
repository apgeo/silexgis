// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Caves;

public static class CaveEndpoints
{
    public static RouteGroupBuilder MapCaveEndpoints(this RouteGroupBuilder api)
    {
        var caves = api.MapGroup("/caves").WithTags("Caves");

        caves.MapGet("/", ListAsync)
            .WithSummary("Paged cave list with filters; visibility-filtered, protected locations obfuscated.");
        caves.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Single cave; protected location fields require ViewExactLocation.");
        caves.MapPost("/", CreateAsync).WithValidation<CaveWriteRequest>()
            .WithSummary("Creates a cave (Editor role and above); the caller becomes owner.");
        caves.MapPut("/{id:guid}", UpdateAsync).WithValidation<CaveWriteRequest>()
            .WithSummary("Full update (Write permission).");
        caves.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Soft delete (Delete permission).");

        return api;
    }

    private static async Task<Results<Ok<PagedResult<CaveListItemDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db,
        AclPermissionService permissions,
        IUserContextAccessor userAccessor,
        IOptions<AccessOptions> access,
        int? page,
        int? pageSize,
        string? sort,
        long? caveTypeId,
        string? region,
        string? search,
        decimal? minLength,
        string? bbox,
        string? tag,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var query = db.Caves.AsNoTracking().VisibleTo(user, db.ObjectAcls, AttachedEntityType.Cave);

        if (!string.IsNullOrWhiteSpace(tag))
        {
            query = query.Where(c => db.Taggings.Any(tg =>
                tg.EntityType == AttachedEntityType.Cave && tg.EntityId == c.Id
                && db.Tags.Any(t => t.Id == tg.TagId && t.Slug == tag)));
        }

        if (caveTypeId is not null)
        {
            query = query.Where(c => c.CaveTypeId == caveTypeId);
        }

        if (!string.IsNullOrWhiteSpace(region))
        {
            query = query.Where(c => c.Region != null && EF.Functions.ILike(c.Region, $"%{region}%"));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(c =>
                EF.Functions.ILike(EF.Functions.Unaccent(c.Name), EF.Functions.Unaccent(pattern))
                || (c.OtherToponyms != null
                    && EF.Functions.ILike(EF.Functions.Unaccent(c.OtherToponyms), EF.Functions.Unaccent(pattern))));
        }

        if (minLength is not null)
        {
            query = query.Where(c => c.SurveyedLength >= minLength);
        }

        if (Bbox.TryParse(bbox, out var box))
        {
            var polygon = box.ToPolygon();
            query = query.Where(c => c.MainGeom != null && c.MainGeom.Intersects(polygon));
        }

        query = ApplySort(query, sort);

        var exactGrants = await permissions.CaveExactLocationGrantsAsync(user, ct);
        var (p, size) = Paging.Normalize(page, pageSize);
        var result = await query.ToPagedAsync(
            p, size, c => c.ToListItem(user, access.Value.LocationGridMeters, exactGrants), ct);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<CaveDto>, ProblemHttpResult>> GetAsync(
        Guid id,
        SilexGisDbContext db,
        IPermissionService permissions,
        IUserContextAccessor userAccessor,
        IOptions<AccessOptions> access,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var cave = await db.Caves.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cave is null)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        if (!await permissions.CanAsync(user, cave, ObjectPermission.Read, ct))
        {
            // Existence of a cave the caller cannot read is not disclosed.
            return ApiProblems.NotFound("cave.not_found");
        }

        // An explicit exact-location grant lifts obfuscation on the single-cave DTO too.
        var exactGrants = cave.LocationProtected
            && await permissions.CanAsync(user, cave, ObjectPermission.ViewExactLocation, ct)
            ? new HashSet<Guid> { cave.Id }
            : null;
        return TypedResults.Ok(cave.ToDto(user, access.Value.LocationGridMeters, exactGrants));
    }

    private static async Task<Results<Created<CaveDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        CaveWriteRequest request,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        IOptions<AccessOptions> access,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!user.CanCreateContent)
        {
            return ApiProblems.Forbidden("cave.create_requires_editor");
        }

        if (!await TeamBindingAllowedAsync(db, user, request.TeamId, ct))
        {
            return ApiProblems.Forbidden("cave.team_membership_required");
        }

        var cave = new Cave { Name = request.Name, OwnerUserId = user.UserId };
        request.Apply(cave);
        cave.OwnerUserId = user.UserId; // Apply() must never change ownership
        db.Caves.Add(cave);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/caves/{cave.Id}", cave.ToDto(user, access.Value.LocationGridMeters));
    }

    private static async Task<Results<Ok<CaveDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        CaveWriteRequest request,
        SilexGisDbContext db,
        IPermissionService permissions,
        IUserContextAccessor userAccessor,
        IOptions<AccessOptions> access,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var cave = await db.Caves.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cave is null)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        if (user is null || !await permissions.CanAsync(user, cave, ObjectPermission.Write, ct))
        {
            return await permissions.CanAsync(user, cave, ObjectPermission.Read, ct)
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("cave.not_found");
        }

        if (request.TeamId != cave.TeamId && !await TeamBindingAllowedAsync(db, user, request.TeamId, ct))
        {
            return ApiProblems.Forbidden("cave.team_membership_required");
        }

        request.Apply(cave);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(cave.ToDto(user, access.Value.LocationGridMeters));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        IPermissionService permissions,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var cave = await db.Caves.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cave is null)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        if (user is null || !await permissions.CanAsync(user, cave, ObjectPermission.Delete, ct))
        {
            return await permissions.CanAsync(user, cave, ObjectPermission.Read, ct)
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("cave.not_found");
        }

        cave.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<bool> TeamBindingAllowedAsync(
        SilexGisDbContext db, UserContext user, Guid? teamId, CancellationToken ct)
    {
        if (teamId is null || user.IsAdmin)
        {
            return teamId is null || await db.Teams.AnyAsync(t => t.Id == teamId, ct);
        }

        return user.IsMemberOf(teamId.Value);
    }

    private static IQueryable<Cave> ApplySort(IQueryable<Cave> query, string? sort) =>
        sort switch
        {
            "name" => query.OrderBy(c => c.Name),
            "-name" => query.OrderByDescending(c => c.Name),
            "surveyedLength" => query.OrderBy(c => c.SurveyedLength),
            "-surveyedLength" => query.OrderByDescending(c => c.SurveyedLength),
            "depth" => query.OrderBy(c => c.Depth),
            "-depth" => query.OrderByDescending(c => c.Depth),
            "region" => query.OrderBy(c => c.Region),
            "-region" => query.OrderByDescending(c => c.Region),
            "updatedAt" => query.OrderBy(c => c.UpdatedAt),
            "-updatedAt" or null or "" => query.OrderByDescending(c => c.UpdatedAt),
            _ => query.OrderByDescending(c => c.UpdatedAt),
        };
}
