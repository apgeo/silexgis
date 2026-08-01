// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;

namespace SilexGis.Domain.Entities;

/// <summary>
/// One access rule. Lives in exactly one of two homes (CHECK-enforced XOR):
/// a permission group (ruleset entry, SQL-GRANT style) or directly on a subject
/// (per-object DACL style — what the object Permissions tab writes). One storage,
/// one evaluator, one precedence walk, one explainer — a second grant mechanism
/// would need its own precedence story against deny, which is why object_acl died.
///
/// Scope anchors are deliberately ON DELETE RESTRICT (feature, feature type) or
/// guarded in delete flows (the FK-less <see cref="ScopeId"/>): deleting what a deny
/// hangs on must be an explicit, audited act — a cascade would let a taxonomy editor
/// silently cancel denies they cannot edit.
/// </summary>
public class AccessEntry : ITimestamped, IAuditable
{
    public long Id { get; set; }

    /// <summary>Home A: the ruleset this entry belongs to.</summary>
    public Guid? PermissionGroupId { get; set; }

    /// <summary>Home B: a direct entry's subject kind (user / caving group).</summary>
    public AccessSubjectKind? SubjectKind { get; set; }

    public Guid? SubjectId { get; set; }

    public AccessEffect Effect { get; set; }

    public AccessDomain Domain { get; set; }

    /// <summary>Bit flags — see <see cref="AccessAction"/>.</summary>
    public AccessAction Actions { get; set; }

    public AccessScopeKind ScopeKind { get; set; }

    /// <summary>Subtree/object anchor in the feature domain — a real FK (RESTRICT).</summary>
    public Guid? ScopeFeatureId { get; set; }

    /// <summary>Caving group, feature set, or non-feature object anchor. No FK
    /// (kind-dependent); delete flows refuse while entries reference the anchor, and
    /// the integrity verifier flags any dangling id it finds.</summary>
    public Guid? ScopeId { get; set; }

    /// <summary>Kind narrowing — feature domain, scopes all/own only: those are the only
    /// scopes where the flattened filter can carry the conjunction faithfully.</summary>
    public FeatureKind? FeatureKind { get; set; }

    /// <summary>Data-level type narrowing, same validity rules as <see cref="FeatureKind"/>.</summary>
    public long? FeatureTypeId { get; set; }

    public Guid? GrantedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    /// <summary>The immutable shape the evaluator and filters consume.</summary>
    public AccessEntrySnapshot ToSnapshot() => new(
        Id, PermissionGroupId, SubjectKind, SubjectId, Effect, Domain, Actions,
        ScopeKind, ScopeFeatureId, ScopeId, FeatureKind, FeatureTypeId);
}
