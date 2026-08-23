// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Notifications;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>
/// Decides which channels a notification goes out on, and gets it out of the system on them.
/// </summary>
/// <remarks>
/// <para>
/// The only place that reads a recipient's address, language and preferences — producers write an
/// id and the facts, and everything about the person is resolved here. That is what lets feature
/// slices queue notifications at all without touching Identity types.
/// </para>
/// <para>
/// Routing happens once per notification and produces zero or more deliveries: one row per channel
/// that has to leave the system. Nothing is "suppressed" — a recipient who does not want email
/// about something simply gets no email delivery for it, and the notification is still in their
/// inbox, because the events somebody switched a transport off for are exactly the ones an inbox
/// exists to show. The inbox is a cell of the same matrix and can be switched off too, but that
/// is not decided here: the row is written whatever anybody has chosen, and the choice is applied
/// where the inbox is read.
/// </para>
/// <para>
/// This class knows no transport. Which channels exist, what each of them needs to reach somebody
/// and how each hands a message over all live behind <see cref="INotificationChannel"/>; here
/// there is only the loop that asks them and the rule that turns their answers into rows. So a
/// second way out of the system is a new implementation, not an edit to this file.
/// </para>
/// <para>
/// A plain service rather than logic inside the worker, so tests can drive a drain directly
/// instead of waiting on a poll.
/// </para>
/// </remarks>
public sealed class NotificationDeliveryService(
    SilexGisDbContext db,
    NotificationChannels channels,
    IMessageDispatcher dispatcher,
    IUnsubscribeTokens unsubscribeTokens,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<NotificationDeliveryService> logger)
{
    /// <summary>Rows leased per drain pass. Small enough that a crash re-sends few, if any.</summary>
    private const int BatchSize = 25;

    /// <summary>Lines listed in a summary before it says how many more there were.</summary>
    private const int MaxDigestItems = 50;

    /// <summary>
    /// How long a notification is kept when the installation says nothing. A year, because the
    /// window is now what an inbox may still show rather than how long an outbound copy is worth
    /// retrying — and a person coming back after a long absence should still find what happened.
    /// </summary>
    private const int DefaultRetentionDays = 365;

    /// <summary>
    /// Routes whatever has not been routed yet, then sends whatever is immediately due. Returns
    /// how many rows it moved, so a caller can drain by looping until it returns zero.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var routed = await RouteAsync(ct);
        var delivered = await DeliverAsync(ct);
        return routed + delivered;
    }

    /// <summary>
    /// Decides the outbound channels for notifications that have none yet. One pass per
    /// notification, ever: the claim stamps the row as routed.
    /// </summary>
    public async Task<int> RouteAsync(CancellationToken ct)
    {
        var ids = await NotificationDeliverySql.ClaimUnroutedAsync(db, BatchSize, ct);
        if (ids.Count == 0)
        {
            return 0;
        }

        var rows = await db.Notifications.Where(n => ids.Contains(n.Id)).ToListAsync(ct);
        foreach (var row in rows)
        {
            await RouteOneAsync(row, ct);
        }

        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    /// <summary>Sends every immediately-due delivery. Returns how many it settled.</summary>
    public async Task<int> DeliverAsync(CancellationToken ct)
    {
        var ids = await NotificationDeliverySql.ClaimDueAsync(db, BatchSize, ct);
        if (ids.Count == 0)
        {
            return 0;
        }

        var deliveries = await db.NotificationDeliveries.Where(d => ids.Contains(d.Id)).ToListAsync(ct);
        var notifications = await NotificationsOfAsync(deliveries, ct);
        foreach (var delivery in deliveries)
        {
            await SendAsync(delivery, notifications[delivery.NotificationId], ct);
        }

        await db.SaveChangesAsync(ct);
        return deliveries.Count;
    }

    /// <summary>
    /// Sends one recipient's daily summary, if anyone's is due. Returns how many deliveries it
    /// settled, so a caller can drain by looping until it returns zero.
    /// </summary>
    public async Task<int> RunDigestAsync(CancellationToken ct)
    {
        var ids = await NotificationDeliverySql.ClaimDueDigestAsync(db, ct);
        if (ids.Count == 0)
        {
            return 0;
        }

        var claimed = await db.NotificationDeliveries
            .Where(d => ids.Contains(d.Id)).OrderBy(d => d.Id).ToListAsync(ct);
        var notifications = await NotificationsOfAsync(claimed, ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == claimed[0].RecipientUserId, ct);

        // Preferences are read again here, not only when the delivery was deferred. A summary is
        // written hours after the events it collects, and the link it carries is itself an
        // invitation to switch it off — so between the deferral and the send the recipient may
        // have done exactly that. Sending anyway would answer "the summary has stopped" with one
        // more summary. Dropping the delivery is all that is needed: the notifications themselves
        // stay in the inbox, which is where somebody who switched email off reads them.
        if (user?.Email is null)
        {
            db.NotificationDeliveries.RemoveRange(claimed);
            await db.SaveChangesAsync(ct);
            return claimed.Count;
        }

        // The same second thought applied per category: a summary claimed for today may contain
        // lines about something the recipient has since switched off, and those must not arrive.
        var stored = await db.UserNotificationPreferences
            .Where(p => p.UserId == user.Id && p.Channel == NotificationChannelKind.Email)
            .ToDictionaryAsync(p => p.Category, p => p.Choice, ct);

        var wanted = claimed
            .Where(d => Resolve(notifications[d.NotificationId].Category, NotificationChannelKind.Email, stored)
                is not NotificationChannelChoice.Off)
            .ToList();

        var dropped = claimed.Where(d => !wanted.Contains(d)).ToList();
        if (dropped.Count > 0)
        {
            db.NotificationDeliveries.RemoveRange(dropped);
        }

        if (wanted.Count == 0)
        {
            await db.SaveChangesAsync(ct);
            return claimed.Count;
        }

        // Each line is that event's own subject: already a one-line summary, already translated,
        // already editable by the operator. Single newlines — the renderer collapses blank ones.
        var lines = new List<string>();
        foreach (var delivery in wanted.Take(MaxDigestItems))
        {
            lines.Add("- " + await SubjectOfAsync(notifications[delivery.NotificationId], user, ct));
        }

        var values = BaseValues(user);
        values["itemCount"] = wanted.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        values["items"] = string.Join("\n", lines);
        // A summary has no category of its own, so its opt-out link cannot name one: it collects
        // whatever the recipient still hears about, and a token for one of those would switch off
        // something they never said anything about.
        values["unsubscribeUrl"] = DigestUnsubscribeLine(user.Id);

        // A summary is one channel's: every line in it was deferred by the same channel, and the
        // claim gathers one recipient's due deferrals — which cannot span channels while only one
        // of them defers at all. A second deferring channel has to make the claim name its own.
        var result = await channels.Of(claimed[0].Channel)
            .SendAsync(user, MessageTemplateCatalog.NotifyDigest, values, ct);

        if (result.Sent)
        {
            MarkAllSent(wanted);
        }
        else
        {
            // The whole batch shares one outcome, so a failed summary is retried as a batch.
            foreach (var delivery in wanted)
            {
                Fail(
                    delivery,
                    result.Error,
                    NotificationDeliveryStatus.Deferred,
                    notifications[delivery.NotificationId].Category,
                    user);
            }
        }

        await db.SaveChangesAsync(ct);
        return claimed.Count;
    }

    /// <summary>Drops notifications once they are old enough to be of no further interest.</summary>
    public Task PruneAsync(CancellationToken ct) => NotificationDeliverySql.PruneAsync(db, RetentionDays, ct);

    private async Task<Dictionary<long, Notification>> NotificationsOfAsync(
        List<NotificationDelivery> deliveries, CancellationToken ct)
    {
        var ids = deliveries.Select(d => d.NotificationId).Distinct().ToList();
        return await db.Notifications.Where(n => ids.Contains(n.Id)).ToDictionaryAsync(n => n.Id, ct);
    }

    private async Task RouteOneAsync(Notification row, CancellationToken ct)
    {
        var definition = MessageTemplateCatalog.Find(row.TemplateKey);
        if (definition is null)
        {
            // A delivery that is dead on arrival rather than no delivery at all: an unknown key is
            // a fault an operator has to be able to see, and the health view is over deliveries.
            AddDead(row, $"No message template named '{row.TemplateKey}'.");
            return;
        }

        // Willing to carry this wording at all, which is a question about the message. A recipient
        // who wants none of it answers below and produces no row; nothing being willing to carry
        // it is a different thing entirely — a fault, recorded so an operator sees it.
        var carriers = channels.All.Where(channel => channel.Carries(definition)).ToList();
        if (carriers.Count == 0)
        {
            AddDead(row, $"No channel carries {definition.Channel} messages.");
            return;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == row.RecipientUserId, ct);
        if (user is null)
        {
            // Nothing to route to. The row itself goes when the account does, by cascade.
            return;
        }

        // A user with no stored preference rows is the ordinary case, not "everything off": most
        // accounts never open the settings page, and the documented defaults are what they get.
        var stored = await StoredAsync(row.RecipientUserId, row.Category, ct);

        // Every willing channel is asked, and the answers become rows. Zero rows is an ordinary
        // outcome, not a failure: the notification has happened and is readable in the inbox
        // whether or not any copy of it left the system.
        var answers = carriers
            .Select(channel => (
                channel.Channel,
                Route: channel.Decide(user, Resolve(row.Category, channel.Kind, stored))))
            .ToList();

        var now = clock.GetUtcNow();
        foreach (var planned in NotificationFanOut.Plan(answers))
        {
            var due = planned.Status == NotificationDeliveryStatus.Deferred
                ? NotificationRouting.NextDigest(now, DigestHourUtc)
                : now;

            Add(row, planned.Channel, planned.Status, DueOutsideTheirNight(due, row.Category, user));
        }
    }

    private async Task SendAsync(NotificationDelivery delivery, Notification row, CancellationToken ct)
    {
        var channel = channels.Of(delivery.Channel);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == delivery.RecipientUserId, ct);
        if (user is null || !channel.CanReach(user))
        {
            // The address went away between routing and sending. Retrying cannot help.
            delivery.Status = NotificationDeliveryStatus.Dead;
            delivery.Error = Truncate("The recipient cannot be reached on this channel.");
            return;
        }

        // Preferences are read again here, not only when the delivery was routed. A failed send
        // backs off over hours, and the message that failed carried an opt-out link of its own —
        // so between the attempt that failed and the one that succeeds the recipient may have used
        // it, or cleared the category on their settings page. Sending anyway would answer "stop
        // telling me about this" with one more of exactly that. Dropping the delivery is all that
        // is needed: the notification stays in the inbox, which is where somebody who switched a
        // channel off reads it. The daily summary already re-asks the same question on its own way
        // out; the immediate path asks it here.
        if (await SuppressedNowAsync(user, row, channel, ct))
        {
            db.NotificationDeliveries.Remove(delivery);
            return;
        }

        var result = await channel.SendAsync(user, row.TemplateKey, ValuesFor(row, user), ct);

        if (result.Sent)
        {
            // Terminates the row even when nothing is configured: the message went to the log,
            // which is what a mail-less installation does. Retrying it would never succeed.
            delivery.Status = NotificationDeliveryStatus.Sent;
            delivery.SentAt = clock.GetUtcNow();
            delivery.Error = null;
            return;
        }

        Fail(delivery, result.Error, NotificationDeliveryStatus.Pending, row.Category, user);
    }

    /// <summary>
    /// Whether this channel would refuse this notification for this recipient as things stand now
    /// — the same two reads routing does, asked again on the way out.
    /// </summary>
    private async Task<bool> SuppressedNowAsync(
        SilexGisUser user, Notification row, INotificationChannel channel, CancellationToken ct)
    {
        var stored = await StoredAsync(user.Id, row.Category, ct);
        return channel.Decide(user, Resolve(row.Category, channel.Kind, stored))
            is NotificationRoute.Suppress;
    }

    /// <summary>
    /// One recipient's stored matrix row per channel for one category. Absent channels are absent
    /// on purpose: what a missing row means is the resolver's business, not this query's.
    /// </summary>
    private async Task<Dictionary<NotificationChannelKind, NotificationChannelChoice>> StoredAsync(
        Guid userId, NotificationCategory category, CancellationToken ct) =>
        await db.UserNotificationPreferences
            .Where(p => p.UserId == userId && p.Category == category)
            .ToDictionaryAsync(p => p.Channel, p => p.Choice, ct);

    /// <summary>
    /// What one cell is worth here — the stored row if there is one, put through the rules that
    /// can override it, against the channels this installation actually has.
    /// </summary>
    private NotificationChannelChoice Resolve(
        NotificationCategory category,
        NotificationChannelKind channel,
        IReadOnlyDictionary<NotificationChannelKind, NotificationChannelChoice> stored) =>
        NotificationMatrix.Resolve(
            category,
            channel,
            stored.TryGetValue(channel, out var choice) ? choice : null,
            channels.Installed);

    private NotificationChannelChoice Resolve(
        NotificationCategory category,
        NotificationChannelKind channel,
        IReadOnlyDictionary<NotificationCategory, NotificationChannelChoice> stored) =>
        NotificationMatrix.Resolve(
            category,
            channel,
            stored.TryGetValue(category, out var choice) ? choice : null,
            channels.Installed);

    /// <summary>
    /// When a delivery may really leave: the instant it would otherwise be due, moved past the
    /// hours the recipient asked not to be interrupted in.
    /// </summary>
    /// <remarks>
    /// Only what leaves the installation is moved. The notification itself is in the reader's list
    /// the moment it happens whatever the hour, because it interrupts nobody — which is what makes
    /// holding the outbound copy back honest rather than a lie about what has happened.
    /// <para>
    /// A category that refuses to be held back for a summary refuses this for the same reason and
    /// is asked the same way, from the category vocabulary rather than by name: the messages that
    /// warn somebody about their own account, or that a party is overdue underground, are exactly
    /// the ones worth waking them for.
    /// </para>
    /// </remarks>
    private DateTimeOffset DueOutsideTheirNight(
        DateTimeOffset due, NotificationCategory category, SilexGisUser user) =>
        NotificationCategories.IsAlwaysImmediate(category)
            ? due
            : QuietHours.NextAllowed(due, QuietHoursFrom, QuietHoursTo, user.TimeZone ?? HouseTimeZone);

    private void Add(
        Notification row,
        NotificationChannel channel,
        NotificationDeliveryStatus status,
        DateTimeOffset notBefore) =>
        db.NotificationDeliveries.Add(new NotificationDelivery
        {
            NotificationId = row.Id,
            RecipientUserId = row.RecipientUserId,
            Channel = channel,
            Status = status,
            NotBefore = notBefore,
            CreatedAt = clock.GetUtcNow(),
        });

    /// <summary>
    /// Records a fault that stopped a notification being routed at all.
    /// </summary>
    /// <remarks>
    /// It lands on the channel the installation sends notifications on, because an operator's view
    /// of what is going wrong is a view over deliveries and a fault with no delivery row is a fault
    /// nobody can see. Which channel that is stays arbitrary only while one of them leaves the
    /// system; a second will want the row to name the transport the wording was written for.
    /// </remarks>
    private void AddDead(Notification row, string error) =>
        db.NotificationDeliveries.Add(new NotificationDelivery
        {
            NotificationId = row.Id,
            RecipientUserId = row.RecipientUserId,
            Channel = NotificationChannel.Email,
            Status = NotificationDeliveryStatus.Dead,
            NotBefore = clock.GetUtcNow(),
            CreatedAt = clock.GetUtcNow(),
            Error = Truncate(error),
        });

    /// <summary>The subject line one notification would have carried on its own.</summary>
    private async Task<string> SubjectOfAsync(Notification row, SilexGisUser user, CancellationToken ct)
    {
        if (MessageTemplateCatalog.Find(row.TemplateKey) is null)
        {
            return row.TemplateKey;
        }

        try
        {
            return await dispatcher.RenderSubjectAsync(row.TemplateKey, user.Locale, ValuesFor(row, user), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Could not render a summary line for {TemplateKey}", row.TemplateKey);
            return row.TemplateKey;
        }
    }

    /// <summary>
    /// What the producer recorded, plus the things only this layer knows: how to address the
    /// recipient, where the installation lives, and how they opt out.
    /// </summary>
    private Dictionary<string, string> ValuesFor(Notification row, SilexGisUser user)
    {
        var values = BaseValues(user);

        var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(row.Placeholders, JsonSerializerOptions.Web);
        if (stored is not null)
        {
            foreach (var (key, value) in stored)
            {
                values[key] = value;
            }
        }

        // A producer writes the path to the thing it is reporting, because a feature slice knows
        // its own routes and has no business knowing where the installation is deployed. Only this
        // layer knows that, so this is where a path becomes a link a mail client can open — a
        // message that printed a bare path would print something nobody can click. A value that is
        // already a whole address is left exactly as it is.
        if (values.TryGetValue("url", out var url))
        {
            values["url"] = Absolute(url);
        }

        // A category nobody may switch off carries no opt-out: the settings page refuses it, so a
        // link that could not work would be a lie. The placeholder renders as nothing instead.
        values["unsubscribeUrl"] = NotificationCategories.IsUserConfigurable(row.Category)
            ? UnsubscribeLine(user.Id, row.Category)
            : string.Empty;

        return values;
    }

    private Dictionary<string, string> BaseValues(SilexGisUser user) => new(StringComparer.Ordinal)
    {
        ["displayName"] = RecipientGreeting.For(user),
        ["siteUrl"] = SiteUrl,
        ["unsubscribeUrl"] = string.Empty,
    };

    private string UnsubscribeLine(Guid userId, NotificationCategory category) =>
        TokenLink(unsubscribeTokens.CreateForCategory(userId, category));

    private string DigestUnsubscribeLine(Guid userId) =>
        TokenLink(unsubscribeTokens.CreateForDigest(userId));

    private string TokenLink(string token) =>
        Link($"/unsubscribe?token={Uri.EscapeDataString(token)}");

    /// <summary>Turns one of this installation's own paths into a whole address.</summary>
    private string Link(string path) => SiteUrl + path;

    /// <summary>
    /// A path is resolved against the installation's address; anything else is left alone, so a
    /// value that is already a whole address is not mangled into one that is not.
    /// </summary>
    private string Absolute(string url) => url.StartsWith('/') ? Link(url) : url;

    private void MarkAllSent(List<NotificationDelivery> deliveries)
    {
        var now = clock.GetUtcNow();
        foreach (var delivery in deliveries)
        {
            delivery.Status = NotificationDeliveryStatus.Sent;
            delivery.SentAt = now;
            delivery.Error = null;
        }
    }

    /// <summary>
    /// Records a failed attempt and says when the next one may happen.
    /// </summary>
    /// <remarks>
    /// The retry instant goes through the same rule as the first one. A back-off that steps to
    /// hours crosses into the night from an evening failure without trying to — an unreachable
    /// mail server at nine in the evening is an entirely ordinary condition — and an installation
    /// that promised nothing leaves in the small hours must keep that promise on the second
    /// attempt as much as on the first. Every instant a delivery becomes due is therefore computed
    /// in one place rather than only the one routing computes.
    /// </remarks>
    private void Fail(
        NotificationDelivery delivery,
        string? error,
        NotificationDeliveryStatus retryStatus,
        NotificationCategory category,
        SilexGisUser user)
    {
        delivery.Error = Truncate(error);
        if (delivery.Attempts >= NotificationRouting.MaxAttempts)
        {
            delivery.Status = NotificationDeliveryStatus.Dead;
            return;
        }

        delivery.Status = retryStatus;
        delivery.NotBefore = DueOutsideTheirNight(
            clock.GetUtcNow() + NotificationRouting.RetryDelay(delivery.Attempts), category, user);
    }

    /// <summary>The column is capped, and this runs inside the path that records a failure.</summary>
    private static string? Truncate(string? error) =>
        error is null ? null : error.Length <= 1000 ? error : error[..1000];

    private string SiteUrl =>
        (configuration.GetValue("PublicUrl", "http://localhost:8080") ?? "http://localhost:8080").TrimEnd('/');

    private int DigestHourUtc => configuration.GetValue("Notifications:DigestHourUtc", 7);

    /// <summary>
    /// The hours nothing may interrupt anybody in, as wall-clock times in each recipient's own
    /// zone. Off unless the installation names both ends: an installation that has said nothing
    /// about its members' nights keeps sending as it always has, rather than holding mail back
    /// for hours nobody asked for.
    /// </summary>
    private TimeOnly? QuietHoursFrom => QuietHours.Parse(configuration["Notifications:QuietHoursFrom"]);

    private TimeOnly? QuietHoursTo => QuietHours.Parse(configuration["Notifications:QuietHoursTo"]);

    /// <summary>
    /// Whose night to use for somebody who has never told the server where they are — the
    /// installation's own, which for a club is where nearly all of its members are anyway.
    /// </summary>
    private string HouseTimeZone => configuration.GetValue("Notifications:TimeZone", "UTC") ?? "UTC";

    /// <summary>
    /// One window over the whole table, keyed on the notification's own age and taking read and
    /// unread alike; deliveries go with it by cascade. Deliberately not keyed on a delivery
    /// outcome — an inbox lists what happened, so keeping only what was successfully emailed
    /// would delete precisely the events somebody had switched email off for.
    /// </summary>
    /// <remarks>
    /// A value at or below zero would empty the table on the next pass, so it is refused in
    /// favour of the default: a mistyped setting must not be able to delete an installation's
    /// history, and there is no legitimate reading of "keep for nothing".
    /// </remarks>
    private int RetentionDays
    {
        get
        {
            var days = configuration.GetValue("Notifications:RetentionDays", DefaultRetentionDays);
            return days > 0 ? days : DefaultRetentionDays;
        }
    }
}
