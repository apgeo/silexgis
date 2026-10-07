// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Linq.Expressions;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// What becomes of the links a trip is named on once the trip is gone.
/// </summary>
/// <remarks>
/// <para>
/// One rule, read twice. A trip removed for good takes down the links that cannot mean anything
/// without it; a trip that is merely deleted keeps every one of them, so that putting it back is
/// exact, and for as long as it is deleted those same links have to read as though they had
/// already gone. If the two readings were written separately a link would vanish when its trip
/// was deleted and reappear, changed, the day the trip was removed — so both ask here, with
/// different answers to "which trip memberships are gone".
/// </para>
/// <para>
/// <b>A link typed with one of the trip roles goes whole</b>, however many features it still
/// names: the role says what <em>this trip</em> did there, so the surviving members are not
/// related to each other by anything once the trip is gone. Leaving it would also leave a
/// directed link with no distinguished member, which the link rules refuse — the result would
/// show on every named cave's links panel as a relation to the other caves, and no later edit
/// of it would be accepted.
/// </para>
/// <para>
/// <b>A link anchored to a moment of the trip goes whole</b> for the same reason, and the reason
/// is worth stating because the relation it is written under is not a trip role and would
/// otherwise slip past the rule above. Such a link says "at 14:05 of this trip", and the two
/// other things it names are a photograph and, usually, the caver the moment was about —
/// related to each other by nothing at all once the instant they share has gone. Left behind it
/// is worse than an orphan: a directed link whose distinguished member has been deleted, which
/// renders on that caver's own links panel as a photograph documenting a person — an
/// association nobody ever made, assembled by a delete.
/// </para>
/// <para>
/// Only where the moment is the link's <em>main</em> member, which is the shape a picture on a
/// moment is written in. A link that merely mentions a moment of the trip while being about
/// something else — a document, say, which is what its main member names — is an association
/// somebody authored deliberately, whose subject outlives the trip and whose curator can still
/// edit it. That one keeps whatever it still relates, under the rule below, exactly like any
/// other link the trip merely joined.
/// </para>
/// <para>
/// <b>A link of any other kind the trip merely joined keeps whatever it still relates</b>, and
/// goes only when one member is left: an association with one end is a thing no surface offers
/// and no delete path would ever reach again.
/// </para>
/// </remarks>
public static class TripLinkFate
{
    /// <summary>The memberships that name this one trip — what is gone when it is removed.</summary>
    public static Expression<Func<ResLinkMember, bool>> OfTrip(Guid tripId) =>
        m => m.EntityType == AttachedEntityType.TripLog && m.EntityId == tripId;

    /// <summary>
    /// The memberships that name a trip no reader can reach: one that is deleted, and one that
    /// was removed leaving a row behind. Asked through the trips' own filter rather than past
    /// it, so "deleted" has the one definition the model gives it — and so composing this into a
    /// larger query does not switch that query's other filters off, which reading past the
    /// filter would.
    /// </summary>
    public static Expression<Func<ResLinkMember, bool>> OfNoLiveTrip(SilexGisDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        return m => m.EntityType == AttachedEntityType.TripLog && !db.TripLogs.Any(t => t.Id == m.EntityId);
    }

    /// <summary>
    /// Whether a link ends once the memberships <paramref name="gone"/> names are gone: it is one
    /// of the two kinds that mean nothing without their trip, or it would be left with fewer
    /// than two members.
    /// </summary>
    public static Expression<Func<ResLink, bool>> EndsWithout(
        SilexGisDbContext db, Expression<Func<ResLinkMember, bool>> gone)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(gone);

        var roleIds = TripRoleLinks.RoleIds(db);
        var going = db.ResLinkMembers.Where(gone);
        var staying = db.ResLinkMembers.Where(Not(gone));
        return link => going.Any(m => m.ResLinkId == link.Id)
            && ((link.RelationTypeId != null && roleIds.Contains(link.RelationTypeId.Value))
                || going.Any(m => m.ResLinkId == link.Id && m.AnchorKind == AnchorKind.TripMoment && m.IsMain)
                || staying.Count(m => m.ResLinkId == link.Id) < 2);
    }

    /// <summary>The opposite of a condition, as a condition a query can still translate.</summary>
    public static Expression<Func<T, bool>> Not<T>(Expression<Func<T, bool>> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return Expression.Lambda<Func<T, bool>>(Expression.Not(condition.Body), condition.Parameters);
    }
}
