// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// The number one person holds in one trip's party — "Caver 3" — written down once, the first time
/// the trip names them, and never changed afterwards.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stored because it cannot be derived.</b> The number was once a person's rank among the
/// roster's rows, and a roster row is rewritten whenever a job changes: giving somebody a second
/// job, or a different one, moved them to the end of the party, and taking anybody off the trip
/// renumbered everyone after them. A follower reads that number back over a telephone and a
/// published page keys its markers, its cards and its "follow this person" choice on it, so a
/// number that moves is a number that silently comes to mean somebody else.
/// </para>
/// <para>
/// <b>A number is given once and to one person.</b> Somebody taken off the roster keeps their row,
/// so the party shows a gap where they were ("Caver 1, Caver 3") and they get the same number back
/// if the trip names them again. A number is never handed to a second person: when the entry it
/// belonged to is removed, or merged into another entry that already holds a number on the trip,
/// the row stays with <see cref="CaverId"/> empty, which keeps the number taken.
/// </para>
/// <para>
/// Bookkeeping, not history: it is deliberately absent from the trip's audit trail, where a line
/// per person saying "was given number 3" would be noise beside the roster change that caused it.
/// </para>
/// </remarks>
public class TripPartyNumber
{
    public Guid TripLogId { get; set; }

    /// <summary>
    /// The trip this row belongs to. Present so the model can hide the row while its trip is
    /// deleted; nothing reads the trip through it, and it is not loaded unless asked for.
    /// </summary>
    public TripLog TripLog { get; set; } = null!;

    /// <summary>From 1, in the order the trip first named its people. Half of the row's key.</summary>
    public int Number { get; set; }

    /// <summary>
    /// Whose number it is, or null for a number that was given and whose holder is gone — kept so
    /// that nobody else is ever given it.
    /// </summary>
    public Guid? CaverId { get; set; }
}
