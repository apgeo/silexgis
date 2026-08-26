// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Access;

/// <summary>
/// Validity of one access entry — the scope/domain/action table, enforced at write
/// time and never silently ignored: a combination the flattened evaluation cannot
/// represent faithfully is rejected, not widened. Structural sanity (anchor columns
/// per scope) is additionally CHECK-enforced in the schema; this rule is the complete
/// semantic authority.
/// </summary>
public static class AccessEntryRules
{
    public const string ScopeInvalidCode = "access_entry.scope_invalid";

    public const string ExceedsOwnRightsCode = "access_entry.exceeds_own_rights";

    public const string DenyFullAdministratorsCode = "access_entry.deny_full_administrators";

    /// <summary>Domains whose rows carry the owner/caving-group/visibility trio — the
    /// only ones where own and caving-group scopes mean anything. Membership is decided
    /// by the columns the row actually has, not by how much of the trio is written today:
    /// a scope that keys on a real column answers honestly even when no row is bound yet,
    /// and it fails closed while the set is empty.</summary>
    public static bool IsTrioDomain(AccessDomain domain) =>
        domain is AccessDomain.Features or AccessDomain.TripLogs or AccessDomain.Geofiles
            or AccessDomain.GeoreferencedMaps or AccessDomain.MapViews
            or AccessDomain.Documents or AccessDomain.Expeditions
            or AccessDomain.Checklists or AccessDomain.Events;

    /// <summary>Domains where an entry may scope to exactly one object. The others have
    /// no per-object identity worth an entry (catalog rows, settings, the audit log).</summary>
    public static bool AllowsObjectScope(AccessDomain domain) =>
        IsTrioDomain(domain)
        || domain is AccessDomain.Files or AccessDomain.CavingGroups
            or AccessDomain.PermissionGroups or AccessDomain.FeatureSets;

    /// <summary>
    /// Returns a problem code when the entry is invalid, null when it is well-formed.
    /// One code (<see cref="ScopeInvalidCode"/>) covers every scope-table violation —
    /// the accompanying message, not the code, says which cell was violated.
    /// </summary>
    public static string? Validate(AccessEntrySnapshot entry)
    {
        if (entry.Actions == AccessAction.None)
        {
            return ScopeInvalidCode;
        }

        // Exactly one home. The schema CHECK enforces this too; validating here keeps
        // the rule visible to unit tests and to write flows before they hit the database.
        var ruleset = entry.PermissionGroupId is not null;
        var direct = entry.SubjectKind is not null && entry.SubjectId is not null;
        if (ruleset == direct)
        {
            return ScopeInvalidCode;
        }

        var narrowed = entry.FeatureKind is not null || entry.FeatureTypeId is not null;

        // Narrowing by kind AND type on one entry would need a conjunction the flat
        // filter arrays cannot carry (they would evaluate it as a disjunction and
        // silently widen) — two entries express the OR, nothing expresses the AND.
        if (entry.FeatureKind is not null && entry.FeatureTypeId is not null)
        {
            return ScopeInvalidCode;
        }

        // Narrowing is valid only where a faithful flat conjunction exists: the feature
        // domain at all/own scopes. "Deny entrances under area X" is a feature set.
        if (narrowed
            && (entry.Domain != AccessDomain.Features
                || entry.ScopeKind is not (AccessScopeKind.All or AccessScopeKind.Own)))
        {
            return ScopeInvalidCode;
        }

        var hasCreate = (entry.Actions & AccessAction.Create) != 0;

        return entry.ScopeKind switch
        {
            AccessScopeKind.All =>
                entry.ScopeFeatureId is not null || entry.ScopeId is not null
                    ? ScopeInvalidCode
                    : null,

            // "Own" needs an owner column, and Create has no owned row to key on.
            AccessScopeKind.Own =>
                entry.ScopeFeatureId is not null || entry.ScopeId is not null
                || !IsTrioDomain(entry.Domain) || hasCreate
                    ? ScopeInvalidCode
                    : null,

            // Caving-group content scope needs the trio's binding column; Create is
            // meaningful here (create content bound to that group).
            AccessScopeKind.CavingGroup =>
                entry.ScopeId is null || entry.ScopeFeatureId is not null
                || !IsTrioDomain(entry.Domain)
                    ? ScopeInvalidCode
                    : null,

            // Subtrees exist only in the containment DAG; Create means "create under
            // this feature" and is valid.
            AccessScopeKind.Subtree =>
                entry.Domain != AccessDomain.Features
                || entry.ScopeFeatureId is null || entry.ScopeId is not null
                    ? ScopeInvalidCode
                    : null,

            // A set is a fixed collection of existing rows — nothing is created "into"
            // one, so Create is rejected.
            AccessScopeKind.FeatureSet =>
                entry.Domain != AccessDomain.Features
                || entry.ScopeId is null || entry.ScopeFeatureId is not null || hasCreate
                    ? ScopeInvalidCode
                    : null,

            // A cabinet is a filing place for documents that already exist, and filing is
            // a write on the document rather than a creation into the cabinet — so Create
            // is rejected here for the same reason it is rejected on a feature set.
            AccessScopeKind.Cabinet =>
                entry.Domain != AccessDomain.Documents
                || entry.ScopeId is null || entry.ScopeFeatureId is not null || hasCreate
                    ? ScopeInvalidCode
                    : null,

            AccessScopeKind.Object =>
                !AllowsObjectScope(entry.Domain) || hasCreate
                || (entry.Domain == AccessDomain.Features
                    ? entry.ScopeFeatureId is null || entry.ScopeId is not null
                    : entry.ScopeId is null || entry.ScopeFeatureId is not null)
                    ? ScopeInvalidCode
                    : null,

            _ => ScopeInvalidCode,
        };
    }

    /// <summary>
    /// The no-amplification rule: a caller who is not a Full Administrator may author
    /// an entry only for (domain, action) combinations they themselves hold an
    /// effective ALLOW for, evaluated against the entry's own anchor context
    /// (<paramref name="anchorFacts"/> — the object's facts at object scope, the root's
    /// at subtree scope, synthetic group/set facts at those scopes, none at all/own).
    /// Returns the action flags the author does NOT hold — <see cref="AccessAction.None"/>
    /// means the write is within bounds. Windows-style WRITE_DAC self-amplification was
    /// deliberately rejected: it turns every delegated "manage sharing" right into
    /// potential full disclosure.
    /// </summary>
    public static AccessAction ExceededActions(
        AccessContext author, AccessEntrySnapshot proposed, AccessTargetFacts? anchorFacts)
    {
        if (author.IsFullAdmin)
        {
            return AccessAction.None;
        }

        var exceeded = AccessAction.None;
        foreach (var action in AccessActions.All)
        {
            if ((proposed.Actions & action) != 0
                && !AccessEvaluator.Decide(author, proposed.Domain, action, anchorFacts).Allowed)
            {
                exceeded |= action;
            }
        }

        return exceeded;
    }
}

/// <summary>The individual action flags, for code that must iterate them.</summary>
public static class AccessActions
{
    public static readonly IReadOnlyList<AccessAction> All =
    [
        AccessAction.Read, AccessAction.Write, AccessAction.Delete, AccessAction.Share,
        AccessAction.ManagePermissions, AccessAction.ViewExactLocation,
        AccessAction.Create, AccessAction.Execute,
    ];

    /// <summary>Every flag ORed — what a domain-wide "every action" seed row carries.</summary>
    public const AccessAction Everything =
        AccessAction.Read | AccessAction.Write | AccessAction.Delete | AccessAction.Share
        | AccessAction.ManagePermissions | AccessAction.ViewExactLocation
        | AccessAction.Create | AccessAction.Execute;
}
