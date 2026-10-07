// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Import.TrackingCsv;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Matching a name written on a tracking sheet against the roster.
/// </summary>
/// <remarks>
/// A tracking sheet is typed while somebody is on the phone, so the same person is written three
/// ways on one sheet. What these hold down is that all three find them, and — the half that
/// matters — that a name several people answer to is never resolved by taking one of them.
/// </remarks>
public class CaverNameLadderTests
{
    private static readonly (int Key, string? Name)[] Roster =
    [
        (1, "Ion Popescu"),
        (2, "Ion Marinescu"),
        (3, "Ana Georgescu"),
        (4, "Ștefan Rădulescu"),
    ];

    private static IReadOnlyList<CaverNameLadder.Hit<int>> Match(string written) =>
        CaverNameLadder.Match(written, Roster);

    [Fact]
    public void A_whole_name_finds_its_person()
    {
        var hit = Match("Ion Popescu").ShouldHaveSingleItem();
        hit.Key.ShouldBe(1);
        hit.By.ShouldBe(CaverNameLadder.Rung.FullName);
    }

    [Fact]
    public void A_given_name_and_an_initial_finds_the_one_person_it_can_mean()
    {
        // Two Ions on the roster, so the given name alone could not answer — the initial is what
        // makes it one person, which is exactly why a sheet starts writing it.
        var hit = Match("Ion P.").ShouldHaveSingleItem();
        hit.Key.ShouldBe(1);
        hit.By.ShouldBe(CaverNameLadder.Rung.GivenNameAndInitial);

        var other = Match("Ion M.").ShouldHaveSingleItem();
        other.Key.ShouldBe(2);
    }

    [Fact]
    public void A_given_name_alone_finds_its_person_when_only_one_answers()
    {
        var hit = Match("Ana").ShouldHaveSingleItem();
        hit.Key.ShouldBe(3);
        hit.By.ShouldBe(CaverNameLadder.Rung.GivenName);
    }

    [Fact]
    public void A_given_name_several_people_answer_to_is_ambiguous_and_is_not_resolved()
    {
        // <b>The load-bearing one.</b> Both Ions answer to "Ion", and the answer is both of them,
        // for somebody to settle. Returning one would file a report against a caver who may not
        // have been underground — the failure this whole ladder is arranged to avoid.
        var hits = Match("Ion");
        hits.Count.ShouldBe(2);
        hits.Select(h => h.Key).ShouldBe([1, 2], ignoreOrder: true);
        hits.ShouldAllBe(h => h.By == CaverNameLadder.Rung.GivenName);
    }

    [Fact]
    public void A_looser_rung_is_not_tried_after_a_narrower_one_has_answered()
    {
        // "Ion P." is answered by the initial rung, so the given-name rung — which would have
        // answered with two people — is never reached. Asserted beside the ambiguous case above,
        // because the difference between them is the whole point of stopping at the first rung
        // that answers.
        Match("Ion P.").Count.ShouldBe(1);
        Match("Ion").Count.ShouldBe(2);
    }

    [Fact]
    public void A_specific_name_the_roster_disagrees_with_is_not_shortened_until_it_matches()
    {
        // The sheet wrote a surname and the roster has nobody by it. Falling back to "Ion" would
        // turn a name the sheet was specific about into a guess between two people.
        Match("Ion Vasilescu").ShouldBeEmpty();
    }

    [Fact]
    public void Diacritics_and_spacing_are_not_what_stops_a_name_matching()
    {
        // One folder for the whole project: a sheet exported without diacritics, or with a doubled
        // space, is the same name. This is the fault that makes an importer look like it works
        // while proposing nothing.
        Match("Stefan Radulescu").ShouldHaveSingleItem().Key.ShouldBe(4);
        Match("Ștefan   Rădulescu").ShouldHaveSingleItem().Key.ShouldBe(4);
        Match("ȘTEFAN RĂDULESCU").ShouldHaveSingleItem().Key.ShouldBe(4);
    }

    [Fact]
    public void A_roster_that_lists_one_person_twice_answers_with_them_once()
    {
        // One row per person per job is how a trip's roster is kept, so the same key arrives twice
        // for somebody with two jobs. Two hits under one key are one person named twice, not two
        // people to choose between — and the difference is whether the trip leader can be imported.
        (int Key, string? Name)[] repeated =
        [
            (1, "Ion Popescu"),
            (1, "Ion Popescu"),
            (3, "Ana Georgescu"),
        ];

        CaverNameLadder.Match("Ion Popescu", repeated).ShouldHaveSingleItem().Key.ShouldBe(1);
        CaverNameLadder.Match("Ion", repeated).ShouldHaveSingleItem().Key.ShouldBe(1);
        CaverNameLadder.Match("Ion P.", repeated).ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void A_person_handed_in_under_two_names_is_found_by_either_and_is_one_person()
    {
        // The roster's entry and the name the person's account goes by: two spellings, one key.
        // A sheet may have been written in either, and a short form both spellings answer to is
        // still one person and not a choice between two.
        (int Key, string? Name)[] twoNames =
        [
            (1, "Ion Popescu"),
            (1, "Ion P. Speologul"),
            (3, "Ana Georgescu"),
        ];

        CaverNameLadder.Match("Ion Popescu", twoNames).ShouldHaveSingleItem().Key.ShouldBe(1);
        var byAccount = CaverNameLadder.Match("Ion P. Speologul", twoNames).ShouldHaveSingleItem();
        byAccount.Key.ShouldBe(1);
        byAccount.By.ShouldBe(CaverNameLadder.Rung.FullName);
        byAccount.Name.ShouldBe("Ion P. Speologul");
        CaverNameLadder.Match("Ion", twoNames).ShouldHaveSingleItem().Key.ShouldBe(1);

        // The twin: a name two different people answer to is still a question, whichever of
        // their spellings it was found under.
        (int Key, string? Name)[] shared = [(1, "Ion Popescu"), (1, "Nelu"), (2, "Nelu")];
        CaverNameLadder.Match("Nelu", shared).Select(h => h.Key).ShouldBe([1, 2]);
    }

    [Fact]
    public void A_whole_name_written_surname_first_finds_its_person()
    {
        // A register is kept surname first and a roster given name first. Both are the whole
        // name, and the rung says which of the two found the person so a reviewer can be shown
        // that the order was not the roster's.
        var hit = Match("Popescu Ion").ShouldHaveSingleItem();
        hit.Key.ShouldBe(1);
        hit.By.ShouldBe(CaverNameLadder.Rung.FullNameAnyOrder);

        Match("Radulescu Stefan").ShouldHaveSingleItem().Key.ShouldBe(4);

        (int Key, string? Name)[] threeWords = [(7, "Ana Maria Pop"), (8, "Ana Pop")];
        var reordered = CaverNameLadder.Match("Pop Ana Maria", threeWords).ShouldHaveSingleItem();
        reordered.Key.ShouldBe(7);
        reordered.By.ShouldBe(CaverNameLadder.Rung.FullNameAnyOrder);
    }

    [Fact]
    public void A_name_in_the_rosters_own_order_is_never_widened_to_the_same_words_in_another()
    {
        // Two people whose names are each other's mirror. Each is found by their own spelling and
        // by nothing else: the any-order rung would answer both, and it is not reached, because
        // the narrower one already answered. The stop rule, seen from the new rung's side.
        (int Key, string? Name)[] mirrored = [(1, "Ion Popescu"), (5, "Popescu Ion")];

        var first = CaverNameLadder.Match("Ion Popescu", mirrored).ShouldHaveSingleItem();
        first.Key.ShouldBe(1);
        first.By.ShouldBe(CaverNameLadder.Rung.FullName);

        CaverNameLadder.Match("Popescu Ion", mirrored).ShouldHaveSingleItem().Key.ShouldBe(5);
    }

    [Fact]
    public void A_reordered_name_several_people_answer_to_is_ambiguous_and_is_not_resolved()
    {
        // Neither person spells their name this way, and both are made of exactly these words.
        (int Key, string? Name)[] roster = [(7, "Ana Maria Pop"), (8, "Maria Ana Pop")];

        var hits = CaverNameLadder.Match("Pop Ana Maria", roster);

        hits.Select(h => h.Key).ShouldBe([7, 8], ignoreOrder: true);
        hits.ShouldAllBe(h => h.By == CaverNameLadder.Rung.FullNameAnyOrder);

        // Beside it, the case that does answer: written as one of them writes it, it is that one.
        CaverNameLadder.Match("Maria Ana Pop", roster).ShouldHaveSingleItem().Key.ShouldBe(8);
    }

    [Fact]
    public void A_reordered_name_that_answers_with_several_is_not_passed_over_for_a_looser_rung()
    {
        // Contrived on purpose, to hold the stop rule where the new rung meets the old ones. Two
        // roster rows were typed as an initial and a name, and the sheet writes "Ion P.". Read as
        // a whole name in another order it is both of them; read as a given name and an initial
        // it would be Ion Popescu alone. The whole-name reading comes first and answers with two,
        // so the walk stops there — falling through would turn "two people answer to this" into a
        // confident match on a third.
        (int Key, string? Name)[] roster = [(1, "Ion Popescu"), (10, "P. Ion"), (11, "P Ion")];

        var hits = CaverNameLadder.Match("Ion P.", roster);

        hits.Select(h => h.Key).ShouldBe([10, 11], ignoreOrder: true);
        hits.ShouldAllBe(h => h.By == CaverNameLadder.Rung.FullNameAnyOrder);

        // Without the two rows that made it ambiguous, the initial rung is reached and answers.
        (int Key, string? Name)[] plain = [(1, "Ion Popescu")];
        var hit = CaverNameLadder.Match("Ion P.", plain).ShouldHaveSingleItem();
        hit.Key.ShouldBe(1);
        hit.By.ShouldBe(CaverNameLadder.Rung.GivenNameAndInitial);
    }

    [Fact]
    public void An_initial_and_a_surname_finds_the_one_person_it_can_mean()
    {
        // Two Ions, one Popescu: the surname is what makes it one person here, as the initial
        // does on the rung this mirrors.
        var hit = Match("I. Popescu").ShouldHaveSingleItem();
        hit.Key.ShouldBe(1);
        hit.By.ShouldBe(CaverNameLadder.Rung.InitialAndSurname);

        // The same written name three ways: without the stop, without the space, in capitals.
        Match("I Popescu").ShouldHaveSingleItem().Key.ShouldBe(1);
        Match("I.Popescu").ShouldHaveSingleItem().Key.ShouldBe(1);
        Match("S. RADULESCU").ShouldHaveSingleItem().Key.ShouldBe(4);
    }

    [Fact]
    public void An_initial_and_a_surname_several_people_answer_to_is_ambiguous_and_is_not_resolved()
    {
        // Ion and Ioana Popescu both sign "I. Popescu". The answer is both, for somebody to
        // settle; a third Popescu whose given name starts otherwise is not among them, which is
        // what shows the initial was read at all.
        (int Key, string? Name)[] roster =
        [
            (1, "Ion Popescu"),
            (6, "Ioana Popescu"),
            (9, "Maria Popescu"),
        ];

        var hits = CaverNameLadder.Match("I. Popescu", roster);

        hits.Select(h => h.Key).ShouldBe([1, 6], ignoreOrder: true);
        hits.ShouldAllBe(h => h.By == CaverNameLadder.Rung.InitialAndSurname);

        CaverNameLadder.Match("M. Popescu", roster).ShouldHaveSingleItem().Key.ShouldBe(9);
    }

    [Fact]
    public void An_initial_narrows_a_surname_down_and_never_stands_in_for_one()
    {
        // The surname has to be the whole of it: "I. Pop" is somebody called Pop, and there is
        // nobody by that name here. Two initials are no name at all.
        Match("I. Pop").ShouldBeEmpty();
        Match("I. P.").ShouldBeEmpty();
        // And the initial has to fit: there is a Georgescu, and her given name is Ana.
        Match("I. Georgescu").ShouldBeEmpty();
    }

    [Fact]
    public void Nothing_written_is_nobody_rather_than_everybody()
    {
        Match("").ShouldBeEmpty();
        Match("   ").ShouldBeEmpty();
        CaverNameLadder.Match<int>(null, Roster).ShouldBeEmpty();
    }
}
