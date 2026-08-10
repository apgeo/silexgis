// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Exercises the real OIDC surface end-to-end: cookie login → authorize (code + PKCE) →
/// token exchange → bearer-protected API → refresh grant.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuthFlowTests : IDisposable
{
    private const string AdminEmail = "admin@test.local";
    private const string AdminPassword = "bootstrap-admin-pass-1";

    private readonly SilexGisApiFactory factory;
    private readonly string connectionString;

    public AuthFlowTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        factory = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?>
            {
                ["PublicUrl"] = "http://localhost",
                ["Auth:OpenRegistration"] = "true",
                ["Admin:Email"] = AdminEmail,
                ["Admin:Password"] = AdminPassword,
            });
    }

    private HttpClient CreateClient() => factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Api_requires_authentication_by_default()
    {
        var response = await CreateClient().GetAsync("/api/v1/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_with_wrong_password_fails()
    {
        var response = await CreateClient().PostAsJsonAsync(
            "/api/v1/auth/login", new { email = AdminEmail, password = "wrong-password" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Authorize_without_session_redirects_to_spa_login()
    {
        var response = await CreateClient().GetAsync(BuildAuthorizeUrl(CreatePkce().Challenge));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldStartWith("/login?returnUrl=");
    }

    [Fact]
    public async Task Full_code_pkce_flow_yields_tokens_that_work_and_refresh()
    {
        var client = CreateClient();

        // 1. Cookie session (bootstrap admin seeded at startup).
        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { email = AdminEmail, password = AdminPassword });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);

        // 2. Authorize → redirect back to the SPA callback with a code.
        var pkce = CreatePkce();
        var authorize = await client.GetAsync(BuildAuthorizeUrl(pkce.Challenge));
        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = authorize.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).ShouldBe("http://localhost/auth/callback");
        var code = QueryHelpers.ParseQuery(location.Query)["code"].ToString();
        code.ShouldNotBeNullOrWhiteSpace();

        // 3. Token exchange with the PKCE verifier.
        var tokens = await ExchangeAsync(client, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = "http://localhost/auth/callback",
            ["client_id"] = "silexgis-spa",
            ["code_verifier"] = pkce.Verifier,
        });

        // 4. Bearer token works against the protected API, and the bootstrap account's
        // full administration shows through the capabilities route (/me itself carries
        // identity only — what the caller may do lives in the access model).
        var me = await GetMeAsync(client, tokens.AccessToken);
        me.GetProperty("email").GetString().ShouldBe(AdminEmail);
        var capabilities = await GetJsonAsync(client, tokens.AccessToken, "/api/v1/me/capabilities");
        capabilities.GetProperty("domains").GetProperty("permissionGroups").GetString()!
            .ShouldContain("managePermissions");
        // The installation-wide role, stated on its own rather than implied by the domains above:
        // a few decisions turn on it directly and nothing else answers them.
        capabilities.GetProperty("isFullAdmin").GetBoolean().ShouldBeTrue();

        // 5. Refresh grant rotates the pair and the new access token works.
        var refreshed = await ExchangeAsync(client, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = tokens.RefreshToken,
            ["client_id"] = "silexgis-spa",
        });
        refreshed.AccessToken.ShouldNotBe(tokens.AccessToken);
        refreshed.RefreshToken.ShouldNotBe(tokens.RefreshToken);
        (await GetMeAsync(client, refreshed.AccessToken))
            .GetProperty("email").GetString().ShouldBe(AdminEmail);
    }

    [Fact]
    public async Task Register_creates_account_that_can_sign_in_when_enabled()
    {
        var client = CreateClient();
        var email = $"user-{Guid.NewGuid():N}@test.local";

        var register = await client.PostAsJsonAsync(
            "/api/v1/auth/register", new { email, password = "a-long-password-1", displayName = "Test User" });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);

        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { email, password = "a-long-password-1" });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Register_joins_the_configured_default_permission_groups()
    {
        using var configured = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?>
            {
                ["Auth:OpenRegistration"] = "true",
                // One real slug and one typo: a misconfigured name is warned about at
                // startup and skipped — it must never make registration itself fail.
                ["Auth:DefaultPermissionGroups"] = "editors, no-such-group",
            });

        var email = $"defgrp-{Guid.NewGuid():N}@test.local";
        var register = await configured.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email, password = "a-long-password-1", displayName = "Grouped User" });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var scope = configured.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();

        // The account came out of registration holding the Editors ruleset…
        var ctx = await RosterHelper.AccessContextOfAsync(db, userId);
        ctx.IsFullAdmin.ShouldBeFalse();
        ctx.Entries.ShouldContain(e =>
            e.Domain == AccessDomain.Features && (e.Actions & AccessAction.Create) != 0);

        // …and no Identity role: permission groups are the only capability carrier.
        (await db.UserRoles.AnyAsync(r => r.UserId == userId)).ShouldBeFalse();

        // The default default: a plain registration joins nothing beyond the implicit
        // All Users membership and the built-ins.
        var plainEmail = $"plain-{Guid.NewGuid():N}@test.local";
        (await CreateClient().PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email = plainEmail, password = "a-long-password-1", displayName = "Plain User" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        var plainId = await db.Users.Where(u => u.Email == plainEmail).Select(u => u.Id).SingleAsync();
        var plain = await RosterHelper.AccessContextOfAsync(db, plainId);
        plain.Entries.ShouldNotContain(e => e.Domain == AccessDomain.Features);
        plain.Entries.ShouldContain(e => e.Domain == AccessDomain.MapLayers);
    }

    [Fact]
    public async Task Register_is_forbidden_when_disabled()
    {
        using var closedFactory = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?> { ["Auth:OpenRegistration"] = "false" });

        var response = await closedFactory.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email = $"x-{Guid.NewGuid():N}@test.local", password = "a-long-password-1" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Password_forgot_always_accepts_and_reset_rejects_bad_tokens()
    {
        var client = CreateClient();

        var forgot = await client.PostAsJsonAsync(
            "/api/v1/auth/password/forgot", new { email = "nobody@test.local" });
        forgot.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var reset = await client.PostAsJsonAsync(
            "/api/v1/auth/password/reset",
            new { email = AdminEmail, token = "bogus", newPassword = "another-long-pass-1" });
        reset.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    public void Dispose() => factory.Dispose();

    private static string BuildAuthorizeUrl(string challenge) =>
        "/connect/authorize" +
        "?client_id=silexgis-spa" +
        "&redirect_uri=" + Uri.EscapeDataString("http://localhost/auth/callback") +
        "&response_type=code" +
        "&scope=" + Uri.EscapeDataString("openid profile email roles offline_access") +
        "&code_challenge=" + challenge +
        "&code_challenge_method=S256" +
        "&state=teststate";

    private static (string Verifier, string Challenge) CreatePkce()
    {
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    private static async Task<(string AccessToken, string RefreshToken)> ExchangeAsync(
        HttpClient client, Dictionary<string, string> form)
    {
        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(form));
        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload.ToString());
        return (payload.GetProperty("access_token").GetString()!,
                payload.GetProperty("refresh_token").GetString()!);
    }

    private static Task<JsonElement> GetMeAsync(HttpClient client, string accessToken) =>
        GetJsonAsync(client, accessToken, "/api/v1/me");

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string accessToken, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new("Bearer", accessToken);
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }
}
