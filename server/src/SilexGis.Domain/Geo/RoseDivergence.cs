// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>
/// How far apart two axial roses are, and how far a reader has to turn one to get the other.
/// </summary>
/// <remarks>
/// <para>
/// The measure is the <b>circular transport cost</b> between the two sector histograms: the least
/// total work, counted as share-of-the-rose times degrees-turned, needed to move one distribution
/// onto the other around the folded half-circle. It is reported two ways because the two answer
/// different questions. <see cref="TransportDegrees"/> is in degrees and is the interpretable one —
/// "on average a trend has to rotate this far" — while <see cref="Normalized"/> divides it by the
/// largest value it can take and is the one to compare between caves.
/// </para>
/// <para>
/// <b>Why the ends of the scale are exactly where they are.</b> On the folded half-circle the
/// furthest anything can be from anything else is a quarter turn: past 90° the shorter way round is
/// back towards where it started, because 100° and 80° are the same distance from 0°. So a rose
/// laid exactly on top of another costs nothing to move and scores zero, and a rose whose whole
/// share sits a quarter turn away costs 90° per unit of share and scores one. Nothing can score
/// more, and the two ends are the two cases worth asserting: a measure that only ever returns small
/// numbers passes a test that checks one of them.
/// </para>
/// <para>
/// <b>Why transport rather than the angle between the two mean axes.</b> A mean axis exists only
/// when a rose has one trend; a cave with two passage directions at right angles has a mean that
/// points nowhere in particular, and comparing that to a fault set would report an agreement or a
/// disagreement that neither body of measurements makes. Transport cost is defined for every pair
/// of roses, including ones with no mean and ones with several peaks, and it reads the whole shape
/// rather than one summary of it. The mean separation is still reported alongside, as
/// <see cref="MeanAxisSeparationDegrees"/>, because when both roses do have a single trend it is
/// the number a reader recognises — but it is null whenever either mean is, and the transport cost
/// is the measure.
/// </para>
/// <para>
/// This is arithmetic over two histograms and knows nothing about what was measured. Whether the
/// two roses are comparable at all — same fold, same sector edges, bearings taken the same way — is
/// the caller's judgment and is checked where the two sets of measurements are assembled.
/// </para>
/// </remarks>
/// <param name="TransportDegrees">
/// Least average rotation, in degrees, between the two roses. Zero when they are identical, at most
/// a quarter turn.
/// </param>
/// <param name="Normalized">
/// <paramref name="TransportDegrees"/> as a share of the quarter turn: 0 for identical roses, 1 for
/// two roses a quarter turn apart.
/// </param>
/// <param name="MeanAxisSeparationDegrees">
/// The acute angle between the two roses' mean axes, 0 to 90, or null when either rose has no mean
/// trend to speak of.
/// </param>
public sealed record RoseDivergence(
    double TransportDegrees,
    double Normalized,
    double? MeanAxisSeparationDegrees);

/// <summary>
/// Compares two axial roses that were binned over the same sectors.
/// </summary>
public static class RoseComparison
{
    /// <summary>
    /// The furthest apart two trends can be once bearings are folded onto the half-circle, and so
    /// the value the transport cost is divided by to put it on a nought-to-one scale.
    /// </summary>
    public const double QuarterTurnDegrees = 90d;

    /// <summary>
    /// Compares two rose histograms by the share of each that falls in each sector.
    /// </summary>
    /// <param name="left">The first rose's sectors, in ascending order and complete.</param>
    /// <param name="right">The second rose's sectors, same sectors in the same order.</param>
    /// <param name="byLength">
    /// True to compare the length-weighted shares, false to compare the counts. Which one is right
    /// is the caller's decision and it changes the answer: a fault set of a few long traces and a
    /// cave of many short legs agree or disagree differently depending on whether a metre or an
    /// observation is the unit.
    /// </param>
    /// <param name="leftMeanAxisDegrees">The first rose's mean axis, or null if it has none.</param>
    /// <param name="rightMeanAxisDegrees">The second rose's mean axis, or null if it has none.</param>
    /// <returns>
    /// The divergence, or null when either rose is empty — nothing was measured, so there is no
    /// disagreement to report, and answering zero would claim the two agree perfectly.
    /// </returns>
    public static RoseDivergence? Compare(
        IReadOnlyList<OrientationBin> left,
        IReadOnlyList<OrientationBin> right,
        bool byLength,
        double? leftMeanAxisDegrees,
        double? rightMeanAxisDegrees)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.Count != right.Count)
        {
            throw new ArgumentException(
                "Two roses can only be compared sector by sector when they were binned over the "
                + "same sectors.",
                nameof(right));
        }

        if (left.Count == 0)
        {
            return null;
        }

        var p = Shares(left, byLength);
        var q = Shares(right, byLength);
        if (p is null || q is null)
        {
            return null;
        }

        var sectorWidth = 180d / left.Count;
        var transport = CircularTransportCost(p, q) * sectorWidth;

        // Floating-point subtraction over a run of shares leaves a residue of the order of the
        // machine epsilon even when the two roses are the same array of numbers, and a caller
        // told the divergence is 3e-17 rather than 0 reasonably concludes the two differ.
        if (transport < 1e-9)
        {
            transport = 0d;
        }

        var normalized = Math.Min(transport / QuarterTurnDegrees, 1d);

        return new RoseDivergence(transport, normalized, MeanSeparation(leftMeanAxisDegrees, rightMeanAxisDegrees));
    }

    /// <summary>
    /// The acute angle between two axes, 0 to 90 — null when either is missing. Axes have no
    /// direction, so a separation of 170° is a separation of 10°, and reporting the first would
    /// call two nearly-parallel trends nearly opposite.
    /// </summary>
    public static double? MeanSeparation(double? leftDegrees, double? rightDegrees)
    {
        if (leftDegrees is not { } a || rightDegrees is not { } b)
        {
            return null;
        }

        var gap = Math.Abs(OrientationStatistics.Fold(a) - OrientationStatistics.Fold(b));
        return gap > QuarterTurnDegrees ? 180d - gap : gap;
    }

    /// <summary>
    /// The per-sector shares of one rose, or null when the rose holds nothing. Read from the
    /// fractions the summary already computed rather than re-derived, so a rose compared here and
    /// a rose drawn on screen are the same numbers.
    /// </summary>
    private static double[]? Shares(IReadOnlyList<OrientationBin> bins, bool byLength)
    {
        var shares = new double[bins.Count];
        var total = 0d;
        for (var i = 0; i < bins.Count; i++)
        {
            shares[i] = byLength ? bins[i].LengthFraction : bins[i].CountFraction;
            total += shares[i];
        }

        return total <= 0d ? null : shares;
    }

    /// <summary>
    /// Least transport cost between two distributions on a cycle of evenly spaced sectors, counted
    /// in sectors moved.
    /// </summary>
    /// <remarks>
    /// On a line the cost is the sum of the absolute running differences. On a cycle there is a
    /// second way round, and the standard result is that the cost becomes the sum of those running
    /// differences each shifted by one constant — the amount of share sent the long way round the
    /// circle — minimised over that constant. The minimiser of a sum of absolute deviations is the
    /// median, so the constant is the median of the running differences and no search is needed.
    /// </remarks>
    private static double CircularTransportCost(double[] p, double[] q)
    {
        var running = new double[p.Length];
        var carried = 0d;
        for (var i = 0; i < p.Length; i++)
        {
            carried += p[i] - q[i];
            running[i] = carried;
        }

        var sorted = (double[])running.Clone();
        Array.Sort(sorted);
        var median = sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2d;

        var cost = 0d;
        foreach (var value in running)
        {
            cost += Math.Abs(value - median);
        }

        return cost;
    }
}
