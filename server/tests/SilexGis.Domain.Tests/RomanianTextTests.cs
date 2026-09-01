// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Catalogue;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The spellings one Romanian search term has to be asked about. The source catalogue holds all
/// three conventions — comma-below, cedilla, and none at all — and folds none of them together,
/// so a single literal query returns part of the answer and looks like the whole of it.
/// </summary>
public class RomanianTextTests
{
    [Fact]
    public void What_was_typed_is_asked_first_and_the_other_conventions_follow()
    {
        // Most faithful first: a search that quietly asked something other than what was typed is
        // worse than one that did not ask.
        RomanianText.SearchSpellings("Peștera Urșilor")
            .ShouldBe(["Peștera Urșilor", "Peştera Urşilor", "Pestera Ursilor"]);
    }

    [Fact]
    public void A_term_typed_with_cedillas_asks_the_comma_below_spelling_too()
    {
        RomanianText.SearchSpellings("Peştera Urşilor")
            .ShouldBe(["Peştera Urşilor", "Peștera Urșilor", "Pestera Ursilor"]);
    }

    [Fact]
    public void A_term_with_no_Romanian_letters_costs_exactly_one_spelling()
    {
        // Three of the four candidates are the same string here, and asking the catalogue the
        // same question three times is traffic somebody else's small service pays for.
        RomanianText.SearchSpellings("Movile").ShouldBe(["Movile"]);
        RomanianText.SearchSpellings("  Movile  ").ShouldBe(["Movile"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_typed_is_nothing_to_search_for(string? term)
    {
        RomanianText.SearchSpellings(term).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Peștera", "Pestera")]
    [InlineData("Peştera", "Pestera")]
    [InlineData("Șura Mare", "Sura Mare")]
    [InlineData("Şura Mare", "Sura Mare")]
    [InlineData("Țapului", "Tapului")]
    [InlineData("Ţapului", "Tapului")]
    [InlineData("Scărișoara", "Scarisoara")]
    [InlineData("Vântului", "Vantului")]
    [InlineData("Închisă", "Inchisa")]
    [InlineData("ăâîșşțţ", "aaisstt")]
    [InlineData("ĂÂÎȘŞȚŢ", "AAISSTT")]
    public void Both_diacritic_conventions_fold_to_the_plain_letters_underneath(string term, string expected)
    {
        RomanianText.ToAscii(term).ShouldBe(expected);
    }

    [Fact]
    public void Each_convention_rewrites_only_the_two_letters_that_differ_between_them()
    {
        RomanianText.ToCommaBelow("Peştera Ţapului").ShouldBe("Peștera Țapului");
        RomanianText.ToCedilla("Peștera Țapului").ShouldBe("Peştera Ţapului");

        // ă, â and î are written the same way under both conventions; only s and t ever differed.
        RomanianText.ToCommaBelow("Scărișoara").ShouldBe("Scărișoara");
        RomanianText.ToCedilla("Vântului").ShouldBe("Vântului");
    }
}
