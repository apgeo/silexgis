// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Watson's two-sample U² against values worked by hand and against the published large-sample
/// critical values (Watson 1962; Zar, Biostatistical Analysis, the U² table's last row: 0.187 at
/// five per cent, 0.268 at one per cent, 0.385 at one in a thousand).
/// </summary>
public class WatsonU2Tests
{
    [Theory]
    [InlineData(0.187, 0.05)]
    [InlineData(0.268, 0.01)]
    [InlineData(0.385, 0.001)]
    public void The_probability_reproduces_the_tabulated_critical_values(double statistic, double p)
    {
        WatsonU2.Probability(statistic).ShouldBe(p, p * 0.03);
    }

    [Fact]
    public void Two_samples_wholly_apart_give_the_hand_worked_statistic()
    {
        // A = {10..50}, B = {60..100}, five each. Walking round: the gap climbs 0.2 a step to 1 and
        // falls back to 0, so Σd = 5 and Σd² = 2·(0.04 + 0.16 + 0.36 + 0.64) + 1 = 3.4. Then
        // U² = 25/100 · (3.4 − 25/10) = 0.225.
        double[] a = [10, 20, 30, 40, 50];
        double[] b = [60, 70, 80, 90, 100];

        var result = WatsonU2.Compare(a, b)!;

        result.Statistic.ShouldBe(0.225, 1e-12);
        result.FirstCount.ShouldBe(5);
        result.SecondCount.ShouldBe(5);
        result.PValue.ShouldBe(2d * (Math.Exp(-2d * Math.PI * Math.PI * 0.225) - Math.Exp(-8d * Math.PI * Math.PI * 0.225)), 1e-6);
        result.PValue.ShouldBeLessThan(0.05);
    }

    [Fact]
    public void Where_the_walk_starts_does_not_change_the_answer()
    {
        double[] a = [5, 47, 133, 200, 251, 300, 352];
        double[] b = [12, 60, 61, 170, 222, 290];

        var original = WatsonU2.Compare(a, b)!.Statistic;
        foreach (var shift in new[] { 17d, 90d, 181d, 333d })
        {
            var rotated = WatsonU2.Compare(
                a.Select(x => x + shift).ToList(), b.Select(x => x + shift).ToList())!.Statistic;
            rotated.ShouldBe(original, 1e-12);
        }
    }

    [Fact]
    public void Identical_samples_give_zero_and_certainty()
    {
        double[] a = [30, 30, 45, 120, 300];

        var result = WatsonU2.Compare(a, a)!;

        result.Statistic.ShouldBe(0d, 1e-15);
        result.PValue.ShouldBe(1d);
    }

    [Fact]
    public void A_tie_across_the_samples_moves_both_curves_at_once()
    {
        // Every value tied across the samples: the curves move together and never part.
        double[] a = [10, 20, 30, 40, 50];
        WatsonU2.Compare(a, a.Select(x => x + 360d).ToList())!.Statistic.ShouldBe(0d, 1e-15);
    }

    [Fact]
    public void Axial_trends_are_compared_as_axes()
    {
        // A passage surveyed 030 one way and 210 the other is one trend; strikes along it match.
        double[] passage = [28, 208, 31, 211, 30, 29];
        double[] strikes = [30, 30, 210, 29, 31];

        var along = WatsonU2.CompareAxial(passage, strikes)!;
        along.PValue.ShouldBeGreaterThan(0.05);

        // Strikes across the passage, at right angles to it, are told apart. Worked by hand on the
        // doubled angles — passage 56 56 58 60 62 62, strikes 236 238 240 240 242 244, with the
        // ties weighted: Σt·d = 37/6 and Σt·d² = 155/36, so U² = 36/144 · (155/36 − (37/6)²/12)
        // = 0.284144.
        double[] across = [118, 121, 300, 122, 119, 120];
        var acrossResult = WatsonU2.CompareAxial(passage, across)!;
        acrossResult.Statistic.ShouldBe(0.284144, 1e-6);
        acrossResult.PValue.ShouldBeLessThan(0.05);
    }

    [Fact]
    public void Too_small_a_sample_gives_no_answer()
    {
        WatsonU2.Compare([1, 2, 3, 4], [10, 20, 30, 40, 50]).ShouldBeNull();
        WatsonU2.Compare([1, 2, 3, 4, 5], [10, 20, 30, 40]).ShouldBeNull();
    }

    [Fact]
    public void A_negative_statistic_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => WatsonU2.Probability(-0.1));
    }
}
