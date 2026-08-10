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
    public void A_draft_and_a_cancelled_trip_tell_nobody()
    {
        // The partition rather than the two cases, so a state added to the enum and left out of
        // the rule shows up here as a state that suppresses — which is what the fail-closed
        // default does — instead of silently sending mail nobody decided to send.
        All.Where(SuppressesParticipantNotification).ShouldBe([Draft, Cancelled], ignoreOrder: true);
    }
}
