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
/// Account administration cannot shut an installation out of its own administration: locking or
/// deleting the last full administrator who can still sign in is refused, and the same act aimed
/// at one administrator among two goes through.
/// </summary>
/// <remarks>
/// <para>
/// Driven over HTTP against the two account endpoints rather than against the guard itself, which
/// is covered where it is defined. What is proved here is that these two endpoints are wired into
/// it — that a lock or a delete is staged, judged and rolled back, not merely that the rule exists.
/// </para>
/// <para>
/// The caller is deliberately <em>not</em> a full administrator. Both verbs refuse to be aimed at
/// the caller's own account before the guard is asked, so an administrator acting on themselves
/// would be stopped by that rule and never reach this one. The caller therefore holds the account
/// rights through an ordinary ruleset, which is also the arrangement an installation that
/// delegates account administration to somebody below full administration would have.
/// </para>
/// <para>
/// The class owns its database, so it can rewrite the escape-hatch group's membership to exactly
/// the accounts each test names without taking away anything another class signs in through.
/// </para>
/// </remarks>
public sealed class UserAdministrationLastAdministratorTests
    : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string tag = Guid.NewGuid().ToString("N")[..8];

    private HttpClient manager = null!;

    public UserAdministrationLastAdministratorTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var managerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ula-mgr-{tag}@t.local");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var group = new PermissionGroup { Name = $"Account managers {tag}", Slug = $"account-managers-{tag}" };
            db.PermissionGroups.Add(group);
            db.AccessEntries.Add(new AccessEntry
            {
                PermissionGroupId = group.Id,
                Effect = AccessEffect.Allow,
                Domain = AccessDomain.Users,
                Actions = AccessAction.Read | AccessAction.Write | AccessAction.Delete | AccessAction.Create,
                ScopeKind = AccessScopeKind.All,
            });
            db.PermissionGroupMembers.Add(new PermissionGroupMember
            {
                PermissionGroupId = group.Id,
                MemberKind = AccessSubjectKind.User,
                MemberId = managerId,
            });
            await db.SaveChangesAsync();
        }

        manager = await AuthHelper.BearerClientAsync(factory, $"ula-mgr-{tag}@t.local");
    }

    [Fact]
    public async Task The_last_administrator_can_be_neither_locked_nor_deleted()
    {
        var soleId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ula-sole-{tag}@t.local");
        await MakeTheAdministratorsExactlyAsync(soleId);

        var locked = await manager.PutAsJsonAsync($"/api/v1/users/{soleId}", new { locked = true });
        locked.StatusCode.ShouldBe(HttpStatusCode.Conflict, await locked.Content.ReadAsStringAsync());
        (await CodeOf(locked)).ShouldBe(FullAdminGuard.LastFullAdminCode);

        var deleted = await manager.DeleteAsync($"/api/v1/users/{soleId}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.Conflict, await deleted.Content.ReadAsStringAsync());
        (await CodeOf(deleted)).ShouldBe(FullAdminGuard.LastFullAdminCode);

        // Both refusals rolled back what they had staged: the account is there, can sign in, is
        // still linked to its person, and is still the administrator.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var row = await db.Users.AsNoTracking().SingleAsync(u => u.Id == soleId);
        row.LockoutEnd.ShouldBeNull();
        (await db.Cavers.AsNoTracking().AnyAsync(c => c.UserId == soleId)).ShouldBeTrue();
        (await FullAdministrators.LiveMemberIdsAsync(db)).ShouldBe([soleId]);
    }

    [Fact]
    public async Task One_administrator_among_two_can_be_locked_and_deleted()
    {
        var firstId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ula-one-{tag}@t.local");
        var secondId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ula-two-{tag}@t.local");
        await MakeTheAdministratorsExactlyAsync(firstId, secondId);

        var locked = await manager.PutAsJsonAsync($"/api/v1/users/{firstId}", new { locked = true });
        locked.StatusCode.ShouldBe(HttpStatusCode.OK, await locked.Content.ReadAsStringAsync());

        // A locked administrator is not a live one, so the second is now the last — and the guard
        // counts it that way rather than by membership rows.
        var lockSecond = await manager.PutAsJsonAsync($"/api/v1/users/{secondId}", new { locked = true });
        lockSecond.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOf(lockSecond)).ShouldBe(FullAdminGuard.LastFullAdminCode);

        var unlocked = await manager.PutAsJsonAsync($"/api/v1/users/{firstId}", new { locked = false });
        unlocked.StatusCode.ShouldBe(HttpStatusCode.OK);

        var deleted = await manager.DeleteAsync($"/api/v1/users/{firstId}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Users.AnyAsync(u => u.Id == firstId)).ShouldBeFalse();
        (await FullAdministrators.LiveMemberIdsAsync(db)).ShouldBe([secondId]);

        // And with one left, the delete that just went through is refused for the survivor.
        var deleteLast = await manager.DeleteAsync($"/api/v1/users/{secondId}");
        deleteLast.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOf(deleteLast)).ShouldBe(FullAdminGuard.LastFullAdminCode);
    }

    /// <summary>
    /// Rewrites the escape-hatch group so that exactly these accounts hold it, by any path, and
    /// proves it took — a test that believed it had reduced the installation to one administrator
    /// while five remained would pass for the wrong reason.
    /// </summary>
    private async Task MakeTheAdministratorsExactlyAsync(params Guid[] userIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var fullAdminsId = (await FullAdministrators.GroupIdAsync(db))!.Value;

        await db.PermissionGroupMembers.Where(m => m.PermissionGroupId == fullAdminsId).ExecuteDeleteAsync();
        foreach (var userId in userIds)
        {
            db.PermissionGroupMembers.Add(new PermissionGroupMember
            {
                PermissionGroupId = fullAdminsId,
                MemberKind = AccessSubjectKind.User,
                MemberId = userId,
            });
        }

        await db.SaveChangesAsync();

        (await FullAdministrators.LiveMemberIdsAsync(db)).Order().ShouldBe(userIds.Order());
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        manager?.Dispose();
        factory.Dispose();
    }
}
