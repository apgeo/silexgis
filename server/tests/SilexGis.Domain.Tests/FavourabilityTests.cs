// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Potential;

namespace SilexGis.Domain.Tests;

/// <summary>Rescoring a layer's values to favourability, and combining the layers by weight.</summary>
public class FavourabilityTests
{
    private static readonly EvidenceLayer Slope = EvidenceLayers.Find(EvidenceLayers.Slope)!;
    private static readonly EvidenceLayer Aspect = EvidenceLayers.Find(EvidenceLayers.Aspect)!;

    [Theory]
    [InlineData(10, 0)]
    [InlineData(-5, 0)] // flat below the low bound
    [InlineData(20, 0.25)]
    [InlineData(30, 0.5)]
    [InlineData(50, 1)]
    [InlineData(80, 1)] // flat above the high bound
    public void A_rising_ramp_is_nought_at_the_low_bound_and_one_at_the_high(double value, double expected) =>
        Favourability.FuzzyLinear(10, 50, value).ShouldBe(expected, 1e-12);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(125, 0.75)]
    [InlineData(500, 0)]
    [InlineData(900, 0)]
    public void A_ramp_with_its_bounds_the_other_way_falls_as_the_value_rises(double value, double expected) =>
        // "The nearer a fault the better": nought at 500 m, one on the fault.
        Favourability.FuzzyLinear(500, 0, value).ShouldBe(expected, 1e-12);

    [Fact]
    public void A_ramp_between_two_equal_bounds_is_refused() =>
        Should.Throw<ArgumentException>(() => Favourability.FuzzyLinear(5, 5, 5));

    [Theory]
    [InlineData(-3, 0.1)]
    [InlineData(4.999, 0.1)]
    [InlineData(5, 0.6)] // a value on a bound belongs to the class that starts there
    [InlineData(14.9, 0.6)]
    [InlineData(15, 1)]
    [InlineData(89, 1)]
    public void A_value_scores_what_its_class_scores(double value, double expected)
    {
        IReadOnlyList<EvidenceClass> classes = [new(5, 0.1), new(15, 0.6), new(null, 1)];

        Favourability.Reclassify(classes, value).ShouldBe(expected);
    }

    [Fact]
    public void A_table_with_no_open_last_class_is_refused_for_a_value_above_it() =>
        Should.Throw<ArgumentException>(() => Favourability.Reclassify([new(5, 0.1), new(15, 0.6)], 20));

    [Fact]
    public void A_cell_with_no_measurement_scores_nothing_rather_than_nought()
    {
        var ramp = new EvidenceTransform(EvidenceTransformKind.FuzzyLinear, Low: 0, High: 30);

        Favourability.Score(Slope, ramp, double.NaN).ShouldBeNull();
        Favourability.Score(Slope, ramp, double.PositiveInfinity).ShouldBeNull();
        Favourability.Score(Slope, ramp, 15).ShouldBe(0.5);
    }

    [Fact]
    public void A_bearing_is_brought_onto_the_compass_before_it_is_classed()
    {
        // North-facing ground is both ends of the scale: the first class and the last score alike.
        var table = new EvidenceTransform(
            EvidenceTransformKind.Classes, Classes: [new(45, 0.9), new(315, 0.2), new(null, 0.9)]);

        Favourability.Score(Aspect, table, 10).ShouldBe(0.9);
        Favourability.Score(Aspect, table, 180).ShouldBe(0.2);
        Favourability.Score(Aspect, table, 350).ShouldBe(0.9);
        // 360 is north again, and -90 is west.
        Favourability.Score(Aspect, table, 360).ShouldBe(0.9);
        Favourability.Score(Aspect, table, 540).ShouldBe(0.2);
        Favourability.Score(Aspect, table, -90).ShouldBe(0.2);
    }

    [Fact]
    public void A_class_scores_what_the_model_gives_it_and_nought_when_the_model_does_not_mention_it()
    {
        var scores = new EvidenceTransform(
            EvidenceTransformKind.Categories, Categories: [new("limestone", 1), new("dolomite", 0.7)]);

        Favourability.ScoreOfCategory(scores, "limestone").ShouldBe(1);
        Favourability.ScoreOfCategory(scores, "dolomite").ShouldBe(0.7);
        Favourability.ScoreOfCategory(scores, "granite").ShouldBe(0);
        // Ground no unit covers has no class: that is no evidence, which is not unfavourable rock.
        Favourability.ScoreOfCategory(scores, null).ShouldBeNull();
    }

    [Fact]
    public void Layers_combine_as_their_weighted_mean()
    {
        // 0.5 * 1.0 + 0.3 * 0.5 + 0.2 * 0.0
        Favourability.Combine([1.0, 0.5, 0.0], [0.5, 0.3, 0.2]).ShouldNotBeNull().ShouldBe(0.65, 1e-12);
        // Only the proportions of the weights matter.
        Favourability.Combine([1.0, 0.5, 0.0], [5, 3, 2]).ShouldNotBeNull().ShouldBe(0.65, 1e-12);
        // Scores between nought and one stay there.
        Favourability.Combine([1.0, 1.0], [2, 7]).ShouldBe(1d);
        Favourability.Combine([0.0, 0.0], [2, 7]).ShouldBe(0d);
    }

    [Fact]
    public void A_cell_missing_any_layer_is_left_empty()
    {
        Favourability.Combine([1.0, null, 0.0], [0.5, 0.3, 0.2]).ShouldBeNull();
        Should.Throw<ArgumentException>(() => Favourability.Combine([1.0], [0.5, 0.5]));
        Should.Throw<ArgumentException>(() => Favourability.Combine([], []));
    }
}
