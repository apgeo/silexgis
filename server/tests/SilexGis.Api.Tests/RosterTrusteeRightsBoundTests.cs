// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A caving group can be a trustee of an ordinary permission group, and every account whose
/// person sits in that caving group then holds the ruleset's rules. Adding the caving group as a
/// trustee is bounded by what the author holds; filling it — enrolling a member, linking an
/// account to a person already seated, folding a roster entry into one that is — hands out the
/// same rules from the other end and carries the same bound. Each test drives the write as a
/// keeper who lacks the ruleset's rules (refused, nothing moved) and, in the same test, as a
/// keeper who holds them (allowed, the account's rights really moved).
/// </summary>
public sealed class RosterTrusteeRightsBoundTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient keeper = null!;   // Manager: Write on the roster and on every caving group
    private HttpClient holder = null!;   // the same, and a direct trustee of the ruleset as well
    private Guid keeperId;
    private Guid holderId;
    private Guid rulesetId;
    private Guid trusteeGroupId;

    public RosterTrusteeRightsBoundTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        keeperId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Manager, $"rtb-keep-{suffix}@t.local");
        holderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Manager, $"rtb-hold-{suffix}@t.local");
        // A full administrator who can sign in, so the lockout guard a merge asks stays quiet.
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"rtb-adm-{suffix}@t.local");
        keeper = await AuthHelper.BearerClientAsync(factory, $"rtb-keep-{suffix}@t.local");
        holder = await AuthHelper.BearerClientAsync(factory, $"rtb-hold-{suffix}@t.local");

        // The ruleset: reading the permission model itself, which no Manager holds. The caving
        // group is its trustee, and so is the holder, directly.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var ruleset = new PermissionGroup { Name = $"Auditors {suffix}", Slug = $"rtb-auditors-{suffix}" };
        var club = new CavingGroup { Name = $"Audit club {suffix}", Slug = $"rtb-club-{suffix}" };
        db.PermissionGroups.Add(ruleset);
        db.CavingGroups.Add(club);
        db.AccessEntries.Add(new AccessEntry
        {
            PermissionGroupId = ruleset.Id,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.PermissionGroups,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.All,
            GrantedBy = holderId,
        });
        db.PermissionGroupMembers.Add(new PermissionGroupMember
        {
            PermissionGroupId = ruleset.Id,
            MemberKind = AccessSubjectKind.CavingGroup,
            MemberId = club.Id,
        });
        db.PermissionGroupMembers.Add(new PermissionGroupMember
        {
            PermissionGroupId = ruleset.Id,
            MemberKind = AccessSubjectKind.User,
            MemberId = holderId,
        });
        await db.SaveChangesAsync();
        rulesetId = ruleset.Id;
        trusteeGroupId = club.Id;

        // The preconditions every test leans on: neither keeper is a full administrator, only the
        // holder reads the permission model, and both may write the club's roster.
        var keeperCtx = await RosterHelper.AccessContextOfAsync(db, keeperId);
        var holderCtx = await RosterHelper.AccessContextOfAsync(db, holderId);
        keeperCtx.IsFullAdmin.ShouldBeFalse();
        holderCtx.IsFullAdmin.ShouldBeFalse();
        ReadsPermissionModel(keeperCtx).ShouldBeFalse();
        ReadsPermissionModel(holderCtx).ShouldBeTrue();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        keeper.Dispose();
        holder.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task Enrolling_an_account_in_a_trustee_group_needs_the_rules_it_hands_on()
    {
        var accountId = await NewAccountAsync("enrol");
        var caverId = await RosterHelper.CaverIdForAsync(factory, accountId);

        var refused = await keeper.PostAsJsonAsync($"/api/v1/caving-groups/{trusteeGroupId}/members",
            new { caverId, role = "member" });
        await ShouldBeRefusedAsync(refused);
        (await ReadsPermissionModelAsync(accountId)).ShouldBeFalse();
        (await SeatedAsync(caverId)).ShouldBeFalse();

        var allowed = await holder.PostAsJsonAsync($"/api/v1/caving-groups/{trusteeGroupId}/members",
            new { caverId, role = "member" });
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await ReadsPermissionModelAsync(accountId)).ShouldBeTrue();

        // Relabelling somebody already seated moves no rules.
        var relabelled = await keeper.PostAsJsonAsync($"/api/v1/caving-groups/{trusteeGroupId}/members",
            new { caverId, role = "admin" });
        relabelled.StatusCode.ShouldBe(HttpStatusCode.OK, await relabelled.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// An account that already reaches the ruleset by another path gains nothing by being seated,
    /// and neither does a person with no account — both stay ordinary roster writes.
    /// </summary>
    [Fact]
    public async Task Seating_somebody_who_gains_nothing_stays_a_keepers_job()
    {
        var accountId = await NewAccountAsync("already");
        var caverId = await RosterHelper.CaverIdForAsync(factory, accountId);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.PermissionGroupMembers.Add(new PermissionGroupMember
            {
                PermissionGroupId = rulesetId,
                MemberKind = AccessSubjectKind.User,
                MemberId = accountId,
            });
            await db.SaveChangesAsync();
        }

        var already = await keeper.PostAsJsonAsync($"/api/v1/caving-groups/{trusteeGroupId}/members",
            new { caverId, role = "member" });
        already.StatusCode.ShouldBe(HttpStatusCode.OK, await already.Content.ReadAsStringAsync());

        var ghost = await NewAccountlessCaverAsync("Ghost");
        var seatedGhost = await keeper.PostAsJsonAsync($"/api/v1/caving-groups/{trusteeGroupId}/members",
            new { caverId = ghost, role = "member" });
        seatedGhost.StatusCode.ShouldBe(HttpStatusCode.OK, await seatedGhost.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Linking_an_account_to_a_seated_person_needs_the_rules_it_hands_on()
    {
        var accountId = await AccountWithoutRosterEntryAsync("link");
        var seat = await SeatedAccountlessCaverAsync("Link seat");

        var refused = await keeper.PostAsJsonAsync($"/api/v1/cavers/{seat}/account-link", new { userId = accountId });
        await ShouldBeRefusedAsync(refused);
        (await ReadsPermissionModelAsync(accountId)).ShouldBeFalse();

        var allowed = await holder.PostAsJsonAsync($"/api/v1/cavers/{seat}/account-link", new { userId = accountId });
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await ReadsPermissionModelAsync(accountId)).ShouldBeTrue();
    }

    [Fact]
    public async Task Merging_an_account_into_a_seated_person_needs_the_rules_it_hands_on()
    {
        var accountId = await NewAccountAsync("merge");
        var withAccount = await RosterHelper.CaverIdForAsync(factory, accountId);
        var seat = await SeatedAccountlessCaverAsync("Merge seat");

        var refused = await keeper.PostAsJsonAsync($"/api/v1/cavers/{seat}/merge", new { sourceCaverId = withAccount });
        await ShouldBeRefusedAsync(refused);
        (await ReadsPermissionModelAsync(accountId)).ShouldBeFalse();

        // The other direction is the same grant: the seat is folded into the account's entry.
        var folded = await keeper.PostAsJsonAsync($"/api/v1/cavers/{withAccount}/merge", new { sourceCaverId = seat });
        await ShouldBeRefusedAsync(folded);
        (await ReadsPermissionModelAsync(accountId)).ShouldBeFalse();

        var allowed = await holder.PostAsJsonAsync($"/api/v1/cavers/{seat}/merge", new { sourceCaverId = withAccount });
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await ReadsPermissionModelAsync(accountId)).ShouldBeTrue();
    }

    /// <summary>
    /// A club's own starter ruleset grants its members exact locations on the club's material,
    /// which a Manager does not hold. Keeping a club's roster is deciding who is in it, so a
    /// keeper who may write the roster still enrols people — the rules scoped to that very club
    /// are what membership means — while the club's other rules stay bounded (the tests above).
    /// </summary>
    [Fact]
    public async Task A_keeper_who_is_not_in_a_club_still_enrols_people_into_it()
    {
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"rtb-found-{suffix}@t.local");
        using var founder = await AuthHelper.BearerClientAsync(factory, $"rtb-found-{suffix}@t.local");
        var created = await founder.PostAsJsonAsync("/api/v1/caving-groups", new
        {
            name = $"Founded club {suffix}",
            description = (string?)null,
            website = (string?)null,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var clubId = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var keeperCtx = await RosterHelper.AccessContextOfAsync(db, keeperId);
            AccessEvaluator.Decide(keeperCtx, AccessDomain.Features, AccessAction.ViewExactLocation,
                new AccessTargetFacts { CavingGroupId = clubId }).Allowed
                .ShouldBeFalse("the keeper does not hold what the club's starter ruleset hands its members");
        }

        var accountId = await NewAccountAsync("club");
        var caverId = await RosterHelper.CaverIdForAsync(factory, accountId);
        var enrolled = await keeper.PostAsJsonAsync($"/api/v1/caving-groups/{clubId}/members",
            new { caverId, role = "member" });
        enrolled.StatusCode.ShouldBe(HttpStatusCode.OK, await enrolled.Content.ReadAsStringAsync());

        await using var after = factory.Services.CreateAsyncScope();
        var afterDb = after.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        AccessEvaluator.Decide(await RosterHelper.AccessContextOfAsync(afterDb, accountId), AccessDomain.Features,
            AccessAction.ViewExactLocation, new AccessTargetFacts { CavingGroupId = clubId }).Allowed.ShouldBeTrue();
    }

    // ---- helpers ----

    private static bool ReadsPermissionModel(AccessContext ctx) =>
        AccessEvaluator.Decide(ctx, AccessDomain.PermissionGroups, AccessAction.Read, null).Allowed;

    private async Task<bool> ReadsPermissionModelAsync(Guid accountId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return ReadsPermissionModel(await RosterHelper.AccessContextOfAsync(db, accountId));
    }

    private async Task<Guid> NewAccountAsync(string tag) =>
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"rtb-{tag}-{suffix}@t.local");

    private async Task<Guid> AccountWithoutRosterEntryAsync(string tag)
    {
        var accountId = await NewAccountAsync(tag);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Cavers.Where(c => c.UserId == accountId).ExecuteUpdateAsync(c => c.SetProperty(x => x.UserId, (Guid?)null));
        return accountId;
    }

    private async Task<Guid> NewAccountlessCaverAsync(string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var caver = new Caver { FullName = $"{name} {suffix}" };
        db.Cavers.Add(caver);
        await db.SaveChangesAsync();
        return caver.Id;
    }

    private async Task<Guid> SeatedAccountlessCaverAsync(string name)
    {
        var caverId = await NewAccountlessCaverAsync(name);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.CavingGroupMemberships.Add(new CavingGroupMembership
        {
            CavingGroupId = trusteeGroupId,
            CaverId = caverId,
            Role = CavingGroupRole.Member,
        });
        await db.SaveChangesAsync();
        return caverId;
    }

    private async Task<bool> SeatedAsync(Guid caverId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.CavingGroupMemberships.AnyAsync(m => m.CavingGroupId == trusteeGroupId && m.CaverId == caverId);
    }

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, body);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().ShouldBe(AccessEntryRules.ExceedsOwnRightsCode);
    }
}
