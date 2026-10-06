// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Access;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Where a create reaches through a caving group. A check made with no row in view cannot see a
/// right held at one group's scope, so the caller's own groups are asked one at a time, by the
/// rule the write itself asks — and what is pinned here is that the answer follows that rule in
/// every direction: an allow names its group and no other, a deny takes it back, and a ruleset
/// that never held Create names nothing.
/// </summary>
public class CreateRulesTests
{
    private static readonly Guid CallerId = Guid.CreateVersion7();
    private static readonly Guid FirstClub = Guid.CreateVersion7();
    private static readonly Guid SecondClub = Guid.CreateVersion7();
    private static readonly Guid SomebodyElsesClub = Guid.CreateVersion7();

    private static AccessEntrySnapshot Entry(
        AccessEffect effect,
        AccessAction actions,
        AccessScopeKind scope,
        Guid? scopeId = null,
        AccessDomain domain = AccessDomain.TripLogs) => new(
        1, Guid.CreateVersion7(), null, null, effect, domain, actions, scope, null, scopeId, null, null);

    /// <summary>What a club's starter ruleset hands its members over one domain.</summary>
    private static AccessEntrySnapshot ClubRuleset(Guid clubId, AccessDomain domain = AccessDomain.TripLogs) =>
        Entry(
            AccessEffect.Allow,
            AccessAction.Read | AccessAction.Write | AccessAction.Create,
            AccessScopeKind.CavingGroup,
            clubId,
            domain);

    private static AccessContext Member(IReadOnlyList<Guid> clubs, params AccessEntrySnapshot[] entries) =>
        new(CallerId, false, clubs, entries);

    [Fact]
    public void A_right_held_at_one_clubs_scope_names_that_club_and_is_invisible_with_no_row_in_view()
    {
        var ctx = Member([FirstClub, SecondClub], ClubRuleset(FirstClub));

        // The domain-wide question still answers no: nothing bound to no club may be created.
        CreateRules.MayCreate(ctx, AccessDomain.TripLogs).ShouldBeFalse();

        // And the group the right is scoped to is named — the other club the caller is in, whose
        // ruleset grants nothing here, is not.
        CreateRules.CavingGroupsToCreateIn(ctx, AccessDomain.TripLogs).ShouldBe([FirstClub]);
    }

    [Fact]
    public void Every_club_whose_ruleset_grants_it_is_named()
    {
        var ctx = Member([FirstClub, SecondClub], ClubRuleset(FirstClub), ClubRuleset(SecondClub));

        CreateRules.CavingGroupsToCreateIn(ctx, AccessDomain.TripLogs)
            .ShouldBe([FirstClub, SecondClub], ignoreOrder: true);
    }

    [Fact]
    public void A_club_the_caller_is_not_in_is_never_named()
    {
        // The entry reaches the caller — a grant over another club's content, made directly —
        // and a create bound there would be accepted. It is still not offered as a matter of
        // course: only the caller's own groups are asked about.
        var ctx = Member([FirstClub], ClubRuleset(SomebodyElsesClub));

        CreateRules.MayCreate(ctx, AccessDomain.TripLogs, SomebodyElsesClub).ShouldBeTrue();
        CreateRules.CavingGroupsToCreateIn(ctx, AccessDomain.TripLogs).ShouldBeEmpty();
    }

    [Fact]
    public void A_domain_wide_deny_empties_the_list_as_it_refuses_the_write()
    {
        // A group-scoped allow and a domain-wide deny sit at the same level, and deny wins
        // within a level — so the write bound to the club is refused, and the list must not
        // offer a door onto that refusal.
        var ctx = Member(
            [FirstClub, SecondClub],
            ClubRuleset(FirstClub),
            ClubRuleset(SecondClub),
            Entry(AccessEffect.Deny, AccessAction.Create, AccessScopeKind.All));

        CreateRules.MayCreate(ctx, AccessDomain.TripLogs, FirstClub).ShouldBeFalse();
        CreateRules.CavingGroupsToCreateIn(ctx, AccessDomain.TripLogs).ShouldBeEmpty();
    }

    [Fact]
    public void A_deny_scoped_to_one_club_takes_only_that_club_out()
    {
        var ctx = Member(
            [FirstClub, SecondClub],
            ClubRuleset(FirstClub),
            ClubRuleset(SecondClub),
            Entry(AccessEffect.Deny, AccessAction.Create, AccessScopeKind.CavingGroup, FirstClub));

        CreateRules.CavingGroupsToCreateIn(ctx, AccessDomain.TripLogs).ShouldBe([SecondClub]);
    }

    [Fact]
    public void A_club_whose_ruleset_lost_create_is_absent()
    {
        // Reading and correcting the club's trips without being allowed to add one is a ruleset
        // a club may well choose; it must not draw a door.
        var readAndWriteOnly = Entry(
            AccessEffect.Allow,
            AccessAction.Read | AccessAction.Write,
            AccessScopeKind.CavingGroup,
            FirstClub);
        var ctx = Member([FirstClub, SecondClub], readAndWriteOnly, ClubRuleset(SecondClub));

        CreateRules.CavingGroupsToCreateIn(ctx, AccessDomain.TripLogs).ShouldBe([SecondClub]);
    }

    [Fact]
    public void A_right_in_one_domain_says_nothing_about_another()
    {
        var ctx = Member([FirstClub], ClubRuleset(FirstClub, AccessDomain.Features));

        CreateRules.CavingGroupsToCreateIn(ctx, AccessDomain.Features).ShouldBe([FirstClub]);
        CreateRules.CavingGroupsToCreateIn(ctx, AccessDomain.TripLogs).ShouldBeEmpty();
    }

    [Fact]
    public void Somebody_holding_the_right_over_the_domain_may_create_in_each_of_their_own_clubs()
    {
        // The domain-wide allow matches whatever the row is bound to, and membership is what the
        // binding guard asks — so their clubs are named beside the map that already says create,
        // and a club they are not in is not.
        var domainWide = Entry(AccessEffect.Allow, AccessAction.Create, AccessScopeKind.All);

        CreateRules.CavingGroupsToCreateIn(Member([FirstClub, SecondClub], domainWide), AccessDomain.TripLogs)
            .ShouldBe([FirstClub, SecondClub], ignoreOrder: true);
        CreateRules.CavingGroupsToCreateIn(Member([], domainWide), AccessDomain.TripLogs).ShouldBeEmpty();
    }

    [Fact]
    public void A_full_administrator_is_asked_about_their_own_clubs_like_anybody_else()
    {
        // Unreachable by deny and allowed everything — and still offered only the clubs they
        // belong to, because the list is about where a form should file their work by default.
        var ctx = new AccessContext(
            CallerId,
            true,
            [FirstClub],
            [Entry(AccessEffect.Deny, AccessAction.Create, AccessScopeKind.All)]);

        CreateRules.CavingGroupsToCreateIn(ctx, AccessDomain.TripLogs).ShouldBe([FirstClub]);
    }

    [Fact]
    public void Nobody_signed_in_and_a_domain_with_no_group_binding_answer_with_no_group()
    {
        CreateRules.CavingGroupsToCreateIn(null, AccessDomain.TripLogs).ShouldBeEmpty();

        // A vocabulary row belongs to no club, so there is no group a new one could be bound to
        // — whoever asks, and whatever they hold.
        var administrator = new AccessContext(CallerId, true, [FirstClub], []);
        CreateRules.CavingGroupsToCreateIn(administrator, AccessDomain.Taxonomies).ShouldBeEmpty();
        CreateRules.CavingGroupsToCreateIn(administrator, AccessDomain.Terrain).ShouldBeEmpty();
    }

    /// <summary>
    /// The property the list exists for, over every domain at once: a group is named exactly
    /// when a create bound to it would pass both questions the write asks. A list that named a
    /// group the write refuses would draw a door onto a refusal; one that left out a group the
    /// write accepts would hide a door, which is the fault this answer was added to end.
    /// </summary>
    [Fact]
    public void The_list_agrees_with_the_write_for_every_domain_and_every_one_of_the_callers_clubs()
    {
        Guid[] clubs = [FirstClub, SecondClub];
        AccessContext[] callers =
        [
            Member(clubs),
            Member(clubs, ClubRuleset(FirstClub), ClubRuleset(SecondClub, AccessDomain.Features)),
            Member(clubs, ClubRuleset(FirstClub), Entry(AccessEffect.Deny, AccessAction.Create, AccessScopeKind.All)),
            Member(clubs, Entry(AccessEffect.Allow, AccessAction.Create, AccessScopeKind.All, domain: AccessDomain.Documents)),
            new AccessContext(CallerId, true, clubs, []),
        ];

        foreach (var ctx in callers)
        {
            foreach (var domain in Enum.GetValues<AccessDomain>())
            {
                var named = CreateRules.CavingGroupsToCreateIn(ctx, domain);
                foreach (var club in clubs)
                {
                    var accepted = AccessEntryRules.IsTrioDomain(domain)
                        && CreateRules.MayCreate(ctx, domain, club)
                        && CavingGroupBindingRules.MayBind(ctx, domain, club);
                    named.Contains(club).ShouldBe(accepted, $"{domain} in {club}");
                }
            }
        }
    }
}
