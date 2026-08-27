// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Events;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Events;

/// <summary>
/// The dated things a club runs that are not trips and not camps — meetings, training, working
/// days, deadlines.
/// </summary>
public static class EventEndpoints
{
    // Visible to the event's other route files rather than private, so every door into an event
    // refuses one nobody may reach in the same words. A second spelling of it is a second answer
    // waiting to drift from this one.
    internal const string NotFoundCode = "event.not_found";

    // A lifecycle state the list was asked to narrow by that this application does not have.
    private const string StateInvalidCode = "event.state_invalid";

    // A kind the list was asked to narrow by that is not one of the six.
    private const string KindInvalidCode = "event.kind_invalid";

    public static RouteGroupBuilder MapEventEndpoints(this RouteGroupBuilder api)
    {
        var events = api.MapGroup("/events").WithTags("Events");

        events.MapGet("/", ListAsync)
            .WithSummary(
                "Paged events, most recent first; visibility-filtered. Narrowed by a date "
                + "window the event overlaps, by a word in its title, by kind and by lifecycle "
                + "state.");
        events.MapGet("/defaults", DefaultsAsync)
            .WithSummary(
                "The audience an event would get if its author named none, so a form can show "
                + "the answer the write would apply rather than guessing at it.");
        events.MapGet("/{id:guid}", GetAsync)
            .WithSummary("A single event. Emits the version token its state route requires back.");
        events.MapPost("/", CreateAsync).WithValidation<EventWriteRequest>()
            .WithSummary("Creates an event (Create permission); the caller becomes owner.");
        events.MapPut("/{id:guid}", UpdateAsync).WithValidation<EventWriteRequest>()
            .WithSummary("Full update (Write permission). The lifecycle state is not part of it.");
        events.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes an event and the rules anchored on it.");
        events.MapPost("/{id:guid}/state", TransitionAsync)
            .WithValidation<EventTransitionRequest>()
            .WithSummary(
                "Moves an event to another lifecycle state (Write permission). One endpoint "
                + "rather than a verb per state: an event has eight states and the moves between "
                + "them are a table, not a handful of named acts.");

        return api;
    }

    private static async Task<Results<Ok<PagedResult<EventDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        int? page,
        int? pageSize,
        DateOnly? from,
        DateOnly? to,
        string? search,
        string? kind,
        string? state,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Kinds and lifecycle states arrive as the camelCase words the rest of the contract
        // spells them with, parsed here rather than by route binding: binding a bad word would
        // answer with a bare 400 carrying no code, and a client cannot tell that apart from any
        // other refusal. The parse is deliberately not "unknown means no filter" — a caller who
        // asked for something this application does not have wants to be told, not handed the
        // whole list.
        EventKind? kindFilter = null;
        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!Enum.TryParse<EventKind>(kind, ignoreCase: true, out var kindValue)
                || !Enum.IsDefined(kindValue))
            {
                return ApiProblems.BadRequest(KindInvalidCode, $"Unknown kind '{kind}'.");
            }

            kindFilter = kindValue;
        }

        ActivityState? stateFilter = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            // Two questions, and the second is the one the camp's list forgets to ask: a word
            // the vocabulary has but an event may never hold names no row, so it is refused
            // here rather than answered with an empty page that reads as "there are none".
            if (!Enum.TryParse<ActivityState>(state, ignoreCase: true, out var stateValue)
                || !Enum.IsDefined(stateValue)
                || !ActivityStates.IsEventState(stateValue))
            {
                return ApiProblems.BadRequest(StateInvalidCode, $"Unknown state '{state}'.");
            }

            stateFilter = stateValue;
        }

        var query = db.Events.AsNoTracking().VisibleTo(ctx, AccessDomain.Events);
        query = query.OverlappingDays(x => x.StartDate, x => x.EndDate, from, to);

        if (kindFilter is { } wantedKind)
        {
            query = query.Where(x => x.Kind == wantedKind);
        }

        if (stateFilter is { } wantedState)
        {
            query = query.Where(x => x.State == wantedState);
        }

        // Accent-insensitive, over the title only — the same reach the trip and camp lists give
        // theirs. A description is a paragraph, and a word that matches one is as likely to be a
        // passing mention as the event somebody is looking for.
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(x =>
                EF.Functions.ILike(EF.Functions.Unaccent(x.Title), EF.Functions.Unaccent(pattern)));
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(ct);

        // The tie-break is the primary key, and it has to be: two events on the same evening are
        // ordinary, and an order that does not distinguish them lets a row appear on two pages or
        // on none as the database chooses. The identifiers are time-ordered, so descending by id
        // reads as "the one entered later first" among events that start together.
        var rows = await query
            .OrderByDescending(x => x.StartDate).ThenByDescending(x => x.Id)
            .Skip((p - 1) * size).Take(size).ToListAsync(ct);

        return TypedResults.Ok(new PagedResult<EventDto>([.. rows.Select(Map)], p, size, total));
    }

    /// <summary>
    /// What audience a new event would get if its author named none. Answered from the same rule
    /// the write applies, so a form cannot show one answer and the create produce another.
    /// </summary>
    private static async Task<Results<Ok<EventDefaultsDto>, UnauthorizedHttpResult>> DefaultsAsync(
        IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var (visibility, cavingGroupId) = EventAudienceRules.DefaultAudience(ctx.CavingGroupIds);
        return TypedResults.Ok(new EventDefaultsDto
        {
            Visibility = visibility,
            CavingGroupId = cavingGroupId,
        });
    }

    private static async Task<Results<Created<EventDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        EventWriteRequest request,
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

        // Who may read the event is one answer in two values, so the default decides it only
        // when the request answers neither of them. A request that names either half has taken
        // the decision itself and both halves are read as it sent them — a stated audience with
        // no group binding is somebody saying "not the club", and quietly supplying one would
        // widen what they asked for.
        //
        // Settled before the create check and the binding check below, so a binding this rule
        // supplies is guarded exactly like one the caller typed rather than slipping in behind
        // them.
        var fallback = EventAudienceRules.DefaultAudience(ctx.CavingGroupIds);
        var (visibility, cavingGroupId) = request.Visibility is null && request.CavingGroupId is null
            ? fallback
            : (request.Visibility ?? fallback.Visibility, request.CavingGroupId);

        var settled = request with { Visibility = visibility, CavingGroupId = cavingGroupId };

        if (!CreateRules.MayCreate(ctx, AccessDomain.Events, settled.CavingGroupId))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        if (ValidateReferences(ctx, settled) is { } problem)
        {
            return problem;
        }

        var row = new Event { Title = settled.Title, OwnerUserId = user.UserId };
        Apply(row, settled);
        db.Events.Add(row);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/events/{row.Id}", Map(row));
    }

    private static async Task<Results<Ok<EventDto>, ProblemHttpResult>> UpdateAsync(
        Guid id,
        EventWriteRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var row = await db.Events.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (await RefuseUnlessWritableAsync(access, ctx, row, ct) is { } refusal)
        {
            return refusal;
        }

        if (ValidateReferences(ctx!, request) is { } problem)
        {
            return problem;
        }

        // Required, as it is on every other full update of a dated record: an edit written on
        // top of a version the author never saw silently discards whatever changed in between,
        // and two committee members correcting the same evening is the ordinary case rather than
        // the exotic one.
        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.Events, row.Id, ct, required: true) is { } stale)
        {
            return stale;
        }

        Apply(row, request);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(Map(row));
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
        var row = await db.Events.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (ctx is null || !(await access.DecideAsync(ctx, AccessAction.Delete, row, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, row, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound(NotFoundCode);
        }

        // The precondition is offered but not required, the way it is on every delete here: a
        // list deletes a row it never loaded a version of, so demanding one would make the
        // ordinary delete impossible from the surface that does it most.
        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.Events, row.Id, ct) is { } stale)
        {
            return stale;
        }

        // A rule anchored on this event means nothing once the event is gone, and a rule whose
        // anchor cannot be resolved is exactly what the integrity check reports as an orphan.
        // Deleting them here is what keeps a routine delete from leaving one behind.
        //
        // Loaded and removed rather than deleted in one statement, because a rule disappearing is
        // a change to who may reach what, and every other place rules are withdrawn records that.
        // A set-based delete never reaches the change tracker, so the withdrawal would happen
        // with nothing in the trail to say it had.
        var anchored = await db.AccessEntries
            .Where(e => e.Domain == AccessDomain.Events
                && e.ScopeKind == AccessScopeKind.Object
                && e.ScopeId == row.Id)
            .ToListAsync(ct);
        db.AccessEntries.RemoveRange(anchored);

        db.Events.Remove(row);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static ProblemHttpResult? ValidateReferences(AccessContext ctx, EventWriteRequest request)
    {
        if (request.CavingGroupId is not null
            && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.Events, request.CavingGroupId.Value))
        {
            return ApiProblems.Forbidden(CavingGroupBindingRules.ForbiddenCode);
        }

        return null;
    }

    private static void Apply(Event row, EventWriteRequest request)
    {
        row.Title = request.Title;
        row.Description = request.Description;
        row.Kind = request.Kind;
        row.StartDate = request.StartDate;

        // An end equal to the start is stored as nothing, because a date-range control has no
        // way to say "one day" other than by picking the same day twice — and a stored end is
        // the "and it ran on to" fact, so keeping it would make every single-day event read as a
        // range of itself. The table's own constraint holds the same rule from below.
        row.EndDate = DayRange.EndForStorage(request.StartDate, request.EndDate);
        row.StartTime = request.StartTime;
        row.EndTime = request.EndTime;
        row.Place = request.Place;

        // An audience the request does not name is left exactly as it stands. The only place an
        // event's audience is decided for it is the moment it is created, and it is decided
        // there before this runs — so a null arriving here can only mean "not editing who may
        // read it", and a save from a surface that never drew the field cannot quietly narrow or
        // widen one.
        //
        // The group binding moves with the audience rather than on its own, because the two are
        // one answer: a group-visible event whose binding is cleared names no group and is
        // therefore readable by nobody but its owner. Writing the binding unconditionally would
        // do exactly that to every save from a surface that drew neither field — the case the
        // nullability above exists to protect — and it would do it silently, with the stored
        // audience still reading "the caving group".
        if (request.Visibility is { } visibility)
        {
            row.Visibility = visibility;
            row.CavingGroupId = request.CavingGroupId;
        }
    }

    private static async Task<Results<Ok<EventDto>, ProblemHttpResult>> GetAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var row = await db.Events.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null || !(await access.DecideAsync(ctx, AccessAction.Read, row, ct)).Allowed)
        {
            // An event somebody may not read is missing rather than forbidden, so that refusing
            // it tells them nothing about whether it exists.
            return ApiProblems.NotFound(NotFoundCode);
        }

        await Concurrency.EmitETagAsync(http, db, VersionedTable.Events, row.Id, ct);
        return TypedResults.Ok(Map(row));
    }

    /// <summary>
    /// Moves an event to another lifecycle state. Which moves exist is not decided here — the
    /// transition table is the one place that knows, so a state an event may not hold and a move
    /// it may not make are refused by the same rule and with the same code.
    /// </summary>
    /// <remarks>
    /// The precondition is required exactly as it is on a full update: the caller is acting on
    /// the state they were shown, and acting on one that changed underneath them is the lost
    /// update the header exists to prevent — two people announcing and un-announcing the same
    /// evening otherwise land whichever order the database happens to see.
    /// </remarks>
    private static async Task<Results<Ok<EventDto>, ProblemHttpResult>> TransitionAsync(
        Guid id,
        EventTransitionRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var row = await db.Events.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (await RefuseUnlessWritableAsync(access, ctx, row, ct) is { } refusal)
        {
            return refusal;
        }

        if (await Concurrency.CheckIfMatchAsync(
                http, db, VersionedTable.Events, row.Id, ct, required: true) is { } stale)
        {
            return stale;
        }

        // One question, not two: a target the vocabulary admits but an event may not hold appears
        // in no pair of the table, so asking the table refuses it for the same reason and under
        // the same code as an illegal move. Asking whether the state is an admitted one first
        // would be a second rule saying the same thing, free to drift from it.
        //
        // The state is present because the validator filter runs before this and requires it; a
        // body that names none is a 400 and never arrives here.
        var target = request.State!.Value;
        if (!ActivityStates.MayEventTransition(row.State, target))
        {
            return ApiProblems.Conflict(
                ActivityStates.EventTransitionInvalidCode,
                $"An event does not move from {row.State} to {target}.");
        }

        row.State = target;
        if (target == ActivityState.Published)
        {
            // Stamped the first time only: de-announcing and announcing again does not rewrite
            // the day the event was first made known.
            row.PublishedAt ??= DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(Map(row));
    }

    internal static async Task<ProblemHttpResult?> RefuseUnlessWritableAsync(
        IAccessService access, AccessContext? ctx, Event row, CancellationToken ct)
    {
        if (ctx is not null && (await access.DecideAsync(ctx, AccessAction.Write, row, ct)).Allowed)
        {
            return null;
        }

        // Somebody who may read it but not change it is told so; somebody who may not read it at
        // all is told nothing beyond that there is nothing there.
        return (await access.DecideAsync(ctx, AccessAction.Read, row, ct)).Allowed
            ? ApiProblems.Forbidden()
            : ApiProblems.NotFound(NotFoundCode);
    }

    internal static EventDto Map(Event row) => new()
    {
        Id = row.Id,
        Title = row.Title,
        Description = row.Description,
        Kind = row.Kind,
        StartDate = row.StartDate,
        EndDate = row.EndDate,
        StartTime = row.StartTime,
        EndTime = row.EndTime,
        Place = row.Place,
        OwnerUserId = row.OwnerUserId,
        CavingGroupId = row.CavingGroupId,
        Visibility = row.Visibility,
        State = row.State,
        PublishedAt = row.PublishedAt,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt,
    };
}
