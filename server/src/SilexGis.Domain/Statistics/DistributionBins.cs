// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Statistics;

/// <summary>One sector of a histogram.</summary>
/// <param name="LowerBound">Included in the sector.</param>
/// <param name="UpperBound">Excluded, except on the topmost sector, whose upper edge is closed so
/// the largest observation has somewhere to fall.</param>
/// <param name="Count">Observations in the sector.</param>
/// <param name="Merged">True when this sector is several of the requested ones joined together
/// because one of them held too few observations to publish on its own. A reader can see that the
/// axis is not evenly divided, which is the point: the alternative is an even axis with a hole in
/// it, and a hole is a statement.</param>
public sealed record DistributionBin(
    double LowerBound, double UpperBound, int Count, bool Merged);

/// <summary>
/// The rule that turns a raw histogram into one that can be published.
/// </summary>
/// <remarks>
/// <para>
/// <b>A thin sector is joined to its neighbour, never removed.</b> Removing it would say more than
/// keeping it: a reader who sees an even axis with one sector missing has been told that something
/// rare exists exactly there, and if the axis is a length or a depth, that is a description of one
/// cave narrow enough to identify it. Joining two sectors says only that the answer in this stretch
/// of the range is coarser than it was asked for.
/// </para>
/// <para>
/// <b>Empty sectors stand.</b> A sector holding nothing describes nothing and identifies nobody, and
/// keeping it is how the shape of the distribution survives — a registry with a wide gap between
/// its short caves and its long ones should show the gap. Only a sector holding at least one
/// observation and fewer than the floor is joined to anything.
/// </para>
/// <para>
/// <b>The whole histogram may collapse to one sector</b>, when the sample is smaller than the floor.
/// That is the honest answer rather than a failure: it says there is a range and there are this
/// many caves in it, and it declines to say where in the range they sit.
/// </para>
/// </remarks>
public static class DistributionBins
{
    /// <summary>
    /// Joins every sector holding between one observation and <paramref name="minimumCount"/> of
    /// them into a neighbour, until no such sector remains.
    /// </summary>
    /// <param name="bins">The raw sectors, in ascending order of lower bound and abutting one
    /// another.</param>
    /// <param name="minimumCount">The floor. One or below leaves the histogram untouched, because
    /// every non-empty sector already holds at least one observation.</param>
    /// <remarks>
    /// Each pass joins the leftmost thin sector to the sector on its right, or to the one on its
    /// left when it is the last. Choosing the neighbour by position rather than by which of them
    /// holds fewer observations is deliberate: a rule that consults the counts makes the *shape* of
    /// the published axis depend on quantities that were withheld, and a caller comparing two
    /// callers' axes could read those quantities back out of where the joins fell.
    /// </remarks>
    public static IReadOnlyList<DistributionBin> Merge(
        IReadOnlyList<DistributionBin> bins, int minimumCount)
    {
        ArgumentNullException.ThrowIfNull(bins);

        if (minimumCount <= 1 || bins.Count <= 1)
        {
            return bins;
        }

        var working = new List<DistributionBin>(bins);

        // Terminates: every join removes a sector, and a single sector is never thin enough to
        // join because there is nothing left to join it to.
        while (working.Count > 1)
        {
            var thin = -1;
            for (var i = 0; i < working.Count; i++)
            {
                if (working[i].Count > 0 && working[i].Count < minimumCount)
                {
                    thin = i;
                    break;
                }
            }

            if (thin < 0)
            {
                break;
            }

            var left = thin == working.Count - 1 ? thin - 1 : thin;
            var right = left + 1;
            working[left] = new DistributionBin(
                working[left].LowerBound,
                working[right].UpperBound,
                working[left].Count + working[right].Count,
                true);
            working.RemoveAt(right);
        }

        return working;
    }
}
