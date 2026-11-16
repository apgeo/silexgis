// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>Watson's two-sample U² test on two sets of trends.</summary>
/// <param name="Statistic">U². Zero when the two samples are spread round the circle alike.</param>
/// <param name="PValue">
/// The chance of a U² at least this large if both samples came from one distribution, from the
/// statistic's large-sample distribution.
/// </param>
/// <param name="FirstCount">Observations in the first sample.</param>
/// <param name="SecondCount">Observations in the second sample.</param>
public sealed record WatsonU2Result(double Statistic, double PValue, int FirstCount, int SecondCount);

/// <summary>
/// Watson's U² — whether two samples of circular data could have come from one distribution.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it measures.</b> The two samples' cumulative distributions are walked round the circle
/// together and the squared gap between them is summed; subtracting the mean gap before squaring
/// is what makes the answer the same wherever the walk starts, which a test on a circle must be
/// (Watson 1962). With <c>d</c> the gap at each distinct value and <c>t</c> how many observations
/// share it, <c>U² = n₁n₂/N² · (Σ t·d² − (Σ t·d)² / N)</c> (Zar, Biostatistical Analysis, the
/// two-sample Watson test with tied values).
/// </para>
/// <para>
/// <b>Axes, not directions.</b> A passage trend and a strike are both axes: a leg surveyed 030 and
/// one surveyed 210 run along the same passage, and a plane striking 030 strikes 210 as well. Each
/// trend is folded onto the half-circle and its angle doubled, which maps the half-circle onto the
/// full one and makes the test's arithmetic apply unchanged (Mardia and Jupp, Directional
/// Statistics, on axial data).
/// </para>
/// <para>
/// <b>The probability.</b> Read from the limiting distribution,
/// <c>P(U² &gt; u) = 2 Σ (−1)^(k−1) exp(−2k²π²u)</c>. It reproduces the tabulated critical values
/// for large samples (0.187 at five per cent, 0.268 at one per cent) and is close for samples of
/// ten or more on each side; below <see cref="MinimumSampleSize"/> no answer is given at all.
/// </para>
/// </remarks>
public static class WatsonU2
{
    /// <summary>The smallest sample on either side the test is run on.</summary>
    public const int MinimumSampleSize = 5;

    private const int SeriesTerms = 100;

    /// <summary>
    /// The test on two samples of axial trends, in degrees — or null when either side has fewer
    /// than <see cref="MinimumSampleSize"/> observations.
    /// </summary>
    public static WatsonU2Result? CompareAxial(IReadOnlyList<double> first, IReadOnlyList<double> second) =>
        Compare(
            first.Select(DoubledAxial).ToList(),
            second.Select(DoubledAxial).ToList());

    /// <summary>
    /// The test on two samples of directions on the full circle, in degrees — or null when either
    /// side has fewer than <see cref="MinimumSampleSize"/> observations.
    /// </summary>
    public static WatsonU2Result? Compare(IReadOnlyList<double> first, IReadOnlyList<double> second)
    {
        var n1 = first.Count;
        var n2 = second.Count;
        if (n1 < MinimumSampleSize || n2 < MinimumSampleSize)
        {
            return null;
        }

        var tagged = first.Select(a => (Angle: StructuralMeasurement.NormalizeAzimuth(a), First: true))
            .Concat(second.Select(a => (Angle: StructuralMeasurement.NormalizeAzimuth(a), First: false)))
            .OrderBy(x => x.Angle)
            .ToList();

        var total = n1 + n2;
        double seen1 = 0, seen2 = 0, sumD = 0, sumD2 = 0;
        var i = 0;
        while (i < tagged.Count)
        {
            // Every observation at one value moves both curves before the gap is read, so a tie
            // across the samples is not counted as a step in one and then a step back.
            var angle = tagged[i].Angle;
            var ties = 0;
            while (i < tagged.Count && tagged[i].Angle == angle)
            {
                if (tagged[i].First)
                {
                    seen1++;
                }
                else
                {
                    seen2++;
                }

                ties++;
                i++;
            }

            var d = (seen1 / n1) - (seen2 / n2);
            sumD += ties * d;
            sumD2 += ties * d * d;
        }

        var statistic = (double)n1 * n2 / ((double)total * total) * (sumD2 - (sumD * sumD / total));
        statistic = Math.Max(0d, statistic);
        return new WatsonU2Result(statistic, Probability(statistic), n1, n2);
    }

    /// <summary>
    /// The chance of a U² at least <paramref name="statistic"/> when both samples come from one
    /// distribution, from the limiting distribution of the statistic.
    /// </summary>
    public static double Probability(double statistic)
    {
        if (!double.IsFinite(statistic) || statistic < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(statistic), "U² is a finite, non-negative number.");
        }

        // Near zero the alternating series needs more terms than it is worth and every term sits
        // close to one; the answer there is "nothing to see", and it is said exactly.
        if (statistic < 0.01d)
        {
            return 1d;
        }

        var sum = 0d;
        for (var k = 1; k <= SeriesTerms; k++)
        {
            var term = Math.Exp(-2d * k * k * Math.PI * Math.PI * statistic);
            sum += k % 2 == 1 ? term : -term;
            if (term < 1e-16)
            {
                break;
            }
        }

        return Math.Clamp(2d * sum, 0d, 1d);
    }

    private static double DoubledAxial(double degrees) => 2d * OrientationStatistics.Fold(degrees);
}
