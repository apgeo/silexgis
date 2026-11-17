// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Potential;

namespace SilexGis.Domain.Tests;

/// <summary>What a cave-potential model may be stored as, what weights it comes to, and who may change it.</summary>
public class CavePotentialModelRulesTests
{
    private static readonly EvidenceTransform SlopeClasses = new(
        EvidenceTransformKind.Classes, Classes: [new(5, 0.2), new(20, 1), new(null, 0.4)]);

    private static readonly EvidenceTransform NearIsBetter = new(EvidenceTransformKind.FuzzyLinear, Low: 500, High: 0);

    private static readonly EvidenceTransform Rock = new(
        EvidenceTransformKind.Categories, Categories: [new("limestone", 1), new("dolomite", 0.7)]);

    private static CavePotentialModelDefinition Direct(params EvidenceLayerSetting[] layers) => new(layers);

    private static EvidenceLayerSetting Layer(string code, EvidenceTransform transform, double? weight = null) =>
        new(code, transform, weight);

    private static CavePotentialModelDefinition ThreeLayers(IReadOnlyList<IReadOnlyList<double>>? pairwise) => new(
        [
            Layer(EvidenceLayers.Lithology, Rock),
            Layer(EvidenceLayers.Slope, SlopeClasses),
            Layer(EvidenceLayers.FaultDistance, NearIsBetter),
        ],
        pairwise);

    private static IReadOnlyList<string> PathsOf(CavePotentialMethod method, CavePotentialModelDefinition? definition) =>
        [.. CavePotentialModelRules.Problems(method, definition).Select(p => p.Path)];

    // ---------- the vocabulary ----------

    [Fact]
    public void The_evidence_layers_are_the_shipped_list_in_its_order()
    {
        // A stored model names its layers by these codes, so the list is appended to and never
        // renamed or reordered. Pinned as literals: a rename would pass every other test while
        // turning every stored model that names the layer into one that names nothing.
        EvidenceLayers.All.Select(l => l.Code).ShouldBe(
        [
            "elevation", "slope", "aspect", "position_index", "ruggedness", "roughness", "geomorphons",
            "fault_distance", "lithology", "sinkhole_distance", "entrance_density",
        ]);
        ((short)CavePotentialMethod.DirectWeights).ShouldBe((short)0);
        ((short)CavePotentialMethod.PairwiseComparison).ShouldBe((short)1);
    }

    [Fact]
    public void Layers_made_from_recorded_features_are_marked_as_using_positions_and_terrain_layers_are_not()
    {
        EvidenceLayers.All.Where(l => l.UsesPositions).Select(l => l.Code).ShouldBe(
            ["fault_distance", "lithology", "sinkhole_distance", "entrance_density"]);

        CavePotentialModelRules.UsesPositions(Direct(
            Layer(EvidenceLayers.Slope, SlopeClasses, 1),
            Layer(EvidenceLayers.Elevation, new(EvidenceTransformKind.FuzzyLinear, Low: 200, High: 1500), 1)))
            .ShouldBeFalse();
        CavePotentialModelRules.UsesPositions(Direct(
            Layer(EvidenceLayers.Slope, SlopeClasses, 1),
            Layer(EvidenceLayers.SinkholeDistance, NearIsBetter, 1)))
            .ShouldBeTrue();
    }

    [Fact]
    public void A_categorical_layer_offers_its_own_closed_classes()
    {
        EvidenceLayers.Find(EvidenceLayers.Lithology)!.Categories.ShouldContain("limestone");
        EvidenceLayers.Find(EvidenceLayers.Geomorphons)!.Categories.Count.ShouldBe(10);
        EvidenceLayers.Find(EvidenceLayers.Slope)!.Categories.ShouldBeEmpty();
        EvidenceLayers.Find("altitude").ShouldBeNull();
        EvidenceLayers.Find(null).ShouldBeNull();
    }

    // ---------- weights given outright ----------

    [Fact]
    public void Weights_given_outright_are_shares_of_their_sum()
    {
        var definition = Direct(
            Layer(EvidenceLayers.Lithology, Rock, 5),
            Layer(EvidenceLayers.Slope, SlopeClasses, 3),
            Layer(EvidenceLayers.FaultDistance, NearIsBetter, 2));

        CavePotentialModelRules.Problems(CavePotentialMethod.DirectWeights, definition).ShouldBeEmpty();
        var weighting = CavePotentialModelRules.Weighting(CavePotentialMethod.DirectWeights, definition);

        weighting.Weights.Select(w => w.Layer).ShouldBe(["lithology", "slope", "fault_distance"]);
        weighting.Weights.Select(w => w.Weight).ShouldBe([0.5, 0.3, 0.2], 1e-12);
        weighting.Consistency.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(2_000_000d)]
    public void A_layer_without_a_usable_weight_is_refused(double? weight) =>
        PathsOf(CavePotentialMethod.DirectWeights, Direct(Layer(EvidenceLayers.Slope, SlopeClasses, weight)))
            .ShouldBe(["layers[0].weight"]);

    [Fact]
    public void Comparisons_on_a_model_with_weights_given_outright_are_refused() =>
        PathsOf(
            CavePotentialMethod.DirectWeights,
            new([Layer(EvidenceLayers.Slope, SlopeClasses, 1), Layer(EvidenceLayers.Lithology, Rock, 1)], [[3]]))
            .ShouldBe(["pairwise"]);

    // ---------- weights from comparisons ----------

    [Fact]
    public void Weights_from_comparisons_come_with_their_consistency()
    {
        var definition = ThreeLayers([[3, 5], [3]]);

        CavePotentialModelRules.Problems(CavePotentialMethod.PairwiseComparison, definition).ShouldBeEmpty();
        var weighting = CavePotentialModelRules.Weighting(CavePotentialMethod.PairwiseComparison, definition);

        weighting.Weights.Select(w => w.Layer).ShouldBe(["lithology", "slope", "fault_distance"]);
        weighting.Weights.Select(w => w.Weight).ShouldBe([0.6370, 0.2583, 0.1047], 0.0001);
        weighting.Consistency.ShouldNotBeNull().Ratio.ShouldBe(0.0332, 0.0001);
        weighting.Consistency.Acceptable.ShouldBeTrue();
    }

    [Fact]
    public void Inconsistent_comparisons_are_warned_about_and_not_refused()
    {
        // Rock far outweighs slope, slope far outweighs faults, and yet faults outweigh rock.
        var definition = ThreeLayers([[9, 1d / 9], [9]]);

        CavePotentialModelRules.Problems(CavePotentialMethod.PairwiseComparison, definition).ShouldBeEmpty();
        var consistency = CavePotentialModelRules
            .Weighting(CavePotentialMethod.PairwiseComparison, definition).Consistency.ShouldNotBeNull();

        consistency.Ratio.ShouldBeGreaterThan(AnalyticHierarchy.AcceptableRatio);
        consistency.Acceptable.ShouldBeFalse();
    }

    [Fact]
    public void A_ninth_typed_to_three_decimals_is_still_on_the_scale() =>
        CavePotentialModelRules.Problems(CavePotentialMethod.PairwiseComparison, ThreeLayers([[0.111, 9], [1]]))
            .ShouldBeEmpty();

    [Fact]
    public void Comparisons_of_the_wrong_shape_or_off_the_scale_are_refused()
    {
        PathsOf(CavePotentialMethod.PairwiseComparison, ThreeLayers(null)).ShouldBe(["pairwise"]);
        PathsOf(CavePotentialMethod.PairwiseComparison, ThreeLayers([[3, 5]])).ShouldBe(["pairwise"]);
        PathsOf(CavePotentialMethod.PairwiseComparison, ThreeLayers([[3], [3]])).ShouldBe(["pairwise[0]"]);
        PathsOf(CavePotentialMethod.PairwiseComparison, ThreeLayers([[3, 10], [0.05]]))
            .ShouldBe(["pairwise[0][1]", "pairwise[1][0]"]);
    }

    [Fact]
    public void A_weight_on_a_layer_of_a_model_weighed_by_comparisons_is_refused()
    {
        var definition = new CavePotentialModelDefinition(
            [Layer(EvidenceLayers.Slope, SlopeClasses, 2), Layer(EvidenceLayers.Lithology, Rock)], [[3]]);

        PathsOf(CavePotentialMethod.PairwiseComparison, definition).ShouldBe(["layers[0].weight"]);
    }

    // ---------- layers and their scoring ----------

    [Fact]
    public void A_model_weighs_at_least_one_known_layer_and_each_at_most_once()
    {
        PathsOf(CavePotentialMethod.DirectWeights, null).ShouldBe(["layers"]);
        PathsOf(CavePotentialMethod.DirectWeights, Direct()).ShouldBe(["layers"]);
        PathsOf(CavePotentialMethod.DirectWeights, Direct(Layer("altitude", SlopeClasses, 1)))
            .ShouldBe(["layers[0].layer"]);
        PathsOf(
            CavePotentialMethod.DirectWeights,
            Direct(Layer(EvidenceLayers.Slope, SlopeClasses, 1), Layer(EvidenceLayers.Slope, SlopeClasses, 1)))
            .ShouldBe(["layers[1].layer"]);
        PathsOf((CavePotentialMethod)99, Direct(Layer(EvidenceLayers.Slope, SlopeClasses, 1))).ShouldBe(["method"]);
    }

    [Fact]
    public void A_layer_is_scored_only_in_a_way_its_values_allow()
    {
        // A bearing takes no ramp; a measurement takes no class scores; a class takes no table.
        PathsOf(CavePotentialMethod.DirectWeights, Direct(Layer(EvidenceLayers.Aspect, NearIsBetter, 1)))
            .ShouldBe(["layers[0].transform.kind"]);
        PathsOf(CavePotentialMethod.DirectWeights, Direct(Layer(EvidenceLayers.Slope, Rock, 1)))
            .ShouldBe(["layers[0].transform.kind"]);
        PathsOf(CavePotentialMethod.DirectWeights, Direct(Layer(EvidenceLayers.Lithology, SlopeClasses, 1)))
            .ShouldBe(["layers[0].transform.kind"]);
        PathsOf(CavePotentialMethod.DirectWeights, Direct(Layer(EvidenceLayers.Slope, null!, 1)))
            .ShouldBe(["layers[0].transform"]);
    }

    [Fact]
    public void A_classes_table_rises_ends_open_and_scores_between_nought_and_one()
    {
        IReadOnlyList<string> Table(params EvidenceClass[] rows) => PathsOf(
            CavePotentialMethod.DirectWeights,
            Direct(Layer(EvidenceLayers.Slope, new(EvidenceTransformKind.Classes, Classes: rows), 1)));

        Table(new EvidenceClass(null, 1)).ShouldBe(["layers[0].transform.classes"]);
        Table(new(20, 0.2), new(5, 1), new(null, 0.4)).ShouldBe(["layers[0].transform.classes[1].upperBound"]);
        Table(new(5, 0.2), new(5, 1), new(null, 0.4)).ShouldBe(["layers[0].transform.classes[1].upperBound"]);
        Table(new(5, 0.2), new(20, 1)).ShouldBe(["layers[0].transform.classes[1].upperBound"]);
        Table(new(null, 0.2), new(null, 1)).ShouldBe(["layers[0].transform.classes[0].upperBound"]);
        Table(new(5, 1.2), new(null, null)).ShouldBe(
            ["layers[0].transform.classes[0].score", "layers[0].transform.classes[1].score"]);
        Table([.. Enumerable.Range(1, CavePotentialModelRules.MaxClasses).Select(i => new EvidenceClass(i, 0.5)), new(null, 1)])
            .ShouldBe(["layers[0].transform.classes"]);
    }

    [Fact]
    public void A_bearings_bounds_lie_on_the_compass() =>
        PathsOf(
            CavePotentialMethod.DirectWeights,
            Direct(Layer(
                EvidenceLayers.Aspect,
                new(EvidenceTransformKind.Classes, Classes: [new(45, 1), new(400, 0.2), new(null, 1)]),
                1)))
            .ShouldBe(["layers[0].transform.classes[1].upperBound"]);

    [Fact]
    public void A_ramp_needs_two_different_bounds()
    {
        IReadOnlyList<string> Ramp(double? low, double? high) => PathsOf(
            CavePotentialMethod.DirectWeights,
            Direct(Layer(EvidenceLayers.Slope, new(EvidenceTransformKind.FuzzyLinear, Low: low, High: high), 1)));

        Ramp(0, 30).ShouldBeEmpty();
        Ramp(30, 0).ShouldBeEmpty();
        Ramp(null, 30).ShouldBe(["layers[0].transform.low"]);
        Ramp(0, null).ShouldBe(["layers[0].transform.high"]);
        Ramp(7, 7).ShouldBe(["layers[0].transform.high"]);
    }

    [Fact]
    public void Class_scores_name_the_layers_own_classes_once_each()
    {
        IReadOnlyList<string> Scores(params EvidenceCategoryScore[] rows) => PathsOf(
            CavePotentialMethod.DirectWeights,
            Direct(Layer(EvidenceLayers.Lithology, new(EvidenceTransformKind.Categories, Categories: rows), 1)));

        Scores().ShouldBe(["layers[0].transform.categories"]);
        Scores(new EvidenceCategoryScore("chalk", 1)).ShouldBe(["layers[0].transform.categories[0].code"]);
        Scores(new("limestone", 1), new("limestone", 0.5)).ShouldBe(["layers[0].transform.categories[1].code"]);
        Scores(new EvidenceCategoryScore("limestone", -0.1)).ShouldBe(["layers[0].transform.categories[0].score"]);
        // A landform class is not a rock.
        Scores(new EvidenceCategoryScore("ridge", 1)).ShouldBe(["layers[0].transform.categories[0].code"]);
    }

    [Fact]
    public void Weights_are_never_worked_out_for_a_definition_with_problems() =>
        Should.Throw<ArgumentException>(() => CavePotentialModelRules.Weighting(
            CavePotentialMethod.DirectWeights, Direct(Layer(EvidenceLayers.Slope, SlopeClasses))));

    // ---------- the stored form ----------

    [Fact]
    public void A_definition_reads_back_as_it_was_written()
    {
        var definition = ThreeLayers([[3, 5], [3]]);

        var stored = CavePotentialModelDefinition.Serialize(definition);
        var read = CavePotentialModelDefinition.Deserialize(stored).ShouldNotBeNull();

        stored.ShouldContain("\"kind\":\"fuzzyLinear\"");
        stored.ShouldNotContain("null");
        read.Layers.Select(l => l.Layer).ShouldBe(["lithology", "slope", "fault_distance"]);
        read.Layers[1].Transform.Classes!.Select(c => c.UpperBound).ShouldBe([5d, 20d, null]);
        read.Layers[2].Transform.Low.ShouldBe(500);
        read.Pairwise.ShouldNotBeNull()[0].ShouldBe([3d, 5d]);
        CavePotentialModelRules.Problems(CavePotentialMethod.PairwiseComparison, read).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"layers\":[{\"layer\":\"slope\",\"transform\":{\"kind\":\"spline\"}}]}")]
    public void A_stored_definition_this_version_cannot_read_is_null_rather_than_a_fault(string? stored) =>
        CavePotentialModelDefinition.Deserialize(stored).ShouldBeNull();

    // ---------- who may change one ----------

    [Fact]
    public void Only_the_author_or_a_full_administrator_may_change_a_model()
    {
        var author = Guid.CreateVersion7();
        var model = new CavePotentialModel { Name = "Karst plateau", OwnerUserId = author };

        CavePotentialModelRules.MayChange(new AccessContext(author, false, [], []), model).ShouldBeTrue();
        CavePotentialModelRules.MayChange(new AccessContext(Guid.CreateVersion7(), true, [], []), model).ShouldBeTrue();
        CavePotentialModelRules.MayChange(new AccessContext(Guid.CreateVersion7(), false, [], []), model).ShouldBeFalse();
    }

    [Fact]
    public void A_model_is_never_shown_to_anybody_without_an_account()
    {
        CavePotentialModelRules.IsAllowedVisibility(Visibility.Private).ShouldBeTrue();
        CavePotentialModelRules.IsAllowedVisibility(Visibility.CavingGroup).ShouldBeTrue();
        CavePotentialModelRules.IsAllowedVisibility(Visibility.Authenticated).ShouldBeTrue();
        CavePotentialModelRules.IsAllowedVisibility(Visibility.Public).ShouldBeFalse();
        CavePotentialModelRules.NamesACavingGroup(Visibility.CavingGroup).ShouldBeTrue();
        CavePotentialModelRules.NamesACavingGroup(Visibility.Private).ShouldBeFalse();
    }
}
