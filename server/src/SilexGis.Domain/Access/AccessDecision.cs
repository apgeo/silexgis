// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Access;

/// <summary>The specificity level a deciding entry matched at — smaller wins.</summary>
public enum AccessLevel
{
    /// <summary>Entries scoped to exactly the evaluated row.</summary>
    Object = 1,

    /// <summary>Subtree entries whose root is in the row's ancestor array; feature-set
    /// entries whose set contains the row.</summary>
    Collection = 2,

    /// <summary>all / own / caving-group scope, including the kind- and type-narrowed
    /// conjunctions.</summary>
    Global = 3,
}

/// <summary>What decided the outcome — the explainer's raw material.</summary>
public enum AccessDecisionSource
{
    /// <summary>No signed-in caller: denied before any rule is consulted. Public read
    /// stays an endpoint-level switch, never an evaluator outcome.</summary>
    Anonymous = 0,

    /// <summary>Membership in the protected Full Administrators group.</summary>
    FullAdministrators = 1,

    /// <summary>Explicit entries at <see cref="AccessDecision.Level"/> decided.</summary>
    Entries = 2,

    /// <summary>Built-in: the context row (for Create, the prospective parent) is owned
    /// by the caller. Only reached when no explicit entry matched anywhere.</summary>
    Ownership = 3,

    /// <summary>Built-in, Read only: the row's effective visibility — its own row or any
    /// ancestor — admits the caller.</summary>
    Visibility = 4,

    /// <summary>Nothing matched and no built-in applied.</summary>
    DefaultDeny = 5,

    /// <summary>Built-in: the caller holds this action on at least one object the row's
    /// content is attached to. Only reached when no explicit entry matched anywhere and
    /// neither ownership nor the read audience already admitted — so a deny is never
    /// widened by an attachment, and a document nobody attached to anything is reachable
    /// through its own rules alone.</summary>
    Attachment = 6,
}

/// <summary>
/// The outcome of one access check, carrying enough to explain itself: which level
/// decided and by which entries. The API layer resolves anchor names and redacts the
/// ones the caller cannot read — the decision itself never does string work.
/// </summary>
public sealed record AccessDecision(
    bool Allowed,
    AccessDecisionSource Source,
    AccessLevel? Level,
    IReadOnlyList<AccessEntrySnapshot> DecidingEntries)
{
    public static readonly AccessDecision AnonymousDeny =
        new(false, AccessDecisionSource.Anonymous, null, []);

    public static readonly AccessDecision FullAdministratorAllow =
        new(true, AccessDecisionSource.FullAdministrators, null, []);
}
