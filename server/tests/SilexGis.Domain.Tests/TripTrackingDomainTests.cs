// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

public class TripTrackingDomainTests
{
    /// <summary>
    /// One shaft in a model whose two sides spell a station differently: the rows carry the root
    /// survey at the front of every name, the viewer that draws them does not. Every rule below can
    /// therefore be asked in both vocabularies, which is the point — the names a person types into
    /// a datum box or a depth filter are read off whichever of the two was in front of them.
    /// </summary>
    private static readonly TrackingDepthResolver.Station[] Shaft =
    [
        Station("cave.ent.0", "cave.ent", 350, isEntrance: true),
        Station("cave.upper.1", "cave.upper", 340),
        Station("cave.upper.2", "cave.upper", 300),
        Station("cave.parallel.2", "cave.parallel", 300),
        Station("cave.deep.3", "cave.deep", 230),
    ];

    private static TrackingDepthResolver.Station Station(
        string name, string surveyName, double z, bool isEntrance = false) =>
        TrackingDepthResolver.Station.Of(SurveyModelFormat.Lox, "cave", name, surveyName, z, isEntrance);

    [Fact]
    public void The_depth_datum_is_the_named_station_when_one_is_configured_and_the_highest_entrance_otherwise()
    {
        TrackingDepthResolver.ReferenceZ(Shaft, "cave.upper.1").ShouldBe(340);
        TrackingDepthResolver.ReferenceZ(Shaft, null).ShouldBe(350);
        // A name that is not a station answers null rather than quietly measuring from elsewhere.
        TrackingDepthResolver.ReferenceZ(Shaft, "cave.nowhere").ShouldBeNull();
    }

    [Fact]
    public void A_survey_with_no_entrance_flags_still_gets_a_datum_and_an_empty_one_gets_none()
    {
        TrackingDepthResolver.Station[] unflagged =
        [
            Flat("a.1", "a", 120),
            Flat("a.2", "a", 180),
        ];
        TrackingDepthResolver.ReferenceZ(unflagged, null).ShouldBe(180);
        TrackingDepthResolver.ReferenceZ([], null).ShouldBeNull();
    }

    [Fact]
    public void The_datum_is_found_under_either_of_a_stations_names()
    {
        // The administrator who sets a datum reads the station off the model, so what arrives is
        // the viewer's name for it; the same station typed out of a survey listing arrives as the
        // rows spell it. Both are that station and both must measure from the same altitude.
        TrackingDepthResolver.ReferenceZ(Shaft, "upper.1").ShouldBe(340);
        TrackingDepthResolver.ReferenceZ(Shaft, "cave.upper.1").ShouldBe(340);

        // And the positive twin of the refusal: accepting both readings must not turn into
        // accepting anything. A name that is neither reading of any station still answers null,
        // and so does the root survey's own name, which names no station at all.
        TrackingDepthResolver.ReferenceZ(Shaft, "upper.9").ShouldBeNull();
        TrackingDepthResolver.ReferenceZ(Shaft, "cave").ShouldBeNull();
    }

    [Fact]
    public void A_model_whose_two_sides_agree_matches_its_one_spelling_and_nothing_more()
    {
        // The case a rule that always stripped a leading component would break: a model with no
        // survey tree, whose names are whole as they stand. "1" is a suffix of "a.1" and must not
        // be taken for it, in either direction.
        TrackingDepthResolver.Station[] flat = [Flat("a.1", "a", 120)];
        TrackingDepthResolver.ReferenceZ(flat, "a.1").ShouldBe(120);
        TrackingDepthResolver.ReferenceZ(flat, "1").ShouldBeNull();
        TrackingDepthResolver.Resolve(flat, 120, 0, ["a"]).Single().ViewerName.ShouldBe("a.1");
        TrackingDepthResolver.Resolve(flat, 120, 0, ["1"]).ShouldBeEmpty();
    }

    private static TrackingDepthResolver.Station Flat(string name, string surveyName, double z) =>
        TrackingDepthResolver.Station.Of(SurveyModelFormat.Survex3d, null, name, surveyName, z, false);

    [Fact]
    public void The_closest_station_wins_and_a_signed_depth_means_the_same_place()
    {
        var down = TrackingDepthResolver.Resolve(Shaft, 350, 115, [], take: 2);
        down[0].Name.ShouldBe("cave.deep.3");
        down[0].DepthM.ShouldBe(120);

        var signed = TrackingDepthResolver.Resolve(Shaft, 350, -115, [], take: 2);
        signed[0].Name.ShouldBe(down[0].Name);
        signed[0].DeltaM.ShouldBe(down[0].DeltaM);
    }

    [Fact]
    public void Stations_at_the_same_depth_come_back_in_name_order_so_the_answer_is_stable()
    {
        var tied = TrackingDepthResolver.Resolve(Shaft, 350, 50, [], take: 3);
        tied[0].Name.ShouldBe("cave.parallel.2");
        tied[1].Name.ShouldBe("cave.upper.2");
        tied[0].DeltaM.ShouldBe(tied[1].DeltaM);
    }

    [Fact]
    public void The_filter_keeps_depth_matching_inside_the_parts_the_party_is_actually_in()
    {
        // Both branches have a station at −50; the filter names the branch, so the parallel
        // shaft at the same depth cannot claim the report.
        var filtered = TrackingDepthResolver.Resolve(Shaft, 350, 50, ["cave.upper"], take: 3);
        filtered.ShouldAllBe(c => c.Name.StartsWith("cave.upper"));
        filtered[0].Name.ShouldBe("cave.upper.2");

        var none = TrackingDepthResolver.Resolve(Shaft, 350, 50, ["cave.absent"], take: 3);
        none.ShouldBeEmpty();
    }

    [Fact]
    public void The_filter_takes_the_start_of_either_spelling_of_a_name()
    {
        // The filter is typed by somebody reading a list of stations, and the list beside it is
        // written in the viewer's spelling. A filter that only understood the rows' spelling would
        // keep nothing at all from a name copied out of that list, and every depth report under it
        // would be refused with nothing on screen saying why.
        var byViewerName = TrackingDepthResolver.Resolve(Shaft, 350, 50, ["upper"], take: 3);
        var byRowName = TrackingDepthResolver.Resolve(Shaft, 350, 50, ["cave.upper"], take: 3);
        byViewerName.Select(c => c.Name).ShouldBe(byRowName.Select(c => c.Name));
        byViewerName.ShouldNotBeEmpty();

        // Widening is not the same as matching everything: a prefix of neither spelling keeps none.
        TrackingDepthResolver.Resolve(Shaft, 350, 50, ["parallel.9"], take: 3).ShouldBeEmpty();
    }

    [Fact]
    public void A_candidate_comes_back_under_both_of_its_names()
    {
        // What is stored and drawn is the viewer's name; what the filter and the survey listing
        // speak is the rows'. Carrying both is what stops each caller converting for itself, which
        // is how the two came to disagree in the first place.
        var deepest = TrackingDepthResolver.Resolve(Shaft, 350, 120, [], take: 1).Single();
        deepest.Name.ShouldBe("cave.deep.3");
        deepest.ViewerName.ShouldBe("deep.3");
        deepest.SurveyName.ShouldBe("cave.deep");
    }

    [Fact]
    public void Tracking_arms_closes_and_rearms_and_never_returns_to_off()
    {
        TripTrackingRules.MayTransition(TripTrackingState.Off, TripTrackingState.Armed).ShouldBeTrue();
        TripTrackingRules.MayTransition(TripTrackingState.Armed, TripTrackingState.Closed).ShouldBeTrue();
        TripTrackingRules.MayTransition(TripTrackingState.Closed, TripTrackingState.Armed).ShouldBeTrue();
        TripTrackingRules.MayTransition(TripTrackingState.Armed, TripTrackingState.Armed).ShouldBeTrue();

        TripTrackingRules.MayTransition(TripTrackingState.Off, TripTrackingState.Closed).ShouldBeFalse();
        TripTrackingRules.MayTransition(TripTrackingState.Armed, TripTrackingState.Off).ShouldBeFalse();
        TripTrackingRules.MayTransition(TripTrackingState.Closed, TripTrackingState.Off).ShouldBeFalse();
    }

    // ---- where one member of the party stands --------------------------------------------
    //
    // StandingOf is pure, takes a plain sequence and is the one home both tracking reads ask.
    // Every ordering it can be handed is enumerable here for the cost of a line, which is worth
    // doing precisely because the surfaces that consume it are HTTP tests against a database:
    // those prove the reads ask this question, and these prove the answer.

    private static TripStanding Standing(params TripPositionEventKind[] reports) =>
        TripTrackingRules.StandingOf([.. reports.Select(kind => new TripPositionEvent { Kind = kind })]);

    [Fact]
    public void Nothing_that_speaks_to_presence_leaves_somebody_unheard_from_rather_than_out()
    {
        // The distinction the whole three-state answer exists for: somebody still in the car park
        // and somebody safely back out are the two readings a watcher most needs told apart, so
        // neither a missing log nor an empty one nor a log of pure notes may collapse into "out".
        TripTrackingRules.StandingOf(null).ShouldBe(TripStanding.Unheard);
        TripTrackingRules.StandingOf([]).ShouldBe(TripStanding.Unheard);
        Standing(TripPositionEventKind.Note).ShouldBe(TripStanding.Unheard);
        Standing(TripPositionEventKind.Note, TripPositionEventKind.Note).ShouldBe(TripStanding.Unheard);

        // The twin, so that the three above cannot pass on a rule that had stopped reading at all.
        Standing(TripPositionEventKind.Note, TripPositionEventKind.Entered).ShouldBe(TripStanding.Underground);
    }

    [Fact]
    public void The_last_report_that_states_a_standing_is_the_answer_however_often_it_changes()
    {
        Standing(TripPositionEventKind.Entered).ShouldBe(TripStanding.Underground);
        Standing(TripPositionEventKind.Entered, TripPositionEventKind.Exited).ShouldBe(TripStanding.Out);

        // A party that turns out to still be underground goes back in, which is the same event
        // the tracking lifecycle re-arms for; nothing about it is exceptional to this fold.
        Standing(
            TripPositionEventKind.Entered, TripPositionEventKind.Exited,
            TripPositionEventKind.Entered).ShouldBe(TripStanding.Underground);
        Standing(
            TripPositionEventKind.Entered, TripPositionEventKind.Exited,
            TripPositionEventKind.Entered, TripPositionEventKind.Exited).ShouldBe(TripStanding.Out);

        // An exit with nothing before it still lands: reports are not required to start with one.
        Standing(TripPositionEventKind.Exited).ShouldBe(TripStanding.Out);
    }

    [Fact]
    public void A_place_answers_only_where_nothing_has_stated_one_and_never_overturns_an_exit()
    {
        // Word arrives by relayed phone call and what gets relayed first is routinely where a
        // team is, not that they went in — so a place has to be able to answer on its own, or a
        // party whose station is on the screen would be counted as never heard from.
        Standing(TripPositionEventKind.AtStation).ShouldBe(TripStanding.Underground);
        Standing(TripPositionEventKind.AtDepth).ShouldBe(TripStanding.Underground);
        Standing(TripPositionEventKind.Note, TripPositionEventKind.AtStation).ShouldBe(TripStanding.Underground);

        // And the other half of the same decision. A report's time defaults to the clock at the
        // moment it is written and an imported scan carries a device's clock, so a place sorting
        // after an exit is as likely to be late log-keeping as a real return underground. The
        // stated standing therefore wins, and going back in is recorded by saying so.
        Standing(TripPositionEventKind.Exited, TripPositionEventKind.AtStation).ShouldBe(TripStanding.Out);
        Standing(TripPositionEventKind.Exited, TripPositionEventKind.AtDepth).ShouldBe(TripStanding.Out);
        Standing(
            TripPositionEventKind.Entered, TripPositionEventKind.Exited,
            TripPositionEventKind.AtStation, TripPositionEventKind.AtDepth).ShouldBe(TripStanding.Out);
        Standing(
            TripPositionEventKind.AtStation, TripPositionEventKind.Exited,
            TripPositionEventKind.AtStation).ShouldBe(TripStanding.Out);

        // The twins: the recorded entry does put them back, and a place after an entry is a
        // position report doing its ordinary job rather than a no-op.
        Standing(
            TripPositionEventKind.Exited, TripPositionEventKind.AtStation,
            TripPositionEventKind.Entered).ShouldBe(TripStanding.Underground);
        Standing(
            TripPositionEventKind.Entered, TripPositionEventKind.AtStation).ShouldBe(TripStanding.Underground);
        Standing(
            TripPositionEventKind.AtStation, TripPositionEventKind.Entered).ShouldBe(TripStanding.Underground);
    }

    [Fact]
    public void A_note_is_transparent_wherever_it_lands()
    {
        // The defect this rule was written for: a note about somebody already out used to put
        // them back underground, and a note before the party set off used to put them there too.
        // A note is defined as word with no position claim, so it has to leave every standing
        // exactly as it found it — checked against each of the three rather than against one.
        Standing(TripPositionEventKind.Exited, TripPositionEventKind.Note).ShouldBe(TripStanding.Out);
        Standing(TripPositionEventKind.Entered, TripPositionEventKind.Note).ShouldBe(TripStanding.Underground);
        Standing(TripPositionEventKind.AtStation, TripPositionEventKind.Note).ShouldBe(TripStanding.Underground);

        // Interleaved rather than trailing, since "the last report is a note" is only the easiest
        // way to get this wrong and not the only one.
        Standing(
            TripPositionEventKind.Note, TripPositionEventKind.Entered, TripPositionEventKind.Note,
            TripPositionEventKind.Exited, TripPositionEventKind.Note).ShouldBe(TripStanding.Out);
    }

    [Fact]
    public void A_place_is_drawable_only_on_the_survey_it_was_measured_in_and_never_against_nothing()
    {
        var measuredIn = Guid.NewGuid();
        var another = Guid.NewGuid();

        // The positive half first, because a rule that refuses everything would pass the rest of
        // this test and would take the whole party off the model.
        TripTrackingRules.DrawableOn(measuredIn, measuredIn).ShouldBeTrue();

        // Two surveys of the same cave spell a station the same way and mean different places by
        // it, so identity of the id is the whole test and nothing weaker will do.
        TripTrackingRules.DrawableOn(measuredIn, another).ShouldBeFalse();

        // Both nulls fail closed: a row naming no survey claims no place worth drawing, and a
        // panel that does not know its own survey has nothing to compare against. Null equals
        // null is deliberately not true here — that is the reading under which a report whose
        // survey reference had been blanked got drawn on whatever happened to be on screen.
        TripTrackingRules.DrawableOn(null, measuredIn).ShouldBeFalse();
        TripTrackingRules.DrawableOn(measuredIn, null).ShouldBeFalse();
        TripTrackingRules.DrawableOn(null, null).ShouldBeFalse();
    }

    [Fact]
    public void An_armed_watch_may_change_survey_within_its_cave_and_may_not_leave_it()
    {
        var cave = Guid.NewGuid();
        var elsewhere = Guid.NewGuid();

        // A corrected or re-imported survey arriving mid-trip is a thing a co-ordinator has to be
        // able to follow; refusing it would be answered by closing the watch.
        TripTrackingRules.MayPointAtCave(TripTrackingState.Armed, cave, cave).ShouldBeTrue();

        // The party is in one cave, and that cave is also the anchor every position on the log is
        // protected by. Moving a live watch out of it is the one swap nothing downstream can undo.
        TripTrackingRules.MayPointAtCave(TripTrackingState.Armed, cave, elsewhere).ShouldBeFalse();

        // A watch that is following nobody may be pointed anywhere, and a watch with no cave yet
        // is being configured rather than moved.
        TripTrackingRules.MayPointAtCave(TripTrackingState.Off, cave, elsewhere).ShouldBeTrue();
        TripTrackingRules.MayPointAtCave(TripTrackingState.Closed, cave, elsewhere).ShouldBeTrue();
        TripTrackingRules.MayPointAtCave(TripTrackingState.Armed, null, elsewhere).ShouldBeTrue();
    }
}
