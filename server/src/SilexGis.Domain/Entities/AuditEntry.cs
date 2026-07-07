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

    /// <summary>Changed properties as JSON: { "prop": { "old": …, "new": … } } (jsonb).</summary>
    public string? Changes { get; set; }

    public IPAddress? Ip { get; set; }
}

/// <summary>Well-known audit action names (extensible — string column by design).</summary>
public static class AuditActions
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Deleted = "deleted";
    public const string PermissionChanged = "permission_changed";
    public const string Login = "login";
}
