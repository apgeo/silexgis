// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using static SilexGis.Domain.Entities.ActivityState;
using static SilexGis.Domain.Entities.ActivityStates;

namespace SilexGis.Domain.Tests;

public class ActivityStateTests
{
    [Fact]
    public void State_values_are_the_schema_contract()
    {
        ((short)Draft).ShouldBe((short)0);
        ((short)Proposed).ShouldBe((short)1);
        ((short)Planned).ShouldBe((short)2);
        ((short)Confirmed).ShouldBe((short)3);
        ((short)Done).ShouldBe((short)4);
        ((short)Published).ShouldBe((short)5);
        ((short)Cancelled).ShouldBe((short)6);
        ((short)Delayed).ShouldBe((short)7);
    }

    [Fact]
    public void All_lists_every_state() =>
        All.ShouldBe(Enum.GetValues<ActivityState>(), ignoreOrder: true);

    [Fact]
    public void A_trip_holds_every_state_in_the_vocabulary()
    {
        TripLogStates.ShouldBe(All, ignoreOrder: true);

        // Stated the other way round as well, so admitting a state to the enum without deciding
        // whether a trip may hold it fails here rather than showing up as a column value nothing
        // renders.
        All.Where(IsTripLogState).ShouldBe(All, ignoreOrder: true);
    }

    [Fact]
    public void A_trip_climbs_the_planning_ladder_one_rung_at_a_time()
    {
        MayTripLogTransition(Draft, Proposed).ShouldBeTrue();
        MayTripLogTransition(Proposed, Planned).ShouldBeTrue();
        MayTripLogTransition(Planned, Confirmed).ShouldBeTrue();
        MayTripLogTransition(Confirmed, Done).ShouldBeTrue();
        MayTripLogTransition(Done, Published).ShouldBeTrue();

        // Each rung is a decision somebody takes, so none of them is reachable by skipping the
        // one before it. Joining the ladder late is allowed; climbing two rungs at once is not,
        // and neither is announcing a trip nobody has run yet.
        MayTripLogTransition(Draft, Confirmed).ShouldBeFalse();
        MayTripLogTransition(Proposed, Confirmed).ShouldBeFalse();
        MayTripLogTransition(Planned, Done).ShouldBeFalse();
        MayTripLogTransition(Proposed, Done).ShouldBeFalse();
        MayTripLogTransition(Proposed, Published).ShouldBeFalse();
        MayTripLogTransition(Planned, Published).ShouldBeFalse();
        MayTripLogTransition(Confirmed, Published).ShouldBeFalse();

        // Joining late, and the two edges a retrospective report is entered by: a trip run before
        // any of this existed is recorded from the workshop with no rungs left to climb.
        MayTripLogTransition(Draft, Planned).ShouldBeTrue();
        MayTripLogTransition(Draft, Done).ShouldBeTrue();
        MayTripLogTransition(Draft, Published).ShouldBeTrue();
    }

    [Fact]
    public void A_trip_is_called_off_while_it_still_lies_ahead_and_not_afterwards()
    {
        // The ordinary cancellation is a confirmed trip abandoned the night before, so calling it
        // off is reachable from every state where it has not happened yet.
        foreach (var live in new[] { Draft, Proposed, Planned, Confirmed, Delayed })
        {
            MayTripLogTransition(live, Cancelled).ShouldBeTrue($"{live} lies before the trip.");
        }

        // Afterwards it is not: a trip that happened cannot be made not to have happened, and
        // removing the record is a different act taken through a different button.
        MayTripLogTransition(Done, Cancelled).ShouldBeFalse();
        MayTripLogTransition(Published, Cancelled).ShouldBeFalse();

        // Reinstating one returns it to the workshop rather than to the rung it fell from.
        MayTripLogTransition(Cancelled, Draft).ShouldBeTrue();
        MayTripLogTransition(Cancelled, Confirmed).ShouldBeFalse();
        MayTripLogTransition(Cancelled, Published).ShouldBeFalse();
    }

    [Fact]
    public void Only_a_trip_with_a_date_to_move_is_put_back()
    {
        MayTripLogTransition(Planned, Delayed).ShouldBeTrue();
        MayTripLogTransition(Confirmed, Delayed).ShouldBeTrue();

        // An idea nobody has dated yet is still an idea, not a postponement; and a trip that
        // happened has nothing left to put back.
        MayTripLogTransition(Draft, Delayed).ShouldBeFalse();
        MayTripLogTransition(Proposed, Delayed).ShouldBeFalse();
        MayTripLogTransition(Done, Delayed).ShouldBeFalse();
        MayTripLogTransition(Published, Delayed).ShouldBeFalse();

        // A new date returns it to being organised: telling people it is on again is a second
        // decision.
        MayTripLogTransition(Delayed, Planned).ShouldBeTrue();
        MayTripLogTransition(Delayed, Confirmed).ShouldBeFalse();
    }

    [Fact]
    public void No_legal_trip_move_touches_a_state_a_trip_cannot_hold()
    {
        // The blanket form of the test above: whatever the table grows, both ends of every move it
        // admits are states a trip is allowed to be in.
        foreach (var from in All)
        {
            foreach (var to in All.Where(to => MayTripLogTransition(from, to)))
            {
                IsTripLogState(from).ShouldBeTrue($"{from} is an origin of a legal move.");
                IsTripLogState(to).ShouldBeTrue($"{to} is a destination of a legal move.");
            }
        }
    }

    [Fact]
    public void Publishing_and_its_reverse_are_both_legal()
    {
        MayTripLogTransition(Draft, Published).ShouldBeTrue();
        MayTripLogTransition(Published, Draft).ShouldBeTrue();
        MayTripLogTransition(Done, Published).ShouldBeTrue();
    }

    [Fact]
    public void Draft_is_the_state_everything_returns_to()
    {
        // Every state a trip can be in other than the workshop has a way back to it, so "how do I
        // get at this again" has one answer whatever the trip is in the middle of.
        foreach (var state in All.Where(state => state != Draft))
        {
            MayTripLogTransition(state, Draft).ShouldBeTrue($"{state} returns to the workshop.");
        }


        // De-announcing a trip and declaring it never happened are two decisions. Neither is
        // reachable in one move from the other, so neither can be taken by accident while taking
        // the other.
        MayTripLogTransition(Published, Cancelled).ShouldBeFalse();
        MayTripLogTransition(Cancelled, Published).ShouldBeFalse();
        MayTripLogTransition(Done, Cancelled).ShouldBeFalse();
        MayTripLogTransition(Cancelled, Done).ShouldBeFalse();
    }

    [Fact]
    public void A_state_is_not_a_move_to_itself() =>
        All.ShouldAllBe(state => !MayTripLogTransition(state, state));

    [Fact]
    public void An_expedition_holds_every_state_in_the_vocabulary()
    {
        ExpeditionStates.ShouldBe(All, ignoreOrder: true);

        // Stated the other way round as well, so a state added to the enum is admitted to a camp
        // by somebody deciding it, not by the list happening to be the whole vocabulary.
        All.Where(IsExpeditionState).ShouldBe(All, ignoreOrder: true);
    }

    [Fact]
    public void Each_kind_declares_its_whole_table_for_itself()
    {
        // This replaces a test that proved the two tables separate by the difference between
        // them: a camp was organised before it happened and a trip was only written up after, so
        // the four planning states were exactly what told the two apart. A trip is prepared in
        // the application now, so that difference is gone and the two tables agree pair for pair.
        //
        // They agree because two decisions came out the same way, not because one rule serves
        // both kinds, and what is left to prove is that each table is still declared for itself.
        // So each one is stated whole and separately below, over every ordered pair the
        // vocabulary can make. A move added to one kind fails the half of this test that names
        // that kind and leaves the other half green, which is what being two tables means; and
        // the refusals stay distinct, so a caller reading one can still tell which kind turned it
        // down.
        MovesAllowedBy(MayTripLogTransition).ShouldBe(
        [
            (Draft, Proposed), (Draft, Planned),
            (Draft, Done), (Draft, Published), (Draft, Cancelled),
            (Proposed, Planned), (Proposed, Draft), (Proposed, Cancelled),
            (Planned, Confirmed), (Planned, Delayed), (Planned, Draft), (Planned, Cancelled),
            (Confirmed, Done), (Confirmed, Delayed), (Confirmed, Draft), (Confirmed, Cancelled),
            (Delayed, Planned), (Delayed, Draft), (Delayed, Cancelled),
            (Done, Published), (Done, Draft),
            (Published, Draft),
            (Cancelled, Draft),
        ], ignoreOrder: true);

        MovesAllowedBy(MayExpeditionTransition).ShouldBe(
        [
            (Draft, Proposed), (Draft, Planned),
            (Draft, Done), (Draft, Published), (Draft, Cancelled),
            (Proposed, Planned), (Proposed, Draft), (Proposed, Cancelled),
            (Planned, Confirmed), (Planned, Delayed), (Planned, Draft), (Planned, Cancelled),
            (Confirmed, Done), (Confirmed, Delayed), (Confirmed, Draft), (Confirmed, Cancelled),
            (Delayed, Planned), (Delayed, Draft), (Delayed, Cancelled),
            (Done, Published), (Done, Draft),
            (Published, Draft),
            (Cancelled, Draft),
        ], ignoreOrder: true);

        TripLogTransitionInvalidCode.ShouldNotBe(ExpeditionTransitionInvalidCode);
    }

    /// <summary>Every ordered pair of states one kind's table admits, asked pair by pair.</summary>
    private static IReadOnlyList<(ActivityState From, ActivityState To)> MovesAllowedBy(
        Func<ActivityState, ActivityState, bool> mayMove) =>
        [.. All.SelectMany(from => All.Select(to => (From: from, To: to)))
               .Where(move => mayMove(move.From, move.To))];

    [Fact]
    public void No_legal_expedition_move_touches_a_state_an_expedition_cannot_hold()
    {
        foreach (var from in All)
        {
            foreach (var to in All.Where(to => MayExpeditionTransition(from, to)))
            {
                IsExpeditionState(from).ShouldBeTrue($"{from} is an origin of a legal move.");
                IsExpeditionState(to).ShouldBeTrue($"{to} is a destination of a legal move.");
            }
        }
    }

    [Fact]
    public void An_expedition_climbs_the_planning_ladder_one_rung_at_a_time()
    {
        MayExpeditionTransition(Draft, Proposed).ShouldBeTrue();
        MayExpeditionTransition(Proposed, Planned).ShouldBeTrue();
        MayExpeditionTransition(Planned, Confirmed).ShouldBeTrue();
        MayExpeditionTransition(Confirmed, Done).ShouldBeTrue();
        MayExpeditionTransition(Done, Published).ShouldBeTrue();

        // Each rung is a decision somebody takes, so none of them is reachable by skipping the
        // one before it. Joining the ladder late is allowed; climbing two rungs at once is not.
        MayExpeditionTransition(Draft, Confirmed).ShouldBeFalse();
        MayExpeditionTransition(Proposed, Confirmed).ShouldBeFalse();
        MayExpeditionTransition(Planned, Done).ShouldBeFalse();
        MayExpeditionTransition(Proposed, Done).ShouldBeFalse();

        // Except into the workshop, which is where a camp that already happened is entered from
        // — a camp run before this system existed has no rungs left to climb.
        MayExpeditionTransition(Draft, Done).ShouldBeTrue();
        MayExpeditionTransition(Draft, Published).ShouldBeTrue();
    }

    [Fact]
    public void A_camp_is_called_off_while_it_has_not_happened_and_not_afterwards()
    {
        // The ordinary cancellation is a confirmed camp abandoned three weeks out, so calling it
        // off is reachable from every state where it still lies ahead.
        foreach (var live in new[] { Draft, Proposed, Planned, Confirmed, Delayed })
        {
            MayExpeditionTransition(live, Cancelled).ShouldBeTrue($"{live} lies before the camp.");
        }

        // Afterwards it is not: a camp that happened cannot be made not to have happened, and
        // removing the record is a different act taken through a different button.
        MayExpeditionTransition(Done, Cancelled).ShouldBeFalse();
        MayExpeditionTransition(Published, Cancelled).ShouldBeFalse();

        // Reinstating one returns it to the workshop rather than to the rung it fell from.
        MayExpeditionTransition(Cancelled, Draft).ShouldBeTrue();
        MayExpeditionTransition(Cancelled, Confirmed).ShouldBeFalse();
        MayExpeditionTransition(Cancelled, Published).ShouldBeFalse();
    }

    [Fact]
    public void Only_a_camp_with_dates_to_move_is_put_back()
    {
        MayExpeditionTransition(Planned, Delayed).ShouldBeTrue();
        MayExpeditionTransition(Confirmed, Delayed).ShouldBeTrue();

        // An idea nobody has dated yet is still an idea, not a postponement; and a camp that
        // happened has nothing left to put back.
        MayExpeditionTransition(Draft, Delayed).ShouldBeFalse();
        MayExpeditionTransition(Proposed, Delayed).ShouldBeFalse();
        MayExpeditionTransition(Done, Delayed).ShouldBeFalse();
        MayExpeditionTransition(Published, Delayed).ShouldBeFalse();

        // New dates return it to being organised: announcing it is on again is a second decision.
        MayExpeditionTransition(Delayed, Planned).ShouldBeTrue();
        MayExpeditionTransition(Delayed, Confirmed).ShouldBeFalse();
    }

    [Fact]
    public void Everything_live_returns_to_the_workshop()
    {
        foreach (var state in All.Where(state => state != Draft))
        {
            MayExpeditionTransition(state, Draft).ShouldBeTrue($"{state} returns to the workshop.");
        }
    }

    [Fact]
    public void An_expedition_state_is_not_a_move_to_itself() =>
        All.ShouldAllBe(state => !MayExpeditionTransition(state, state));

    [Fact]
    public void The_two_refusals_name_the_thing_that_refused_them()
    {
        // A caller reading a refusal has to be able to tell which activity refused it, so the
        // codes are not shared between the two tables.
        TripLogTransitionInvalidCode.ShouldBe("trip_log.state_transition_invalid");
        ExpeditionTransitionInvalidCode.ShouldBe("expedition.state_transition_invalid");
    }

    [Fact]
    public void A_draft_and_a_cancelled_trip_tell_nobody()
    {
        // The partition rather than the two cases, so a state added to the enum and left out of
        // the rule shows up here as a state that suppresses — which is what the fail-closed
        // default does — instead of silently sending mail nobody decided to send.
        All.Where(SuppressesParticipantNotification).ShouldBe([Draft, Cancelled], ignoreOrder: true);
    }
}
