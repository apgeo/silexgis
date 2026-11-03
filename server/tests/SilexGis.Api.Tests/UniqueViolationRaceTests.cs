// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Features.CavingGroups;
using SilexGis.Api.Features.Permissions;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The writes that check "is this taken?" and then save leave a window in which a second request
/// passes the same check; the unique index refuses the later save, and each such save answers
/// that refusal with the code the check itself gives. These tests open the window on purpose: a
/// second transaction writes the conflicting row and stays open, the request is sent, is seen
/// waiting on that row's index, and only then does the other transaction commit — so the request
/// is decided by the index and not by its own check, every time. A wrong index name in a catch,
/// or an exception shaped differently from what the catch expects, answers 500 here.
/// </summary>
public sealed class UniqueViolationRaceTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];
    private HttpClient admin = null!;

    public UniqueViolationRaceTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"uvr-adm-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"uvr-adm-{suffix}@t.local");
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        admin.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task Linking_an_account_another_request_is_linking_at_once_is_refused_with_a_code()
    {
        var accountId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"uvr-link-{suffix}@t.local");
        await WithDbAsync(db => db.Cavers.Where(c => c.UserId == accountId)
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.UserId, (Guid?)null)));
        var first = await NewCaverAsync("First");
        var second = await NewCaverAsync("Second");

        var response = await RaceAsync(
            other => other.Cavers.Where(c => c.Id == second)
                .ExecuteUpdateAsync(c => c.SetProperty(x => x.UserId, (Guid?)accountId)),
            () => admin.PostAsJsonAsync($"/api/v1/cavers/{first}/account-link", new { userId = accountId }));

        await ShouldAnswerAsync(response, HttpStatusCode.BadRequest, "caver.account_already_linked");
        (await WithDbAsync(db => db.Cavers.Where(c => c.Id == first).Select(c => c.UserId).SingleAsync()))
            .ShouldBeNull();
    }

    [Fact]
    public async Task Enrolling_somebody_another_request_is_enrolling_at_once_is_refused_with_a_code()
    {
        var groupId = await NewCavingGroupAsync("Enrol race");
        var caverId = await NewCaverAsync("Enrolled");

        var response = await RaceAsync(
            async other =>
            {
                other.CavingGroupMemberships.Add(new CavingGroupMembership
                {
                    CavingGroupId = groupId, CaverId = caverId, Role = CavingGroupRole.Admin,
                });
                await other.SaveChangesAsync();
            },
            () => admin.PostAsJsonAsync($"/api/v1/caving-groups/{groupId}/members", new { caverId, role = "member" }));

        await ShouldAnswerAsync(response, HttpStatusCode.Conflict, CavingGroupEndpoints.MemberRacedCode);
        (await WithDbAsync(db => db.CavingGroupMemberships
                .Where(m => m.CavingGroupId == groupId && m.CaverId == caverId).Select(m => m.Role).ToListAsync()))
            .ShouldBe([CavingGroupRole.Admin], "the role the other request saved stands");
    }

    [Fact]
    public async Task Creating_a_caving_group_another_request_is_creating_at_once_is_refused_with_a_code()
    {
        var name = $"Created at once {suffix}";
        var response = await RaceAsync(
            other => AddCavingGroupAsync(other, name),
            () => admin.PostAsJsonAsync("/api/v1/caving-groups", new
            {
                name, description = (string?)null, website = (string?)null,
            }));

        await ShouldAnswerAsync(response, HttpStatusCode.BadRequest, "caving_group.name_taken");
    }

    [Fact]
    public async Task Renaming_a_caving_group_onto_a_name_taken_at_once_is_refused_with_a_code()
    {
        var renamedId = await NewCavingGroupAsync("Renamed");
        var name = $"Renamed onto {suffix}";
        var response = await RaceAsync(
            other => AddCavingGroupAsync(other, name),
            () => admin.PutAsJsonAsync($"/api/v1/caving-groups/{renamedId}", new
            {
                name, description = (string?)null, website = (string?)null,
            }));

        await ShouldAnswerAsync(response, HttpStatusCode.BadRequest, "caving_group.name_taken");
    }

    [Fact]
    public async Task Creating_a_permission_group_another_request_is_creating_at_once_is_refused_with_a_code()
    {
        var name = $"Ruleset at once {suffix}";
        var response = await RaceAsync(
            other => AddPermissionGroupAsync(other, name),
            () => admin.PostAsJsonAsync("/api/v1/permission-groups", new { name, description = (string?)null }));

        await ShouldAnswerAsync(response, HttpStatusCode.BadRequest, PermissionGroupEndpoints.NameTakenCode);
    }

    [Fact]
    public async Task Renaming_a_permission_group_onto_a_name_taken_at_once_is_refused_with_a_code()
    {
        var renamedId = await WithDbAsync(db => AddPermissionGroupAsync(db, $"Renamed ruleset {suffix}"));
        var name = $"Ruleset renamed onto {suffix}";
        var response = await RaceAsync(
            other => AddPermissionGroupAsync(other, name),
            () => admin.PutAsJsonAsync($"/api/v1/permission-groups/{renamedId}", new { name }));

        await ShouldAnswerAsync(response, HttpStatusCode.BadRequest, PermissionGroupEndpoints.NameTakenCode);
    }

    /// <summary>
    /// Adding a trustee who is already there answers that the trustee is there, so the request
    /// that lost the race answers the same, and the trustee is listed once.
    /// </summary>
    [Fact]
    public async Task Adding_a_trustee_another_request_is_adding_at_once_answers_as_already_there()
    {
        var rulesetId = await WithDbAsync(db => AddPermissionGroupAsync(db, $"Trustee race {suffix}"));
        var trusteeId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"uvr-trustee-{suffix}@t.local");

        var response = await RaceAsync(
            async other =>
            {
                other.PermissionGroupMembers.Add(new PermissionGroupMember
                {
                    PermissionGroupId = rulesetId, MemberKind = AccessSubjectKind.User, MemberId = trusteeId,
                });
                await other.SaveChangesAsync();
            },
            () => admin.PostAsJsonAsync($"/api/v1/permission-groups/{rulesetId}/members",
                new { memberKind = "user", memberId = trusteeId }));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        (await WithDbAsync(db => db.PermissionGroupMembers.CountAsync(m => m.PermissionGroupId == rulesetId
            && m.MemberKind == AccessSubjectKind.User && m.MemberId == trusteeId))).ShouldBe(1);
    }

    // ---- the race ----

    /// <summary>
    /// Writes the conflicting row in a transaction of its own, sends the request while that
    /// transaction is open, waits until the request is seen blocked on the row, then commits.
    /// </summary>
    private async Task<HttpResponseMessage> RaceAsync(
        Func<SilexGisDbContext, Task> conflictingWrite, Func<Task<HttpResponseMessage>> send)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var other = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await using var transaction = await other.Database.BeginTransactionAsync();
        await conflictingWrite(other);

        var pending = send();
        var blocked = false;
        // Bounded by time, not by a count of polls: on a loaded machine running eight test
        // processes a request has been seen taking more than twenty seconds to reach its insert.
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline && !pending.IsCompleted)
        {
            var waiting = await other.Database.SqlQueryRaw<long>(
                "SELECT count(*) AS \"Value\" FROM pg_stat_activity "
                + "WHERE datname = current_database() AND wait_event_type = 'Lock' "
                + "AND pid <> pg_backend_pid()").SingleAsync();
            if (waiting > 0)
            {
                blocked = true;
                break;
            }

            await Task.Delay(50);
        }

        // Proof the request got past its own check and was stopped by the index — without it a
        // request that answered from its check (or failed before reaching it) would pass as well.
        blocked.ShouldBeTrue(pending.IsCompleted
            ? $"the request answered before reaching the index: {(int)(await pending).StatusCode}"
            : "the request was never seen waiting on the other transaction's row");

        await transaction.CommitAsync();
        return await pending;
    }

    private static Task AddCavingGroupAsync(SilexGisDbContext db, string name)
    {
        db.CavingGroups.Add(new CavingGroup { Name = name, Slug = Tag.Slugify(name) });
        return db.SaveChangesAsync();
    }

    private static async Task<Guid> AddPermissionGroupAsync(SilexGisDbContext db, string name)
    {
        var group = new PermissionGroup { Name = name, Slug = Tag.Slugify(name) };
        db.PermissionGroups.Add(group);
        await db.SaveChangesAsync();
        return group.Id;
    }

    private async Task<Guid> NewCavingGroupAsync(string name) =>
        await WithDbAsync(async db =>
        {
            var group = new CavingGroup { Name = $"{name} {suffix}", Slug = Tag.Slugify($"{name} {suffix}") };
            db.CavingGroups.Add(group);
            await db.SaveChangesAsync();
            return group.Id;
        });

    private async Task<Guid> NewCaverAsync(string name) =>
        await WithDbAsync(async db =>
        {
            var caver = new Caver { FullName = $"{name} {suffix}" };
            db.Cavers.Add(caver);
            await db.SaveChangesAsync();
            return caver.Id;
        });

    private async Task<T> WithDbAsync<T>(Func<SilexGisDbContext, Task<T>> work)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<SilexGisDbContext>());
    }

    private static async Task ShouldAnswerAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, body);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().ShouldBe(code);
    }
}
