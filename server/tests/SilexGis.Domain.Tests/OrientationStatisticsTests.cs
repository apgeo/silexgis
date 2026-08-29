// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The orientation arithmetic against hand-computed answers.
///
/// <para>
/// Every expected number here was worked out independently of the implementation, and the two
/// that matter most are the ones a plausible-looking wrong implementation still passes everything
/// else with: a sample that straddles north, where averaging bearings directly gives an answer
/// nearly at right angles to the passage, and a Rayleigh probability checked against a published
/// critical value rather than against whatever the code happens to produce.
/// </para>
/// </summary>
public class OrientationStatisticsTests
{
    private static OrientationSample At(double azimuth, double lengthM = 1d) => new(azimuth, lengthM);

    /// <summary>
    /// How far apart two trends are as axes, degrees in 0–90. Comparing the numbers directly would
    /// fail on a north–south trend for no reason but rounding: 0 and 180 are one axis, and which
    /// side of the wrap a mean lands on is decided by the last bit of a vector sum.
    /// </summary>
    private static double AxialGap(double a, double b)
    {
        var gap = Math.Abs(OrientationStatistics.Fold(a) - OrientationStatistics.Fold(b));
        return gap > 90d ? 180d - gap : gap;
    }

    [Fact]
    public void A_bearing_and_the_same_bearing_surveyed_backwards_fold_onto_one_trend()
    {
        OrientationStatistics.Fold(10d).ShouldBe(10d);
        OrientationStatistics.Fold(190d).ShouldBe(10d, 1e-12);
        OrientationStatistics.Fold(350d).ShouldBe(170d, 1e-12);
        OrientationStatistics.Fold(170d).ShouldBe(170d);

        // The half-circle is closed at zero and open at 180: due north and due south are one axis.
        OrientationStatistics.Fold(0d).ShouldBe(0d);
        OrientationStatistics.Fold(180d).ShouldBe(0d);
        OrientationStatistics.Fold(360d).ShouldBe(0d);

        // Values outside a single turn, in both directions, are still bearings.
        OrientationStatistics.Fold(-10d).ShouldBe(170d, 1e-12);
        OrientationStatistics.Fold(730d).ShouldBe(10d, 1e-12);

        // A bearing a hair below zero must not round onto the excluded upper edge.
        OrientationStatistics.Fold(-1e-15).ShouldBe(0d);
        OrientationStatistics.Fold(-1e-15).ShouldBeLessThan(180d);
    }

    [Fact]
    public void A_sample_that_straddles_north_trends_north_and_not_across_it()
    {
        // Three legs at 350, 010 and 020. Worked by hand: doubling gives 340, 020 and 040; the
        // unit vectors sum to (2.6454, 0.6428), whose direction is 13.657 degrees, and half of
        // that is the trend. The arithmetic mean of 350, 10 and 20 is 126.67 — an answer 120
        // degrees away from the passage, which is what this test exists to catch.
        var summary = OrientationStatistics.Summarize([At(350d), At(10d), At(20d)]);

        summary.ByCount.MeanAxisDegrees!.Value.ShouldBe(6.8285, 0.001);
        summary.ByCount.ResultantLength.ShouldBe(0.90747, 0.00001);

        AxialGap(summary.ByCount.MeanAxisDegrees!.Value, 126.6667d)
            .ShouldBeGreaterThan(60d, "averaging the bearings themselves would land near 127 degrees");
    }

    [Fact]
    public void Opposite_bearings_are_one_perfectly_concentrated_trend_and_not_two_opposed_ones()
    {
        // A directional mean would cancel these to nothing. Axially they are the same passage.
        var summary = OrientationStatistics.Summarize([At(10d), At(190d)]);

        summary.ByCount.ResultantLength.ShouldBe(1d, 1e-12);
        AxialGap(summary.ByCount.MeanAxisDegrees!.Value, 10d).ShouldBeLessThan(1e-9);
    }

    [Fact]
    public void The_rayleigh_probability_matches_a_published_critical_value()
    {
        // The tabulated Rayleigh critical value at the five per cent level for ten observations is
        // Z = 2.945. A sample built to sit exactly on it must come back with a probability of
        // about five per cent — the series approximation puts it at 0.0486, which is the closest
        // a closed form gets to a table read off a distribution.
        const double criticalZ = 2.945d;
        const int n = 10;

        // Five legs either side of a north-south axis, placed so the resultant is sqrt(Z/n): with
        // half the sample at +phi and half at -phi in doubled space the resultant is cos(phi).
        var half = Math.Acos(Math.Sqrt(criticalZ / n)) * 180d / Math.PI / 2d;
        var samples = Enumerable.Repeat(At(half), 5).Concat(Enumerable.Repeat(At(180d - half), 5));

        var measure = OrientationStatistics.Summarize(samples).ByCount;

        measure.EffectiveSampleSize.ShouldBe(n, 1e-9);
        measure.RayleighZ!.Value.ShouldBe(criticalZ, 1e-9);
        measure.RayleighP!.Value.ShouldBe(0.0486, 0.0005);
        measure.RayleighP!.Value.ShouldBe(0.05, 0.003, "the five per cent table value it was built from");

        // The two limbs are symmetric about north, so that is the trend.
        AxialGap(measure.MeanAxisDegrees!.Value, 0d).ShouldBeLessThan(1e-6);
    }

    [Fact]
    public void Passage_running_every_way_equally_has_no_trend_and_the_test_says_so()
    {
        // One leg in the middle of each of the eighteen sectors.
        var samples = Enumerable.Range(0, OrientationStatistics.BinCount).Select(i => At(5d + (10d * i)));

        var summary = OrientationStatistics.Summarize(samples);

        summary.ByCount.ResultantLength.ShouldBe(0d, 1e-12);
        summary.ByCount.MeanAxisDegrees.ShouldBeNull("a resultant of zero points nowhere");
        summary.ByCount.RayleighP!.Value.ShouldBe(1d, 1e-9);

        // Evenly spread across every sector is the most disordered a rose can be.
        summary.ByCount.EntropyNats!.Value.ShouldBe(Math.Log(18d), 1e-12);
        summary.ByCount.EntropyNormalized!.Value.ShouldBe(1d, 1e-12);
    }

    [Fact]
    public void All_the_passage_in_one_sector_is_no_disorder_at_all()
    {
        var summary = OrientationStatistics.Summarize([At(42d), At(44d), At(46d)]);

        summary.ByCount.EntropyNats!.Value.ShouldBe(0d, 1e-12);
        summary.ByCount.EntropyNormalized!.Value.ShouldBe(0d, 1e-12);
        summary.Bins[4].Count.ShouldBe(3);
    }

    [Fact]
    public void Counting_legs_and_counting_metres_are_different_roses()
    {
        // Nine two-metre zig-zags running east and one hundred metres of straight gallery running
        // north. By leg the cave runs east; by passage it runs north. Both answers are correct and
        // they are not the same answer, which is why both are published.
        var samples = Enumerable.Repeat(At(90d, 2d), 9).Append(At(0d, 100d));

        var summary = OrientationStatistics.Summarize(samples);

        summary.SampleCount.ShouldBe(10);
        summary.TotalLengthM.ShouldBe(118d, 1e-9);

        AxialGap(summary.ByCount.MeanAxisDegrees!.Value, 90d).ShouldBeLessThan(1e-9);
        AxialGap(summary.ByLength.MeanAxisDegrees!.Value, 0d).ShouldBeLessThan(1e-9);

        // Counting legs, the sample is ten observations. Counting metres it is barely more than
        // one, because one leg carries almost all of the length — so the Rayleigh test, which
        // needs at least two independent observations, is refused rather than answered.
        summary.ByCount.EffectiveSampleSize.ShouldBe(10d, 1e-9);
        summary.ByCount.RayleighP!.Value.ShouldBeLessThan(0.01);
        summary.ByLength.EffectiveSampleSize.ShouldBeLessThan(2d);
        summary.ByLength.RayleighZ.ShouldBeNull();
        summary.ByLength.RayleighP.ShouldBeNull();
    }

    [Fact]
    public void The_two_weightings_share_one_set_of_sectors()
    {
        var summary = OrientationStatistics.Summarize([At(5d, 1d), At(95d, 3d)]);

        summary.Bins.Count.ShouldBe(18);
        summary.Bins[0].FromDegrees.ShouldBe(0d);
        summary.Bins[0].ToDegrees.ShouldBe(10d);
        summary.Bins[17].FromDegrees.ShouldBe(170d);
        summary.Bins[17].ToDegrees.ShouldBe(180d);

        summary.Bins[0].Count.ShouldBe(1);
        summary.Bins[0].LengthM.ShouldBe(1d, 1e-12);
        summary.Bins[0].CountFraction.ShouldBe(0.5d, 1e-12);
        summary.Bins[0].LengthFraction.ShouldBe(0.25d, 1e-12);

        summary.Bins[9].Count.ShouldBe(1);
        summary.Bins[9].LengthFraction.ShouldBe(0.75d, 1e-12);

        summary.Bins.Sum(b => b.Count).ShouldBe(2);
        summary.Bins.Sum(b => b.LengthM).ShouldBe(4d, 1e-12);
        summary.Bins.Sum(b => b.CountFraction).ShouldBe(1d, 1e-12);
        summary.Bins.Sum(b => b.LengthFraction).ShouldBe(1d, 1e-12);
    }

    [Fact]
    public void A_bearing_lands_in_the_sector_its_folded_trend_belongs_to()
    {
        OrientationStatistics.BinIndexOf(0d).ShouldBe(0);
        OrientationStatistics.BinIndexOf(9.999d).ShouldBe(0);
        OrientationStatistics.BinIndexOf(10d).ShouldBe(1);
        OrientationStatistics.BinIndexOf(179.999d).ShouldBe(17);

        // Bearings past the half-circle land on the sector of the trend they describe.
        OrientationStatistics.BinIndexOf(180d).ShouldBe(0);
        OrientationStatistics.BinIndexOf(185d).ShouldBe(0);
        OrientationStatistics.BinIndexOf(350d).ShouldBe(17);
        OrientationStatistics.BinIndexOf(-5d).ShouldBe(17);
    }

    [Fact]
    public void A_cave_with_nothing_measured_is_answered_rather_than_refused()
    {
        var summary = OrientationStatistics.Summarize([]);

        summary.SampleCount.ShouldBe(0);
        summary.TotalLengthM.ShouldBe(0d);
        summary.Bins.Count.ShouldBe(18, "an empty rose still has its axis");
        summary.Bins.ShouldAllBe(b => b.Count == 0 && b.LengthM == 0d);

        foreach (var measure in new[] { summary.ByCount, summary.ByLength })
        {
            measure.MeanAxisDegrees.ShouldBeNull();
            measure.ResultantLength.ShouldBe(0d);
            measure.RayleighZ.ShouldBeNull();
            measure.RayleighP.ShouldBeNull();
            measure.EntropyNats.ShouldBeNull();
            measure.EntropyNormalized.ShouldBeNull();
        }
    }

    [Fact]
    public void Legs_of_no_length_are_counted_but_weigh_nothing()
    {
        // A survey export contains legs between coincident stations. They are observations of a
        // direction and no passage at all, so the count-weighted answer keeps them and the
        // length-weighted one has nothing to work with.
        var summary = OrientationStatistics.Summarize([At(30d, 0d), At(150d, 0d)]);

        summary.SampleCount.ShouldBe(2);
        summary.TotalLengthM.ShouldBe(0d);
        summary.ByCount.MeanAxisDegrees.ShouldNotBeNull();
        summary.ByLength.MeanAxisDegrees.ShouldBeNull();
        summary.ByLength.EntropyNats.ShouldBeNull();
        summary.Bins.Sum(b => b.LengthFraction).ShouldBe(0d);
    }

    [Fact]
    public void A_length_that_cannot_be_a_length_is_refused_rather_than_folded_into_the_answer()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => OrientationStatistics.Summarize([At(30d, -1d)]));
        Should.Throw<ArgumentOutOfRangeException>(
            () => OrientationStatistics.Summarize([At(30d, double.NaN)]));
        Should.Throw<ArgumentOutOfRangeException>(
            () => OrientationStatistics.Summarize([At(double.NaN, 1d)]));
    }
}
