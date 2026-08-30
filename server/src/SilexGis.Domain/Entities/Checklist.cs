// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// A named list of things to settle before a party sets off — a permit to obtain, a key to
/// collect, gear to book — authored by whoever wants one and shared as far as its audience
/// says.
/// <para>
/// There is one kind of checklist, not two. A list an administrator publishes for the whole
/// installation is this same row, owned by the account that published it and given an
/// audience everyone falls inside; nothing about it is a special case, and no reader asks
/// whether a list is "the default" before deciding whether it may be read. That is what
/// keeps a club's own list and a published one on one code path: an audience column already
/// says "everybody may read this", so a second mechanism saying the same thing would be free
/// to disagree with it.
/// </para>
/// <para>
/// Its audience is the visibility column alone. There is deliberately no capability token:
/// what a visitor with no account can reach is a short, named allow-list, and a list of
/// preparations for a trip has no reason to join it.
/// </para>
/// </summary>
public class Checklist : IProtectedEntity, ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Title { get; set; }

    public string? Description { get; set; }

    public Guid OwnerUserId { get; set; }

    public Guid? CavingGroupId { get; set; }

    public Visibility Visibility { get; set; } = Visibility.Private;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}

/// <summary>
/// One line of a checklist: free text, in an order somebody chose.
/// <para>
/// It is a row rather than an element of a document on the list, and the reason is the tick.
/// Somebody confirming an item is recorded against that item by identity, so the identity has
/// to be something the database can hold another table to — a key inside a JSON document is a
/// reference nothing can enforce, and reordering or rewording a list would quietly strand
/// every confirmation made against it.
/// </para>
/// <para>
/// The text is free, and that is a decision rather than a first version of something else.
/// Deriving "this is already done" from what the trip itself records would serve barely half
/// of what people actually put on such a list — whether the gear is booked is answered by the
/// party's own free-text note and by nothing a query can reach, and being sure somebody
/// carries first aid has no vocabulary on a trip at all. The way back, when there is one, is
/// an optional code on the item saying which recorded fact settles it; until then a list is
/// read and ticked by people.
/// </para>
/// </summary>
public class ChecklistItem
{
    /// <summary>Stable for the life of the row: a confirmation names it, and rewording the
    /// line it labels must not move which confirmation belongs to what.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ChecklistId { get; set; }

    public required string Text { get; set; }

    /// <summary>Where the line sits in the list its author wrote.</summary>
    public int SortOrder { get; set; }
}
