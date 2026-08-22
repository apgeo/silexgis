// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Notifications;

/// <summary>
/// What one channel answers when asked whether it would carry a notification.
/// </summary>
/// <remarks>
/// A decision, never a result, and the two are deliberately kept apart: what a channel answers
/// here decides whether a delivery row exists at all, and what happens to that row afterwards is
/// a transport outcome recorded on the row itself. That is why nothing is ever stored as
/// "suppressed" — a refusal produces no row to store it on, and the notification is in the
/// recipient's inbox either way.
/// </remarks>
public enum NotificationRoute
{
    /// <summary>Send it now — a delivery due immediately.</summary>
    Send,

    /// <summary>Hold it for the recipient's daily digest — a delivery due at the next window.</summary>
    Defer,

    /// <summary>
    /// Do not send it: the recipient asked not to hear about this here, or cannot be reached on
    /// this channel. No delivery row is created, which is the whole of what a refusal means.
    /// </summary>
    Suppress,
}

/// <summary>
/// Whether a queued notification is sent, held for a digest, or dropped — and how a failed send
/// backs off.
/// </summary>
/// <remarks>
/// Pure functions in Domain, beside the other rules that decide what a person may receive or see,
/// so the policy has one home and can be tested without a database or a mail server. The worker
/// supplies the facts; this decides.
/// </remarks>
public static class NotificationRouting
{
    /// <summary>How many sends are attempted before a row is given up on.</summary>
    public const int MaxAttempts = 5;

    /// <summary>
    /// Decides what to do with one notification.
    /// </summary>
    /// <param name="category">What the notification is about.</param>
    /// <param name="categoryEnabled">The recipient's answer for that category.</param>
    /// <param name="masterEmailEnabled">The recipient's "notify me by email" switch.</param>
    /// <param name="digest">Immediate, or batched into a daily summary.</param>
    /// <param name="hasAddress">Whether there is anywhere to send it.</param>
    public static NotificationRoute Decide(
        NotificationCategory category,
        bool categoryEnabled,
        bool masterEmailEnabled,
        NotificationDigest digest,
        bool hasAddress)
    {
        // Nowhere to send beats everything, including a security alert: there is no address.
        if (!hasAddress)
        {
            return NotificationRoute.Suppress;
        }

        // A security alert warns someone their account is being taken over. Whoever is doing it
        // may hold a live session, so neither the master switch nor a digest delay applies —
        // the settings page refuses to switch this category off for the same reason.
        if (NotificationCategories.IsAlwaysImmediate(category))
        {
            return NotificationRoute.Send;
        }

        if (!masterEmailEnabled || !categoryEnabled)
        {
            return NotificationRoute.Suppress;
        }

        return digest == NotificationDigest.Daily ? NotificationRoute.Defer : NotificationRoute.Send;
    }

    /// <summary>
    /// How long to wait before attempting a send again. Widening steps, because the failures that
    /// are worth retrying at all (a mail server refusing connections, a gateway rate-limiting) are
    /// the kind that take minutes or hours to clear rather than seconds.
    /// </summary>
    public static TimeSpan RetryDelay(int attempts) => attempts switch
    {
        <= 1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(30),
        4 => TimeSpan.FromHours(2),
        _ => TimeSpan.FromHours(6),
    };

    /// <summary>
    /// The next time the daily digest window opens, strictly after <paramref name="now"/>.
    /// </summary>
    /// <remarks>
    /// The hour is UTC. There is no per-user timezone anywhere in the schema, so inventing one
    /// here would be a column, a settings field and a migration for a refinement nobody asked for;
    /// the digest arriving at a fixed hour is the honest behaviour until that exists.
    /// </remarks>
    public static DateTimeOffset NextDigest(DateTimeOffset now, int hourUtc)
    {
        var hour = Math.Clamp(hourUtc, 0, 23);
        var utc = now.ToUniversalTime();
        var today = new DateTimeOffset(utc.Year, utc.Month, utc.Day, hour, 0, 0, TimeSpan.Zero);
        return today > utc ? today : today.AddDays(1);
    }
}
