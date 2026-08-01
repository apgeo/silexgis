// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// A named ruleset with a trustee list — the SQL-role / AD-security-group concept.
/// First-class and freestanding: it binds to rules (access entries), never to content
/// rows. Roles like "Editors" are nothing but seeded, editable permission groups.
/// Full Administrators is special only through <see cref="IsProtected"/> and the
/// evaluator's membership short-circuit — it carries no entries at all.
/// </summary>
public class PermissionGroup : ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public required string Slug { get; set; }

    public string? Description { get; set; }

    /// <summary>A protected group cannot be deleted or renamed (lockout guard).</summary>
    public bool IsProtected { get; set; }

    /// <summary>Created by seeding rather than an operator. Informational — seeded
    /// groups stay ordinary, editable data.</summary>
    public bool IsSeeded { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}

/// <summary>
/// A trustee of a permission group: a user, or a whole caving group (whose
/// account-holding members inherit the group's rules through their caver row).
/// Membership moves authorization, so rows are audited like grant edits.
/// There is deliberately no caver member kind (permissions never attach to cavers)
/// and no permission-group member kind (no nesting — the closure stays one join deep,
/// which is what keeps every filter flat).
/// </summary>
public class PermissionGroupMember : ITimestamped, IAuditable
{
    public long Id { get; set; }

    public Guid PermissionGroupId { get; set; }

    public Access.AccessSubjectKind MemberKind { get; set; }

    /// <summary>User id or caving-group id per <see cref="MemberKind"/>. No FK — the
    /// integrity verifier flags dangling ids, and the live-admin guard keeps the
    /// Full Administrators group reachable regardless.</summary>
    public Guid MemberId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
