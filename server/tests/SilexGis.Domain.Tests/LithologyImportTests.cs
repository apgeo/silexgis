// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Import;

namespace SilexGis.Domain.Tests;

public sealed class LithologyImportTests
{
    private static readonly Dictionary<string, string> NoMapping = [];

    [Theory]
    [InlineData("limestone", "limestone")]
    [InlineData("Calcar", "limestone")]
    [InlineData("  CALCARE ", "limestone")]
    [InlineData("Marmură", "marble")]
    [InlineData("Gips / anhidrit", "gypsum_anhydrite")]
    [InlineData("gypsum_anhydrite", "gypsum_anhydrite")]
    [InlineData("Șist cristalin", "crystalline_schist")]
    [InlineData("Ş ist cristalin", null)]
    [InlineData("gresie", "sandstone")]
    [InlineData("other", "other")]
    public void A_whole_value_is_recognised_by_a_code_or_a_plain_name(string value, string? expected)
    {
        var match = LithologyImport.Resolve(value, NoMapping);
        match.Code.ShouldBe(expected);
        match.Kind.ShouldBe(expected is null ? LithologyMatchKind.Unmatched : LithologyMatchKind.Recognised);
    }

    [Theory]
    [InlineData("limestone with chert")]
    [InlineData("conglomerate")]
    [InlineData("Altele")]
    [InlineData("")]
    [InlineData(null)]
    public void Part_of_a_value_an_ambiguous_word_or_nothing_is_not_guessed(string? value)
    {
        var match = LithologyImport.Resolve(value, NoMapping);
        match.Code.ShouldBeNull();
        match.Kind.ShouldBe(LithologyMatchKind.Unmatched);
    }

    [Fact]
    public void The_reviewers_mapping_is_read_first_and_compared_folded()
    {
        var mapping = new Dictionary<string, string>
        {
            ["Flis carpatic"] = "shale_marl",
            // Overrides what the value would be recognised as on its own.
            ["calcar"] = "dolomite",
        };

        LithologyImport.Resolve("FLIȘ  carpatic", mapping)
            .ShouldBe(new LithologyMatch("shale_marl", LithologyMatchKind.Mapped));
        LithologyImport.Resolve("Calcar", mapping)
            .ShouldBe(new LithologyMatch("dolomite", LithologyMatchKind.Mapped));
    }

    [Fact]
    public void A_mapping_to_a_code_outside_the_vocabulary_matches_nothing()
    {
        var mapping = new Dictionary<string, string> { ["Creta"] = "chalk" };
        LithologyImport.Resolve("Creta", mapping).Kind.ShouldBe(LithologyMatchKind.Unmatched);
    }

    [Fact]
    public void Every_class_is_recognised_by_its_own_code()
    {
        foreach (var cls in Lithology.Classes)
        {
            LithologyImport.Resolve(cls.Code, NoMapping).Code.ShouldBe(cls.Code);
        }
    }

    [Fact]
    public void The_rock_column_is_the_one_named_or_else_a_usual_name()
    {
        var row = new Dictionary<string, string?> { ["LITOLOGIE"] = "Calcar", ["ROCA"] = "Gresie" };
        SourceAttributeReader.ReadLithology(row, new ImportAttributeMapping()).ShouldBe("Calcar");
        SourceAttributeReader.ReadLithology(row, new ImportAttributeMapping { LithologyField = "ROCA" })
            .ShouldBe("Gresie");

        // A named column the row does not have answers nothing rather than falling back.
        SourceAttributeReader.ReadLithology(row, new ImportAttributeMapping { LithologyField = "rock" })
            .ShouldBeNull();
    }

    [Fact]
    public void Folding_ignores_case_diacritics_and_punctuation()
    {
        LithologyImport.Fold("Gips / Anhidrit").ShouldBe("gips anhidrit");
        LithologyImport.Fold("gypsum_anhydrite").ShouldBe("gypsum anhydrite");
        LithologyImport.Fold("--Șist--cristalin--").ShouldBe("sist cristalin");
    }
}
