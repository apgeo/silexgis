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
    public void Nothing_written_is_nobody_rather_than_everybody()
    {
        Match("").ShouldBeEmpty();
        Match("   ").ShouldBeEmpty();
        CaverNameLadder.Match<int>(null, Roster).ShouldBeEmpty();
    }
}
