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
    public void A_trip_holds_four_of_the_eight_states()
    {
        TripLogStates.ShouldBe([Draft, Done, Published, Cancelled], ignoreOrder: true);

        // Stated the other way round as well, so admitting a state to the enum without deciding
        // whether a trip may hold it fails here rather than showing up as a column value nothing
        // renders.
        All.Where(IsTripLogState).ShouldBe([Draft, Done, Published, Cancelled], ignoreOrder: true);
    }

    [Fact]
    public void A_trip_cannot_be_moved_into_a_state_kept_for_planning()
    {
        foreach (var planning in new[] { Proposed, Planned, Confirmed, Delayed })
        {
            IsTripLogState(planning).ShouldBeFalse($"{planning} is not a state a trip holds.");

            // Refused as a destination and as an origin. A trip that somehow held one could
            // otherwise be moved out of it into a legal state and the illegal value would be gone
            // before anybody saw it.
            MayTripLogTransition(Draft, planning).ShouldBeFalse();
            MayTripLogTransition(planning, Draft).ShouldBeFalse();
            MayTripLogTransition(planning, Published).ShouldBeFalse();
        }
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
        MayTripLogTransition(Done, Draft).ShouldBeTrue();
        MayTripLogTransition(Cancelled, Draft).ShouldBeTrue();

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
    public void The_two_tables_are_independent_of_each_other()
    {
        // A camp is organised before it happens and a trip is written up after, so the states
        // kept for planning are exactly what tells the two apart. If admitting them to one ever
        // admits them to the other, this is where it shows.
        foreach (var planning in new[] { Proposed, Planned, Confirmed, Delayed })
        {
            IsExpeditionState(planning).ShouldBeTrue($"{planning} is a state a camp holds.");
            IsTripLogState(planning).ShouldBeFalse($"{planning} is not a state a trip holds.");
        }

        MayExpeditionTransition(Draft, Proposed).ShouldBeTrue();
        MayTripLogTransition(Draft, Proposed).ShouldBeFalse();
    }

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
