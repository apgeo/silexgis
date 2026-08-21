// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using static SilexGis.Domain.Entities.ActivityState;

namespace SilexGis.Domain.Tests;

public class TripPlanNoticesTests
{
    [Fact]
    public void Only_a_trip_people_are_expecting_announces_its_changes() =>
        // The partition rather than the cases one at a time, so a state added to the vocabulary
        // and left out of the rule shows up here as a state that stays silent — which is what the
        // fail-closed default arm does — instead of quietly mailing everybody.
        ActivityStates.All.Where(TripPlanNotices.AnnouncesChanges)
            .ShouldBe([Proposed, Planned, Confirmed, Delayed], ignoreOrder: true);

    [Fact]
    public void A_draft_announces_nothing_because_it_has_been_shown_to_nobody() =>
        // Telling somebody a draft changed would be telling them it exists, which is the one thing
        // a draft does not do. Its counterpart, a trip being organised, is asserted beside it so
        // the rule is proved rather than the absence of a message.
        (TripPlanNotices.AnnouncesChanges(Draft), TripPlanNotices.AnnouncesChanges(Planned))
            .ShouldBe((false, true));

    [Fact]
    public void The_change_notice_is_a_different_question_from_the_state_entry_one()
    {
        // Two rules, two answers, and they disagree on purpose: entering a state never tells the
        // roster a proposed trip exists, while editing one that people are expecting does. Pinned
        // so that neither rule is later "simplified" into the other.
        ActivityStates.SuppressesParticipantNotification(Proposed).ShouldBeFalse();
        TripPlanNotices.AnnouncesChanges(Proposed).ShouldBeTrue();

        // And the case that matters most: a cancelled trip announces nothing by being cancelled.
        ActivityStates.SuppressesParticipantNotification(Cancelled).ShouldBeTrue();
        TripPlanNotices.AnnouncesChanges(Cancelled).ShouldBeFalse();
    }
}
