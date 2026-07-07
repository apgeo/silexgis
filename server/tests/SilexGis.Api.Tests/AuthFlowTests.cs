// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Shouldly;
using SilexGis.Api.Tests.Support;

namespace SilexGis.Api.Tests;

/// <summary>
/// Exercises the real OIDC surface end-to-end: cookie login → authorize (code + PKCE) →
/// token exchange → bearer-protected API → refresh grant (05-auth-permissions.md §1).
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

        // 4. Bearer token works against the protected API.
        var me = await GetMeAsync(client, tokens.AccessToken);
        me.GetProperty("email").GetString().ShouldBe(AdminEmail);
        me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldContain("Admin");

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

    private static async Task<JsonElement> GetMeAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new("Bearer", accessToken);
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }
}
