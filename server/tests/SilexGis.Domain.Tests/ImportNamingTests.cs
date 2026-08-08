// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Import;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The name an imported object ends up with, and how alike two names have to be before
/// duplicate detection calls them the same thing.
/// </summary>
public class ImportNamingTests
{
    private static TermMatch MatchIn(string name, string term)
    {
        var match = TermMatcher.Match(name, [term], TermMatchMode.Contains);
        match.ShouldNotBeNull();
        return match.Value;
    }

    [Fact]
    public void The_identifying_term_comes_out_of_the_front_of_the_name()
    {
        ImportNameCleaner.Apply("P. Ursilor", MatchIn("P. Ursilor", "p."), TermStripMode.Leading)
            .ShouldBe("Ursilor");
        ImportNameCleaner.Apply("Peștera Ursilor", MatchIn("Peștera Ursilor", "peștera"), TermStripMode.Leading)
            .ShouldBe("Ursilor");
    }

    [Fact]
    public void A_leading_strip_leaves_a_term_that_is_not_leading_alone()
    {
        // "Ursilor cave" under a leading rule keeps its word: the rule says where the term is
        // expected, and quietly stripping it from anywhere would rename half an import.
        ImportNameCleaner.Apply("Ursilor cave", MatchIn("Ursilor cave", "cave"), TermStripMode.Leading)
            .ShouldBe("Ursilor cave");
        ImportNameCleaner.Apply("Ursilor cave", MatchIn("Ursilor cave", "cave"), TermStripMode.Trailing)
            .ShouldBe("Ursilor");
    }

    [Fact]
    public void Stripping_from_the_middle_leaves_no_double_space_and_no_empty_brackets()
    {
        ImportNameCleaner.Apply("Ursilor (peșteră)", MatchIn("Ursilor (peșteră)", "peșteră"), TermStripMode.Anywhere)
            .ShouldBe("Ursilor");
        ImportNameCleaner.Apply("Ursilor cave nord", MatchIn("Ursilor cave nord", "cave"), TermStripMode.Anywhere)
            .ShouldBe("Ursilor nord");
    }

    [Fact]
    public void A_name_that_is_nothing_but_the_term_keeps_its_only_label()
    {
        // Stripping "Peștera" out of a waypoint called exactly that would leave an object with
        // no name at all, which is worse than a vague one.
        ImportNameCleaner.Apply("Peștera", MatchIn("Peștera", "peștera"), TermStripMode.Leading)
            .ShouldBe("Peștera");
    }

    [Fact]
    public void Nothing_is_stripped_when_the_rule_does_not_ask()
    {
        ImportNameCleaner.Apply("P. Ursilor", MatchIn("P. Ursilor", "p."), TermStripMode.None)
            .ShouldBe("P. Ursilor");
    }

    [Fact]
    public void A_prefix_is_applied_with_exactly_one_space_and_an_empty_one_changes_nothing()
    {
        ImportNameCleaner.WithPrefix("Bihor", "Ursilor").ShouldBe("Bihor Ursilor");
        ImportNameCleaner.WithPrefix("  Bihor  ", " Ursilor ").ShouldBe("Bihor Ursilor");
        ImportNameCleaner.WithPrefix(null, "Ursilor").ShouldBe("Ursilor");
        ImportNameCleaner.WithPrefix("   ", "Ursilor").ShouldBe("Ursilor");
        ImportNameCleaner.WithPrefix("Bihor", null).ShouldBe("Bihor");
    }

    [Fact]
    public void Name_similarity_folds_before_it_compares()
    {
        NameSimilarity.Of("Peștera Ursilor", "Pestera Ursilor").ShouldBe(1);
        NameSimilarity.Of("Peștera Ursilor", "PESTERA URSILOR").ShouldBe(1);
        NameSimilarity.Of("Ursilor", "Ursilor 2").ShouldBeGreaterThan(0.7);
        NameSimilarity.Of("Ursilor", "Scărișoara").ShouldBeLessThan(0.4);
    }

    [Fact]
    public void An_unnamed_candidate_is_not_a_perfect_match_for_an_unnamed_feature()
    {
        // Scoring two blanks as identical would make every nameless waypoint a duplicate of
        // every nameless feature within the radius.
        NameSimilarity.Of(null, null).ShouldBe(0);
        NameSimilarity.Of(string.Empty, "Ursilor").ShouldBe(0);
    }
}
