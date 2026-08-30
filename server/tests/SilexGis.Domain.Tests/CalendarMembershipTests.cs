// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Calendar;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using static SilexGis.Domain.Entities.ActivityState;

namespace SilexGis.Domain.Tests;

public class CalendarMembershipTests
{
    [Fact]
    public void Every_state_the_vocabulary_has_is_placed_somewhere() =>
        // The whole partition rather than the cases one at a time, so a state added to the
        // vocabulary and left out of the rule shows up here as one placed nowhere — which is
        // what the fail-closed default arm does — instead of appearing on somebody's month
        // before anybody decided what it means there.
        ActivityStates.All
            .ToDictionary(state => state, CalendarMembership.PlacementOf)
            .ShouldBe(new Dictionary<ActivityState, CalendarPlacement>
            {
                [Draft] = CalendarPlacement.Off,
                [Proposed] = CalendarPlacement.Ahead,
                [Planned] = CalendarPlacement.Ahead,
                [Confirmed] = CalendarPlacement.Ahead,
                [Done] = CalendarPlacement.Behind,
                [Published] = CalendarPlacement.Behind,
                [Cancelled] = CalendarPlacement.CalledOff,
                [Delayed] = CalendarPlacement.PutBack,
            });

    [Fact]
    public void A_state_nobody_has_named_is_off_the_calendar_rather_than_on_it()
    {
        // The default arm proved directly, with a value the vocabulary does not have. A state
        // added to the enum and forgotten here is off the calendar until somebody decides, which
        // is the recoverable failure: a row missing from a grid is asked about, a row drawn on a
        // day nobody agreed to is acted on.
        CalendarMembership.PlacementOf((ActivityState)99).ShouldBe(CalendarPlacement.Off);
        CalendarMembership.ShowsOnCalendar((ActivityState)99).ShouldBeFalse();

        // And a real one beside it, so a rule answering "off" to everything could not pass.
        CalendarMembership.ShowsOnCalendar(Planned).ShouldBeTrue();
    }

    [Fact]
    public void The_forward_half_is_the_same_answer_as_the_run_up_reminder()
    {
        // Not a coincidence and not a copy: the states meaning the date is still one somebody is
        // going on are asked of the reminder rule rather than re-listed, so the two cannot drift.
        // Pinned as an equality of sets so that widening one rule and not the other fails here.
        ActivityStates.All.Where(s => CalendarMembership.PlacementOf(s) == CalendarPlacement.Ahead)
            .ShouldBe(ActivityStates.All.Where(TripPlanNotices.RemindsOfDate), ignoreOrder: true);
    }

    [Fact]
    public void A_row_put_back_is_listed_but_not_drawn_on_the_date_it_no_longer_keeps()
    {
        // The one state that reaches the calendar without reaching a day cell, pinned against the
        // states that do. Drawing a postponed row on the date it still carries repeats the exact
        // mistake the run-up reminder rule exists to avoid: quoting a date nobody is going on.
        CalendarMembership.PlacementOf(Delayed).ShouldBe(CalendarPlacement.PutBack);
        CalendarMembership.ShowsOnCalendar(Delayed).ShouldBeTrue();

        CalendarMembership.PlacementOf(Planned).ShouldBe(CalendarPlacement.Ahead);
    }

    [Fact]
    public void A_row_called_off_is_shown_and_marked_rather_than_hidden()
    {
        // Hiding it would make the calendar quietly disagree with the row's own page, and the
        // person who was going on it is exactly the reader who most needs to notice. It is
        // marked so it cannot read as an ordinary outing, and a reader who wants only what is
        // going ahead narrows it away themselves.
        CalendarMembership.PlacementOf(Cancelled).ShouldBe(CalendarPlacement.CalledOff);
        CalendarMembership.ShowsOnCalendar(Cancelled).ShouldBeTrue();
    }

    [Fact]
    public void A_draft_is_kept_off_the_calendar_and_that_is_all_it_is()
    {
        // The rule is about what a calendar shows and nothing else. Nothing in this type takes a
        // reader, an account or a visibility, so it cannot be reached by anything deciding who
        // may read a row — a draft with public visibility is public, and stays exactly as
        // readable as it was on every surface that showed it.
        CalendarMembership.ShowsOnCalendar(Draft).ShouldBeFalse();

        typeof(CalendarMembership).GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(m => m.Name is nameof(CalendarMembership.PlacementOf)
                or nameof(CalendarMembership.ShowsOnCalendar))
            .SelectMany(m => m.GetParameters())
            .ShouldAllBe(p => p.ParameterType == typeof(ActivityState));
    }

    [Fact]
    public void The_shown_states_are_the_placed_ones_and_are_taken_from_the_whole_vocabulary() =>
        // The list a query narrows on is derived from the rule rather than written beside it, so
        // there is no second list to forget.
        CalendarMembership.ShownStates
            .ShouldBe([Proposed, Planned, Confirmed, Done, Published, Cancelled, Delayed], ignoreOrder: true);
}
