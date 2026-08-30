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
public class AccessEntry : ITimestamped, IAuditable, IAuditChild
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

    /// <summary>
    /// The camp whose sharing wrote this row, when a camp's sharing reached the trips in it.
    /// Null for every rule somebody authored directly.
    /// </summary>
    /// <remarks>
    /// A marker rather than a later inference. Withdrawing or re-applying a camp's sharing has
    /// to find exactly the rows that camp wrote and nothing else, and matching on subject,
    /// domain, actions and reach would silently swallow an identical rule somebody had authored
    /// by hand on the trip's own permissions tab — the rule they wrote would disappear when a
    /// camp they have nothing to do with stopped sharing.
    /// </remarks>
    public Guid? GrantedViaExpeditionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    /// <summary>
    /// A rule a camp's sharing wrote belongs on that camp's trail; one somebody authored
    /// belongs to nobody but itself, and answers null.
    /// </summary>
    /// <remarks>
    /// Sharing one camp writes a rule onto every trip it gathered — a fortnight camp can be
    /// forty rows created in one act, none of which anybody sat down and authored. Without a
    /// root they are forty unattributed rows in the trail with nothing tying them to the act
    /// that made them or to each other; with it, they are one act somebody can find, read and
    /// account for.
    /// </remarks>
    public string? RootEntityType => GrantedViaExpeditionId is null ? null : nameof(Expedition);

    public string? RootEntityId => GrantedViaExpeditionId?.ToString();

    /// <summary>The immutable shape the evaluator and filters consume.</summary>
    public AccessEntrySnapshot ToSnapshot() => new(
        Id, PermissionGroupId, SubjectKind, SubjectId, Effect, Domain, Actions,
        ScopeKind, ScopeFeatureId, ScopeId, FeatureKind, FeatureTypeId);
}
