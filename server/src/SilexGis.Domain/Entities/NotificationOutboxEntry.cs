// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// Where one queued notification has got to. Values are part of the schema contract —
/// do not renumber.
/// </summary>
public enum NotificationOutboxStatus : short
{
    /// <summary>Not yet routed, or waiting out a retry backoff.</summary>
    Pending = 0,

    /// <summary>Routed into the recipient's daily digest, waiting for its window.</summary>
    Deferred = 1,

    Sent = 2,

    /// <summary>Routing decided not to send: category off, master switch off, or no address.</summary>
    Suppressed = 3,

    /// <summary>Permanently failed — retries exhausted, or the template is not in the catalogue.</summary>
    Dead = 4,
}

/// <summary>
/// One notification owed to one person.
/// </summary>
/// <remarks>
/// <para>
/// This table is not only a durability device, it is a layer crossing. A feature slice may not
/// read user rows (the architecture test forbids it, and <c>db.Users</c> is an Identity type), so
/// a producer writes only the recipient's <em>id</em> and the facts of what happened. Resolving
/// the address, the language and the preferences — and deciding whether to send at all — is the
/// worker's job, in Infrastructure, where reading users is allowed.
/// </para>
/// <para>
/// Deliberately separate from <c>processing_jobs</c>, whose contract is one heavy job at a time
/// with terminal failures and a startup sweep that re-runs whatever was interrupted. Re-running an
/// interrupted send means sending it twice, and one grant to a large caving group is one row per member —
/// the opposite shape. See the queue contract in the data-model spec.
/// </para>
/// <para>
/// There is no Claimed state: claiming pushes <see cref="NotBefore"/> out by a lease, so a process
/// that dies mid-send leaves the row Pending and simply due again when the lease expires. That is
/// what removes the need for a startup sweep.
/// </para>
/// </remarks>
public class NotificationOutboxEntry
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    public NotificationCategory Category { get; set; }

    /// <summary>A key in the message catalogue. An unknown one is dead on arrival, never sent.</summary>
    public required string TemplateKey { get; set; }

    /// <summary>Placeholder values the producer knew, as a JSON object (jsonb).</summary>
    public string Placeholders { get; set; } = "{}";

    public NotificationOutboxStatus Status { get; set; } = NotificationOutboxStatus.Pending;

    public int Attempts { get; set; }

    /// <summary>
    /// Not due before this. Carries three jobs at once: the retry backoff, the lease taken while a
    /// send is in flight, and the digest window — which is why the digest needs no scheduler.
    /// </summary>
    public DateTimeOffset NotBefore { get; set; } = DateTimeOffset.UtcNow;

    public string? Error { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? SentAt { get; set; }
}
