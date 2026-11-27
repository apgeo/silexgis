// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Access;
using SilexGis.Domain.TestData;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The people a test-data run invents: the same profile always invents the same people, every
/// way an account can be set up appears even in a small run, every club is led, nothing generated
/// can reach a real mailbox or telephone, and every planned grant is one the permission model
/// accepts.
/// </summary>
public class TestDataPeoplePlannerTests
{
    private static readonly TestDataProfile Small = TestDataProfiles.Small;

    [Fact]
    public void The_same_profile_invents_the_same_people_and_another_seed_others()
    {
        var first = TestDataPlanner.Plan(Small).People;
        var again = TestDataPlanner.Plan(Small).People;
        var other = TestDataPlanner.Plan(Small with { Seed = Small.Seed + 1 }).People;

        again.Clubs.Select(c => c.Name).ShouldBe(first.Clubs.Select(c => c.Name));
        again.Cavers.Select(c => (c.FullName, c.Email, c.Account?.Kind)).ShouldBe(first.Cavers.Select(c => (c.FullName, c.Email, c.Account?.Kind)));
        again.Grants.ShouldBe(first.Grants);
        other.Cavers.Select(c => c.FullName).ShouldNotBe(first.Cavers.Select(c => c.FullName));
    }

    [Fact]
    public void Counts_come_out_as_the_profile_asks()
    {
        var people = TestDataPlanner.Plan(TestDataProfiles.Default).People;

        people.Clubs.Count(c => c.Type == CavingGroupType.CavingClub).ShouldBe(14);
        people.Clubs.Count(c => c.Type == CavingGroupType.Organization).ShouldBe(2);
        people.Cavers.Count.ShouldBe(320);
        people.AccountHolders.Count().ShouldBe(60);
        people.Cavers.Count(c => c.ForeignGuest).ShouldBe(26); // 8 % of 320, rounded
        people.Cavers.Where(c => c.ForeignGuest).ShouldAllBe(c => c.Memberships.Count == 0);
        people.Cavers.Count(c => c.Memberships.Count(m => people.Clubs[m.ClubIndex].Type == CavingGroupType.CavingClub) == 2)
            .ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_small_run_has_one_account_of_every_kind()
    {
        var people = TestDataPlanner.Plan(Small).People;

        var kinds = people.AccountHolders.Select(c => c.Account!.Kind).ToHashSet();
        foreach (var kind in Enum.GetValues<TestDataAccountKind>())
        {
            kinds.ShouldContain(kind, $"no {kind} account in a small run");
        }
    }

    [Fact]
    public void Every_club_has_a_roster_and_exactly_one_owner()
    {
        var people = TestDataPlanner.Plan(TestDataProfiles.Default).People;

        foreach (var club in people.Clubs)
        {
            var roster = people.Cavers.SelectMany(c => c.Memberships).Where(m => m.ClubIndex == club.Index).ToList();
            roster.ShouldNotBeEmpty($"{club.Name} has nobody");
            roster.Count(m => m.Role == CavingGroupRole.Owner).ShouldBe(1, $"{club.Name} is not owned by exactly one person");
        }

        people.Clubs.Select(c => c.Name).Distinct().Count().ShouldBe(people.Clubs.Count);
        people.Clubs.Select(c => c.Slug).Distinct().Count().ShouldBe(people.Clubs.Count);
    }

    [Fact]
    public void Nothing_generated_can_reach_a_real_mailbox_or_telephone()
    {
        var people = TestDataPlanner.Plan(TestDataProfiles.Large).People;

        var emails = people.Cavers.Select(c => c.Email).OfType<string>().ToList();
        emails.ShouldNotBeEmpty();
        emails.ShouldAllBe(e => e.EndsWith("@" + TestDataPeoplePlanner.EmailDomain, StringComparison.Ordinal));
        emails.ShouldAllBe(e => e.All(ch => ch < 128));
        emails.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(emails.Count);
        people.AccountHolders.ShouldAllBe(c => c.Account!.Email == c.Email);

        var phones = people.Cavers.Select(c => c.Phone).OfType<string>().ToList();
        phones.ShouldNotBeEmpty();
        phones.ShouldAllBe(p => p.StartsWith(TestDataPeoplePlanner.PhonePrefix, StringComparison.Ordinal) && p.Length == TestDataPeoplePlanner.PhonePrefix.Length + 3);
        people.Clubs.Select(c => c.Website).OfType<string>().ShouldAllBe(w => w.EndsWith(".example.org", StringComparison.Ordinal));
    }

    [Fact]
    public void Club_leaders_are_the_first_to_hold_accounts()
    {
        var people = TestDataPlanner.Plan(TestDataProfiles.Default).People;

        var owners = people.Cavers.Where(c => c.Memberships.Any(m => m.Role == CavingGroupRole.Owner)).ToList();
        owners.ShouldAllBe(c => c.Account != null);
    }

    [Fact]
    public void Every_club_only_cave_is_bound_to_a_club_and_mostly_to_one_near_it()
    {
        var plan = TestDataPlanner.Plan(TestDataProfiles.Default);

        var clubOnly = plan.Caves.Where(c => c.Visibility == Visibility.CavingGroup).ToList();
        clubOnly.ShouldNotBeEmpty();
        clubOnly.ShouldAllBe(c => c.ClubIndex != null);
        clubOnly.ShouldAllBe(c => plan.People.Clubs[c.ClubIndex!.Value].Type == CavingGroupType.CavingClub);
        plan.Caves.Where(c => c.Visibility != Visibility.CavingGroup).ShouldAllBe(c => c.ClubIndex == null);
        clubOnly.Count(c => plan.People.Clubs[c.ClubIndex!.Value].HomeRegionCode == c.RegionCode)
            .ShouldBeGreaterThan(clubOnly.Count / 2);
    }

    [Fact]
    public void A_run_without_people_binds_no_cave_to_a_club()
    {
        var plan = TestDataPlanner.Plan(Small with { People = new TestDataPeopleProfile { Clubs = 0, Organisations = 0, Cavers = 0, Accounts = 0 } });

        plan.People.Clubs.ShouldBeEmpty();
        plan.People.Cavers.ShouldBeEmpty();
        plan.Caves.ShouldAllBe(c => c.ClubIndex == null);
    }

    [Fact]
    public void Every_planned_grant_is_one_the_permission_model_accepts_and_every_scope_shape_occurs()
    {
        var plan = TestDataPlanner.Plan(TestDataProfiles.Default);
        var grants = plan.People.Grants;

        foreach (var grant in grants)
        {
            var snapshot = new AccessEntrySnapshot(
                0,
                null,
                grant.Subject == PlannedGrantSubject.Account ? AccessSubjectKind.User : AccessSubjectKind.CavingGroup,
                Guid.NewGuid(),
                grant.Effect,
                grant.Domain,
                grant.Actions,
                grant.Scope,
                grant.ScopeCaveIndex is not null || grant.ScopeRegionCode is not null ? Guid.NewGuid() : null,
                grant.ScopeClubIndex is not null || grant.Scope is AccessScopeKind.CavingGroup ? Guid.NewGuid() : null,
                null,
                null);
            AccessEntryRules.Validate(snapshot).ShouldBeNull($"{grant} is refused");
        }

        foreach (var scope in new[] { AccessScopeKind.All, AccessScopeKind.Own, AccessScopeKind.CavingGroup, AccessScopeKind.Subtree, AccessScopeKind.Object })
        {
            grants.ShouldContain(g => g.Scope == scope, $"no grant scoped {scope}");
        }

        grants.ShouldContain(g => g.Effect == AccessEffect.Deny);
        grants.ShouldContain(g => g.Subject == PlannedGrantSubject.Club);
        grants.Where(g => g.ScopeCaveIndex is { } cave && (g.Actions & AccessAction.ViewExactLocation) != 0)
            .ShouldAllBe(g => plan.Caves[g.ScopeCaveIndex!.Value].LocationProtected);
        grants.Where(g => g.Effect == AccessEffect.Deny)
            .ShouldAllBe(g => plan.Caves[g.ScopeCaveIndex!.Value].Visibility == Visibility.Public);

        // The share of accounts given exact positions beyond their own club's, as the profile asks.
        var holders = plan.People.Cavers.Count(c => c.Account is { Kind: TestDataAccountKind.Member or TestDataAccountKind.Editor or TestDataAccountKind.Reviewer or TestDataAccountKind.TwoFactor });
        var exactGrants = grants.Count(g => g.Subject == PlannedGrantSubject.Account && (g.Actions & AccessAction.ViewExactLocation) != 0);
        Math.Abs(exactGrants - (holders * 0.15)).ShouldBeLessThanOrEqualTo(1.0);
    }

    [Fact]
    public void A_profile_asking_for_more_accounts_than_cavers_is_refused()
    {
        var bad = Small with { People = Small.People with { Accounts = Small.People.Cavers + 1 } };

        TestDataProfileRules.Problems(bad, 1000).ShouldContain(p => p.StartsWith("people.accounts", StringComparison.Ordinal));
        TestDataProfileRules.Problems(Small, 1000).ShouldBeEmpty();
        TestDataProfileRules.Problems(TestDataProfiles.Large, 50_000).ShouldBeEmpty();
    }
}
