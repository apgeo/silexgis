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
/// <para>
/// The row it writes is itself the recipient's in-app notification. Which channels it additionally
/// goes out on — if any — is decided later, by the pass that is allowed to read the recipient.
/// </para>
/// </remarks>
public static class NotificationQueue
{
    /// <summary>
    /// Queues one notification for one person. Call it before the <c>SaveChangesAsync</c> that
    /// commits the change being reported.
    /// </summary>
    /// <param name="targetKind">
    /// What the notification is about, so the reader's right to see it can be decided again at the
    /// moment they read it rather than trusted from the name and path frozen into
    /// <paramref name="placeholders"/> when it was queued. Optional in the signature only so that
    /// a producer can be written without one; a message that names an object and passes no target
    /// can never be re-checked, and which messages are excused from passing one is a list with a
    /// reason against each entry, pinned by a test.
    /// </param>
    /// <param name="targetId">Which one. Pass it with <paramref name="targetKind"/> or not at all.</param>
    /// <param name="segmentsPerCopy">
    /// What one outbound copy of this is expected to cost on a transport that bills by the piece,
    /// for a producer that has already weighed the wording it is queuing. Left out by every
    /// producer whose message cannot travel that way, and by any that has not weighed it: the
    /// guard on the day's spending then falls back to its own floor rather than reading the
    /// message as free.
    /// </param>
    public static void Enqueue(
        SilexGisDbContext db,
        Guid recipientUserId,
        NotificationCategory category,
        string templateKey,
        IReadOnlyDictionary<string, string> placeholders,
        NotificationTargetKind? targetKind = null,
        Guid? targetId = null,
        int segmentsPerCopy = 0) =>
        db.Notifications.Add(new Notification
        {
            RecipientUserId = recipientUserId,
            Category = category,
            TemplateKey = templateKey,
            Placeholders = JsonSerializer.Serialize(placeholders, JsonSerializerOptions.Web),
            TargetKind = targetKind,
            TargetId = targetId,
            SegmentsPerCopy = segmentsPerCopy,
        });
}
