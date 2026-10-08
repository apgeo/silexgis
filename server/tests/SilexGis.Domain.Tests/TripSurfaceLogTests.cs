// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Which watches a camp's surface log lists: every running one, and a closed one for a while.
/// </summary>
/// <remarks>
/// Each refusal is stated beside the case it differs from by one fact, because a rule that
/// answered false to everything would pass every "is not listed" on its own.
/// </remarks>
public class TripSurfaceLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 18, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan TwoDays = TimeSpan.FromDays(2);

    // When the watches below were armed, unless a case is about that very fact.
    private static readonly DateTimeOffset Armed = Now.AddHours(-9);

    [Fact]
    public void A_running_watch_is_listed_however_long_it_has_run_and_whatever_the_window()
    {
        // The row a coordinator must not lose is the watch nobody closed, so its age is no
        // argument — and neither is a window of nothing, which is about closed watches only.
        TripSurfaceLog.Lists(Now, TripTrackingState.Armed, Armed, closedAt: null, TwoDays).ShouldBeTrue();
        TripSurfaceLog.Lists(Now, TripTrackingState.Armed, Armed, closedAt: null, TimeSpan.Zero).ShouldBeTrue();
        // Whatever closing instant the row happens to carry: it says nothing about a watch
        // that is running, and the rule does not consult it.
        TripSurfaceLog.Lists(Now, TripTrackingState.Armed, Armed, Now.AddDays(-40), TwoDays).ShouldBeTrue();
    }

    [Fact]
    public void A_closed_watch_is_listed_until_the_window_has_passed_and_not_at_the_instant_it_does()
    {
        var closed = Now - TwoDays;

        // The edge to the second: one second inside the window, the instant it ends, one past.
        TripSurfaceLog.Lists(Now.AddSeconds(-1), TripTrackingState.Closed, Armed, closed, TwoDays).ShouldBeTrue();
        TripSurfaceLog.Lists(Now, TripTrackingState.Closed, Armed, closed, TwoDays).ShouldBeFalse();
        TripSurfaceLog.Lists(Now.AddSeconds(1), TripTrackingState.Closed, Armed, closed, TwoDays).ShouldBeFalse();

        // And the near edge: closed this very instant is the row's best moment.
        TripSurfaceLog.Lists(Now, TripTrackingState.Closed, Armed, Now, TwoDays).ShouldBeTrue();
    }

    [Fact]
    public void A_closing_instant_slightly_ahead_of_the_clock_is_just_closed_not_an_error()
    {
        // Two machines' clocks, or a closing written a moment before the read's own reading of
        // the time. Listed, beside the long-closed case so this is not "closed is always listed".
        TripSurfaceLog.Lists(Now, TripTrackingState.Closed, Armed, Now.AddSeconds(5), TwoDays).ShouldBeTrue();
        TripSurfaceLog.Lists(Now, TripTrackingState.Closed, Armed, Now.AddDays(-3), TwoDays).ShouldBeFalse();
    }

    [Fact]
    public void A_closed_watch_with_no_closing_instant_is_read_as_closed_long_ago()
    {
        TripSurfaceLog.Lists(Now, TripTrackingState.Closed, Armed, closedAt: null, TwoDays).ShouldBeFalse();
        // The same watch with an instant is listed — the missing value is what decided it.
        TripSurfaceLog.Lists(Now, TripTrackingState.Closed, Armed, Now.AddHours(-1), TwoDays).ShouldBeTrue();
    }

    [Fact]
    public void A_window_of_nothing_lists_running_watches_only()
    {
        TripSurfaceLog.Lists(Now, TripTrackingState.Closed, Armed, Now, TimeSpan.Zero).ShouldBeFalse();
        // Not even a closing instant ahead of the clock gets past a window of nothing, and a
        // negative window — which start-up refuses — is read the same way rather than as "always".
        TripSurfaceLog.Lists(Now, TripTrackingState.Closed, Armed, Now.AddSeconds(5), TimeSpan.Zero).ShouldBeFalse();
        TripSurfaceLog.Lists(Now, TripTrackingState.Closed, Armed, Now.AddSeconds(5), TimeSpan.FromHours(-1))
            .ShouldBeFalse();
        TripSurfaceLog.Lists(Now, TripTrackingState.Armed, Armed, closedAt: null, TimeSpan.Zero).ShouldBeTrue();
    }

    [Fact]
    public void A_very_long_window_is_a_long_window_and_not_an_overflow()
    {
        TripSurfaceLog.Lists(Now, TripTrackingState.Closed, Armed, Now.AddYears(-30), TimeSpan.MaxValue).ShouldBeTrue();
        TripSurfaceLog.Lists(Now, TripTrackingState.Closed, Armed, DateTimeOffset.MinValue, TimeSpan.MaxValue)
            .ShouldBeTrue();
    }

    [Fact]
    public void A_watch_filed_as_closed_that_never_ran_is_not_a_party_that_just_came_out()
    {
        // What importing a device's recording writes: closed at the moment of the import, never
        // armed. Closed a minute ago by the clock, about a party nobody at the surface followed.
        var filed = Now.AddMinutes(-1);
        TripSurfaceLog.Lists(Now, TripTrackingState.Closed, armedAt: null, filed, TwoDays).ShouldBeFalse();
        // However generous the window.
        TripSurfaceLog.Lists(Now, TripTrackingState.Closed, armedAt: null, filed, TimeSpan.MaxValue)
            .ShouldBeFalse();
        // The same row, once armed, is listed — the missing arming is what decided it, and how
        // long ago the arming was is no part of the question.
        TripSurfaceLog.Lists(Now, TripTrackingState.Closed, Armed, filed, TwoDays).ShouldBeTrue();
        TripSurfaceLog.Lists(Now, TripTrackingState.Closed, Now.AddDays(-40), filed, TwoDays).ShouldBeTrue();
        // And a running watch is never asked how it came to run.
        TripSurfaceLog.Lists(Now, TripTrackingState.Armed, armedAt: null, closedAt: null, TwoDays).ShouldBeTrue();
    }

    [Fact]
    public void A_watch_that_was_never_armed_is_never_listed()
    {
        TripSurfaceLog.Lists(Now, TripTrackingState.Off, armedAt: null, closedAt: null, TwoDays).ShouldBeFalse();
        // Not even with a closing instant inside the window, which no request can write but a
        // row could carry: nobody was followed, so there is no party to count.
        TripSurfaceLog.Lists(Now, TripTrackingState.Off, armedAt: null, Now, TwoDays).ShouldBeFalse();
        TripSurfaceLog.Lists(Now, TripTrackingState.Armed, Armed, closedAt: null, TwoDays).ShouldBeTrue();
    }

    [Fact]
    public void Every_state_the_watch_can_hold_is_answered_for()
    {
        // A state added later would fall into the "not listed" branch without anybody deciding
        // that. If this fails, decide what the new state means for the log and say it in the rule.
        Enum.GetValues<TripTrackingState>().Length.ShouldBe(3);
    }
}
