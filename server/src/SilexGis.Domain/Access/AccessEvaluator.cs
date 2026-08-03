// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Access;

/// <summary>
/// The effective-access rule, in its authoritative pure form. Three specificity levels
/// (object → collection → global), deny beats allow within a level, the first level
/// containing any matching entry decides and the walk stops; built-ins — ownership, the
/// read-time visibility cascade over the ancestor chain, and reach through an attached
/// object — apply only when no explicit entry matched anywhere; beneath everything, deny;
/// above everything, Full Administrators membership. Membership path never affects
/// weight: whether a rule reaches the caller directly, via a caving group or via a
/// permission group changes nothing — only scope specificity and effect matter.
///
/// This rule exists in three synchronized forms that may never diverge: this evaluator,
/// the EF expression twin (<c>AccessQueryExtensions.VisibleTo</c>) and the Dapper
/// fragment twin (<c>AccessSql</c>) — parity tests pin them against a live database.
/// Change them together or not at all. Everything here is flat: subtree matching is an
/// array membership test on the row's ancestor ids, set matching a set-id lookup —
/// nothing recurses.
/// </summary>
public static class AccessEvaluator
{
    /// <summary>
    /// Decides (domain, action) for the caller against a target context — a row's facts,
    /// the Create target facts, or null for a genuinely target-less domain-wide check
    /// (which only unnarrowed Global entries can match). <paramref name="action"/> must
    /// be a single flag.
    /// </summary>
    public static AccessDecision Decide(
        AccessContext? ctx, AccessDomain domain, AccessAction action, AccessTargetFacts? target)
    {
        if (ctx is null)
        {
            return AccessDecision.AnonymousDeny;
        }

        if (ctx.IsFullAdmin)
        {
            return AccessDecision.FullAdministratorAllow;
        }

        List<AccessEntrySnapshot>? objectLevel = null, collectionLevel = null, globalLevel = null;
        foreach (var entry in ctx.Entries)
        {
            if (entry.Domain != domain || (entry.Actions & action) == 0)
            {
                continue;
            }

            switch (LevelOf(entry, ctx, target))
            {
                case AccessLevel.Object:
                    (objectLevel ??= []).Add(entry);
                    break;
                case AccessLevel.Collection:
                    (collectionLevel ??= []).Add(entry);
                    break;
                case AccessLevel.Global:
                    (globalLevel ??= []).Add(entry);
                    break;
                default:
                    break;
            }
        }

        if (DecideLevel(objectLevel, AccessLevel.Object) is { } atObject)
        {
            return atObject;
        }

        if (DecideLevel(collectionLevel, AccessLevel.Collection) is { } atCollection)
        {
            return atCollection;
        }

        if (DecideLevel(globalLevel, AccessLevel.Global) is { } atGlobal)
        {
            return atGlobal;
        }

        // Built-ins — only reachable when NO explicit entry matched: an owner can be
        // locked out of their own object by a deny (Full Administrators are the
        // recovery path), and visibility never overrides a deny.
        if (target?.OwnerUserId is { } owner && owner == ctx.UserId)
        {
            return new AccessDecision(true, AccessDecisionSource.Ownership, null, []);
        }

        if (action == AccessAction.Read && target is not null && VisibilityAdmits(ctx, target))
        {
            return new AccessDecision(true, AccessDecisionSource.Visibility, null, []);
        }

        // Attachment reach, last of the built-ins and the weakest reason of the three: a
        // document is reachable through any one object it is attached to that the caller
        // already holds this action on. It sits here, and only here, so that rules written
        // about the document itself settle the question first — a deny an attachment could
        // talk past would not be a deny — while a document attached to nothing stays
        // reachable through its own rules, its owner and its audience alone.
        if (target?.ReachedByAttachment == true)
        {
            return new AccessDecision(true, AccessDecisionSource.Attachment, null, []);
        }

        return new AccessDecision(false, AccessDecisionSource.DefaultDeny, null, []);
    }

    /// <summary>
    /// Whether attachment reach could still change an answer already decided without it.
    /// It can only when nothing decided at all: an entry — or full administration — is the
    /// whole answer in both directions, and the other built-ins have already admitted when
    /// they apply. Resolving reach costs a walk over every attached object in every world,
    /// so callers ask this first and pay only when the question is genuinely still open.
    /// </summary>
    public static bool AttachmentReachCouldDecide(AccessDecision decision) =>
        decision is { Allowed: false, Source: AccessDecisionSource.DefaultDeny };

    /// <summary>
    /// The read-time visibility cascade (the D1d rule): a row is visibility-readable
    /// when its own row or any ancestor admits the caller — authenticated/public admit
    /// any signed-in caller (anonymous never reaches this method at all); caving-group
    /// visibility admits callers sharing THAT link's owning caving group.
    /// </summary>
    public static bool VisibilityAdmits(AccessContext ctx, AccessTargetFacts target)
    {
        foreach (var fact in target.VisibilityChain)
        {
            if (fact.Visibility >= Visibility.Authenticated)
            {
                return true;
            }

            if (fact.Visibility == Visibility.CavingGroup
                && fact.CavingGroupId is { } cavingGroupId
                && ctx.CavingGroupIds.Contains(cavingGroupId))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The level an entry matches the context at, or null when it does not match.
    /// Kind/type-narrowed entries speak about rows, so they never match a target-less
    /// check; an entry narrows by kind OR type, never both (enforced at write time),
    /// and the narrowing is a conjunction with its scope — own∧kind never widens to
    /// all∧kind.
    /// </summary>
    private static AccessLevel? LevelOf(
        AccessEntrySnapshot entry, AccessContext ctx, AccessTargetFacts? target) => entry.ScopeKind switch
    {
        AccessScopeKind.Object =>
            target?.ObjectId is { } objectId
            && objectId == (entry.Domain == AccessDomain.Features ? entry.ScopeFeatureId : entry.ScopeId)
                ? AccessLevel.Object
                : null,

        AccessScopeKind.Subtree =>
            target is not null && entry.ScopeFeatureId is { } root && target.AncestorIds.Contains(root)
                ? AccessLevel.Collection
                : null,

        AccessScopeKind.FeatureSet =>
            target is not null && entry.ScopeId is { } setId && target.FeatureSetIds.Contains(setId)
                ? AccessLevel.Collection
                : null,

        AccessScopeKind.CavingGroup =>
            target?.CavingGroupId is { } cavingGroupId && cavingGroupId == entry.ScopeId
                ? AccessLevel.Global
                : null,

        AccessScopeKind.All =>
            MatchesNarrowing(entry, target) ? AccessLevel.Global : null,

        AccessScopeKind.Own =>
            target is not null
            && target.OwnerUserId == ctx.UserId
            && target.OwnerUserId is not null
            && MatchesNarrowing(entry, target)
                ? AccessLevel.Global
                : null,

        _ => null,
    };

    private static bool MatchesNarrowing(AccessEntrySnapshot entry, AccessTargetFacts? target)
    {
        if (entry.FeatureKind is { } kind)
        {
            return target is not null && target.FeatureKind == kind;
        }

        if (entry.FeatureTypeId is { } typeId)
        {
            return target is not null && target.FeatureTypeId == typeId;
        }

        return true;
    }

    private static AccessDecision? DecideLevel(List<AccessEntrySnapshot>? matched, AccessLevel level)
    {
        if (matched is null || matched.Count == 0)
        {
            return null;
        }

        var denies = matched.Where(e => e.Effect == AccessEffect.Deny).ToList();
        return denies.Count > 0
            ? new AccessDecision(false, AccessDecisionSource.Entries, level, denies)
            : new AccessDecision(true, AccessDecisionSource.Entries, level, matched);
    }
}
