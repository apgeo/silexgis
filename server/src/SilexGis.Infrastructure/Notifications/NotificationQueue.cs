// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>
/// How a producer says "someone should be told about this".
/// </summary>
/// <remarks>
/// <para>
/// Synchronous and does no I/O on purpose. It only adds a row to the context the caller is
/// already about to save, which buys two things that matter:
/// </para>
/// <para>
/// The notification commits in the same transaction as the change it describes. Nothing in this
/// codebase opens an explicit transaction, so a producer's single <c>SaveChangesAsync</c> is one
/// implicit transaction — a rollback takes the queued notification with it, and there is nothing
/// to compensate. A row therefore exists only if the thing it reports really happened.
/// </para>
/// <para>
/// And the request pays one extra INSERT in a batch it was already sending. It never waits on a
/// mail server, never reads the recipient's preferences, and never reads a user row — which it
/// could not do anyway, since a feature slice may not touch Identity types.
/// </para>
/// </remarks>
public static class NotificationQueue
{
    /// <summary>
    /// Queues one notification for one person. Call it before the <c>SaveChangesAsync</c> that
    /// commits the change being reported.
    /// </summary>
    public static void Enqueue(
        SilexGisDbContext db,
        Guid recipientUserId,
        NotificationCategory category,
        string templateKey,
        IReadOnlyDictionary<string, string> placeholders) =>
        db.NotificationOutbox.Add(new NotificationOutboxEntry
        {
            UserId = recipientUserId,
            Category = category,
            TemplateKey = templateKey,
            Placeholders = JsonSerializer.Serialize(placeholders, JsonSerializerOptions.Web),
            Status = NotificationOutboxStatus.Pending,
            NotBefore = DateTimeOffset.UtcNow,
        });
}
