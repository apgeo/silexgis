// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Administrators' writes that meet another writer on the same row. The account row is also written
/// whole by the sign-in machinery, each such write guarded by the concurrency stamp it read; an
/// administrator's change that left the stamp in place let a sign-in begun before it put back the
/// lock or the review mark it had just changed, and the other way round answered a bare 500.
/// Removing the same member twice at once answered a 500 as well.
/// </summary>
public sealed class AccountWriteRaceTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string connectionString;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient admin = null!;

    public AccountWriteRaceTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        factory = new SilexGisApiFactory(postgres.ConnectionString);
    }

    public async ValueTask InitializeAsync()
    {
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"awr-adm-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"awr-adm-{suffix}@t.local");
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
    public async Task A_sign_in_failure_begun_before_the_review_mark_cannot_put_the_mark_back()
    {
        var subjectId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"awr-rev-{suffix}@t.local");

        await using var signIn = factory.Services.CreateAsyncScope();
        var users = signIn.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
        var stale = await users.FindByIdAsync(subjectId.ToString());
        stale.ShouldNotBeNull();

        (await admin.PutAsJsonAsync($"/api/v1/users/{subjectId}/upload-review", new { uploadsNeedReview = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The sign-in's whole-row write of the copy it read before the mark was set.
        var failed = await users.AccessFailedAsync(stale);
        failed.Succeeded.ShouldBeFalse("a write of a copy read before the administrator's change must not land");

        (await ReadAsync(subjectId)).UploadsNeedReview.ShouldBeTrue();
    }

    [Fact]
    public async Task A_sign_in_failure_begun_before_a_lock_cannot_lift_it()
    {
        var subjectId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"awr-lock-{suffix}@t.local");

        await using var signIn = factory.Services.CreateAsyncScope();
        var users = signIn.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
        var stale = await users.FindByIdAsync(subjectId.ToString());
        stale.ShouldNotBeNull();

        (await admin.PutAsJsonAsync($"/api/v1/users/{subjectId}", new { locked = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await users.AccessFailedAsync(stale)).Succeeded.ShouldBeFalse();

        (await ReadAsync(subjectId)).LockoutEnd.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_lock_that_meets_an_account_row_written_since_it_was_read_is_refused_by_name()
    {
        var subjectId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"awr-race-{suffix}@t.local");

        var response = await AnotherWriterFirst.SendAsync(
            factory,
            $"UPDATE users SET concurrency_stamp = '{Guid.NewGuid()}' WHERE id = '{subjectId}'",
            () => admin.PutAsJsonAsync($"/api/v1/users/{subjectId}", new { locked = true }));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe("user.changed_meanwhile");
        (await ReadAsync(subjectId)).LockoutEnd.ShouldBeNull("a refused write changes nothing");

        // Read again, the same request goes through.
        (await admin.PutAsJsonAsync($"/api/v1/users/{subjectId}", new { locked = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Removing_a_trustee_another_removal_just_took_out_answers_as_done()
    {
        var subjectId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"awr-pg-{suffix}@t.local");
        var created = await admin.PostAsJsonAsync("/api/v1/permission-groups/", new { name = $"Race {suffix}", description = (string?)null });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var groupId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await admin.PostAsJsonAsync($"/api/v1/permission-groups/{groupId}/members", new { memberKind = "user", memberId = subjectId }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await AnotherWriterFirst.SendAsync(
            factory,
            $"DELETE FROM permission_group_members WHERE permission_group_id = '{groupId}' AND member_id = '{subjectId}'",
            () => admin.DeleteAsync($"/api/v1/permission-groups/{groupId}/members/user/{subjectId}"));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Removing_a_caving_group_member_another_removal_just_took_out_answers_as_done()
    {
        var subjectId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"awr-cg-{suffix}@t.local");
        var created = await admin.PostAsJsonAsync("/api/v1/caving-groups", new { name = $"Race club {suffix}" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var groupId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Guid caverId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caverId = (await RosterHelper.AddMemberAsync(db, groupId, subjectId)).Id;
        }

        var response = await AnotherWriterFirst.SendAsync(
            factory,
            $"DELETE FROM caving_group_memberships WHERE caving_group_id = '{groupId}' AND caver_id = '{caverId}'",
            () => admin.DeleteAsync($"/api/v1/caving-groups/{groupId}/members/{caverId}"));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Deleting_an_account_whose_row_was_written_since_it_was_read_is_refused_by_name()
    {
        var subjectId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"awr-del-{suffix}@t.local");

        var response = await AnotherWriterFirst.SendAsync(
            factory,
            $"UPDATE users SET concurrency_stamp = '{Guid.NewGuid()}' WHERE id = '{subjectId}'",
            () => admin.DeleteAsync($"/api/v1/users/{subjectId}"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe("user.changed_meanwhile");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Users.AnyAsync(u => u.Id == subjectId)).ShouldBeTrue("a refused delete removes nothing");
        }

        // Read again, the same request goes through.
        (await admin.DeleteAsync($"/api/v1/users/{subjectId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// An account an administrator creates arrives with its roster entry and its default groups or
    /// not at all. The two used to be saved apart, so a request that failed between them — the
    /// tab closed, a constraint refused — left an account nobody could repair by creating it again.
    /// </summary>
    [Fact]
    public async Task An_account_whose_roster_entry_fails_to_save_is_not_created_either()
    {
        var email = $"awr-tx-{suffix}@t.local";
        var name = $"Never Created {suffix}";
        var refuse = new RefuseTheRosterEntryOf(name);
        using var host = new SilexGisApiFactory(
            connectionString,
            configureServices: services =>
                services.ConfigureDbContext<SilexGisDbContext>(options => options.AddInterceptors(refuse)));
        using var hostAdmin = await AuthHelper.BearerClientAsync(host, $"awr-adm-{suffix}@t.local");

        try
        {
            using var response = await hostAdmin.PostAsJsonAsync("/api/v1/users/", new { email, displayName = name });
            response.IsSuccessStatusCode.ShouldBeFalse(await response.Content.ReadAsStringAsync());
        }
        catch (DbUpdateException)
        {
            // The test host may hand the refusal back as the exception itself; either way the
            // question is what was left behind.
        }

        refuse.Refused.ShouldBeTrue("the roster entry's save must have been reached and refused");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Users.AnyAsync(u => u.Email == email)).ShouldBeFalse("an account without its roster entry was left behind");
    }

    /// <summary>Fails the save that adds the roster entry of one name, as a failing constraint would.</summary>
    private sealed class RefuseTheRosterEntryOf(string fullName) : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public bool Refused { get; private set; }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<SilexGis.Domain.Entities.Caver>()
                    .Any(e => e.State == EntityState.Added && e.Entity.FullName == fullName) == true)
            {
                Refused = true;
                throw new DbUpdateException("Refused by the test, as a failing constraint would.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private async Task<SilexGisUser> ReadAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }
}
