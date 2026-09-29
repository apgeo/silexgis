// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import.TrackingCsv;

namespace SilexGis.Domain.Tests;

/// <summary>Reading "went in" and "came out" off a sheet written in a club's own words.</summary>
public class TrackingCsvStateWordsTests
{
    private static readonly TrackingCsvStateWords Words = TrackingCsvStateWords.Default;

    [Fact]
    public void Both_languages_this_application_speaks_are_understood_out_of_the_box()
    {
        Words.KindOf("intrare").ShouldBe(TripPositionEventKind.Entered);
        Words.KindOf("ieșire").ShouldBe(TripPositionEventKind.Exited);
        Words.KindOf("went in").ShouldBe(TripPositionEventKind.Entered);
        Words.KindOf("went out").ShouldBe(TripPositionEventKind.Exited);
    }

    [Fact]
    public void A_word_containing_another_is_not_read_as_the_one_inside_it()
    {
        // "went out" contains "in" nowhere, but "went in" does contain "in" and an implementation
        // reaching for Contains would read "went out" as neither and "intrare" as an entry by
        // accident of substring. Equality on the folded word is what makes the two lists safe to
        // extend with whatever a club writes.
        Words.KindOf("went out").ShouldBe(TripPositionEventKind.Exited);
        Words.KindOf("out").ShouldBe(TripPositionEventKind.Exited);
        Words.KindOf("in").ShouldBe(TripPositionEventKind.Entered);

        // The words above are all list words, so they answer the same under equality and under
        // containment. These are not, and each one is what a containment test would get wrong:
        // a phrase with a list word inside it ("intrat" is one) would be read as that word, and a
        // fragment of a list word ("went" of "went out", "i" of "iesire") would be read as the
        // word it is a piece of. Each is a word nobody listed, so it names no standing and is
        // reported rather than guessed at.
        Words.KindOf("intrat in bivuac").ShouldBeNull();
        Words.KindOf("went").ShouldBeNull();
        Words.KindOf("i").ShouldBeNull();
    }

    [Fact]
    public void Diacritics_and_case_are_not_what_decides()
    {
        Words.KindOf("iesire").ShouldBe(TripPositionEventKind.Exited);
        Words.KindOf("IEȘIRE").ShouldBe(TripPositionEventKind.Exited);
        Words.KindOf(" Intrare ").ShouldBe(TripPositionEventKind.Entered);
    }

    [Fact]
    public void A_blank_column_and_an_unknown_word_both_name_no_standing()
    {
        // Null is the ordinary answer: most rows of a tracking sheet are places inside the cave.
        // An unrecognised word is also null rather than a guess — the row is then read by its
        // place columns, and the word is reported so somebody can add it to the lists.
        Words.KindOf(null).ShouldBeNull();
        Words.KindOf("").ShouldBeNull();
        Words.KindOf("la baza puțului").ShouldBeNull();
    }

    [Fact]
    public void A_club_can_name_its_own_words_and_the_defaults_stop_applying()
    {
        var theirs = new TrackingCsvStateWords { WentIn = ["plecare"], CameOut = ["revenire"] };
        theirs.KindOf("plecare").ShouldBe(TripPositionEventKind.Entered);
        theirs.KindOf("revenire").ShouldBe(TripPositionEventKind.Exited);
        // Named words are an instruction, so the built-in ones are not also in force: a sheet whose
        // "intrare" column means something else to that club must not be read as an entry.
        theirs.KindOf("intrare").ShouldBeNull();
    }
}
