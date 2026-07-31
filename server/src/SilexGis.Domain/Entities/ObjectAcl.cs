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
/// roles/ownership and team membership). The target is EITHER a feature (real FK) OR a
/// non-feature entity via the polymorphic pair — exactly one shape set (CHECK-enforced).
/// Unique per (target, subject); flags are OR-ed when multiple rows apply to a caller
/// (e.g. a user grant plus a team grant). Grants are audited — ViewExactLocation grants
/// especially matter.
///
/// Grants are deliberately NON-CASCADING over the feature hierarchy: a grant applies to
/// its target row only. The cascade semantics the owner decided (grants and visibility
/// cascading down containment, with overrides and deny) belong to the planned
/// ruleset-based permission system, which will consume the features' ancestor arrays;
/// nothing here anticipates it.
/// </summary>
public class ObjectAcl : ITimestamped, IAuditable
{
    public long Id { get; set; }

    /// <summary>Feature target (XOR with the polymorphic pair).</summary>
    public Guid? FeatureId { get; set; }

    public AttachedEntityType? EntityType { get; set; }

    public Guid? EntityId { get; set; }

    public AclSubjectKind SubjectKind { get; set; }

    public Guid SubjectId { get; set; }

    public ObjectPermission Permissions { get; set; }

    public Guid? GrantedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
