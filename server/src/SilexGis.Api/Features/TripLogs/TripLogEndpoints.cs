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
            .WithSummary("Creates a trip log (Create permission on trip logs); the caller becomes owner.");
        trips.MapPut("/{id:guid}", UpdateAsync).WithValidation<TripLogWriteRequest>()
            .WithSummary(
                "Full update (Write permission). The whole roster is replaced, in every role, so a "
                + "person left out of both lists is taken off the trip; the caves are replaced only "
                + "when a list is supplied, and left as they are when the field is omitted.");
        trips.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes a trip log with its links and attachments.");
        trips.MapPost("/{id:guid}/publish", PublishAsync)
            .WithSummary("Announces a trip log and tells the people named on it (Write permission).");
        trips.MapPost("/{id:guid}/unpublish", UnpublishAsync)
            .WithSummary("Returns a trip log to draft — the reverse of publishing (Write permission).");

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
            // Filtering trips by a location-protected cave would place the cave through
            // the trips' geometries — behave as if nothing is linked.
            if (await protection.ShouldRedactLinkAsync(ctx, caveId, ct))
            {
                var (emptyPage, emptySize) = Paging.Normalize(page, pageSize);
                return TypedResults.Ok(new PagedResult<TripLogDto>([], emptyPage, emptySize, 0));
            }

            // Role-agnostic: the question is which trips this cave is named on, not what they
            // did there. Narrowing it to one role would quietly answer a smaller question.
            var namingTrips = TripRoleLinks.TripIdsNaming(db, caveId.Value);
            query = query.Where(x => namingTrips.Contains(x.Id));
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

    private static async Task<Results<Created<TripLogDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
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

        if (!CreateRules.MayCreate(ctx, AccessDomain.TripLogs, request.CavingGroupId))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        var problem = await ValidateReferencesAsync(db, ctx, request, ct);
        if (problem is not null)
        {
            return problem;
        }

        var trip = new TripLog { Title = request.Title, OwnerUserId = user.UserId };
        Apply(trip, request);
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

    /// <summary>
    /// Announces a trip: the point at which the people named on it are told about it.
    /// </summary>
    private static Task<Results<Ok<TripLogDto>, ProblemHttpResult>> PublishAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        CancellationToken ct) =>
        TransitionAsync(id, ActivityState.Published, http, db, access, accessAccessor, userAccessor, protection, ct);

    /// <summary>
    /// The reverse of publishing. It takes the trip back for more work; it does not undo the
    /// announcement, and the date of the first one is kept. It is also the door a trip that was
    /// called off comes back through, so there is one answer to "how do I get at this again".
    /// </summary>
    private static Task<Results<Ok<TripLogDto>, ProblemHttpResult>> UnpublishAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        CancellationToken ct) =>
        TransitionAsync(id, ActivityState.Draft, http, db, access, accessAccessor, userAccessor, protection, ct);

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
        ActivityState target,
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
        trip.CavingGroupId = request.CavingGroupId;
        trip.Visibility = request.Visibility;
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

    // Reconcile with a diff (add/remove only what changed) rather than delete-all +
    // recreate-all: a full recreate logs a "created" event for every unchanged child on every
    // save, so the diff keeps the timeline honest. Also preserves caves the caller could not
    // see: those were redacted out of the list they edited, so treating the submitted list as
    // the whole truth would silently drop them.
    //
    // That last guard is why a list is only reconciled when one is actually supplied. Naming a
    // cave one at a time — which is how it is done now — cannot express "forget everything not
    // in this list", so the hazard simply does not arise there; it arises only here, where an
    // absence has to be read as an instruction, and here it is guarded.
    //
    // Reads over every role, writes under one. A cave the trip already names — whatever it did
    // there — is left exactly as it is rather than named a second time, and a cave dropped from
    // the list is unnamed only from the role this path writes: a list with no roles in it is not
    // an instruction to forget that the trip surveyed somewhere.
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
        var redacted = await protection.RedactedLinkTargetIdsAsync(ctx, named, ct);
        var desired = requestedCaveIds.Concat(named.Where(redacted.Contains)).ToHashSet();

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
        // be used to probe for caves.
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
    private static async Task<List<TripLogDto>> MapWithChildrenAsync(
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

        // Exact trip geometry + protected-cave link would disclose the cave; hide those links.
        var redacted = await protection.RedactedLinkTargetIdsAsync(
            ctx, [.. caveLinks.Select(x => x.FeatureId)], ct);

        // Which of these trips this caller may change, decided for the whole page at once so a
        // longer listing does not cost more round trips. It answers one question here: who is
        // told what went wrong, as against who is told that something did.
        var writable = await ProtectedWrites.WritableAsync(access, ctx, trips, ct);

        return [.. trips.Select(trip => MapOne(trip, writable.Contains(trip.Id)))];

        TripLogDto MapOne(TripLog trip, bool mayWrite)
        {
            var (safety, safetyVersion) = TripDisclosure.Safety(trip, mayWrite);
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
                [.. caveLinks.Where(x => x.TripId == trip.Id && !redacted.Contains(x.FeatureId)).Select(x => x.FeatureId)],
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
                safetyVersion);
        }
    }
}
