// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Shouldly;
using SilexGis.Api.Auth;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Auth;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SilexGis.Api.Tests;

/// <summary>
/// An administrator switching off somebody else's second factor: the way back in for a member who
/// has lost both their authenticator and their recovery codes.
/// </summary>
/// <remarks>
/// <para>
/// The happy path is read back from the stores rather than from the answer, because what matters
/// is what is left behind: no method on, no key that an old authenticator app still matches, no
/// recovery code anybody wrote down, no session that can be renewed, one warning to the owner and
/// one line in the audit trail.
/// </para>
/// <para>
/// The second factors are switched on directly through Identity rather than through the enrolment
/// endpoints, which would need a code from an authenticator app or a delivered message for each.
/// The state those endpoints produce is exactly what is set here, and the endpoints themselves are
/// covered where they are defined.
/// </para>
/// </remarks>
public sealed class UserTwoFactorResetTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string DeviceRedirectUri = "http://127.0.0.1:54321/callback";

    private readonly SilexGisApiFactory factory;
    private readonly string tag = Guid.NewGuid().ToString("N")[..8];

    private HttpClient admin = null!;
    private HttpClient manager = null!;
    private HttpClient reader = null!;
    private Guid adminId;

    public UserTwoFactorResetTests(PostgresFixture postgres) =>
        // Every test here signs in several accounts from one address, against a per-address limit.
        factory = new SilexGisApiFactory(
            postgres.ConnectionString, new Dictionary<string, string?> { ["Auth:RateLimitPerMinute"] = "200" });

    public async Task InitializeAsync()
    {
        adminId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, Email("adm"));
        var managerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, Email("mgr"));
        var readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, Email("rd"));

        // Account rights handed out below full administration, as an installation that delegates
        // the register would: one account may change accounts, the other may only read them.
        await GrantUsersRightsAsync(managerId, AccessAction.Read | AccessAction.Write | AccessAction.Delete, "managers");
        await GrantUsersRightsAsync(readerId, AccessAction.Read, "readers");

        admin = await AuthHelper.BearerClientAsync(factory, Email("adm"));
        manager = await AuthHelper.BearerClientAsync(factory, Email("mgr"));
        reader = await AuthHelper.BearerClientAsync(factory, Email("rd"));
    }

    [Fact]
    public async Task A_reset_switches_every_method_off_ends_every_session_and_tells_the_owner_once()
    {
        var subjectId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, Email("sub"));

        // A session on a device, obtained before the second factor was on so that a password was
        // enough to get it — the credential a reset has to end.
        var device = await DeviceRefreshTokenAsync(Email("sub"));
        var oldKey = await SwitchEveryMethodOnAsync(subjectId);

        // The positive halves: the session renews, and the register says there is a second factor
        // to reset, so what follows is the reset's doing.
        var renewed = await RefreshAsync(device);
        renewed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stillLive = await RefreshTokenOf(renewed);
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/users/{subjectId}"))
            .GetProperty("twoFactorEnabled").GetBoolean().ShouldBeTrue();

        var reset = await admin.PostAsync($"/api/v1/users/{subjectId}/two-factor/reset", null);
        reset.StatusCode.ShouldBe(HttpStatusCode.OK, await reset.Content.ReadAsStringAsync());
        (await JsonOf(reset)).GetProperty("twoFactorEnabled").GetBoolean().ShouldBeFalse();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
            var user = await users.FindByIdAsync(subjectId.ToString());
            user.ShouldNotBeNull();
            user.TwoFactorAuthenticatorEnabled.ShouldBeFalse();
            user.TwoFactorEmailEnabled.ShouldBeFalse();
            user.TwoFactorSmsEnabled.ShouldBeFalse();
            user.PreferredTwoFactorMethod.ShouldBeNull();
            user.TwoFactorEnabled.ShouldBeFalse();
            (await users.CountRecoveryCodesAsync(user)).ShouldBe(0);
            (await users.GetAuthenticatorKeyAsync(user)).ShouldNotBe(oldKey);

            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Notifications.CountAsync(n => n.RecipientUserId == subjectId
                && n.TemplateKey == MessageTemplateCatalog.NotifySecurityTwoFactorDisabled)).ShouldBe(1);

            var audit = await db.AuditEntries.AsNoTracking()
                .Where(a => a.Action == AuditActions.TwoFactorReset && a.EntityId == subjectId.ToString())
                .ToListAsync();
            audit.Count.ShouldBe(1);
            audit[0].UserId.ShouldBe(adminId);
            audit[0].EntityType.ShouldBe(nameof(SilexGisUser));
        }

        // Every session ended: the renewed token no longer renews, and the store says why.
        (await RefreshAsync(stillLive)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SubjectTokenStatusesAsync(subjectId)).ShouldNotContain(Statuses.Valid);

        // And the owner is back in with their password alone, free to set a second factor up again.
        using var anonymous = factory.CreateClient();
        var login = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/login", new { email = Email("sub"), password = AuthHelper.Password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonOf(login)).GetProperty("userId").GetGuid().ShouldBe(subjectId);
    }

    [Fact]
    public async Task An_administrator_cannot_reset_their_own_second_factor()
    {
        await SwitchEveryMethodOnAsync(adminId);

        var reset = await admin.PostAsync($"/api/v1/users/{adminId}/two-factor/reset", null);
        reset.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOf(reset)).ShouldBe("user.cannot_reset_own_two_factor");
        (await TwoFactorOnAsync(adminId)).ShouldBeTrue();
    }

    [Fact]
    public async Task An_account_without_a_second_factor_has_nothing_to_reset()
    {
        var subjectId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, Email("none"));

        var reset = await admin.PostAsync($"/api/v1/users/{subjectId}/two-factor/reset", null);
        reset.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOf(reset)).ShouldBe("user.two_factor_not_enabled");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.AuditEntries.AnyAsync(a => a.Action == AuditActions.TwoFactorReset
            && a.EntityId == subjectId.ToString())).ShouldBeFalse();
    }

    /// <summary>
    /// A delegated right over accounts resets an ordinary member's second factor, and stops at a
    /// full administrator's.
    /// </summary>
    [Fact]
    public async Task Only_a_full_administrator_resets_a_full_administrators_second_factor()
    {
        var memberId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, Email("mem"));
        await SwitchEveryMethodOnAsync(memberId);
        var otherAdminId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, Email("adm2"));
        await SwitchEveryMethodOnAsync(otherAdminId);

        (await manager.PostAsync($"/api/v1/users/{memberId}/two-factor/reset", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TwoFactorOnAsync(memberId)).ShouldBeFalse();

        var refused = await manager.PostAsync($"/api/v1/users/{otherAdminId}/two-factor/reset", null);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeOf(refused)).ShouldBe(AccessEntryRules.ExceedsOwnRightsCode);
        (await TwoFactorOnAsync(otherAdminId)).ShouldBeTrue();

        // A full administrator may.
        (await admin.PostAsync($"/api/v1/users/{otherAdminId}/two-factor/reset", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TwoFactorOnAsync(otherAdminId)).ShouldBeFalse();
    }

    /// <summary>
    /// The same seat is out of reach for the other account actions: a delegated right over accounts
    /// that could lock, unlock or delete a full administrator could take the seat down, or put back
    /// somebody a full administrator shut out.
    /// </summary>
    [Fact]
    public async Task A_delegated_right_over_accounts_cannot_lock_unlock_or_delete_a_full_administrator()
    {
        var memberId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, Email("mem2"));
        var otherAdminId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, Email("adm3"));

        foreach (var locked in new[] { true, false })
        {
            var refused = await manager.PutAsJsonAsync($"/api/v1/users/{otherAdminId}", new { locked });
            refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await CodeOf(refused)).ShouldBe(AccessEntryRules.ExceedsOwnRightsCode);
        }

        // Locked by a full administrator, a delegated manager cannot lift it either.
        (await admin.PutAsJsonAsync($"/api/v1/users/{otherAdminId}", new { locked = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await manager.PutAsJsonAsync($"/api/v1/users/{otherAdminId}", new { locked = false }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await IsLockedAsync(otherAdminId)).ShouldBeTrue();

        var deleteRefused = await manager.DeleteAsync($"/api/v1/users/{otherAdminId}");
        deleteRefused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeOf(deleteRefused)).ShouldBe(AccessEntryRules.ExceedsOwnRightsCode);
        (await ExistsAsync(otherAdminId)).ShouldBeTrue();

        // An ordinary member is within reach.
        (await manager.PutAsJsonAsync($"/api/v1/users/{memberId}", new { locked = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await IsLockedAsync(memberId)).ShouldBeTrue();
        (await manager.DeleteAsync($"/api/v1/users/{memberId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ExistsAsync(memberId)).ShouldBeFalse();
    }

    [Fact]
    public async Task Reading_the_register_is_not_enough_and_an_anonymous_caller_reaches_nothing()
    {
        var subjectId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, Email("ro"));
        await SwitchEveryMethodOnAsync(subjectId);

        var refused = await reader.PostAsync($"/api/v1/users/{subjectId}/two-factor/reset", null);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeOf(refused)).ShouldBe("acl.forbidden");

        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsync($"/api/v1/users/{subjectId}/two-factor/reset", null))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await TwoFactorOnAsync(subjectId)).ShouldBeTrue();
        (await admin.PostAsync($"/api/v1/users/{Guid.NewGuid()}/two-factor/reset", null))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private string Email(string role) => $"utf-{role}-{tag}@t.local";

    private async Task GrantUsersRightsAsync(Guid userId, AccessAction actions, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var group = new PermissionGroup { Name = $"Account {name} {tag}", Slug = $"account-{name}-{tag}" };
        db.PermissionGroups.Add(group);
        db.AccessEntries.Add(new AccessEntry
        {
            PermissionGroupId = group.Id,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Users,
            Actions = actions,
            ScopeKind = AccessScopeKind.All,
        });
        db.PermissionGroupMembers.Add(new PermissionGroupMember
        {
            PermissionGroupId = group.Id,
            MemberKind = AccessSubjectKind.User,
            MemberId = userId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Switches all three methods on, with a preferred one and a set of recovery codes, and returns
    /// the authenticator key so a test can show it was replaced.
    /// </summary>
    private async Task<string> SwitchEveryMethodOnAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
        var user = await users.FindByIdAsync(userId.ToString());
        user.ShouldNotBeNull();

        (await users.ResetAuthenticatorKeyAsync(user)).Succeeded.ShouldBeTrue();
        // Numbers are unique across accounts, and every class shares one database server.
        user.PhoneNumber = "+4071" + RandomNumberGenerator.GetInt32(10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        user.PhoneNumberConfirmed = true;
        user.TwoFactorAuthenticatorEnabled = true;
        user.TwoFactorEmailEnabled = true;
        user.TwoFactorSmsEnabled = true;
        user.PreferredTwoFactorMethod = TwoFactorMethod.Authenticator;
        (await users.SetTwoFactorEnabledAsync(user, true)).Succeeded.ShouldBeTrue();
        (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10)).ShouldNotBeNull();
        (await users.CountRecoveryCodesAsync(user)).ShouldBe(10);

        return (await users.GetAuthenticatorKeyAsync(user))!;
    }

    private async Task<bool> IsLockedAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        return user.LockoutEnd > DateTimeOffset.UtcNow;
    }

    private async Task<bool> ExistsAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Users.AnyAsync(u => u.Id == userId);
    }

    private async Task<bool> TwoFactorOnAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        return user.TwoFactorEnabled && TwoFactorPolicy.AnyEnabled(user.ToTwoFactorState());
    }

    /// <summary>
    /// Signs in the way an installed app does — cookie login, authorize with PKCE, then the
    /// exchange — and returns the refresh token it was issued.
    /// </summary>
    private async Task<string> DeviceRefreshTokenAsync(string email)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = AuthHelper.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var authorize = await client.GetAsync(
            $"/connect/authorize?client_id={IdentitySeeder.SpeleoLocClientId}" +
            "&redirect_uri=" + Uri.EscapeDataString(DeviceRedirectUri) +
            "&response_type=code" +
            "&scope=" + Uri.EscapeDataString("openid profile email roles offline_access") +
            $"&code_challenge={challenge}&code_challenge_method=S256&state=s");
        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var code = QueryHelpers.ParseQuery(authorize.Headers.Location!.Query)["code"].ToString();

        var token = await client.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = DeviceRedirectUri,
                ["client_id"] = IdentitySeeder.SpeleoLocClientId,
                ["code_verifier"] = verifier,
            }));
        token.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await RefreshTokenOf(token);
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        factory.CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = IdentitySeeder.SpeleoLocClientId,
            }));

    private static async Task<string> RefreshTokenOf(HttpResponseMessage response)
    {
        var payload = await JsonOf(response);
        payload.TryGetProperty("refresh_token", out var refresh)
            .ShouldBeTrue("offline_access must yield a refresh token");
        return refresh.GetString()!;
    }

    private async Task<List<string>> SubjectTokenStatusesAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();

        var statuses = new List<string>();
        await foreach (var token in tokens.FindBySubjectAsync(userId.ToString()))
        {
            statuses.Add(await tokens.GetStatusAsync(token) ?? "(none)");
        }

        statuses.ShouldNotBeEmpty("this account has no stored tokens at all, so nothing was proved");
        return statuses;
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
        manager?.Dispose();
        reader?.Dispose();
        factory.Dispose();
    }
}
