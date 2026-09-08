// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Statistics;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What a histogram is allowed to publish.
///
/// <para>
/// The rule under test is a disclosure rule wearing a presentation rule's clothes: a sector holding
/// one or two caves is joined to its neighbour rather than removed, because an even axis with a
/// missing sector announces that something rare sits exactly there — and on an axis of lengths or
/// depths that announcement is narrow enough to name a cave.
/// </para>
/// </summary>
public class DistributionBinTests
{
    [Fact]
    public void A_sector_holding_too_few_is_joined_to_its_neighbour_and_nothing_is_lost()
    {
        IReadOnlyList<DistributionBin> raw =
        [
            new(0, 100, 40, false),
            new(100, 200, 1, false),
            new(200, 300, 30, false),
        ];

        var merged = DistributionBins.Merge(raw, minimumCount: 5);

        merged.Count.ShouldBe(2);
        merged.Sum(b => b.Count).ShouldBe(71);

        // The thin sector was joined rightwards, so the axis reads 0–100 and 100–300 and every
        // observation is still somewhere.
        merged[0].ShouldBe(new DistributionBin(0, 100, 40, false));
        merged[1].ShouldBe(new DistributionBin(100, 300, 31, true));

        // Nothing published sits between one and the floor, which is the property the rule exists
        // for; and no sector was dropped, which is the property that makes it safe.
        merged.ShouldAllBe(b => b.Count == 0 || b.Count >= 5);
    }

    [Fact]
    public void An_empty_sector_stands_because_it_describes_nothing_and_names_nobody()
    {
        IReadOnlyList<DistributionBin> raw =
        [
            new(0, 100, 40, false),
            new(100, 200, 0, false),
            new(200, 300, 30, false),
        ];

        var merged = DistributionBins.Merge(raw, minimumCount: 5);

        // Untouched: the gap between the short caves and the long ones is the shape of the
        // registry, and joining it away would hide the one thing the histogram was drawn for.
        merged.ShouldBe(raw);
    }

    [Fact]
    public void A_thin_sector_at_the_top_of_the_range_is_joined_downwards()
    {
        IReadOnlyList<DistributionBin> raw =
        [
            new(0, 100, 40, false),
            new(100, 200, 20, false),
            new(200, 300, 2, false),
        ];

        var merged = DistributionBins.Merge(raw, minimumCount: 5);

        merged.Count.ShouldBe(2);
        merged[1].ShouldBe(new DistributionBin(100, 300, 22, true));
    }

    [Fact]
    public void A_run_of_thin_sectors_is_joined_until_every_one_of_them_clears_the_floor()
    {
        IReadOnlyList<DistributionBin> raw =
        [
            new(0, 10, 1, false),
            new(10, 20, 1, false),
            new(20, 30, 1, false),
            new(30, 40, 1, false),
            new(40, 50, 1, false),
            new(50, 60, 1, false),
            new(60, 70, 40, false),
        ];

        var merged = DistributionBins.Merge(raw, minimumCount: 5);

        merged.Sum(b => b.Count).ShouldBe(46);
        merged.ShouldAllBe(b => b.Count == 0 || b.Count >= 5);

        // The joining stops as soon as a sector clears the floor, so the first five singletons
        // make a sector of five and the axis stays as fine as the rule allows; the sixth has
        // nothing thin left beside it and joins the populous sector above it.
        merged.Count.ShouldBe(2);
        merged[0].ShouldBe(new DistributionBin(0, 50, 5, true));
        merged[1].ShouldBe(new DistributionBin(50, 70, 41, true));
    }

    [Fact]
    public void A_sample_smaller_than_the_floor_collapses_to_one_sector_rather_than_disappearing()
    {
        IReadOnlyList<DistributionBin> raw =
        [
            new(0, 100, 1, false),
            new(100, 200, 1, false),
            new(200, 300, 1, false),
        ];

        var merged = DistributionBins.Merge(raw, minimumCount: 5);

        // It says there is a range and three caves in it, and declines to say where in the range
        // they sit. That is less than was asked for and more than nothing.
        merged.Count.ShouldBe(1);
        merged[0].ShouldBe(new DistributionBin(0, 300, 3, true));
    }

    [Fact]
    public void A_floor_of_one_leaves_the_histogram_exactly_as_it_was()
    {
        IReadOnlyList<DistributionBin> raw =
        [
            new(0, 100, 1, false),
            new(100, 200, 0, false),
            new(200, 300, 3, false),
        ];

        DistributionBins.Merge(raw, minimumCount: 1).ShouldBe(raw);
        DistributionBins.Merge(raw, minimumCount: 0).ShouldBe(raw);
    }
}
