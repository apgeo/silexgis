// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
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
}
