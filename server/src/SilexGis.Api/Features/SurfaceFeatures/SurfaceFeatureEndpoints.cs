// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.SurfaceFeatures;

public static class SurfaceFeatureEndpoints
{
    public static RouteGroupBuilder MapSurfaceFeatureEndpoints(this RouteGroupBuilder api)
    {
        var features = api.MapGroup("/surface-features").WithTags("SurfaceFeatures");

        features.MapGet("/", ListAsync)
            .WithSummary("Paged surface-feature list with filters; visibility-filtered.");
        features.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Single surface feature.");
        features.MapPost("/", CreateAsync).WithValidation<SurfaceFeatureWriteRequest>()
            .WithSummary("Creates a surface feature (Editor role and above); the caller becomes owner.");
        features.MapPut("/{id:guid}", UpdateAsync).WithValidation<SurfaceFeatureWriteRequest>()
            .WithSummary("Full update (Write permission).");
        features.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes a surface feature (Delete permission).");

        return api;
    }

    private static async Task<Results<Ok<PagedResult<SurfaceFeatureDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        int? page,
        int? pageSize,
        long? featureTypeId,
        Guid? caveId,
        string? search,
        string? bbox,
        string? tag,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var query = db.SurfaceFeatures.AsNoTracking().VisibleTo(user);

        if (!string.IsNullOrWhiteSpace(tag))
        {
            query = query.Where(f => db.Taggings.Any(tg =>
                tg.EntityType == AttachedEntityType.SurfaceFeature && tg.EntityId == f.Id
                && db.Tags.Any(t => t.Id == tg.TagId && t.Slug == tag)));
        }

        if (featureTypeId is not null)
        {
            query = query.Where(f => f.FeatureTypeId == featureTypeId);
        }

        if (caveId is not null)
        {
            // Filtering by a cave whose exact location the caller may not see would
            // reveal it through the matched features' geometries — behave as if no
            // features are linked to it.
            if (await CaveLinkRedaction.ShouldRedactAsync(db, user, caveId, ct))
            {
                var (emptyPage, emptySize) = Paging.Normalize(page, pageSize);
                return TypedResults.Ok(new PagedResult<SurfaceFeatureDto>([], emptyPage, emptySize, 0));
            }

            query = query.Where(f => f.CaveId == caveId);
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
            query = query.Where(f => f.Geom.Intersects(polygon));
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(f => f.UpdatedAt)
            .Skip((p - 1) * size).Take(size).ToListAsync(ct);

        var redacted = await CaveLinkRedaction.RedactedCaveIdsAsync(
            db, user, rows.Where(f => f.CaveId is not null).Select(f => f.CaveId!.Value), ct);
        var items = rows
            .Select(f => f.ToDto(redactCaveLink: f.CaveId is not null && redacted.Contains(f.CaveId.Value)))
            .ToList();

        return TypedResults.Ok(new PagedResult<SurfaceFeatureDto>(items, p, size, total));
    }

    private static async Task<Results<Ok<SurfaceFeatureDto>, ProblemHttpResult>> GetAsync(
        Guid id,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var feature = await db.SurfaceFeatures.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null || !PermissionEvaluator.Can(user, feature, ObjectPermission.Read))
        {
            // Existence of a feature the caller cannot read is not disclosed.
            return ApiProblems.NotFound("surface_feature.not_found");
        }

        var redact = await CaveLinkRedaction.ShouldRedactAsync(db, user, feature.CaveId, ct);
        return TypedResults.Ok(feature.ToDto(redactCaveLink: redact));
    }

    private static async Task<Results<Created<SurfaceFeatureDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        SurfaceFeatureWriteRequest request,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!user.CanCreateContent)
        {
            return ApiProblems.Forbidden("surface_feature.create_requires_editor");
        }

        var validation = await ValidateReferencesAsync(db, user, request, ct);
        if (validation is not null)
        {
            return validation;
        }

        var geom = request.Geometry.ToGeometryOrNull()!; // ValidateReferencesAsync guarantees non-null

        var feature = new SurfaceFeature { Geom = geom, OwnerUserId = user.UserId };
        request.Apply(feature, geom);
        feature.OwnerUserId = user.UserId; // Apply() must never change ownership
        db.SurfaceFeatures.Add(feature);
        await db.SaveChangesAsync(ct);

        var redact = await CaveLinkRedaction.ShouldRedactAsync(db, user, feature.CaveId, ct);
        return TypedResults.Created($"/api/v1/surface-features/{feature.Id}", feature.ToDto(redactCaveLink: redact));
    }

    private static async Task<Results<Ok<SurfaceFeatureDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        SurfaceFeatureWriteRequest request,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var feature = await db.SurfaceFeatures.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null)
        {
            return ApiProblems.NotFound("surface_feature.not_found");
        }

        if (user is null || !PermissionEvaluator.Can(user, feature, ObjectPermission.Write))
        {
            return PermissionEvaluator.Can(user, feature, ObjectPermission.Read)
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("surface_feature.not_found");
        }

        var validation = await ValidateReferencesAsync(db, user, request, ct);
        if (validation is not null)
        {
            return validation;
        }

        request.Apply(feature, request.Geometry.ToGeometryOrNull()!);
        await db.SaveChangesAsync(ct);
        var redact = await CaveLinkRedaction.ShouldRedactAsync(db, user, feature.CaveId, ct);
        return TypedResults.Ok(feature.ToDto(redactCaveLink: redact));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var feature = await db.SurfaceFeatures.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null)
        {
            return ApiProblems.NotFound("surface_feature.not_found");
        }

        if (user is null || !PermissionEvaluator.Can(user, feature, ObjectPermission.Delete))
        {
            return PermissionEvaluator.Can(user, feature, ObjectPermission.Read)
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("surface_feature.not_found");
        }

        // Polymorphic attachment rows have no FK to the feature — clean them up in the
        // same transaction as the entity.
        await db.Attachments
            .Where(a => a.EntityType == AttachedEntityType.SurfaceFeature && a.EntityId == feature.Id)
            .ExecuteDeleteAsync(ct);
        db.SurfaceFeatures.Remove(feature);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Cross-entity rules that need the database: geometry parses and matches the feature
    /// type's geometry kind; team binding requires membership; a linked cave must be
    /// visible to the caller (an invisible cave is reported as not found, not forbidden).
    /// </summary>
    private static async Task<ProblemHttpResult?> ValidateReferencesAsync(
        SilexGisDbContext db, UserContext user, SurfaceFeatureWriteRequest request, CancellationToken ct)
    {
        Geometry? geom = request.Geometry.ToGeometryOrNull();
        if (geom is null)
        {
            return ApiProblems.BadRequest("surface_feature.geometry_invalid", "Geometry is malformed or invalid.");
        }

        var featureType = await db.FeatureTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.FeatureTypeId, ct);
        if (featureType is null)
        {
            return ApiProblems.BadRequest("surface_feature.feature_type_unknown", "Unknown feature type.");
        }

        if (!GeoJsonGeometry.MatchesKind(geom, featureType.GeometryKind))
        {
            return ApiProblems.BadRequest(
                "surface_feature.geometry_kind_mismatch",
                $"Feature type '{featureType.Code}' does not accept {geom.GeometryType} geometries.");
        }

        if (request.TeamId is not null && !user.IsAdmin && !user.IsMemberOf(request.TeamId.Value))
        {
            return ApiProblems.Forbidden("surface_feature.team_membership_required");
        }

        if (request.CaveId is not null)
        {
            var cave = await db.Caves.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CaveId, ct);
            if (cave is null || !PermissionEvaluator.Can(user, cave, ObjectPermission.Read))
            {
                return ApiProblems.BadRequest("surface_feature.cave_not_found", "Linked cave does not exist.");
            }
        }

        return null;
    }
}
