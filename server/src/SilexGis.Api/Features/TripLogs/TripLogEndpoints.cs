// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Api.Features.TripLogs;

public static class TripLogEndpoints
{
    // A lifecycle state a listing was asked to narrow by that a trip does not have. Its own code
    // rather than the transition refusal's: nothing is being moved, a word was simply not
    // recognised, and a client that cannot tell those apart cannot say anything useful about
    // either.
    private const string StateInvalidCode = "trip_log.state_invalid";

    public static RouteGroupBuilder MapTripLogEndpoints(this RouteGroupBuilder api)
    {
        var trips = api.MapGroup("/trip-logs").WithTags("TripLogs");

        trips.MapGet("/", ListAsync)
            .WithSummary("Paged trip logs with date/cave filters; visibility-filtered.");
        trips.MapGet("/mine", MineAsync)
            .WithSummary(
                "The trips the calling account is on \u2014 named on the roster or asked about it "
                + "\u2014 soonest first, from today unless a window says otherwise, and narrowable "
                + "by lifecycle state. Whose trips these are is worked out from the caller and "
                + "cannot be asked for: there is no parameter naming a person, because one would "
                + "answer where a named person has been out of trips the asker may not read.");
        trips.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Single trip log with caves and participants.");
        trips.MapPost("/", CreateAsync).WithValidation<TripLogWriteRequest>()
            .WithSummary(
                "Creates a trip log written up after the event (Create permission on trip logs); "
                + "the caller becomes owner, and an audience the request does not name is private.");
        trips.MapPost("/plans", CreatePlanAsync).WithValidation<TripLogWriteRequest>()
            .WithSummary(
                "Creates a trip that has not happened yet (Create permission on trip logs). The "
                + "same trip in every respect but one: an audience the request does not name is "
                + "the author's caving group rather than private, because a proposal only its "
                + "author can read is a proposal to nobody. The state it starts in is the same.");
        trips.MapGet("/plan-default", PlanDefaultAsync)
            .WithSummary(
                "The audience a trip being planned would get for this caller if they name none, "
                + "answered before the trip exists so a form can say who will see it.");
        trips.MapPut("/{id:guid}", UpdateAsync).WithValidation<TripLogWriteRequest>()
            .WithSummary(
                "Full update (Write permission). The whole roster is replaced, in every role, so a "
                + "person left out of both lists is taken off the trip; the caves are replaced only "
                + "when a list is supplied, and left as they are when the field is omitted.");
        trips.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes a trip log with its links and attachments.");
        trips.MapPost("/{id:guid}/state", TransitionAsync)
            .WithValidation<TripLogTransitionRequest>()
            .WithSummary(
                "Moves a trip log to another lifecycle state (Write permission). One endpoint "
                + "rather than a verb per state: the moves a trip may make are a table, and a "
                + "verb per move can only ever offer the handful somebody thought to name.");
        trips.MapPost("/{id:guid}/callout", ArrangeCalloutAsync)
            .WithValidation<TripCalloutRequest>()
            .WithSummary(
                "Arranges, changes or calls off the check that notices if the party does not come "
                + "back (Write permission). Its own door rather than two fields on the trip, so "
                + "that saving the trip from a surface which never drew them cannot quietly leave "
                + "a party unwatched.");
        trips.MapPost("/{id:guid}/callout/stand-down", StandDownCalloutAsync)
            .WithSummary(
                "Says the party is out, which stops the overdue check. Open to anyone the trip "
                + "names or has asked, and deliberately not to whoever may edit the trip: it is a "
                + "statement about where people are, not a change to the record of the trip. It "
                + "stands the check down rather than erasing it, so what was arranged stays "
                + "readable afterwards.");
        trips.MapGet("/{id:guid}/report", TripReportEndpoints.DownloadAsync)
            .WithSummary(
                "The trip written up as a document, built from this caller's own reading of the "
                + "trip — the same one the page shows.");
        trips.MapPost("/{id:guid}/report", TripReportEndpoints.KeepAsync)
            .WithSummary(
                "Writes the trip up and files the document against the trip, replacing any report "
                + "kept there before (Write permission).");

        return api;
    }

    private static async Task<Results<Ok<PagedResult<TripLogDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        int? page,
        int? pageSize,
        DateOnly? from,
        DateOnly? to,
        Guid? caveId,
        Guid? expeditionId,
        string? search,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        var query = db.TripLogs.AsNoTracking().VisibleTo(ctx, AccessDomain.TripLogs);

        query = query.OverlappingDays(x => x.TripDate, x => x.TripDateEnd, from, to);

        if (caveId is not null)
        {
            // Asked through the same rule the rows themselves are mapped by, and for the same
            // reason in both directions. A cave this caller may not open is one the listing
            // will not name, so it must not be usable as a filter either: an id that answers
            // differently from one that does not exist is an id anybody can go looking for,
            // and the answer would be the trips that reached it — each carrying its own exact
            // geometry, which places the cave the filter would not name. The position gate is
            // the other half of the same question: filtering by a guarded cave places it
            // through the trips' geometries even when the cave itself is readable. Either one
            // failing behaves as if nothing is linked.
            if ((await TripCaveDisclosure.DisclosableCaveIdsAsync(db, protection, ctx, [caveId.Value], ct)).Count == 0)
            {
                var (emptyPage, emptySize) = Paging.Normalize(page, pageSize);
                return TypedResults.Ok(new PagedResult<TripLogDto>([], emptyPage, emptySize, 0));
            }

            // Role-agnostic: the question is which trips this cave is named on, not what they
            // did there. Narrowing it to one role would quietly answer a smaller question.
            var namingTrips = TripRoleLinks.TripIdsNaming(db, caveId.Value);
            query = query.Where(x => namingTrips.Contains(x.Id));
        }

        if (expeditionId is not null)
        {
            // This is the camp's own trip list, so it is filtered like every other listing here
            // and shows only what the caller may read — the same camp therefore lists different
            // trips to different people, and both listings are right.
            //
            // A camp the caller may not read answers as though it gathered nothing, rather than
            // filtering by it: the trips are readable, so filtering would tell the caller which
            // of them a camp they cannot open holds, and an id that answers differently from one
            // that does not exist is an id anybody can go looking for.
            var readableCamp = await db.Expeditions.AsNoTracking()
                .VisibleTo(ctx, AccessDomain.Expeditions)
                .AnyAsync(x => x.Id == expeditionId.Value, ct);
            if (!readableCamp)
            {
                var (emptyPage, emptySize) = Paging.Normalize(page, pageSize);
                return TypedResults.Ok(new PagedResult<TripLogDto>([], emptyPage, emptySize, 0));
            }

            var members = db.ExpeditionTrips.AsNoTracking()
                .Where(m => m.ExpeditionId == expeditionId.Value)
                .Select(m => m.TripLogId);
            query = query.Where(x => members.Contains(x.Id));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(x => EF.Functions.ILike(EF.Functions.Unaccent(x.Title), EF.Functions.Unaccent(pattern)));
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.TripDate).ThenByDescending(x => x.CreatedAt)
            .Skip((p - 1) * size).Take(size).ToListAsync(ct);

        var items = await MapWithChildrenAsync(db, access, protection, ctx, user, rows, ct);
        return TypedResults.Ok(new PagedResult<TripLogDto>(items, p, size, total));
    }

    /// <summary>
    /// The caller's own trips, soonest first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Whose trips these are is resolved from the request's own identity and from nothing
    /// supplied. That is the whole shape of this route, not an implementation detail: a
    /// parameter naming a person would let somebody assemble where that person has been out of
    /// trips they may never open, and the size of the answer gives it away even when no row
    /// comes back — ask once, ask again with a different window, and the difference is when that
    /// person was underground. Filtering trips by participant is refused everywhere else in this
    /// application for exactly that reason, and a query string is the same request with
    /// different spelling. So there is no participant parameter here, under any name, and adding
    /// one would undo a refusal the rest of the code takes seriously.
    /// </para>
    /// <para>
    /// Being on a trip is not a right to read it. The caller's ordinary reading is applied first
    /// and this narrows what is left, so a trip somebody was asked about and then shut out of
    /// disappears from their own list — which is correct: the list is a view of records, and a
    /// record nobody may read is not shown by a different door.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<PagedResult<TripLogDto>>, UnauthorizedHttpResult, ProblemHttpResult>> MineAsync(
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        int? page,
        int? pageSize,
        DateOnly? from,
        DateOnly? to,
        string? state,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        // Lifecycle states arrive as the camelCase words the rest of the contract spells them
        // with, parsed here rather than by route binding: binding a bad word would answer with a
        // bare 400 carrying no code, and a client cannot tell that apart from any other refusal.
        // A word this application does not have, and one a trip cannot hold, are refused alike —
        // a caller who asked for something that cannot exist wants to be told, not handed the
        // whole list.
        ActivityState? stateFilter = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            if (!Enum.TryParse<ActivityState>(state, ignoreCase: true, out var stateValue)
                || !Enum.IsDefined(stateValue)
                || !ActivityStates.IsTripLogState(stateValue))
            {
                return ApiProblems.BadRequest(StateInvalidCode, $"Unknown state '{state}'.");
            }

            stateFilter = stateValue;
        }

        var query = db.TripLogs.AsNoTracking().VisibleTo(ctx, AccessDomain.TripLogs);

        // Being asked counts as being on it, and saying no counts as not being on it. Somebody
        // invited and not yet written onto the roster has the trip in their diary as much as
        // anybody already named; somebody who declined does not, and would otherwise fill a short
        // list with the weekends they turned down while the trip they are going on falls off the
        // end. Being written onto the party regardless is the organiser overruling the answer, and
        // puts the trip back.
        var mine = TripAudience.TripIdsTheAccountIsOn(db, user.UserId);
        query = query.Where(x => mine.Contains(x.Id));

        // Coming up means from today onwards unless the caller says otherwise, so the default is
        // a floor rather than a fixed window: a list of what somebody is going on is useless if
        // it opens on last winter. Dates here are the trip's own calendar days and today is read
        // in UTC, which is the only clock this application stores — a trip on the boundary can
        // therefore appear or drop a few hours early or late for a reader far from it, and that
        // is preferable to a per-reader answer nothing else in the application gives.
        //
        // An explicit window is honoured as asked, backwards included. Every row here is a trip
        // the caller is on and may already read, so widening it discloses nothing that was being
        // withheld; the same list then answers "what have I been on" without a second door.
        var windowStart = from ?? DateOnly.FromDateTime(DateTime.UtcNow);

        query = query.OverlappingDays(x => x.TripDate, x => x.TripDateEnd, windowStart, to);

        // No state is excluded by default, and that is a decision rather than an omission. A trip
        // the caller is on that has been called off is exactly the thing they most need to see on
        // a list of what is coming up, and hiding it would make the list quietly disagree with the
        // trip's own page. Narrowing is offered instead, so a surface that wants only what is
        // going ahead asks for it and says so.
        if (stateFilter is { } wantedState)
        {
            query = query.Where(x => x.State == wantedState);
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(ct);

        // Ascending, which is the opposite of every other trip listing and is the point of this
        // one: the next thing somebody is going on is the row they came for, so it is the first.
        // The tie-break is the primary key and has to be — two trips on the same day are
        // ordinary, and an order that does not separate them lets a row appear on two pages or on
        // none as the database chooses. A timestamp is the same bug one step further away, since
        // two rows written in the same tick tie again; the identifiers are time-ordered, so
        // ascending by id reads as "the one entered first" among trips that start together.
        var rows = await query.OrderBy(x => x.TripDate).ThenBy(x => x.Id)
            .Skip((p - 1) * size).Take(size).ToListAsync(ct);

        var items = await MapWithChildrenAsync(db, access, protection, ctx, user, rows, ct);
        return TypedResults.Ok(new PagedResult<TripLogDto>(items, p, size, total));
    }

    private static async Task<Results<Ok<TripLogDto>, ProblemHttpResult>> GetAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        var trip = await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (trip is null || !(await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed)
        {
            return ApiProblems.NotFound("trip_log.not_found");
        }

        var items = await MapWithChildrenAsync(db, access, protection, ctx!, user!, [trip], ct);
        await Concurrency.EmitETagAsync(http, db, VersionedTable.TripLogs, trip.Id, ct);
        return TypedResults.Ok(items[0]);
    }

    /// <summary>Creates a trip written up after the event.</summary>
    private static Task<Results<Created<TripLogDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        TripLogWriteRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        TripLogWriteService writes,
        CancellationToken ct) =>
        CreateCoreAsync(
            TripCreationIntent.Report, request, db, access, accessAccessor, userAccessor,
            protection, writes, ct);

    /// <summary>Creates a trip that has not happened yet.</summary>
    private static Task<Results<Created<TripLogDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreatePlanAsync(
        TripLogWriteRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        TripLogWriteService writes,
        CancellationToken ct) =>
        CreateCoreAsync(
            TripCreationIntent.Plan, request, db, access, accessAccessor, userAccessor,
            protection, writes, ct);

    /// <summary>
    /// Creating a trip, whichever door it came through. The two doors differ in one thing and it
    /// is decided here, before anything is checked against it: the audience a request that names
    /// none falls back to. Everything after that point is identical, which is why they share a
    /// body rather than each growing their own copy of eight steps.
    /// </summary>
    private static async Task<Results<Created<TripLogDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateCoreAsync(
        TripCreationIntent intent,
        TripLogWriteRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        TripLogWriteService writes,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        TripLog trip;
        try
        {
            trip = await writes.CreateAsync(intent, ToInput(request), ctx, user.UserId, ct: ct);
        }
        catch (TripWriteException e)
        {
            return e.Denied ? ApiProblems.Forbidden(e.Code) : ApiProblems.BadRequest(e.Code, e.Message);
        }

        await db.SaveChangesAsync(ct);

        var items = await MapWithChildrenAsync(db, access, protection, ctx, user, [trip], ct);
        return TypedResults.Created($"/api/v1/trip-logs/{trip.Id}", items[0]);
    }

    /// <summary>
    /// Who would be able to read a trip this caller plans, if they name no audience themselves —
    /// the same rule the plan door applies, answered before the trip exists so a form can name
    /// the audience rather than recite the rule. The group's name travels with its id because a
    /// notice saying "your group" and a reader who belongs to one they had forgotten about are
    /// not the same thing.
    /// </summary>
    /// <remarks>
    /// Tells the caller nothing they do not already know: it reports their own membership, and
    /// only when it is the single one that decides the answer.
    /// </remarks>
    private static async Task<Results<Ok<TripPlanDefaultDto>, UnauthorizedHttpResult>> PlanDefaultAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var (visibility, groupId) = TripAudienceRules.DefaultAudience(
            TripCreationIntent.Plan, ctx.CavingGroupIds);
        if (groupId is null)
        {
            return TypedResults.Ok(new TripPlanDefaultDto(visibility, null, null));
        }

        var name = await db.CavingGroups.AsNoTracking()
            .Where(group => group.Id == groupId.Value)
            .Select(group => group.Name)
            .FirstOrDefaultAsync(ct);
        return TypedResults.Ok(new TripPlanDefaultDto(visibility, groupId, name));
    }

    private static async Task<Results<Ok<TripLogDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        TripLogWriteRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        TripLogWriteService writes,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        var trip = await db.TripLogs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (trip is null)
        {
            return ApiProblems.NotFound("trip_log.not_found");
        }

        if (ctx is null || user is null || !(await access.DecideAsync(ctx, AccessAction.Write, trip, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("trip_log.not_found");
        }

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.TripLogs, trip.Id, ct, required: true) is { } stale)
        {
            return stale;
        }

        TripWriteOutcome outcome;
        try
        {
            outcome = await writes.UpdateAsync(trip, ToInput(request), ctx, ct: ct);
        }
        catch (TripWriteException e)
        {
            return e.Denied ? ApiProblems.Forbidden(e.Code) : ApiProblems.BadRequest(e.Code, e.Message);
        }

        // A trip people are expecting to go on has changed under them, so the people it concerns
        // are told — everybody named on it and everybody asked about it, minus whoever this same
        // write has just told about it another way. What changed is not in the message: saying so
        // would mean saying which field, and the fields include the places the trip is about.
        //
        // Asked of the write rather than of the change tracker here, because queueing the roster
        // notice is itself a change and would answer the question for it: a request that carries
        // back exactly what it was given moves no column, and a notice announcing a change that
        // provably did not happen is how a category earns being muted along with the messages
        // that matter.
        if (outcome.SomethingChanged)
        {
            await TripPlanNotifier.ChangedAsync(db, access, user, trip, outcome.NewlyNamedUserIds, ct);
        }

        // A cave named onto a trip after people were asked onto it is the same pairing arriving in
        // the other order, and the people who can open it are told the same way.
        await TripCaveAccessNotifier.CavesAddedAsync(db, access, user, trip, outcome.AddedCaveIds, ct);
        await db.SaveChangesAsync(ct);

        var items = await MapWithChildrenAsync(db, access, protection, ctx, user, [trip], ct);
        return TypedResults.Ok(items[0]);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        TripLogWriteService writes,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var trip = await db.TripLogs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (trip is null)
        {
            return ApiProblems.NotFound("trip_log.not_found");
        }

        if (ctx is null || !(await access.DecideAsync(ctx, AccessAction.Delete, trip, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("trip_log.not_found");
        }

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.TripLogs, trip.Id, ct) is { } stale)
        {
            return stale;
        }

        // What goes with a trip is stated in one place, because the undo that takes a whole
        // imported spreadsheet back has to remove a trip the same way this route does.
        await writes.DeleteAsync(trip, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Arranges, changes or calls off the check that notices if a party does not come back.
    /// </summary>
    /// <remarks>
    /// Whoever may change the trip, because arranging one is part of planning the trip. Saying the
    /// party is out is the other half and is not this: it belongs to the people on the trip and
    /// has its own route. The precondition header is required as on every other write to a trip —
    /// this one edits the record, and two people arranging different hours is the lost update it
    /// exists to catch.
    /// </remarks>
    private static async Task<Results<Ok<TripLogDto>, ProblemHttpResult>> ArrangeCalloutAsync(
        Guid id,
        TripCalloutRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        var trip = await db.TripLogs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (trip is null)
        {
            return ApiProblems.NotFound("trip_log.not_found");
        }

        if (ctx is null || user is null || !(await access.DecideAsync(ctx, AccessAction.Write, trip, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("trip_log.not_found");
        }

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.TripLogs, trip.Id, ct, required: true) is { } stale)
        {
            return stale;
        }

        ApplyCallout(trip, request);
        await db.SaveChangesAsync(ct);

        var arranged = await MapWithChildrenAsync(db, access, protection, ctx, user, [trip], ct);
        return TypedResults.Ok(arranged[0]);
    }

    /// <summary>
    /// Says the party is out: the overdue check stops, and stays on the trip as something that was
    /// arranged and stood down rather than being erased.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Who may.</b> Anybody the trip names or has asked — not whoever may edit it. The two are
    /// different questions with different answers: the person who knows the party is out is on the
    /// trip, and making them wait for somebody with the right to change the record is exactly the
    /// delay a callout exists to avoid. Whoever may edit the trip is not left without a way: the
    /// arrangement itself is theirs to change, and clearing the alarm time on the trip calls the
    /// whole thing off. Reading the trip is required as well, because saying nothing about a trip
    /// you cannot read is the same refusal every other route gives.
    /// </para>
    /// <para>
    /// <b>Why it takes no precondition header.</b> Every other write to a trip requires one, to
    /// stop two people overwriting each other's edits. This one is not an edit: there is one value
    /// it can write, everybody who may call it is saying the same thing, and a stale header would
    /// refuse the message that says a party is safe. Two people tapping it at once is two people
    /// agreeing.
    /// </para>
    /// <para>
    /// A second tap answers 200 rather than a conflict, for the same reason: somebody who is not
    /// sure the first one went through will tap again, and telling them it failed is the one wrong
    /// answer available. A trip nobody arranged a check for is the refusal, because there is
    /// nothing to stand down and saying "done" would report a check that never existed as handled.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<TripLogDto>, ProblemHttpResult>> StandDownCalloutAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        var trip = await db.TripLogs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (trip is null || ctx is null || user is null)
        {
            return ApiProblems.NotFound("trip_log.not_found");
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed)
        {
            return ApiProblems.NotFound("trip_log.not_found");
        }

        // The same set the alarm itself is sent to, asked from the one place that knows it: the
        // people it would wake are the people who may say it is not needed. Two definitions of
        // "who this trip concerns" would be free to disagree, and the disagreement that mattered
        // would be somebody told a party is overdue with no way to say they are not.
        var concerns = await TripAudience.ConcerningAsync(db, [trip.Id], user.UserId, ct);
        if (!concerns.Contains(trip.Id))
        {
            return ApiProblems.Forbidden();
        }

        if (trip.CalloutState == TripCalloutState.None)
        {
            return ApiProblems.Conflict(
                "trip_log.callout_not_armed",
                "This trip has no overdue check to stand down.");
        }

        if (trip.CalloutState != TripCalloutState.StoodDown)
        {
            // The times stay exactly as they were. What was arranged is part of the record of the
            // trip — a search that was nearly called is worth being able to read afterwards — and
            // the state is the only thing that moves.
            trip.CalloutState = TripCalloutState.StoodDown;
            await db.SaveChangesAsync(ct);
        }

        var items = await MapWithChildrenAsync(db, access, protection, ctx, user, [trip], ct);
        return TypedResults.Ok(items[0]);
    }

    // ---- shared pieces ----

    /// <summary>
    /// Moves a trip to another lifecycle state. Which moves exist is not decided here — the
    /// transition table is the one place that knows, so a state a trip may not hold and a move it
    /// may not make are refused by the same rule and with the same code.
    /// </summary>
    /// <remarks>
    /// The precondition is required exactly as it is on a full update: publishing announces the
    /// write-up somebody has read, and announcing a version that changed underneath them is the
    /// lost update the header exists to prevent.
    /// </remarks>
    private static async Task<Results<Ok<TripLogDto>, ProblemHttpResult>> TransitionAsync(
        Guid id,
        TripLogTransitionRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        ITripRosterAnnouncer announcer,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        var trip = await db.TripLogs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (trip is null)
        {
            return ApiProblems.NotFound("trip_log.not_found");
        }

        if (ctx is null || user is null || !(await access.DecideAsync(ctx, AccessAction.Write, trip, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("trip_log.not_found");
        }

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.TripLogs, trip.Id, ct, required: true) is { } stale)
        {
            return stale;
        }

        // One question, not two: a target the vocabulary admits but a trip may not hold appears in
        // no pair of the table, so asking the table refuses it for the same reason and under the
        // same code as an illegal move. Asking whether the state is an admitted one first would be
        // a second rule saying the same thing, free to drift from it.
        //
        // The state is present because the validator filter runs before this and requires it; a
        // body that names none is a 400 and never arrives here.
        var target = request.State!.Value;
        if (!ActivityStates.MayTripLogTransition(trip.State, target))
        {
            return ApiProblems.Conflict(
                ActivityStates.TripLogTransitionInvalidCode,
                $"A trip log does not move from {trip.State} to {target}.");
        }

        var stateBefore = trip.State;
        trip.State = target;
        if (target == ActivityState.Cancelled)
        {
            // A deliberate notice sent by the path that calls a trip off, not a state-entry one:
            // the switch that decides whether entering a state tells the roster the trip exists
            // still answers "no" for a cancelled trip, and should. Being called off is the one
            // thing the people expecting to go on it have to be told, and it is told here.
            await TripPlanNotifier.CancelledAsync(db, access, user, trip, stateBefore, ct);
        }
        else if (target != ActivityState.Published && TripPlanNotices.AnnouncesChanges(stateBefore))
        {
            // A move of a plan is a change to it, and the one people are most likely to need: a
            // trip put back to a date not yet chosen the evening before is exactly what somebody
            // expecting to go on it has to hear. Told only when they were already expecting it —
            // a plan leaving the workshop announces itself by other means, and the notice would
            // otherwise be the first they heard of it. Whether the state it lands in is still one
            // people are expecting anything from is the notifier's own question, so a plan going
            // back into the workshop or off to be written up stays silent.
            await TripPlanNotifier.ChangedAsync(db, access, user, trip, [], ct);
        }

        if (target == ActivityState.Published)
        {
            // The stamp records when the trip first went out and is never moved: withdrawing it and
            // putting it out again is the same announcement corrected, so "since when has this been
            // public" keeps one answer.
            //
            // Telling people is a separate question with a different answer, on purpose. Every
            // announcement tells whoever may read the trip at that moment, including one that
            // follows a withdrawal, because the alternative is worse: while a trip is back in draft
            // its edits notify nobody, so announcing only the first time would silently leave out
            // everybody added in between — and they are exactly the people the announcement is for.
            // A correction cycle therefore costs the roster a repeated message, which is the side
            // to err on.
            trip.PublishedAt ??= DateTimeOffset.UtcNow;
            await NotifyOnPublishAsync(db, announcer, trip, ct);
        }

        await db.SaveChangesAsync(ct);

        var items = await MapWithChildrenAsync(db, access, protection, ctx, user, [trip], ct);
        return TypedResults.Ok(items[0]);
    }

    /// <summary>
    /// Tells the people named on a trip that it has been announced.
    /// </summary>
    /// <remarks>
    /// The roster is re-read here rather than trusting a list assembled while the trip was a
    /// draft: a draft's roster is written and rewritten before anyone else sees it, so the people
    /// to tell at the moment of the announcement are whoever is on it then, not whoever was added
    /// by the last edit. Whether each of them may actually read the trip is decided by the
    /// notifier itself, for every caller alike.
    /// </remarks>
    private static async Task NotifyOnPublishAsync(
        SilexGisDbContext db, ITripRosterAnnouncer announcer, TripLog trip, CancellationToken ct)
    {
        var roster = await (
            from participant in db.TripLogParticipants.AsNoTracking()
            join caver in db.Cavers.AsNoTracking() on participant.CaverId equals caver.Id
            where participant.TripLogId == trip.Id && caver.UserId != null
            select caver.UserId!.Value)
            .Distinct()
            .ToListAsync(ct);

        await announcer.AnnounceAsync(trip, roster, ct);
    }

    /// <summary>
    /// Arms, re-arms or calls off the overdue check from the two times the request states.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The state is not a field anybody sends, because a surface that could post "armed" could
    /// post "stood down" too, and saying a party is out belongs to the people on the trip rather
    /// than to whoever may edit it. So it follows from the alarm time, by three readings:
    /// </para>
    /// <para>
    /// No alarm time means there is no arrangement — a callout is called off by clearing the hour
    /// it was set for, and there is nothing else an absent alarm on a trip somebody is editing
    /// could be asking for.
    /// </para>
    /// <para>
    /// An alarm time that differs from the stored one is a new arrangement and arms the check,
    /// whichever state it was in. A party who came back, stood the check down and then went in
    /// again for the evening has arranged a second callout, not repeated the first.
    /// </para>
    /// <para>
    /// The same alarm time leaves the state alone, and that reading is the load-bearing one: it is
    /// what stops an edit made for some entirely other reason — a note added, a person added,
    /// anything a form saves whole — from quietly re-arming a check that somebody has already
    /// stood down, which would raise an alarm about a party that is sitting in the pub.
    /// </para>
    /// </remarks>
    private static void ApplyCallout(TripLog trip, TripCalloutRequest request)
    {
        var armedFor = trip.CalloutAlarmAt;
        trip.ExpectedReturnAt = request.ExpectedReturnAt;
        trip.CalloutAlarmAt = request.CalloutAlarmAt;

        if (request.CalloutAlarmAt is null)
        {
            trip.CalloutState = TripCalloutState.None;
        }
        else if (request.CalloutAlarmAt != armedFor)
        {
            trip.CalloutState = TripCalloutState.Armed;
        }
    }

    /// <summary>
    /// Whether the scheduled pass would actually look at this trip's check, which is a narrower
    /// question than whether a check is arranged on it.
    /// </summary>
    /// <remarks>
    /// A check on a trip that was called off or put back stays on the record — clearing it is a
    /// person's decision, not a rule's — but no pass will ever raise it, because the arrangement it
    /// was set against no longer describes anything happening. Saying when the pass last ran would
    /// then be answering a question nobody asked: the pass did run, and it deliberately looked
    /// straight past this trip. Reporting that as <i>checked</i> is the one reading this whole
    /// value exists to prevent, so the answer is withheld and the surface falls back to saying the
    /// check is not being watched.
    /// </remarks>
    private static bool IsWatched(TripLog trip) =>
        trip.CalloutState is TripCalloutState.Armed or TripCalloutState.Overdue
            && TripCalloutRules.WatchesForOverdue(trip.State);

    /// <summary>
    /// The request as the write core reads it: geometry parsed out of the wire format, sections
    /// as raw text, nothing left that knows what JSON is.
    /// </summary>
    /// <remarks>
    /// A shape that arrived and could not be read as a geometry is reported rather than dropped,
    /// because the refusal it earns is ordered with the reference checks the core makes and
    /// therefore falls after the decision about whether this caller may create a trip at all.
    /// </remarks>
    private static TripWriteInput ToInput(TripLogWriteRequest request) => new()
    {
        Title = request.Title,
        TripTypeId = request.TripTypeId,
        TripDate = request.TripDate,
        TripDateEnd = request.TripDateEnd,
        EntryTime = request.EntryTime,
        ExitTime = request.ExitTime,
        Description = request.Description,
        Results = request.Results,
        WeatherConditions = request.WeatherConditions,
        LocationText = request.LocationText,
        OrganizingCavingGroupId = request.OrganizingCavingGroupId,
        Geom = request.Geom?.ToGeometryOrNull(),
        MeetingGeom = request.MeetingGeom?.ToGeometryOrNull(),
        GeometryMalformed = Unreadable(request.Geom) || Unreadable(request.MeetingGeom),
        CaveIds = request.CaveIds,
        Participants = [.. request.Participants.Select(ToEntry)],
        Proposers = request.Proposers is { } proposers ? [.. proposers.Select(ToEntry)] : null,
        CavingGroupId = request.CavingGroupId,
        Visibility = request.Visibility,
        DepthReachedM = request.DepthReachedM,
        LengthSurveyedM = request.LengthSurveyedM,
        SurveyStations = request.SurveyStations,
        RopeMetres = request.RopeMetres,
        HadIncident = request.HadIncident,
        MaxParticipants = request.MaxParticipants,
        Sections = SectionsOf(request),
    };

    private static bool Unreadable(GeoJsonGeometry? geometry) =>
        geometry is not null && geometry.ToGeometryOrNull() is null;

    private static TripRosterEntry ToEntry(TripParticipantWrite write) => new()
    {
        CaverId = write.CaverId,
        NewCaverName = write.NewCaverName,
        RoleId = write.RoleId,
        EntryTime = write.EntryTime,
        ExitTime = write.ExitTime,
        Note = write.Note,
    };

    /// <summary>
    /// The three sections as raw text, or null for one the request does not mention. Absent and
    /// "an empty object" are different answers: the first leaves what is stored alone, the
    /// second clears it.
    /// </summary>
    private static TripSectionWrite SectionsOf(TripLogWriteRequest request) => new(
        RawSection(request.FieldData), RawSection(request.Logistics), RawSection(request.Safety));

    private static string? RawSection(JsonElement? section) =>
        section is { ValueKind: JsonValueKind.Object } value ? value.GetRawText() : null;

    /// <summary>
    /// Batch-loads caves/participants, applies cave-link redaction, and holds back the parts of
    /// a trip that answer to a narrower audience than the trip itself.
    /// </summary>
    /// <remarks>
    /// Reachable from the rest of this slice on purpose, and the only way into a trip's contents:
    /// what a caller may be told about a trip is decided here, once, so a surface that renders a
    /// trip some other way — a written-up document, say — inherits every one of these decisions
    /// instead of restating them. A second reading of the tables is how two surfaces come to
    /// disagree about who may see what, long after both were written.
    /// </remarks>
    internal static async Task<List<TripLogDto>> MapWithChildrenAsync(
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        AccessContext ctx,
        UserContext user,
        IReadOnlyList<TripLog> trips,
        CancellationToken ct)
    {
        var tripIds = trips.Select(x => x.Id).ToList();

        // Every role at once — the list means "caves this trip is about", which is what the
        // roles collectively say. Narrowed to caves because that is what the field promises and
        // what its readers resolve; a role naming a spring or a shaft belongs to the roles, not
        // here. Distinct because two roles naming one cave are two rows and one cave.
        var caveLinks = await TripRoleLinks.PairsForAsync(db, tripIds, FeatureKind.Cave, ct);

        // Which camp gathered each trip, narrowed to the camps this caller may read. A camp is
        // governed in its own right, so naming one on a trip a caller may read would hand them
        // the identity of a thing they have no right to open — and the identity is enough to ask
        // for it. Filtered in the statement, not after it, for the reason every other listing
        // here is: a filter applied to results is a filter somebody later forgets to apply.
        var readableCampIds = db.Expeditions.AsNoTracking()
            .VisibleTo(ctx, AccessDomain.Expeditions).Select(e => e.Id);
        var campOfTrip = await db.ExpeditionTrips.AsNoTracking()
            .Where(m => tripIds.Contains(m.TripLogId) && readableCampIds.Contains(m.ExpeditionId))
            .Select(m => new { m.TripLogId, m.ExpeditionId })
            .ToDictionaryAsync(m => m.TripLogId, m => m.ExpeditionId, ct);

        // When the pass that watches for overdue parties last finished, asked only when one of
        // these trips actually has a live check — the answer means nothing for a trip nobody
        // arranged one for, and asking anyway would put a query on every listing of every trip.
        //
        // Only a pass that succeeded counts. A pass that failed checked nothing, and a failure
        // that reported itself as "last checked at" would be the exact reassurance this value
        // exists to withhold: an armed alarm whose watcher has not run is unchecked, and a surface
        // has to be able to say so. Null — no pass has ever completed — says the same thing more
        // strongly, so it is never dressed up as "not applicable".
        DateTimeOffset? lastSwept = null;
        HashSet<Guid> onTheTrip = [];
        var liveCallouts = trips
            .Where(t => t.CalloutState is TripCalloutState.Armed or TripCalloutState.Overdue)
            .Select(t => t.Id)
            .ToList();
        if (liveCallouts.Count > 0)
        {
            lastSwept = await db.ProcessingJobs.AsNoTracking()
                .Where(j => j.Kind == ProcessingJobKinds.TripCalloutSweep
                    && j.Status == ProcessingJobStatus.Succeeded)
                .MaxAsync(j => j.CompletedAt, ct);

            // Asked from the one place that knows who a trip concerns, and asked only about the
            // trips where the answer could mean anything.
            onTheTrip = await TripAudience.ConcerningAsync(db, liveCallouts, user.UserId, ct);
        }

        var roles = await TripLogWriteService.ShippedRosterRolesAsync(db, ct);
        var participantRows = await (
            from participant in db.TripLogParticipants.AsNoTracking()
            join caver in db.Cavers.AsNoTracking() on participant.CaverId equals caver.Id
            where tripIds.Contains(participant.TripLogId)
            select new
            {
                participant.TripLogId,
                participant.RoleId,
                participant.CaverId,
                caver.UserId,
                participant.EntryTime,
                participant.ExitTime,
                participant.Note,
            })
            .ToListAsync(ct);

        // Resolved rather than joined: the label a participant may be shown under is a rule with
        // one home, and it is never their address. People without an account keep their roster name.
        var labels = await CaverDirectory.ResolveLabelsAsync(
            db, user, participantRows.Select(x => x.CaverId), ct);

        var participants = participantRows
            .Select(x => new
            {
                x.TripLogId,
                x.RoleId,
                Dto = new TripParticipantDto(
                    x.CaverId,
                    labels.GetValueOrDefault(x.CaverId) ?? string.Empty,
                    x.UserId,
                    x.RoleId,
                    x.EntryTime,
                    x.ExitTime,
                    x.Note),
            })
            .ToList();

        // Which of the caves these trips name this caller may be told about — readable first,
        // then placeable. Decided for the whole page at once; what is left out is counted per
        // trip below rather than named.
        var disclosableCaves = await TripCaveDisclosure.DisclosableCaveIdsAsync(
            db, protection, ctx, [.. caveLinks.Select(x => x.FeatureId)], ct);

        // Which of these trips this caller may change, decided for the whole page at once so a
        // longer listing does not cost more round trips. It answers one question here: who is
        // told what went wrong, as against who is told that something did.
        var writable = await ProtectedWrites.WritableAsync(access, ctx, trips, ct);

        // How much of the list its purpose names each of these trips has settled, resolved for the
        // whole page at once rather than per row: which list a trip is measured against belongs to
        // its purpose, so asking per trip would put the same query on a page fifty times. Nothing
        // here narrows the page — a trip with nothing settled is on this list exactly as a trip
        // fully settled is, because how ready a party is is a reading for that party and never a
        // rule about who may see the plan.
        var readiness = await TripChecklistReads.ReadinessForAsync(db, ctx, trips, ct);

        return [.. trips.Select(trip => MapOne(trip, writable.Contains(trip.Id)))];

        TripLogDto MapOne(TripLog trip, bool mayWrite)
        {
            var (safety, safetyVersion) = TripDisclosure.Safety(trip, mayWrite);
            var named = caveLinks.Where(x => x.TripId == trip.Id).Select(x => x.FeatureId).ToList();
            return new TripLogDto(
                trip.Id,
                trip.Title,
                trip.TripTypeId,
                trip.TripDate,
                trip.TripDateEnd,
                trip.EntryTime,
                trip.ExitTime,
                trip.Description,
                trip.Results,
                trip.WeatherConditions,
                trip.LocationText,
                trip.OrganizingCavingGroupId,
                trip.Geom is null ? null : GeoJsonGeometry.From(trip.Geom),
                [.. named.Where(disclosableCaves.Contains)],
                // Everyone but the proposers, whatever job they did, so a role added to the
                // vocabulary after this was written shows up as somebody who was there rather
                // than as nobody at all. The two lists partition the roster between them.
                [.. participants.Where(x => x.TripLogId == trip.Id && x.RoleId != roles.Proposer).Select(x => x.Dto)],
                [.. participants.Where(x => x.TripLogId == trip.Id && x.RoleId == roles.Proposer).Select(x => x.Dto)],
                trip.OwnerUserId,
                trip.CavingGroupId,
                trip.Visibility,
                trip.CreatedAt,
                trip.UpdatedAt,
                trip.State,
                trip.PublishedAt,
                trip.DepthReachedM,
                trip.LengthSurveyedM,
                trip.SurveyStations,
                trip.RopeMetres,
                trip.HadIncident,
                JsonSerializer.Deserialize<JsonElement>(trip.FieldData),
                trip.FieldDataSchemaVersion,
                JsonSerializer.Deserialize<JsonElement>(trip.Logistics),
                trip.LogisticsSchemaVersion,
                safety is null ? null : JsonSerializer.Deserialize<JsonElement>(safety),
                safetyVersion,
                campOfTrip.TryGetValue(trip.Id, out var campId) ? campId : null,
                named.Count(id => !disclosableCaves.Contains(id)),
                trip.MaxParticipants,
                trip.ExpectedReturnAt,
                trip.CalloutAlarmAt,
                trip.CalloutState,
                IsWatched(trip) ? lastSwept : null,
                onTheTrip.Contains(trip.Id),
                trip.MeetingGeom is null ? null : GeoJsonGeometry.From(trip.MeetingGeom),
                readiness.TryGetValue(trip.Id, out var settled)
                    ? new TripChecklistReadinessDto(
                        settled.ChecklistId, settled.Readiness.Ticked, settled.Readiness.Total)
                    : null);
        }
    }
}
