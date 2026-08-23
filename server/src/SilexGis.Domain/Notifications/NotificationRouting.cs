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
    /// Decides what to do with one notification on one channel.
    /// </summary>
    /// <remarks>
    /// Every question about who wants what has already been answered by the time this is called:
    /// <paramref name="choice"/> is one resolved cell of the preference matrix, so the categories
    /// nobody may switch off and the channels a category may never use are settled in one place
    /// rather than argued about again here. What is left is the pair of facts only the transport
    /// knows — is there anywhere to send, and does this choice mean now or later.
    /// </remarks>
    /// <param name="choice">The recipient's resolved answer for this category on this channel.</param>
    /// <param name="hasAddress">Whether there is anywhere to send it.</param>
    public static NotificationRoute Decide(NotificationChannelChoice choice, bool hasAddress)
    {
        // Nowhere to send beats everything, including a category nobody may switch off: an
        // address that is not there cannot be reached by insisting.
        if (!hasAddress)
        {
            return NotificationRoute.Suppress;
        }

        return choice switch
        {
            NotificationChannelChoice.Immediate => NotificationRoute.Send,
            NotificationChannelChoice.Daily => NotificationRoute.Defer,
            _ => NotificationRoute.Suppress,
        };
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
    /// The hour is UTC and installation-wide: one window means one claim gathering one recipient's
    /// whole batch, and a summary arriving at a fixed hour is a weaker promise than a message
    /// arriving now, so it is the one that can afford to be approximate. The recipient's own zone
    /// is known and is not read here on purpose — it decides the hours nothing may interrupt them
    /// in, which is applied to whatever this returns, and per-user summary hours are a separate
    /// question nobody has asked yet.
    /// </remarks>
    public static DateTimeOffset NextDigest(DateTimeOffset now, int hourUtc)
    {
        var hour = Math.Clamp(hourUtc, 0, 23);
        var utc = now.ToUniversalTime();
        var today = new DateTimeOffset(utc.Year, utc.Month, utc.Day, hour, 0, 0, TimeSpan.Zero);
        return today > utc ? today : today.AddDays(1);
    }
}
