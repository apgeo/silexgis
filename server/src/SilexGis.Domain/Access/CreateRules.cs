// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Access;

/// <summary>
/// Whether the caller may create in a domain. Creation has no row to evaluate yet, so
/// the walk runs against the *target context*: the prospective parent feature's facts
/// (which is how a subtree-scoped Create reaches, and how the ownership built-in lets
/// someone extend their own feature) plus the requested caving-group binding (which is
/// how a club's starter ruleset lets members add club content). A create with neither
/// sees global entries only.
/// </summary>
public static class CreateRules
{
    public const string ForbiddenCode = "access.create_forbidden";

    /// <summary>Creation with no parent feature — trip logs, uploads, saved views.</summary>
    public static bool MayCreate(
        AccessContext? ctx, AccessDomain domain, Guid? requestedCavingGroupId = null) =>
        MayCreate(ctx, domain, AccessTargetFacts.ForCreate(null, [], requestedCavingGroupId));

    /// <summary>Creation against a full target context (parent facts, binding, kind).</summary>
    public static bool MayCreate(AccessContext? ctx, AccessDomain domain, AccessTargetFacts target) =>
        AccessEvaluator.Decide(ctx, domain, AccessAction.Create, target).Allowed;

    /// <summary>
    /// The caller's own caving groups in which they may create a row of this domain bound to
    /// that group — what a create door needs to know before there is a request to decide.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A domain-wide check, made with no row in view, cannot see a right held at one caving
    /// group's scope: such an entry matches rows bound to that group and nothing else, so with
    /// nothing in view it matches nothing. That is correct for a coarse answer about the domain
    /// as such, and it leaves a member whose only right to add content is their club's ruleset
    /// with no way to learn that the right exists. This is that way, and it is deliberately the
    /// same two questions the write asks of a bound create — may this be created, and may it be
    /// bound to that group — put once per group the caller belongs to. So the answer cannot
    /// drift from the write: a deny that defeats the create defeats it here, and a ruleset that
    /// lost Create drops its group from the list in the same edit.
    /// </para>
    /// <para>
    /// Only the caller's own groups are asked about. Somebody can also be granted the right
    /// over the content of a group they are not in, and a create bound there is accepted; but
    /// that is a grant made on purpose for a particular job, and a form that offered every such
    /// group as a matter of course would file ordinary work under a club its author does not
    /// belong to. Whoever holds one names the group themselves.
    /// </para>
    /// <para>
    /// A domain whose rows carry no caving-group binding has nothing to answer and answers with
    /// no group. The order is the context's own and means nothing; whoever shows these to a
    /// person orders them by name.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Guid> CavingGroupsToCreateIn(AccessContext? ctx, AccessDomain domain)
    {
        if (ctx is null || !AccessEntryRules.IsTrioDomain(domain))
        {
            return [];
        }

        return
        [
            .. ctx.CavingGroupIds.Where(cavingGroupId =>
                MayCreate(ctx, domain, cavingGroupId)
                && CavingGroupBindingRules.MayBind(ctx, domain, cavingGroupId)),
        ];
    }
}
