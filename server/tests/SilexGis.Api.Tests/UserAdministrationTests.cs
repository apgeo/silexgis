// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
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
/// The last-administrator guard firing is driven by a class of its own, which can shrink its own
/// installation to a single administrator without taking away the accounts these tests sign in
/// through.
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
        var listed = await admin.GetFromJsonAsync<JsonElement>("/api/v1/users/?pageSize=500");
        var subject = listed.GetProperty("items").EnumerateArray()
            .Single(u => u.GetProperty("id").GetGuid() == subjectId);

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
    /// The single read names the permission groups an account belongs to, and for each one whether
    /// the account is named itself or reaches it through a caving group of its roster entry.
    /// </summary>
    [Fact]
    public async Task The_single_read_names_each_permission_group_and_the_paths_to_it()
    {
        var direct = new PermissionGroup { Name = $"Direct {tag}", Slug = $"ua-direct-{tag}" };
        var throughClub = new PermissionGroup { Name = $"Through the club {tag}", Slug = $"ua-club-{tag}" };
        var club = new CavingGroup { Name = $"Club {tag}", Slug = $"ua-club-{tag}" };

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.PermissionGroups.AddRange(direct, throughClub);
            db.CavingGroups.Add(club);
            var caverId = await db.Cavers.Where(c => c.UserId == subjectId).Select(c => c.Id).SingleAsync();
            db.CavingGroupMemberships.Add(new CavingGroupMembership { CaverId = caverId, CavingGroupId = club.Id });
            db.PermissionGroupMembers.AddRange(
                new PermissionGroupMember
                {
                    PermissionGroupId = direct.Id, MemberKind = AccessSubjectKind.User, MemberId = subjectId,
                },
                new PermissionGroupMember
                {
                    PermissionGroupId = throughClub.Id, MemberKind = AccessSubjectKind.CavingGroup, MemberId = club.Id,
                });
            await db.SaveChangesAsync();
        }

        var one = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/users/{subjectId}");
        var groups = one.GetProperty("permissionGroups").EnumerateArray().ToList();

        var named = groups.Single(g => g.GetProperty("id").GetGuid() == direct.Id);
        named.GetProperty("name").GetString().ShouldBe(direct.Name);
        named.GetProperty("direct").GetBoolean().ShouldBeTrue();
        named.GetProperty("through").GetArrayLength().ShouldBe(0);

        var reached = groups.Single(g => g.GetProperty("id").GetGuid() == throughClub.Id);
        reached.GetProperty("direct").GetBoolean().ShouldBeFalse();
        reached.GetProperty("through").EnumerateArray()
            .Select(c => (c.GetProperty("id").GetGuid(), c.GetProperty("name").GetString()))
            .ShouldBe([(club.Id, club.Name)]);

        // A group the account is in by no path is not listed.
        var admins = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/users/{adminId}");
        admins.GetProperty("permissionGroups").EnumerateArray()
            .Select(g => g.GetProperty("id").GetGuid())
            .ShouldNotContain(direct.Id);
    }

    /// <summary>
    /// The register is searched by any name an account is known by, accent-blind, and read a page
    /// at a time with the total beside it.
    /// </summary>
    [Fact]
    public async Task The_register_is_searched_and_paged()
    {
        var invited = await admin.PostAsJsonAsync("/api/v1/users/", new
        {
            email = $"ua-search-{tag}@t.local",
            displayName = $"Ştefan Ciobanu {tag}",
        });
        invited.StatusCode.ShouldBe(HttpStatusCode.Created, await invited.Content.ReadAsStringAsync());

        // Typed without the diacritic, as somebody on a keyboard without it would.
        var byName = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/users/?q=stefan ciobanu {tag}");
        byName.GetProperty("totalItems").GetInt32().ShouldBe(1);
        byName.GetProperty("items")[0].GetProperty("email").GetString().ShouldBe($"ua-search-{tag}@t.local");

        var byAddress = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/users/?q=ua-sub-{tag}");
        byAddress.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("id").GetGuid())
            .ShouldBe([subjectId]);

        // Every account of this class carries the tag; one per page leaves the rest for later
        // pages and says how many there are in all.
        var paged = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/users/?q={tag}&page=2&pageSize=1");
        paged.GetProperty("items").GetArrayLength().ShouldBe(1);
        paged.GetProperty("page").GetInt32().ShouldBe(2);
        paged.GetProperty("totalItems").GetInt32().ShouldBeGreaterThanOrEqualTo(4);
    }

    /// <summary>
    /// An administrator opens an account on an installation that does not let people register,
    /// the invitation is mailed, and its link is what lets the person in.
    /// </summary>
    [Fact]
    public async Task An_invited_account_is_entered_through_the_mailed_link()
    {
        var email = $"ua-invite-{tag}@t.local";
        var created = await admin.PostAsJsonAsync("/api/v1/users/", new { email, displayName = "Ana Invitata" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        var body = await JsonOf(created);
        body.GetProperty("invitationSent").GetBoolean().ShouldBeTrue();
        // Mailed, so not also handed back: the link exists in exactly one place.
        body.GetProperty("setPasswordUrl").ValueKind.ShouldBe(JsonValueKind.Null);
        var user = body.GetProperty("user");
        user.GetProperty("email").GetString().ShouldBe(email);
        user.GetProperty("hasPassword").GetBoolean().ShouldBeFalse();
        var id = user.GetProperty("id").GetGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var row = await db.Users.AsNoTracking().SingleAsync(u => u.Id == id);
            row.PasswordHash.ShouldBeNull("nobody but the person ever knows their password");
            row.DisplayName.ShouldBe("Ana Invitata");

            // Made as registration makes an account: the person is on the roster from the start.
            (await db.Cavers.AsNoTracking().AnyAsync(c => c.UserId == id)).ShouldBeTrue();
        }

        var message = factory.Messages.Last;
        message.Recipient.ShouldBe(email);
        message.Body.ShouldContain(email);
        message.Link.AbsolutePath.ShouldBe("/reset-password");

        await ChooseFirstPasswordAsync(message.Link, "a-first-password-1");
        using var invitee = await AuthHelper.BearerClientAsync(factory, email, "a-first-password-1");
        (await invitee.GetAsync("/api/v1/me")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // In, so a second invitation would now be a way into somebody else's account.
        var again = await admin.PostAsync($"/api/v1/users/{id}/invitation", null);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOf(again)).ShouldBe("user.already_enterable");
    }

    /// <summary>
    /// Without a mail server the link comes back to the administrator, once, to hand over — and it
    /// works exactly as the mailed one does.
    /// </summary>
    [Fact]
    public async Task Without_mail_the_link_is_handed_to_the_administrator()
    {
        factory.Messages.MailConfigured = false;
        var email = $"ua-handover-{tag}@t.local";

        var created = await admin.PostAsJsonAsync("/api/v1/users/", new { email, displayName = "Ion Predat" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var body = await JsonOf(created);
        body.GetProperty("invitationSent").GetBoolean().ShouldBeFalse();
        body.GetProperty("expiresHours").GetInt32().ShouldBeGreaterThan(0);
        var link = new Uri(body.GetProperty("setPasswordUrl").GetString()!);
        QueryHelpers.ParseQuery(link.Query)["invite"].ToString().ShouldBe("1");

        // The first link was lost; a second one replaces it, and the account still has no password.
        var id = body.GetProperty("user").GetProperty("id").GetGuid();
        var again = await admin.PostAsync($"/api/v1/users/{id}/invitation", null);
        again.StatusCode.ShouldBe(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
        var second = new Uri((await JsonOf(again)).GetProperty("setPasswordUrl").GetString()!);

        await ChooseFirstPasswordAsync(second, "handed-over-pass-1");
        using var invitee = await AuthHelper.BearerClientAsync(factory, email, "handed-over-pass-1");
        (await invitee.GetAsync("/api/v1/me")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // And the register now says the account has been entered.
        var one = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/users/{id}");
        one.GetProperty("hasPassword").GetBoolean().ShouldBeTrue();
    }

    /// <summary>
    /// The address rules are registration's: one account per address whatever its case, a
    /// malformed address refused before anything is written, and the refusal named.
    /// </summary>
    [Fact]
    public async Task An_invitation_follows_the_address_rules_of_registration()
    {
        var taken = await admin.PostAsJsonAsync(
            "/api/v1/users/", new { email = subjectEmail.ToUpperInvariant(), displayName = "Dublura" });
        taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOf(taken)).ShouldBe("user.email_taken");

        var malformed = await admin.PostAsJsonAsync("/api/v1/users/", new { email = "not-an-address", displayName = "X" });
        malformed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOf(malformed)).ShouldBe("validation.failed");
        (await JsonOf(malformed)).GetProperty("errors").TryGetProperty("Email", out _).ShouldBeTrue();

        var nameless = await admin.PostAsJsonAsync("/api/v1/users/", new { email = $"ua-nameless-{tag}@t.local", displayName = "  " });
        nameless.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await JsonOf(nameless)).GetProperty("errors").TryGetProperty("DisplayName", out _).ShouldBeTrue();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Users.CountAsync(u => u.NormalizedEmail == subjectEmail.ToUpperInvariant())).ShouldBe(1);
        (await db.Users.AnyAsync(u => u.Email == $"ua-nameless-{tag}@t.local")).ShouldBeFalse();
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
        (await editor.PostAsync($"/api/v1/users/{subjectId}/invitation", null)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        var created = await editor.PostAsJsonAsync(
            "/api/v1/users/", new { email = $"ua-ed-made-{tag}@t.local", displayName = "Nu" });
        created.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeOf(created)).ShouldBe("access.create_forbidden");

        // And nothing happened on the way past: a refusal that had already written would be the
        // worst of both.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var row = await db.Users.AsNoTracking().SingleAsync(u => u.Id == subjectId);
        row.LockoutEnd.ShouldBeNull();
        (await db.Users.AnyAsync(u => u.Email == $"ua-ed-made-{tag}@t.local")).ShouldBeFalse();
    }

    /// <summary>An unauthenticated caller reaches none of it.</summary>
    [Fact]
    public async Task An_anonymous_caller_reaches_no_part_of_the_register()
    {
        var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/users/")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync($"/api/v1/users/{subjectId}")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/api/v1/users/", new { email = $"ua-anon-{tag}@t.local", displayName = "A" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync($"/api/v1/users/{subjectId}/invitation", null)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>Completes the set-password page's request with the token a link carries.</summary>
    private async Task ChooseFirstPasswordAsync(Uri link, string password)
    {
        var query = QueryHelpers.ParseQuery(link.Query);
        using var anonymous = factory.CreateClient();
        var reset = await anonymous.PostAsJsonAsync("/api/v1/auth/password/reset", new
        {
            email = query["email"].ToString(),
            token = query["token"].ToString(),
            newPassword = password,
        });
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent, await reset.Content.ReadAsStringAsync());
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
