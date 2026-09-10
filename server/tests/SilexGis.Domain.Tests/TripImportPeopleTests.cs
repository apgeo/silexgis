// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Import;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What the names on a club's sheet become, and — the part that matters as much — what the
/// reviewer is told they will become before they press the button.
///
/// <para>
/// The sheet under test is the shape a real club writes: a given name and an initial, a lone
/// given name, and one full name among them. Read under the shipped settings it yields one
/// person out of four, which is the whole reason the choice exists; read by somebody who has
/// said their club writes people that way it yields four. Both readings are asserted here, and
/// in both the headline figures are asserted against the outcome rather than against each
/// other, because a screen that says nothing needs attention while people are being dropped is
/// the failure this whole area exists to prevent.
/// </para>
/// </summary>
public class TripImportPeopleTests
{
    // Invented, and written in the form the refusal was measured against: two given-name-plus-
    // initial names, one lone given name, one full name.
    private static readonly string[] Sheet = ["Ion B.", "Raluca R.", "Gheorghe", "Maria Popescu"];

    private static readonly IReadOnlyList<TripImportCandidate> NobodyAnswers = [];

    /// <summary>
    /// Every distinct name on the sheet, resolved against a roster in which nothing answers to
    /// any of them — the state a club is in when it imports its history for the first time.
    /// </summary>
    private static IReadOnlyList<TripImportPersonMatch> Resolve(TripImportOptions options) =>
        [.. Sheet.Select(name => TripImportPeople.Match(name, NobodyAnswers, options))
            .Where(m => m is not null)
            .Select(m => m!)];

    [Fact]
    public void The_shipped_settings_make_one_person_out_of_four_names_and_say_so()
    {
        var options = new TripImportOptions { CreateMissingCavers = true };
        options.CreateAbbreviatedCavers.ShouldBeFalse("the choice ships off");

        var people = Resolve(options);
        var counts = TripImportPeople.Count(people);

        people.Count(p => p.WillCreate).ShouldBe(1);
        people.Single(p => p.WillCreate).Source.ShouldBe("Maria Popescu");

        // The figures the review renders, against the outcome rather than against each other.
        counts.WillCreate.ShouldBe(1);
        counts.Uncreatable.ShouldBe(3);
        counts.Ambiguous.ShouldBe(0);

        // Nobody is silently dropped: every name on the sheet is either one that will become a
        // person or one the screen is telling the reviewer it cannot.
        (counts.WillCreate + counts.Ambiguous + counts.Uncreatable).ShouldBe(Sheet.Length);
    }

    [Fact]
    public void Allowing_abbreviated_names_makes_four_people_and_the_figures_follow()
    {
        var options = new TripImportOptions
        {
            CreateMissingCavers = true,
            CreateAbbreviatedCavers = true,
        };

        var people = Resolve(options);
        var counts = TripImportPeople.Count(people);

        people.ShouldAllBe(p => p.WillCreate);
        counts.WillCreate.ShouldBe(4);

        // The half that is easy to leave behind: with the names now creatable, they must stop
        // counting as people needing a decision, or the standing warning above the confirm
        // button goes on naming people the import is about to create perfectly well.
        counts.Uncreatable.ShouldBe(0);
        counts.Ambiguous.ShouldBe(0);
    }

    [Fact]
    public void The_choice_widens_what_may_be_created_without_creating_anything_on_its_own()
    {
        // The roster switch is what creates; this one only says which names it may create from.
        var options = new TripImportOptions { CreateAbbreviatedCavers = true };

        var people = Resolve(options);
        var counts = TripImportPeople.Count(people);

        people.ShouldAllBe(p => !p.WillCreate);
        counts.WillCreate.ShouldBe(0);

        // And with nothing being created, none of these names is one nobody could be made from:
        // reporting them as impossible would be telling the reviewer that flipping the roster
        // switch would not help, which is untrue.
        people.ShouldAllBe(p => p.MayCreate);
        counts.Uncreatable.ShouldBe(0);

        // Which is why this combination is not one the review offers. Read as figures on a
        // screen it says nobody needs a decision and nobody will be created, over a sheet whose
        // every name is dropped — so the widening choice is unusable until the roster switch is
        // on, and turning that switch off takes this one with it.
        counts.WillCreate.ShouldBe(0);
    }

    [Fact]
    public void A_name_two_people_already_answer_to_stays_unresolved_however_the_choice_stands()
    {
        // Short is not the same as ambiguous. Two real people hold this name, and which of them
        // was underground is a question only a person can answer — creating a third would be a
        // claim about who was there, made by nobody.
        IReadOnlyList<TripImportCandidate> both =
        [
            new(Guid.NewGuid(), "Ion Bălan"),
            new(Guid.NewGuid(), "Ion Bogdan"),
        ];

        var options = new TripImportOptions
        {
            CreateMissingCavers = true,
            CreateAbbreviatedCavers = true,
        };

        var match = TripImportPeople.Match("Ion B.", both, options).ShouldNotBeNull();

        match.State.ShouldBe(TripImportMatchState.Ambiguous);
        match.WillCreate.ShouldBeFalse();
        match.CaverId.ShouldBeNull();
        match.Candidates.Count.ShouldBe(2);

        var counts = TripImportPeople.Count([match]);
        counts.Ambiguous.ShouldBe(1);
        counts.WillCreate.ShouldBe(0);

        // The positive half of the same reading: one person answering to the name is matched to
        // that person, not duplicated, with the choice on.
        var single = TripImportPeople.Match("Ion B.", [both[0]], options).ShouldNotBeNull();
        single.State.ShouldBe(TripImportMatchState.Matched);
        single.CaverId.ShouldBe(both[0].Id);
        single.WillCreate.ShouldBeFalse();
    }

    [Fact]
    public void A_reviewer_who_has_said_which_person_is_meant_is_still_obeyed()
    {
        var wanted = Guid.NewGuid();
        IReadOnlyList<TripImportCandidate> both =
        [
            new(wanted, "Ion Bălan"),
            new(Guid.NewGuid(), "Ion Bogdan"),
        ];

        var options = new TripImportOptions
        {
            CreateMissingCavers = true,
            CreateAbbreviatedCavers = true,
            CaverChoices = new Dictionary<string, Guid> { ["ion b."] = wanted },
        };

        var match = TripImportPeople.Match("Ion B.", both, options).ShouldNotBeNull();

        match.State.ShouldBe(TripImportMatchState.Matched);
        match.CaverId.ShouldBe(wanted);
        match.WillCreate.ShouldBeFalse();
    }

    [Fact]
    public void An_empty_cell_is_not_a_person_under_either_setting()
    {
        foreach (var allowed in new[] { false, true })
        {
            var options = new TripImportOptions
            {
                CreateMissingCavers = true,
                CreateAbbreviatedCavers = allowed,
            };

            TripImportPeople.Match("   ", NobodyAnswers, options).ShouldBeNull();
            TripImportPeople.Match(null, NobodyAnswers, options).ShouldBeNull();
        }
    }
}
