// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Import;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The comparison a field GPS makes necessary. Every case here comes from a way real files
/// mangle Romanian: the comma-below letters written as cedillas by older Windows fonts, the
/// diacritics dropped entirely by units with an ASCII display, and everything upper-cased.
/// </summary>
public class TermMatchingTests
{
    [Theory]
    [InlineData("Peștera Ursilor")]   // comma below (the correct letter)
    [InlineData("Peştera Ursilor")]   // cedilla (what older fonts produced)
    [InlineData("Pestera Ursilor")]   // no diacritics at all
    [InlineData("PESTERA URSILOR")]   // upper case, as a handheld writes it
    [InlineData("peştera ursilor")]
    public void One_term_matches_every_way_a_unit_spells_it(string name)
    {
        TermMatcher.Match(name, ["peșteră", "peștera"], TermMatchMode.WholeWord).ShouldNotBeNull();
    }

    [Fact]
    public void Whole_word_refuses_a_term_buried_inside_another_word()
    {
        // "aven" inside "Avenue" is the case that makes a Contains rule useless.
        TermMatcher.Match("Avenue Street", ["aven"], TermMatchMode.WholeWord).ShouldBeNull();
        TermMatcher.Match("Avenul Negru", ["aven"], TermMatchMode.WholeWord).ShouldBeNull();
        TermMatcher.Match("Aven de la Scărișoara", ["aven"], TermMatchMode.WholeWord).ShouldNotBeNull();
    }

    [Fact]
    public void Contains_finds_what_whole_word_will_not()
    {
        TermMatcher.Match("Avenul Negru", ["aven"], TermMatchMode.Contains).ShouldNotBeNull();
    }

    [Fact]
    public void A_term_ending_in_a_dot_needs_no_word_boundary_after_it()
    {
        // "p." is how half of Romania labels a cave on a handheld. Requiring a non-word
        // character after the dot would mean it never matched at all.
        var match = TermMatcher.Match("P. Ursilor", ["p."], TermMatchMode.WholeWord);
        match.ShouldNotBeNull();
        match.Value.Start.ShouldBe(0);
        match.Value.Length.ShouldBe(2);
    }

    [Fact]
    public void Prefix_only_matches_where_the_name_opens()
    {
        TermMatcher.Match("P. Ursilor", ["p."], TermMatchMode.Prefix).ShouldNotBeNull();
        TermMatcher.Match("Izvorul p. mare", ["p."], TermMatchMode.Prefix).ShouldBeNull();

        // …which is exactly why the shipped abbreviation rule is a prefix rule: a bare "p."
        // in the middle of a label is far more often an abbreviation of something else.
        TermMatcher.Match("Izvorul p. mare", ["p."], TermMatchMode.Contains).ShouldNotBeNull();
    }

    [Fact]
    public void A_regular_expression_is_applied_to_the_folded_text()
    {
        // Written in plain ASCII and still matching an accented name is the point: a club
        // should not have to write character classes for every diacritic.
        TermMatcher.Match("Peștera nr. 42", [@"^pestera nr\. \d+$"], TermMatchMode.Regex).ShouldNotBeNull();
    }

    [Fact]
    public void An_unparseable_pattern_claims_nothing_and_is_refused_at_the_door()
    {
        TermMatcher.IsValidRegex("[unclosed").ShouldBeFalse();
        TermMatcher.Match("anything", ["[unclosed"], TermMatchMode.Regex).ShouldBeNull();

        TermMatcher.IsValidRegex(@"^p\.").ShouldBeTrue();
    }

    [Fact]
    public void The_earliest_match_wins_whatever_order_the_terms_are_listed_in()
    {
        // A set holding several spellings of one word must not depend on which was typed
        // first — the two orders below have to agree.
        var forwards = TermMatcher.Match("Doline mari peșteră", ["peșteră", "doline"], TermMatchMode.WholeWord);
        var backwards = TermMatcher.Match("Doline mari peșteră", ["doline", "peșteră"], TermMatchMode.WholeWord);

        forwards.ShouldNotBeNull();
        backwards.ShouldNotBeNull();
        forwards.Value.Start.ShouldBe(backwards.Value.Start);
        forwards.Value.Start.ShouldBe(0);
    }

    [Fact]
    public void Positions_are_reported_in_the_original_text_not_the_folded_one()
    {
        // The whole reason the folding keeps an index map: the span is used to cut the term
        // out of the name, and cutting at a folded offset would slice the wrong characters.
        var match = TermMatcher.Match("Peștera Ursilor", ["peștera"], TermMatchMode.WholeWord);
        match.ShouldNotBeNull();
        "Peștera Ursilor".Substring(match.Value.Start, match.Value.Length).ShouldBe("Peștera");
    }

    [Fact]
    public void Nothing_matches_an_empty_or_missing_name()
    {
        TermMatcher.Match(null, ["peșteră"], TermMatchMode.Contains).ShouldBeNull();
        TermMatcher.Match(string.Empty, ["peșteră"], TermMatchMode.Contains).ShouldBeNull();
        TermMatcher.Match("Peștera", [string.Empty], TermMatchMode.Contains).ShouldBeNull();
    }
}
