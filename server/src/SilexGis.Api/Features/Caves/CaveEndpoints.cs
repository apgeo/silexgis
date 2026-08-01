// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// Typed cave facade over the feature aggregate: a cave is a feature row (identity, name,
/// description, access control, protection, main-entrance point cache) plus the cave
/// subtype row (speleological attributes). Aggregate invariants — hierarchy edges,
/// derived protection, delegated access of entrance/centerline children, subtree soft
/// delete — go through the feature write service.
/// </summary>
public static class CaveEndpoints
{
    public static RouteGroupBuilder MapCaveEndpoints(this RouteGroupBuilder api)
    {
        var caves = api.MapGroup("/caves").WithTags("Caves");

        caves.MapGet("/", ListAsync)
            .WithSummary("Paged cave list with filters; visibility-filtered, protected locations obfuscated.");
        caves.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Single cave; protected location fields require ViewExactLocation.");
        caves.MapGet("/{id:guid}/summary", GetSummaryAsync)
            .WithSummary("Cave header data: related-record counts, main entrance, caller capabilities.");
        caves.MapPost("/", CreateAsync).WithValidation<CaveWriteRequest>()
            .WithSummary("Creates a cave (Editor role and above); the caller becomes owner.");
        caves.MapPut("/{id:guid}", UpdateAsync).WithValidation<CaveWriteRequest>()
            .WithSummary("Full update (Write permission).");
        caves.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Soft delete of the cave and its subtree (Delete permission).");

        return api;
    }

    private static async Task<Results<Ok<PagedResult<CaveListItemDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
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

        var query = db.Features.AsNoTracking()
            .Include(f => f.Cave)
            .Where(f => f.Kind == FeatureKind.Cave)
            .VisibleTo(user, db.ObjectAcls);

        if (!string.IsNullOrWhiteSpace(tag))
        {
            query = query.Where(f => db.Taggings.Any(tg =>
                tg.FeatureId == f.Id && db.Tags.Any(t => t.Id == tg.TagId && t.Slug == tag)));
        }

        if (caveTypeId is not null)
        {
            query = query.Where(f => f.Cave!.CaveTypeId == caveTypeId);
        }

        if (!string.IsNullOrWhiteSpace(region))
        {
            query = query.Where(f => f.Cave!.Region != null && EF.Functions.ILike(f.Cave!.Region, $"%{region}%"));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(f =>
                (f.Name != null
                    && EF.Functions.ILike(EF.Functions.Unaccent(f.Name), EF.Functions.Unaccent(pattern)))
                || (f.Cave!.OtherToponyms != null
                    && EF.Functions.ILike(EF.Functions.Unaccent(f.Cave!.OtherToponyms), EF.Functions.Unaccent(pattern))));
        }

        if (minLength is not null)
        {
            query = query.Where(f => f.Cave!.SurveyedLength >= minLength);
        }

        if (Bbox.TryParse(bbox, out var box))
        {
            var polygon = box.ToPolygon();
            query = query.Where(f => f.Geom != null && f.Geom.Intersects(polygon));
        }

        query = ApplySort(query, sort);

        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((p - 1) * size).Take(size).ToListAsync(ct);

        // Exact view is decided per row against every protected root above it.
        var exactIds = await protection.ExactViewIdsAsync(user, [.. rows.Select(f => f.Id)], ct);
        var grid = access.Value.LocationGridMeters;
        var items = rows.Select(f => f.ToListItem(exactIds.Contains(f.Id), grid)).ToList();
        return TypedResults.Ok(new PagedResult<CaveListItemDto>(items, p, size, total));
    }

    private static async Task<Results<Ok<CaveDto>, ProblemHttpResult>> GetAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IPermissionService permissions,
        FeatureProtection protection,
        IUserContextAccessor userAccessor,
        IOptions<AccessOptions> access,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var feature = await CaveFeatureAsync(db.Features.AsNoTracking(), id, ct);
        if (feature is null || !await permissions.CanAsync(user, feature, ObjectPermission.Read, ct))
        {
            // Existence of a cave the caller cannot read is not disclosed.
            return ApiProblems.NotFound("cave.not_found");
        }

        var exact = (await protection.ExactViewIdsAsync(user, [feature.Id], ct)).Contains(feature.Id);
        var parents = await ParentsAsync(db, user!, feature.Id, ct);
        await Concurrency.EmitETagAsync(http, db, VersionedTable.Features, feature.Id, ct);
        return TypedResults.Ok(feature.ToDto(exact, access.Value.LocationGridMeters, parents));
    }

    private static async Task<Results<Ok<CaveSummaryDto>, ProblemHttpResult>> GetSummaryAsync(
        Guid id,
        SilexGisDbContext db,
        IPermissionService permissions,
        FeatureProtection protection,
        IUserContextAccessor userAccessor,
        IOptions<AccessOptions> access,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var feature = await CaveFeatureAsync(db.Features.AsNoTracking(), id, ct);
        if (feature is null || !await permissions.CanAsync(user, feature, ObjectPermission.Read, ct))
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        var centerlineCount = await db.Centerlines.CountAsync(c => c.CaveFeatureId == id, ct);
        var surveyModelCount = await db.SurveyModels.CountAsync(s => s.CaveFeatureId == id, ct);
        var attachmentCount = await db.Attachments.CountAsync(a => a.FeatureId == id, ct);

        // Trip links are visibility-filtered — two callers may legitimately see different counts.
        var visibleTrips = db.TripLogs.AsNoTracking().VisibleTo(user!, db.ObjectAcls, AttachedEntityType.TripLog);
        var tripLogCount = await db.TripLogCaves.CountAsync(
            l => l.CaveId == id && visibleTrips.Any(t => t.Id == l.TripLogId), ct);

        var main = await db.CaveEntrances.AsNoTracking()
            .Include(e => e.Feature)
            .FirstOrDefaultAsync(e => e.CaveFeatureId == id && e.IsMain, ct);

        var exactIds = await protection.ExactViewIdsAsync(user, main is null ? [id] : [id, main.Id], ct);
        var mainDto = main is null
            ? null
            : new CaveMainEntranceDto(
                main.Id,
                main.Feature.Name,
                CaveMapping.MapGeom(main.Feature.Geom, exactIds.Contains(main.Id), access.Value.LocationGridMeters),
                ApproximateLocation: !exactIds.Contains(main.Id));

        var effective = await permissions.EffectiveAsync(user, feature, ct);
        var caps = new CavePermissionsDto(
            CanWrite: effective.HasFlag(ObjectPermission.Write),
            CanDelete: effective.HasFlag(ObjectPermission.Delete),
            CanShare: effective.HasFlag(ObjectPermission.Share),
            CanManagePermissions: effective.HasFlag(ObjectPermission.ManagePermissions),
            // The real multi-root decision, not the row-local ACL flag: an ancestor's
            // protection vetoes exact view even when the row itself grants it.
            CanViewExactLocation: exactIds.Contains(id));

        return TypedResults.Ok(new CaveSummaryDto(
            feature.Id, feature.Name ?? string.Empty, feature.Cave!.EntranceCount,
            centerlineCount, surveyModelCount, attachmentCount, tripLogCount, mainDto, caps));
    }

    private static async Task<Results<Created<CaveDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        CaveWriteRequest request,
        SilexGisDbContext db,
        FeatureWriteService writer,
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

        if (!await CavingGroupBindingAllowedAsync(db, user, request.CavingGroupId, ct))
        {
            return ApiProblems.Forbidden("cave.caving_group_membership_required");
        }

        IReadOnlyList<ParentSpec> parents = [];
        if (request.ParentId is { } parentId)
        {
            // The parent must exist and be readable by the caller — a body reference must
            // never confirm the existence of rows the caller cannot see.
            if (!await db.Features.VisibleTo(user, db.ObjectAcls).AnyAsync(f => f.Id == parentId, ct))
            {
                return ApiProblems.BadRequest("cave.parent_not_found");
            }

            parents = [new ParentSpec(parentId, IsPrimary: true)];
        }

        var feature = new Feature
        {
            OwnerUserId = user.UserId,
            // Safe to set directly on create: the write service recomputes the subtree's
            // effective protection while establishing the hierarchy edges.
            LocationProtected = request.LocationProtected,
        };
        var cave = new Cave();
        feature.Cave = cave;
        request.Apply(feature, cave);

        try
        {
            await writer.CreateCaveAsync(feature, cave, parents, ct);
            if (request.Properties is { ValueKind: JsonValueKind.Object })
            {
                await writer.ValidatePropertiesAsync(feature, ct);
            }
        }
        catch (FeatureWriteException ex)
        {
            return ApiProblems.BadRequest(ex.Code, string.Join("; ", ex.Errors));
        }

        await db.SaveChangesAsync(ct);
        var parentDtos = await ParentsAsync(db, user, feature.Id, ct);
        // The creator owns the row, so the mapping is exact by construction.
        return TypedResults.Created(
            $"/api/v1/caves/{feature.Id}",
            feature.ToDto(exact: true, access.Value.LocationGridMeters, parentDtos));
    }

    private static async Task<Results<Ok<CaveDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        CaveWriteRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IPermissionService permissions,
        FeatureProtection protection,
        FeatureWriteService writer,
        IUserContextAccessor userAccessor,
        IOptions<AccessOptions> access,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var feature = await CaveFeatureAsync(db.Features, id, ct);
        if (feature is null)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        if (user is null || !await permissions.CanAsync(user, feature, ObjectPermission.Write, ct))
        {
            return await permissions.CanAsync(user, feature, ObjectPermission.Read, ct)
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("cave.not_found");
        }

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.Features, feature.Id, ct, required: true) is { } stale)
        {
            return stale;
        }

        if (request.CavingGroupId != feature.CavingGroupId && !await CavingGroupBindingAllowedAsync(db, user, request.CavingGroupId, ct))
        {
            return ApiProblems.Forbidden("cave.caving_group_membership_required");
        }

        var cave = feature.Cave!;

        // Write-path protection guard: a caller without exact view only ever saw
        // obfuscated values (null address/registry/notes), so ignore any change they
        // submit to those fields — a full-replace PUT would otherwise write the
        // obfuscated echo back over the real data.
        var exact = (await protection.ExactViewIdsAsync(user, [feature.Id], ct)).Contains(feature.Id);
        var preserved = (cave.ClosestAddress, cave.LandRegistryNumber, cave.LocationNotes);
        var accessChanged = feature.CavingGroupId != request.CavingGroupId || feature.Visibility != request.Visibility;

        request.Apply(feature, cave);

        if (!exact)
        {
            (cave.ClosestAddress, cave.LandRegistryNumber, cave.LocationNotes) = preserved;
        }

        try
        {
            // The protection flag itself is preserved for non-exact callers: they must not
            // be able to clear the very protection that redacts what they see. Flips go
            // through the write service, which restamps the whole subtree.
            if (exact && request.LocationProtected != feature.LocationProtected)
            {
                await writer.SetLocationProtectedAsync(feature.Id, request.LocationProtected, ct);
            }

            if (request.Properties is { ValueKind: JsonValueKind.Object })
            {
                await writer.ValidatePropertiesAsync(feature, ct);
            }

            if (accessChanged)
            {
                // Entrance/centerline children carry a copy of the cave's owner/caving group/visibility.
                await writer.SyncDelegatedAccessAsync(feature.Id, ct);
            }
        }
        catch (FeatureWriteException ex)
        {
            return ApiProblems.BadRequest(ex.Code, string.Join("; ", ex.Errors));
        }

        await db.SaveChangesAsync(ct);
        await Concurrency.EmitETagAsync(http, db, VersionedTable.Features, feature.Id, ct);
        var parents = await ParentsAsync(db, user, feature.Id, ct);
        return TypedResults.Ok(feature.ToDto(exact, access.Value.LocationGridMeters, parents));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IPermissionService permissions,
        FeatureWriteService writer,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var feature = await db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id && f.Kind == FeatureKind.Cave, ct);
        if (feature is null)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        if (user is null || !await permissions.CanAsync(user, feature, ObjectPermission.Delete, ct))
        {
            return await permissions.CanAsync(user, feature, ObjectPermission.Read, ct)
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("cave.not_found");
        }

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.Features, feature.Id, ct) is { } stale)
        {
            return stale;
        }

        // One stamp over the cave and its containment subtree (entrances, centerlines),
        // so a later restore undoes exactly this deletion.
        await writer.SoftDeleteAsync(feature.Id, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static Task<Feature?> CaveFeatureAsync(IQueryable<Feature> features, Guid id, CancellationToken ct) =>
        features.Include(f => f.Cave).FirstOrDefaultAsync(f => f.Id == id && f.Kind == FeatureKind.Cave, ct);

    /// <summary>Breadcrumb data: the containment parents the caller may see, primary edge first.</summary>
    private static async Task<IReadOnlyList<CaveParentDto>> ParentsAsync(
        SilexGisDbContext db, UserContext user, Guid featureId, CancellationToken ct)
    {
        var rows = await db.FeatureHierarchyEdges.AsNoTracking()
            .Where(e => e.ChildId == featureId)
            .Join(
                db.Features.VisibleTo(user, db.ObjectAcls),
                e => e.ParentId,
                f => f.Id,
                (e, f) => new { f.Id, f.Name, e.IsPrimary })
            .ToListAsync(ct);
        return [.. rows
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .Select(x => new CaveParentDto(x.Id, x.Name, x.IsPrimary))];
    }

    private static async Task<bool> CavingGroupBindingAllowedAsync(
        SilexGisDbContext db, UserContext user, Guid? cavingGroupId, CancellationToken ct)
    {
        if (cavingGroupId is null || user.IsAdmin)
        {
            return cavingGroupId is null || await db.CavingGroups.AnyAsync(t => t.Id == cavingGroupId, ct);
        }

        return user.IsMemberOf(cavingGroupId.Value);
    }

    private static IQueryable<Feature> ApplySort(IQueryable<Feature> query, string? sort) =>
        sort switch
        {
            "name" => query.OrderBy(f => f.Name),
            "-name" => query.OrderByDescending(f => f.Name),
            "surveyedLength" => query.OrderBy(f => f.Cave!.SurveyedLength),
            "-surveyedLength" => query.OrderByDescending(f => f.Cave!.SurveyedLength),
            "depth" => query.OrderBy(f => f.Cave!.Depth),
            "-depth" => query.OrderByDescending(f => f.Cave!.Depth),
            "region" => query.OrderBy(f => f.Cave!.Region),
            "-region" => query.OrderByDescending(f => f.Cave!.Region),
            "updatedAt" => query.OrderBy(f => f.UpdatedAt),
            "-updatedAt" or null or "" => query.OrderByDescending(f => f.UpdatedAt),
            _ => query.OrderByDescending(f => f.UpdatedAt),
        };
}
