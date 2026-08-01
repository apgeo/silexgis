// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Notifications;

namespace SilexGis.Domain.Tests;

public class NotificationRoutingTests
{
    /// <summary>Everything on and immediate, so each test names only what it varies.</summary>
    private static NotificationRoute Decide(
        NotificationCategory category = NotificationCategory.CavingGroupMembership,
        bool categoryEnabled = true,
        bool masterEmailEnabled = true,
        NotificationDigest digest = NotificationDigest.Immediate,
        bool hasAddress = true) =>
        NotificationRouting.Decide(category, categoryEnabled, masterEmailEnabled, digest, hasAddress);

    [Fact]
    public void Sends_when_everything_is_on() => Decide().ShouldBe(NotificationRoute.Send);

    [Fact]
    public void Suppresses_a_category_the_recipient_switched_off() =>
        Decide(categoryEnabled: false).ShouldBe(NotificationRoute.Suppress);

    [Fact]
    public void Suppresses_everything_when_the_master_switch_is_off() =>
        Decide(masterEmailEnabled: false).ShouldBe(NotificationRoute.Suppress);

    [Fact]
    public void Defers_to_the_digest_when_the_recipient_asked_for_a_daily_summary() =>
        Decide(digest: NotificationDigest.Daily).ShouldBe(NotificationRoute.Defer);

    [Fact]
    public void Sends_a_security_alert_even_with_every_switch_off()
    {
        // A security alert warns someone their account is being taken over, and whoever is doing
        // it may hold a live session. The settings page refuses to switch this category off for
        // the same reason, so honouring the switches here would be a hole behind that refusal.
        Decide(
            NotificationCategory.SecurityAlerts,
            categoryEnabled: false,
            masterEmailEnabled: false,
            digest: NotificationDigest.Daily)
            .ShouldBe(NotificationRoute.Send);
    }

    [Fact]
    public void Suppresses_even_a_security_alert_when_there_is_no_address() =>
        Decide(NotificationCategory.SecurityAlerts, hasAddress: false).ShouldBe(NotificationRoute.Suppress);

    [Fact]
    public void Every_ordinary_category_honours_the_switches() =>
        NotificationCategories.All
            .Where(c => !NotificationCategories.IsAlwaysImmediate(c))
            .ShouldAllBe(c => Decide(c, categoryEnabled: false) == NotificationRoute.Suppress);

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
