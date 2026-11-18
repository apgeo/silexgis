// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Potential;

namespace SilexGis.Domain.Tests;

/// <summary>
/// A whole data-driven run on a grid small enough to count by hand: which entrances are used, how
/// they are parted, what is recorded, and that nothing recorded is anything but a count.
/// </summary>
public class TrainedFavourabilityTests
{
    private static readonly EvidenceLayer Slope = EvidenceLayers.Find(EvidenceLayers.Slope)!;

    // Two classes: gentle below 10 degrees, steep from there up.
    private static readonly EvidenceLayerSetting SlopeClasses = new(
        EvidenceLayers.Slope,
        new EvidenceTransform(EvidenceTransformKind.Classes, Classes: [new(10, null), new(null, null)]));

    private const int Cells = 100;

    /// <summary>A hundred cells in a row: the first forty steep, the rest gentle.</summary>
    private static BuiltEvidence Ground(Guid[]? features = null, int protectedFeatures = 0) => new(
        Slope,
        [.. Enumerable.Range(0, Cells).Select(i => i < 40 ? 30f : 2f)],
        null,
        features is null ? EvidenceProvenance.None : new EvidenceProvenance(features, protectedFeatures));

    private static bool[] Everywhere => [.. Enumerable.Repeat(true, Cells)];

    private static CavePotentialModelDefinition Model(int seed = 4, double share = 0.3) =>
        new([SlopeClasses], Training: new CavePotentialTraining(share, seed));

    /// <summary>Sixteen entrances on steep ground and four on gentle, one to a cell.</summary>
    private static List<TrainingEntrance> Entrances() =>
    [
        .. Enumerable.Range(0, 16).Select(i => new TrainingEntrance(i * 2, i % 4 == 0)),
        .. Enumerable.Range(0, 4).Select(i => new TrainingEntrance(50 + (i * 10), false)),
    ];

    [Theory]
    [InlineData(CavePotentialMethod.FrequencyRatio)]
    [InlineData(CavePotentialMethod.WeightsOfEvidence)]
    public void A_run_counts_its_tables_from_the_training_part_and_tests_on_the_part_kept_back(CavePotentialMethod method)
    {
        var map = TrainedFavourability.Compute(method, Model(), [Ground()], Everywhere, Entrances());

        var training = map.Summary.Training.ShouldNotBeNull();
        training.HeldOutShare.ShouldBe(0.3);
        training.Seed.ShouldBe(4);
        // Twenty occupied cells, three in ten kept back.
        training.TrainingCells.ShouldBe(14);
        training.HeldOutCells.ShouldBe(6);
        training.TrainingEntrances.ShouldBe(14);
        training.HeldOutEntrances.ShouldBe(6);
        training.ProtectedEntrances.ShouldBe(4);
        training.Cells.ShouldBe(Cells);
        training.PriorProbability.ShouldBe(0.14, 1e-12);

        // One table, a row per class, and the rows add up to the ground and to the training part.
        var table = training.Layers.ShouldHaveSingleItem();
        table.Layer.ShouldBe(EvidenceLayers.Slope);
        table.Classes.Select(c => c.Cells).ShouldBe([60, 40]);
        table.Classes.Sum(c => c.Occurrences).ShouldBe(14);
        var steep = table.Classes[1];
        var gentle = table.Classes[0];
        steep.FrequencyRatio!.Value.ShouldBe((steep.Occurrences / 14d) / 0.4, 1e-12);
        steep.Occurrences.ShouldBeGreaterThan(gentle.Occurrences);
        steep.Contrast!.Value.ShouldBeGreaterThan(0);

        // The map is drawn from nought to one whatever scale the method works on, the steep
        // ground at the top; the method's own scale is kept beside it.
        map.Values[0].ShouldBe(1f);
        map.Values[99].ShouldBe(0f);
        map.Summary.Minimum.ShouldBe(0d);
        map.Summary.Maximum.ShouldBe(1d);
        map.Summary.Mean!.Value.ShouldBe(0.4, 1e-12);
        map.Summary.AreaCells.ShouldBe(Cells);
        map.Summary.ScoredCells.ShouldBe(Cells);
        training.ValueMaximum.ShouldBeGreaterThan(training.ValueMinimum);
        if (method == CavePotentialMethod.WeightsOfEvidence)
        {
            // With one layer the posterior is exactly the occupied share of the class, so the
            // probabilities add back up to the cells the model learnt from.
            training.ValueMaximum.ShouldBe(steep.Occurrences / 40d, 1e-6);
            training.ValueMinimum.ShouldBe(gentle.Occurrences / 60d, 1e-6);
            training.ExpectedOccurrences!.Value.ShouldBe(14, 1e-4);
        }
        else
        {
            training.ValueMaximum.ShouldBe(steep.FrequencyRatio.Value, 1e-6);
            training.ExpectedOccurrences.ShouldBeNull();
        }

        // Both curves are drawn against their own part, and are curves of a two-step map.
        training.SuccessRate.Occurrences.ShouldBe(14);
        training.PredictionRate.Occurrences.ShouldBe(6);
        training.SuccessRate.Cells.ShouldBe(Cells);
        training.SuccessRate.Points.Select(p => p.AreaShare).ShouldBe([0, 0.4, 1], 1e-12);
        training.SuccessRate.Points[1].OccurrenceShare.ShouldBe(steep.Occurrences / 14d, 1e-12);
        training.SuccessRate.AreaUnderCurve.ShouldBeGreaterThan(0.5);
        training.PredictionRate.AreaUnderCurve.ShouldBeInRange(0d, 1d);

        map.UsedEntrances.ShouldBe(Enumerable.Range(0, 20));
    }

    [Fact]
    public void The_same_seed_keeps_back_the_same_cells_and_another_seed_others()
    {
        var first = TrainedFavourability.Compute(
            CavePotentialMethod.WeightsOfEvidence, Model(seed: 4), [Ground()], Everywhere, Entrances());
        var again = TrainedFavourability.Compute(
            CavePotentialMethod.WeightsOfEvidence, Model(seed: 4), [Ground()], Everywhere, Entrances());

        again.Summary.Training!.Layers[0].Classes.ShouldBe(first.Summary.Training!.Layers[0].Classes);
        again.Summary.Training.PredictionRate.AreaUnderCurve.ShouldBe(first.Summary.Training.PredictionRate.AreaUnderCurve);
        again.Values.ShouldBe(first.Values);

        // Over a run of seeds the steep class is not always left the same number of training
        // cells: the seed is what decides the split.
        Enumerable.Range(0, 20)
            .Select(seed => TrainedFavourability.Compute(
                CavePotentialMethod.WeightsOfEvidence, Model(seed), [Ground()], Everywhere, Entrances())
                .Summary.Training!.Layers[0].Classes[1].Occurrences)
            .Distinct()
            .Count()
            .ShouldBeGreaterThan(1);
    }

    [Fact]
    public void An_entrance_off_the_weighed_ground_neither_teaches_nor_tests_and_is_reported_unused()
    {
        // The last ten cells lie outside the area; one entrance is there, one is off the grid, and
        // one sits on a cell the layer has no reading for.
        var inside = Everywhere;
        for (var i = 90; i < Cells; i++)
        {
            inside[i] = false;
        }

        var ground = Ground();
        ground.Values![45] = float.NaN;
        var entrances = Entrances();
        entrances.Add(new TrainingEntrance(95, true));
        entrances.Add(new TrainingEntrance(null, true));
        entrances.Add(new TrainingEntrance(45, true));

        var map = TrainedFavourability.Compute(
            CavePotentialMethod.FrequencyRatio, Model(), [ground], inside, entrances);

        // Entrance 19 sits on cell 80, inside; the three added ones are the unused ones.
        map.UsedEntrances.ShouldBe(Enumerable.Range(0, 20));
        var training = map.Summary.Training.ShouldNotBeNull();
        (training.TrainingEntrances + training.HeldOutEntrances).ShouldBe(20);
        training.ProtectedEntrances.ShouldBe(4);
        training.Cells.ShouldBe(89);
        map.Summary.AreaCells.ShouldBe(90);
        map.Summary.ScoredCells.ShouldBe(89);
        float.IsNaN(map.Values[95]).ShouldBeTrue();
        float.IsNaN(map.Values[45]).ShouldBeTrue();
    }

    [Fact]
    public void Two_entrances_in_one_cell_are_one_cell_and_both_are_counted_as_used()
    {
        var entrances = Entrances();
        entrances.Add(new TrainingEntrance(0, false));

        var training = TrainedFavourability.Compute(
            CavePotentialMethod.FrequencyRatio, Model(), [Ground()], Everywhere, entrances).Summary.Training.ShouldNotBeNull();

        (training.TrainingCells + training.HeldOutCells).ShouldBe(20);
        (training.TrainingEntrances + training.HeldOutEntrances).ShouldBe(21);
    }

    [Fact]
    public void Too_few_occupied_cells_fail_the_run_by_name_instead_of_drawing_a_map_from_a_handful()
    {
        var nine = Entrances().Take(TrainingSplits.MinimumCells - 1).ToList();

        Should.Throw<CavePotentialRunException>(() => TrainedFavourability.Compute(
                CavePotentialMethod.WeightsOfEvidence, Model(), [Ground()], Everywhere, nine))
            .Code.ShouldBe(CavePotentialRunFailures.TooFewEntrances);

        Should.Throw<CavePotentialRunException>(() => TrainedFavourability.Compute(
                CavePotentialMethod.WeightsOfEvidence, Model(), [Ground()], new bool[Cells], Entrances()))
            .Code.ShouldBe(CavePotentialRunFailures.NothingScored);
    }

    [Fact]
    public void What_a_run_stores_is_read_back_whole_and_holds_no_cell_and_no_entrance()
    {
        var features = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var map = TrainedFavourability.Compute(
            CavePotentialMethod.WeightsOfEvidence, Model(), [Ground(features, 1)], Everywhere, Entrances());

        var json = CavePotentialRunSummary.Serialize(map.Summary);
        var read = CavePotentialRunSummary.Deserialize(json).ShouldNotBeNull();

        read.Layers.ShouldHaveSingleItem().ShouldBe(new CavePotentialLayerSummary(EvidenceLayers.Slope, 0, 2, 1, Cells));
        var training = read.Training.ShouldNotBeNull();
        training.Layers[0].Classes.ShouldBe(map.Summary.Training!.Layers[0].Classes);
        training.SuccessRate.Points.ShouldBe(map.Summary.Training.SuccessRate.Points);
        training.PredictionRate.AreaUnderCurve.ShouldBe(map.Summary.Training.PredictionRate.AreaUnderCurve);

        // The stored text names counts, shares and weights. Nothing in it is keyed by a cell or a
        // feature: neither id appears, and no member is a list of cells or of entrances.
        foreach (var id in features)
        {
            json.ShouldNotContain(id.ToString());
        }

        using var document = System.Text.Json.JsonDocument.Parse(json);
        document.RootElement.GetProperty("training").EnumerateObject().Select(p => p.Name).ShouldBe(
        [
            "heldOutShare", "seed", "trainingEntrances", "heldOutEntrances", "trainingCells", "heldOutCells",
            "protectedEntrances", "cells", "priorProbability", "valueMinimum", "valueMaximum", "expectedOccurrences",
            "layers", "successRate", "predictionRate",
        ]);
    }
}
