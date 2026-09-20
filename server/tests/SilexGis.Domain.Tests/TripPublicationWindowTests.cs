// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

/// <summary>
/// When a published trip stops being published.
/// </summary>
/// <remarks>
/// Every case that says a link is closed is stated beside the case that says it is open, because a
/// rule that returned false for everything would satisfy each closure on its own and would take the
/// feature away. The pairs are the point: the same link, one thing different.
/// </remarks>
public class TripPublicationWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Grace = TimeSpan.FromDays(2);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    [Fact]
    public void An_armed_watch_inside_the_window_is_open_and_the_same_link_past_it_is_not()
    {
        // The backstop, and the failure the column exists for: a watch nobody ever closed, on a
        // trip that went fine, with the address sitting in an article being crawled.
        TripPublicationWindow.IsOpen(
            Now, revokedAt: null, expiresAt: Now.AddDays(1),
            TripTrackingState.Armed, closedAt: null, Grace).ShouldBeTrue();

        TripPublicationWindow.IsOpen(
            Now, revokedAt: null, expiresAt: Now.AddSeconds(-1),
            TripTrackingState.Armed, closedAt: null, Grace).ShouldBeFalse();
    }

    [Fact]
    public void The_instant_the_window_ends_is_already_closed()
        // Stated because it is the boundary somebody will change by accident: an expiry that is
        // inclusive means a link that "works until Thursday" works on Thursday for an unbounded
        // instant, and the whole value of this column is that it is an end.
        => TripPublicationWindow.IsOpen(
            Now, revokedAt: null, expiresAt: Now,
            TripTrackingState.Armed, closedAt: null, Grace).ShouldBeFalse();

    [Fact]
    public void A_revoked_link_is_closed_and_its_unrevoked_twin_is_open()
    {
        TripPublicationWindow.IsOpen(
            Now, revokedAt: Now.AddHours(-1), expiresAt: Now.AddDays(5),
            TripTrackingState.Armed, closedAt: null, Grace).ShouldBeFalse();

        TripPublicationWindow.IsOpen(
            Now, revokedAt: null, expiresAt: Now.AddDays(5),
            TripTrackingState.Armed, closedAt: null, Grace).ShouldBeTrue();
    }

    [Fact]
    public void A_closed_watch_keeps_answering_through_the_grace_window_and_stops_after_it()
    {
        // The half that is easy to argue away and must not be. The instant a coordinator closes the
        // watch is the instant the page has its most important thing to say — everybody is out —
        // and it is when the people who have been refreshing it all evening are looking.
        TripPublicationWindow.IsOpen(
            Now, revokedAt: null, expiresAt: Now.AddDays(10),
            TripTrackingState.Closed, closedAt: Now.AddHours(-1), Grace).ShouldBeTrue();

        TripPublicationWindow.IsOpen(
            Now, revokedAt: null, expiresAt: Now.AddDays(10),
            TripTrackingState.Closed, closedAt: Now - Grace, Grace).ShouldBeFalse();
    }

    [Fact]
    public void The_expiry_still_wins_inside_the_grace_window()
        // The two bounds are not alternatives: the earliest ending wins, so a watch closed a minute
        // ago on a link that ran out yesterday opens nothing. Without this the grace window would
        // be a way to reach past the expiry.
        => TripPublicationWindow.IsOpen(
            Now, revokedAt: null, expiresAt: Now.AddDays(-1),
            TripTrackingState.Closed, closedAt: Now.AddMinutes(-1), Grace).ShouldBeFalse();

    [Fact]
    public void A_watch_that_is_off_opens_nothing_even_with_everything_else_in_order()
    {
        // Never armed, or stood down deliberately. The twin beside it is the identical row with
        // the state changed, so this cannot pass because some other field was wrong.
        TripPublicationWindow.IsOpen(
            Now, revokedAt: null, expiresAt: Now.AddDays(5),
            TripTrackingState.Off, closedAt: null, Grace).ShouldBeFalse();

        TripPublicationWindow.IsOpen(
            Now, revokedAt: null, expiresAt: Now.AddDays(5),
            TripTrackingState.Armed, closedAt: null, Grace).ShouldBeTrue();
    }

    [Fact]
    public void A_watch_recorded_as_closed_with_no_instant_is_treated_as_closed_long_ago()
        // Fail-closed for a row whose history is incomplete: the alternative reading — "closed just
        // now" — would hand an unbounded grace window to exactly the rows nothing can date.
        => TripPublicationWindow.IsOpen(
            Now, revokedAt: null, expiresAt: Now.AddDays(5),
            TripTrackingState.Closed, closedAt: null, Grace).ShouldBeFalse();

    [Fact]
    public void A_link_minted_during_a_trip_lasts_one_window_past_the_end_of_it()
    {
        // The trip's dates decide where the countdown starts, not whether the page answers. A link
        // minted on the first morning of a three-week expedition must not lapse in the middle of it.
        var firstMorning = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        var expedition = TripPublicationWindow.ExpiresAtFor(
            firstMorning, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 21), Lifetime);

        expedition.ShouldBe(new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero) + Lifetime);
        // Which is to say: still open on the expedition's last day, which counting from the mint
        // would not have been.
        expedition.ShouldBeGreaterThan(new DateTimeOffset(2026, 9, 21, 23, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_one_day_trip_gets_the_window_and_no_more()
    {
        var morning = new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);

        TripPublicationWindow.ExpiresAtFor(morning, new DateOnly(2026, 9, 12), null, Lifetime)
            .ShouldBe(new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero) + Lifetime);
    }

    [Fact]
    public void A_link_minted_long_after_the_trip_counts_from_now_rather_than_from_the_trip()
        // Otherwise a club writing up last winter's trips would mint links that were born expired,
        // which is a different failure from the one this exists to prevent and just as unhelpful.
        => TripPublicationWindow.ExpiresAtFor(Now, new DateOnly(2025, 1, 5), null, Lifetime)
            .ShouldBe(Now + Lifetime);

    [Fact]
    public void An_end_date_earlier_than_the_start_is_ignored_rather_than_obeyed()
        // A trip whose recorded end precedes its start is nonsense somebody typed, and reading it
        // literally would shorten the window on the strength of it.
        => TripPublicationWindow.ExpiresAtFor(
                new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero),
                new DateOnly(2026, 9, 12), new DateOnly(2026, 9, 1), Lifetime)
            .ShouldBe(new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero) + Lifetime);
}
