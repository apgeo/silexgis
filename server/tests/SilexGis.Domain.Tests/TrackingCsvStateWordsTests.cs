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

    [Fact]
    public void A_note_has_a_word_too_in_both_languages_and_a_club_can_name_its_own()
    {
        Words.KindOf("nota").ShouldBe(TripPositionEventKind.Note);
        Words.KindOf("Notă").ShouldBe(TripPositionEventKind.Note);
        Words.KindOf("note").ShouldBe(TripPositionEventKind.Note);

        // Replaced on its own, like the other two: naming the note words says nothing about how
        // going in and coming out are written.
        var theirs = new TrackingCsvStateWords { Noted = ["mesaj"] };
        theirs.KindOf("mesaj").ShouldBe(TripPositionEventKind.Note);
        theirs.KindOf("nota").ShouldBeNull();
        theirs.KindOf("intrare").ShouldBe(TripPositionEventKind.Entered);
    }

    [Fact]
    public void The_mark_of_a_place_kept_back_is_never_a_listed_word_whatever_the_lists_say()
    {
        Words.KindOf(TrackingCsvStateWords.Withheld).ShouldBeNull();

        // Somebody who lists the mark as a word of their own does not get it read as one: a row
        // carrying it has a place the sheet does not say, and reading it as an entry, an exit or
        // a note would let a re-import write that over the place the log holds. The same lists
        // still read their other words, which is what shows the mark is what was refused.
        var careless = new TrackingCsvStateWords
        {
            WentIn = [TrackingCsvStateWords.Withheld, "jos"],
            CameOut = [TrackingCsvStateWords.Withheld, "sus"],
            Noted = [TrackingCsvStateWords.Withheld, "mesaj"],
        };
        careless.KindOf("RETINUT").ShouldBeNull();
        careless.KindOf("jos").ShouldBe(TripPositionEventKind.Entered);
        careless.KindOf("sus").ShouldBe(TripPositionEventKind.Exited);
        careless.KindOf("mesaj").ShouldBe(TripPositionEventKind.Note);
    }
}
