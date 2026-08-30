// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The dip arithmetic. What these pin is that inclination is treated as the linear, signed
/// quantity it is — not folded, not doubled, not wrapped — because the orientation arithmetic
/// sitting beside it does all three, and applying either one's rules to the other's data gives a
/// plausible number that is wrong.
/// </summary>
public class DipStatisticsTests
{
    [Fact]
    public void A_climb_and_a_descent_of_the_same_steepness_are_not_the_same_passage()
    {
        // The distinction the sign carries. Folded or made absolute, these two would be one
        // answer: a cave of 30-degree passage. Kept signed they are what they are — a ramp up and
        // a ramp down, averaging level, each of them steep.
        var summary = DipStatistics.Summarize([new(30, 10), new(-30, 10)]);

        summary.MeanDipDegrees.ShouldNotBeNull().ShouldBe(0, tolerance: 1e-9);
        summary.MeanAbsoluteDipDegrees.ShouldNotBeNull().ShouldBe(30, tolerance: 1e-9);
        summary.MaximumDipDegrees.ShouldBe(30);
        summary.MinimumDipDegrees.ShouldBe(-30);
    }

    [Fact]
    public void A_cave_whose_every_leg_climbs_reports_two_climbs_and_calls_neither_of_them_a_descent()
    {
        // Every leg rises, so there is no descending passage to report the steepest of. The two
        // extremes are still both climbs, and both come back positive — which is only readable
        // because the fields say they are the least and greatest inclination rather than the
        // steepest descent and the steepest ascent. A field promising a descent and handing back
        // a climb is a number nothing downstream can tell is wrong.
        var summary = DipStatistics.Summarize([new(2.29, 50), new(5, 50), new(11, 50)]);

        summary.MinimumDipDegrees.ShouldBe(2.29);
        summary.MaximumDipDegrees.ShouldBe(11);
        summary.MeanAbsoluteDipDegrees.ShouldNotBeNull().ShouldBeGreaterThan(0);
    }

    [Fact]
    public void The_mean_is_weighted_by_metres_of_passage_not_by_number_of_legs()
    {
        // Four legs down one pitch and one leg along a gallery. By count this cave is steep; by
        // length it is nearly level, and the length answer is the one that describes the cave.
        var summary = DipStatistics.Summarize(
        [
            new(-80, 5), new(-80, 5), new(-80, 5), new(-80, 5), new(0, 400),
        ]);

        summary.SampleCount.ShouldBe(5);
        summary.TotalLengthM.ShouldBe(420, tolerance: 1e-9);

        // 4 x 5 m at -80 degrees over 420 m of passage.
        summary.MeanDipDegrees.ShouldNotBeNull().ShouldBe(-80d * 20d / 420d, tolerance: 1e-9);
        summary.MeanDipDegrees.ShouldNotBeNull().ShouldBeInRange(-4.0, -3.5);
    }

    [Fact]
    public void Every_sector_of_the_histogram_is_present_and_the_two_weightings_each_sum_to_one()
    {
        var summary = DipStatistics.Summarize([new(-85, 10), new(5, 30), new(85, 60)]);

        summary.Bins.Count.ShouldBe(18);
        summary.Bins[0].FromDegrees.ShouldBe(-90);
        summary.Bins[17].ToDegrees.ShouldBe(90);
        summary.Bins.Select(b => b.FromDegrees).ShouldBeInOrder();

        summary.Bins[0].Count.ShouldBe(1);
        summary.Bins[9].Count.ShouldBe(1);
        summary.Bins[17].Count.ShouldBe(1);
        summary.Bins.Count(b => b.Count == 0).ShouldBe(15);

        summary.Bins.Sum(b => b.CountFraction).ShouldBe(1, tolerance: 1e-9);
        summary.Bins.Sum(b => b.LengthFraction).ShouldBe(1, tolerance: 1e-9);
        summary.Bins[17].LengthFraction.ShouldBe(0.6, tolerance: 1e-9);
    }

    [Fact]
    public void A_vertical_pitch_lands_in_the_last_sector_rather_than_opening_a_nineteenth()
    {
        DipStatistics.BinIndexOf(90).ShouldBe(17);
        DipStatistics.BinIndexOf(-90).ShouldBe(0);
        DipStatistics.BinIndexOf(0).ShouldBe(9);
        DipStatistics.BinIndexOf(-0.001).ShouldBe(8);

        // Out of range is a caller error; it is clamped into the range rather than binned wherever
        // the arithmetic happens to land.
        DipStatistics.BinIndexOf(400).ShouldBe(17);
        DipStatistics.BinIndexOf(-400).ShouldBe(0);
    }

    [Fact]
    public void Nothing_to_measure_answers_nulls_and_a_full_empty_histogram()
    {
        var summary = DipStatistics.Summarize([]);

        summary.SampleCount.ShouldBe(0);
        summary.TotalLengthM.ShouldBe(0);
        summary.MeanDipDegrees.ShouldBeNull();
        summary.MeanAbsoluteDipDegrees.ShouldBeNull();
        summary.MaximumDipDegrees.ShouldBeNull();
        summary.MinimumDipDegrees.ShouldBeNull();

        // A histogram with no holes in it, so a caller drawing it has nothing to special-case.
        summary.Bins.Count.ShouldBe(18);
        summary.Bins.ShouldAllBe(b => b.Count == 0 && b.LengthM == 0);
    }

    [Fact]
    public void Legs_with_no_length_at_all_still_answer_rather_than_dividing_by_zero()
    {
        var summary = DipStatistics.Summarize([new(40, 0), new(20, 0)]);

        summary.TotalLengthM.ShouldBe(0);
        summary.MeanDipDegrees.ShouldNotBeNull().ShouldBe(30, tolerance: 1e-9);
        summary.Bins.Sum(b => b.CountFraction).ShouldBe(1, tolerance: 1e-9);
        summary.Bins.ShouldAllBe(b => b.LengthFraction == 0);
    }
}
