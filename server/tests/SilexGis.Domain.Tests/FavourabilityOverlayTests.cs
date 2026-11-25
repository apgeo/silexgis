// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Potential;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The weighted overlay of a run: every layer rescored cell by cell and combined by weight, inside
/// the area and nowhere else.
/// </summary>
public class FavourabilityOverlayTests
{
    private static readonly EvidenceLayer Slope = EvidenceLayers.Find(EvidenceLayers.Slope)!;
    private static readonly EvidenceLayer Rock = EvidenceLayers.Find(EvidenceLayers.Lithology)!;
    private static readonly EvidenceLayer Sinkholes = EvidenceLayers.Find(EvidenceLayers.SinkholeDistance)!;

    private static readonly EvidenceLayerSetting SlopeRamp =
        new(EvidenceLayers.Slope, new EvidenceTransform(EvidenceTransformKind.FuzzyLinear, Low: 0, High: 40));

    private static readonly EvidenceLayerSetting RockScores = new(
        EvidenceLayers.Lithology,
        new EvidenceTransform(
            EvidenceTransformKind.Categories,
            Categories: [new("limestone", 1), new("granite", 0.2)]));

    private static float Class(string code) => Rock.Categories.ToList().IndexOf(code);

    [Fact]
    public void Each_cell_is_the_weighted_mean_of_its_layers_scores()
    {
        // Four cells: slope 0, 10, 20, 40 degrees on a ramp from 0 to 40; rock limestone, limestone,
        // granite, a class the model does not mention. Weights three to one.
        var slope = new BuiltEvidence(Slope, [0f, 10f, 20f, 40f], null, EvidenceProvenance.None);
        var rock = new BuiltEvidence(
            Rock,
            [Class("limestone"), Class("limestone"), Class("granite"), Class("shale_marl")],
            null,
            new EvidenceProvenance([Guid.NewGuid(), Guid.NewGuid()], 1));

        var map = FavourabilityOverlay.Compute(
            [SlopeRamp, RockScores], [0.75, 0.25], [slope, rock], [true, true, true, true]);

        map.Values[0].ShouldBe(0.25f, 1e-6f);                       // 0.75 * 0    + 0.25 * 1
        map.Values[1].ShouldBe(0.4375f, 1e-6f);                     // 0.75 * 0.25 + 0.25 * 1
        map.Values[2].ShouldBe((0.75f * 0.5f) + (0.25f * 0.2f), 1e-6f);
        map.Values[3].ShouldBe(0.75f, 1e-6f);                       // an unmentioned class scores nought

        map.Summary.AreaCells.ShouldBe(4);
        map.Summary.ScoredCells.ShouldBe(4);
        map.Summary.Minimum!.Value.ShouldBe(0.25, 1e-6);
        map.Summary.Maximum!.Value.ShouldBe(0.75, 1e-6);
        map.Summary.Mean!.Value.ShouldBe((0.25 + 0.4375 + 0.425 + 0.75) / 4, 1e-6);
        map.Summary.Layers.Select(l => l.Layer).ShouldBe([EvidenceLayers.Slope, EvidenceLayers.Lithology]);
        map.Summary.Layers[1].Features.ShouldBe(2);
        map.Summary.Layers[1].ProtectedFeatures.ShouldBe(1);
    }

    [Fact]
    public void Weights_are_reported_as_shares_of_one_whatever_they_sum_to()
    {
        var slope = new BuiltEvidence(Slope, [20f], null, EvidenceProvenance.None);
        var rock = new BuiltEvidence(Rock, [Class("limestone")], null, EvidenceProvenance.None);

        var map = FavourabilityOverlay.Compute([SlopeRamp, RockScores], [6, 2], [slope, rock], [true]);

        map.Values[0].ShouldBe(((6f * 0.5f) + (2f * 1f)) / 8f, 1e-6f);
        map.Summary.Layers.Select(l => l.Weight).ShouldBe([0.75, 0.25]);
    }

    [Fact]
    public void A_cell_outside_the_area_is_left_empty_whatever_evidence_lies_under_it()
    {
        var slope = new BuiltEvidence(Slope, [40f, 40f, 40f], null, EvidenceProvenance.None);

        var map = FavourabilityOverlay.Compute([SlopeRamp], [1], [slope], [true, false, true]);

        map.Values[0].ShouldBe(1f);
        float.IsNaN(map.Values[1]).ShouldBeTrue();
        map.Summary.AreaCells.ShouldBe(2);
        map.Summary.ScoredCells.ShouldBe(2);
    }

    [Fact]
    public void A_cell_missing_one_layer_is_left_empty_rather_than_scored_on_the_rest()
    {
        var slope = new BuiltEvidence(Slope, [20f, float.NaN, 20f], null, EvidenceProvenance.None);
        var rock = new BuiltEvidence(Rock, [Class("limestone"), Class("limestone"), float.NaN], null, EvidenceProvenance.None);

        var map = FavourabilityOverlay.Compute([SlopeRamp, RockScores], [1, 1], [slope, rock], [true, true, true]);

        map.Values[0].ShouldBe(0.75f, 1e-6f);
        float.IsNaN(map.Values[1]).ShouldBeTrue();
        float.IsNaN(map.Values[2]).ShouldBeTrue();
        map.Summary.ScoredCells.ShouldBe(1);
        map.Summary.Layers[0].Cells.ShouldBe(2);
        map.Summary.Layers[1].Cells.ShouldBe(2);
    }

    [Fact]
    public void An_area_where_nothing_is_scored_has_no_least_greatest_or_mean()
    {
        var slope = new BuiltEvidence(Slope, [float.NaN], null, EvidenceProvenance.None);

        var map = FavourabilityOverlay.Compute([SlopeRamp], [1], [slope], [true]);

        map.Summary.ScoredCells.ShouldBe(0);
        map.Summary.Minimum.ShouldBeNull();
        map.Summary.Maximum.ShouldBeNull();
        map.Summary.Mean.ShouldBeNull();
    }

    [Fact]
    public void A_ramp_written_downwards_scores_the_nearest_ground_highest()
    {
        var near = new EvidenceLayerSetting(
            EvidenceLayers.SinkholeDistance,
            new EvidenceTransform(EvidenceTransformKind.FuzzyLinear, Low: 500, High: 0));
        var distance = new BuiltEvidence(Sinkholes, [0f, 250f, 900f], null, EvidenceProvenance.None);

        var map = FavourabilityOverlay.Compute([near], [1], [distance], [true, true, true]);

        map.Values.ShouldBe([1f, 0.5f, 0f]);
    }

    [Fact]
    public void Layers_that_are_not_the_models_or_not_on_the_grid_are_refused()
    {
        var slope = new BuiltEvidence(Slope, [1f, 2f], null, EvidenceProvenance.None);
        var unavailable = new BuiltEvidence(Slope, null, EvidenceUnavailable.NoLayer, EvidenceProvenance.None);

        Should.Throw<ArgumentException>(() => FavourabilityOverlay.Compute([SlopeRamp], [1], [slope], [true]));
        Should.Throw<ArgumentException>(() => FavourabilityOverlay.Compute([SlopeRamp], [1], [unavailable], [true, true]));
        Should.Throw<ArgumentException>(() => FavourabilityOverlay.Compute([RockScores], [1], [slope], [true, true]));
        Should.Throw<ArgumentException>(() => FavourabilityOverlay.Compute([SlopeRamp], [1, 1], [slope], [true, true]));
    }

    [Fact]
    public void A_summary_survives_being_stored()
    {
        var slope = new BuiltEvidence(Slope, [20f], null, EvidenceProvenance.None);
        var summary = FavourabilityOverlay.Compute([SlopeRamp], [1], [slope], [true]).Summary;

        var read = CavePotentialRunSummary.Deserialize(CavePotentialRunSummary.Serialize(summary))!;

        read.ScoredCells.ShouldBe(1);
        read.Mean!.Value.ShouldBe(0.5, 1e-9);
        read.Layers.Single().Layer.ShouldBe(EvidenceLayers.Slope);
        CavePotentialRunSummary.Deserialize("{}").ShouldBeNull();
        CavePotentialRunSummary.Deserialize("not json").ShouldBeNull();
    }
}
