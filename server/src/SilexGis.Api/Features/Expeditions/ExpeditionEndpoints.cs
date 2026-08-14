// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Expeditions;

public static class ExpeditionEndpoints
{
    private const string NotFoundCode = "expedition.not_found";

    public static RouteGroupBuilder MapExpeditionEndpoints(this RouteGroupBuilder api)
    {
        var expeditions = api.MapGroup("/expeditions").WithTags("Expeditions");

        expeditions.MapGet("/", ListAsync)
            .WithSummary("Paged expeditions, most recent first; visibility-filtered.");
        expeditions.MapGet("/{id:guid}", GetAsync)
            .WithSummary("A single expedition.");
        expeditions.MapPost("/", CreateAsync).WithValidation<ExpeditionWriteRequest>()
            .WithSummary("Creates an expedition (Create permission); the caller becomes owner.");
        expeditions.MapPut("/{id:guid}", UpdateAsync).WithValidation<ExpeditionWriteRequest>()
            .WithSummary("Full update (Write permission). The lifecycle state is not part of it.");
        expeditions.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes an expedition and the rules anchored on it.");
        expeditions.MapPost("/{id:guid}/state", TransitionAsync)
            .WithValidation<ExpeditionTransitionRequest>()
            .WithSummary(
                "Moves an expedition to another lifecycle state (Write permission). One endpoint "
                + "rather than a verb per state: a camp has eight states and the moves between "
                + "them are a table, not a handful of named acts.");

        return api;
    }

    private static async Task<Results<Ok<PagedResult<ExpeditionDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var query = db.Expeditions.AsNoTracking().VisibleTo(ctx, AccessDomain.Expeditions);

        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(ct);

        // The tie-break is the primary key, and it has to be: two camps starting the same day are
        // ordinary, and an order that does not distinguish them lets a row appear on two pages or
        // on none as the database chooses. Sorting on a timestamp instead is the same bug one
        // step further away — two rows written in the same tick tie again. The identifiers are
        // time-ordered, so descending by id reads as "the one entered later first" among camps
        // that start together, which is the answer somebody paging a list expects.
        var rows = await query
            .OrderByDescending(x => x.StartDate).ThenByDescending(x => x.Id)
            .Skip((p - 1) * size).Take(size).ToListAsync(ct);

        return TypedResults.Ok(new PagedResult<ExpeditionDto>([.. rows.Select(Map)], p, size, total));
    }

    private static async Task<Results<Ok<ExpeditionDto>, ProblemHttpResult>> GetAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var expedition = await db.Expeditions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (expedition is null || !(await access.DecideAsync(ctx, AccessAction.Read, expedition, ct)).Allowed)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        await Concurrency.EmitETagAsync(http, db, VersionedTable.Expeditions, expedition.Id, ct);
        return TypedResults.Ok(Map(expedition));
    }

    private static async Task<Results<Created<ExpeditionDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        ExpeditionWriteRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!CreateRules.MayCreate(ctx, AccessDomain.Expeditions, request.CavingGroupId))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        if (ValidateReferences(ctx, request) is { } problem)
        {
            return problem;
        }

        var expedition = new Expedition { Name = request.Name, OwnerUserId = user.UserId };
        Apply(expedition, request);
        db.Expeditions.Add(expedition);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/expeditions/{expedition.Id}", Map(expedition));
    }

    private static async Task<Results<Ok<ExpeditionDto>, ProblemHttpResult>> UpdateAsync(
        Guid id,
        ExpeditionWriteRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var expedition = await db.Expeditions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (expedition is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (await RefuseUnlessWritableAsync(access, ctx, expedition, ct) is { } refusal)
        {
            return refusal;
        }

        if (await Concurrency.CheckIfMatchAsync(
                http, db, VersionedTable.Expeditions, expedition.Id, ct, required: true) is { } stale)
        {
            return stale;
        }

        if (ValidateReferences(ctx!, request) is { } problem)
        {
            return problem;
        }

        Apply(expedition, request);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(Map(expedition));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var expedition = await db.Expeditions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (expedition is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (ctx is null || !(await access.DecideAsync(ctx, AccessAction.Delete, expedition, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, expedition, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound(NotFoundCode);
        }

        // The precondition is offered but not required, the way it is on every delete here: a
        // list or a map deletes a row it never loaded a version of, so demanding one would make
        // the ordinary delete impossible from the surfaces that do it most.
        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.Expeditions, expedition.Id, ct) is { } stale)
        {
            return stale;
        }

        // A rule anchored on this camp means nothing once the camp is gone, and a rule whose
        // anchor cannot be resolved is exactly what the integrity check reports as an orphan.
        // Deleting them here is what keeps a routine delete from leaving one behind.
        //
        // Loaded and removed rather than deleted in one statement, because a rule disappearing
        // is a change to who may reach what, and every other place rules are withdrawn records
        // that. A set-based delete never reaches the change tracker, so the withdrawal would
        // happen with nothing in the trail to say it had.
        var anchored = await db.AccessEntries
            .Where(e => e.Domain == AccessDomain.Expeditions
                && e.ScopeKind == AccessScopeKind.Object
                && e.ScopeId == expedition.Id)
            .ToListAsync(ct);
        db.AccessEntries.RemoveRange(anchored);

        db.Expeditions.Remove(expedition);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Moves an expedition to another lifecycle state. Which moves exist is not decided here —
    /// the transition table is the one place that knows, so a state a camp may not hold and a
    /// move it may not make are refused by the same rule and with the same code.
    /// </summary>
    /// <remarks>
    /// The precondition is required exactly as it is on a full update: announcing a camp, or
    /// calling one off, acts on the version somebody read, and acting on one that changed
    /// underneath them is the lost update the header exists to prevent.
    /// </remarks>
    private static async Task<Results<Ok<ExpeditionDto>, ProblemHttpResult>> TransitionAsync(
        Guid id,
        ExpeditionTransitionRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var expedition = await db.Expeditions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (expedition is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (await RefuseUnlessWritableAsync(access, ctx, expedition, ct) is { } refusal)
        {
            return refusal;
        }

        if (await Concurrency.CheckIfMatchAsync(
                http, db, VersionedTable.Expeditions, expedition.Id, ct, required: true) is { } stale)
        {
            return stale;
        }

        // One question, not two: a target the vocabulary admits but an expedition may not hold
        // appears in no pair of the table, so asking the table refuses it for the same reason and
        // under the same code as an illegal move. Asking whether the state is an admitted one
        // first would be a second rule saying the same thing, free to drift from it.
        //
        // The state is present because the validator filter runs before this and requires it; a
        // body that names none is a 400 and never arrives here.
        var target = request.State!.Value;
        if (!ActivityStates.MayExpeditionTransition(expedition.State, target))
        {
            return ApiProblems.Conflict(
                ActivityStates.ExpeditionTransitionInvalidCode,
                $"An expedition does not move from {expedition.State} to {target}.");
        }

        expedition.State = target;
        if (target == ActivityState.Published)
        {
            // Stamped the first time only: de-announcing and announcing again does not rewrite
            // the day the camp was first made known.
            expedition.PublishedAt ??= DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(Map(expedition));
    }

    /// <summary>
    /// The refusal a caller who may not write this camp gets: 403 when they can see it, 404 when
    /// they cannot — so a refusal never tells somebody a camp exists that they may not read.
    /// </summary>
    private static async Task<ProblemHttpResult?> RefuseUnlessWritableAsync(
        IAccessService access, AccessContext? ctx, Expedition expedition, CancellationToken ct)
    {
        if (ctx is not null && (await access.DecideAsync(ctx, AccessAction.Write, expedition, ct)).Allowed)
        {
            return null;
        }

        return (await access.DecideAsync(ctx, AccessAction.Read, expedition, ct)).Allowed
            ? ApiProblems.Forbidden()
            : ApiProblems.NotFound(NotFoundCode);
    }

    /// <summary>Geometry validity and the club binding — the checks a request's own shape cannot carry.</summary>
    private static ProblemHttpResult? ValidateReferences(AccessContext ctx, ExpeditionWriteRequest request)
    {
        if (request.Geom is not null && request.Geom.ToGeometryOrNull() is null)
        {
            return ApiProblems.BadRequest("expedition.geometry_invalid", "Geometry is malformed or invalid.");
        }

        if (request.CavingGroupId is not null
            && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.Expeditions, request.CavingGroupId.Value))
        {
            return ApiProblems.Forbidden(CavingGroupBindingRules.ForbiddenCode);
        }

        return null;
    }

    private static void Apply(Expedition expedition, ExpeditionWriteRequest request)
    {
        expedition.Name = request.Name;
        expedition.Description = request.Description;
        expedition.StartDate = request.StartDate;
        expedition.EndDate = DayRange.EndForStorage(request.StartDate, request.EndDate);
        expedition.Geom = request.Geom?.ToGeometryOrNull();
        expedition.CavingGroupId = request.CavingGroupId;
        expedition.Visibility = request.Visibility;
    }

    private static ExpeditionDto Map(Expedition expedition) => new()
    {
        Id = expedition.Id,
        Name = expedition.Name,
        Description = expedition.Description,
        StartDate = expedition.StartDate,
        EndDate = expedition.EndDate,
        Geom = expedition.Geom is null ? null : GeoJsonGeometry.From(expedition.Geom),
        OwnerUserId = expedition.OwnerUserId,
        CavingGroupId = expedition.CavingGroupId,
        Visibility = expedition.Visibility,
        State = expedition.State,
        PublishedAt = expedition.PublishedAt,
        CreatedAt = expedition.CreatedAt,
        UpdatedAt = expedition.UpdatedAt,
    };
}
