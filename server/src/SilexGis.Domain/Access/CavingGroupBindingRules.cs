// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Access;

/// <summary>
/// Guard on setting or changing a row's caving-group binding. Binding content to group
/// G hands G's members whatever their rulesets grant over G's content — without this
/// check, anyone with domain-wide Write could push a row into a club and thereby move
/// rights. Full admins are exempt; members of G may bind to their own group; everyone
/// else needs an undefeated allow that names G's content specifically — a domain-wide
/// allow is deliberately NOT enough, exactly as domain-wide Write never implied
/// membership of every club before.
/// </summary>
public static class CavingGroupBindingRules
{
    public const string ForbiddenCode = "access.caving_group_binding_forbidden";

    public static bool MayBind(AccessContext? ctx, AccessDomain domain, Guid cavingGroupId)
    {
        if (ctx is null)
        {
            return false;
        }

        if (ctx.IsFullAdmin || ctx.CavingGroupIds.Contains(cavingGroupId))
        {
            return true;
        }

        var facts = new AccessTargetFacts { CavingGroupId = cavingGroupId };
        return HoldsGroupScopedAllow(ctx, domain, AccessAction.Create, cavingGroupId, facts)
            || HoldsGroupScopedAllow(ctx, domain, AccessAction.Write, cavingGroupId, facts);
    }

    /// <summary>
    /// True when the precedence walk answers ALLOW for the group's content AND that
    /// allow rests on an entry scoped to this very group — so denies at any level still
    /// defeat it, while a broader allow alone does not qualify.
    /// </summary>
    private static bool HoldsGroupScopedAllow(
        AccessContext ctx, AccessDomain domain, AccessAction action, Guid cavingGroupId, AccessTargetFacts facts)
    {
        var decision = AccessEvaluator.Decide(ctx, domain, action, facts);
        return decision.Allowed
            && decision.Source == AccessDecisionSource.Entries
            && decision.DecidingEntries.Any(e =>
                e.ScopeKind == AccessScopeKind.CavingGroup && e.ScopeId == cavingGroupId);
    }
}
