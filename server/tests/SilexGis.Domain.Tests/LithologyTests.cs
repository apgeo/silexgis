// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The lithology vocabulary and the per-class split. What is worth pinning is that "karstifiable"
/// is decided by the vocabulary and nothing else: an unknown code never adds ground or entrances to
/// the karstifiable totals, and a class with no outcrop has no density rather than a zero one.
/// </summary>
public class LithologyTests
{
    [Fact]
    public void The_carbonates_evaporites_and_calcareous_conglomerate_are_the_karstifiable_classes()
    {
        Lithology.Classes.Where(c => c.Karstifiable).Select(c => c.Code).ShouldBe(
        [
            "limestone", "dolomite", "marble", "gypsum_anhydrite", "salt", "conglomerate_calcareous",
        ]);
        Lithology.Classes.Count.ShouldBe(13);
        Lithology.Classes.Select(c => c.Code).Distinct().Count().ShouldBe(13);
    }

    [Fact]
    public void A_code_outside_the_vocabulary_is_not_found()
    {
        Lithology.Find("limestone")!.Karstifiable.ShouldBeTrue();
        Lithology.Find("granite")!.Karstifiable.ShouldBeFalse();
        Lithology.Find("Limestone").ShouldBeNull();
        Lithology.Find("chalk").ShouldBeNull();
        Lithology.Find(null).ShouldBeNull();
    }

    [Fact]
    public void The_seeded_schema_offers_exactly_the_vocabulary_and_no_position()
    {
        using var doc = JsonDocument.Parse(Lithology.PropertiesSchemaJson());
        var props = doc.RootElement.GetProperty("properties");

        props.EnumerateObject().Select(p => p.Name).ShouldBe(["lithology", "formation", "age", "source"]);
        props.GetProperty(Lithology.PropertyKey).GetProperty("enum").EnumerateArray()
            .Select(e => e.GetString()).ShouldBe(Lithology.Classes.Select(c => c.Code));
    }

    [Fact]
    public void The_split_gives_shares_densities_and_outcrop_normalised_karst_density()
    {
        // A 10 km² outline: 4 km² limestone with 8 entrances, 3 km² sandstone with 1, 2 km² of an
        // unrecognised code with 2, and 1 km² unmapped with 1.
        var breakdown = LithologyStatistics.Summarize(
        [
            new LithologyOutcrop("sandstone", 3_000_000, 1),
            new LithologyOutcrop("limestone", 4_000_000, 8),
            new LithologyOutcrop("chalk", 2_000_000, 2),
        ],
            outlineAreaM2: 10_000_000,
            unmapped: new LithologyOutcrop(null, 1_000_000, 1));

        breakdown.Classes.Select(c => c.Code).ShouldBe(["limestone", "sandstone", "chalk"]);

        var limestone = breakdown.Classes[0];
        limestone.AreaKm2.ShouldBe(4d, 1e-12);
        limestone.AreaShare!.Value.ShouldBe(0.4, 1e-12);
        limestone.EntrancesPerKm2!.Value.ShouldBe(2d, 1e-12);
        limestone.Karstifiable.ShouldBe(true);

        var chalk = breakdown.Classes[2];
        chalk.Recognised.ShouldBeFalse();
        chalk.Karstifiable.ShouldBeNull();

        breakdown.MappedAreaKm2.ShouldBe(9d, 1e-12);
        breakdown.UnmappedAreaKm2.ShouldBe(1d, 1e-12);
        breakdown.UnmappedEntranceCount.ShouldBe(1);

        // Only limestone is karstifiable here: the unknown code is not guessed into the total.
        breakdown.KarstifiableAreaKm2.ShouldBe(4d, 1e-12);
        breakdown.KarstifiableEntranceCount.ShouldBe(8);
        breakdown.KarstifiableEntrancesPerKm2!.Value.ShouldBe(2d, 1e-12);
    }

    [Fact]
    public void Without_karstifiable_ground_or_an_outline_area_the_densities_are_absent_not_zero()
    {
        var breakdown = LithologyStatistics.Summarize(
            [new LithologyOutcrop("granite", 0, 0), new LithologyOutcrop(null, 500_000, 0)],
            outlineAreaM2: null,
            unmapped: new LithologyOutcrop(null, 0, 0));

        breakdown.KarstifiableEntrancesPerKm2.ShouldBeNull();
        breakdown.Classes.ShouldAllBe(c => c.AreaShare == null);
        breakdown.Classes.Single(c => c.Code == "granite").EntrancesPerKm2.ShouldBeNull();
        breakdown.Classes.Single(c => c.Code == null).EntrancesPerKm2!.Value.ShouldBe(0d);
    }

    [Fact]
    public void A_negative_area_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => LithologyStatistics.Summarize(
            [new LithologyOutcrop("limestone", -1, 0)], 1, new LithologyOutcrop(null, 0, 0)));
    }
}
