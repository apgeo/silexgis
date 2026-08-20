// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>
/// Who was asked on a trip, and what they have said.
/// </summary>
/// <remarks>
/// <para>
/// Every route here is a sub-resource of one trip, and the trip is what governs them: seeing the
/// list takes the right to read the trip and nothing besides. That is deliberately the trip's own
/// rule and not the wider camp's, which asks for the right to read people as well — a trip is an
/// afternoon and its list of people is already served on the trip's own reading, so asking for a
/// second right here would withhold from a reader what the same reader is shown one route away.
/// </para>
/// <para>
/// A caller who may not read the trip is answered as though it did not exist, on every route
/// including the writes, so that a refusal never confirms a trip is there — and never confirms
/// which people are being considered for it, which is the same disclosure one row at a time.
/// </para>
/// <para>
/// Nothing here grants anything. Being asked on a trip that names a cave does not make that cave
/// readable; the list says who is coming and no more.
/// </para>
/// </remarks>
public static class TripInvitationEndpoints
{
    /// <summary>The trip, as the trip's own routes answer it — unreachable and absent read alike.</summary>
    public const string TripNotFoundCode = "trip_log.not_found";

    /// <summary>A person who has no entry in the club's directory.</summary>
    public const string CaverUnknownCode = "trip_invitation.caver_unknown";

    /// <summary>The plan may be read, but this is somebody else's answer to give.</summary>
    public const string AnswerForbiddenCode = "trip_invitation.answer_forbidden";

    /// <summary>Editing who is on the list is running the trip.</summary>
    public const string InviteForbiddenCode = "trip_invitation.invite_forbidden";

    /// <summary>Taking somebody off the list is running the trip.</summary>
    public const string RemoveForbiddenCode = "trip_invitation.remove_forbidden";

    /// <summary>
    /// A person in the directory this trip's list says nothing about. Distinct from a person who
    /// is not in the directory at all: a client told the first can offer to put them on the list,
    /// and a client told the second would send somebody to add a person who is already there.
    /// </summary>
    public const string NotOnListCode = "trip_invitation.not_on_list";

    public const string NoteInvalidCode = "trip_invitation.note_invalid";

    /// <summary>A body carrying no answer. The note was not what was wrong with it.</summary>
    public const string ResponseMissingCode = "trip_invitation.response_missing";

    /// <summary>
    /// Two requests about the same person on the same trip landing together, one of which the
    /// single-answer-per-person index refused.
    /// </summary>
    public const string ConcurrentAnswerCode = "trip_invitation.concurrent_answer";

    /// <summary>Choosing who is on the trip is running it.</summary>
    public const string SelectForbiddenCode = "trip_invitation.select_forbidden";

    /// <summary>Only somebody who has said they are coming can be picked to come.</summary>
    public const string SelectNotAttendingCode = "trip_invitation.select_not_attending";

    /// <summary>Writing the answers into the trip's list of people is running the trip.</summary>
    public const string PromoteForbiddenCode = "trip_invitation.promote_forbidden";

    /// <summary>A trip that has not happened yet has no list of who was on it.</summary>
    public const string PromoteTooEarlyCode = "trip_invitation.promote_too_early";

    public static RouteGroupBuilder MapTripInvitationEndpoints(this RouteGroupBuilder api)
    {
        // Mounted under the trip because the trip is what an answer is about and what decides who
        // may see it. There is no route addressed by invitation id alone: that would be a second
        // door onto the same rows with its own idea of who may open it.
        var invitations = api.MapGroup("/trip-logs/{tripLogId:guid}/invitations").WithTags("TripLogs");

        invitations.MapGet("/", ListAsync)
            .WithSummary(
                "Everybody on this trip's list with what they have said, in the order they "
                + "answered in. Takes the right to read the trip and nothing besides.");
        invitations.MapPost("/", InviteAsync).WithValidation<TripInvitationCreateRequest>()
            .WithSummary(
                "Puts somebody on this trip's list (Write permission on the trip). Asking again "
                + "somebody already on it changes nothing about what they have said.");
        invitations.MapPut("/{caverId:guid}/response", RespondAsync)
            .WithValidation<TripInvitationResponseRequest>()
            .WithSummary(
                "Records what one person says about coming. Anyone who may read the trip answers "
                + "for themselves; answering for somebody else takes the right to write the trip. "
                + "Somebody who was never asked may answer, which puts them on the list.");

        invitations.MapPut("/{caverId:guid}/selection", SelectAsync)
            .WithValidation<TripInvitationSelectionRequest>()
            .WithSummary(
                "Picks one person out for the trip, or puts them back in the order (Write "
                + "permission on the trip). A picked person is on the trip wherever they stand "
                + "in the order people answered in, and the order itself is unchanged.");

        invitations.MapDelete("/{caverId:guid}", RemoveAsync)
            .WithSummary(
                "Takes somebody off this trip's list entirely, answer and all (Write permission "
                + "on the trip). For a person put on it by mistake — recording a \"no\" in their "
                + "name instead would be writing down words they never said.");

        invitations.MapPost("/promote", PromoteAsync)
            .WithSummary(
                "Writes everybody holding a place on the trip into its list of people (Write "
                + "permission on the trip), once the trip has happened. Nobody is told: everybody "
                + "written in was told when they were asked.");

        return api;
    }

    /// <summary>
    /// The whole list, ordered the way the trip fills up.
    /// </summary>
    /// <remarks>
    /// Oldest answer first, with those who have not answered after them, and by key within a
    /// coincidence of timing. That order is not decoration: where a trip has a limit, who is on it
    /// and who is waiting is this order compared against that limit, so it is worked out from the
    /// rows every time rather than written into them — a stored place in a queue is a fact that
    /// starts disagreeing with the answers the moment somebody changes their mind.
    /// </remarks>
    private static async Task<Results<Ok<TripInvitationListDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        ListAsync(
            Guid tripLogId,
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

        var trip = await ReadableTripAsync(db, access, ctx, tripLogId, ct);
        if (trip is null)
        {
            return ApiProblems.NotFound(TripNotFoundCode);
        }

        // Read unordered and ordered afterwards by the one expression that defines the sign-up
        // order, which is the same expression that decides who holds a place and who is waiting.
        // Two copies of that rule — one to sort the rows and one to number them — would compile,
        // pass every test, and render place 3 above place 2 the day either was changed alone.
        var rows = TripAttendance.InSignUpOrder(
            await db.TripInvitations.AsNoTracking()
                .Where(x => x.TripLogId == tripLogId)
                .ToListAsync(ct));

        var mayWrite = (await access.DecideAsync(ctx, AccessAction.Write, trip, ct)).Allowed;
        var caverIds = rows.Select(x => x.CaverId).Distinct().ToList();

        // Resolved rather than joined: what a person may be shown as is a rule with one home, and
        // an account's own label wins there so nobody appears twice under two names.
        var labels = await CaverDirectory.ResolveLabelsAsync(db, user, caverIds, ct);
        var accounts = await AccountsOfAsync(db, caverIds, ct);

        // Worked out here from the rows that were just read, against the trip's own limit. The
        // list is unpaged for exactly this reason: a place in the order computed over part of the
        // rows would not be a place in the order at all.
        var places = TripAttendance.Rank(rows, trip.MaxParticipants);
        var attending = places.Values.Count(x => x.Attending);

        return TypedResults.Ok(new TripInvitationListDto
        {
            TripLogId = tripLogId,
            MaxParticipants = trip.MaxParticipants,
            AttendingCount = attending,
            WaitingCount = places.Count - attending,
            Invitations = [.. rows.Select(row => Map(row, labels, accounts, ctx, user, mayWrite, places))],
        });
    }

    /// <summary>Puts somebody on the list.</summary>
    /// <remarks>
    /// Asking again somebody already on the list is not an error and undoes nothing: the stamps
    /// say when they were first asked and by whom, and their answer — which they may already have
    /// given without being asked at all — is left exactly as it stands.
    /// </remarks>
    private static async Task<Results<
        Created<TripInvitationDto>, Ok<TripInvitationDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        InviteAsync(
            Guid tripLogId,
            TripInvitationCreateRequest request,
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

        // The trip is asked about before anything else, so somebody with no right to it never
        // learns from a refusal whether it exists or who is being considered for it.
        var trip = await ReadableTripAsync(db, access, ctx, tripLogId, ct);
        if (trip is null)
        {
            return ApiProblems.NotFound(TripNotFoundCode);
        }

        var mayWrite = (await access.DecideAsync(ctx, AccessAction.Write, trip, ct)).Allowed;
        if (!TripInvitationRules.MayInvite(user.UserId, mayWrite, ctx.IsFullAdmin))
        {
            return ApiProblems.Forbidden(InviteForbiddenCode);
        }

        var caver = await db.Cavers.AsNoTracking()
            .Where(c => c.Id == request.CaverId)
            .Select(c => new { c.Id, c.UserId })
            .FirstOrDefaultAsync(ct);
        if (caver is null)
        {
            return ApiProblems.BadRequest(CaverUnknownCode, "That person has no entry in the directory.");
        }

        var existing = await db.TripInvitations
            .FirstOrDefaultAsync(x => x.TripLogId == tripLogId && x.CaverId == request.CaverId, ct);

        var invitation = existing ?? new TripInvitation { TripLogId = tripLogId, CaverId = request.CaverId };

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

        var dto = await MapOneAsync(db, ctx, user, trip, invitation, caver.UserId, mayWrite, ct);
        return existing is null
            ? TypedResults.Created(
                "/api/v1/trip-logs/" + tripLogId + "/invitations/" + invitation.CaverId + "/response", dto)
            : TypedResults.Ok(dto);
    }

    /// <summary>Records what one person says.</summary>
    /// <remarks>
    /// <para>
    /// The row is written where none exists, because somebody who was never asked still answers —
    /// a member who sees a trip their club is running and says they are coming. That is the whole
    /// reason the answer and the invitation are one row: were the invitation the parent row, those
    /// answers would have nothing to hang on.
    /// </para>
    /// <para>
    /// A limit the trip carries is not consulted here and never refuses this write. Refusing the
    /// ninth yes on a trip with room for eight would destroy exactly the record a waiting list
    /// exists to keep — who else wanted to come, and in what order they said so.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<TripInvitationDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        RespondAsync(
            Guid tripLogId,
            Guid caverId,
            TripInvitationResponseRequest request,
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

        var trip = await ReadableTripAsync(db, access, ctx, tripLogId, ct);
        if (trip is null)
        {
            return ApiProblems.NotFound(TripNotFoundCode);
        }

        var caver = await db.Cavers.AsNoTracking()
            .Where(c => c.Id == caverId)
            .Select(c => new { c.Id, c.UserId })
            .FirstOrDefaultAsync(ct);
        if (caver is null)
        {
            return ApiProblems.BadRequest(CaverUnknownCode, "That person has no entry in the directory.");
        }

        var mayWrite = (await access.DecideAsync(ctx, AccessAction.Write, trip, ct)).Allowed;
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
            .FirstOrDefaultAsync(x => x.TripLogId == tripLogId && x.CaverId == caverId, ct);
        if (invitation is null)
        {
            invitation = new TripInvitation { TripLogId = tripLogId, CaverId = caverId };
            db.TripInvitations.Add(invitation);
        }

        invitation.Response = response;
        invitation.Note = TripInvitationRules.Normalize(request.Note);

        // Restamped by every change of mind rather than kept from when the row was made: somebody
        // who said maybe in March and yes in June joined the queue in June, and inheriting the
        // older stamp would put them in front of everybody who said yes in between.
        invitation.RespondedAt = DateTimeOffset.UtcNow;

        // The subject is the person the answer is about; this is whoever wrote it down. On a plan
        // that may be read while somebody is looking for a caving party, "Ana said yes" and "the
        // leader wrote down that Ana said yes" are not the same fact.
        invitation.RespondedByUserId = user.UserId;

        // A pick does not survive the answer it was made about. Only somebody who has said they
        // are coming may be picked, and the row must not be able to reach by one route a state
        // the other refuses to write: a stamp left standing through a withdrawal would put the
        // person back on the trip, ahead of everybody waiting and past the limit, the moment they
        // said yes again — a choice nobody made after they took their name back.
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

        return TypedResults.Ok(await MapOneAsync(db, ctx, user, trip, invitation, caver.UserId, mayWrite, ct));
    }

    /// <summary>Picks one person out for the trip, or puts them back in the order.</summary>
    /// <remarks>
    /// <para>
    /// The pick sits beside the order people answered in, not instead of it. Everybody's place in
    /// that order is unchanged by it and stays visible; what changes is that a picked person is on
    /// the trip wherever they stand in it, and the places the picks take come out of the room
    /// first. Whoever runs a trip has reasons the order cannot express — the one member with the
    /// key, the two who can drive — and a queue that could not be overridden would be answering a
    /// question nobody asked it.
    /// </para>
    /// <para>
    /// Only somebody who has said they are coming may be picked. Picking a person who declined, or
    /// who has not answered, would put somebody on a trip against what they themselves said about
    /// it, and the row would then hold two contradictory facts about the same person with nothing
    /// to say which was meant.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<TripInvitationDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        SelectAsync(
            Guid tripLogId,
            Guid caverId,
            TripInvitationSelectionRequest request,
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

        var trip = await ReadableTripAsync(db, access, ctx, tripLogId, ct);
        if (trip is null)
        {
            return ApiProblems.NotFound(TripNotFoundCode);
        }

        var mayWrite = (await access.DecideAsync(ctx, AccessAction.Write, trip, ct)).Allowed;

        // Choosing the team is running the trip, and unlike answering there is no branch for
        // doing it to yourself: putting your own name in front of everybody who signed up before
        // you is the one thing the order exists to prevent.
        if (!TripInvitationRules.MayInvite(user.UserId, mayWrite, ctx.IsFullAdmin))
        {
            return ApiProblems.Forbidden(SelectForbiddenCode);
        }

        var invitation = await db.TripInvitations
            .FirstOrDefaultAsync(x => x.TripLogId == tripLogId && x.CaverId == caverId, ct);
        if (invitation is null)
        {
            return ApiProblems.BadRequest(
                NotOnListCode, "That person has said nothing about this trip.");
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

        return TypedResults.Ok(await MapOneAsync(db, ctx, user, trip, invitation, account, mayWrite, ct));
    }

    /// <summary>Takes somebody off the list entirely.</summary>
    /// <remarks>
    /// <para>
    /// Editing who is on the list is running the trip, so this is the trip's write right and the
    /// same right that puts somebody on it. There is no self branch: taking your own name off is
    /// answering no, which has its own route and leaves a record of what you said.
    /// </para>
    /// <para>
    /// It exists because the alternative is worse. A list somebody can only be added to leaves an
    /// organiser who picked the wrong name out of a directory of people who share a first name
    /// with one way to keep them off the plan — writing a "no" in their name — and a refusal
    /// nobody gave is exactly the falsification that keeping the subject and the writer in
    /// separate columns exists to prevent, on a record that may be read while somebody is looking
    /// for a caving party.
    /// </para>
    /// <para>
    /// The whole row goes, answer and all, and the deletion lands on the trip's own trail so the
    /// answer that was there is still readable in the history. Nothing is written in its place: a
    /// tombstone row would keep the person on the list, which is the one thing this undoes.
    /// </para>
    /// </remarks>
    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>>
        RemoveAsync(
            Guid tripLogId,
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

        var trip = await ReadableTripAsync(db, access, ctx, tripLogId, ct);
        if (trip is null)
        {
            return ApiProblems.NotFound(TripNotFoundCode);
        }

        var mayWrite = (await access.DecideAsync(ctx, AccessAction.Write, trip, ct)).Allowed;
        if (!TripInvitationRules.MayInvite(user.UserId, mayWrite, ctx.IsFullAdmin))
        {
            return ApiProblems.Forbidden(RemoveForbiddenCode);
        }

        var invitation = await db.TripInvitations
            .FirstOrDefaultAsync(x => x.TripLogId == tripLogId && x.CaverId == caverId, ct);
        if (invitation is null)
        {
            return ApiProblems.NotFound(NotOnListCode);
        }

        // Removed through the change tracker rather than deleted in one statement, so what was
        // taken off the list and by whom reaches the trip's history.
        db.TripInvitations.Remove(invitation);
        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }

    /// <summary>Writes the answers into the trip's list of people.</summary>
    /// <remarks>
    /// <para>
    /// Intent and attendance are two records and this is the one deliberate act that turns one
    /// into the other. It is not folded into the move that closes the trip: a list of who was
    /// there that appeared because a state changed would be a write nobody performed, and an
    /// audit trail cannot account for those. It is also simply not the same fact — who said they
    /// were coming is not who turned up, and whoever ran the trip is the one who knows the
    /// difference and is left free to correct the list afterwards.
    /// </para>
    /// <para>
    /// Offered only once the trip has happened, because the list it writes into is the record of
    /// who was on it. Writing that record for an afternoon still ahead would state as fact
    /// something nobody can yet know, and it is the same list every count of attendance, every
    /// tally of hours underground and every person's own history reads.
    /// </para>
    /// <para>
    /// <b>Nobody is told.</b> Naming somebody on a trip through its own write path sends them
    /// word, and it should — being put on a trip is news. This path deliberately does not:
    /// everybody written in here asked to be, was told when they were asked, and answered. A
    /// message saying they have been added to a trip they signed up for weeks ago is noise, and
    /// noise is how people learn to ignore the messages that matter.
    /// </para>
    /// <para>
    /// Running it twice changes nothing the second time. A trip can be reopened and closed again,
    /// so this has to be safe to repeat; and anybody already recorded on the trip in any capacity
    /// is left exactly as they are rather than gaining a second, plainer row beside the one that
    /// says they led it — one person on one trip is one row per job, so a redundant row would be
    /// counted as a separate person by everything that counts rows.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<TripPromotionDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        PromoteAsync(
            Guid tripLogId,
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

        var trip = await ReadableTripAsync(db, access, ctx, tripLogId, ct);
        if (trip is null)
        {
            return ApiProblems.NotFound(TripNotFoundCode);
        }

        var mayWrite = (await access.DecideAsync(ctx, AccessAction.Write, trip, ct)).Allowed;
        if (!TripInvitationRules.MayInvite(user.UserId, mayWrite, ctx.IsFullAdmin))
        {
            return ApiProblems.Forbidden(PromoteForbiddenCode);
        }

        // The two states that mean the afternoon is behind us. Announced is admitted beside
        // finished because a trip reaches it only through being finished, and refusing there
        // would leave whoever wrote the trip up before turning the answers into the list of
        // people with no way back to it — the move out of announced is a move back to the
        // workshop, which is not a thing to make somebody do to record who went.
        if (trip.State is not (ActivityState.Done or ActivityState.Published))
        {
            return ApiProblems.Conflict(
                PromoteTooEarlyCode,
                "A trip's list of people records who was on it, so it is written once the trip has happened.");
        }

        var rows = await db.TripInvitations.AsNoTracking()
            .Where(x => x.TripLogId == tripLogId)
            .ToListAsync(ct);

        // The same reading of the answers the list itself shows, taken here rather than trusted
        // from whatever the caller last saw: who holds a place moves every time somebody answers,
        // and the team written down has to be the one standing at the moment it is written.
        var places = TripAttendance.Rank(rows, trip.MaxParticipants);
        var attending = rows
            .Where(x => places.TryGetValue(x.Id, out var place) && place.Attending)
            .Select(x => x.CaverId)
            .ToList();

        var roles = await TripLogEndpoints.ShippedRosterRolesAsync(db, ct);

        // Everybody already recorded as having been on this trip, whatever they did on it.
        // Proposers are not in this set on purpose: putting a trip forward is not being on it,
        // and a proposer who said yes belongs in the list of people who went like anybody else.
        var named = await db.TripLogParticipants.AsNoTracking()
            .Where(p => p.TripLogId == tripLogId && p.RoleId != roles.Proposer)
            .Select(p => p.CaverId)
            .Distinct()
            .ToListAsync(ct);
        var alreadyNamed = named.ToHashSet();

        var promoted = 0;
        foreach (var caverId in attending.Distinct().Where(id => !alreadyNamed.Contains(id)))
        {
            db.TripLogParticipants.Add(new TripLogParticipant
            {
                TripLogId = tripLogId,
                RoleId = roles.Participant,
                CaverId = caverId,
            });
            promoted++;
        }

        // The act itself, on the trip's trail. The rows it wrote carry their own entries and point
        // back at the trip, but an act that wrote none writes nothing at all otherwise — and
        // "somebody turned the answers into the list of people and it changed nothing" is exactly
        // the line a person reconstructing how a name got onto a trip needs to be able to read.
        db.Set<AuditEntry>().Add(new AuditEntry
        {
            UserId = user.UserId,
            Action = AuditActions.Updated,
            EntityType = nameof(TripLog),
            EntityId = tripLogId.ToString(),
            // Counts of what the act did, and deliberately no "before" beside any of them. These
            // are not fields of the trip that held an earlier value; a prior value written here
            // would be invented, and the history surface reads one as an offer to put the field
            // back — putting "Roster" back to nothing is not a thing anybody can mean.
            Changes = JsonSerializer.Serialize(
                new Dictionary<string, Dictionary<string, object?>>
                {
                    ["Roster"] = new() { ["new"] = "promoted" },
                    ["Attending"] = new() { ["new"] = attending.Count },
                    ["Promoted"] = new() { ["new"] = promoted },
                    ["AlreadyNamed"] = new() { ["new"] = attending.Count - promoted },
                }),
        });

        await db.SaveChangesAsync(ct);

        // The trip's own row is stamped so its version moves. The list of people this just wrote
        // is reconciled whole by the trip's write path — everybody not named in that request is
        // removed — and its precondition is the trip's version. Leaving the version where it was
        // would let somebody who loaded the trip before this ran save a title correction carrying
        // the roster they saw, pass the precondition, and silently delete every row written here.
        await db.TripLogs
            .Where(x => x.Id == tripLogId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow), ct);

        return TypedResults.Ok(new TripPromotionDto
        {
            TripLogId = tripLogId,
            Attending = attending.Count,
            Promoted = promoted,
            AlreadyNamed = attending.Count - promoted,
        });
    }

    /// <summary>
    /// The trip if this caller may read it, and null when they may not or it is not there. One
    /// question, the same one the trip's own surface asks: a list that decided for itself would
    /// become a way of learning that a trip exists without being allowed to open it.
    /// </summary>
    private static async Task<TripLog?> ReadableTripAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, Guid tripLogId, CancellationToken ct)
    {
        var trip = await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tripLogId, ct);
        return trip is not null && (await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed
            ? trip
            : null;
    }

    /// <summary>The index that holds one person to one standing answer on one trip.</summary>
    private const string InvitationUniqueIndex = "ix_trip_invitations_trip_log_id_caver_id";

    /// <summary>
    /// Whether a failed write is a second request about the same person on the same trip arriving
    /// at the same moment — two taps on "yes", or an organiser writing down an answer in the
    /// instant its owner gives it. Both handlers look for an existing row and add one when they
    /// find none, so both can look, find nothing, and try to add; the index refuses the second,
    /// and it is refused as a conflict with a name rather than escaping as an unhandled fault.
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
            "Somebody wrote about this person on this trip at the same moment. Read the list "
            + "again and repeat the change if it is still wanted.");

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
    /// One row as it is answered back after being written. The whole trip's answers are re-read to
    /// build it, because the one fact this row carries that is not its own — whether the person is
    /// on the trip or waiting for a place — is a property of every answer on the trip taken
    /// together, and telling somebody they are in when the yes before theirs took the last place
    /// would be worse than telling them nothing.
    /// </summary>
    private static async Task<TripInvitationDto> MapOneAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        UserContext user,
        TripLog trip,
        TripInvitation invitation,
        Guid? caverAccountId,
        bool mayWriteTrip,
        CancellationToken ct)
    {
        var labels = await CaverDirectory.ResolveLabelsAsync(db, user, [invitation.CaverId], ct);
        var rows = await db.TripInvitations.AsNoTracking()
            .Where(x => x.TripLogId == trip.Id)
            .ToListAsync(ct);

        return Map(
            invitation,
            labels,
            new Dictionary<Guid, Guid?> { [invitation.CaverId] = caverAccountId },
            ctx,
            user,
            mayWriteTrip,
            TripAttendance.Rank(rows, trip.MaxParticipants));
    }

    private static TripInvitationDto Map(
        TripInvitation invitation,
        IReadOnlyDictionary<Guid, string> labels,
        IReadOnlyDictionary<Guid, Guid?> accounts,
        AccessContext ctx,
        UserContext user,
        bool mayWriteTrip,
        IReadOnlyDictionary<long, TripAttendancePlace> places)
    {
        var placed = places.TryGetValue(invitation.Id, out var place);
        return new TripInvitationDto
        {
            Id = invitation.Id,
            TripLogId = invitation.TripLogId,
            CaverId = invitation.CaverId,
            CaverName = labels.GetValueOrDefault(invitation.CaverId, string.Empty),
            Response = invitation.Response,
            InvitedByUserId = invitation.InvitedByUserId,
            InvitedAt = invitation.InvitedAt,
            RespondedAt = invitation.RespondedAt,
            RespondedByUserId = invitation.RespondedByUserId,
            SelectedAt = invitation.SelectedAt,
            Note = invitation.Note,
            // Absent, and not zero, for anybody who has not said yes: no place is a different
            // fact from the place before the first, and a nought here would sort ahead of
            // everybody on a surface that ordered by it.
            Place = placed ? place.Place : null,
            Attending = placed && place.Attending,
            MayAnswer = TripInvitationRules.MayAnswerFor(
                user.UserId,
                accounts.GetValueOrDefault(invitation.CaverId),
                mayWriteTrip,
                ctx.IsFullAdmin),
            CreatedAt = invitation.CreatedAt,
            UpdatedAt = invitation.UpdatedAt,
        };
    }
}
