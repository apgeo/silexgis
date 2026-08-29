// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Notifications;

namespace SilexGis.Domain.Tests;

/// <summary>
/// A table of instants against one window, because every interesting case is an instant: the
/// window that wraps past midnight, the minute at either end of it, and the two nights a year a
/// clock moves — which is where computing the window as a fixed offset from UTC gives an answer
/// that is an hour wrong and looks entirely reasonable.
/// </summary>
public class QuietHoursTests
{
    private const string Bucharest = "Europe/Bucharest";

    /// <summary>The ordinary window: quiet from ten at night until seven in the morning.</summary>
    private static readonly TimeOnly Opens = new(22, 0);
    private static readonly TimeOnly Closes = new(7, 0);

    [Fact]
    public void An_installation_that_names_no_window_holds_nothing_back()
    {
        var due = new DateTimeOffset(2026, 7, 1, 2, 30, 0, TimeSpan.Zero);

        QuietHours.NextAllowed(due, null, null, Bucharest).ShouldBe(due);
        QuietHours.NextAllowed(due, Opens, null, Bucharest).ShouldBe(due);
        QuietHours.NextAllowed(due, null, Closes, Bucharest).ShouldBe(due);
    }

    [Fact]
    public void A_window_that_starts_where_it_ends_is_refused_rather_than_read_as_every_hour()
    {
        // Read as "always quiet" it would silence an account forever from one mistyped setting,
        // which is the one outcome this feature must never be able to produce.
        var due = new DateTimeOffset(2026, 7, 1, 2, 30, 0, TimeSpan.Zero);

        QuietHours.NextAllowed(due, Opens, Opens, Bucharest).ShouldBe(due);
    }

    [Fact]
    public void Daytime_is_not_held_back()
    {
        // 15:00 in Bucharest, deep inside the working day.
        var due = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

        QuietHours.NextAllowed(due, Opens, Closes, Bucharest).ShouldBe(due);
    }

    [Fact]
    public void The_evening_side_of_a_window_that_wraps_waits_until_the_next_morning()
    {
        // 23:30 local on the first of July, which is summer time: UTC+3.
        var due = new DateTimeOffset(2026, 7, 1, 20, 30, 0, TimeSpan.Zero);

        QuietHours.NextAllowed(due, Opens, Closes, Bucharest)
            .ShouldBe(new DateTimeOffset(2026, 7, 2, 4, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void The_small_hours_wait_until_the_same_morning()
    {
        // 03:00 local, past midnight, so the window closes later the same day and not tomorrow.
        var due = new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero);

        QuietHours.NextAllowed(due, Opens, Closes, Bucharest)
            .ShouldBe(new DateTimeOffset(2026, 7, 2, 4, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void The_minute_the_window_opens_is_inside_it_and_the_minute_it_closes_is_not()
    {
        // Exactly 22:00 local: held. Exactly 07:00 local: sent, because a window that included
        // its own end would hold the first message of the morning for a further day.
        var opensAt = new DateTimeOffset(2026, 7, 1, 19, 0, 0, TimeSpan.Zero);
        var closesAt = new DateTimeOffset(2026, 7, 2, 4, 0, 0, TimeSpan.Zero);

        QuietHours.NextAllowed(opensAt, Opens, Closes, Bucharest).ShouldBe(closesAt);
        QuietHours.NextAllowed(closesAt, Opens, Closes, Bucharest).ShouldBe(closesAt);
    }

    [Fact]
    public void A_window_that_does_not_wrap_closes_the_same_day()
    {
        // 13:00 to 14:00 local, which is not a night but is the other shape of window and the
        // one a comparison written only for the wrapping case gets wrong.
        var due = new DateTimeOffset(2026, 7, 1, 10, 30, 0, TimeSpan.Zero);

        QuietHours.NextAllowed(due, new TimeOnly(13, 0), new TimeOnly(14, 0), Bucharest)
            .ShouldBe(new DateTimeOffset(2026, 7, 1, 11, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void The_night_the_clocks_go_forward_ends_an_hour_earlier_in_utc()
    {
        // Romania moves to summer time on 29 March 2026: 03:00 EET becomes 04:00 EEST. A message
        // held on the evening of the 28th is due at 07:00 local on the 29th, which is 04:00 UTC.
        // Held against a fixed UTC+2 — the offset that was in force when it was held — it would
        // be 05:00 UTC, an hour after the reader's morning had started.
        var due = new DateTimeOffset(2026, 3, 28, 21, 30, 0, TimeSpan.Zero);

        QuietHours.NextAllowed(due, Opens, Closes, Bucharest)
            .ShouldBe(new DateTimeOffset(2026, 3, 29, 4, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void The_night_the_clocks_go_back_ends_an_hour_later_in_utc()
    {
        // 25 October 2026: 04:00 EEST becomes 03:00 EET. 07:00 local is 05:00 UTC, an hour later
        // than the same wall time the morning before.
        var due = new DateTimeOffset(2026, 10, 24, 20, 30, 0, TimeSpan.Zero);

        QuietHours.NextAllowed(due, Opens, Closes, Bucharest)
            .ShouldBe(new DateTimeOffset(2026, 10, 25, 5, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_window_closing_in_an_hour_a_clock_skips_closes_at_the_first_minute_that_exists()
    {
        // No instant has a wall time of 03:30 in Bucharest on 29 March 2026 — the clock goes
        // straight from 03:00 to 04:00 — so a window ending then ends when the clock reappears.
        var due = new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.Zero);

        QuietHours.NextAllowed(due, Opens, new TimeOnly(3, 30), Bucharest)
            .ShouldBe(new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_reader_who_has_never_said_where_they_are_is_read_in_utc()
    {
        // 02:30 UTC is inside the window read in UTC and outside it read in Bucharest, so this
        // says which of the two happened.
        var due = new DateTimeOffset(2026, 7, 1, 2, 30, 0, TimeSpan.Zero);

        QuietHours.NextAllowed(due, Opens, Closes, null)
            .ShouldBe(new DateTimeOffset(2026, 7, 1, 7, 0, 0, TimeSpan.Zero));
        QuietHours.NextAllowed(due, Opens, Closes, "   ")
            .ShouldBe(new DateTimeOffset(2026, 7, 1, 7, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_zone_this_host_has_never_heard_of_is_read_in_utc_rather_than_throwing()
    {
        // The browser's copy of the zone database may be newer than the server's, and a name from
        // a newer one must not take the delivery path down.
        var due = new DateTimeOffset(2026, 7, 1, 2, 30, 0, TimeSpan.Zero);

        QuietHours.NextAllowed(due, Opens, Closes, "Mars/Olympus_Mons")
            .ShouldBe(new DateTimeOffset(2026, 7, 1, 7, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("22:00", 22, 0)]
    [InlineData("07:30", 7, 30)]
    [InlineData("00:00", 0, 0)]
    public void A_window_bound_is_read_as_hours_and_minutes(string value, int hour, int minute) =>
        QuietHours.Parse(value).ShouldBe(new TimeOnly(hour, minute));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("10 pm")]
    [InlineData("25:00")]
    [InlineData("7:00")]
    public void A_bound_nobody_can_read_is_nothing_rather_than_a_guess(string? value) =>
        // Nothing means quiet hours are off, so a mistyped setting leaves delivery as it was
        // instead of holding messages at an hour nobody chose.
        QuietHours.Parse(value).ShouldBeNull();
}
