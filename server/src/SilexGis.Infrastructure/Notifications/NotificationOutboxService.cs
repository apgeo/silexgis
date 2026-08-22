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
/// Turns queued notifications into sent messages.
/// </summary>
/// <remarks>
/// <para>
/// The only place that reads a recipient's address, language and preferences — producers write an
/// id and the facts, and everything about the person is resolved here. That is what lets feature
/// slices queue notifications at all without touching Identity types.
/// </para>
/// <para>
/// Also the single terminator of every row: routing decides send, defer or suppress exactly once,
/// so a notification can never go out immediately <em>and</em> in a digest.
/// </para>
/// <para>
/// A plain service rather than logic inside the worker, so tests can drive a drain directly
/// instead of waiting on a poll.
/// </para>
/// </remarks>
public sealed class NotificationOutboxService(
    SilexGisDbContext db,
    IMessageDispatcher dispatcher,
    IUnsubscribeTokens unsubscribeTokens,
    IConfiguration configuration,
    ILogger<NotificationOutboxService> logger)
{
    /// <summary>Rows leased per drain pass. Small enough that a crash re-sends few, if any.</summary>
    private const int BatchSize = 25;

    /// <summary>Lines listed in a digest before it says "and more".</summary>
    private const int MaxDigestItems = 50;

    private const int RetentionDays = 30;

    /// <summary>Sends every immediately-due notification. Returns how many rows it settled.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var ids = await NotificationOutboxSql.ClaimDueAsync(db, BatchSize, ct);
        if (ids.Count == 0)
        {
            return 0;
        }

        var rows = await db.NotificationOutbox.Where(r => ids.Contains(r.Id)).ToListAsync(ct);
        foreach (var row in rows)
        {
            await SettleAsync(row, ct);
        }

        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    /// <summary>
    /// Sends one recipient's daily summary, if anyone's is due. Returns how many rows it settled,
    /// so a caller can drain by looping until it returns zero.
    /// </summary>
    public async Task<int> RunDigestAsync(CancellationToken ct)
    {
        var ids = await NotificationOutboxSql.ClaimDueDigestAsync(db, ct);
        if (ids.Count == 0)
        {
            return 0;
        }

        var rows = await db.NotificationOutbox.Where(r => ids.Contains(r.Id)).OrderBy(r => r.Id).ToListAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == rows[0].UserId, ct);

        // Preferences are read again here, not only when the row was deferred. A summary is
        // written hours after the events it collects, and the link it carries is itself an
        // invitation to switch it off — so between the deferral and the send the recipient may
        // have done exactly that. Sending anyway would answer "the summary has stopped" with one
        // more summary.
        if (user?.Email is null || !user.NotifyEmailEnabled)
        {
            MarkAll(rows, NotificationOutboxStatus.Suppressed);
            await db.SaveChangesAsync(ct);
            return rows.Count;
        }

        // The same second thought applied per category: a summary claimed for today may contain
        // lines about something the recipient has since switched off, and those must not arrive.
        var claimed = rows;
        var stored = await db.UserNotificationPreferences
            .Where(p => p.UserId == user.Id)
            .ToDictionaryAsync(p => p.Category, p => p.Enabled, ct);

        rows = claimed
            .Where(r => stored.TryGetValue(r.Category, out var enabled)
                ? enabled
                : NotificationCategories.DefaultEnabled(r.Category))
            .ToList();

        var dropped = claimed.Where(r => !rows.Contains(r)).ToList();
        if (dropped.Count > 0)
        {
            MarkAll(dropped, NotificationOutboxStatus.Suppressed);
        }

        if (rows.Count == 0)
        {
            await db.SaveChangesAsync(ct);
            return claimed.Count;
        }

        // Each line is that event's own subject: already a one-line summary, already translated,
        // already editable by the operator. Single newlines — the renderer collapses blank ones.
        var lines = new List<string>();
        foreach (var row in rows.Take(MaxDigestItems))
        {
            lines.Add("- " + await SubjectOfAsync(row, user, ct));
        }

        var values = BaseValues(user);
        values["itemCount"] = rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        values["items"] = string.Join("\n", lines);
        // A summary has no category of its own, so its opt-out link cannot name one: it collects
        // whatever the recipient still hears about, and a token for one of those would switch off
        // something they never said anything about.
        values["unsubscribeUrl"] = DigestUnsubscribeLine(user.Id);

        var result = await dispatcher.SendAsync(
            MessageTemplateCatalog.NotifyDigest, user.Email, user.Locale, values, ct);

        if (result.Sent)
        {
            MarkAll(rows, NotificationOutboxStatus.Sent);
        }
        else
        {
            // The whole batch shares one outcome, so a failed digest is retried as a batch.
            foreach (var row in rows)
            {
                Fail(row, result.Error, NotificationOutboxStatus.Deferred);
            }
        }

        await db.SaveChangesAsync(ct);
        return claimed.Count;
    }

    /// <summary>Drops settled rows once they are old enough to be of no further interest.</summary>
    public Task PruneAsync(CancellationToken ct) => NotificationOutboxSql.PruneAsync(db, RetentionDays, ct);

    private async Task SettleAsync(NotificationOutboxEntry row, CancellationToken ct)
    {
        var definition = MessageTemplateCatalog.Find(row.TemplateKey);
        if (definition is null)
        {
            // Never handed to the dispatcher: an unknown key is the one thing it throws on, and a
            // poison row must not take down the rest of the batch.
            Kill(row, $"No message template named '{row.TemplateKey}'.");
            return;
        }

        if (definition.Channel != MessageChannel.Email)
        {
            // The outbox is email-only. The guard is the documentation.
            Kill(row, "Notifications are delivered by email only.");
            return;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == row.UserId, ct);
        if (user is null)
        {
            row.Status = NotificationOutboxStatus.Suppressed;
            return;
        }

        // A user with no stored preference rows is the ordinary case, not "everything off".
        var stored = await db.UserNotificationPreferences
            .Where(p => p.UserId == row.UserId && p.Category == row.Category)
            .Select(p => (bool?)p.Enabled)
            .FirstOrDefaultAsync(ct);

        var route = NotificationRouting.Decide(
            row.Category,
            stored ?? NotificationCategories.DefaultEnabled(row.Category),
            user.NotifyEmailEnabled,
            user.NotifyDigest,
            !string.IsNullOrWhiteSpace(user.Email));

        switch (route)
        {
            case NotificationRoute.Suppress:
                row.Status = NotificationOutboxStatus.Suppressed;
                return;

            case NotificationRoute.Defer:
                row.Status = NotificationOutboxStatus.Deferred;
                row.NotBefore = NotificationRouting.NextDigest(DateTimeOffset.UtcNow, DigestHourUtc);
                return;
        }

        var result = await dispatcher.SendAsync(
            row.TemplateKey, user.Email!, user.Locale, ValuesFor(row, user), ct);

        if (result.Sent)
        {
            // Terminates the row even when nothing is configured: the message went to the log,
            // which is what a mail-less installation does. Retrying it would never succeed.
            row.Status = NotificationOutboxStatus.Sent;
            row.SentAt = DateTimeOffset.UtcNow;
            row.Error = null;
            return;
        }

        Fail(row, result.Error, NotificationOutboxStatus.Pending);
    }

    /// <summary>The subject line one queued event would have carried on its own.</summary>
    private async Task<string> SubjectOfAsync(NotificationOutboxEntry row, SilexGisUser user, CancellationToken ct)
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
    private Dictionary<string, string> ValuesFor(NotificationOutboxEntry row, SilexGisUser user)
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

        // Security alerts carry no opt-out: the settings page refuses to switch them off, so a
        // link that could not work would be a lie. The placeholder renders as nothing instead.
        values["unsubscribeUrl"] = NotificationCategories.IsUserConfigurable(row.Category)
            ? UnsubscribeLine(user.Id, row.Category)
            : string.Empty;

        return values;
    }

    private Dictionary<string, string> BaseValues(SilexGisUser user) => new(StringComparer.Ordinal)
    {
        ["displayName"] = Greeting(user),
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

    /// <summary>Never the email address: it is the one thing the profile rules may be hiding.</summary>
    private static string Greeting(SilexGisUser user) =>
        !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName!
        : !string.IsNullOrWhiteSpace(user.FirstName) ? user.FirstName!
        : "there";

    private static void MarkAll(List<NotificationOutboxEntry> rows, NotificationOutboxStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var row in rows)
        {
            row.Status = status;
            row.SentAt = status == NotificationOutboxStatus.Sent ? now : row.SentAt;
            row.Error = null;
        }
    }

    private static void Fail(NotificationOutboxEntry row, string? error, NotificationOutboxStatus retryStatus)
    {
        row.Error = Truncate(error);
        if (row.Attempts >= NotificationRouting.MaxAttempts)
        {
            row.Status = NotificationOutboxStatus.Dead;
            return;
        }

        row.Status = retryStatus;
        row.NotBefore = DateTimeOffset.UtcNow + NotificationRouting.RetryDelay(row.Attempts);
    }

    private static void Kill(NotificationOutboxEntry row, string error)
    {
        row.Status = NotificationOutboxStatus.Dead;
        row.Error = Truncate(error);
    }

    /// <summary>The column is capped, and this runs inside the path that records a failure.</summary>
    private static string? Truncate(string? error) =>
        error is null ? null : error.Length <= 1000 ? error : error[..1000];

    private string SiteUrl =>
        (configuration.GetValue("PublicUrl", "http://localhost:8080") ?? "http://localhost:8080").TrimEnd('/');

    private int DigestHourUtc => configuration.GetValue("Notifications:DigestHourUtc", 7);
}
