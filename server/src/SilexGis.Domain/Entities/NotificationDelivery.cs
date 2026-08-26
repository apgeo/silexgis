// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// A way a notification leaves this installation.
/// </summary>
/// <remarks>
/// <b>A channel is something that leaves the system.</b> Email and text message, and later
/// WhatsApp and push, all hand a message to somebody else's infrastructure and can fail there.
/// In-app does not: the notification row's own existence is its in-app presence, written in the
/// producer's transaction with no address and no failure mode, so in-app is deliberately absent
/// from this enum. A delivery row whose status could only ever be one value would teach nothing
/// and double the table — and keeping the enum to what can fail is what makes an operator's
/// health view exactly a view over deliveries.
/// <para>
/// Stored as smallint and append-only: values are part of the schema contract.
/// </para>
/// </remarks>
public enum NotificationChannel : short
{
    Email = 0,

    /// <summary>
    /// Text message. The one value here that charges the installation per message, which is why a
    /// row naming it is also a unit of spending: a day's cost is counted over the rows created
    /// that day, so a row that exists has been committed to whether or not the gateway has taken
    /// it yet.
    /// </summary>
    Sms = 1,
}

/// <summary>
/// Where one attempt to get a notification out of the system has got to. Values are part of the
/// schema contract — do not renumber.
/// </summary>
/// <remarks>
/// Every value here is a transport outcome. There is deliberately no "suppressed": whether a
/// recipient wants a channel is a routing question, answered before this row exists, and the
/// answer is simply that no row was created. The notification is in the recipient's inbox either
/// way — which is the point, because the events somebody switched email off for are exactly the
/// ones an inbox exists to show.
/// </remarks>
public enum NotificationDeliveryStatus : short
{
    /// <summary>Due to be attempted, or waiting out a retry backoff.</summary>
    Pending = 0,

    /// <summary>Held for the recipient's daily summary, waiting for its window.</summary>
    Deferred = 1,

    Sent = 2,

    /// <summary>Permanently failed — retries exhausted, or the message cannot be composed.</summary>
    Dead = 3,
}

/// <summary>
/// One notification going out on one channel.
/// </summary>
/// <remarks>
/// <para>
/// There is no Claimed state: claiming pushes <see cref="NotBefore"/> out by a lease, so a process
/// that dies mid-send leaves the row as it was and simply due again once the lease expires. That
/// is what removes the need for a startup sweep — a sweep would re-send every message that was in
/// flight during a restart.
/// </para>
/// <para>
/// <see cref="RecipientUserId"/> is a copy of the parent's, kept here so the daily summary can
/// gather one person's whole batch in a single statement without joining back to the parent. It is
/// written once, when routing creates the row, and is never independently updated.
/// </para>
/// </remarks>
public class NotificationDelivery
{
    public long Id { get; set; }

    public long NotificationId { get; set; }

    /// <summary>Denormalised from the parent so the digest claim needs no join.</summary>
    public Guid RecipientUserId { get; set; }

    public NotificationChannel Channel { get; set; }

    public NotificationDeliveryStatus Status { get; set; } = NotificationDeliveryStatus.Pending;

    public int Attempts { get; set; }

    /// <summary>
    /// Not due before this. Carries three jobs at once: the retry backoff, the lease taken while a
    /// send is in flight, and the digest window — which is why the digest needs no scheduler.
    /// </summary>
    public DateTimeOffset NotBefore { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }
}
