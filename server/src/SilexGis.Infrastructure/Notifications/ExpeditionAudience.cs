// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>
/// Who is on a camp: whoever its roster records a stay for.
/// </summary>
/// <remarks>
/// <para>
/// The roster and nothing else. A camp's people are not its cavers — the cook, the driver and
/// whoever kept the base camp were there for the fortnight and went underground on none of it —
/// so being on a camp is never derived from the trips gathered into it, and the roster is the one
/// table that records a stay. It is kept by whoever is running the camp, over its span, which is
/// what makes it a record of presence rather than of intent; there is no answer to union in,
/// because a camp asks nobody anything.
/// </para>
/// <para>
/// A person with two roles on one camp holds two rows and is on it once; the query returns the
/// camp, so a listing narrowing by it is unaffected. The dates of the stay are not read here:
/// being on a camp for one of its ten days is being on the camp, exactly as being named on one day
/// of a trip is being on the trip.
/// </para>
/// <para>
/// It takes the account and nothing else, for the reason the trip and event rules do: the only
/// account it can be asked about is the one making the request, so it can never answer where a
/// named person has been out of camps the asker may not open.
/// </para>
/// </remarks>
public static class ExpeditionAudience
{
    /// <summary>The camps one account is on, as a query rather than an answer.</summary>
    public static IQueryable<Guid> ExpeditionIdsTheAccountIsOn(SilexGisDbContext db, Guid userId) =>
        (from stay in db.ExpeditionRoster.AsNoTracking()
         join caver in db.Cavers.AsNoTracking() on stay.CaverId equals caver.Id
         where caver.UserId == userId
         select stay.ExpeditionId)
        .Distinct();
}
