// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Account administration: who exists, who can sign in, and the two things an operator has to be
/// able to do about it.
/// </summary>
/// <remarks>
/// <para>
/// The surface was published in the API contract and had never existed. An operator could not
/// remove a departed member's account or shut out a compromised one through the product at all,
/// and the seeded <c>Users</c> access domain was enforced nowhere — so the negative half of these
/// tests is not ceremony: before this, every one of them would have failed by answering 404 rather
/// than by refusing.
/// </para>
/// <para>
/// What is <em>not</em> here is the last-administrator guard firing, and that is stated beside the
/// tests rather than left to be discovered: both destructive verbs run inside its protocol, but
/// making it fire needs an installation with one administrator and this suite shares a database
/// with five. Writing it down is the point — a rule believed to be covered because a class about
/// the right subject exists is worse than one nobody claimed.
/// </para>
/// </remarks>
public sealed class UserAdministrationTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string tag = Guid.NewGuid().ToString("N")[..8];

    private HttpClient admin = null!;
    private HttpClient editor = null!;
    private Guid adminId;
    private Guid subjectId;
    private string subjectEmail = string.Empty;

    public UserAdministrationTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        adminId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"ua-adm-{tag}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"ua-ed-{tag}@t.local");

        subjectEmail = $"ua-sub-{tag}@t.local";
        subjectId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, subjectEmail);

        admin = await AuthHelper.BearerClientAsync(factory, $"ua-adm-{tag}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"ua-ed-{tag}@t.local");
    }

    /// <summary>
    /// The register lists accounts and says, for each, the one thing an operator is looking for:
    /// whether it can sign in.
    /// </summary>
    [Fact]
    public async Task The_register_lists_accounts_and_whether_each_can_sign_in()
    {
        var listed = await admin.GetFromJsonAsync<JsonElement>("/api/v1/users/");
        var subject = listed.EnumerateArray().Single(u => u.GetProperty("id").GetGuid() == subjectId);

        subject.GetProperty("email").GetString().ShouldBe(subjectEmail);
        subject.GetProperty("isLockedOut").GetBoolean().ShouldBeFalse();

        // The single read answers the same, so a screen that opened one account does not have to
        // disagree with the list it came from.
        var one = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/users/{subjectId}");
        one.GetProperty("email").GetString().ShouldBe(subjectEmail);
        one.GetProperty("isLockedOut").GetBoolean().ShouldBeFalse();

        (await admin.GetAsync($"/api/v1/users/{Guid.NewGuid()}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Locking shuts an account out and unlocking lets it back in, both visible on the register.
    /// </summary>
    [Fact]
    public async Task An_account_can_be_shut_out_and_let_back_in()
    {
        var locked = await admin.PutAsJsonAsync($"/api/v1/users/{subjectId}", new { locked = true });
        locked.StatusCode.ShouldBe(HttpStatusCode.OK, await locked.Content.ReadAsStringAsync());
        (await JsonOf(locked)).GetProperty("isLockedOut").GetBoolean().ShouldBeTrue();

        // Asserted at the store as well as at the response: the lock has to be written as the
        // lockout every other part of the application reads, not as a flag of this surface's own.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var row = await db.Users.AsNoTracking().SingleAsync(u => u.Id == subjectId);
            row.LockoutEnd.ShouldNotBeNull();
            row.LockoutEnd!.Value.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
        }

        var unlocked = await admin.PutAsJsonAsync($"/api/v1/users/{subjectId}", new { locked = false });
        unlocked.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonOf(unlocked)).GetProperty("isLockedOut").GetBoolean().ShouldBeFalse();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var row = await db.Users.AsNoTracking().SingleAsync(u => u.Id == subjectId);
            row.LockoutEnd.ShouldBeNull();

            // Letting somebody back in clears what shut them out, or the next mistyped password
            // locks them straight out again.
            row.AccessFailedCount.ShouldBe(0);
        }
    }

    /// <summary>An account can be removed, and the person it belonged to is not.</summary>
    /// <remarks>
    /// A club's roster outlives the logins attached to it: somebody who leaves takes their account
    /// with them, and the trips they were on keep naming them. So the directory entry survives the
    /// delete with its link cleared, rather than cascading away and taking its history with it.
    /// </remarks>
    [Fact]
    public async Task Deleting_an_account_leaves_the_person_behind()
    {
        var doomedEmail = $"ua-gone-{tag}@t.local";
        var doomedId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, doomedEmail);

        Guid caverId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var caver = await db.Cavers.FirstAsync(c => c.UserId == doomedId);
            caverId = caver.Id;
        }

        (await admin.DeleteAsync($"/api/v1/users/{doomedId}")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Users.AnyAsync(u => u.Id == doomedId)).ShouldBeFalse();

            var caver = await db.Cavers.AsNoTracking().SingleAsync(c => c.Id == caverId);
            caver.UserId.ShouldBeNull("the person outlives the account, with the link cleared");
        }

        (await admin.GetAsync($"/api/v1/users/{doomedId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Neither verb may be aimed at the caller's own account.
    /// </summary>
    /// <remarks>
    /// Separate from the last-administrator guard and not covered by it: an operator shutting
    /// themselves out while a colleague remains passes that guard and is still a mistake nobody
    /// asked to make, and there is no route back in from the other side of it.
    /// </remarks>
    [Fact]
    public async Task An_operator_cannot_lock_or_delete_their_own_account()
    {
        var lockedSelf = await admin.PutAsJsonAsync($"/api/v1/users/{adminId}", new { locked = true });
        lockedSelf.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOf(lockedSelf)).ShouldBe("user.cannot_lock_self");

        var deletedSelf = await admin.DeleteAsync($"/api/v1/users/{adminId}");
        deletedSelf.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOf(deletedSelf)).ShouldBe("user.cannot_delete_self");

        // Still there, and still able to act — a refusal that had half-applied would be worse than
        // the thing it refused.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var row = await db.Users.AsNoTracking().SingleAsync(u => u.Id == adminId);
        row.LockoutEnd.ShouldBeNull();
    }

    // The last-administrator guard is deliberately not driven from here, and the reason is a
    // property of the fixture rather than of the rule. Both destructive verbs run inside that
    // guard's protocol — stage, ask whether any full administrator can still sign in, roll back —
    // but firing it needs an installation with exactly one such account, and the suite shares one
    // database with five. Reducing it to one would mean locking the others, which is the seeded
    // administration every other class in the suite signs in through.
    //
    // Measured rather than assumed, because "it could not be tested" is the sentence a missing
    // test hides behind: a first draft of this class asserted its way down to one administrator
    // and its own fixture-proof caught the installation standing at five. The rule itself is
    // covered where it is defined, against the query that defines live membership. What is left
    // uncovered here is only that these two endpoints are wired into it, and that is the gap.

    /// <summary>
    /// The domain is enforced: an account without it is refused rather than answered.
    /// </summary>
    /// <remarks>
    /// This is the assertion the whole surface existed without. The <c>Users</c> domain was seeded
    /// and checked nowhere, so every one of these calls used to answer 404 — which reads as a
    /// refusal and is not one.
    /// </remarks>
    [Fact]
    public async Task An_account_without_the_users_domain_is_refused_on_every_verb()
    {
        (await editor.GetAsync("/api/v1/users/")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await editor.GetAsync($"/api/v1/users/{subjectId}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await editor.PutAsJsonAsync($"/api/v1/users/{subjectId}", new { locked = true }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await editor.DeleteAsync($"/api/v1/users/{subjectId}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // And nothing happened on the way past: a refusal that had already written would be the
        // worst of both.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var row = await db.Users.AsNoTracking().SingleAsync(u => u.Id == subjectId);
        row.LockoutEnd.ShouldBeNull();
    }

    /// <summary>An unauthenticated caller reaches none of it.</summary>
    [Fact]
    public async Task An_anonymous_caller_reaches_no_part_of_the_register()
    {
        var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/users/")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync($"/api/v1/users/{subjectId}")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var body = await JsonOf(response);
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        admin?.Dispose();
        editor?.Dispose();
        factory.Dispose();
    }
}
