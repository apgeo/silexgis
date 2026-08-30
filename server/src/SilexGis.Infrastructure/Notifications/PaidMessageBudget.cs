// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Notifications;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>
/// What this installation has spent today on channels that charge for what they send, and on
/// what.
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
/// What a row costs is the number of pieces the carrier split its text into, not one: a text is
/// billed by the segment, and a single character outside the narrow alphabet re-encodes a whole
/// message into segments less than half the size — so an installation writing to people in a
/// language with diacritics pays twice what counting messages would say, on wording of exactly
/// the same length. The amount is taken where the text exists, which is the hand-over, and stored
/// on the row; this reads the column rather than re-rendering anything, because the text a row
/// was sent with does not survive on it and an operator may have rewritten that wording since.
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
    /// What that work will cost is estimated rather than counted, because the text of a copy
    /// nobody has been handed yet does not exist. The estimate is not a constant: whoever accepted
    /// the work weighed the wording that will leave, with that announcement's own group and sender
    /// names in it, in every language the installation writes — so a long club name costs what a
    /// long club name costs rather than what a short one does. That amount is carried on the rows
    /// the work left behind and read back here, so the figure the next sender is refused against
    /// is the figure the last one was measured with.
    /// </para>
    /// <para>
    /// Work that nobody weighed falls back to a floor, which is a guess and is documented as one
    /// where it is defined. It is never taken as cheaper than that floor even when a weighed
    /// amount is smaller, and the floor is deliberately not one: assuming one would be assuming
    /// the narrow alphabet, and under-counting on that assumption is the whole reason this number
    /// was wrong before.
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

        // Written and not yet routed, each at what its producer weighed one copy of it at — the
        // same amount the row will be given when it is routed, so the number does not step up or
        // down as the rows appear.
        var waiting = await db.Notifications
            .Where(n => n.RoutedAt == null && n.CreatedAt >= since && PaidCategories.Contains(n.Category))
            .SumAsync(
                n => (int?)(n.SegmentsPerCopy > TextMessageSegments.Unrendered
                    ? n.SegmentsPerCopy
                    : TextMessageSegments.Unrendered),
                ct) ?? 0;

        // Accepted and not yet turned into notifications at all: everybody it still has to reach,
        // at what the sender was measured against when it was accepted.
        var unexpanded = await db.CavingGroupAnnouncements
            .Where(a => a.ExpandedAt == null && a.CreatedAt >= since)
            .SumAsync(
                a => (int?)(a.RecipientCount * (a.SegmentsPerCopy > TextMessageSegments.Unrendered
                    ? a.SegmentsPerCopy
                    : TextMessageSegments.Unrendered)),
                ct) ?? 0;

        // On every charging channel, because the accepted work may go out on each of them.
        return spent + ((waiting + unexpanded) * paidChannels.Count);
    }

    /// <summary>
    /// What today's charged messages on those channels have cost — the ledger of rows, pending
    /// ones included, and the half of the day's cost that already exists.
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

        // The same rows the count was over, summed by what each one costs instead of by existing.
        // Nullable because a day with no rows in it sums to nothing rather than to zero, and this
        // is asked on an idle installation far more often than on a busy one.
        return await db.NotificationDeliveries
            .Where(d => paidChannels.Contains(d.Channel) && d.CreatedAt >= since)
            .SumAsync(d => (int?)d.Segments, ct) ?? 0;
    }

    /// <summary>
    /// What one copy of an announcement will cost at worst, weighed from the wording that would
    /// actually leave.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked before anything is written, by the guard that decides whether an announcement may be
    /// made at all, and again nowhere: the answer is recorded on what the announcement leaves
    /// behind so that the projection and the refusal cannot come apart.
    /// </para>
    /// <para>
    /// Everything the text will say is known here except who reads it in which language, and that
    /// is the one thing that doubles the price — so every language the installation writes is
    /// weighed and the largest answer stands. An audience who all read the cheap language is
    /// therefore charged more than they cost, which is the safe direction for a ceiling; charging
    /// them less would let through work the installation has said it will not pay for. The names
    /// are the real ones, so a club whose name fills half the message is charged for it.
    /// </para>
    /// <para>
    /// Never less than one. A message is a message even where a wording renders to nothing, and a
    /// guard whose whole purpose is to know what has been spent must not answer "free".
    /// </para>
    /// </remarks>
    public static async Task<int> AnnouncementSegmentsAsync(
        IMessageDispatcher dispatcher,
        IConfiguration configuration,
        string senderName,
        string cavingGroupName,
        CancellationToken ct)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["actorName"] = senderName,
            ["cavingGroupName"] = cavingGroupName,

            // Whole rather than a path, because that is what is rendered on the way out and the
            // installation's own address is a good part of what a text message weighs.
            ["url"] = NotificationLinks.Absolute(configuration, NotificationLinks.Inbox),
        };

        var weighed = await dispatcher.WeighAsync(
            MessageTemplateCatalog.NotifyGroupAnnouncement, MessageChannel.Sms, values, ct);

        return Math.Max(weighed, 1);
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
