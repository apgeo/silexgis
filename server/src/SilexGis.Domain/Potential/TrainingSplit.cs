// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Statistics;

namespace SilexGis.Domain.Potential;

/// <summary>The occupied cells of an area, parted into those a model learns from and those kept back to test it.</summary>
/// <param name="Training">The cells the weights are counted from, in ascending order.</param>
/// <param name="HeldOut">The cells the model never sees, in ascending order.</param>
public sealed record TrainingSplit(IReadOnlyList<int> Training, IReadOnlyList<int> HeldOut);

/// <summary>
/// The parting of known caves into a training part and a held-out part, by a stated seed.
/// </summary>
/// <remarks>
/// <para>
/// A map is always good at finding the caves it was made from. What it is worth is how well it
/// finds caves it was not told about, and the only caves available for that question are known
/// ones kept back from the counting.
/// </para>
/// <para>
/// What is parted is occupied cells, not entrances. Two entrances in one cell are one unit to the
/// counting, and parting them would put the same cell on both sides: the model would be tested on
/// ground it had been shown.
/// </para>
/// </remarks>
public static class TrainingSplits
{
    /// <summary>The smallest share of the occupied cells that may be kept back.</summary>
    public const double MinHeldOutShare = 0.1d;

    /// <summary>The largest share that may be kept back: beyond a half the test outweighs the model.</summary>
    public const double MaxHeldOutShare = 0.5d;

    /// <summary>The share kept back when none is stated.</summary>
    public const double DefaultHeldOutShare = 0.3d;

    /// <summary>
    /// The fewest occupied cells a model is trained on. Below it the held-out part is a cell or
    /// two, whose curve is a step or two and whose area says nothing about the model.
    /// </summary>
    public const int MinimumCells = 10;

    /// <summary>Whether a share lies in the range a split accepts.</summary>
    public static bool IsAllowedShare(double heldOutShare) =>
        double.IsFinite(heldOutShare) && heldOutShare >= MinHeldOutShare && heldOutShare <= MaxHeldOutShare;

    /// <summary>
    /// The split of these cells, or null when there are fewer than <see cref="MinimumCells"/>.
    /// </summary>
    /// <remarks>
    /// The cells are put in ascending order before any is drawn, so the split depends on which
    /// cells are occupied, the share and the seed, and on nothing else — not on the order the
    /// entrances were read in. The held-out part is the share of the cells rounded to the nearest
    /// whole cell, and never none and never all.
    /// </remarks>
    /// <param name="occupiedCells">The cells that hold a known entrance, each once.</param>
    public static TrainingSplit? Of(IReadOnlyCollection<int> occupiedCells, double heldOutShare, int seed)
    {
        ArgumentNullException.ThrowIfNull(occupiedCells);
        if (!IsAllowedShare(heldOutShare))
        {
            throw new ArgumentOutOfRangeException(nameof(heldOutShare));
        }

        var cells = occupiedCells.Order().ToArray();
        for (var i = 1; i < cells.Length; i++)
        {
            if (cells[i] == cells[i - 1])
            {
                throw new ArgumentException("A cell is occupied once however many entrances it holds.", nameof(occupiedCells));
            }
        }

        if (cells.Length < MinimumCells)
        {
            return null;
        }

        var heldOut = Math.Clamp(
            (int)Math.Round(cells.Length * heldOutShare, MidpointRounding.AwayFromZero), 1, cells.Length - 1);

        // The first steps of a Fisher–Yates shuffle: after them the front of the array is a
        // uniform draw of that many cells, which is all that is needed of the shuffle.
        var random = new DeterministicRandom(seed);
        for (var i = 0; i < heldOut; i++)
        {
            var j = i + random.NextBelow(cells.Length - i);
            (cells[i], cells[j]) = (cells[j], cells[i]);
        }

        return new TrainingSplit(
            [.. cells.Skip(heldOut).Order()],
            [.. cells.Take(heldOut).Order()]);
    }
}
