// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Trips;

/// <summary>
/// How much of a checklist a trip has settled: <c>Ticked</c> of <c>Total</c>.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is ever written down, and nothing anywhere is refused because of it. It is a
/// reading of rows that already exist — the lines on a list, and the confirmations made against
/// them — worked out on the way out and stored nowhere, so there is never a second copy of it to
/// disagree with the first the moment somebody adds a line or takes a confirmation back.
/// </para>
/// <para>
/// It is advisory in the strongest sense: it is not a state the trip is in, it does not gate any
/// write, and it is never consulted when deciding who may read a trip. Who may read a row is
/// settled by that row's audience and the entries about it, in one place; a figure like this one
/// joining that decision would be a second answer to the same question, free to disagree with
/// the first — and a plan that disappeared because somebody took a confirmation back would be a
/// disclosure rule nobody wrote and nobody could audit.
/// </para>
/// <para>
/// It is also deliberately capable of understating and incapable of overstating. A confirmation
/// naming a line that is not on the list being measured counts for nothing, so the answer is
/// never more settled than the list really is. A mark that is sometimes behind is worth having;
/// one that could be ahead would not be.
/// </para>
/// </remarks>
/// <param name="Ticked">Lines of the list confirmed for this trip. Never more than <paramref name="Total"/>.</param>
/// <param name="Total">Lines on the list. Zero for a list with nothing on it, which is settled.</param>
public readonly record struct TripReadiness(int Ticked, int Total)
{
    /// <summary>Whether every line on the list has been confirmed. A list with no lines is.</summary>
    public bool IsComplete => Ticked >= Total;

    /// <summary>
    /// What a trip has settled of one list.
    /// </summary>
    /// <param name="itemIds">The lines on the list being measured.</param>
    /// <param name="tickedItemIds">
    /// The lines this trip has confirmations against. Confirmations naming anything outside
    /// <paramref name="itemIds"/> are ignored rather than counted, which is what keeps the answer
    /// from ever reading more settled than the list is.
    /// </param>
    public static TripReadiness Of(
        IReadOnlyCollection<Guid> itemIds, IReadOnlyCollection<Guid> tickedItemIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        ArgumentNullException.ThrowIfNull(tickedItemIds);

        if (itemIds.Count == 0)
        {
            return new TripReadiness(0, 0);
        }

        var ticked = new HashSet<Guid>(tickedItemIds);
        return new TripReadiness(itemIds.Count(ticked.Contains), itemIds.Count);
    }
}
