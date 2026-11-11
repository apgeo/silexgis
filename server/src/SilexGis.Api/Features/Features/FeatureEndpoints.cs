// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Filters;
using SilexGis.Infrastructure.Geodata;
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
    /// <summary>A type change refused because the caller may not edit features of the new type.</summary>
    public const string TypeChangeForbiddenCode = "feature.type_change_forbidden";

    public static RouteGroupBuilder MapFeatureEndpoints(this RouteGroupBuilder api)
    {
        var features = api.MapGroup("/features").WithTags("Features");

        features.MapGet("/", ListAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary(
                "Paged cross-kind feature list; visibility-filtered and location-protected. `filter` "
                + "(a filter document as a link token) or `savedFilterId` narrows it by the feature "
                + "world's conditions, applied after visibility and counted over the narrowed set; "
                + "`search`, `ids` and `bbox` narrow the same set further.");
        features.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Resolves any feature id to a typed envelope (common view plus subtype attributes).");
        features.MapPost("/", CreateAsync).WithValidation<FeatureCreateRequest>()
            .WithSummary("Creates a generic feature (Create permission in the target context); the caller becomes owner.");
        features.MapPut("/{id:guid}", UpdateAsync).WithValidation<FeatureUpdateRequest>()
            .WithSummary("Full update of a generic feature (Write permission, If-Match required).");
        features.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary(
                "Soft-deletes a feature and its containment subtree (Delete permission). Nothing is "
                + "removed: the deletion can be undone from the list of deleted features.");
        features.MapGet("/deleted", FeatureDeletionEndpoints.ListAsync)
            .WithSummary(
                "The deletions of caves, entrances and surface features the caller may undo, most "
                + "recent first: one row per deletion, with what went with it. Carries no position.");
        features.MapPost("/{id:guid}/restore", FeatureDeletionEndpoints.RestoreAsync)
            .WithSummary(
                "Puts a deleted cave, entrance or surface feature back with everything that was "
                + "deleted along with it (the right to read it and to delete it), and answers the "
                + "feature with its version. Refused while something containing it is deleted.");

        return api;
    }

    private static async Task<Results<Ok<PagedResult<FeatureListItemDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        FeatureProtection protection,
        IOptions<AccessOptions> accessOptions,
        IOptions<MapOptions> mapOptions,
        SurfaceFilters surfaceFilters,
        string? filter,
        Guid? savedFilterId,
        int? page,
        int? pageSize,
        string? bbox,
        string? search,
        [FromQuery] Guid[]? ids,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var narrowing = await surfaceFilters.ForAsync<Feature>(
            ctx, FeatureFilterWorld.Key, filter, savedFilterId, ct);
        if (narrowing.Refusal is { } refusal)
        {
            return refusal.ToProblem();
        }

        // Kind, category, type and tag are conditions of the filter document, not parameters of
        // their own: one spelling of the question, so the list, its export and a saved filter
        // cannot drift apart. The narrowing is applied to rows this caller may already see, and
        // the count and the page below are both taken over the composed set.
        var query = narrowing.Narrowing!.Apply(
            db.Features.AsNoTracking().VisibleTo(ctx, db.Features, db.FeatureSetMembers));

        // A named set of objects — what a multi-selection asks about, and what a working set is.
        // It narrows the same visibility-filtered query as every other condition rather than
        // being answered from a second path: naming an id you may not read must return nothing,
        // not a row, and must not be distinguishable from naming one that does not exist.
        if (ids is { Length: > 0 })
        {
            // The most objects one request may name is the ceiling on a selection, because a
            // selection's summary is what asks.
            var maxIds = mapOptions.Value.SelectionMax;
            if (ids.Length > maxIds)
            {
                return ApiProblems.BadRequest(
                    "feature.too_many_ids", $"At most {maxIds} objects can be asked about at once.");
            }

            query = query.Where(f => ids.Contains(f.Id));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = LikePatterns.Containing(search);
            query = query.Where(f =>
                f.Name != null
                && EF.Functions.ILike(EF.Functions.Unaccent(f.Name), EF.Functions.Unaccent(pattern), LikePatterns.Escape));
        }

        // A protected row is in the box by the position this caller is shown — the snapped
        // point, or nowhere for a line or polygon whose geometry is withheld from them — never by
        // its stored one: tested on the stored geometry, the page and its total would bisect a
        // hidden position below the protection grid.
        // A box that is not a window is refused, not read as "no window" (the page would then
        // list every readable row) nor handed on reversed (the stored-geometry test and the
        // shown-position test disagree on such a box, so unprotected rows would answer while
        // protected ones did not).
        if (WindowProblems.Optional(bbox, out var window) is { } windowProblem)
        {
            return windowProblem;
        }

        // Every statement from here on may visit each readable row — or, for a wide window, each
        // row in it — and probe its ancestors, which the planner prices high enough to compile for a
        // reader who owns nothing: a cost larger than the statement itself, paid again on every
        // request. Without JIT for them alone.
        await using var planner = await PlannerSql.WithoutJitAsync(db.Database, ct);

        if (window is { } box)
        {
            var polygon = box.ToPolygon();
            var protectedMembers = await WindowMembershipSql.ProtectedMembersAsync(
                db, ctx, box.West, box.South, box.East, box.North,
                accessOptions.Value.LocationGridMeters, null, ct);
            query = query.Where(f => f.Geom != null
                && ((!f.IsProtectedEffective && f.Geom.Intersects(polygon))
                    || protectedMembers.Contains(f.Id)));
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
            var exactCenterlines = await protection.ExactViewIdsAsync(ctx, protectedCenterlineIds, ct);
            var withheld = protectedCenterlineIds.Where(x => !exactCenterlines.Contains(x)).ToArray();
            if (withheld.Length > 0)
            {
                query = query.Where(f => !withheld.Contains(f.Id));
            }
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(ct);
        // The id breaks ties so that rows written in one transaction keep their place between pages.
        var rows = await FeatureFilterWorld.NewestFirst(query)
            .Skip((p - 1) * size).Take(size).ToListAsync(ct);

        var types = await FeatureTypesOfAsync(db, rows, ct);
        var exact = await protection.ExactViewIdsAsync(ctx, rows.Select(f => f.Id).ToList(), ct);
        var grid = accessOptions.Value.LocationGridMeters;
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
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        FeatureProtection protection,
        IOptions<AccessOptions> accessOptions,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var feature = await db.Features.AsNoTracking()
            .Include(f => f.Cave).Include(f => f.Entrance).Include(f => f.Centerline)
            .FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null || !(await access.DecideAsync(ctx, AccessAction.Read, feature, ct)).Allowed)
        {
            // Existence of a feature the caller cannot read is not disclosed.
            return ApiProblems.NotFound("feature.not_found");
        }

        var exact = (await protection.ExactViewIdsAsync(ctx, [feature.Id], ct)).Contains(feature.Id);

        // A protected centerline cannot be served obfuscated (any part of it is exact
        // location data); without exact view the row behaves as if it did not exist.
        if (feature.Kind == FeatureKind.Centerline && !exact)
        {
            return ApiProblems.NotFound("feature.not_found");
        }

        await Concurrency.EmitETagAsync(http, db, VersionedTable.Features, feature.Id, ct);
        return TypedResults.Ok(
            await EnvelopeAsync(db, ctx!, feature, exact, accessOptions.Value.LocationGridMeters, ct));
    }

    /// <summary>
    /// A feature as its own address answers it: the common view and the attributes of its kind,
    /// with its position as this caller may have it. One home, because a second route now answers
    /// a feature this way and the two must not come to differ in what they withhold.
    /// </summary>
    /// <param name="feature">The feature, loaded with its subtype rows.</param>
    /// <param name="exact">Whether the caller may place it exactly — decided by the caller of this.</param>
    internal static async Task<FeatureEnvelopeDto> EnvelopeAsync(
        SilexGisDbContext db, AccessContext ctx, Feature feature, bool exact, double gridMeters, CancellationToken ct)
    {
        var featureType = feature.FeatureTypeId is null
            ? null
            : await db.FeatureTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == feature.FeatureTypeId, ct);
        var parents = await PrimaryChainAsync(db, ctx, feature, ct);
        var dto = feature.ToDto(
            featureType?.Code, FeatureMapping.DisplayPolicy(feature, featureType), exact, gridMeters, parents);
        return new FeatureEnvelopeDto(
            feature.Kind,
            dto,
            feature.Cave?.ToCaveDto(exact),
            feature.Entrance?.ToEntranceDto(exact),
            feature.Centerline?.ToCenterlineDto());
    }

    private static async Task<Results<Created<FeatureDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        FeatureCreateRequest request,
        SilexGisDbContext db,
        FeatureWriteService writeService,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        IOptions<AccessOptions> accessOptions,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (request.Kind != FeatureKind.Generic)
        {
            return ApiProblems.BadRequest(
                "feature.kind_not_generic",
                "Caves, entrances and centerlines are created through their typed endpoints.");
        }

        if (request.CavingGroupId is not null
            && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.Features, request.CavingGroupId.Value))
        {
            return ApiProblems.Forbidden(CavingGroupBindingRules.ForbiddenCode);
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
        // The primary parent is the create's context: a subtree-scoped Create reaches
        // through it, and owning it is what lets someone extend their own feature.
        var primaryParentId = parentSpecs.FirstOrDefault(s => s.IsPrimary).ParentId;
        var createFacts = await CreateContext.ParentCreateFactsAsync(
            db, ctx, primaryParentId == Guid.Empty ? null : primaryParentId, request.CavingGroupId,
            FeatureKind.Generic, ct, request.FeatureTypeId);
        if (parentSpecs.Count > 0)
        {
            var parentIds = parentSpecs.Select(s => s.ParentId).Distinct().ToArray();
            var visibleParents = await db.Features.AsNoTracking().VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                .Where(f => parentIds.Contains(f.Id)).Select(f => f.Id).ToListAsync(ct);
            if (visibleParents.Count != parentIds.Length || createFacts is null)
            {
                // A parent the caller cannot read is reported exactly like a missing one.
                return ApiProblems.BadRequest("feature.parent_not_found", "A parent feature does not exist.");
            }
        }

        // Creation is decided by the walk against that target context, so a Create deny
        // bites and a subtree- or club-scoped Create reaches exactly where it should.
        if (!CreateRules.MayCreate(ctx, AccessDomain.Features, createFacts
            ?? AccessTargetFacts.ForCreate(null, [], request.CavingGroupId, FeatureKind.Generic, request.FeatureTypeId)))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
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
            CavingGroupId = request.CavingGroupId,
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
        var parents = await PrimaryChainAsync(db, ctx, feature, ct);
        // The creator owns the row, so they always see it exactly.
        return TypedResults.Created(
            $"/api/v1/features/{feature.Id}",
            feature.ToDto(
                featureType.Code, FeatureMapping.DisplayPolicy(feature, featureType), exact: true,
                accessOptions.Value.LocationGridMeters, parents));
    }

    private static async Task<Results<Ok<FeatureDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        FeatureUpdateRequest request,
        HttpContext http,
        SilexGisDbContext db,
        FeatureWriteService writeService,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        FeatureProtection protection,
        IOptions<AccessOptions> accessOptions,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var feature = await db.Features.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null || await IsWithheldAsync(protection, ctx, feature, ct))
        {
            return ApiProblems.NotFound("feature.not_found");
        }

        if (ctx is null || !(await access.DecideAsync(ctx, AccessAction.Write, feature, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, feature, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("feature.not_found");
        }

        if (feature.Kind != FeatureKind.Generic)
        {
            return ApiProblems.BadRequest(
                "feature.kind_not_generic",
                "Caves, entrances and centerlines are edited through their typed endpoints.");
        }

        await using var hold = await Concurrency.HoldIfMatchAsync(http, db, VersionedTable.Features, feature.Id, ct, required: true);
        if (hold.Refusal is { } stale)
        {
            return stale;
        }

        var featureType = await db.FeatureTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.FeatureTypeId, ct);
        if (featureType is null)
        {
            return ApiProblems.BadRequest("feature.type_required", "Unknown feature type.");
        }

        if (featureType.BoundKind != FeatureKind.Generic)
        {
            return ApiProblems.BadRequest(
                FeatureWriteService.TypeBoundCode,
                $"Kind '{featureType.Code}' is the type of every {featureType.BoundKind} and of nothing else.");
        }

        // A rule may reach only some types, so the Write decided above, on the row as it is
        // stored, says nothing about the type it is being turned into: someone who may edit
        // sinkholes alone would otherwise make a row of a type they may neither create nor edit,
        // and a type-scoped deny on reading that type would then hide it from everyone else.
        // Asked on the same row's facts with the new type, so ownership, club, ancestry and set
        // membership weigh exactly as they did for the old one.
        if (request.FeatureTypeId != feature.FeatureTypeId)
        {
            var asConverted = (await access.FactsOfAsync(feature, ct)) with { FeatureTypeId = request.FeatureTypeId };
            if (!AccessEvaluator.Decide(ctx, AccessDomain.Features, AccessAction.Write, asConverted).Allowed)
            {
                return ApiProblems.Forbidden(
                    TypeChangeForbiddenCode, "You may not edit features of the type you are changing this one into.");
            }
        }

        // Binding is checked only when it changes: the edit form sends the feature's current
        // club back with every save, and keeping a binding the feature already has asks nothing
        // beyond the Write decided above — an editor with domain-wide rights who is not in that
        // club, or an owner who has since left it, still edits the feature.
        if (request.CavingGroupId is { } club
            && club != feature.CavingGroupId
            && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.Features, club))
        {
            return ApiProblems.Forbidden(CavingGroupBindingRules.ForbiddenCode);
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

        if (featureType.RequiresParent && !await db.FeatureHierarchyEdges.AnyAsync(e => e.ChildId == feature.Id, ct))
        {
            return ApiProblems.BadRequest(
                "feature.parent_required",
                $"Kind '{featureType.Code}' only exists inside a containing feature.");
        }

        var exact = (await protection.ExactViewIdsAsync(ctx, [feature.Id], ct)).Contains(feature.Id);

        // The geometry the row will hold is checked against the type it will have, not only a
        // geometry the request sent: a caller who was shown none keeps the stored one, and a type
        // change must not leave an outline under a kind that is only ever a point. The refusal
        // names the class only to a caller who may see the geometry: to one who was shown a
        // snapped point, "polygon" would describe the protected shape they were not given.
        var storedGeom = exact ? geom : feature.Geom;
        if (storedGeom is not null
            && (GeometryClasses.Of(storedGeom) is not { } geometryClass
                || !featureType.AcceptedGeometryClasses.Contains(geometryClass)))
        {
            return ApiProblems.BadRequest(
                "feature.geometry_invalid",
                exact
                    ? $"Kind '{featureType.Code}' does not accept {storedGeom.GeometryType} geometries."
                    : $"Kind '{featureType.Code}' does not accept the geometry this feature holds.");
        }

        feature.Name = request.Name;
        feature.FeatureTypeId = request.FeatureTypeId;
        feature.Category = featureType.Category;
        feature.Description = request.Description;
        feature.Properties = RawProperties(request.Properties);
        feature.CavingGroupId = request.CavingGroupId;
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
        await hold.AnswerAsync(ct);

        var parents = await PrimaryChainAsync(db, ctx, feature, ct);
        return TypedResults.Ok(feature.ToDto(
            featureType.Code, FeatureMapping.DisplayPolicy(feature, featureType), exact,
            accessOptions.Value.LocationGridMeters, parents));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        FeatureWriteService writeService,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        FeatureProtection protection,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        // No tracking: the soft delete writes directly to the database, and a stale
        // tracked copy would confuse the cave-mirror recount the write service runs after it.
        var feature = await db.Features.AsNoTracking()
            .Include(f => f.Entrance)
            .Include(f => f.Centerline)
            .FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null || ctx is null || !(await access.DecideAsync(ctx, AccessAction.Read, feature, ct)).Allowed)
        {
            return ApiProblems.NotFound("feature.not_found");
        }

        // A protected centerline does not exist for a caller without exact view, as its read
        // says; a 403 here would disclose that the cave has a survey at all.
        if (await IsWithheldAsync(protection, ctx, feature, ct))
        {
            return ApiProblems.NotFound("feature.not_found");
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Delete, feature, ct)).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        // An entrance's or a centerline's delete also writes its cave (the entrance mirror, the
        // default shape), so the cave is locked first, in the order its own kind's routes take.
        var caveId = feature.Entrance?.CaveFeatureId ?? feature.Centerline?.CaveFeatureId;

        // And it asks what the kind's own address asks: the right to edit that cave. A child
        // copies its cave's owner and club when it is made and keeps them afterwards, so the
        // right to delete the child alone can outlive the right over the cave whose main entrance,
        // entrance count and current survey this delete recomputes.
        if (caveId is { } container)
        {
            var cave = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == container, ct);
            if (cave is null || !(await access.DecideAsync(ctx, AccessAction.Write, cave, ct)).Allowed)
            {
                return ApiProblems.Forbidden();
            }
        }
        await using var hold = await Concurrency.HoldIfMatchAsync(
            http, db, VersionedTable.Features, feature.Id, ct, containerId: caveId);
        if (hold.Refusal is { } stale)
        {
            return stale;
        }

        // The kind's own rule, the same one its typed address runs: a centerline hands the
        // cave's shape on before it goes, an entrance leaves the cave's mirror recomputed.
        await writeService.DeleteFeatureAsync(feature.Id, ct);

        // The soft delete stamps rows immediately, but its audit entries ride the context.
        await db.SaveChangesAsync(ct);
        await hold.CommitAsync(ct);
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
    /// The feature's primary-parent chain (outermost ancestor first) as breadcrumb rows.
    /// The walk and its truncation at the first unreadable ancestor belong to the shared
    /// rule; this only dresses the answer in this surface's DTO.
    /// </summary>
    internal static async Task<IReadOnlyList<FeatureBreadcrumbDto>> PrimaryChainAsync(
        SilexGisDbContext db, AccessContext ctx, Feature feature, CancellationToken ct)
    {
        var chain = await FeaturePrimaryChains.OfAsync(
            db, ctx, new FeaturePrimaryChains.Subject(feature.Id, feature.AncestorIds), ct);
        return [.. chain.Select(step => new FeatureBreadcrumbDto(step.Id, step.Name))];
    }

    /// <summary>
    /// Whether the feature does not exist for this caller although they may read its row: a
    /// protected centerline traces the cave's course, cannot be served obfuscated, and is
    /// withheld whole from a caller without exact view. Every address of the feature answers
    /// such a caller as it answers one for an id that names nothing — a 403 or a list of its
    /// links would say that the cave has a survey at all.
    /// </summary>
    internal static async Task<bool> IsWithheldAsync(
        FeatureProtection protection, AccessContext? ctx, Feature feature, CancellationToken ct) =>
        feature.Kind == FeatureKind.Centerline
        && !(await protection.ExactViewIdsAsync(ctx, [feature.Id], ct)).Contains(feature.Id);

    internal static ProblemHttpResult WriteProblem(FeatureWriteException ex) =>
        ApiProblems.BadRequest(ex.Code, string.Join(" ", ex.Errors));

    internal static string RawProperties(JsonElement? properties) =>
        properties is { ValueKind: JsonValueKind.Object } p ? p.GetRawText() : "{}";
}
