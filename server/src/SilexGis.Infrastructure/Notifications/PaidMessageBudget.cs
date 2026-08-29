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
/// A row is the charge, which decides which side of the line every path falls on. A message
/// refused before anything was handed over — a channel the installation has not agreed to pay
/// for, a recipient with no proved address, a wording with no form that travels that way — is a
/// row that was never written, and costs nothing. A message the gateway took and then reported a
/// failure on is a row that exists, and costs, because the far side may well have sent it: the
/// only honest reading of a failed hand-over is that it may have happened. A row removed
/// afterwards, because the recipient's answer changed before it went, gives the day its money
/// back for the same reason — nothing left.
/// </para>
/// <para>
/// The channel set is derived from what is registered rather than named here, so an installation
/// with nothing charging wired counts zero without a special case for it.
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

    /// <summary>
    /// What today has been committed to spending, including work that is accepted but has not
    /// reached a delivery row yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What the guard on a new announcement must read, and it is deliberately a larger number
    /// than the ledger above. An announcement is accepted in one transaction and turned into
    /// outbound copies by a background pass seconds or minutes later, so between the two there is
    /// a window in which the rows that would prove it exist do not. A second announcement made in
    /// that window reads the same headroom the first one already took, and both are let through —
    /// which is how a ceiling of a hundred is passed by two announcements of sixty.
    /// </para>
    /// <para>
    /// Counted at its worst, for the same reason the announcement's own cost is: everybody the
    /// accepted work still has to reach, on every charging channel it may use. Some of those
    /// people will have switched the channel off, so the real bill is usually smaller — but a
    /// bound that assumed the usual case would let the expensive case through.
    /// </para>
    /// <para>
    /// Two shapes of accepted work, because an announcement takes one of two paths: a small
    /// roster is written straight into notifications that have not been routed yet, and a large
    /// one is recorded once and expanded later. They cannot double-count each other — the
    /// expansion writes the notifications and stamps the record in one save, so a record is
    /// waiting or its notifications exist, never both.
    /// </para>
    /// <para>
    /// The operator's page reads this one too, and not the ledger below. A view counting
    /// differently from the guard is worse than no view — it would read as headroom on the day
    /// the guard started refusing — so the number an operator is shown is the number the next
    /// announcement will be measured against, promises included.
    /// </para>
    /// </remarks>
    public static async Task<int> CommittedTodayAsync(
        SilexGisDbContext db,
        IReadOnlyList<NotificationChannel> paidChannels,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (paidChannels.Count == 0)
        {
            return 0;
        }

        var since = Midnight(clock);
        var spent = await SpentTodayAsync(db, paidChannels, clock, ct);

        var waiting = await db.Notifications
            .CountAsync(n => n.RoutedAt == null && n.CreatedAt >= since && PaidCategories.Contains(n.Category), ct);

        var unexpanded = await db.CavingGroupAnnouncements
            .Where(a => a.ExpandedAt == null && a.CreatedAt >= since)
            .SumAsync(a => (int?)a.RecipientCount, ct) ?? 0;

        return spent + ((waiting + unexpanded) * paidChannels.Count);
    }

    /// <summary>
    /// How many charged messages have been handed over today on those channels — the ledger of
    /// rows, pending ones included, and the half of the day's cost that already exists.
    /// </summary>
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

        var since = Midnight(clock);
        return await db.NotificationDeliveries
            .CountAsync(d => paidChannels.Contains(d.Channel) && d.CreatedAt >= since, ct);
    }

    /// <summary>
    /// The categories that could ever put a message on a charging channel, read from the same
    /// ceilings the routing does rather than named here, so a category given one later is counted
    /// without this remembering to be edited.
    /// </summary>
    private static readonly NotificationCategory[] PaidCategories =
        [.. NotificationCategories.All
            .Where(category =>
                (NotificationCategories.Ceiling(category) & NotificationChannelKinds.Paid)
                != NotificationChannelKind.None)];

    /// <summary>
    /// The start of the day the ceiling is counted over. UTC rather than anybody's local one,
    /// matching every other stored instant here.
    /// </summary>
    private static DateTimeOffset Midnight(TimeProvider clock) =>
        new(clock.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
}
