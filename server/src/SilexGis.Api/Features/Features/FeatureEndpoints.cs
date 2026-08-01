// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Features;

/// <summary>
/// The cross-kind feature surface: a paged list over the supertype, a resolver that
/// answers any feature id with its typed envelope, and CRUD for generic (data-driven)
/// kinds. Subtyped kinds — caves, entrances, centerlines — are written through their
/// typed endpoints; this slice only reads them.
/// </summary>
public static class FeatureEndpoints
{
    public static RouteGroupBuilder MapFeatureEndpoints(this RouteGroupBuilder api)
    {
        var features = api.MapGroup("/features").WithTags("Features");

        features.MapGet("/", ListAsync)
            .WithSummary("Paged cross-kind feature list with filters; visibility-filtered and location-protected.");
        features.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Resolves any feature id to a typed envelope (common view plus subtype attributes).");
        features.MapPost("/", CreateAsync).WithValidation<FeatureCreateRequest>()
            .WithSummary("Creates a generic feature (Editor role and above); the caller becomes owner.");
        features.MapPut("/{id:guid}", UpdateAsync).WithValidation<FeatureUpdateRequest>()
            .WithSummary("Full update of a generic feature (Write permission, If-Match required).");
        features.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Soft-deletes a feature and its containment subtree (Delete permission).");

        return api;
    }

    private static async Task<Results<Ok<PagedResult<FeatureListItemDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        IOptions<AccessOptions> access,
        int? page,
        int? pageSize,
        string? kind,
        long? featureTypeId,
        string? category,
        string? bbox,
        string? tag,
        string? search,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var query = db.Features.AsNoTracking().VisibleTo(user, db.ObjectAcls);

        // Enum query parameters arrive as the camelCase strings the JSON contract uses;
        // parse case-insensitively (route binding's Enum.TryParse would not).
        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!Enum.TryParse<FeatureKind>(kind, ignoreCase: true, out var kindValue) || !Enum.IsDefined(kindValue))
            {
                return ApiProblems.BadRequest("feature.kind_invalid", $"Unknown feature kind '{kind}'.");
            }

            query = query.Where(f => f.Kind == kindValue);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            if (!Enum.TryParse<FeatureCategory>(category, ignoreCase: true, out var categoryValue)
                || !Enum.IsDefined(categoryValue))
            {
                return ApiProblems.BadRequest("feature.category_invalid", $"Unknown feature category '{category}'.");
            }

            query = query.Where(f => f.Category == categoryValue);
        }

        if (featureTypeId is not null)
        {
            query = query.Where(f => f.FeatureTypeId == featureTypeId);
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            query = query.Where(f => db.Taggings.Any(tg =>
                tg.FeatureId == f.Id && db.Tags.Any(t => t.Id == tg.TagId && t.Slug == tag)));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(f =>
                f.Name != null && EF.Functions.ILike(EF.Functions.Unaccent(f.Name), EF.Functions.Unaccent(pattern)));
        }

        if (Bbox.TryParse(bbox, out var box))
        {
            var polygon = box.ToPolygon();
            query = query.Where(f => f.Geom != null && f.Geom.Intersects(polygon));
        }

        // Protected centerlines are withheld entirely for callers without exact view
        // (they trace the cave's course underground). Exclude them before counting so
        // paging stays correct and their existence is never disclosed.
        var protectedCenterlineIds = await query
            .Where(f => f.Kind == FeatureKind.Centerline && f.IsProtectedEffective)
            .Select(f => f.Id)
            .ToListAsync(ct);
        if (protectedCenterlineIds.Count > 0)
        {
            var exactCenterlines = await protection.ExactViewIdsAsync(user, protectedCenterlineIds, ct);
            var withheld = protectedCenterlineIds.Where(x => !exactCenterlines.Contains(x)).ToArray();
            if (withheld.Length > 0)
            {
                query = query.Where(f => !withheld.Contains(f.Id));
            }
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(f => f.UpdatedAt)
            .Skip((p - 1) * size).Take(size).ToListAsync(ct);

        var types = await FeatureTypesOfAsync(db, rows, ct);
        var exact = await protection.ExactViewIdsAsync(user, rows.Select(f => f.Id).ToList(), ct);
        var grid = access.Value.LocationGridMeters;
        var items = rows.Select(f =>
        {
            var type = f.FeatureTypeId is not null && types.TryGetValue(f.FeatureTypeId.Value, out var t) ? t : null;
            return f.ToListItem(type?.Code, FeatureMapping.DisplayPolicy(f, type), exact.Contains(f.Id), grid);
        }).ToList();

        return TypedResults.Ok(new PagedResult<FeatureListItemDto>(items, p, size, total));
    }

    private static async Task<Results<Ok<FeatureEnvelopeDto>, ProblemHttpResult>> GetAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IPermissionService permissions,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        IOptions<AccessOptions> access,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var feature = await db.Features.AsNoTracking()
            .Include(f => f.Cave).Include(f => f.Entrance).Include(f => f.Centerline)
            .FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null || !await permissions.CanAsync(user, feature, ObjectPermission.Read, ct))
        {
            // Existence of a feature the caller cannot read is not disclosed.
            return ApiProblems.NotFound("feature.not_found");
        }

        var exact = (await protection.ExactViewIdsAsync(user, [feature.Id], ct)).Contains(feature.Id);

        // A protected centerline cannot be served obfuscated (any part of it is exact
        // location data); without exact view the row behaves as if it did not exist.
        if (feature.Kind == FeatureKind.Centerline && !exact)
        {
            return ApiProblems.NotFound("feature.not_found");
        }

        var featureType = feature.FeatureTypeId is null
            ? null
            : await db.FeatureTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == feature.FeatureTypeId, ct);
        var parents = await PrimaryChainAsync(db, user!, feature, ct);

        await Concurrency.EmitETagAsync(http, db, VersionedTable.Features, feature.Id, ct);
        var dto = feature.ToDto(
            featureType?.Code, FeatureMapping.DisplayPolicy(feature, featureType), exact,
            access.Value.LocationGridMeters, parents);
        return TypedResults.Ok(new FeatureEnvelopeDto(
            feature.Kind,
            dto,
            feature.Cave?.ToCaveDto(exact),
            feature.Entrance?.ToEntranceDto(exact),
            feature.Centerline?.ToCenterlineDto()));
    }

    private static async Task<Results<Created<FeatureDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        FeatureCreateRequest request,
        SilexGisDbContext db,
        FeatureWriteService writeService,
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
            return ApiProblems.Forbidden("feature.create_requires_editor");
        }

        if (request.Kind != FeatureKind.Generic)
        {
            return ApiProblems.BadRequest(
                "feature.kind_not_generic",
                "Caves, entrances and centerlines are created through their typed endpoints.");
        }

        if (request.TeamId is not null && !user.IsAdmin && !user.IsMemberOf(request.TeamId.Value))
        {
            return ApiProblems.Forbidden("feature.team_membership_required");
        }

        Geometry? geom = null;
        if (request.Geometry is not null)
        {
            geom = request.Geometry.ToGeometryOrNull();
            if (geom is null)
            {
                return ApiProblems.BadRequest("feature.geometry_invalid", "Geometry is malformed or invalid.");
            }
        }

        var parentSpecs = (request.Parents ?? []).Select(x => new ParentSpec(x.ParentId, x.IsPrimary)).ToList();
        if (parentSpecs.Count > 0)
        {
            var parentIds = parentSpecs.Select(s => s.ParentId).Distinct().ToArray();
            var visibleParents = await db.Features.AsNoTracking().VisibleTo(user, db.ObjectAcls)
                .Where(f => parentIds.Contains(f.Id)).Select(f => f.Id).ToListAsync(ct);
            if (visibleParents.Count != parentIds.Length)
            {
                // A parent the caller cannot read is reported exactly like a missing one.
                return ApiProblems.BadRequest("feature.parent_not_found", "A parent feature does not exist.");
            }
        }

        var feature = new Feature
        {
            Name = request.Name,
            FeatureTypeId = request.FeatureTypeId,
            Geom = geom,
            Description = request.Description,
            Properties = RawProperties(request.Properties),
            LocationProtected = request.LocationProtected,
            OwnerUserId = user.UserId,
            TeamId = request.TeamId,
            Visibility = request.Visibility,
        };

        try
        {
            await writeService.CreateGenericAsync(feature, parentSpecs, ct);
        }
        catch (FeatureWriteException ex)
        {
            return WriteProblem(ex);
        }

        await db.SaveChangesAsync(ct);

        var featureType = await db.FeatureTypes.AsNoTracking().FirstAsync(t => t.Id == request.FeatureTypeId, ct);
        var parents = await PrimaryChainAsync(db, user, feature, ct);
        // The creator owns the row, so they always see it exactly.
        return TypedResults.Created(
            $"/api/v1/features/{feature.Id}",
            feature.ToDto(
                featureType.Code, FeatureMapping.DisplayPolicy(feature, featureType), exact: true,
                access.Value.LocationGridMeters, parents));
    }

    private static async Task<Results<Ok<FeatureDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        FeatureUpdateRequest request,
        HttpContext http,
        SilexGisDbContext db,
        FeatureWriteService writeService,
        IPermissionService permissions,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        IOptions<AccessOptions> access,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var feature = await db.Features.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null)
        {
            return ApiProblems.NotFound("feature.not_found");
        }

        if (user is null || !await permissions.CanAsync(user, feature, ObjectPermission.Write, ct))
        {
            return await permissions.CanAsync(user, feature, ObjectPermission.Read, ct)
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("feature.not_found");
        }

        if (feature.Kind != FeatureKind.Generic)
        {
            return ApiProblems.BadRequest(
                "feature.kind_not_generic",
                "Caves, entrances and centerlines are edited through their typed endpoints.");
        }

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.Features, feature.Id, ct, required: true) is { } stale)
        {
            return stale;
        }

        var featureType = await db.FeatureTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.FeatureTypeId, ct);
        if (featureType is null)
        {
            return ApiProblems.BadRequest("feature.type_required", "Unknown feature type.");
        }

        if (request.TeamId is not null && !user.IsAdmin && !user.IsMemberOf(request.TeamId.Value))
        {
            return ApiProblems.Forbidden("feature.team_membership_required");
        }

        Geometry? geom = null;
        if (request.Geometry is not null)
        {
            geom = request.Geometry.ToGeometryOrNull();
            if (geom is null)
            {
                return ApiProblems.BadRequest("feature.geometry_invalid", "Geometry is malformed or invalid.");
            }

            var geometryClass = GeometryClasses.Of(geom);
            if (geometryClass is null || !featureType.AcceptedGeometryClasses.Contains(geometryClass.Value))
            {
                return ApiProblems.BadRequest(
                    "feature.geometry_invalid",
                    $"Kind '{featureType.Code}' does not accept {geom.GeometryType} geometries.");
            }
        }

        if (featureType.RequiresParent && !await db.FeatureHierarchyEdges.AnyAsync(e => e.ChildId == feature.Id, ct))
        {
            return ApiProblems.BadRequest(
                "feature.parent_required",
                $"Kind '{featureType.Code}' only exists inside a containing feature.");
        }

        var exact = (await protection.ExactViewIdsAsync(user, [feature.Id], ct)).Contains(feature.Id);

        feature.Name = request.Name;
        feature.FeatureTypeId = request.FeatureTypeId;
        feature.Category = featureType.Category;
        feature.Description = request.Description;
        feature.Properties = RawProperties(request.Properties);
        feature.TeamId = request.TeamId;
        feature.Visibility = request.Visibility;

        // A caller without exact view was shown obfuscated (snapped or omitted)
        // coordinates; writing that echo back would corrupt the stored precise geometry,
        // and clearing the protection flag would expose it. Preserve both — everything
        // else in the payload is theirs to edit.
        if (exact)
        {
            feature.Geom = geom;
            if (feature.LocationProtected != request.LocationProtected)
            {
                try
                {
                    await writeService.SetLocationProtectedAsync(feature.Id, request.LocationProtected, ct);
                }
                catch (FeatureWriteException ex)
                {
                    return WriteProblem(ex);
                }
            }
        }

        try
        {
            await writeService.ValidatePropertiesAsync(feature, ct);
        }
        catch (FeatureWriteException ex)
        {
            return WriteProblem(ex);
        }

        await db.SaveChangesAsync(ct);
        await Concurrency.EmitETagAsync(http, db, VersionedTable.Features, feature.Id, ct);

        var parents = await PrimaryChainAsync(db, user, feature, ct);
        return TypedResults.Ok(feature.ToDto(
            featureType.Code, FeatureMapping.DisplayPolicy(feature, featureType), exact,
            access.Value.LocationGridMeters, parents));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        FeatureWriteService writeService,
        IPermissionService permissions,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        // No tracking: the soft delete writes directly to the database, and a stale
        // tracked copy would confuse the cave-mirror recount below.
        var feature = await db.Features.AsNoTracking()
            .Include(f => f.Entrance)
            .FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null)
        {
            return ApiProblems.NotFound("feature.not_found");
        }

        if (user is null || !await permissions.CanAsync(user, feature, ObjectPermission.Delete, ct))
        {
            return await permissions.CanAsync(user, feature, ObjectPermission.Read, ct)
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("feature.not_found");
        }

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.Features, feature.Id, ct) is { } stale)
        {
            return stale;
        }

        await writeService.SoftDeleteAsync(feature.Id, ct);

        // Deleting an entrance through the uniform address must keep the cave's derived
        // mirror (entrance count + representative point) consistent, exactly as the
        // typed entrance endpoint does.
        if (feature.Kind == FeatureKind.CaveEntrance && feature.Entrance is not null)
        {
            await writeService.SyncCaveMirrorAsync(feature.Entrance.CaveFeatureId, ct);
        }

        // The soft delete stamps rows immediately, but its audit entries ride the context.
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>Feature-type rows referenced by the given features, keyed by id.</summary>
    internal static async Task<Dictionary<long, FeatureType>> FeatureTypesOfAsync(
        SilexGisDbContext db, IReadOnlyCollection<Feature> features, CancellationToken ct)
    {
        var typeIds = features.Where(f => f.FeatureTypeId is not null)
            .Select(f => f.FeatureTypeId!.Value).Distinct().ToArray();
        return typeIds.Length == 0
            ? []
            : await db.FeatureTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
    }

    /// <summary>
    /// The feature's primary-parent chain (outermost ancestor first) for breadcrumbs.
    /// Truncated at the first ancestor the caller may not read — names above that point
    /// are never disclosed.
    /// </summary>
    internal static async Task<IReadOnlyList<FeatureBreadcrumbDto>> PrimaryChainAsync(
        SilexGisDbContext db, UserContext user, Feature feature, CancellationToken ct)
    {
        var ancestorIds = feature.AncestorIds.Where(a => a != feature.Id).ToArray();
        if (ancestorIds.Length == 0)
        {
            return [];
        }

        // One flat read: the primary edge of the feature and of every ancestor.
        var chainChildIds = feature.AncestorIds;
        var primaryParentOf = await db.FeatureHierarchyEdges.AsNoTracking()
            .Where(e => e.IsPrimary && chainChildIds.Contains(e.ChildId))
            .ToDictionaryAsync(e => e.ChildId, e => e.ParentId, ct);
        var visibleNames = await db.Features.AsNoTracking().VisibleTo(user, db.ObjectAcls)
            .Where(f => ancestorIds.Contains(f.Id))
            .Select(f => new { f.Id, f.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        var chain = new List<FeatureBreadcrumbDto>();
        var current = feature.Id;
        // The DAG is cycle-free by construction; the cap keeps corrupt data from looping.
        while (chain.Count <= ancestorIds.Length && primaryParentOf.TryGetValue(current, out var parentId))
        {
            if (!visibleNames.TryGetValue(parentId, out var name))
            {
                break;
            }

            chain.Add(new FeatureBreadcrumbDto(parentId, name));
            current = parentId;
        }

        chain.Reverse();
        return chain;
    }

    internal static ProblemHttpResult WriteProblem(FeatureWriteException ex) =>
        ApiProblems.BadRequest(ex.Code, string.Join(" ", ex.Errors));

    internal static string RawProperties(JsonElement? properties) =>
        properties is { ValueKind: JsonValueKind.Object } p ? p.GetRawText() : "{}";
}
