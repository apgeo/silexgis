// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
using Shouldly;
using SilexGis.Domain.Catalogue;

namespace SilexGis.Domain.Tests;

/// <summary>
/// One record of the Romanian community catalogue, read as the fields of a cave here. Two of the
/// rules below are prohibitions rather than translations — an altitude and a locality are the
/// two pieces of a foreign record that location protection is about — and the rest are the small
/// disagreements between what that register writes and what these columns hold.
/// </summary>
public class SpeologieMappingTests
{
    /// <summary>The description budget the import runs with, so these read as the real one does.</summary>
    private const int Budget = 20_000;

    private static readonly DateTimeOffset RetrievedAt = new(2026, 9, 1, 19, 30, 57, TimeSpan.Zero);

    /// <summary>A record with nothing on it but the two fields the catalogue always fills.</summary>
    private static readonly SpeologieRecord Bare = new(4211, "Peștera Urșilor");

    private static SpeologieCaveValues Map(SpeologieRecord record, int maxDescriptionChars = Budget) =>
        SpeologieMapping.ToCaveValues(record, RetrievedAt, maxDescriptionChars);

    [Fact]
    public void An_altitude_never_appears_in_the_properties_bag_under_any_key()
    {
        // The bag is serialised straight onto the wire, including on the path that answers a
        // share link to somebody who has not signed in, and there is no protection filter along
        // that path to extend. An altitude is a coordinate component: one parked in the bag would
        // route around the whole location-protection mechanism for every cave that carried it.
        //
        // Asserted against the serialised bag rather than against a list of keys, because the
        // rule is about the value leaving, not about the name it would leave under.
        var values = Map(Bare with { Altitudine = 1387.5 });

        values.Altitude.ShouldBe(1387.5m);

        values.Properties.ToJsonString().ShouldNotContain("1387");
        values.Properties.ShouldNotContain(p => p.Value!.ToJsonString().Contains("1387", StringComparison.Ordinal));
        values.Properties.Select(p => p.Key)
            .ShouldNotContain(k => k.Contains("altitud", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_locality_goes_to_the_redacted_column_and_nowhere_else()
    {
        // closest_address is withheld from a caller who may not see a protected cave's exact
        // position; the properties bag and the description are not. Naming the village a
        // protected cave sits above, in a field shown to everyone, gives away most of what the
        // protection exists to withhold.
        //
        // The locality here is deliberately plain ASCII: a name with diacritics in it would be
        // escaped to \uXXXX in the serialised bag, and the check would then pass whether or not
        // the value was there.
        var values = Map(Bare with
        {
            Localitate = "Pietroasa",
            Descriere = "<p>Galerie fosilă cu blocuri prăbușite.</p>",
        });

        values.ClosestAddress.ShouldBe("Pietroasa");

        values.Properties.ToJsonString().ShouldNotContain("Pietroasa");
        values.Description.ShouldNotBeNull();
        values.Description.ShouldNotContain("Pietroasa");
    }

    [Theory]
    [InlineData("clasaA", "A")]
    [InlineData("clasaB", "B")]
    [InlineData("clasaC", "C")]
    [InlineData("CLASAB", "B")]
    [InlineData("rezervație științifică", "rezervație științifică")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void A_protection_class_becomes_its_bare_letter_and_anything_else_is_carried_through(
        string? raw, string? expected)
    {
        // The column is free text everywhere else in the application, so a value nobody
        // recognises is kept rather than dropped: it is still better than a value nobody has.
        Map(Bare with { Clasificare = raw }).ProtectionClass.ShouldBe(expected);
    }

    [Fact]
    public void The_downward_range_is_stored_as_a_magnitude_whatever_sign_the_source_used()
    {
        // The catalogue's sign convention for it is not consistent: most rows are positive and at
        // least one is negative, and both mean the same distance downwards.
        Map(Bare with { DenNegativa = -220 }).NegativeDepth.ShouldBe(220m);
        Map(Bare with { DenNegativa = 220 }).NegativeDepth.ShouldBe(220m);
        Map(Bare with { DenNegativa = null }).NegativeDepth.ShouldBeNull();
    }

    [Fact]
    public void A_metre_value_is_rounded_to_what_the_column_holds()
    {
        var values = Map(Bare with { Lungime = 1234.567, Denivelare = 88.005, Altitudine = 1387 });

        values.SurveyedLength.ShouldBe(1234.57m);
        values.Depth.ShouldBe(88.01m);
        values.Altitude.ShouldBe(1387m);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(1e12)]
    [InlineData(-1e12)]
    [InlineData(1e30)]
    public void A_metre_value_the_column_cannot_hold_empties_the_field_rather_than_failing(double metres)
    {
        // The source is a community register and a typo in it should cost one field, not the
        // whole cave — a save that throws refuses every cave in the confirmation, including the
        // ones that were fine.
        var values = Map(Bare with { Lungime = metres, Denivelare = metres, Altitudine = metres });

        values.SurveyedLength.ShouldBeNull();
        values.Depth.ShouldBeNull();
        values.Altitude.ShouldBeNull();
    }

    [Fact]
    public void The_same_scientific_interests_normalise_to_the_same_string_however_they_were_spaced()
    {
        // Both spacings occur in the catalogue, and unnormalised they are two different values
        // for one set of interests.
        var tight = Map(Bare with { Stiinta = "mineralogica,ursus" });
        var loose = Map(Bare with { Stiinta = "mineralogica, ursus" });

        var science = tight.Properties[SpeologieMapping.Keys.Science]!.GetValue<string>();

        science.ShouldBe("mineralogica, ursus");
        loose.Properties[SpeologieMapping.Keys.Science]!.GetValue<string>().ShouldBe(science);
        tight.Description.ShouldNotBeNull();
        tight.Description.ShouldContain("Științific: mineralogica, ursus");
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public void The_sump_flag_arrives_as_text_and_is_stored_as_a_boolean(string raw, bool expected)
    {
        Map(Bare with { Scufundabila = raw }).Properties[SpeologieMapping.Keys.Sump]!
            .GetValue<bool>().ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("x")]
    public void Anything_that_is_not_a_sump_answer_leaves_the_key_out(string? raw)
    {
        Map(Bare with { Scufundabila = raw }).Properties
            .ContainsKey(SpeologieMapping.Keys.Sump).ShouldBeFalse();
    }

    [Fact]
    public void The_bag_carries_the_catalogue_id_as_text_and_omits_what_the_catalogue_did_not_say()
    {
        var values = Map(Bare);

        // Text rather than a number: the lookup that finds an already-imported cave reads the id
        // out of the jsonb document as text, and the index it uses is built on that expression.
        values.Properties[SpeologieMapping.Keys.Id]!.GetValue<string>().ShouldBe("4211");
        values.Properties.ToJsonString().ShouldContain("\"speologieId\":\"4211\"");

        // An absent value is an absent key. A key present with a null reads as "the catalogue
        // says this is empty", which is a stronger claim than the catalogue made.
        values.Properties.ToJsonString().ShouldNotContain("null");
        values.Properties.Select(p => p.Key).ShouldBe(
            [SpeologieMapping.Keys.Id, SpeologieMapping.Keys.RetrievedAt], ignoreOrder: true);
    }

    [Fact]
    public void The_catalogue_s_own_codes_are_carried_verbatim_under_their_own_keys()
    {
        var values = Map(Bare with
        {
            Slug = "pestera-ursilor",
            Judet = "bh",
            NrHidro = "3.14",
            BazinHidroId = 7,
            Roca = "00",
            CodAp = "2.613",
            Disparuta = true,
        });

        values.Properties[SpeologieMapping.Keys.County]!.GetValue<string>().ShouldBe("BH");
        values.Properties[SpeologieMapping.Keys.HydroNumber]!.GetValue<string>().ShouldBe("3.14");
        values.Properties[SpeologieMapping.Keys.HydroBasinId]!.GetValue<int>().ShouldBe(7);
        // No legend for the rock code is published anywhere, so it is carried rather than guessed
        // at: a field that looks surveyed and is not is worse than an empty one.
        values.Properties[SpeologieMapping.Keys.RockCode]!.GetValue<string>().ShouldBe("00");
        values.Properties[SpeologieMapping.Keys.ProtectedAreaCode]!.GetValue<string>().ShouldBe("2.613");
        values.Properties[SpeologieMapping.Keys.Vanished]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public void The_public_page_is_built_from_the_slug_and_a_record_without_one_gets_no_link()
    {
        var linked = Map(Bare with { Slug = "pestera-ursilor" });

        linked.Website.ShouldBe("https://www.speologie.org/pestera-ursilor");
        linked.Properties[SpeologieMapping.Keys.Url]!.GetValue<string>().ShouldBe(linked.Website);
        linked.Properties[SpeologieMapping.Keys.Slug]!.GetValue<string>().ShouldBe("pestera-ursilor");

        // No slug means no public page. A guessed address would be a link to nothing.
        var unlinked = Map(Bare with { Slug = "   " });

        unlinked.Website.ShouldBeNull();
        unlinked.Properties.ContainsKey(SpeologieMapping.Keys.Url).ShouldBeFalse();
        unlinked.Properties.ContainsKey(SpeologieMapping.Keys.Slug).ShouldBeFalse();
    }

    [Fact]
    public void A_record_with_no_description_still_says_where_it_came_from()
    {
        Map(Bare).Description.ShouldBe("— speologie.org #4211");
    }

    [Fact]
    public void The_footer_survives_a_description_that_had_to_be_cut()
    {
        // Losing the line that says where a record came from would be the worst possible
        // truncation: the cave would keep the text and lose the only way to check it.
        var record = Bare with
        {
            Slug = "pestera-ursilor",
            Judet = "BH",
            Descriere = "<p>" + string.Concat(Enumerable.Repeat("galerie ", 500)) + "</p>",
        };

        var values = Map(record, maxDescriptionChars: 200);

        values.Description.ShouldNotBeNull();
        values.Description.ShouldContain(HtmlToText.TruncationMarker);
        values.Description.ShouldContain("speologie.org #4211");
        values.Description.ShouldContain("https://www.speologie.org/pestera-ursilor");
        values.Description.ShouldContain("Județ: BH");
        values.Description.ShouldEndWith("Județ: BH");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_record_whose_title_is_empty_still_produces_a_usable_name(string title)
    {
        // The name is what a person finds the cave by, and the catalogue's id is the one thing
        // the record certainly has.
        Map(new SpeologieRecord(4211, title)).Name.ShouldBe("#4211");
    }

    [Fact]
    public void A_refresh_rewrites_this_integration_s_keys_and_leaves_every_other_one_alone()
    {
        var existing = new JsonObject
        {
            ["surveyFile"] = "ursilor.th",                        // another integration's
            ["notes"] = "verified on the ground",                 // somebody's own
            [SpeologieMapping.Keys.County] = "GJ",                // ours, and out of date
            [SpeologieMapping.Keys.ProtectedAreaCode] = "2.613",  // ours, and retracted at the source
        }.ToJsonString();

        var merged = JsonNode.Parse(
            SpeologieMapping.MergeProperties(existing, Map(Bare with { Judet = "BH" }).Properties))!.AsObject();

        merged["surveyFile"]!.GetValue<string>().ShouldBe("ursilor.th");
        merged["notes"]!.GetValue<string>().ShouldBe("verified on the ground");
        merged[SpeologieMapping.Keys.County]!.GetValue<string>().ShouldBe("BH");

        // A code the catalogue no longer publishes has to disappear here too, or the cave goes on
        // asserting something the source has retracted.
        merged.ContainsKey(SpeologieMapping.Keys.ProtectedAreaCode).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("[1, 2, 3]")]
    public void A_bag_that_cannot_be_read_does_not_strand_the_cave(string? existing)
    {
        // A bag that will not parse is a defect somewhere else. Refusing the refresh over it
        // would leave the cave permanently unrefreshable.
        var merged = JsonNode.Parse(
            SpeologieMapping.MergeProperties(existing, Map(Bare).Properties))!.AsObject();

        merged[SpeologieMapping.Keys.Id]!.GetValue<string>().ShouldBe("4211");
        merged.ContainsKey(SpeologieMapping.Keys.RetrievedAt).ShouldBeTrue();
    }
}
