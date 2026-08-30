// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Notifications;

namespace SilexGis.Domain.Tests;

public class NotificationRoutingTests
{
    [Fact]
    public void Sends_what_the_recipient_asked_to_hear_as_it_happens() =>
        NotificationRouting.Decide(NotificationChannelChoice.Immediate, hasAddress: true)
            .ShouldBe(NotificationRoute.Send);

    [Fact]
    public void Suppresses_what_the_recipient_switched_off_here() =>
        NotificationRouting.Decide(NotificationChannelChoice.Off, hasAddress: true)
            .ShouldBe(NotificationRoute.Suppress);

    [Fact]
    public void Defers_what_the_recipient_asked_for_as_a_daily_summary() =>
        NotificationRouting.Decide(NotificationChannelChoice.Daily, hasAddress: true)
            .ShouldBe(NotificationRoute.Defer);

    [Theory]
    [InlineData(NotificationChannelChoice.Off)]
    [InlineData(NotificationChannelChoice.Immediate)]
    [InlineData(NotificationChannelChoice.Daily)]
    public void Suppresses_every_choice_when_there_is_nowhere_to_send(NotificationChannelChoice choice) =>
        // Including a choice nobody may switch off: an address that is not there cannot be
        // reached by insisting, and a delivery row would only fail its way to dead.
        NotificationRouting.Decide(choice, hasAddress: false).ShouldBe(NotificationRoute.Suppress);

    [Fact]
    public void Retry_delay_widens_and_then_holds()
    {
        var delays = Enumerable.Range(1, NotificationRouting.MaxAttempts)
            .Select(NotificationRouting.RetryDelay)
            .ToList();

        delays.ShouldBe(delays.OrderBy(d => d).ToList(), "each wait should be at least as long as the last");
        delays[0].ShouldBeLessThan(delays[^1]);
        delays[^1].ShouldBeLessThanOrEqualTo(TimeSpan.FromHours(6));
    }

    [Fact]
    public void Next_digest_is_today_when_the_window_is_still_ahead()
    {
        var now = new DateTimeOffset(2026, 7, 31, 6, 59, 0, TimeSpan.Zero);

        NotificationRouting.NextDigest(now, 7)
            .ShouldBe(new DateTimeOffset(2026, 7, 31, 7, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Next_digest_rolls_to_tomorrow_once_the_window_has_arrived()
    {
        // Strictly after, so a row created exactly at the window waits a day rather than being
        // swept into a digest that is already being assembled.
        var now = new DateTimeOffset(2026, 7, 31, 7, 0, 0, TimeSpan.Zero);

        NotificationRouting.NextDigest(now, 7)
            .ShouldBe(new DateTimeOffset(2026, 8, 1, 7, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Next_digest_reads_the_clock_in_utc_whatever_offset_it_arrives_in()
    {
        // 09:30 at +03:00 is 06:30 UTC, so the 07:00 window is still ahead of it today.
        var now = new DateTimeOffset(2026, 7, 31, 9, 30, 0, TimeSpan.FromHours(3));

        NotificationRouting.NextDigest(now, 7)
            .ShouldBe(new DateTimeOffset(2026, 7, 31, 7, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    [InlineData(99)]
    public void Next_digest_clamps_an_impossible_hour_rather_than_throwing(int hour)
    {
        // Misconfiguration must not take the worker down on every tick.
        var next = NotificationRouting.NextDigest(new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero), hour);

        next.Hour.ShouldBeInRange(0, 23);
    }
}
