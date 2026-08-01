// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Api.Features.Permissions;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.FeatureSets;

/// <summary>
/// Named sets of features that rules can hang on, independent of the containment
/// hierarchy — the way to say "these particular caves" when no area contains exactly
/// them, and the way to express a narrowing the subtree scopes deliberately refuse.
/// </summary>
/// <remarks>
/// Membership here IS access: adding a feature to a set silently moves whatever rules
/// name that set. So sets are their own resource domain rather than a corner of the
/// feature one, membership edits are audited like rule edits, and a set cannot be
/// deleted while a rule still points at it.
/// </remarks>
public static class FeatureSetEndpoints
{
    public const string NotFoundCode = "feature_set.not_found";
    public const string NameTakenCode = "feature_set.name_taken";
    public const string InUseCode = "feature_set.in_use";

    public static RouteGroupBuilder MapFeatureSetEndpoints(this RouteGroupBuilder api)
    {
        var sets = api.MapGroup("/feature-sets").WithTags("FeatureSets");

        sets.MapGet("/", ListAsync).WithSummary("All feature sets with their sizes.");
        sets.MapGet("/{id:guid}", GetAsync).WithSummary("One feature set.");
        sets.MapGet("/{id:guid}/members", GetMembersAsync)
            .WithSummary("The features in the set the caller may read.");
        sets.MapPost("/", CreateAsync).WithValidation<FeatureSetWriteRequest>()
            .WithSummary("Creates a feature set.");
        sets.MapPut("/{id:guid}", UpdateAsync).WithValidation<FeatureSetWriteRequest>()
            .WithSummary("Renames or re-describes a feature set.");
        sets.MapPut("/{id:guid}/members", ReplaceMembersAsync)
            .WithValidation<FeatureSetMemberReplaceRequest>()
            .WithSummary("Replaces the set's membership (audited — this moves access).");
        sets.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes a feature set, unless a rule still points at it.");

        return api;
    }

    private static async Task<Results<Ok<List<FeatureSetDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Holds(ctx, AccessAction.Read))
        {
            return ApiProblems.Forbidden();
        }

        return TypedResults.Ok(await Project(db.FeatureSets.AsNoTracking().OrderBy(s => s.Name), db).ToListAsync(ct));
    }

    private static async Task<Results<Ok<FeatureSetDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id, SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Holds(ctx, AccessAction.Read, id))
        {
            return ApiProblems.Forbidden();
        }

        var set = await Project(db.FeatureSets.AsNoTracking().Where(s => s.Id == id), db).FirstOrDefaultAsync(ct);
        return set is null ? ApiProblems.NotFound(NotFoundCode) : TypedResults.Ok(set);
    }

    /// <summary>
    /// The set's features, filtered by what the caller may read. A set is a security
    /// anchor, not a way to enumerate rows somebody cannot otherwise see, so the count
    /// on the set itself and this list can legitimately differ.
    /// </summary>
    private static async Task<Results<Ok<List<Guid>>, UnauthorizedHttpResult, ProblemHttpResult>> GetMembersAsync(
        Guid id, SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Holds(ctx, AccessAction.Read, id))
        {
            return ApiProblems.Forbidden();
        }

        if (!await db.FeatureSets.AnyAsync(s => s.Id == id, ct))
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var ids = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => db.FeatureSetMembers.Any(m => m.FeatureSetId == id && m.FeatureId == f.Id))
            .Select(f => f.Id)
            .ToListAsync(ct);
        return TypedResults.Ok(ids);
    }

    private static async Task<Results<Created<FeatureSetDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        FeatureSetWriteRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!CreateRules.MayCreate(ctx, AccessDomain.FeatureSets))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        var slug = Tag.Slugify(request.Name);
        if (slug.Length == 0)
        {
            return ApiProblems.BadRequest("feature_set.name_invalid",
                "The name must contain letters or digits.");
        }

        if (await db.FeatureSets.AnyAsync(s => s.Slug == slug || s.Name == request.Name.Trim(), ct))
        {
            return ApiProblems.BadRequest(NameTakenCode, "A feature set with this name already exists.");
        }

        var set = new FeatureSet { Name = request.Name.Trim(), Slug = slug, Description = request.Description };
        db.FeatureSets.Add(set);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/feature-sets/{set.Id}",
            new FeatureSetDto(set.Id, set.Name, set.Slug, set.Description, 0));
    }

    private static async Task<Results<Ok<FeatureSetDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        FeatureSetWriteRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var set = await db.FeatureSets.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (set is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!Holds(ctx, AccessAction.Write, id))
        {
            return ApiProblems.Forbidden();
        }

        set.Name = request.Name.Trim();
        set.Description = request.Description;
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(await Project(db.FeatureSets.AsNoTracking().Where(s => s.Id == id), db).FirstAsync(ct));
    }

    /// <summary>
    /// Replaces the membership. Written as an explicit audit event because the junction
    /// table carries no identity of its own: without this, the one edit in the model that
    /// moves access without touching a rule would be the one edit with no trail.
    /// </summary>
    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> ReplaceMembersAsync(
        Guid id,
        FeatureSetMemberReplaceRequest request,
        SilexGisDbContext db,
        ICurrentUser currentUser,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!await db.FeatureSets.AnyAsync(s => s.Id == id, ct))
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!Holds(ctx, AccessAction.Write, id))
        {
            return ApiProblems.Forbidden();
        }

        var requested = request.FeatureIds.Distinct().ToList();
        var known = await db.Features.AsNoTracking()
            .Where(f => requested.Contains(f.Id))
            .Select(f => f.Id)
            .ToListAsync(ct);
        if (known.Count != requested.Count)
        {
            return ApiProblems.BadRequest("feature_set.feature_not_found", "A named feature does not exist.");
        }

        var current = await db.FeatureSetMembers.Where(m => m.FeatureSetId == id).ToListAsync(ct);
        var currentIds = current.Select(m => m.FeatureId).ToHashSet();
        var added = requested.Where(f => !currentIds.Contains(f)).ToList();
        var removed = currentIds.Where(f => !requested.Contains(f)).ToList();
        if (added.Count == 0 && removed.Count == 0)
        {
            return TypedResults.NoContent();
        }

        db.FeatureSetMembers.RemoveRange(current.Where(m => removed.Contains(m.FeatureId)));
        db.FeatureSetMembers.AddRange(added.Select(f => new FeatureSetMember { FeatureSetId = id, FeatureId = f }));

        db.Set<AuditEntry>().Add(new AuditEntry
        {
            UserId = currentUser.UserId,
            Action = AuditActions.PermissionChanged,
            EntityType = nameof(FeatureSet),
            EntityId = id.ToString(),
            Changes = System.Text.Json.JsonSerializer.Serialize(
                new Dictionary<string, Dictionary<string, object?>>
                {
                    ["Members"] = new()
                    {
                        ["old"] = currentIds.OrderBy(x => x).ToList(),
                        ["new"] = requested.OrderBy(x => x).ToList(),
                    },
                }),
        });

        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        Guid id, SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var set = await db.FeatureSets.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (set is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!Holds(ctx, AccessAction.Delete, id))
        {
            return ApiProblems.Forbidden();
        }

        // Deleting the anchor of a deny would cancel it silently — the one thing the whole
        // model refuses to let happen by side effect. Allows are refused too: dropping a
        // set out from under any rule should be an act somebody chose.
        if (await db.AccessEntries.AnyAsync(
                e => e.ScopeKind == AccessScopeKind.FeatureSet && e.ScopeId == id, ct))
        {
            return ApiProblems.Conflict(InUseCode, "Rules still point at this feature set; remove them first.");
        }

        db.FeatureSets.Remove(set);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static bool Holds(AccessContext ctx, AccessAction action, Guid? featureSetId = null) =>
        AccessEvaluator.Decide(
            ctx,
            AccessDomain.FeatureSets,
            action,
            featureSetId is { } id ? new AccessTargetFacts { ObjectId = id } : null).Allowed;

    /// <summary>
    /// Projects to the wire shape. Takes the source already ordered and filtered: sorting
    /// a projected record is not something the database can be asked to do.
    /// </summary>
    private static IQueryable<FeatureSetDto> Project(
        IQueryable<FeatureSet> source, SilexGisDbContext db) =>
        source.Select(s => new FeatureSetDto(
            s.Id, s.Name, s.Slug, s.Description,
            db.FeatureSetMembers.Count(m => m.FeatureSetId == s.Id)));
}
