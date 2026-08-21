// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
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
            where tripIds.Contains(invitation.TripLogId) && caver.UserId == userId
            select invitation.TripLogId)
            .ToListAsync(ct);

        return [.. named, .. asked];
    }
}
