// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Potential;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Weights counted from occupied cells, held to figures worked outside this code from the
/// published formulas (Bonham-Carter, "Geographic Information Systems for Geoscientists", 1994,
/// chapter 9) and to identities those formulas imply, rather than to figures this code produced.
/// </summary>
public class EvidenceWeightsTests
{
    private const double Tolerance = 1e-6;

    [Fact]
    public void A_pattern_over_a_third_of_the_ground_that_holds_nine_caves_in_ten_has_its_worked_weights()
    {
        // 10,000 cells of which 200 are occupied; the pattern covers 3,600 and holds 180 of them.
        // P(B|D) = 180/200, P(B|not D) = 3420/9800, P(not B|D) = 20/200, P(not B|not D) = 6380/9800.
        var row = EvidenceWeights.OfClass(0, 3_600, 180, 10_000, 200);

        row.PositiveWeight!.Value.ShouldBe(0.947381, Tolerance);          // ln(0.9 / 0.348980)
        row.NegativeWeight!.Value.ShouldBe(-1.873371, Tolerance);         // ln(0.1 / 0.651020)
        row.Contrast!.Value.ShouldBe(2.820752, Tolerance);
        row.ContrastDeviation!.Value.ShouldBe(0.236653, Tolerance);       // sqrt(1/180 + 1/3420 + 1/20 + 1/6380)
        row.StudentisedContrast!.Value.ShouldBe(11.919354, Tolerance);
        row.FrequencyRatio!.Value.ShouldBe(2.5, 1e-12);                   // (180/200) / (3600/10000)
        row.Corrected.ShouldBeFalse();
    }

    [Fact]
    public void A_three_class_layer_has_its_hand_computed_frequency_ratios_and_weights()
    {
        // 1,000 cells, 50 occupied. Class by class: cells, occupied, share of caves over share of ground.
        //   0: 500, 10 → (10/50) / (500/1000) = 0.4
        //   1: 300, 15 → (15/50) / (300/1000) = 1.0
        //   2: 200, 25 → (25/50) / (200/1000) = 2.5
        var (classes, ground, occupied) = ThreeClasses();

        var trained = EvidenceWeights.Train(["slope"], [classes], [3], ground, occupied)!;

        trained.Cells.ShouldBe(1_000);
        trained.Occurrences.ShouldBe(50);
        trained.PriorProbability.ShouldBe(0.05, 1e-12);
        var rows = trained.Layers.ShouldHaveSingleItem().Classes;
        rows.Select(r => (r.Class, r.Cells, r.Occurrences)).ShouldBe([(0, 500, 10), (1, 300, 15), (2, 200, 25)]);
        rows.Select(r => r.FrequencyRatio!.Value).ShouldBe([0.4, 1.0, 2.5], 1e-12);

        double[] positive = [-0.947381, 0, 0.998529];
        double[] negative = [0.502092, 0, -0.489548];
        double[] deviation = [0.359465, 0.316624, 0.294965];
        double[] studentised = [-4.032313, 0, 5.044920];
        for (var k = 0; k < 3; k++)
        {
            rows[k].PositiveWeight!.Value.ShouldBe(positive[k], Tolerance, $"W+ of class {k}");
            rows[k].NegativeWeight!.Value.ShouldBe(negative[k], Tolerance, $"W- of class {k}");
            rows[k].Contrast!.Value.ShouldBe(positive[k] - negative[k], 2 * Tolerance, $"C of class {k}");
            rows[k].ContrastDeviation!.Value.ShouldBe(deviation[k], Tolerance, $"s(C) of class {k}");
            rows[k].StudentisedContrast!.Value.ShouldBe(studentised[k], Tolerance, $"C/s(C) of class {k}");
            rows[k].Corrected.ShouldBeFalse();
        }

        // The ratios of a layer, weighted by each class's share of the ground, always come to one.
        rows.Sum(r => r.FrequencyRatio!.Value * r.Cells / trained.Cells).ShouldBe(1, 1e-12);
    }

    [Fact]
    public void A_class_with_no_occupied_cell_is_weighed_after_a_half_is_added_to_each_count()
    {
        // 100 cells, 10 occupied; the class has 20 cells and none of the 10. The four counts
        // 0, 20, 10, 70 become 0.5, 20.5, 10.5, 70.5.
        var row = EvidenceWeights.OfClass(4, 20, 0, 100, 10);

        row.Corrected.ShouldBeTrue();
        row.FrequencyRatio.ShouldBe(0);
        row.PositiveWeight!.Value.ShouldBe(-1.600608, Tolerance);
        row.NegativeWeight!.Value.ShouldBe(0.208727, Tolerance);
        row.Contrast!.Value.ShouldBe(-1.809335, Tolerance);
        row.ContrastDeviation!.Value.ShouldBe(1.469082, Tolerance);
        row.StudentisedContrast!.Value.ShouldBe(-1.231609, Tolerance);
        row.Class.ShouldBe(4);
    }

    [Fact]
    public void A_class_that_holds_every_occupied_cell_is_corrected_the_same_way()
    {
        // 100 cells, 10 occupied, all 10 in a class of 30: counts 10, 20, 0, 70.
        var row = EvidenceWeights.OfClass(0, 30, 10, 100, 10);

        row.Corrected.ShouldBeTrue();
        row.FrequencyRatio!.Value.ShouldBe(10d / 3, 1e-12);
        row.PositiveWeight!.Value.ShouldBe(1.443915, Tolerance);
        row.NegativeWeight!.Value.ShouldBe(-2.835796, Tolerance);
        row.StudentisedContrast!.Value.ShouldBe(2.913186, Tolerance);
    }

    [Fact]
    public void A_class_no_cell_falls_in_has_an_empty_row_and_one_every_cell_falls_in_tells_nothing()
    {
        var absent = EvidenceWeights.OfClass(2, 0, 0, 100, 10);
        absent.ShouldBe(new EvidenceClassWeights(2, 0, 0, null, null, null, null, null, null, false));

        var everywhere = EvidenceWeights.OfClass(0, 100, 10, 100, 10);
        everywhere.ShouldBe(new EvidenceClassWeights(0, 100, 10, 1d, 0d, null, null, null, null, false));
    }

    [Theory]
    [InlineData(20, 5, 100, 0)]    // nothing occupied
    [InlineData(20, 5, 100, 100)]  // everything occupied
    [InlineData(20, 21, 100, 30)]  // more occupied cells than cells
    [InlineData(20, 5, 100, 4)]    // more occupied in the class than anywhere
    [InlineData(90, 1, 100, 20)]   // more occupied outside the class than there is ground outside it
    [InlineData(101, 5, 100, 10)]  // a class larger than the ground
    public void Counts_that_cannot_be_counts_are_refused(int cells, int occurrences, int totalCells, int totalOccurrences)
    {
        Should.Throw<ArgumentException>(() => EvidenceWeights.OfClass(0, cells, occurrences, totalCells, totalOccurrences));
    }

    [Fact]
    public void With_one_layer_the_posterior_of_a_cell_is_the_occupied_share_of_its_class()
    {
        // The identity the weights exist for: prior odds times exp(W+) are the class's own odds.
        var (classes, ground, occupied) = ThreeClasses();
        var trained = EvidenceWeights.Train(["slope"], [classes], [3], ground, occupied)!;

        var posterior = EvidenceWeights.PosteriorProbabilities(trained, [classes], ground);

        ((double)posterior[0]).ShouldBe(10d / 500, 1e-8);
        ((double)posterior[500]).ShouldBe(15d / 300, 1e-8);
        ((double)posterior[800]).ShouldBe(25d / 200, 1e-8);
        // And so a single layer gives back exactly the cells it was trained on.
        EvidenceWeights.ExpectedOccurrences(posterior).ShouldBe(50, 1e-4);
    }

    [Fact]
    public void Two_layers_add_their_weights_to_the_prior_and_their_ratios_to_each_other()
    {
        var (first, ground, occupied) = ThreeClasses();

        // A second layer of two classes: class 0 has 400 cells and 30 of the occupied ones (the
        // ten of the first class, the fifteen of the second, five of the third); class 1 the rest.
        var second = Enumerable.Repeat(1, 1_000).ToArray();
        foreach (var cell in Enumerable.Range(0, 380).Concat(Enumerable.Range(500, 15)).Concat(Enumerable.Range(800, 5)))
        {
            second[cell] = 0;
        }

        var trained = EvidenceWeights.Train(["slope", "geomorphons"], [first, second], [3, 2], ground, occupied)!;
        var table = trained.Layers[1].Classes;
        table.Select(r => (r.Cells, r.Occurrences)).ShouldBe([(400, 30), (600, 20)]);
        table[0].PositiveWeight!.Value.ShouldBe(0.432133, Tolerance);   // ln((30/50) / (370/950))
        table[1].PositiveWeight!.Value.ShouldBe(-0.422857, Tolerance);  // ln((20/50) / (580/950))
        table[0].FrequencyRatio!.Value.ShouldBe(1.5, 1e-12);
        table[1].FrequencyRatio!.Value.ShouldBe(2d / 3, 1e-12);

        var posterior = EvidenceWeights.PosteriorProbabilities(trained, [first, second], ground);
        // ln(50/950) + W+ of the cell's class on each layer, back through the logistic.
        ((double)posterior[800]).ShouldBe(0.180380, Tolerance);   // third class, class 0
        ((double)posterior[400]).ShouldBe(0.013194, Tolerance);   // first class, class 1
        ((double)posterior[500]).ShouldBe(0.075000, Tolerance);   // second class, class 0
        // These two layers are not independent among the occupied cells, and the sum shows it.
        EvidenceWeights.ExpectedOccurrences(posterior).ShouldBe(41.3823, 1e-3);

        var ratios = EvidenceWeights.SummedFrequencyRatios(trained, [first, second], ground);
        ((double)ratios[800]).ShouldBe(2.5 + 1.5, 1e-6);
        ((double)ratios[400]).ShouldBe(0.4 + (2d / 3), 1e-6);
        ((double)ratios[500]).ShouldBe(1.0 + 1.5, 1e-6);
    }

    [Fact]
    public void Ground_is_the_cells_inside_the_area_that_every_layer_classes_and_the_rest_stay_empty()
    {
        int[] first = [0, 0, 1, 1, EvidenceClassing.NoClass, 1];
        int[] second = [0, 1, 0, EvidenceClassing.NoClass, 0, 1];
        bool[] inside = [true, true, false, true, true, true];

        var ground = EvidenceWeights.Ground([first, second], inside);

        ground.ShouldBe([true, true, false, false, false, true]);

        var trained = EvidenceWeights.Train(["slope", "aspect"], [first, second], [2, 2], ground, [1])!;
        trained.Cells.ShouldBe(3);
        trained.Occurrences.ShouldBe(1);
        var map = EvidenceWeights.PosteriorProbabilities(trained, [first, second], ground);
        map.Select(float.IsNaN).ShouldBe([false, false, true, true, true, false]);
        EvidenceWeights.SummedFrequencyRatios(trained, [first, second], ground)
            .Select(float.IsNaN).ShouldBe([false, false, true, true, true, false]);
    }

    [Fact]
    public void Training_refuses_an_occupied_cell_off_the_ground_and_has_nothing_to_learn_from_none_or_all()
    {
        int[] classes = [0, 0, 1, 1];
        bool[] ground = [true, true, true, false];

        Should.Throw<ArgumentException>(() => EvidenceWeights.Train(["slope"], [classes], [2], ground, [3]));
        Should.Throw<ArgumentException>(() => EvidenceWeights.Train(["slope"], [classes], [2], ground, [4]));
        Should.Throw<ArgumentException>(() => EvidenceWeights.Train(["slope"], [classes], [1], ground, [0]));
        EvidenceWeights.Train(["slope"], [classes], [2], ground, []).ShouldBeNull();
        EvidenceWeights.Train(["slope"], [classes], [2], ground, [0, 1, 2]).ShouldBeNull();
        // A cell named twice is one occupied cell.
        EvidenceWeights.Train(["slope"], [classes], [2], ground, [0, 0])!.Occurrences.ShouldBe(1);
    }

    /// <summary>
    /// A thousand cells in three classes of 500, 300 and 200, of which the first 10, 15 and 25 of
    /// each class are occupied.
    /// </summary>
    private static (int[] Classes, bool[] Ground, int[] Occupied) ThreeClasses()
    {
        var classes = new int[1_000];
        Array.Fill(classes, 1, 500, 300);
        Array.Fill(classes, 2, 800, 200);
        var ground = Enumerable.Repeat(true, 1_000).ToArray();
        int[] occupied = [.. Enumerable.Range(0, 10), .. Enumerable.Range(500, 15), .. Enumerable.Range(800, 25)];
        return (classes, ground, occupied);
    }
}
