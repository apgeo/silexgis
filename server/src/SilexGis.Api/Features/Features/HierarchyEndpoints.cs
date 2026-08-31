// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Features;

/// <summary>
/// Containment-hierarchy edges of a feature (a DAG with exactly one primary edge per
/// feature). Edge edits go through the feature write service, which enforces cycle
/// prevention and recomputes the derived ancestor state.
/// </summary>
public static class HierarchyEndpoints
{
    public static RouteGroupBuilder MapFeatureHierarchyEndpoints(this RouteGroupBuilder api)
    {
        var features = api.MapGroup("/features").WithTags("Features");

        features.MapGet("/{id:guid}/parents", GetParentsAsync)
            .WithSummary("The feature's parent edges (readable parents only).");
        features.MapPut("/{id:guid}/parents", SetParentsAsync).WithValidation<SetParentsRequest>()
            .WithSummary(
                "Replaces the feature's parent edges (Write permission; exactly one primary edge, "
                + "and never an empty list for a kind that only exists inside a containing feature).");
        features.MapGet("/{id:guid}/children", GetChildrenAsync)
            .WithSummary("Paged children of the feature; visibility-filtered.");

        return api;
    }

    private static async Task<Results<Ok<List<FeatureParentDto>>, ProblemHttpResult>> GetParentsAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var feature = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null || !(await access.DecideAsync(ctx, AccessAction.Read, feature, ct)).Allowed)
        {
            return ApiProblems.NotFound("feature.not_found");
        }

        return TypedResults.Ok(await ParentsViewAsync(db, ctx!, id, ct));
    }

    private static async Task<Results<Ok<List<FeatureParentDto>>, UnauthorizedHttpResult, ProblemHttpResult>> SetParentsAsync(
        Guid id,
        SetParentsRequest request,
        HttpContext http,
        SilexGisDbContext db,
        FeatureWriteService writeService,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        FeatureProtection protection,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var feature = await db.Features.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null)
        {
            return ApiProblems.NotFound("feature.not_found");
        }

        if (ctx is null || !(await access.DecideAsync(ctx, AccessAction.Write, feature, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, feature, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("feature.not_found");
        }

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.Features, feature.Id, ct) is { } stale)
        {
            return stale;
        }

        var parentIds = request.Parents.Select(p => p.ParentId).Distinct().ToArray();
        if (parentIds.Length > 0)
        {
            var visibleParents = await db.Features.AsNoTracking().VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                .Where(f => parentIds.Contains(f.Id)).Select(f => f.Id).ToListAsync(ct);
            if (visibleParents.Count != parentIds.Length)
            {
                // A parent the caller cannot read is reported exactly like a missing one.
                return ApiProblems.BadRequest("feature.parent_not_found", "A parent feature does not exist.");
            }
        }

        // Re-parenting is a protected write: moving a feature out of a protected subtree
        // publishes its exact coordinates, and moving one in subjects it to roots the
        // caller cannot evaluate. Both the current ancestry (the feature's own exact
        // view) and every requested parent's ancestry must be exactly viewable.
        var involved = parentIds.Append(feature.Id).ToList();
        var exactViewable = await protection.ExactViewIdsAsync(ctx, involved, ct);
        if (!exactViewable.Contains(feature.Id) || parentIds.Any(p => !exactViewable.Contains(p)))
        {
            return ApiProblems.Forbidden();
        }

        try
        {
            await writeService.SetParentsAsync(
                feature.Id, request.Parents.Select(p => new ParentSpec(p.ParentId, p.IsPrimary)).ToList(), ct);
        }
        catch (FeatureWriteException ex)
        {
            return FeatureEndpoints.WriteProblem(ex);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ParentsViewAsync(db, ctx, id, ct));
    }

    private static async Task<Results<Ok<PagedResult<FeatureChildDto>>, ProblemHttpResult>> GetChildrenAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        FeatureProtection protection,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var feature = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null || !(await access.DecideAsync(ctx, AccessAction.Read, feature, ct)).Allowed)
        {
            return ApiProblems.NotFound("feature.not_found");
        }

        var query = db.FeatureHierarchyEdges.AsNoTracking()
            .Where(e => e.ParentId == id)
            .Join(
                db.Features.AsNoTracking().VisibleTo(ctx!, db.Features, db.FeatureSetMembers),
                e => e.ChildId,
                f => f.Id,
                (e, f) => new { f.Id, f.Kind, f.Name, f.IsProtectedEffective, e.IsPrimary });

        // Protected centerline children are withheld entirely for callers without exact
        // view — even their existence is not disclosed. Excluded before counting.
        var protectedCenterlineIds = await query
            .Where(x => x.Kind == FeatureKind.Centerline && x.IsProtectedEffective)
            .Select(x => x.Id)
            .ToListAsync(ct);
        if (protectedCenterlineIds.Count > 0)
        {
            var exactCenterlines = await protection.ExactViewIdsAsync(ctx, protectedCenterlineIds, ct);
            var withheld = protectedCenterlineIds.Where(x => !exactCenterlines.Contains(x)).ToArray();
            if (withheld.Length > 0)
            {
                query = query.Where(x => !withheld.Contains(x.Id));
            }
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((p - 1) * size).Take(size).ToListAsync(ct);
        var items = rows.Select(x => new FeatureChildDto(x.Id, x.Kind, x.Name, x.IsPrimary)).ToList();

        return TypedResults.Ok(new PagedResult<FeatureChildDto>(items, p, size, total));
    }

    /// <summary>The feature's parent edges joined with readable parent rows (primary edge first).</summary>
    private static async Task<List<FeatureParentDto>> ParentsViewAsync(
        SilexGisDbContext db, AccessContext ctx, Guid id, CancellationToken ct)
    {
        var rows = await db.FeatureHierarchyEdges.AsNoTracking()
            .Where(e => e.ChildId == id)
            .Join(
                db.Features.AsNoTracking().VisibleTo(ctx, db.Features, db.FeatureSetMembers),
                e => e.ParentId,
                f => f.Id,
                (e, f) => new { f.Id, f.Name, e.IsPrimary })
            .ToListAsync(ct);
        return rows
            .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new FeatureParentDto(x.Id, x.Name, x.IsPrimary))
            .ToList();
    }
}
