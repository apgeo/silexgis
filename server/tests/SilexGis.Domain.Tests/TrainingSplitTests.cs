// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Potential;
using SilexGis.Domain.Statistics;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The seeded parting of occupied cells, held to a second implementation of the generator and
/// the draw written outside this code, so a stated seed is shown to mean the same split anywhere.
/// </summary>
public class TrainingSplitTests
{
    private static readonly int[] TwentyCells = [.. Enumerable.Range(0, 20).Select(k => (3 * k) + 1)];

    [Fact]
    public void The_generator_gives_the_numbers_of_its_seed()
    {
        var random = new DeterministicRandom(42);
        random.NextDouble().ShouldBe(0.0732434786478745, 1e-16);
        random.NextDouble().ShouldBe(0.7969781808377971, 1e-16);
        random.NextDouble().ShouldBe(0.9255126349256994, 1e-16);

        var negative = new DeterministicRandom(-7);
        negative.NextDouble().ShouldBe(0.43902550638283144, 1e-16);
        negative.NextDouble().ShouldBe(0.10462261616631352, 1e-16);
    }

    [Fact]
    public void A_stated_seed_and_share_give_the_split_worked_out_independently()
    {
        var split = TrainingSplits.Of(TwentyCells, 0.3, 2026)!;

        split.HeldOut.ShouldBe([4, 7, 34, 37, 52, 55]);
        split.Training.ShouldBe([1, 10, 13, 16, 19, 22, 25, 28, 31, 40, 43, 46, 49, 58]);

        TrainingSplits.Of(TwentyCells, 0.3, 1)!.HeldOut.ShouldBe([1, 10, 19, 25, 49, 58]);
    }

    [Fact]
    public void The_split_does_not_depend_on_the_order_the_cells_arrive_in()
    {
        var forwards = TrainingSplits.Of(TwentyCells, 0.3, 2026)!;
        var backwards = TrainingSplits.Of([.. TwentyCells.Reverse()], 0.3, 2026)!;

        backwards.HeldOut.ShouldBe(forwards.HeldOut);
        backwards.Training.ShouldBe(forwards.Training);
    }

    [Theory]
    [InlineData(20, 0.1, 2)]
    [InlineData(20, 0.5, 10)]
    [InlineData(10, 0.25, 3)]  // two and a half cells are three
    [InlineData(10, 0.1, 1)]
    [InlineData(37, 0.3, 11)]
    public void The_held_out_part_is_the_share_to_the_nearest_cell_and_the_two_parts_are_the_whole(
        int count, double share, int expected)
    {
        int[] cells = [.. Enumerable.Range(100, count)];

        var split = TrainingSplits.Of(cells, share, 7)!;

        split.HeldOut.Count.ShouldBe(expected);
        split.Training.Intersect(split.HeldOut).ShouldBeEmpty();
        split.Training.Concat(split.HeldOut).Order().ShouldBe(cells);
        split.Training.ShouldBe(split.Training.Order());
        split.HeldOut.ShouldBe(split.HeldOut.Order());
    }

    [Fact]
    public void The_ten_cell_split_matches_the_independent_draw()
    {
        var split = TrainingSplits.Of([.. Enumerable.Range(0, 10)], 0.25, 5)!;

        split.HeldOut.ShouldBe([1, 3, 7]);
        split.Training.ShouldBe([0, 2, 4, 5, 6, 8, 9]);
    }

    [Fact]
    public void Fewer_cells_than_the_minimum_are_not_split()
    {
        TrainingSplits.Of([.. Enumerable.Range(0, TrainingSplits.MinimumCells - 1)], 0.3, 1).ShouldBeNull();
        TrainingSplits.Of([.. Enumerable.Range(0, TrainingSplits.MinimumCells)], 0.3, 1).ShouldNotBeNull();
        TrainingSplits.Of([], 0.3, 1).ShouldBeNull();
    }

    [Theory]
    [InlineData(0.09)]
    [InlineData(0.51)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(double.NaN)]
    public void A_share_outside_the_range_is_refused(double share)
    {
        TrainingSplits.IsAllowedShare(share).ShouldBeFalse();
        Should.Throw<ArgumentOutOfRangeException>(() => TrainingSplits.Of(TwentyCells, share, 1));
    }

    [Fact]
    public void The_stated_bounds_and_default_are_allowed_and_a_cell_named_twice_is_refused()
    {
        TrainingSplits.IsAllowedShare(TrainingSplits.MinHeldOutShare).ShouldBeTrue();
        TrainingSplits.IsAllowedShare(TrainingSplits.MaxHeldOutShare).ShouldBeTrue();
        TrainingSplits.IsAllowedShare(TrainingSplits.DefaultHeldOutShare).ShouldBeTrue();
        Should.Throw<ArgumentException>(() => TrainingSplits.Of([.. TwentyCells, 4], 0.3, 1));
    }
}
