// SPDX-License-Identifier: AGPL-3.0-or-later
using Orientation = Therion.Orientation;

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
/// The test itself (Watson 1962; ties handled as in Zar's two-sample form; axial trends folded onto
/// the half-circle and doubled, after Mardia and Jupp; the probability from the statistic's
/// limiting distribution, no answer below <see cref="MinimumSampleSize"/> on either side) lives in
/// the dependency-free orientation-statistics module of the survey-format library. This class
/// carries its answer into this application's own result record, which the API answers with and
/// which the API layer may name where it may not name the library.
/// </remarks>
public static class WatsonU2
{
    /// <summary>The smallest sample on either side the test is run on.</summary>
    public const int MinimumSampleSize = Orientation.WatsonU2.MinimumSampleSize;

    /// <summary>
    /// The test on two samples of axial trends, in degrees — or null when either side has fewer
    /// than <see cref="MinimumSampleSize"/> observations.
    /// </summary>
    public static WatsonU2Result? CompareAxial(IReadOnlyList<double> first, IReadOnlyList<double> second) =>
        FromLibrary(Orientation.WatsonU2.CompareAxial(first, second));

    /// <summary>
    /// The test on two samples of directions on the full circle, in degrees — or null when either
    /// side has fewer than <see cref="MinimumSampleSize"/> observations.
    /// </summary>
    public static WatsonU2Result? Compare(IReadOnlyList<double> first, IReadOnlyList<double> second) =>
        FromLibrary(Orientation.WatsonU2.Compare(first, second));

    /// <summary>
    /// The chance of a U² at least <paramref name="statistic"/> when both samples come from one
    /// distribution, from the limiting distribution of the statistic.
    /// </summary>
    public static double Probability(double statistic) => Orientation.WatsonU2.Probability(statistic);

    private static WatsonU2Result? FromLibrary(Orientation.WatsonU2Result? result) =>
        result is null
            ? null
            : new WatsonU2Result(result.Statistic, result.PValue, result.FirstCount, result.SecondCount);
}
