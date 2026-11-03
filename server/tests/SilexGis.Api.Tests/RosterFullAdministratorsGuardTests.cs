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
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A caving group can be a trustee of Full Administrators, so every roster write that moves an
/// account onto or off such a group's membership changes who holds full administration. A roster
/// keeper holds Write on the roster and on every caving group, and none of those writes may let
/// them make — or unmake — a full administrator: linking an account, folding one entry into
/// another in either direction, enrolling a member and taking one out. Each test drives the same
/// write twice, once as the keeper (refused, nothing changed) and once as a full administrator
/// (allowed, and the account's standing really moved), so a refusal that simply broke the route
/// would fail here as surely as a guard that let the keeper through.
/// </summary>
public sealed class RosterFullAdministratorsGuardTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient keeper = null!;   // Manager role: Write on the roster and on every caving group
    private HttpClient admin = null!;    // a full administrator — also what keeps the lockout guard quiet
    private Guid adminsCavingGroupId;

    public RosterFullAdministratorsGuardTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        var keeperId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Manager, $"rfa-keep-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"rfa-adm-{suffix}@t.local");
        keeper = await AuthHelper.BearerClientAsync(factory, $"rfa-keep-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"rfa-adm-{suffix}@t.local");

        // The board: a caving group that has been made a trustee of Full Administrators, so that
        // every account whose person sits in it is a full administrator.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var board = new CavingGroup { Name = $"Board {suffix}", Slug = $"rfa-board-{suffix}" };
        db.CavingGroups.Add(board);
        var fullAdminsId = await db.PermissionGroups
            .Where(g => g.Slug == SeededPermissionGroups.FullAdministratorsSlug)
            .Select(g => g.Id).SingleAsync();
        db.PermissionGroupMembers.Add(new PermissionGroupMember
        {
            PermissionGroupId = fullAdminsId,
            MemberKind = AccessSubjectKind.CavingGroup,
            MemberId = board.Id,
        });
        await db.SaveChangesAsync();
        adminsCavingGroupId = board.Id;

        // The precondition every test leans on: the keeper is not a full administrator.
        (await RosterHelper.AccessContextOfAsync(db, keeperId)).IsFullAdmin.ShouldBeFalse();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        keeper.Dispose();
        admin.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task Linking_an_account_to_a_person_on_the_board_is_reserved_to_full_administrators()
    {
        var accountId = await AccountWithoutRosterEntryAsync("link");
        var boardSeat = await AccountlessBoardMemberAsync("Link seat");

        var refused = await keeper.PostAsJsonAsync($"/api/v1/cavers/{boardSeat}/account-link", new { userId = accountId });
        await ShouldBeRefusedAsync(refused, FullAdminGuard.GrantsFullAdminCode);
        (await IsFullAdminAsync(accountId)).ShouldBeFalse();

        var allowed = await admin.PostAsJsonAsync($"/api/v1/cavers/{boardSeat}/account-link", new { userId = accountId });
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await IsFullAdminAsync(accountId)).ShouldBeTrue();
    }

    /// <summary>
    /// The direction the guard used to miss: the duplicate carries the account and the survivor
    /// carries the board seat. The merge moves the account onto the survivor, which is exactly
    /// the link the route above refuses.
    /// </summary>
    [Fact]
    public async Task Merging_an_account_into_a_person_on_the_board_is_reserved_to_full_administrators()
    {
        var accountId = await NewAccountAsync("merge-into");
        var withAccount = await RosterEntryOfAsync(accountId);
        var boardSeat = await AccountlessBoardMemberAsync("Merge seat");

        var refused = await keeper.PostAsJsonAsync($"/api/v1/cavers/{boardSeat}/merge", new { sourceCaverId = withAccount });
        await ShouldBeRefusedAsync(refused, FullAdminGuard.GrantsFullAdminCode);
        (await IsFullAdminAsync(accountId)).ShouldBeFalse();
        (await CaverExistsAsync(withAccount)).ShouldBeTrue();

        var allowed = await admin.PostAsJsonAsync($"/api/v1/cavers/{boardSeat}/merge", new { sourceCaverId = withAccount });
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await IsFullAdminAsync(accountId)).ShouldBeTrue();
    }

    /// <summary>The other direction: the board seat is the duplicate folded into the account's entry.</summary>
    [Fact]
    public async Task Merging_a_person_on_the_board_into_an_account_is_reserved_to_full_administrators()
    {
        var accountId = await NewAccountAsync("merge-from");
        var withAccount = await RosterEntryOfAsync(accountId);
        var boardSeat = await AccountlessBoardMemberAsync("Folded seat");

        var refused = await keeper.PostAsJsonAsync($"/api/v1/cavers/{withAccount}/merge", new { sourceCaverId = boardSeat });
        await ShouldBeRefusedAsync(refused, FullAdminGuard.GrantsFullAdminCode);
        (await IsFullAdminAsync(accountId)).ShouldBeFalse();

        var allowed = await admin.PostAsJsonAsync($"/api/v1/cavers/{withAccount}/merge", new { sourceCaverId = boardSeat });
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await IsFullAdminAsync(accountId)).ShouldBeTrue();
    }

    /// <summary>
    /// A merge that moves no account onto the board changes nobody's standing, so a keeper may
    /// still tidy up duplicates of board members: two account-less entries fold together freely.
    /// </summary>
    [Fact]
    public async Task Merging_two_account_less_entries_moves_no_account_and_stays_a_keepers_job()
    {
        var boardSeat = await AccountlessBoardMemberAsync("Seat");
        var duplicate = await AccountlessBoardMemberAsync("Seat duplicate");

        var merged = await keeper.PostAsJsonAsync($"/api/v1/cavers/{boardSeat}/merge", new { sourceCaverId = duplicate });
        merged.StatusCode.ShouldBe(HttpStatusCode.OK, await merged.Content.ReadAsStringAsync());
        (await CaverExistsAsync(duplicate)).ShouldBeFalse();
    }

    [Fact]
    public async Task Enrolling_an_account_on_the_board_is_reserved_to_full_administrators()
    {
        var accountId = await NewAccountAsync("enrol");
        var caverId = await RosterEntryOfAsync(accountId);

        var refused = await keeper.PostAsJsonAsync($"/api/v1/caving-groups/{adminsCavingGroupId}/members",
            new { caverId, role = "member" });
        await ShouldBeRefusedAsync(refused, FullAdminGuard.GrantsFullAdminCode);
        (await IsFullAdminAsync(accountId)).ShouldBeFalse();
        (await IsBoardMemberAsync(caverId)).ShouldBeFalse();

        // An account-less person gains nobody anything until an account is linked to them, and the
        // link is guarded — so enrolling one stays an ordinary roster write.
        var accountless = await NewAccountlessCaverAsync("Enrolled ghost");
        var ghost = await keeper.PostAsJsonAsync($"/api/v1/caving-groups/{adminsCavingGroupId}/members",
            new { caverId = accountless, role = "member" });
        ghost.StatusCode.ShouldBe(HttpStatusCode.OK, await ghost.Content.ReadAsStringAsync());

        var allowed = await admin.PostAsJsonAsync($"/api/v1/caving-groups/{adminsCavingGroupId}/members",
            new { caverId, role = "member" });
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await IsFullAdminAsync(accountId)).ShouldBeTrue();

        // Relabelling a member already on the board moves nobody onto a new path.
        var relabelled = await keeper.PostAsJsonAsync($"/api/v1/caving-groups/{adminsCavingGroupId}/members",
            new { caverId, role = "admin" });
        relabelled.StatusCode.ShouldBe(HttpStatusCode.OK, await relabelled.Content.ReadAsStringAsync());
        (await IsFullAdminAsync(accountId)).ShouldBeTrue();
    }

    [Fact]
    public async Task Taking_somebody_off_the_board_is_reserved_to_full_administrators_but_leaving_is_not()
    {
        var removedId = await NewAccountAsync("remove");
        var removedCaver = await BoardMemberWithAccountAsync(removedId);
        (await IsFullAdminAsync(removedId)).ShouldBeTrue();

        var refused = await keeper.DeleteAsync($"/api/v1/caving-groups/{adminsCavingGroupId}/members/{removedCaver}");
        await ShouldBeRefusedAsync(refused, FullAdminGuard.RevokesFullAdminCode);
        (await IsFullAdminAsync(removedId)).ShouldBeTrue();

        var allowed = await admin.DeleteAsync($"/api/v1/caving-groups/{adminsCavingGroupId}/members/{removedCaver}");
        allowed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await allowed.Content.ReadAsStringAsync());
        (await IsFullAdminAsync(removedId)).ShouldBeFalse();

        // A member stepping down of their own accord is not an edit of anybody else's rights.
        var leaverId = await NewAccountAsync("leave");
        var leaverCaver = await BoardMemberWithAccountAsync(leaverId);
        using var leaver = await AuthHelper.BearerClientAsync(factory, $"rfa-leave-{suffix}@t.local");
        var left = await leaver.DeleteAsync($"/api/v1/caving-groups/{adminsCavingGroupId}/members/{leaverCaver}");
        left.StatusCode.ShouldBe(HttpStatusCode.NoContent, await left.Content.ReadAsStringAsync());
        (await IsFullAdminAsync(leaverId)).ShouldBeFalse();
    }

    [Fact]
    public async Task Unlinking_an_account_from_a_person_on_the_board_is_reserved_to_full_administrators()
    {
        var accountId = await NewAccountAsync("unlink");
        var caverId = await BoardMemberWithAccountAsync(accountId);

        var refused = await keeper.DeleteAsync($"/api/v1/cavers/{caverId}/account-link");
        await ShouldBeRefusedAsync(refused, FullAdminGuard.RevokesFullAdminCode);
        (await IsFullAdminAsync(accountId)).ShouldBeTrue();

        var allowed = await admin.DeleteAsync($"/api/v1/cavers/{caverId}/account-link");
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await IsFullAdminAsync(accountId)).ShouldBeFalse();
    }

    /// <summary>
    /// Deleting the board outright takes its row off the Full Administrators trustee list and full
    /// administration from everybody seated in it. An installation Administrator holds Delete on
    /// every caving group but is not a full administrator, and the delete must not be the way round
    /// the reservation the trustee list and the member removal keep.
    /// </summary>
    [Fact]
    public async Task Deleting_the_board_is_reserved_to_full_administrators()
    {
        using var administrator = await AdministratorClientAsync("del-board");
        var seatedId = await NewAccountAsync("seated");
        _ = await BoardMemberWithAccountAsync(seatedId);
        (await IsFullAdminAsync(seatedId)).ShouldBeTrue();

        var refused = await administrator.DeleteAsync($"/api/v1/caving-groups/{adminsCavingGroupId}");
        await ShouldBeRefusedAsync(refused, FullAdminGuard.RevokesFullAdminCode);
        (await IsFullAdminAsync(seatedId)).ShouldBeTrue();

        // The same administrator deletes an ordinary club: the refusal is about the board alone.
        var club = await NewCavingGroupAsync("Ordinary club");
        var ordinary = await administrator.DeleteAsync($"/api/v1/caving-groups/{club}");
        ordinary.StatusCode.ShouldBe(HttpStatusCode.NoContent, await ordinary.Content.ReadAsStringAsync());

        var allowed = await admin.DeleteAsync($"/api/v1/caving-groups/{adminsCavingGroupId}");
        allowed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await allowed.Content.ReadAsStringAsync());
        (await IsFullAdminAsync(seatedId)).ShouldBeFalse();
    }

    /// <summary>
    /// Deleting a person cascades their memberships, so deleting one seated on the board takes full
    /// administration from their account — the change the unlink above reserves.
    /// </summary>
    [Fact]
    public async Task Deleting_a_person_on_the_board_is_reserved_to_full_administrators()
    {
        using var administrator = await AdministratorClientAsync("del-person");
        var accountId = await NewAccountAsync("deleted");
        var caverId = await BoardMemberWithAccountAsync(accountId);

        var refused = await administrator.DeleteAsync($"/api/v1/cavers/{caverId}");
        await ShouldBeRefusedAsync(refused, FullAdminGuard.RevokesFullAdminCode);
        (await IsFullAdminAsync(accountId)).ShouldBeTrue();
        (await CaverExistsAsync(caverId)).ShouldBeTrue();

        // A person who is not on the board is the same administrator's to delete.
        var elsewhere = await NewAccountlessCaverAsync("Elsewhere");
        var ordinary = await administrator.DeleteAsync($"/api/v1/cavers/{elsewhere}");
        ordinary.StatusCode.ShouldBe(HttpStatusCode.NoContent, await ordinary.Content.ReadAsStringAsync());

        var allowed = await admin.DeleteAsync($"/api/v1/cavers/{caverId}");
        allowed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await allowed.Content.ReadAsStringAsync());
        (await IsFullAdminAsync(accountId)).ShouldBeFalse();
    }

    // ---- helpers ----

    /// <summary>
    /// An account in the seeded Administrators ruleset — Delete on every caving group and every
    /// person — and, proved here, not a full administrator.
    /// </summary>
    private async Task<HttpClient> AdministratorClientAsync(string tag)
    {
        var accountId = await NewAccountAsync(tag);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var administratorsId = await db.PermissionGroups
            .Where(g => g.Slug == SeededPermissionGroups.AdministratorsSlug)
            .Select(g => g.Id).SingleAsync();
        db.PermissionGroupMembers.Add(new PermissionGroupMember
        {
            PermissionGroupId = administratorsId,
            MemberKind = AccessSubjectKind.User,
            MemberId = accountId,
        });
        await db.SaveChangesAsync();

        var ctx = await RosterHelper.AccessContextOfAsync(db, accountId);
        ctx.IsFullAdmin.ShouldBeFalse();
        AccessEvaluator.Decide(ctx, AccessDomain.CavingGroups, AccessAction.Delete, null).Allowed.ShouldBeTrue();
        AccessEvaluator.Decide(ctx, AccessDomain.Cavers, AccessAction.Delete, null).Allowed.ShouldBeTrue();
        return await AuthHelper.BearerClientAsync(factory, $"rfa-{tag}-{suffix}@t.local");
    }

    private async Task<Guid> NewCavingGroupAsync(string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var group = new CavingGroup { Name = $"{name} {suffix}", Slug = $"rfa-{Guid.NewGuid():N}" };
        db.CavingGroups.Add(group);
        await db.SaveChangesAsync();
        return group.Id;
    }

    private async Task<Guid> NewAccountAsync(string tag) =>
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"rfa-{tag}-{suffix}@t.local");

    /// <summary>An account whose roster entry has been detached, so it is free to be linked.</summary>
    private async Task<Guid> AccountWithoutRosterEntryAsync(string tag)
    {
        var accountId = await NewAccountAsync(tag);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Cavers.Where(c => c.UserId == accountId).ExecuteUpdateAsync(c => c.SetProperty(x => x.UserId, (Guid?)null));
        return accountId;
    }

    private Task<Guid> RosterEntryOfAsync(Guid accountId) => RosterHelper.CaverIdForAsync(factory, accountId);

    private async Task<Guid> NewAccountlessCaverAsync(string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var caver = new Caver { FullName = $"{name} {suffix}" };
        db.Cavers.Add(caver);
        await db.SaveChangesAsync();
        return caver.Id;
    }

    private async Task<Guid> AccountlessBoardMemberAsync(string name)
    {
        var caverId = await NewAccountlessCaverAsync(name);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.CavingGroupMemberships.Add(new CavingGroupMembership
        {
            CavingGroupId = adminsCavingGroupId,
            CaverId = caverId,
            Role = CavingGroupRole.Member,
        });
        await db.SaveChangesAsync();
        return caverId;
    }

    private async Task<Guid> BoardMemberWithAccountAsync(Guid accountId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (await RosterHelper.AddMemberAsync(db, adminsCavingGroupId, accountId)).Id;
    }

    private async Task<bool> IsFullAdminAsync(Guid accountId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (await RosterHelper.AccessContextOfAsync(db, accountId)).IsFullAdmin;
    }

    private async Task<bool> IsBoardMemberAsync(Guid caverId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.CavingGroupMemberships.AnyAsync(m => m.CavingGroupId == adminsCavingGroupId && m.CaverId == caverId);
    }

    private async Task<bool> CaverExistsAsync(Guid caverId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Cavers.AnyAsync(c => c.Id == caverId);
    }

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, body);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().ShouldBe(code);
    }
}
