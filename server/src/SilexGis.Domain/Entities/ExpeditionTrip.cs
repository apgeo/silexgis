// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// A trip's place in a camp: one row per (expedition, trip), and at most one row per trip.
/// </summary>
/// <remarks>
/// <para>
/// A join row rather than a column on the trip. The trip is already a large record with a
/// positional contract, and which camp gathered it is the camp's concern — a camp is the thing
/// that has members, and a trip stays a complete record with no camp at all. The "at most one"
/// is carried by a unique index on the trip rather than by every writer remembering it, so a
/// second row for the same trip is refused by the database and not by whoever wrote the handler.
/// </para>
/// <para>
/// Deliberately not a resource link: a link caps its membership and resolves every member at
/// once, and a fortnight's camp is exactly the size where both of those bite.
/// </para>
/// </remarks>
public class ExpeditionTrip : ITimestamped, IAuditable, IAuditChild
{
    public long Id { get; set; }

    public Guid ExpeditionId { get; set; }

    public Guid TripLogId { get; set; }

    /// <summary>
    /// When the trip was put in this camp. Kept as a fact of its own rather than read off the
    /// row's audit timestamps: a later act that asks "which trips were in the camp when sharing
    /// was last applied" is asking about the joining, and a row touched for any other reason
    /// would move an audit stamp underneath it.
    /// </summary>
    public DateTimeOffset JoinedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    /// <summary>
    /// A trip joining or leaving is a change to the camp, so it surfaces on the camp's timeline:
    /// a camp gathers forty trips over a fortnight and nobody reads forty separate histories.
    /// </summary>
    public string RootEntityType => nameof(Expedition);

    public string RootEntityId => ExpeditionId.ToString();
}
