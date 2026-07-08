// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>ACL grant subject: a single user or a whole team. Stored as smallint.</summary>
public enum AclSubjectKind : short
{
    User = 0,
    Team = 1,
}

/// <summary>
/// Explicit per-object permission grant (the third authorization layer after
/// roles/ownership and team membership). Unique per (entity, subject); flags are OR-ed
/// when multiple rows apply to a caller (e.g. a user grant plus a team grant).
/// Grants are audited — ViewExactLocation grants especially matter.
/// </summary>
public class ObjectAcl : ITimestamped, IAuditable
{
    public long Id { get; set; }

    public AttachedEntityType EntityType { get; set; }

    public Guid EntityId { get; set; }

    public AclSubjectKind SubjectKind { get; set; }

    public Guid SubjectId { get; set; }

    public ObjectPermission Permissions { get; set; }

    public Guid? GrantedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
