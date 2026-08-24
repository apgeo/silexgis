// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// The kind of thing a notification is about, so the reader's right to see it can be decided
/// again when they read it rather than trusted from when it was queued.
/// </summary>
/// <remarks>
/// <para>
/// Stored as smallint and append-only: values are part of the schema contract — never renumber,
/// never reuse. A notification with no target at all stores null, which is a different statement
/// from any value here: it means the notification is about the account itself or about something
/// with no page to open, not that its target is unknown.
/// </para>
/// <para>
/// Deliberately its own vocabulary rather than the attachment one. Attachments and taggings each
/// speak their own allow-set over a shared enum for the same reason: appending a value there must
/// never quietly widen what a reader can be pointed at from a message.
/// </para>
/// </remarks>
public enum NotificationTargetKind : short
{
    Feature = 0,
    TripLog = 1,
    CavingGroup = 2,
    Geofile = 3,
    GeoreferencedMap = 4,
    MapView = 5,
    Expedition = 6,
    Document = 7,
}

/// <summary>
/// One thing that happened, that one person should be told about.
/// </summary>
/// <remarks>
/// <para>
/// <b>This row's existence is the in-app notification.</b> It is written in the producer's own
/// transaction, needs no address, reaches no network and has no failure mode — which is why
/// in-app is not a delivery. What leaves the system is a delivery; see
/// <see cref="NotificationDelivery"/>.
/// </para>
/// <para>
/// The row is also a layer crossing. A feature slice may not read user rows (the architecture test
/// forbids it, and the user table is an Identity type), so a producer writes only the recipient's
/// <em>id</em> and the facts of what happened. Resolving the address, the language and the
/// preferences — and deciding which channels this goes out on, if any — happens where reading
/// users is allowed.
/// </para>
/// <para>
/// <see cref="TargetKind"/> and <see cref="TargetId"/> are what make the row safe to display later.
/// A producer freezes a rendered name and a path into <see cref="Placeholders"/>, and by the time
/// the recipient reads it they may have lost access to the thing it names; the target reference is
/// the only thing a reader's current access can be re-decided against, so a row written without
/// one can never be re-checked.
/// </para>
/// <para>
/// Deliberately separate from the processing queue, whose contract is one heavy job at a time with
/// terminal failures and a startup sweep that re-runs whatever was interrupted. Re-running an
/// interrupted send means sending it twice, and one grant to a large caving group is one row per
/// member — the opposite shape.
/// </para>
/// </remarks>
public class Notification
{
    public long Id { get; set; }

    public Guid RecipientUserId { get; set; }

    public NotificationCategory Category { get; set; }

    /// <summary>A key in the message catalogue. An unknown one can never be rendered or sent.</summary>
    public required string TemplateKey { get; set; }

    /// <summary>Placeholder values the producer knew, as a JSON object (jsonb).</summary>
    public string Placeholders { get; set; } = "{}";

    /// <summary>What the notification is about; null when it is about nothing openable.</summary>
    public NotificationTargetKind? TargetKind { get; set; }

    /// <summary>Which one; null exactly when <see cref="TargetKind"/> is null.</summary>
    public Guid? TargetId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the recipient opened it in this application. Null while unread, which is what the
    /// unread count and its partial index are over. Reading a message by email never sets it:
    /// there is no honest way to know, and the only mechanisms that would are surveillance.
    /// </summary>
    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>
    /// When outbound channels were decided for this notification. Null means routing has not run
    /// yet; stamped exactly once, so no notification can be fanned out twice.
    /// </summary>
    public DateTimeOffset? RoutedAt { get; set; }
}
