// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

public class TripCalloutRulesTests
{
    [Fact]
    public void A_trip_called_off_or_put_back_is_no_longer_watched()
    {
        // Nobody went on a trip that was called off, and a trip that was put back has moved — the
        // hour an alarm was armed against belongs to a date that is no longer true.
        TripCalloutRules.WatchesForOverdue(ActivityState.Cancelled).ShouldBeFalse();
        TripCalloutRules.WatchesForOverdue(ActivityState.Delayed).ShouldBeFalse();

        // The positive beside them, so a rule that answered "not watched" to everything — which
        // would silently swallow every alarm this system exists to raise — could not pass.
        TripCalloutRules.WatchesForOverdue(ActivityState.Confirmed).ShouldBeTrue();
        TripCalloutRules.WatchesForOverdue(ActivityState.Planned).ShouldBeTrue();
    }

    [Fact]
    public void Every_other_state_is_watched_including_ones_nobody_has_thought_of()
    {
        // The default is deliberately the opposite way round from the one that decides whether a
        // change is worth mailing about. There, an unnamed state stays silent, and the cost of
        // being wrong is a message nobody got. Here the cost of being wrong is a party nobody went
        // looking for, so an unnamed state is watched and somebody stands the false alarm down.
        var unwatched = Enum.GetValues<ActivityState>()
            .Where(state => !TripCalloutRules.WatchesForOverdue(state))
            .ToList();

        unwatched.ShouldBe([ActivityState.Delayed, ActivityState.Cancelled], ignoreOrder: true);

        // And a value the vocabulary does not carry at all still reads as watched.
        TripCalloutRules.WatchesForOverdue((ActivityState)9999).ShouldBeTrue();
    }
}
