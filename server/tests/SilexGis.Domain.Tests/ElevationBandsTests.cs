// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The level-band arithmetic. Half of what these pin is that it finds the levels that are there;
/// the other half — and the more important half — is that it refuses to find levels that are not.
/// A classifier asked only for its best partition always has one, so the tests that matter most
/// here are the ones where the right answer is nothing.
/// </summary>
public class ElevationBandsTests
{
    [Fact]
    public void The_break_lands_where_it_was_worked_out_by_hand()
    {
        // Six heights in two obvious groups, small enough to classify on paper. Minimising the
        // within-group sum of squares, the two-group optimum is {1,2,3} | {10,11,12}: each group
        // has a mean at its middle and a sum of squares of 2, for a total of 4. Every other cut
        // scores worse — {1,2} | {3,10,11,12} is 0.5 + 50, and {1,2,3,10} | {11,12} is 50 + 0.5 —
        // so if the implementation is doing anything other than the exact optimum this moves.
        var proposal = ElevationBands.Summarize(
            [new(1, 1), new(2, 1), new(3, 1), new(10, 1), new(11, 1), new(12, 1)]);

        proposal.Bands.Count.ShouldBe(2);
        proposal.Bands[0].FromM.ShouldBe(1);
        proposal.Bands[0].ToM.ShouldBe(3);
        proposal.Bands[1].FromM.ShouldBe(10);
        proposal.Bands[1].ToM.ShouldBe(12);

        // The whole set has a mean of 6.5 and a sum of squares of 125.5, so the fit the partition
        // achieves is 1 - 4 / 125.5.
        proposal.GoodnessOfVarianceFit.ShouldNotBeNull().ShouldBe(1d - (4d / 125.5d), tolerance: 1e-9);
    }

    [Fact]
    public void A_cave_with_two_storeys_is_proposed_as_two_storeys()
    {
        // Passage measured on two levels a hundred and ninety metres apart, which is what a cave
        // cut at two base levels looks like in its elevation histogram.
        var proposal = ElevationBands.Summarize(
        [
            new(1000, 40), new(1002, 55), new(1004, 30), new(1007, 60), new(1010, 25),
            new(1200, 35), new(1203, 45), new(1205, 50), new(1208, 20), new(1210, 40),
        ]);

        proposal.Bands.Count.ShouldBe(2);
        proposal.Bands[0].FromM.ShouldBe(1000);
        proposal.Bands[0].ToM.ShouldBe(1010);
        proposal.Bands[0].Count.ShouldBe(5);
        proposal.Bands[1].FromM.ShouldBe(1200);
        proposal.Bands[1].ToM.ShouldBe(1210);
        proposal.Bands[1].Count.ShouldBe(5);

        // Every metre of passage is inside one band or the other; the bands are a partition of the
        // measurements, not a selection from them.
        proposal.Bands.Sum(b => b.WeightFraction).ShouldBe(1, tolerance: 1e-9);
        proposal.Bands.Sum(b => b.Count).ShouldBe(proposal.SampleCount);
    }

    [Fact]
    public void Heights_spread_evenly_are_reported_as_no_storeys_rather_than_as_the_best_available_cut()
    {
        // Twenty-one heights a metre apart: a cave on a slope, with no level anywhere in it. The
        // best two-way cut of this still explains three quarters of the variance, which is why the
        // fit cannot be what decides. Nothing is proposed, and nothing is the true answer.
        var samples = Enumerable.Range(0, 21).Select(i => new ElevationSample(i, 1));
        var proposal = ElevationBands.Summarize(samples);

        proposal.Bands.ShouldBeEmpty();
        proposal.GoodnessOfVarianceFit.ShouldBeNull();

        // The histogram is still answered — refusing to name levels is not refusing to draw the
        // distribution, and the reader is the one who decides there are no levels in it.
        proposal.SampleCount.ShouldBe(21);
        proposal.Bins.ShouldNotBeEmpty();
        proposal.Bins.Sum(b => b.Count).ShouldBe(21);
    }

    [Fact]
    public void A_large_evenly_spread_set_is_still_reported_as_no_storeys()
    {
        // The gap that occurs by chance in an evenly spread population grows with the sample size,
        // so the spacing rule alone would eventually admit one. Four hundred heights over four
        // hundred metres: the widest chance gap here is a few metres and the floor under the rule
        // is twenty, so it is refused on the share of the range rather than on the spacing. Both
        // guards are load-bearing and they cover different sample sizes.
        var samples = Enumerable.Range(0, 400).Select(i => new ElevationSample(i * 1.0, 1));
        var proposal = ElevationBands.Summarize(samples);

        proposal.Bands.ShouldBeEmpty();
    }

    [Fact]
    public void A_cave_on_one_level_is_proposed_as_no_levels_and_not_as_one()
    {
        // One band spanning everything is never returned, because a reader cannot tell it apart
        // from "nothing found" and only one of the two would be true.
        var proposal = ElevationBands.Summarize(
            [new(820, 12), new(821, 30), new(822, 8), new(823, 41), new(824, 19), new(825, 6)]);

        proposal.Bands.ShouldBeEmpty();
        proposal.LowestM.ShouldBe(820);
        proposal.HighestM.ShouldBe(825);
    }

    [Fact]
    public void One_stray_measurement_far_above_the_cave_is_not_a_storey()
    {
        // A single leg two hundred metres up separates cleanly, so the gap rule alone would take
        // it. It is refused because a band needs two distinct heights of its own: a level is a
        // range that is occupied, and one measurement is an outlier the classification had nowhere
        // else to put.
        var proposal = ElevationBands.Summarize(
        [
            new(500, 20), new(502, 25), new(504, 30), new(506, 15), new(508, 22), new(510, 18),
            new(760, 4),
        ]);

        proposal.Bands.ShouldBeEmpty();
    }

    [Fact]
    public void Two_stray_measurements_far_above_the_cave_are_a_storey()
    {
        // The same shape as the previous test with one more measurement up there. This is the
        // positive half of the two-distinct-heights rule: the rule refuses a band of one, not a
        // band that is merely small, and without this the previous test would also pass for an
        // implementation that had simply stopped proposing anything.
        var proposal = ElevationBands.Summarize(
        [
            new(500, 20), new(502, 25), new(504, 30), new(506, 15), new(508, 22), new(510, 18),
            new(760, 4), new(763, 6),
        ]);

        proposal.Bands.Count.ShouldBe(2);
        proposal.Bands[1].FromM.ShouldBe(760);
        proposal.Bands[1].ToM.ShouldBe(763);
        proposal.Bands[1].Count.ShouldBe(2);
    }

    [Fact]
    public void More_separated_groups_than_the_cap_allows_are_proposed_as_the_cap()
    {
        // Six clearly separated groups. The proposal stops at five, because it is read by a person
        // and because the exact search costs the square of the number of distinct heights. What the
        // cap must not do is fail: the answer is still five real bands, not none.
        var samples = new List<ElevationSample>();
        foreach (var floorM in (double[])[0, 100, 200, 300, 400, 500])
        {
            samples.Add(new ElevationSample(floorM, 10));
            samples.Add(new ElevationSample(floorM + 1, 10));
            samples.Add(new ElevationSample(floorM + 2, 10));
        }

        var proposal = ElevationBands.Summarize(samples);

        proposal.Bands.Count.ShouldBe(ElevationBands.MaximumBands);
        proposal.Bands.Sum(b => b.Count).ShouldBe(18);
        proposal.Bands.ShouldBeInOrder(SortDirection.Ascending, Comparer<ElevationBand>.Create(
            static (a, b) => a.FromM.CompareTo(b.FromM)));
    }

    [Fact]
    public void A_band_is_weighed_in_metres_of_passage_and_not_in_number_of_legs()
    {
        // Two storeys: the lower one is one long gallery surveyed in three legs, the upper one is a
        // maze of eight short ones. By leg count the upper level is the cave; by passage length the
        // lower one is, and length is what a hypsometry answers in.
        var samples = new List<ElevationSample>
        {
            new(300, 400), new(302, 380), new(304, 420),
        };
        for (var i = 0; i < 8; i++)
        {
            samples.Add(new ElevationSample(600 + i, 5));
        }

        var proposal = ElevationBands.Summarize(samples);

        proposal.Bands.Count.ShouldBe(2);
        proposal.Bands[0].Count.ShouldBe(3);
        proposal.Bands[1].Count.ShouldBe(8);
        proposal.Bands[0].WeightM.ShouldBe(1200, tolerance: 1e-9);
        proposal.Bands[1].WeightM.ShouldBe(40, tolerance: 1e-9);
        proposal.Bands[0].WeightFraction.ShouldBeGreaterThan(0.9);
    }

    [Fact]
    public void Sector_edges_are_round_numbers_so_two_caves_can_be_read_against_each_other()
    {
        // A width taken straight from the data would give edges like 1207.3, which nobody can
        // compare between caves and which move when one leg moves.
        ElevationBands.BinWidthFor(1000, 1210).ShouldBe(10);
        ElevationBands.BinWidthFor(0, 700).ShouldBe(50);
        ElevationBands.BinWidthFor(0, 20).ShouldBe(1);
        ElevationBands.BinWidthFor(400, 400).ShouldBe(1);

        var proposal = ElevationBands.Summarize(
            [new(1005, 1), new(1123, 1), new(1210, 1)]);

        proposal.BinWidthM.ShouldBe(10);
        proposal.Bins[0].FromM.ShouldBe(1000);
        proposal.Bins.Count.ShouldBeLessThanOrEqualTo(ElevationBands.MaximumBinCount + 1);
    }

    [Fact]
    public void The_highest_measurement_is_inside_the_histogram_and_not_a_sector_of_its_own()
    {
        // Left open at the top, the single highest measurement lands in an otherwise empty sector
        // above everything else — which reads as a level, and is an artefact of the arithmetic.
        var proposal = ElevationBands.Summarize(
            [new(1000, 1), new(1050, 1), new(1100, 1)]);

        proposal.Bins[^1].Count.ShouldBeGreaterThan(0);
        proposal.Bins.Sum(b => b.Count).ShouldBe(3);
        proposal.Bins.Sum(b => b.CountFraction).ShouldBe(1, tolerance: 1e-9);
    }

    [Fact]
    public void Nothing_to_measure_answers_nothing_rather_than_zeroes()
    {
        var proposal = ElevationBands.Summarize([]);

        proposal.SampleCount.ShouldBe(0);
        proposal.TotalWeightM.ShouldBe(0);
        proposal.LowestM.ShouldBeNull();
        proposal.HighestM.ShouldBeNull();
        proposal.Bins.ShouldBeEmpty();
        proposal.Bands.ShouldBeEmpty();
        proposal.GoodnessOfVarianceFit.ShouldBeNull();
    }

    [Fact]
    public void Measurements_that_are_not_numbers_are_dropped_rather_than_dragging_every_band_with_them()
    {
        var proposal = ElevationBands.Summarize(
        [
            new(double.NaN, 10), new(1, 1), new(2, 1), new(3, 1),
            new(double.PositiveInfinity, 10), new(10, 1), new(11, 1), new(12, 1),
        ]);

        proposal.SampleCount.ShouldBe(6);
        proposal.Bands.Count.ShouldBe(2);
        proposal.HighestM.ShouldBe(12);
    }

    [Fact]
    public void Measurements_with_no_length_at_all_still_answer_rather_than_dividing_by_zero()
    {
        // Entrance and spring altitudes arrive weightless. The bands then come out of the heights
        // alone, which is the count-based answer, and the fractions are zero rather than not a
        // number.
        var proposal = ElevationBands.Summarize(
            [new(400, 0), new(401, 0), new(402, 0), new(900, 0), new(901, 0), new(902, 0)]);

        proposal.TotalWeightM.ShouldBe(0);
        proposal.Bands.Count.ShouldBe(2);
        proposal.Bands.ShouldAllBe(b => b.WeightFraction == 0);
        proposal.Bins.ShouldAllBe(b => b.WeightFraction == 0);
        proposal.Bins.Sum(b => b.CountFraction).ShouldBe(1, tolerance: 1e-9);
    }

    [Fact]
    public void Thousands_of_legs_are_classified_without_the_search_growing_without_bound()
    {
        // A survey of this size has more distinct mid-heights than the exact search may work on, so
        // they are collapsed onto an even ladder first. What must survive the collapse is the
        // answer: two storeys, at the heights they were measured at.
        var samples = new List<ElevationSample>();
        for (var i = 0; i < 3000; i++)
        {
            samples.Add(new ElevationSample(1000 + (i * 0.01), 2));
            samples.Add(new ElevationSample(1400 + (i * 0.01), 3));
        }

        var proposal = ElevationBands.Summarize(samples);

        proposal.SampleCount.ShouldBe(6000);
        proposal.Bands.Count.ShouldBe(2);
        proposal.Bands[0].FromM.ShouldBe(1000, tolerance: 1e-6);
        proposal.Bands[0].ToM.ShouldBe(1029.99, tolerance: 1e-6);
        proposal.Bands[1].FromM.ShouldBe(1400, tolerance: 1e-6);
        proposal.Bands[0].WeightM.ShouldBe(6000, tolerance: 1e-6);
        proposal.Bands[1].WeightM.ShouldBe(9000, tolerance: 1e-6);
    }
}
