// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Import;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What a source row says about itself once the mapping is applied, and the one JSON shape every
/// stored import document is written in. Every row an import reads goes through the first, and
/// every review, rule set and queued confirmation through the second, so a quiet change to either
/// renames or mis-reads a whole import at once.
/// </summary>
public class SourceAttributeReaderTests
{
    private static readonly ImportAttributeMapping Detect = new();

    [Fact]
    public void A_chosen_column_the_row_does_not_have_yields_nothing_rather_than_another_column()
    {
        var row = new Dictionary<string, string?> { ["name"] = "Peștera Ursilor", ["desc"] = "o descriere" };

        var read = SourceAttributeReader.Read(row, new ImportAttributeMapping { NameField = "denumire" });

        // The importer named a column; quietly reading "name" instead is how a whole import ends
        // up named after the wrong thing.
        read.Name.ShouldBeNull();
        read.Description.ShouldBe("o descriere");
    }

    [Fact]
    public void A_chosen_column_is_read_when_the_row_has_it()
    {
        var row = new Dictionary<string, string?> { ["name"] = "Wrong", ["denumire"] = "  Avenul din Șesuri  " };

        SourceAttributeReader.Read(row, new ImportAttributeMapping { NameField = "denumire" })
            .Name.ShouldBe("Avenul din Șesuri");
    }

    [Fact]
    public void Columns_are_found_by_folded_key_whatever_their_case()
    {
        var row = new Dictionary<string, string?> { ["NAME"] = "Izbucul Tăuz", ["Description"] = "un izbuc", ["COD"] = "4111/1" };

        var read = SourceAttributeReader.Read(row, Detect);

        read.Name.ShouldBe("Izbucul Tăuz");
        read.Description.ShouldBe("un izbuc");
        read.Code.ShouldBe("4111/1");
    }

    [Fact]
    public void A_description_that_only_repeats_the_name_is_dropped()
    {
        var row = new Dictionary<string, string?> { ["name"] = "Dolina Mare", ["desc"] = "Dolina Mare" };

        var read = SourceAttributeReader.Read(row, Detect);

        read.Name.ShouldBe("Dolina Mare");
        read.Description.ShouldBeNull();
    }

    [Fact]
    public void A_blank_value_is_no_value_and_the_next_candidate_answers()
    {
        var row = new Dictionary<string, string?> { ["name"] = "   ", ["title"] = "Peștera din Văi" };

        SourceAttributeReader.Read(row, Detect).Name.ShouldBe("Peștera din Văi");
    }

    [Theory]
    [InlineData("1234,5", 1234.5)]
    [InlineData("1234.5", 1234.5)]
    [InlineData("987", 987.0)]
    public void An_elevation_reads_with_either_decimal_mark(string text, double expected)
    {
        var row = new Dictionary<string, string?> { ["name"] = "P", ["ele"] = text };

        SourceAttributeReader.Read(row, new ImportAttributeMapping { ElevationField = "ele" })
            .Elevation!.Value.ShouldBe(expected, 1e-9);
    }

    [Fact]
    public void An_elevation_that_is_not_a_number_is_no_elevation()
    {
        var row = new Dictionary<string, string?> { ["name"] = "P", ["ele"] = "high" };

        SourceAttributeReader.Read(row, new ImportAttributeMapping { ElevationField = "ele" }).Elevation.ShouldBeNull();
    }

    [Fact]
    public void A_decision_survives_the_stored_shape_with_its_enums_spelled_as_words()
    {
        var target = Guid.NewGuid();
        var decision = new ImportDecision
        {
            Action = ImportDecisionAction.Attach,
            Kind = ImportTargetKind.CaveEntrance,
            EntranceTypeCode = "natural",
            Name = "Intrarea de sus",
            AttachToFeatureId = target,
            KeepElevation = false,
        };

        var json = ImportJson.Serialize(new Dictionary<string, ImportDecision> { ["17"] = decision });

        // Words, not numbers: a stored review read back after an enum gained a member must still
        // mean what it meant, and nulls are left out rather than written.
        json.ShouldContain("\"action\":\"attach\"");
        json.ShouldContain("\"kind\":\"caveEntrance\"");
        json.ShouldNotContain("null");

        var back = ImportJson.Deserialize<Dictionary<string, ImportDecision>>(json)!;
        back["17"].ShouldBe(decision);
    }
}
