// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Api.Features.TripLogs;

public static class TripLogEndpoints
{
    public static RouteGroupBuilder MapTripLogEndpoints(this RouteGroupBuilder api)
    {
        var trips = api.MapGroup("/trip-logs").WithTags("TripLogs");

        trips.MapGet("/", ListAsync)
            .WithSummary("Paged trip logs with date/cave filters; visibility-filtered.");
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

        // A trip may run across several days, so the window asks whether the trip overlapped it
        // rather than whether it started inside it: a trip that ran 27 February to 2 March belongs
        // in March as much as in February. A trip with no end date is one day long.
        if (from is not null)
        {
            var start = from.Value;
            query = query.Where(x => (x.TripDateEnd ?? x.TripDate) >= start);
        }

        if (to is not null)
        {
            var end = to.Value;
            query = query.Where(x => x.TripDate <= end);
        }

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
            if ((await DisclosableCaveIdsAsync(db, protection, ctx, [caveId.Value], ct)).Count == 0)
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
        TripSectionWriter sections,
        CancellationToken ct) =>
        CreateCoreAsync(
            TripCreationIntent.Report, request, db, access, accessAccessor, userAccessor,
            protection, sections, ct);

    /// <summary>Creates a trip that has not happened yet.</summary>
    private static Task<Results<Created<TripLogDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreatePlanAsync(
        TripLogWriteRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        TripSectionWriter sections,
        CancellationToken ct) =>
        CreateCoreAsync(
            TripCreationIntent.Plan, request, db, access, accessAccessor, userAccessor,
            protection, sections, ct);

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
        TripSectionWriter sections,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        // Who may read the trip is one answer in two values, so the default decides it only when
        // the request answers neither of them. A request that names either half has taken the
        // decision itself and both halves are read as it sent them — a stated audience with no
        // group binding is somebody saying "not the club", and quietly supplying one would widen
        // what they asked for. A request that names only a group is still naming a group: the
        // audience falls back, but dropping the binding would take the row out of every rule
        // written about that group's content, a refusal aimed at the group among them.
        //
        // Settled before the create check and the reference checks below, so a binding this rule
        // supplies is guarded exactly like one the caller typed rather than slipping in behind
        // them.
        var fallback = TripAudienceRules.DefaultAudience(intent, ctx.CavingGroupIds);
        var (visibility, cavingGroupId) = request.Visibility is null && request.CavingGroupId is null
            ? fallback
            : (request.Visibility ?? fallback.Visibility, request.CavingGroupId);

        if (!CreateRules.MayCreate(ctx, AccessDomain.TripLogs, cavingGroupId))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        var problem = await ValidateReferencesAsync(db, ctx, request with { CavingGroupId = cavingGroupId }, ct);
        if (problem is not null)
        {
            return problem;
        }

        var trip = new TripLog { Title = request.Title, OwnerUserId = user.UserId };
        Apply(trip, request);
        trip.Visibility = visibility;
        trip.CavingGroupId = cavingGroupId;
        // A new row has nothing stored, so every section is a first write and is measured
        // against the purpose's schemas as they stand.
        try
        {
            await sections.ApplyAsync(trip, SectionsOf(request), typeChanged: true, ct);
        }
        catch (TripWriteException e)
        {
            return ApiProblems.BadRequest(e.Code, e.Message);
        }

        db.TripLogs.Add(trip);
        // No existing children on create, so the reconcile helpers reduce to pure inserts.
        if (request.CaveIds is { } caveIds)
        {
            await ReconcileCaveLinksAsync(db, protection, ctx, trip.Id, caveIds, ct);
        }

        var added = await ReconcileRosterAsync(db, trip.Id, request, ct);
        await NotifyParticipantsAsync(db, access, user, trip, added, ct);
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
        TripSectionWriter sections,
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

        var problem = await ValidateReferencesAsync(db, ctx, request, ct);
        if (problem is not null)
        {
            return problem;
        }

        // Read before Apply overwrites it: moving a trip to another purpose re-measures all
        // three sections, because the schemas they answer to are not the ones they were
        // measured against any more.
        var typeChanged = trip.TripTypeId != request.TripTypeId;
        Apply(trip, request);
        try
        {
            await sections.ApplyAsync(trip, SectionsOf(request), typeChanged, ct);
        }
        catch (TripWriteException e)
        {
            return ApiProblems.BadRequest(e.Code, e.Message);
        }

        if (request.CaveIds is { } caveIds)
        {
            await ReconcileCaveLinksAsync(db, protection, ctx, trip.Id, caveIds, ct);
        }

        var added = await ReconcileRosterAsync(db, trip.Id, request, ct);
        await NotifyParticipantsAsync(db, access, user, trip, added, ct);
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

        // Participant rows cascade; polymorphic rows are cleaned here.
        //
        // The trip's place in a camp goes with it, and the camp is otherwise untouched: a camp
        // that gathered this trip has one fewer member, which is what deleting the trip means.
        await db.ExpeditionTrips.Where(m => m.TripLogId == trip.Id).ExecuteDeleteAsync(ct);

        // Every rule anchored on this trip goes with it — the ones authored on its own
        // permissions tab and the ones a camp's sharing wrote onto it alike. A rule whose
        // anchor no longer exists is what the integrity check reports as an orphan, and it
        // reads as a live grant on every surface that lists rules by subject.
        //
        // Loaded and removed rather than deleted in one statement, because a rule
        // disappearing is a change to who may reach what, and every other place rules are
        // withdrawn records that. A set-based delete never reaches the change tracker, so the
        // withdrawal would happen with nothing in the trail to say it had.
        var anchored = await db.AccessEntries
            .Where(e => e.Domain == AccessDomain.TripLogs
                && e.ScopeKind == AccessScopeKind.Object
                && e.ScopeId == trip.Id)
            .ToListAsync(ct);
        db.AccessEntries.RemoveRange(anchored);

        await db.Attachments
            .Where(a => a.EntityType == AttachedEntityType.TripLog && a.EntityId == trip.Id)
            .ExecuteDeleteAsync(ct);
        await db.Taggings
            .Where(x => x.EntityType == AttachedEntityType.TripLog && x.EntityId == trip.Id)
            .ExecuteDeleteAsync(ct);
        // The trip's memberships go, and so do the links that cannot mean anything without it.
        //
        // A link typed with one of the trip roles goes whole, however many features it still
        // names: the role says what *this trip* did there, so the surviving members are not
        // related to each other by anything once the trip is gone. Leaving it would also leave a
        // directed link with no distinguished member, which the link rules refuse — the result
        // would show on every named cave's links panel as a relation to the other caves, and no
        // later edit of it would be accepted.
        //
        // A link of any other kind the trip merely joined keeps whatever it still relates, and
        // goes only when one member is left: an association with one end is a thing no surface
        // offers and no delete path would ever reach again. Remaining members cascade with it.
        var roleIds = TripRoleLinks.RoleIds(db);
        var linkIds = await db.ResLinkMembers
            .Where(m => m.EntityType == AttachedEntityType.TripLog && m.EntityId == trip.Id)
            .Select(m => m.ResLinkId)
            .Distinct()
            .ToListAsync(ct);
        var roleLinkIds = await db.ResLinks
            .Where(l => linkIds.Contains(l.Id)
                && l.RelationTypeId != null && roleIds.Contains(l.RelationTypeId.Value))
            .Select(l => l.Id)
            .ToListAsync(ct);
        await db.ResLinkMembers
            .Where(m => m.EntityType == AttachedEntityType.TripLog && m.EntityId == trip.Id)
            .ExecuteDeleteAsync(ct);
        await db.ResLinks
            .Where(l => roleLinkIds.Contains(l.Id)
                || (linkIds.Contains(l.Id) && db.ResLinkMembers.Count(m => m.ResLinkId == l.Id) < 2))
            .ExecuteDeleteAsync(ct);
        db.TripLogs.Remove(trip);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
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

        trip.State = target;
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
            await NotifyOnPublishAsync(db, access, user, trip, ct);
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
        SilexGisDbContext db, IAccessService access, UserContext user, TripLog trip, CancellationToken ct)
    {
        var roster = await (
            from participant in db.TripLogParticipants.AsNoTracking()
            join caver in db.Cavers.AsNoTracking() on participant.CaverId equals caver.Id
            where participant.TripLogId == trip.Id && caver.UserId != null
            select caver.UserId!.Value)
            .Distinct()
            .ToListAsync(ct);

        await NotifyParticipantsAsync(db, access, user, trip, roster, ct);
    }

    private static void Apply(TripLog trip, TripLogWriteRequest request)
    {
        trip.Title = request.Title;
        trip.TripTypeId = request.TripTypeId;
        trip.TripDate = request.TripDate;
        trip.TripDateEnd = request.TripDateEnd;
        trip.EntryTime = request.EntryTime;
        trip.ExitTime = request.ExitTime;
        trip.Description = request.Description;
        trip.Results = request.Results;
        trip.WeatherConditions = request.WeatherConditions;
        trip.LocationText = request.LocationText;
        trip.DepthReachedM = request.DepthReachedM;
        trip.LengthSurveyedM = request.LengthSurveyedM;
        trip.SurveyStations = request.SurveyStations;
        trip.RopeMetres = request.RopeMetres;
        trip.HadIncident = request.HadIncident;
        trip.OrganizingCavingGroupId = request.OrganizingCavingGroupId;
        trip.Geom = request.Geom?.ToGeometryOrNull();
        // An audience the request does not name is left exactly as it stands. The only place a
        // trip's audience is decided for it is the moment it is created, and it is decided there
        // before this runs — so a null arriving here can only mean "not editing who may read it",
        // and a save from a surface that never drew the field cannot quietly narrow or widen one.
        //
        // The group binding moves with it rather than on its own, because the two are one answer:
        // a group-visible trip whose binding is cleared names no group and is therefore readable
        // by nobody but its owner. Writing the binding unconditionally would do exactly that to
        // every save from a surface that drew neither field — the case the nullability above
        // exists to protect — and it would do it silently, with the stored audience still
        // reading "the caving group".
        if (request.Visibility is { } visibility)
        {
            trip.Visibility = visibility;
            trip.CavingGroupId = request.CavingGroupId;
        }
    }

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
    /// The role a bare list of caves is written under. The list says the trip is about those
    /// caves and nothing finer, so it is recorded as the plainest of the roles that carries
    /// that meaning; a trip that did something more particular there says so through the role
    /// it was recorded under, and this path never overwrites that.
    /// </summary>
    private const string CaveListRole = "trip-visited";

    /// <summary>
    /// Of the caves a trip's roles name, the ones this caller may be told about at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two gates, in this order. The first is readability: <em>naming a cave is a read of the
    /// cave</em>. A trip's own audience is not the cave's — a cave nobody but its owner may open
    /// can be named on a trip half the club reads, and a trip's readership is in general a list
    /// somebody types. Handing over the identifier of such a cave hands over the one thing that
    /// is enough to go and ask for the cave elsewhere, so the identifier is withheld rather than
    /// the name alone — on the trip's own cave list and on everything built from it, which is
    /// what this rule governs and the whole of what it claims. A trip carries other panels with
    /// withholding rules of their own, and they answer for themselves.
    /// </para>
    /// <para>
    /// The second is placement, and it is a separate question with a separate answer: a trip
    /// carries its own exact geometry, so "this trip reached that cave" places a guarded cave by
    /// proximity even when the cave itself is perfectly readable. Neither gate implies the other
    /// — the placement walk deliberately answers only about position and reads its rows past
    /// every visibility filter, so it can never stand in for the first.
    /// </para>
    /// <para>
    /// This is the one home of that rule for a trip's cave list. Every surface carrying the list
    /// asks here — the read that produces it and the write that reconciles it alike — because
    /// the write has to put back exactly what the read took out, and two copies of the predicate
    /// are two answers waiting to drift apart.
    /// </para>
    /// </remarks>
    internal static async Task<HashSet<Guid>> DisclosableCaveIdsAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        IReadOnlyCollection<Guid> namedCaveIds,
        CancellationToken ct)
    {
        var ids = namedCaveIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        // Narrowed in the statement rather than after it, and narrowed to caves in the same
        // predicate: the list promises caves, and a role naming a spring belongs to the roles.
        var readable = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => ids.Contains(f.Id) && f.Kind == FeatureKind.Cave)
            .Select(f => f.Id)
            .ToListAsync(ct);

        // Asked over what survived the first gate, never over the whole named set: the position
        // rule cannot be expressed in the same statement, and asking it first would let an
        // unguarded private cave through on the strength of having no position to guard.
        var redacted = await protection.RedactedLinkTargetIdsAsync(ctx, readable, ct);
        return [.. readable.Where(id => !redacted.Contains(id))];
    }

    // Reconcile with a diff (add/remove only what changed) rather than delete-all +
    // recreate-all: a full recreate logs a "created" event for every unchanged child on every
    // save, so the diff keeps the timeline honest. Also preserves caves the caller was never
    // shown, for either of the two reasons a cave is kept off the list they edited: treating a
    // list handed over short as the whole truth would silently drop them.
    //
    // That last guard is why a list is only reconciled when one is actually supplied. Naming a
    // cave one at a time — which is how it is done now — cannot express "forget everything not
    // in this list", so the hazard simply does not arise there; it arises only here, where an
    // absence has to be read as an instruction, and here it is guarded.
    //
    // Reads over every role, writes under one, and that asymmetry is chosen rather than
    // tolerated. A cave the trip already names — whatever it did there — is left exactly as it is
    // rather than named a second time, and a cave dropped from the list is unnamed only from the
    // role this path writes: a list with no roles in it is not an instruction to forget that the
    // trip surveyed somewhere, and one coarse list must not be able to erase a finer statement
    // somebody made on purpose elsewhere. The consequence, accepted with the rule: dropping a
    // cave the trip holds only under some other role does nothing. Nothing is hidden by that —
    // this write answers with the trip read afresh, whose list still names that cave — and the
    // way to take such a cave off a trip is through the role that put it there.
    private static async Task ReconcileCaveLinksAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        Guid tripId,
        IReadOnlyList<Guid> requestedCaveIds,
        CancellationToken ct)
    {
        // Read through exactly the narrowing the caller was answered through, caves only. A role
        // names any linkable target, and a spring or a shaft named under one of them can never
        // appear in a list of caves — so a view any wider here would read those as absences and
        // unname them, on a request that never mentioned them. What the caller could not have
        // been shown, they cannot be taken to have dropped.
        var named = (await TripRoleLinks.PairsForAsync(db, [tripId], FeatureKind.Cave, ct))
            .Select(pair => pair.FeatureId)
            .Distinct()
            .ToList();

        // Put back everything this caller was never shown, decided by the very function that
        // decided what to show them. The two have to agree exactly: whatever the read takes out,
        // the write puts back. A narrower re-add is not a smaller safeguard — it is a silent
        // deletion, because the caller omits a cave they were never offered and the trip loses a
        // link nobody asked to drop. That is why this asks the shared rule rather than the
        // position rule alone: the position rule reads its rows past every visibility filter and
        // answers only about where a cave is, so a cave held back for being unreadable is not
        // among the ones it names.
        var disclosable = await DisclosableCaveIdsAsync(db, protection, ctx, named, ct);
        var desired = requestedCaveIds.Concat(named.Where(id => !disclosable.Contains(id))).ToHashSet();

        foreach (var caveId in named.Where(id => !desired.Contains(id)))
        {
            await TripRoleLinks.UnnameFeatureAsync(db, tripId, caveId, CaveListRole, ct);
        }

        foreach (var caveId in desired.Where(id => !named.Contains(id)))
        {
            // A role code that is not in the vocabulary means the installation's link types were
            // never seeded — the naming would silently record nothing, and answering 200 to a
            // write that stored nothing is worse than failing.
            if (!await TripRoleLinks.NameFeatureAsync(db, tripId, caveId, CaveListRole, ctx.UserId, ct))
            {
                throw new InvalidOperationException($"Relation type '{CaveListRole}' is not seeded.");
            }
        }
    }

    /// <summary>
    /// The two roles the trip write path names by itself: everyone a trip records was either
    /// simply there or put it forward, and both are shipped rows precisely so this can rely on
    /// them existing. Missing means the vocabulary was never seeded, and a write that stored
    /// nobody while answering 200 is worse than one that fails.
    /// </summary>
    private static async Task<(long Participant, long Proposer)> ShippedRosterRolesAsync(
        SilexGisDbContext db, CancellationToken ct)
    {
        var ids = await db.TripParticipantRoles.AsNoTracking()
            .Where(r => r.Code == TripParticipantRoleSeeds.ParticipantCode
                || r.Code == TripParticipantRoleSeeds.ProposerCode)
            .ToDictionaryAsync(r => r.Code, r => r.Id, ct);

        if (!ids.TryGetValue(TripParticipantRoleSeeds.ParticipantCode, out var participant)
            || !ids.TryGetValue(TripParticipantRoleSeeds.ProposerCode, out var proposer))
        {
            throw new InvalidOperationException("The shipped participant roles are not seeded.");
        }

        return (participant, proposer);
    }

    /// <summary>
    /// Brings a trip's whole roster in line with what was asked for, and reports the registered
    /// users genuinely newly listed — the only point at which that is knowable, since afterwards
    /// an added row is indistinguishable from one that was already there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The roster is reconciled in one pass over every role, because the two lists together are
    /// the whole of it: a row in a job neither list mentions has been withdrawn, and leaving it
    /// standing would make a job impossible to take away once given. What that costs is stated on
    /// the request itself — a surface showing people must send back the rows it did not show.
    /// </para>
    /// <para>
    /// A row already there in the same job is kept and brought up to date rather than replaced,
    /// so an unchanged roster writes nothing: the change tracker sees equal values and records no
    /// history, which is what keeps re-saving a trip out of its timeline.
    /// </para>
    /// <para>
    /// **Newly listed is asked of the trip, not of the job.** Somebody already named on the trip
    /// who is now also its surveyor learns nothing from being told they are on a trip they are
    /// already on, so the second row tells nobody. Only a person the trip did not name at all
    /// before this write is new to it.
    /// </para>
    /// </remarks>
    private static async Task<List<Guid>> ReconcileRosterAsync(
        SilexGisDbContext db, Guid tripId, TripLogWriteRequest request, CancellationToken ct)
    {
        var roles = await ShippedRosterRolesAsync(db, ct);

        // Each list says what its entries are for; an entry naming its own role overrides that,
        // which is how a job beyond the two the lists are named after gets recorded at all.
        var requested = new List<(long RoleId, TripParticipantWrite Write)>();
        requested.AddRange(request.Participants.Select(p => (p.RoleId ?? roles.Participant, p)));
        requested.AddRange((request.Proposers ?? []).Select(p => (p.RoleId ?? roles.Proposer, p)));

        // A name with no roster entry becomes one, so the person can be counted and found again
        // on later trips. Repeating a name already in the roster reuses it rather than making a
        // second entry for the same person.
        var named = requested
            .Where(p => p.Write.CaverId is null)
            .Select(p => p.Write.NewCaverName!.Trim())
            .Where(name => name.Length > 0)
            .ToList();

        // Two people can share a name — that is exactly the state the roster merge exists to
        // resolve — so the lookup groups before it keys. Keying the query straight by name would
        // fault on the duplicate and lose the whole trip write over a coincidence of spelling.
        // The oldest entry wins, so the same typed name resolves to the same person every time
        // rather than to whichever row the database happened to return first.
        var matchedRows = named.Count == 0
            ? []
            : await db.Cavers.Where(c => named.Contains(c.FullName))
                .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
                .ToListAsync(ct);
        var matched = matchedRows
            .GroupBy(c => c.FullName)
            .ToDictionary(g => g.Key, g => g.First().Id);

        // Keyed on the pair the roster is unique on, so one person in two jobs is two entries and
        // the same person named twice for one job is one — the last of them, since a request that
        // says a thing twice means it once.
        var desired = new Dictionary<(long RoleId, Guid CaverId), TripParticipantWrite>();
        foreach (var (roleId, write) in requested)
        {
            var caverId = write.CaverId;
            if (caverId is null)
            {
                var name = write.NewCaverName!.Trim();
                if (!matched.TryGetValue(name, out var existingId))
                {
                    var created = new Caver { FullName = name };
                    db.Cavers.Add(created);
                    matched[name] = created.Id;
                    existingId = created.Id;
                }

                caverId = existingId;
            }

            desired[(roleId, caverId.Value)] = write;
        }

        var existing = await db.TripLogParticipants.Where(x => x.TripLogId == tripId).ToListAsync(ct);

        // Read before the loop below empties `desired`, and before any row is removed: this is
        // the trip's roster as it stood when the request arrived, which is the only thing that
        // can answer whether a person is new to the trip.
        var alreadyNamed = existing.Select(x => x.CaverId).ToHashSet();

        foreach (var participant in existing)
        {
            if (desired.Remove((participant.RoleId, participant.CaverId), out var write))
            {
                participant.EntryTime = write.EntryTime;
                participant.ExitTime = write.ExitTime;
                participant.Note = Trimmed(write.Note);
                continue;
            }

            db.TripLogParticipants.Remove(participant);
        }

        foreach (var (key, write) in desired)
        {
            db.TripLogParticipants.Add(new TripLogParticipant
            {
                TripLogId = tripId,
                RoleId = key.RoleId,
                CaverId = key.CaverId,
                EntryTime = write.EntryTime,
                ExitTime = write.ExitTime,
                Note = Trimmed(write.Note),
            });
        }

        var newcomers = desired.Keys
            .Select(key => key.CaverId)
            .Where(caverId => !alreadyNamed.Contains(caverId))
            .Distinct()
            .ToList();

        // Only the newly listed people who hold an account: there is nobody to tell for the rest.
        return await db.Cavers
            .Where(c => newcomers.Contains(c.Id) && c.UserId != null)
            .Select(c => c.UserId!.Value)
            .ToListAsync(ct);
    }

    private static string? Trimmed(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Tells the people newly listed on a trip. Queued before the caller's save, so a notification
    /// exists only if the trip write it describes actually committed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The message names the trip and its date and nothing else. A trip's linked caves are
    /// deliberately absent: a cave link beside a trip's own location is exactly the disclosure the
    /// cave-link redaction rules exist to prevent, and a notification is no less an outbound copy
    /// of that data than a DTO is.
    /// </para>
    /// <para>
    /// Every path that would tell somebody about a trip comes through here, so the two rules that
    /// decide whether a message goes out at all are checked here once, for every caller alike. A
    /// trip being written and a trip called off both keep their silence however their roster is
    /// edited; and nobody is told about a trip they could not open, whether their name went on it
    /// through an edit or through the announcement. A message to somebody the trip is closed to
    /// would be useless to them and would still hand them its title and date, so the recipient's
    /// own right to read it is decided here — freshly, against the trip as it now stands — rather
    /// than assumed from their being named on it.
    /// </para>
    /// </remarks>
    private static async Task NotifyParticipantsAsync(
        SilexGisDbContext db,
        IAccessService access,
        UserContext user,
        TripLog trip,
        IEnumerable<Guid> addedUserIds,
        CancellationToken ct)
    {
        if (ActivityStates.SuppressesParticipantNotification(trip.State))
        {
            return;
        }

        // Load-bearing rather than defensive: one person holds as many roles on a trip as they
        // did jobs, so one write can newly list the same person several times over.
        var candidates = addedUserIds.Distinct().Where(id => id != user.UserId).ToList();
        var recipients = new List<Guid>();
        foreach (var candidate in candidates)
        {
            var theirs = await AccessContextResolver.ResolveAsync(db, candidate, ct);
            if ((await access.DecideAsync(theirs, AccessAction.Read, trip, ct)).Allowed)
            {
                recipients.Add(candidate);
            }
        }

        if (recipients.Count == 0)
        {
            return;
        }

        var actorLabels = await ProfileDirectory.ResolveLabelsAsync(db, user, [user.UserId], ct);
        var actorName = actorLabels.GetValueOrDefault(user.UserId) ?? string.Empty;

        foreach (var recipient in recipients)
        {
            NotificationQueue.Enqueue(
                db,
                recipient,
                NotificationCategory.TripParticipation,
                MessageTemplateCatalog.NotifyTripParticipation,
                new Dictionary<string, string>
                {
                    ["actorName"] = actorName,
                    ["tripTitle"] = trip.Title,
                    ["tripDate"] = trip.TripDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    ["url"] = $"/trip-logs/{trip.Id}",
                });
        }
    }

    /// <summary>Geometry validity, trip-purpose and cave existence, participant-user existence.</summary>
    private static async Task<ProblemHttpResult?> ValidateReferencesAsync(
        SilexGisDbContext db, AccessContext ctx, TripLogWriteRequest request, CancellationToken ct)
    {
        if (request.Geom is not null && request.Geom.ToGeometryOrNull() is null)
        {
            return ApiProblems.BadRequest("trip_log.geometry_invalid", "Geometry is malformed or invalid.");
        }

        // The purpose vocabulary is a row set an installation extends, so an unknown identity is
        // a plain bad request rather than a shape the request validator could have caught. The
        // vocabulary is readable by every account, so naming a row that does not exist discloses
        // nothing that reading the list would not.
        if (request.TripTypeId is { } tripTypeId
            && !await db.TripTypes.AnyAsync(t => t.Id == tripTypeId, ct))
        {
            return ApiProblems.BadRequest("trip_log.type_unknown", "That trip type does not exist.");
        }

        if (request.CavingGroupId is not null
            && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.TripLogs, request.CavingGroupId.Value))
        {
            return ApiProblems.Forbidden(CavingGroupBindingRules.ForbiddenCode);
        }

        // A cave is a feature row, so existence and readability are one filtered count; an id
        // the caller cannot read is reported exactly like a nonexistent one, so linking cannot
        // be used to probe for caves. Nobody is ever forced to send one: the list a caller was
        // handed holds only caves they may be told about, and the ones kept off it are put back
        // by the reconcile rather than expected back from them — so refusing an unreadable id
        // here cannot turn into a save that fails over a cave the caller never saw.
        var caveIds = (request.CaveIds ?? []).Distinct().ToList();
        if (caveIds.Count > 0)
        {
            var readable = await db.Features.AsNoTracking()
                .Where(f => f.Kind == FeatureKind.Cave && caveIds.Contains(f.Id))
                .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                .CountAsync(ct);
            if (readable != caveIds.Count)
            {
                return ApiProblems.BadRequest("trip_log.cave_not_found", "A linked cave does not exist.");
            }
        }

        var roster = request.Participants.Concat(request.Proposers ?? []).ToList();
        var caverIds = roster
            .Where(x => x.CaverId is not null).Select(x => x.CaverId!.Value).Distinct().ToList();
        if (caverIds.Count > 0)
        {
            var found = await db.Cavers.Where(c => caverIds.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct);
            if (found.Count != caverIds.Count)
            {
                return ApiProblems.BadRequest("trip_log.participant_unknown", "A participant user does not exist.");
            }
        }

        // The role vocabulary is a row set an installation extends, so an unknown identity is a
        // plain bad request for the same reason the purpose is — and the restricting foreign key
        // would otherwise refuse it far below anything that could turn it into an answer.
        var roleIds = roster.Where(x => x.RoleId is not null).Select(x => x.RoleId!.Value).Distinct().ToList();
        if (roleIds.Count > 0)
        {
            var known = await db.TripParticipantRoles.AsNoTracking()
                .CountAsync(r => roleIds.Contains(r.Id), ct);
            if (known != roleIds.Count)
            {
                return ApiProblems.BadRequest(
                    "trip_log.participant_role_unknown", "A participant role does not exist.");
            }
        }

        return null;
    }

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

        var roles = await ShippedRosterRolesAsync(db, ct);
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
        var disclosableCaves = await DisclosableCaveIdsAsync(
            db, protection, ctx, [.. caveLinks.Select(x => x.FeatureId)], ct);

        // Which of these trips this caller may change, decided for the whole page at once so a
        // longer listing does not cost more round trips. It answers one question here: who is
        // told what went wrong, as against who is told that something did.
        var writable = await ProtectedWrites.WritableAsync(access, ctx, trips, ct);

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
                named.Count(id => !disclosableCaves.Contains(id)));
        }
    }
}
