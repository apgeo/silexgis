// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>
/// Who a trip concerns: whoever is named on it and whoever was asked about it, unioned.
/// </summary>
/// <remarks>
/// <para>
/// Both halves, because a plan reaches people twice over — somebody asked and not yet written in
/// cares that the date moved exactly as much as somebody already on the roster, and once the
/// answers are written into the roster the same person is on both lists. Whether each of them may
/// actually read the trip is a separate question decided afterwards, once, for every caller alike.
/// </para>
/// <para>
/// It lives here rather than beside the routes that edit a trip because the same set is needed by
/// something that is not a request at all: the scheduled pass that notices a party is overdue has
/// nobody's session to work from and must reach exactly the same people. Two copies of "who this
/// trip concerns" would be free to disagree, and the disagreement that mattered would be the one
/// where the alarm reached fewer people than the edit notice did.
/// </para>
/// <para>
/// Somebody on a trip's list with no account is not here, because there is nowhere to write to
/// them. The trip's own list is where whoever is organising it sees they will have to be reached
/// another way.
/// </para>
/// </remarks>
public static class TripAudience
{
    /// <summary>The accounts a trip concerns, with no duplicates removed — the caller narrows.</summary>
    public static async Task<List<Guid>> PeopleConcernedAsync(
        SilexGisDbContext db, Guid tripId, CancellationToken ct = default)
    {
        var named = await (
            from participant in db.TripLogParticipants.AsNoTracking()
            join caver in db.Cavers.AsNoTracking() on participant.CaverId equals caver.Id
            where participant.TripLogId == tripId && caver.UserId != null
            select caver.UserId!.Value)
            .ToListAsync(ct);

        var asked = await (
            from invitation in db.TripInvitations.AsNoTracking()
            join caver in db.Cavers.AsNoTracking() on invitation.CaverId equals caver.Id
            where invitation.TripLogId == tripId && caver.UserId != null
            select caver.UserId!.Value)
            .ToListAsync(ct);

        return [.. named, .. asked];
    }

    /// <summary>
    /// The trips one account is on, as a query rather than an answer, so a listing can narrow by
    /// it without first knowing which trips to ask about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Whoever is named on a trip and whoever was asked about it and has not said no, joined
    /// through the roster entry the account owns. An account with no roster entry matches
    /// nothing, which is the right answer rather than an error — most people on a roster have no
    /// account, and an account not yet on one is on no trips.
    /// </para>
    /// <para>
    /// The declined answer is why this is not the same set as the two above, and the difference
    /// is deliberate rather than an oversight. Who a trip <em>concerns</em> has to include the
    /// person who turned it down: they were asked, so they are owed the news that the date moved
    /// or that the party is overdue. What somebody is <em>going on</em> must not include it — a
    /// diary five rows long fills up with the five weekends they declined and pushes the trip
    /// they are actually going on off the end, and nothing on the row carries their own answer,
    /// so a declined trip reads exactly like one they are attending. Somebody who said no and was
    /// nevertheless written onto the roster still matches through the named half, which is
    /// correct: being put on the party is the organiser overruling the answer.
    /// </para>
    /// <para>
    /// It takes the account and nothing else on purpose. A version of this that took a person as
    /// an argument would answer "where has this named person been" out of trips the asker may
    /// never open, which is a question about a person rather than about the records, and the size
    /// of the answer gives it away even when no row is returned. Because the only account it can
    /// be asked about is the one making the request, there is no such question to ask.
    /// </para>
    /// </remarks>
    public static IQueryable<Guid> TripIdsTheAccountIsOn(SilexGisDbContext db, Guid userId)
    {
        var named =
            from participant in db.TripLogParticipants.AsNoTracking()
            join caver in db.Cavers.AsNoTracking() on participant.CaverId equals caver.Id
            where caver.UserId == userId
            select participant.TripLogId;

        // Answers are held about club events as well as about trips, and only the trip ones are
        // trips somebody is on — a row whose subject is an event names no trip at all.
        var asked =
            from invitation in db.TripInvitations.AsNoTracking()
            join caver in db.Cavers.AsNoTracking() on invitation.CaverId equals caver.Id
            where caver.UserId == userId
                && invitation.Response != TripInvitationResponse.No
                && invitation.TripLogId != null
            select invitation.TripLogId!.Value;

        // Union rather than Concat: somebody asked and then written onto the roster holds a row
        // in both halves, and a person carrying two jobs on one trip holds two roster rows.
        return named.Union(asked);
    }

    /// <summary>
    /// Which of these trips concern one particular account — the same question as above, asked
    /// the other way round because a page asks it about its reader rather than about a trip.
    /// </summary>
    /// <remarks>
    /// One definition, asked twice: what a surface offers somebody and what a route lets them do
    /// have to be the same rule, or a page grows a button that answers 403 — or worse, hides one
    /// from a person entitled to press it. The second failure is the one that matters here,
    /// because the button says a party is safe.
    /// </remarks>
    public static async Task<HashSet<Guid>> ConcerningAsync(
        SilexGisDbContext db, IReadOnlyList<Guid> tripIds, Guid userId, CancellationToken ct = default)
    {
        var named = await (
            from participant in db.TripLogParticipants.AsNoTracking()
            join caver in db.Cavers.AsNoTracking() on participant.CaverId equals caver.Id
            where tripIds.Contains(participant.TripLogId) && caver.UserId == userId
            select participant.TripLogId)
            .ToListAsync(ct);

        var asked = await (
            from invitation in db.TripInvitations.AsNoTracking()
            join caver in db.Cavers.AsNoTracking() on invitation.CaverId equals caver.Id
            where invitation.TripLogId != null
                && tripIds.Contains(invitation.TripLogId!.Value)
                && caver.UserId == userId
            select invitation.TripLogId!.Value)
            .ToListAsync(ct);

        return [.. named, .. asked];
    }
}
