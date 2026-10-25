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
using SilexGis.Domain.Auth;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Personal access keys: made, listed and revoked by their holder on the web, presented by a field
/// device as a bearer value, and kept to the routes that declare the key's purpose.
/// </summary>
/// <remarks>
/// The property everything here protects is that a key is never a whole-account credential: it
/// opens the few routes a device needs and is refused everywhere else, it cannot make or list keys,
/// and every way of taking an account's credentials back — revoking, changing or resetting the
/// password, locking the account — stops it on the next call. Each refusal is asserted beside the
/// positive case, so a refusal is the rule refusing and not a flow that never worked.
/// </remarks>
public sealed class AccessKeyTests(PostgresFixture postgres) : IDisposable, IClassFixture<PostgresFixture>
{
    private const string NewPassword = "integration-test-pass-2";

    private readonly SilexGisApiFactory factory = new(
        postgres.ConnectionString, new Dictionary<string, string?> { ["Auth:AccessKeyMaxDays"] = "400" });

    [Fact]
    public async Task A_key_opens_the_routes_that_declare_its_purpose_and_is_refused_on_every_other()
    {
        const string Email = "key-routes@test.local";
        var userId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, Email);
        var web = await AuthHelper.BearerClientAsync(factory, Email);
        var key = await AuthHelper.CreateAccessKeyAsync(web, "Pixel 7");
        var device = AuthHelper.AccessKeyClient(factory, key);

        // The routes a device needs: who it is acting as, and the cave search.
        var me = await device.GetAsync("/api/v1/me");
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await me.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid().ShouldBe(userId);
        (await device.GetAsync("/api/v1/caves")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Everything else that needs an account refuses the key with its own code — the same
        // routes answer the ordinary sign-in, so the refusal is the key's and not the route's.
        string[] refused = ["/api/v1/me/calendar-feeds", "/api/v1/me/access-keys"];
        foreach (var path in refused)
        {
            (await web.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.OK, path);
            var answer = await device.GetAsync(path);
            answer.StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
            (await CodeAsync(answer)).ShouldBe("auth.access_key_not_accepted", path);
        }

        // The sign-in server's own user-information route validates the bearer value itself,
        // before any route runs, and a key is not one of its tokens.
        (await web.GetAsync("/connect/userinfo")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await device.GetAsync("/connect/userinfo")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // A key cannot change the password, and cannot make another key.
        var password = await device.PutAsJsonAsync(
            "/api/v1/me/password", new { currentPassword = AuthHelper.Password, newPassword = NewPassword });
        password.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(password)).ShouldBe("auth.access_key_not_accepted");
        var mint = await device.PostAsJsonAsync("/api/v1/me/access-keys", new { name = "second" });
        mint.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(mint)).ShouldBe("auth.access_key_not_accepted");
        (await KeyCountAsync(userId)).ShouldBe(1);

        // A route that needs no account answers the device as it answers anybody.
        (await device.GetAsync("/api/v1/about")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_holder_sees_the_key_once_then_only_its_prefix_and_its_last_use()
    {
        const string Email = "key-list@test.local";
        var userId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, Email);
        var web = await AuthHelper.BearerClientAsync(factory, Email);

        var created = await web.PostAsJsonAsync("/api/v1/me/access-keys", new { name = "  Pixel 7  ", expiresInDays = 30 });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var secret = body.GetProperty("secret").GetString()!;
        secret.ShouldStartWith("sgx_");
        secret.Length.ShouldBe(AccessKeyText.Length);
        var row = body.GetProperty("key");
        row.GetProperty("name").GetString().ShouldBe("Pixel 7");
        row.GetProperty("prefix").GetString().ShouldBe(secret[..12]);
        row.GetProperty("purpose").GetString().ShouldBe("surveyUpload");
        row.GetProperty("state").GetString().ShouldBe("active");
        (row.GetProperty("expiresAt").GetDateTimeOffset() - row.GetProperty("createdAt").GetDateTimeOffset())
            .ShouldBe(TimeSpan.FromDays(30));

        // Nothing stored or listed afterwards holds the key itself.
        var list = await (await web.GetAsync("/api/v1/me/access-keys")).Content.ReadAsStringAsync();
        list.ShouldNotContain(secret);
        list.ShouldContain(secret[..12]);
        var listed = JsonDocument.Parse(list).RootElement;
        listed.GetProperty("maxDays").GetInt32().ShouldBe(400);
        listed.GetProperty("defaultDays").GetInt32().ShouldBe(365);
        listed.GetProperty("offeredDays").EnumerateArray().Select(d => d.GetInt32()).ShouldBe([30, 90, 365]);
        var only = listed.GetProperty("keys").EnumerateArray().Single();
        only.TryGetProperty("lastUsedAt", out var unused).ShouldBeTrue();
        unused.ValueKind.ShouldBe(JsonValueKind.Null);

        // A use is recorded, and a second use inside the minute does not write again.
        var device = AuthHelper.AccessKeyClient(factory, secret);
        (await device.GetAsync("/api/v1/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstUse = await LastUsedAsync(userId);
        firstUse.ShouldNotBeNull();
        (await device.GetAsync("/api/v1/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await LastUsedAsync(userId)).ShouldBe(firstUse);
    }

    [Fact]
    public async Task A_key_is_made_with_a_name_and_a_lifetime_inside_the_ceiling_and_only_by_a_signed_in_caller()
    {
        const string Email = "key-validation@test.local";
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, Email);
        var web = await AuthHelper.BearerClientAsync(factory, Email);

        var unnamed = await web.PostAsJsonAsync("/api/v1/me/access-keys", new { name = "   " });
        unnamed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(unnamed)).ShouldBe("validation.failed");

        var tooLong = await web.PostAsJsonAsync("/api/v1/me/access-keys", new { name = new string('a', 101) });
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        foreach (var days in new[] { 0, 401 })
        {
            var outside = await web.PostAsJsonAsync("/api/v1/me/access-keys", new { name = "phone", expiresInDays = days });
            outside.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await CodeAsync(outside)).ShouldBe("access_key.lifetime_invalid");
        }

        (await web.PostAsJsonAsync("/api/v1/me/access-keys", new { name = "phone", expiresInDays = 400 }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/v1/me/access-keys", new { name = "phone" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/v1/me/access-keys")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Each_account_sees_and_revokes_only_its_own_keys()
    {
        const string Owner = "key-owner@test.local";
        const string Other = "key-other@test.local";
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, Owner);
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, Other);
        var owner = await AuthHelper.BearerClientAsync(factory, Owner);
        var other = await AuthHelper.BearerClientAsync(factory, Other);
        var key = await AuthHelper.CreateAccessKeyAsync(owner);
        var id = await OnlyKeyIdAsync(owner);

        (await KeysOfAsync(other)).ShouldBeEmpty();
        var stranger = await other.DeleteAsync($"/api/v1/me/access-keys/{id}");
        stranger.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(stranger)).ShouldBe("access_key.not_found");

        var device = AuthHelper.AccessKeyClient(factory, key);
        (await device.GetAsync("/api/v1/me")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await owner.DeleteAsync($"/api/v1/me/access-keys/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.DeleteAsync($"/api/v1/me/access-keys/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await device.GetAsync("/api/v1/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await KeysOfAsync(owner)).Single().GetProperty("state").GetString().ShouldBe("revoked");
    }

    /// <summary>
    /// A key that does not open anything is refused exactly as a bad sign-in token is — status,
    /// challenge header and body code — whatever made it bad, so a guessed key cannot be told from
    /// a revoked one, nor a locked account's key from an unknown one.
    /// </summary>
    [Fact]
    public async Task An_unknown_revoked_expired_or_locked_out_key_is_refused_as_a_bad_bearer_token_is()
    {
        const string Email = "key-refusals@test.local";
        var userId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, Email);
        var web = await AuthHelper.BearerClientAsync(factory, Email);

        var badToken = await RefusalAsync("not-a-sign-in-token");
        badToken.Status.ShouldBe(HttpStatusCode.Unauthorized);
        badToken.Code.ShouldBe("auth.unauthenticated");

        (await RefusalAsync(AccessKeyText.Mint().Key)).ShouldBe(badToken);
        (await RefusalAsync("sgx_" + new string('a', 300))).ShouldBe(badToken);

        var revoked = await AuthHelper.CreateAccessKeyAsync(web, "revoked");
        var revokedId = (await KeysOfAsync(web)).Single(k => k.GetProperty("name").GetString() == "revoked")
            .GetProperty("id").GetGuid();
        (await AuthHelper.AccessKeyClient(factory, revoked).GetAsync("/api/v1/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await web.DeleteAsync($"/api/v1/me/access-keys/{revokedId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RefusalAsync(revoked)).ShouldBe(badToken);

        var expired = await AuthHelper.CreateAccessKeyAsync(web, "expired");
        (await AuthHelper.AccessKeyClient(factory, expired).GetAsync("/api/v1/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.ApiAccessKeys.Where(k => k.UserId == userId && k.Name == "expired")
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        (await RefusalAsync(expired)).ShouldBe(badToken);

        // Locking is how an administrator disables an account; the key is refused while the lock
        // holds and opens again when it is lifted, so the refusal is the lock's.
        var locked = await AuthHelper.CreateAccessKeyAsync(web, "locked");
        await SetLockAsync(Email, DateTimeOffset.UtcNow.AddHours(1));
        (await RefusalAsync(locked)).ShouldBe(badToken);
        await SetLockAsync(Email, null);
        (await AuthHelper.AccessKeyClient(factory, locked).GetAsync("/api/v1/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Changing_the_password_revokes_every_key_of_the_account()
    {
        const string Email = "key-password-change@test.local";
        var userId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, Email);
        var web = await AuthHelper.BearerClientAsync(factory, Email);
        var first = AuthHelper.AccessKeyClient(factory, await AuthHelper.CreateAccessKeyAsync(web, "first"));
        var second = AuthHelper.AccessKeyClient(factory, await AuthHelper.CreateAccessKeyAsync(web, "second"));
        (await first.GetAsync("/api/v1/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await second.GetAsync("/api/v1/me")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var change = await web.PutAsJsonAsync(
            "/api/v1/me/password", new { currentPassword = AuthHelper.Password, newPassword = NewPassword });
        change.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await first.GetAsync("/api/v1/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await second.GetAsync("/api/v1/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await RevokedCountAsync(userId)).ShouldBe(2);
    }

    [Fact]
    public async Task Resetting_the_password_revokes_every_key_of_the_account()
    {
        const string Email = "key-password-reset@test.local";
        var userId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, Email);
        var web = await AuthHelper.BearerClientAsync(factory, Email);
        var device = AuthHelper.AccessKeyClient(factory, await AuthHelper.CreateAccessKeyAsync(web));
        (await device.GetAsync("/api/v1/me")).StatusCode.ShouldBe(HttpStatusCode.OK);

        string token;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
            token = await users.GeneratePasswordResetTokenAsync((await users.FindByEmailAsync(Email))!);
        }

        var reset = await factory.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/password/reset", new { email = Email, token, newPassword = NewPassword });
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await device.GetAsync("/api/v1/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await RevokedCountAsync(userId)).ShouldBe(1);
    }

    private async Task<(HttpStatusCode Status, string? Code, string Challenge)> RefusalAsync(string bearer)
    {
        var response = await AuthHelper.AccessKeyClient(factory, bearer).GetAsync("/api/v1/me");
        return (response.StatusCode, await CodeAsync(response), response.Headers.WwwAuthenticate.ToString());
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return JsonDocument.Parse(text).RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static async Task<List<JsonElement>> KeysOfAsync(HttpClient web)
    {
        var list = await web.GetFromJsonAsync<JsonElement>("/api/v1/me/access-keys");
        return [.. list.GetProperty("keys").EnumerateArray()];
    }

    private static async Task<Guid> OnlyKeyIdAsync(HttpClient web) =>
        (await KeysOfAsync(web)).Single().GetProperty("id").GetGuid();

    private async Task<int> KeyCountAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ApiAccessKeys.CountAsync(k => k.UserId == userId);
    }

    private async Task<int> RevokedCountAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ApiAccessKeys.CountAsync(k => k.UserId == userId && k.RevokedAt != null);
    }

    private async Task<DateTimeOffset?> LastUsedAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ApiAccessKeys.Where(k => k.UserId == userId).Select(k => k.LastUsedAt).SingleAsync();
    }

    private async Task SetLockAsync(string email, DateTimeOffset? until)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
        var user = await users.FindByEmailAsync(email);
        user.ShouldNotBeNull();
        (await users.SetLockoutEnabledAsync(user, true)).Succeeded.ShouldBeTrue();
        (await users.SetLockoutEndDateAsync(user, until)).Succeeded.ShouldBeTrue();
    }

    public void Dispose() => factory.Dispose();
}
