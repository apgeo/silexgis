// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Potential;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Success-rate and prediction-rate curves, held to curves drawn by hand and to the closed form
/// of the area under a ranking.
/// </summary>
public class RateCurveTests
{
    [Fact]
    public void The_trapezoid_rule_gives_the_area_under_a_known_curve()
    {
        // (0,0) → (0.5,0.25) → (1,1): 0.5 × 0.125 + 0.5 × 0.625.
        RateCurves.AreaUnder([new(0, 0), new(0.5, 0.25), new(1, 1)]).ShouldBe(0.375, 1e-12);
        RateCurves.AreaUnder([new(0, 0), new(1, 1)]).ShouldBe(0.5, 1e-12);
        RateCurves.AreaUnder([new(0, 0)]).ShouldBe(0);
    }

    [Fact]
    public void A_map_of_three_values_has_the_curve_drawn_by_hand()
    {
        // Ten cells: two of the highest value holding one occupied cell, three of the middle value
        // holding the other, five of the lowest holding none.
        float[] values = [0.9f, 0.1f, 0.5f, 0.1f, 0.9f, 0.5f, 0.1f, 0.5f, 0.1f, 0.1f];

        var curve = RateCurves.Of(values, [4, 7])!;

        curve.Cells.ShouldBe(10);
        curve.Occurrences.ShouldBe(2);
        curve.Points.ShouldBe([new(0, 0), new(0.2, 0.5), new(0.5, 1), new(1, 1)]);
        // 0.2 × 0.25 + 0.3 × 0.75 + 0.5 × 1.
        curve.AreaUnderCurve.ShouldBe(0.775, 1e-12);
    }

    [Fact]
    public void Tied_cells_are_one_step_so_the_curve_does_not_depend_on_where_they_are_stored()
    {
        float[] values = [0.9f, 0.1f, 0.5f, 0.1f, 0.9f, 0.5f, 0.1f, 0.5f, 0.1f, 0.1f];
        var first = RateCurves.Of(values, [4, 7])!;

        // The same map with the occupied cells elsewhere among their ties.
        var second = RateCurves.Of(values, [0, 2])!;
        // And with the cells stored in the opposite order.
        var third = RateCurves.Of([.. values.Reverse()], [9, 4])!;

        second.Points.ShouldBe(first.Points);
        third.Points.ShouldBe(first.Points);
        second.AreaUnderCurve.ShouldBe(first.AreaUnderCurve);
    }

    [Fact]
    public void A_map_that_says_the_same_everywhere_ranks_no_better_than_chance()
    {
        var curve = RateCurves.Of(Enumerable.Repeat(0.3f, 50).ToArray(), [3, 17, 40])!;

        curve.Points.ShouldBe([new(0, 0), new(1, 1)]);
        curve.AreaUnderCurve.ShouldBe(0.5, 1e-12);
    }

    [Fact]
    public void A_perfect_ranking_falls_short_of_one_by_the_ground_the_occupied_cells_take()
    {
        // Ten distinct values, the two highest occupied: the curve reaches one at a fifth of the
        // ground, and the area is 1 − 2 / (2 × 10).
        float[] values = [.. Enumerable.Range(0, 10).Select(i => (float)i)];

        var curve = RateCurves.Of(values, [9, 8])!;

        curve.Points[1].ShouldBe(new RatePoint(0.1, 0.5));
        curve.Points[2].ShouldBe(new RatePoint(0.2, 1));
        curve.AreaUnderCurve.ShouldBe(0.9, 1e-12);
    }

    [Fact]
    public void Cells_without_a_value_are_neither_ground_nor_found()
    {
        float[] values = [0.9f, float.NaN, 0.5f, 0.1f, float.NaN, 0.1f];

        var curve = RateCurves.Of(values, [0, 1, 3])!;

        curve.Cells.ShouldBe(4);
        curve.Occurrences.ShouldBe(2);
        curve.Points.ShouldBe([new(0, 0), new(0.25, 0.5), new(0.5, 0.5), new(1, 1)]);

        RateCurves.Of(values, [1, 4]).ShouldBeNull();
        RateCurves.Of(values, []).ShouldBeNull();
        RateCurves.Of([float.NaN, float.NaN], [0]).ShouldBeNull();
        Should.Throw<ArgumentException>(() => RateCurves.Of(values, [6]));
    }

    [Fact]
    public void A_curve_of_many_steps_is_given_at_every_hundredth_and_keeps_its_exact_area()
    {
        // A thousand distinct values; the occupied cells are those ranked 1st, 11th, 21st, … from
        // the top — a hundred of them, evenly spread, so the curve hugs the diagonal from above.
        var values = new float[1_000];
        var occupied = new List<int>();
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = i;
            if ((values.Length - 1 - i) % 10 == 0)
            {
                occupied.Add(i);
            }
        }

        var curve = RateCurves.Of(values, occupied)!;

        curve.Points.Count.ShouldBe(RateCurves.MaxPoints);
        curve.Points[0].ShouldBe(new RatePoint(0, 0));
        curve.Points[^1].ShouldBe(new RatePoint(1, 1));
        for (var i = 1; i < curve.Points.Count; i++)
        {
            curve.Points[i].AreaShare.ShouldBe(i / 100d, 1e-12);
            curve.Points[i].OccurrenceShare.ShouldBeGreaterThanOrEqualTo(curve.Points[i - 1].OccurrenceShare);
        }

        // Every tenth cell from the first: at a share of i/100 exactly i of the hundred are found.
        curve.Points[37].OccurrenceShare.ShouldBe(0.37, 1e-12);

        // For distinct values the area is 1 − (sum of the occupied ranks − half their number) / (cells × occupied).
        // Ranks 1, 11, …, 991 sum to 49,600.
        curve.AreaUnderCurve.ShouldBe(1 - ((49_600 - 50) / (1_000d * 100)), 1e-12);
    }
}
