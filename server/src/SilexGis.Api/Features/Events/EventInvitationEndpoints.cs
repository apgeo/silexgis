// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Events;

/// <summary>
/// Who was asked to an event, and what they have said.
/// </summary>
/// <remarks>
/// <para>
/// The same answering mechanism the trip's list uses, over the same rows, about the other kind of
/// subject. One table holds both, so an answer means the same thing here as it does there and
/// there is one place where what an answer is is decided.
/// </para>
/// <para>
/// Every route here is a sub-resource of one event, and the event is what governs them: seeing the
/// list takes the right to read the event and nothing besides. A caller who may not read the event
/// is answered as though it did not exist, on every route including the writes, so that a refusal
/// never confirms an event is there — and never confirms which people are being considered for it,
/// which is the same disclosure one row at a time.
/// </para>
/// <para>
/// There is no promotion here and no roster to promote into. A trip keeps two records — who meant
/// to come and who was on it — and one deliberate act turns the first into the second. An event
/// keeps one: its answers are the whole record of who was coming, so there is nothing to write
/// them into and nothing that could disagree with them.
/// </para>
/// </remarks>
public static class EventInvitationEndpoints
{
    /// <summary>A person who has no entry in the club's directory.</summary>
    public const string CaverUnknownCode = "event_invitation.caver_unknown";

    /// <summary>The event may be read, but this is somebody else's answer to give.</summary>
    public const string AnswerForbiddenCode = "event_invitation.answer_forbidden";

    /// <summary>Editing who is on the list is running the event.</summary>
    public const string InviteForbiddenCode = "event_invitation.invite_forbidden";

    /// <summary>Taking somebody off the list is running the event.</summary>
    public const string RemoveForbiddenCode = "event_invitation.remove_forbidden";

    /// <summary>
    /// A person in the directory this event's list says nothing about. Distinct from a person who
    /// is not in the directory at all: a client told the first can offer to put them on the list,
    /// and a client told the second would send somebody to add a person who is already there.
    /// </summary>
    public const string NotOnListCode = "event_invitation.not_on_list";

    public const string NoteInvalidCode = "event_invitation.note_invalid";

    /// <summary>A body carrying no answer. The note was not what was wrong with it.</summary>
    public const string ResponseMissingCode = "event_invitation.response_missing";

    /// <summary>
    /// Two requests about the same person on the same event landing together, one of which the
    /// single-answer-per-person index refused.
    /// </summary>
    public const string ConcurrentAnswerCode = "event_invitation.concurrent_answer";

    /// <summary>Choosing who is coming is running the event.</summary>
    public const string SelectForbiddenCode = "event_invitation.select_forbidden";

    /// <summary>Only somebody who has said they are coming can be picked to come.</summary>
    public const string SelectNotAttendingCode = "event_invitation.select_not_attending";

    /// <summary>
    /// The event is a kind nobody is asked to. A deadline is a date rather than a gathering: there
    /// is nobody to come to it, so every route in this group refuses.
    /// <para>
    /// Its own code, and neither of the two answers that would have been easier. A 404 would say
    /// the event is not there, one line after the same handler decided the caller may read it, and
    /// a client cannot tell that apart from a wrong id. A bare 400 carries no code at all, so a
    /// client cannot tell it apart from any other refusal and would have to guess from prose.
    /// </para>
    /// </summary>
    public const string KindTakesNoResponsesCode = "event_invitation.kind_takes_no_responses";

    public static RouteGroupBuilder MapEventInvitationEndpoints(this RouteGroupBuilder api)
    {
        // Mounted under the event because the event is what an answer is about and what decides
        // who may see it. There is no route addressed by invitation id alone: that would be a
        // second door onto the same rows with its own idea of who may open it — and the rows here
        // are the trip's rows, so a door like that would open both.
        var invitations = api.MapGroup("/events/{eventId:guid}/invitations").WithTags("Events");

        invitations.MapGet("/", ListAsync)
            .WithSummary(
                "Everybody on this event's list with what they have said, in the order they "
                + "answered in. Takes the right to read the event and nothing besides.");
        invitations.MapPost("/", InviteAsync).WithValidation<EventInvitationCreateRequest>()
            .WithSummary(
                "Puts somebody on this event's list (Write permission on the event). Asking again "
                + "somebody already on it changes nothing about what they have said.");
        invitations.MapPut("/{caverId:guid}/response", RespondAsync)
            .WithValidation<EventInvitationResponseRequest>()
            .WithSummary(
                "Records what one person says about coming. Anyone who may read the event answers "
                + "for themselves; answering for somebody else takes the right to write the event. "
                + "Somebody who was never asked may answer, which puts them on the list.");
        invitations.MapPut("/{caverId:guid}/selection", SelectAsync)
            .WithValidation<EventInvitationSelectionRequest>()
            .WithSummary(
                "Picks one person out for the event, or puts them back in the order (Write "
                + "permission on the event). A picked person is in wherever they stand in the "
                + "order people answered in, and the order itself is unchanged.");
        invitations.MapDelete("/{caverId:guid}", RemoveAsync)
            .WithSummary(
                "Takes somebody off this event's list entirely, answer and all (Write permission "
                + "on the event). For a person put on it by mistake — recording a \"no\" in their "
                + "name instead would be writing down words they never said.");

        return api;
    }

    /// <summary>
    /// The whole list, ordered the way the event fills up.
    /// </summary>
    /// <remarks>
    /// Oldest answer first, with those who have not answered after them, and by key within a
    /// coincidence of timing. That is the same order the trip's list is in, worked out by the same
    /// expression over the same rows — so the same answers given in the same sequence number the
    /// same people the same way whichever kind of thing they are about.
    /// </remarks>
    private static async Task<Results<Ok<EventInvitationListDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        ListAsync(
            Guid eventId,
            SilexGisDbContext db,
            IAccessService access,
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

        if (await SubjectAsync(db, access, ctx, eventId, ct) is not { } row)
        {
            return NoSuchEvent();
        }

        if (RefuseUnlessAsked(row) is { } refusal)
        {
            return refusal;
        }

        // Read unordered and ordered afterwards by the one expression that defines the sign-up
        // order, which is the same expression that decides who holds a place and who is waiting.
        // Two copies of that rule — one to sort the rows and one to number them — would compile,
        // pass every test, and render place 3 above place 2 the day either was changed alone.
        var rows = TripAttendance.InSignUpOrder(
            await db.TripInvitations.AsNoTracking()
                .Where(x => x.EventId == eventId)
                .ToListAsync(ct));

        var mayWrite = (await access.DecideAsync(ctx, AccessAction.Write, row, ct)).Allowed;
        var caverIds = rows.Select(x => x.CaverId).Distinct().ToList();

        // Resolved rather than joined: what a person may be shown as is a rule with one home, and
        // an account's own label wins there so nobody appears twice under two names.
        var labels = await CaverDirectory.ResolveLabelsAsync(db, user, caverIds, ct);
        var accounts = await AccountsOfAsync(db, caverIds, ct);

        // Worked out here from the rows that were just read, against the event's own limit. The
        // list is unpaged for exactly this reason: a place in the order computed over part of the
        // rows would not be a place in the order at all.
        var places = TripAttendance.Rank(rows, row.MaxParticipants);
        var attending = places.Values.Count(x => x.Attending);

        return TypedResults.Ok(new EventInvitationListDto
        {
            EventId = eventId,
            MaxParticipants = row.MaxParticipants,
            AttendingCount = attending,
            WaitingCount = places.Count - attending,
            Invitations = [.. rows.Select(x => Map(x, labels, accounts, ctx, user, mayWrite, places))],
        });
    }

    /// <summary>Puts somebody on the list.</summary>
    /// <remarks>
    /// Asking again somebody already on the list is not an error and undoes nothing: the stamps
    /// say when they were first asked and by whom, and their answer — which they may already have
    /// given without being asked at all — is left exactly as it stands.
    /// </remarks>
    private static async Task<Results<
        Created<EventInvitationDto>, Ok<EventInvitationDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        InviteAsync(
            Guid eventId,
            EventInvitationCreateRequest request,
            SilexGisDbContext db,
            IAccessService access,
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

        // The event is asked about before anything else, so somebody with no right to it never
        // learns from a refusal whether it exists or who is being considered for it.
        if (await SubjectAsync(db, access, ctx, eventId, ct) is not { } row)
        {
            return NoSuchEvent();
        }

        if (RefuseUnlessAsked(row) is { } refusal)
        {
            return refusal;
        }

        var mayWrite = (await access.DecideAsync(ctx, AccessAction.Write, row, ct)).Allowed;
        if (!TripInvitationRules.MayInvite(user.UserId, mayWrite, ctx.IsFullAdmin))
        {
            return ApiProblems.Forbidden(InviteForbiddenCode);
        }

        var caver = await CaverAsync(db, request.CaverId, ct);
        if (caver is null)
        {
            return ApiProblems.BadRequest(CaverUnknownCode, "That person has no entry in the directory.");
        }

        var existing = await db.TripInvitations
            .FirstOrDefaultAsync(x => x.EventId == eventId && x.CaverId == request.CaverId, ct);

        var invitation = existing ?? new TripInvitation { EventId = eventId, CaverId = request.CaverId };

        // First asked wins on both stamps: asking a second time is a reminder, and a record of
        // when somebody was asked that moved every time they were nudged would answer the wrong
        // question about how long they have had to reply.
        invitation.InvitedAt ??= DateTimeOffset.UtcNow;
        invitation.InvitedByUserId ??= user.UserId;

        if (existing is null)
        {
            db.TripInvitations.Add(invitation);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (IsSameAnswerRace(e))
        {
            return SameAnswerRaceRefusal();
        }

        var dto = await MapOneAsync(db, ctx, user, row, invitation, caver.UserId, mayWrite, ct);
        return existing is null
            ? TypedResults.Created(
                "/api/v1/events/" + eventId + "/invitations/" + invitation.CaverId + "/response", dto)
            : TypedResults.Ok(dto);
    }

    /// <summary>Records what one person says.</summary>
    /// <remarks>
    /// <para>
    /// The row is written where none exists, because somebody who was never asked still answers —
    /// a member who sees an evening their club is running and says they are coming. That is the
    /// whole reason the answer and the invitation are one row: were the invitation the parent row,
    /// those answers would have nothing to hang on.
    /// </para>
    /// <para>
    /// A limit the event carries is not consulted here and never refuses this write. Refusing the
    /// ninth yes on an evening with room for eight would destroy exactly the record a waiting list
    /// exists to keep — who else wanted to come, and in what order they said so.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<EventInvitationDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        RespondAsync(
            Guid eventId,
            Guid caverId,
            EventInvitationResponseRequest request,
            SilexGisDbContext db,
            IAccessService access,
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

        if (await SubjectAsync(db, access, ctx, eventId, ct) is not { } row)
        {
            return NoSuchEvent();
        }

        if (RefuseUnlessAsked(row) is { } refusal)
        {
            return refusal;
        }

        var caver = await CaverAsync(db, caverId, ct);
        if (caver is null)
        {
            return ApiProblems.BadRequest(CaverUnknownCode, "That person has no entry in the directory.");
        }

        var mayWrite = (await access.DecideAsync(ctx, AccessAction.Write, row, ct)).Allowed;
        if (!TripInvitationRules.MayAnswerFor(user.UserId, caver.UserId, mayWrite, ctx.IsFullAdmin))
        {
            return ApiProblems.Forbidden(AnswerForbiddenCode);
        }

        var noteProblems = TripInvitationRules.ValidateNote(request.Note);
        if (noteProblems.Count > 0)
        {
            return ApiProblems.BadRequest(NoteInvalidCode, string.Join("; ", noteProblems));
        }

        // The shape check has already refused a body that says nothing; re-read here so the
        // vocabulary and the handler cannot ever disagree about what an empty answer is.
        if (request.Response is not { } response)
        {
            return ApiProblems.BadRequest(ResponseMissingCode, "An answer says yes, no or maybe.");
        }

        var invitation = await db.TripInvitations
            .FirstOrDefaultAsync(x => x.EventId == eventId && x.CaverId == caverId, ct);
        if (invitation is null)
        {
            invitation = new TripInvitation { EventId = eventId, CaverId = caverId };
            db.TripInvitations.Add(invitation);
        }

        invitation.Response = response;
        invitation.Note = TripInvitationRules.Normalize(request.Note);

        // Restamped by every change of mind rather than kept from when the row was made: somebody
        // who said maybe in March and yes in June joined the queue in June, and inheriting the
        // older stamp would put them in front of everybody who said yes in between.
        invitation.RespondedAt = DateTimeOffset.UtcNow;

        // The subject is the person the answer is about; this is whoever wrote it down. "Ana said
        // yes" and "the secretary wrote down that Ana said yes" are not the same fact.
        invitation.RespondedByUserId = user.UserId;

        // A pick does not survive the answer it was made about. Only somebody who has said they
        // are coming may be picked, and the row must not be able to reach by one route a state the
        // other refuses to write: a stamp left standing through a withdrawal would put the person
        // back in, ahead of everybody waiting and past the limit, the moment they said yes again —
        // a choice nobody made after they took their name back.
        if (response != TripInvitationResponse.Yes)
        {
            invitation.SelectedAt = null;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (IsSameAnswerRace(e))
        {
            return SameAnswerRaceRefusal();
        }

        return TypedResults.Ok(await MapOneAsync(db, ctx, user, row, invitation, caver.UserId, mayWrite, ct));
    }

    /// <summary>Picks one person out for the event, or puts them back in the order.</summary>
    /// <remarks>
    /// <para>
    /// The pick sits beside the order people answered in, not instead of it. Everybody's place in
    /// that order is unchanged by it and stays visible; what changes is that a picked person is in
    /// wherever they stand in it, and the places the picks take come out of the room first.
    /// Whoever runs an event has reasons the order cannot express — the one member with the key,
    /// the two who can drive — and a queue that could not be overridden would be answering a
    /// question nobody asked it.
    /// </para>
    /// <para>
    /// Only somebody who has said they are coming may be picked. Picking a person who declined, or
    /// who has not answered, would put somebody down for an evening against what they themselves
    /// said about it, and the row would then hold two contradictory facts about the same person
    /// with nothing to say which was meant.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<EventInvitationDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        SelectAsync(
            Guid eventId,
            Guid caverId,
            EventInvitationSelectionRequest request,
            SilexGisDbContext db,
            IAccessService access,
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

        if (await SubjectAsync(db, access, ctx, eventId, ct) is not { } row)
        {
            return NoSuchEvent();
        }

        if (RefuseUnlessAsked(row) is { } refusal)
        {
            return refusal;
        }

        var mayWrite = (await access.DecideAsync(ctx, AccessAction.Write, row, ct)).Allowed;

        // Choosing who comes is running the event, and unlike answering there is no branch for
        // doing it to yourself: putting your own name in front of everybody who signed up before
        // you is the one thing the order exists to prevent.
        if (!TripInvitationRules.MayInvite(user.UserId, mayWrite, ctx.IsFullAdmin))
        {
            return ApiProblems.Forbidden(SelectForbiddenCode);
        }

        var invitation = await db.TripInvitations
            .FirstOrDefaultAsync(x => x.EventId == eventId && x.CaverId == caverId, ct);
        if (invitation is null)
        {
            return ApiProblems.BadRequest(
                NotOnListCode, "That person has said nothing about this event.");
        }

        var selected = request.Selected == true;
        if (selected && invitation.Response != TripInvitationResponse.Yes)
        {
            return ApiProblems.BadRequest(
                SelectNotAttendingCode, "Only somebody who has said they are coming can be picked.");
        }

        // Stamped when the choice was made and cleared when it is taken back, rather than kept as
        // a flag beside a date: one column cannot disagree with itself about whether somebody was
        // picked and when.
        invitation.SelectedAt = selected ? DateTimeOffset.UtcNow : null;
        await db.SaveChangesAsync(ct);

        var account = await db.Cavers.AsNoTracking()
            .Where(c => c.Id == caverId)
            .Select(c => c.UserId)
            .FirstOrDefaultAsync(ct);

        return TypedResults.Ok(await MapOneAsync(db, ctx, user, row, invitation, account, mayWrite, ct));
    }

    /// <summary>Takes somebody off the list entirely.</summary>
    /// <remarks>
    /// <para>
    /// Editing who is on the list is running the event, so this is the event's write right and the
    /// same right that puts somebody on it. There is no self branch: taking your own name off is
    /// answering no, which has its own route and leaves a record of what you said.
    /// </para>
    /// <para>
    /// It exists because the alternative is worse. A list somebody can only be added to leaves an
    /// organiser who picked the wrong name out of a directory of people who share a first name
    /// with one way to keep them off it — writing a "no" in their name — and a refusal nobody gave
    /// is exactly the falsification that keeping the subject and the writer in separate columns
    /// exists to prevent.
    /// </para>
    /// <para>
    /// The whole row goes, answer and all, and the deletion lands on the event's own trail so the
    /// answer that was there is still readable in the history. Nothing is written in its place: a
    /// tombstone row would keep the person on the list, which is the one thing this undoes.
    /// </para>
    /// </remarks>
    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>>
        RemoveAsync(
            Guid eventId,
            Guid caverId,
            SilexGisDbContext db,
            IAccessService access,
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

        if (await SubjectAsync(db, access, ctx, eventId, ct) is not { } row)
        {
            return NoSuchEvent();
        }

        if (RefuseUnlessAsked(row) is { } refusal)
        {
            return refusal;
        }

        var mayWrite = (await access.DecideAsync(ctx, AccessAction.Write, row, ct)).Allowed;
        if (!TripInvitationRules.MayInvite(user.UserId, mayWrite, ctx.IsFullAdmin))
        {
            return ApiProblems.Forbidden(RemoveForbiddenCode);
        }

        var invitation = await db.TripInvitations
            .FirstOrDefaultAsync(x => x.EventId == eventId && x.CaverId == caverId, ct);
        if (invitation is null)
        {
            return ApiProblems.NotFound(NotOnListCode);
        }

        // Removed through the change tracker rather than deleted in one statement, so what was
        // taken off the list and by whom reaches the event's history.
        db.TripInvitations.Remove(invitation);
        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }

    /// <summary>
    /// The event this group is about, or null when this caller may not read it or it is not there.
    /// Read through the event's own helper so that a sub-resource cannot become a way of learning
    /// that an event exists without being allowed to open it.
    /// </summary>
    private static Task<Event?> SubjectAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, Guid eventId, CancellationToken ct) =>
        EventEndpoints.ReadableEventAsync(db, access, ctx, eventId, ct);

    /// <summary>Unreachable and absent alike, in the words every other door into an event uses.</summary>
    private static ProblemHttpResult NoSuchEvent() => ApiProblems.NotFound(EventEndpoints.NotFoundCode);

    /// <summary>
    /// The refusal every route in this group makes when the event is a kind nobody is asked to,
    /// and null when it is one they are. Asked on the reads as well as the writes: a deadline
    /// answering an empty list would read as "nobody has said anything yet" rather than "there is
    /// nothing here to answer", and a surface would draw a sign-up sheet on a date.
    /// <para>
    /// Which kinds are asked is not decided here. It is a property of the kind, answered in the
    /// one place that knows, so a kind added to the vocabulary and not named there accepts nothing
    /// until somebody decides that it should.
    /// </para>
    /// </summary>
    private static ProblemHttpResult? RefuseUnlessAsked(Event row) =>
        EventKinds.AcceptsResponses(row.Kind)
            ? null
            : ApiProblems.Conflict(
                KindTakesNoResponsesCode,
                "Nobody is asked to an event of this kind, so there is nothing to answer.");

    /// <summary>The index that holds one person to one standing answer on one event.</summary>
    private const string InvitationUniqueIndex = "ix_trip_invitations_event_id_caver_id";

    /// <summary>
    /// Whether a failed write is a second request about the same person on the same event arriving
    /// at the same moment — two taps on "yes", or a secretary writing down an answer in the instant
    /// its owner gives it. Both handlers look for an existing row and add one when they find none,
    /// so both can look, find nothing, and try to add; the index refuses the second, and it is
    /// refused as a conflict with a name rather than escaping as an unhandled fault.
    /// <para>
    /// The event's own index by name, and not the trip's. One answer table now carries a unique
    /// index per subject, and matching the wrong name would let a double-tap on an event escape
    /// this handler as a fault with no code on it.
    /// </para>
    /// </summary>
    private static bool IsSameAnswerRace(Exception e) =>
        e is DbUpdateException
        && e.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: InvitationUniqueIndex,
        };

    private static ProblemHttpResult SameAnswerRaceRefusal() =>
        ApiProblems.Conflict(
            ConcurrentAnswerCode,
            "Somebody wrote about this person on this event at the same moment. Read the list "
            + "again and repeat the change if it is still wanted.");

    private static Task<CaverAccount?> CaverAsync(SilexGisDbContext db, Guid caverId, CancellationToken ct) =>
        db.Cavers.AsNoTracking()
            .Where(c => c.Id == caverId)
            .Select(c => new CaverAccount(c.Id, c.UserId))
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// The account behind each of these people, where they have one. There is no path from a
    /// roster entry to the authorization context — most people on a roster never sign in — so
    /// answering "is this caller the person this row is about" means reading the link itself.
    /// </summary>
    private static async Task<Dictionary<Guid, Guid?>> AccountsOfAsync(
        SilexGisDbContext db, IReadOnlyCollection<Guid> caverIds, CancellationToken ct)
    {
        if (caverIds.Count == 0)
        {
            return [];
        }

        var rows = await db.Cavers.AsNoTracking()
            .Where(c => caverIds.Contains(c.Id))
            .Select(c => new { c.Id, c.UserId })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.Id, r => r.UserId);
    }

    /// <summary>
    /// One row as it is answered back after being written. The whole event's answers are re-read to
    /// build it, because the one fact this row carries that is not its own — whether the person is
    /// in or waiting for a place — is a property of every answer on the event taken together, and
    /// telling somebody they are in when the yes before theirs took the last place would be worse
    /// than telling them nothing.
    /// </summary>
    private static async Task<EventInvitationDto> MapOneAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        UserContext user,
        Event row,
        TripInvitation invitation,
        Guid? caverAccountId,
        bool mayWriteEvent,
        CancellationToken ct)
    {
        var labels = await CaverDirectory.ResolveLabelsAsync(db, user, [invitation.CaverId], ct);
        var rows = await db.TripInvitations.AsNoTracking()
            .Where(x => x.EventId == row.Id)
            .ToListAsync(ct);

        return Map(
            invitation,
            labels,
            new Dictionary<Guid, Guid?> { [invitation.CaverId] = caverAccountId },
            ctx,
            user,
            mayWriteEvent,
            TripAttendance.Rank(rows, row.MaxParticipants));
    }

    private static EventInvitationDto Map(
        TripInvitation invitation,
        IReadOnlyDictionary<Guid, string> labels,
        IReadOnlyDictionary<Guid, Guid?> accounts,
        AccessContext ctx,
        UserContext user,
        bool mayWriteEvent,
        IReadOnlyDictionary<long, TripAttendancePlace> places)
    {
        var placed = places.TryGetValue(invitation.Id, out var place);
        return new EventInvitationDto
        {
            Id = invitation.Id,
            // Every row reaching here was selected by the event it answers about, so its subject
            // is that event; a row whose subject is a trip never comes through this list.
            EventId = invitation.EventId!.Value,
            CaverId = invitation.CaverId,
            CaverName = labels.GetValueOrDefault(invitation.CaverId, string.Empty),
            Response = invitation.Response,
            InvitedByUserId = invitation.InvitedByUserId,
            InvitedAt = invitation.InvitedAt,
            RespondedAt = invitation.RespondedAt,
            RespondedByUserId = invitation.RespondedByUserId,
            SelectedAt = invitation.SelectedAt,
            Note = invitation.Note,
            // Absent, and not zero, for anybody who has not said yes: no place is a different fact
            // from the place before the first, and a nought here would sort ahead of everybody on a
            // surface that ordered by it.
            Place = placed ? place.Place : null,
            Attending = placed && place.Attending,
            MayAnswer = TripInvitationRules.MayAnswerFor(
                user.UserId,
                accounts.GetValueOrDefault(invitation.CaverId),
                mayWriteEvent,
                ctx.IsFullAdmin),
            CreatedAt = invitation.CreatedAt,
            UpdatedAt = invitation.UpdatedAt,
        };
    }

    /// <summary>A person in the directory and the account behind them, where they have one.</summary>
    private sealed record CaverAccount(Guid Id, Guid? UserId);
}
