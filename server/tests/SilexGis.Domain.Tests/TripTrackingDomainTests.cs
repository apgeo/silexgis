// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

public class TripTrackingDomainTests
{
    private static readonly TrackingDepthResolver.Station[] Shaft =
    [
        new("cave.ent.0", "cave.ent", 350, IsEntrance: true),
        new("cave.upper.1", "cave.upper", 340, IsEntrance: false),
        new("cave.upper.2", "cave.upper", 300, IsEntrance: false),
        new("cave.parallel.2", "cave.parallel", 300, IsEntrance: false),
        new("cave.deep.3", "cave.deep", 230, IsEntrance: false),
    ];

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
            new("a.1", "a", 120, false),
            new("a.2", "a", 180, false),
        ];
        TrackingDepthResolver.ReferenceZ(unflagged, null).ShouldBe(180);
        TrackingDepthResolver.ReferenceZ([], null).ShouldBeNull();
    }

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
}
