// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;

namespace SilexGis.Domain.Entities;

/// <summary>
/// Append-only audit record. EntityId is a string because audited
/// entities use mixed key types (uuid domain entities, bigint taxonomies); the
/// audit log is a trace, not a foreign-key relationship.
/// </summary>
public class AuditEntry
{
    public long Id { get; set; }

    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;

    public Guid? UserId { get; set; }

    public required string Action { get; set; }

    public string? EntityType { get; set; }

    public string? EntityId { get; set; }

    /// <summary>
    /// CLR type + key of the entity whose timeline this row also belongs to (a child's
    /// parent, e.g. an entrance's cave). Null for root entities and legacy rows. Lets a
    /// parent's history query pick up its children's changes.
    /// </summary>
    public string? RootEntityType { get; set; }

    public string? RootEntityId { get; set; }

    /// <summary>Changed properties as JSON: { "prop": { "old": …, "new": … } } (jsonb).</summary>
    public string? Changes { get; set; }

    public IPAddress? Ip { get; set; }

    /// <summary>
    /// The logical action that wrote this row — one web request, one upload, one import, one
    /// restore, one undo, one job run. Every row the action wrote shares it. Null on rows written
    /// before the history named its source.
    /// </summary>
    public Guid? ChangeSetId { get; set; }

    /// <summary>What kind of action wrote this row. Null on rows written before it was recorded.</summary>
    public ChangeSource? Source { get; set; }

    /// <summary>
    /// The import batch this change belongs to — the upload or the import that made it, or the one
    /// an undo took back. What lets the rows a batch wrote be found again and reversed.
    /// </summary>
    public Guid? ImportBatchId { get; set; }
}

/// <summary>Well-known audit action names (extensible — string column by design).</summary>
public static class AuditActions
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Deleted = "deleted";
    public const string PermissionChanged = "permission_changed";

    /// <summary>
    /// An operator put a dead outbound message back in the queue by hand. Its own name because it
    /// is neither a change to a row somebody edited nor an automatic transition: it is a person
    /// deciding that a message somebody else was meant to receive should be attempted again.
    /// </summary>
    public const string NotificationRetried = "notification_retried";
    public const string Login = "login";

    /// <summary>
    /// An administrator switched off every second factor of somebody else's account. Its own name
    /// because account rows are deliberately not recorded field by field, and this is the act on
    /// one most worth reconstructing: whoever did it decided that somebody could, for a while, sign
    /// in with a password alone.
    /// </summary>
    public const string TwoFactorReset = "two_factor_reset";

    /// <summary>
    /// Data left the installation as a file. Its own name because it is not a change to
    /// anything: nothing in the database is different afterwards, and yet it is the act most
    /// worth being able to reconstruct later — a file outlives every permission check that
    /// produced it, so "who took what, and what did they choose to do with the positions they
    /// were not allowed to disclose" has to be answerable from the record rather than from
    /// whoever still remembers.
    /// </summary>
    public const string Exported = "exported";
}
