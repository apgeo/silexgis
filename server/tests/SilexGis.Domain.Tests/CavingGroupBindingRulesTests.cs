// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Access;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The binding guard: pushing content into caving group G hands G's members whatever
/// their rulesets grant over G's content, so it takes membership, full admin, or an
/// undefeated allow that names G's content specifically — never a merely domain-wide
/// right.
/// </summary>
public class CavingGroupBindingRulesTests
{
    private static readonly Guid CallerId = Guid.CreateVersion7();
    private static readonly Guid GroupId = Guid.CreateVersion7();

    private static AccessEntrySnapshot Entry(
        AccessEffect effect, AccessAction actions, AccessScopeKind scope, Guid? scopeId = null) => new(
        1, Guid.CreateVersion7(), null, null, effect, AccessDomain.Features, actions, scope,
        null, scopeId, null, null);

    [Fact]
    public void Members_and_full_admins_may_bind()
    {
        CavingGroupBindingRules.MayBind(
            new AccessContext(CallerId, false, [GroupId], []), AccessDomain.Features, GroupId)
            .ShouldBeTrue();
        CavingGroupBindingRules.MayBind(
            new AccessContext(CallerId, true, [], []), AccessDomain.Features, GroupId)
            .ShouldBeTrue();
        CavingGroupBindingRules.MayBind(null, AccessDomain.Features, GroupId).ShouldBeFalse();
    }

    [Fact]
    public void A_group_scoped_allow_qualifies_a_domain_wide_allow_does_not()
    {
        var scoped = new AccessContext(CallerId, false, [],
            [Entry(AccessEffect.Allow, AccessAction.Write, AccessScopeKind.CavingGroup, GroupId)]);
        CavingGroupBindingRules.MayBind(scoped, AccessDomain.Features, GroupId).ShouldBeTrue();

        // Domain-wide Write never implied membership of every club before the redesign,
        // and it does not now — this is the hole the guard exists to close.
        var domainWide = new AccessContext(CallerId, false, [],
            [Entry(AccessEffect.Allow, AccessAction.Write | AccessAction.Create, AccessScopeKind.All)]);
        CavingGroupBindingRules.MayBind(domainWide, AccessDomain.Features, GroupId).ShouldBeFalse();
    }

    [Fact]
    public void A_deny_defeats_the_group_scoped_allow()
    {
        var ctx = new AccessContext(CallerId, false, [],
        [
            Entry(AccessEffect.Allow, AccessAction.Write, AccessScopeKind.CavingGroup, GroupId),
            Entry(AccessEffect.Deny, AccessAction.Write | AccessAction.Create, AccessScopeKind.All),
        ]);

        CavingGroupBindingRules.MayBind(ctx, AccessDomain.Features, GroupId).ShouldBeFalse();
    }

    [Fact]
    public void Either_create_or_write_scoped_to_the_group_suffices()
    {
        var createOnly = new AccessContext(CallerId, false, [],
            [Entry(AccessEffect.Allow, AccessAction.Create, AccessScopeKind.CavingGroup, GroupId)]);
        CavingGroupBindingRules.MayBind(createOnly, AccessDomain.Features, GroupId).ShouldBeTrue();

        var otherGroup = new AccessContext(CallerId, false, [],
            [Entry(AccessEffect.Allow, AccessAction.Create, AccessScopeKind.CavingGroup, Guid.CreateVersion7())]);
        CavingGroupBindingRules.MayBind(otherGroup, AccessDomain.Features, GroupId).ShouldBeFalse();
    }
}
