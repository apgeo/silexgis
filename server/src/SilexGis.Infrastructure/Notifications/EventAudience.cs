// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>
/// Who a club event concerns: everybody who was asked about it.
/// </summary>
/// <remarks>
/// <para>
/// One half where a trip has two, and that is the shape of the thing rather than an omission. A
/// trip is a record of who went, so it carries a roster as well as the answers; an event carries
/// no roster at all — who is coming and who is waiting is worked out from the answers whenever it
/// is asked. So the answers <i>are</i> the audience, and there is no second list to union with.
/// </para>
/// <para>
/// Whoever said no is still here. They were asked, so they are owed the news that the evening is
/// nearly here — the same reading a trip takes, and deliberately not the same set as the events
/// somebody is going to.
/// </para>
/// <para>
/// Somebody in the directory with no account is not here, because there is nowhere to write to
/// them. Whether each of the rest may actually read the event is a separate question, decided
/// afterwards and once, by the rule every producer shares.
/// </para>
/// </remarks>
public static class EventAudience
{
    /// <summary>The accounts an event concerns, with no duplicates removed — the caller narrows.</summary>
    public static async Task<List<Guid>> PeopleConcernedAsync(
        SilexGisDbContext db, Guid eventId, CancellationToken ct = default) =>
        await (
            // Answers about events and answers about trips are rows in one table, each naming
            // exactly one of the two; a row whose subject is a trip names no event at all.
            from invitation in db.TripInvitations.AsNoTracking()
            join caver in db.Cavers.AsNoTracking() on invitation.CaverId equals caver.Id
            where invitation.EventId == eventId && caver.UserId != null
            select caver.UserId!.Value)
            .ToListAsync(ct);

    /// <summary>
    /// The events one account is on, as a query rather than an answer, so a listing can narrow by
    /// it without first knowing which events to ask about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Whoever was asked about the event and has not said no, joined through the roster entry the
    /// account owns — the same reading a trip takes of its answers, deliberately, so that "mine"
    /// means one thing across every family the calendar shows. An event has no roster to union
    /// in: who is coming is worked out from the answers, so the answers are the whole of it.
    /// </para>
    /// <para>
    /// The declined answer is left out for the reason it is left out of a trip's: a diary fills up
    /// with the evenings somebody turned down and nothing on the row carries their own answer, so
    /// a declined meeting would read exactly like one they are going to. Somebody asked and still
    /// silent is in, because being asked is being expected.
    /// </para>
    /// <para>
    /// It takes the account and nothing else, on purpose. A version taking a person would answer
    /// "which evenings has this named person been asked to" out of events the asker may never
    /// open; because the only account it can be asked about is the one making the request, there
    /// is no such question to ask.
    /// </para>
    /// </remarks>
    public static IQueryable<Guid> EventIdsTheAccountIsOn(SilexGisDbContext db, Guid userId) =>
        from invitation in db.TripInvitations.AsNoTracking()
        join caver in db.Cavers.AsNoTracking() on invitation.CaverId equals caver.Id
        where caver.UserId == userId
            && invitation.Response != TripInvitationResponse.No
            && invitation.EventId != null
        select invitation.EventId!.Value;
}
