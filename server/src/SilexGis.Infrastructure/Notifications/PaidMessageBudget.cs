// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Notifications;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>
/// What this installation has spent today on messages that charge per message, and on what.
/// </summary>
/// <remarks>
/// <para>
/// One home for the question because two places ask it and must agree: the send that refuses to
/// go past the day's ceiling, and the operator's view of how much of that ceiling is gone. A view
/// counting differently from the guard is worse than no view — it would read as headroom on the
/// day the guard started refusing.
/// </para>
/// <para>
/// Counted over delivery rows written since midnight UTC, pending ones included: a message the
/// installation has committed to sending is money committed whether or not the gateway has taken
/// it yet. The day is UTC rather than anybody's local one, matching every other stored instant
/// here — an installation whose ceiling reset at a different hour than its logs record would be
/// impossible to reason about after the fact.
/// </para>
/// <para>
/// While nothing charging is wired the channel set is empty and the answer is zero. That is the
/// honest shape rather than a special case: the ceiling is real and simply never binds until
/// there is something to spend.
/// </para>
/// </remarks>
public static class PaidMessageBudget
{
    /// <summary>
    /// The delivery channels an announcement could go out on that charge for every message: the
    /// category's ceiling, narrowed to what is wired here, narrowed again to what the installation
    /// has agreed to pay for.
    /// </summary>
    public static IReadOnlyList<NotificationChannel> ChannelsFor(
        NotificationChannels channels, AnnouncementSettings announcements) =>
        channels.PaidFor(
            NotificationMatrix.Usable(
                NotificationCategory.GroupAnnouncement,
                channels.Installed,
                announcements.PaidChannelsAllowed)
            & NotificationChannelKinds.Paid);

    /// <summary>How many charged messages have been committed to today on those channels.</summary>
    public static async Task<int> SpentTodayAsync(
        SilexGisDbContext db,
        IReadOnlyList<NotificationChannel> paidChannels,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (paidChannels.Count == 0)
        {
            return 0;
        }

        var since = new DateTimeOffset(clock.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
        return await db.NotificationDeliveries
            .CountAsync(d => paidChannels.Contains(d.Channel) && d.CreatedAt >= since, ct);
    }
}
