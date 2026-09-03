// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The karstification index. The property worth pinning is not the arithmetic — it is that an area
/// nobody has mapped the depressions of does not read as an area that has none.
/// </summary>
public class KarstificationIndexTests
{
    [Fact]
    public void An_area_at_both_references_scores_the_top_of_the_scale()
    {
        var components = KarstificationIndex.Components(
            KarstificationIndex.CaveDensityReferencePerKm2,
            KarstificationIndex.DepressionAreaRatioReference);

        KarstificationIndex.Score(components)!.Value.ShouldBe(1d, 1e-9);
        KarstificationIndex.Classify(1d).ShouldBe(KarstificationClass.VeryHigh);
    }

    [Fact]
    public void Beyond_a_reference_the_component_stops_ranking_rather_than_running_away()
    {
        // Ten times the reference density is still one, so one exceptional area cannot flatten
        // every other area's class by stretching the top of the scale.
        var components = KarstificationIndex.Components(
            KarstificationIndex.CaveDensityReferencePerKm2 * 10d, null);

        components.Single(c => c.Name == KarstificationIndex.CaveDensityComponent)
            .Normalised.ShouldBe(1d);
    }

    [Fact]
    public void A_component_that_could_not_be_measured_is_absent_rather_than_zero()
    {
        // Half the reference density, and no depressions stored at all.
        var components = KarstificationIndex.Components(
            KarstificationIndex.CaveDensityReferencePerKm2 / 2d, depressionAreaRatio: null);

        var depression = components.Single(c => c.Name == KarstificationIndex.DepressionAreaRatioComponent);
        depression.Value.ShouldBeNull();
        depression.Normalised.ShouldBeNull();

        // The mean is taken over what was measured: 0.5, not 0.25. Folding the missing reading in
        // as a zero would rank an unmapped area below one genuinely without depressions.
        KarstificationIndex.Score(components)!.Value.ShouldBe(0.5d, 1e-9);

        // And the same density with a measured ratio of zero really does score lower — the two
        // cases are distinguishable, which is the whole point of the first assertion.
        var measuredZero = KarstificationIndex.Components(
            KarstificationIndex.CaveDensityReferencePerKm2 / 2d, depressionAreaRatio: 0d);
        KarstificationIndex.Score(measuredZero)!.Value.ShouldBe(0.25d, 1e-9);
    }

    [Fact]
    public void Nothing_measurable_is_unknown_rather_than_a_low_score()
    {
        var components = KarstificationIndex.Components(null, null);

        KarstificationIndex.Score(components).ShouldBeNull();
        KarstificationIndex.Classify(null).ShouldBe(KarstificationClass.Unknown);
    }

    [Theory]
    [InlineData(0d, KarstificationClass.VeryLow)]
    [InlineData(0.19d, KarstificationClass.VeryLow)]
    [InlineData(0.2d, KarstificationClass.Low)]
    [InlineData(0.4d, KarstificationClass.Moderate)]
    [InlineData(0.6d, KarstificationClass.High)]
    [InlineData(0.8d, KarstificationClass.VeryHigh)]
    public void The_classes_are_even_fifths_a_reader_can_reconstruct(double score, KarstificationClass expected) =>
        KarstificationIndex.Classify(score).ShouldBe(expected);
}
