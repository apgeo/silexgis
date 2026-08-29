// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// One line of a checklist confirmed as settled for one trip: who said so, and when.
/// </summary>
/// <remarks>
/// <para>
/// It is written down because it is not a function of anything else. "Jane confirmed the permit
/// on Tuesday" cannot be worked out from the trip, from the list, or from the line — it is a
/// person's assertion about the world, and the only record of it is the one made when they made
/// it. Everything derived from these rows — how much of a list is settled — is computed on the
/// way out and stored nowhere.
/// </para>
/// <para>
/// The list itself is never copied onto the trip. A trip points at a list and confirms lines of
/// it; rewording a line leaves every confirmation against it standing, because a confirmation
/// names the line's own key rather than its text.
/// </para>
/// <para>
/// The list is named on the row even though the line already belongs to one, and that is
/// deliberate rather than redundant. Which list a trip is working through can change — a purpose
/// re-pointed at a different one, a club adopting its own — and a confirmation belongs to the
/// list it was made against, not to whichever list the trip would resolve to today. The pair is
/// kept honest by the database rather than by the code that writes it: the line is referenced by
/// list and key together, so a row claiming a line belongs to a list it does not belong to
/// cannot be written at all.
/// </para>
/// <para>
/// Nothing anywhere refuses a write because a list is unsettled. A checklist exists to make
/// preparing a trip easier to see, and a party that must finish the paperwork before it can save
/// the plan is a party that keeps the plan somewhere else. How settled a trip is is advisory,
/// and it is never consulted when deciding who may read the trip.
/// </para>
/// </remarks>
public class TripChecklistTick
{
    public Guid TripLogId { get; set; }

    /// <summary>The list this confirmation was made against, as it stood when it was made.</summary>
    public Guid ChecklistId { get; set; }

    /// <summary>The line confirmed. Stable across every rewording of the text it labels.</summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Who confirmed it, or null once that account is gone. The confirmation itself survives the
    /// account: that the permit was in hand on Tuesday stays true after whoever said so leaves.
    /// </summary>
    public Guid? TickedByUserId { get; set; }

    /// <summary>When it was confirmed, in UTC, which is the only clock this application stores.</summary>
    public DateTimeOffset TickedAt { get; set; }
}
