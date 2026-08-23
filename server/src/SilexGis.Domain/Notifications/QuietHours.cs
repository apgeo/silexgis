// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;

namespace SilexGis.Domain.Notifications;

/// <summary>
/// The hours of a person's own night, and when a message that would have interrupted them may
/// leave instead.
/// </summary>
/// <remarks>
/// <para>
/// A pure function over instants, so the rule can be read and tested without a clock, a database
/// or a worker: everything it needs is passed in, and what it returns is the instant a delivery
/// becomes due. Nothing here decides <em>whether</em> something is sent — only when.
/// </para>
/// <para>
/// The window is wall-clock time in the reader's own zone, which is the only reading of "not at
/// three in the morning" that is true all year. Computed against a UTC offset instead it drifts by
/// an hour every spring and autumn in any zone that observes summer time, and by more than that
/// for anybody who has travelled — a feature whose entire purpose is that nothing arrives at 03:00
/// cannot be built on arithmetic that is wrong for half the year.
/// </para>
/// <para>
/// The inbox is deliberately not subject to this. A notification is in the reader's list the
/// moment it happens whatever the hour, because it interrupts nobody and an inbox that hid
/// overnight events would be lying to somebody who woke at four and opened the application. Quiet
/// hours only move what leaves the installation and arrives with a noise attached.
/// </para>
/// </remarks>
public static class QuietHours
{
    /// <summary>
    /// The earliest instant at or after <paramref name="due"/> that falls outside the quiet
    /// window, or <paramref name="due"/> itself when it already does.
    /// </summary>
    /// <remarks>
    /// Quiet hours are off unless both bounds are given, and a window whose start equals its end
    /// is refused rather than read as "every hour of every day" — that reading would silence an
    /// account forever from one mistyped setting, which is the one outcome a quiet-hours feature
    /// must never be able to produce. A zone this host has never heard of falls back to UTC for
    /// the same kind of reason: the browser's copy of the zone database may be newer than the
    /// server's, and a name that arrives from a newer one must not throw on the delivery path.
    /// </remarks>
    /// <param name="due">When the delivery would otherwise become due.</param>
    /// <param name="from">The wall-clock time the quiet window opens, in the reader's zone.</param>
    /// <param name="to">The wall-clock time it closes. Earlier than <paramref name="from"/> for
    /// the ordinary window that wraps past midnight.</param>
    /// <param name="zone">The reader's IANA zone name, or nothing when they have never said.</param>
    public static DateTimeOffset NextAllowed(
        DateTimeOffset due, TimeOnly? from, TimeOnly? to, string? zone)
    {
        if (from is not { } opens || to is not { } closes || opens == closes)
        {
            return due;
        }

        var tz = ZoneOf(zone);
        var local = TimeZoneInfo.ConvertTime(due, tz);
        var wall = TimeOnly.FromDateTime(local.DateTime);

        // A window that wraps past midnight is the ordinary one — 22:00 to 07:00 — and it is the
        // case a straight "between" comparison gets wrong, so the two shapes are spelled out.
        var inside = opens < closes
            ? wall >= opens && wall < closes
            : wall >= opens || wall < closes;

        if (!inside)
        {
            return due;
        }

        // The window closes on the next calendar day only when the wall clock is still on the
        // evening side of a wrapping window; past midnight it closes later the same day.
        var closesOn = opens < closes || wall < closes
            ? DateOnly.FromDateTime(local.DateTime)
            : DateOnly.FromDateTime(local.DateTime).AddDays(1);

        var end = At(closesOn, closes, tz);

        // Clocks going back can put the end of a window at or before the instant it was computed
        // from. The window is over either way; holding the message longer would be arbitrary.
        return end > due ? end : due;
    }

    /// <summary>
    /// Reads a window bound written as <c>HH:mm</c>. Anything else is nothing, so a setting
    /// nobody can parse leaves delivery exactly as it was rather than silencing it.
    /// </summary>
    public static TimeOnly? Parse(string? value) =>
        TimeOnly.TryParseExact(
            value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    private static TimeZoneInfo ZoneOf(string? zone)
    {
        if (string.IsNullOrWhiteSpace(zone))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(zone);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>One wall-clock time on one day in one zone, as the instant it really happens.</summary>
    private static DateTimeOffset At(DateOnly day, TimeOnly time, TimeZoneInfo tz)
    {
        var wall = day.ToDateTime(time, DateTimeKind.Unspecified);

        // The hour a clock skips forward over does not exist: no instant has that wall time, so
        // the window closes at the first one that does.
        while (tz.IsInvalidTime(wall))
        {
            wall = wall.AddMinutes(1);
        }

        // An hour a clock repeats has two instants with this wall time; the offset asked for here
        // is the standard-time one, which is the later of the two — the window is certainly over
        // by then, whereas the earlier reading is still inside it.
        return new DateTimeOffset(wall, tz.GetUtcOffset(wall)).ToUniversalTime();
    }
}
