// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// The links that stand: every link but a removed place pin, and those that cannot mean anything
/// without a trip that is deleted. The one home of the answer, which every route that finds a link
/// as a link reads through.
/// </summary>
/// <remarks>
/// <para>
/// Deleting a trip removes nothing, so that putting it back is exact: its links stay in storage
/// with their members. What they must not do meanwhile is read as links. A trip's role link without
/// its trip is a relation between the caves it named that nobody ever asserted, and it would sit on
/// each of those caves' panels until the trip was removed for good. So such a link answers as one
/// that is not there — which is what it becomes if the trip is never restored, by the same rule.
/// </para>
/// <para>
/// A place pinned on a phone's map is removed by stamping it rather than by deleting it, because
/// the phones must be told it went and an undo may bring it back under the identifier they hold.
/// Removed, it is no link at all to anything but the rasters stream and the undo, which read the
/// pins through their own query. There is deliberately no query filter for this: the stream reads
/// removed pins in the same statement as filtered features, and lifting one filter lifts them all.
/// A new reader of the links table that neither reads through this nor filters by relation would
/// see removed pins.
/// </para>
/// <para>
/// Tracked or not is each caller's to say. That holds only while nothing composed into this states
/// an option of its own: one written inside a subquery is taken for the whole query, and a link read
/// through it would be edited, saved and answered without being written.
/// </para>
/// </remarks>
public static class ResLinkStanding
{
    public static IQueryable<ResLink> Of(SilexGisDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.ResLinks
            .Where(l => l.DeletedAt == null)
            .Where(TripLinkFate.Not(TripLinkFate.EndsWithout(db, TripLinkFate.OfNoLiveTrip(db))));
    }
}
