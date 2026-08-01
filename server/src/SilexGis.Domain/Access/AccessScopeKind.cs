// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Access;

/// <summary>
/// What slice of a domain an access entry covers. Stored as smallint and append-only —
/// a future scope (e.g. a parallel-hierarchy subtree) gets a new value, existing values
/// never change meaning. Valid (scope, domain, action) combinations are enforced by
/// <see cref="AccessEntryRules"/> — invalid ones are rejected at write time, never
/// silently ignored.
/// </summary>
public enum AccessScopeKind : short
{
    /// <summary>The whole domain.</summary>
    All = 0,

    /// <summary>Objects the evaluated user owns. Trio-carrying domains only.</summary>
    Own = 1,

    /// <summary>Objects bound to one caving group (scope_id). Trio-carrying domains only.</summary>
    CavingGroup = 2,

    /// <summary>A feature (scope_feature_id) plus every containment descendant — matched
    /// flat via the rows' ancestor arrays. Feature domain only.</summary>
    Subtree = 3,

    /// <summary>Members of one named feature set (scope_id). Feature domain only.</summary>
    FeatureSet = 4,

    /// <summary>Exactly one object: scope_feature_id in the feature domain, scope_id
    /// elsewhere.</summary>
    Object = 5,
}

/// <summary>Effect of an access entry. Stored as smallint — do not renumber.</summary>
public enum AccessEffect : short
{
    Allow = 0,
    Deny = 1,
}

/// <summary>
/// Subject of a direct access entry or member of a permission group. There is
/// deliberately no caver value: permissions attach to users, never to cavers — an
/// account-less person can neither hold nor receive a right.
/// </summary>
public enum AccessSubjectKind : short
{
    User = 0,
    CavingGroup = 1,
}
