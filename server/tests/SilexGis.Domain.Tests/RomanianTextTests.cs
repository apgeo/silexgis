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
        var spellings = RomanianText.SearchSpellings("Peștera Urșilor");

        spellings[0].ShouldBe("Peștera Urșilor", "what was typed is asked about first");
        spellings[1].ShouldBe("Pestera Ursilor", "and the unaccented form second, because much of the catalogue is typed that way");
        spellings.ShouldContain("Peştera Urşilor");
    }

    [Fact]
    public void A_term_typed_with_cedillas_asks_the_comma_below_spelling_too()
    {
        var spellings = RomanianText.SearchSpellings("Peştera Urşilor");

        spellings[0].ShouldBe("Peştera Urşilor");
        spellings.ShouldContain("Peștera Urșilor");
        spellings.ShouldContain("Pestera Ursilor");
    }

    [Fact]
    public void A_term_whose_letters_are_all_unambiguous_costs_exactly_one_spelling()
    {
        // Nothing in these can have been written another way, so there is nothing to ask twice —
        // and asking the same question twice is traffic somebody else's small service pays for.
        RomanianText.SearchSpellings("Rece").ShouldBe(["Rece"]);
        RomanianText.SearchSpellings("  Rece  ").ShouldBe(["Rece"]);
    }

    /// <summary>
    /// A vowel counts as ambiguous, and it has to: <c>Cetatile</c> is written <c>Cetățile</c> in
    /// the register, and the difference is a vowel as much as a consonant.
    /// </summary>
    [Fact]
    public void An_ordinary_looking_word_is_still_expanded_because_its_vowels_might_be_accented()
    {
        var spellings = RomanianText.SearchSpellings("Movile");

        spellings[0].ShouldBe("Movile");
        spellings.ShouldContain("Movîle");
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

    /// <summary>
    /// The direction that actually matters, and the one the first version of this got wrong: a
    /// keyboard makes people type <c>ursilor</c>, and the catalogue holds <c>Urșilor</c>.
    /// </summary>
    /// <remarks>
    /// Measured against the live catalogue, an unaccented term is not merely an incomplete
    /// question — it is usually no question at all. <c>Scarisoara</c>, <c>Topolnita</c> and
    /// <c>Cetatile</c> each return zero caves, while the accented spellings return several.
    /// </remarks>
    [Theory]
    [InlineData("ursilor", "urșilor")]
    [InlineData("ursilor", "urşilor")]
    [InlineData("pestera", "peștera")]
    [InlineData("padis", "padiș")]
    [InlineData("padis", "padiş")]
    [InlineData("topolnita", "topolnița")]
    [InlineData("topolnita", "topolniţa")]
    [InlineData("cetatile", "cetățile")]
    public void An_unaccented_term_asks_about_the_accented_spelling_the_register_uses(
        string typed, string expected)
    {
        RomanianText.SearchSpellings(typed).ShouldContain(expected);
    }

    /// <summary>
    /// The expansion is bounded, and cheap substitutions are spent before dear ones.
    /// </summary>
    /// <remarks>
    /// The ordering is the whole reason a two-substitution spelling like <c>cetățile</c> is
    /// reachable at all: counting substitutions alone would rank it behind every single-vowel
    /// spelling of the word, none of which is a cave. Consonants come first because theirs is the
    /// ambiguity that splits this catalogue, and because a word has few of them and many vowels.
    /// </remarks>
    [Fact]
    public void The_expansion_is_bounded_and_spends_its_width_on_consonants_first()
    {
        var spellings = RomanianText.SearchSpellings("cetatile");

        spellings.Count.ShouldBeLessThanOrEqualTo(RomanianText.MaxSpellings);
        spellings.ShouldBeUnique();

        // Every t-substitution is reached before the expansion runs out; the vowels follow.
        spellings.ShouldContain("cețatile");
        spellings.ShouldContain("ceţatile");
        spellings.ShouldContain("cetățile");
    }

    /// <summary>
    /// A word whose letters are all unambiguous is not expanded at all, so a search for one costs
    /// the far end exactly one field.
    /// </summary>
    [Fact]
    public void A_word_with_no_ambiguous_letter_is_not_expanded()
    {
        RomanianText.SearchSpellings("Rece").Count.ShouldBe(1);
        RomanianText.SearchSpellings("Vulcu").Count.ShouldBe(1);
    }
}
