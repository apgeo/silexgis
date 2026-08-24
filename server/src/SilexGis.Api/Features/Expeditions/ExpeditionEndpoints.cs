// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
    // Visible to the camp's other route files rather than private, so every door into a camp — the
    // camp itself, its trips, its roster — refuses a camp nobody may reach in the same words. A
    // second spelling of it is a second answer waiting to drift from this one.
    internal const string NotFoundCode = "expedition.not_found";

    // The trip's own refusal code, spelled here because it is a wire contract rather than
    // something the trips' handlers own: a caller told a trip is missing must be told it in the
    // same words wherever they asked.
    private const string TripNotFoundCode = "trip_log.not_found";

    private const string NotAMemberCode = "expedition.trip_not_in_expedition";

    // A lifecycle state the list was asked to narrow by that this application does not have.
    private const string StateInvalidCode = "expedition.state_invalid";

    private const string MembershipConflictCode = "expedition.trip_membership_conflict";

    // The index that carries "a trip is in at most one camp". Named here so a violation of that
    // one rule is told apart from any other write that happens to reach the database as a conflict.
    private const string MembershipUniqueIndex = "ix_expedition_trips_trip_log_id";

    public static RouteGroupBuilder MapExpeditionEndpoints(this RouteGroupBuilder api)
    {
        var expeditions = api.MapGroup("/expeditions").WithTags("Expeditions");

        expeditions.MapGet("/", ListAsync)
            .WithSummary(
                "Paged expeditions, most recent first; visibility-filtered. Narrowed by a date "
                + "window the camp overlaps, by a word in its name, and by lifecycle state.");
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

        expeditions.MapPost("/{id:guid}/trips", AddTripAsync).WithValidation<ExpeditionTripRequest>()
            .WithSummary(
                "Puts a trip in this camp. A trip belongs to at most one camp, so a trip that "
                + "was in another is moved out of it and the answer says so.");
        expeditions.MapDelete("/{id:guid}/trips/{tripLogId:guid}", RemoveTripAsync)
            .WithSummary("Takes a trip out of this camp. The trip itself is untouched.");

        // The trip's own side of the same relationship, mapped from here rather than beside the
        // trip's other routes. Joining and leaving are one rule with one set of refusals, and a
        // rule written out in two slices is a rule that drifts — the day one side grows a check
        // the other lacks is the day the same act is allowed from one page and refused from the
        // other. The camp is the thing that has members, so its slice answers for both doors.
        var trips = api.MapGroup("/trip-logs").WithTags("Expeditions");
        trips.MapPut("/{id:guid}/expedition", SetExpeditionAsync).WithValidation<TripExpeditionRequest>()
            .WithSummary(
                "Sets which camp a trip belongs to, or takes it out of one when no camp is "
                + "named. Putting it in a camp takes the right to write both; taking it out "
                + "takes the right to write the trip.");

        return api;
    }

    private static async Task<Results<Ok<PagedResult<ExpeditionDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        int? page,
        int? pageSize,
        DateOnly? from,
        DateOnly? to,
        string? search,
        string? state,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Lifecycle states arrive as the camelCase words the rest of the contract spells them
        // with, parsed here rather than by route binding: binding a bad word would answer with a
        // bare 400 carrying no code, and a client cannot tell that apart from any other refusal.
        // The parse is deliberately not "unknown means no filter" — a caller who asked for
        // something this application does not have wants to be told, not handed the whole list.
        ActivityState? stateFilter = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            if (!Enum.TryParse<ActivityState>(state, ignoreCase: true, out var stateValue)
                || !Enum.IsDefined(stateValue))
            {
                return ApiProblems.BadRequest(StateInvalidCode, $"Unknown state '{state}'.");
            }

            stateFilter = stateValue;
        }

        var query = db.Expeditions.AsNoTracking().VisibleTo(ctx, AccessDomain.Expeditions);

        query = query.OverlappingDays(x => x.StartDate, x => x.EndDate, from, to);

        if (stateFilter is { } wantedState)
        {
            query = query.Where(x => x.State == wantedState);
        }

        // Accent-insensitive, over the name only — the same reach the trip list gives its title.
        // A description is a paragraph, and a word that matches one is as likely to be a passing
        // mention as the camp somebody is looking for.
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(x =>
                EF.Functions.ILike(EF.Functions.Unaccent(x.Name), EF.Functions.Unaccent(pattern)));
        }

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
        // The rules the camp's sharing wrote onto the trips it gathered go with it too. They
        // are anchored on those trips, not on the camp, so nothing above finds them — the
        // marker is what does, and it is the only thing that can: a rule matching one of them
        // action for action may equally have been authored by hand on that trip's own tab, and
        // that one stays. Without this sweep the camp's grants would outlive the camp with no
        // surface left that could name, review or withdraw them.
        //
        // Loaded and removed rather than deleted in one statement, because a rule disappearing
        // is a change to who may reach what, and every other place rules are withdrawn records
        // that. A set-based delete never reaches the change tracker, so the withdrawal would
        // happen with nothing in the trail to say it had.
        var anchored = await db.AccessEntries
            .Where(e => (e.Domain == AccessDomain.Expeditions
                    && e.ScopeKind == AccessScopeKind.Object
                    && e.ScopeId == expedition.Id)
                || e.GrantedViaExpeditionId == expedition.Id)
            .ToListAsync(ct);
        db.AccessEntries.RemoveRange(anchored);

        // The polymorphic rows a camp can carry — files attached to it, tags on it, and its
        // place in a relation — have no foreign key to follow, so nothing removes them unless
        // this does. Left behind they are exactly what the integrity check reports as orphans,
        // and a tag on a camp that no longer exists would keep counting towards that tag's use.
        //
        // A relation the camp merely joined keeps whatever it still relates and goes only when
        // one member is left: an association with one end is a thing no surface offers and no
        // later edit would be accepted for. Remaining members cascade with it.
        //
        // A directed relation the camp was the distinguished member of goes whatever is left of
        // it: a directed link reads from its main member, and one with none is a state the link
        // rules refuse, so leaving it would leave a relation that renders on every other
        // member's panel and that no later edit of it — not even one appointing a new main —
        // would be accepted for.
        await db.Attachments
            .Where(a => a.EntityType == AttachedEntityType.Expedition && a.EntityId == expedition.Id)
            .ExecuteDeleteAsync(ct);
        await db.Taggings
            .Where(t => t.EntityType == AttachedEntityType.Expedition && t.EntityId == expedition.Id)
            .ExecuteDeleteAsync(ct);
        var memberships = await db.ResLinkMembers
            .Where(m => m.EntityType == AttachedEntityType.Expedition && m.EntityId == expedition.Id)
            .Select(m => new { m.ResLinkId, m.IsMain })
            .ToListAsync(ct);
        var linkIds = memberships.Select(m => m.ResLinkId).Distinct().ToList();
        var mainOfIds = memberships.Where(m => m.IsMain).Select(m => m.ResLinkId).Distinct().ToList();
        var headlessIds = mainOfIds.Count == 0
            ? []
            : await db.ResLinks
                .Where(l => mainOfIds.Contains(l.Id)
                    && db.ResLinkRelationTypes.Any(t => t.Id == l.RelationTypeId && t.Directed))
                .Select(l => l.Id)
                .ToListAsync(ct);
        await db.ResLinkMembers
            .Where(m => m.EntityType == AttachedEntityType.Expedition && m.EntityId == expedition.Id)
            .ExecuteDeleteAsync(ct);
        await db.ResLinks
            .Where(l => headlessIds.Contains(l.Id)
                || (linkIds.Contains(l.Id) && db.ResLinkMembers.Count(m => m.ResLinkId == l.Id) < 2))
            .ExecuteDeleteAsync(ct);

        // The membership rows go with the camp, and nothing else does: the trips it gathered
        // stand alone perfectly well and are what the people who wrote them still have. That is
        // carried by the membership row's own foreign keys, so a camp deleted by any route — a
        // handler, a repair script, a cascade from somewhere else — releases its trips rather
        // than taking them.
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
    /// Puts a trip in this camp.
    /// </summary>
    /// <remarks>
    /// Forming the membership takes the right to write <em>both</em> rows: the camp gains a
    /// member its roll-up counts, and the trip gains a camp it is shown as belonging to. Neither
    /// party is joined to the other by somebody with authority over only one of them.
    /// </remarks>
    private static async Task<Results<Ok<ExpeditionTripDto>, ProblemHttpResult>> AddTripAsync(
        Guid id,
        ExpeditionTripRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var expedition = await db.Expeditions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (expedition is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (await RefuseUnlessWritableAsync(access, ctx, expedition, ct) is { } refusal)
        {
            return refusal;
        }

        var trip = await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.TripLogId, ct);
        if (trip is null)
        {
            return ApiProblems.NotFound(TripNotFoundCode);
        }

        if (await RefuseUnlessTripWritableAsync(access, ctx, trip, ct) is { } tripRefusal)
        {
            return tripRefusal;
        }

        var (row, conflict) = await JoinAsync(db, expedition.Id, trip.Id, ct);
        return conflict ?? (Results<Ok<ExpeditionTripDto>, ProblemHttpResult>)TypedResults.Ok(row!);
    }

    /// <summary>
    /// Takes a trip out of this camp.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Either party may end it: the right to write the camp, or the right to write the trip. A
    /// relationship needs both to form and one to end, and the alternative strands rows — a camp
    /// whose organiser cannot edit a trip could never evict it, and a trip whose owner cannot
    /// reach the camp could never get out of it.
    /// </para>
    /// <para>
    /// A trip the caller may not read answers as though it were not a member, exactly as the
    /// camp's own trip listing withholds it: a refusal that distinguished "not in this camp"
    /// from "in it and not yours to touch" would answer a question the listing refuses to.
    /// </para>
    /// </remarks>
    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveTripAsync(
        Guid id,
        Guid tripLogId,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var expedition = await db.Expeditions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (expedition is null || ctx is null
            || !(await access.DecideAsync(ctx, AccessAction.Read, expedition, ct)).Allowed)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var trip = await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tripLogId, ct);
        if (trip is null || !(await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed)
        {
            return ApiProblems.NotFound(TripNotFoundCode);
        }

        var row = await db.ExpeditionTrips
            .FirstOrDefaultAsync(x => x.ExpeditionId == id && x.TripLogId == tripLogId, ct);
        if (row is null)
        {
            return ApiProblems.NotFound(NotAMemberCode);
        }

        var mayWriteCamp = (await access.DecideAsync(ctx, AccessAction.Write, expedition, ct)).Allowed;
        if (!mayWriteCamp && !(await access.DecideAsync(ctx, AccessAction.Write, trip, ct)).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        db.ExpeditionTrips.Remove(row);
        await ReleaseCampRulesAsync(db, id, tripLogId, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Takes back the rules a camp's sharing wrote onto a trip, now that the trip has left it.
    /// </summary>
    /// <remarks>
    /// A camp's sharing reaches a trip because the trip is in the camp; once it is not, the grant
    /// has lost the thing it was justified by. Leaving it would be worse than untidy: it is
    /// anchored on the trip and marked with a camp the trip no longer belongs to, so the trip's own
    /// permissions surface does not offer it — that surface deliberately leaves the camp's rules to
    /// the camp — and the only route that could withdraw it asks for authority over a camp the
    /// trip's owner may not hold. The access would be permanent and nobody could see it, let alone
    /// remove it.
    ///
    /// The marker, and nothing but the marker, exactly as a withdrawal from the camp matches: a
    /// rule of the same shape somebody authored on this trip's own tab is theirs and stays.
    ///
    /// Loaded and removed rather than deleted in one statement, because a rule disappearing is a
    /// change to who may reach what and the trail has to carry it; a set-based delete never reaches
    /// the change tracker.
    /// </remarks>
    private static async Task ReleaseCampRulesAsync(
        SilexGisDbContext db, Guid expeditionId, Guid tripLogId, CancellationToken ct)
    {
        var granted = await db.AccessEntries
            .Where(e => e.GrantedViaExpeditionId == expeditionId && e.ScopeId == tripLogId)
            .ToListAsync(ct);
        if (granted.Count > 0)
        {
            db.AccessEntries.RemoveRange(granted);
        }
    }

    /// <summary>
    /// Sets which camp a trip belongs to, from the trip's own side, or takes it out of one.
    /// </summary>
    /// <remarks>
    /// A full write of one fact, so naming no camp means the trip is in none — there is no
    /// second reading for an absent field to carry here, unlike a request that edits many things
    /// at once and has to tell "not editing this" from "clear it".
    /// </remarks>
    private static async Task<Results<Ok<ExpeditionTripDto>, NoContent, ProblemHttpResult>> SetExpeditionAsync(
        Guid id,
        TripExpeditionRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var trip = await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (trip is null)
        {
            return ApiProblems.NotFound(TripNotFoundCode);
        }

        if (await RefuseUnlessTripWritableAsync(access, ctx, trip, ct) is { } refusal)
        {
            return refusal;
        }

        if (request.ExpeditionId is not { } expeditionId)
        {
            // Leaving takes authority over the trip alone, which the check above established.
            var current = await db.ExpeditionTrips.FirstOrDefaultAsync(x => x.TripLogId == trip.Id, ct);
            if (current is not null)
            {
                db.ExpeditionTrips.Remove(current);
                await ReleaseCampRulesAsync(db, current.ExpeditionId, trip.Id, ct);
                await db.SaveChangesAsync(ct);
            }

            return TypedResults.NoContent();
        }

        var expedition = await db.Expeditions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == expeditionId, ct);
        if (expedition is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (await RefuseUnlessWritableAsync(access, ctx, expedition, ct) is { } campRefusal)
        {
            return campRefusal;
        }

        var (row, conflict) = await JoinAsync(db, expedition.Id, trip.Id, ct);
        return conflict
            ?? (Results<Ok<ExpeditionTripDto>, NoContent, ProblemHttpResult>)TypedResults.Ok(row!);
    }

    /// <summary>
    /// Writes the membership row, having established that the caller may. The one place the
    /// "at most one camp" rule is maintained, so both doors into it behave identically.
    /// </summary>
    /// <remarks>
    /// It reads which camp the trip is in and then writes, so two people acting on the same trip at
    /// the same moment can both act on a state that stopped being true between the two. The
    /// database is what refuses the second of them — the unique index on the trip, or a delete that
    /// finds the row already gone — and this translates that refusal into the answer a client can
    /// act on, rather than letting a race the schema correctly caught surface as a server fault
    /// with no code on it. The caller's remedy is to re-read the trip and ask again.
    /// </remarks>
    private static async Task<(ExpeditionTripDto? Row, ProblemHttpResult? Problem)> JoinAsync(
        SilexGisDbContext db, Guid expeditionId, Guid tripLogId, CancellationToken ct)
    {
        var existing = await db.ExpeditionTrips.FirstOrDefaultAsync(x => x.TripLogId == tripLogId, ct);
        if (existing is not null && existing.ExpeditionId == expeditionId)
        {
            // Already where it is being put, and the joining date stays as it was: restating a
            // fact does not make it new, and something later asks that date what the camp held
            // at a moment in the past.
            return (Map(existing, movedFromAnother: false), null);
        }

        // Left and rejoined in two acts rather than repointed in one. The unique index is on the
        // trip, so the row leaving and the row arriving cannot both stand for an instant; and a
        // move recorded as a departure and an arrival appears on both camps' histories, while a
        // row edited in place appears only on the one it ended up in. The transaction is what
        // keeps a failure between the two from leaving the trip in neither.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var moved = existing is not null;
        var row = new ExpeditionTrip
        {
            ExpeditionId = expeditionId,
            TripLogId = tripLogId,
            JoinedAt = DateTimeOffset.UtcNow,
        };

        try
        {
            if (existing is not null)
            {
                db.ExpeditionTrips.Remove(existing);

                // A move is a departure as well as an arrival, so what the camp it left had
                // granted on this trip goes with the leaving — the new camp grants nothing by
                // gaining a member, and the old one's grant has lost what justified it.
                await ReleaseCampRulesAsync(db, existing.ExpeditionId, tripLogId, ct);
                await db.SaveChangesAsync(ct);
            }

            db.ExpeditionTrips.Add(row);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception e) when (IsMembershipRace(e))
        {
            // Nothing is rolled back by hand: disposing the transaction undoes whichever leg had
            // already run, and an explicit rollback on a connection that has just failed is one
            // more way for the handler to throw instead of answering.
            return (null, ApiProblems.Conflict(
                MembershipConflictCode,
                "This trip's camp was changed by somebody else while this request was being "
                + "handled. Read the trip again and repeat the change if it is still wanted."));
        }

        return (Map(row, moved), null);
    }

    /// <summary>
    /// Whether a failed membership write is somebody else having moved the same trip in the
    /// meantime: the unique index on the trip refusing a second camp for it, or the row this
    /// request meant to remove having already been removed.
    /// </summary>
    private static bool IsMembershipRace(Exception e) =>
        e is DbUpdateConcurrencyException
        || (e is DbUpdateException
            && e.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: MembershipUniqueIndex,
            });

    private static ExpeditionTripDto Map(ExpeditionTrip row, bool movedFromAnother) => new()
    {
        ExpeditionId = row.ExpeditionId,
        TripLogId = row.TripLogId,
        JoinedAt = row.JoinedAt,
        MovedFromAnotherExpedition = movedFromAnother,
    };

    /// <summary>
    /// The refusal a caller who may not write a trip gets, in the trip's own words: 403 when
    /// they can read it, 404 when they cannot.
    /// </summary>
    private static async Task<ProblemHttpResult?> RefuseUnlessTripWritableAsync(
        IAccessService access, AccessContext? ctx, TripLog trip, CancellationToken ct)
    {
        if (ctx is not null && (await access.DecideAsync(ctx, AccessAction.Write, trip, ct)).Allowed)
        {
            return null;
        }

        return (await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed
            ? ApiProblems.Forbidden()
            : ApiProblems.NotFound(TripNotFoundCode);
    }

    /// <summary>
    /// The refusal a caller who may not write this camp gets: 403 when they can see it, 404 when
    /// they cannot — so a refusal never tells somebody a camp exists that they may not read.
    /// </summary>
    /// <remarks>
    /// Shared with the camp's other route files: the ladder a write to anything belonging to a
    /// camp climbs is one rule, and the day it is written out twice is the day one copy grows a
    /// check the other lacks.
    /// </remarks>
    internal static async Task<ProblemHttpResult?> RefuseUnlessWritableAsync(
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

    /// <summary>
    /// The camp as every reading of it gives it. Shared with the camp's own write-up, which states
    /// only what a reading already produced rather than going to the row a second time.
    /// </summary>
    internal static ExpeditionDto Map(Expedition expedition) => new()
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
