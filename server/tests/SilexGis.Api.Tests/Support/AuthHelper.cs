// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Test users and bearer clients: creates accounts directly via Identity, then obtains
/// tokens through the real code+PKCE flow (no shortcuts around the auth stack).
/// </summary>
public static class AuthHelper
{
    public const string Password = "integration-test-pass-1";

    public static async Task<Guid> CreateUserAsync(SilexGisApiFactory factory, string role, string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
        var user = new SilexGisUser { UserName = email, Email = email, EmailConfirmed = true };
        (await userManager.CreateAsync(user, Password)).Succeeded.ShouldBeTrue($"user create failed for {email}");
        (await userManager.AddToRoleAsync(user, role)).Succeeded.ShouldBeTrue($"role assign failed for {email}");

        // Registration gives every account a roster entry, so accounts made the short way here
        // must have one too — otherwise tests would exercise a state the application never
        // produces, and membership (which hangs off the person) would have nothing to attach to.
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        CaverDirectory.CreateForNewAccount(db, user.Id, user.DisplayName, user.UserName, user.Email);

        // Roles carry their capabilities through the seeded permission groups now; an
        // account minted after startup seeding joins the same groups the seed-time
        // mapping would have put it in (Admin → Full Administrators, Editor → Editors…).
        await PermissionGroupSeeder.EnsureRoleMembershipsAsync(db, user.Id, role);
        await db.SaveChangesAsync();

        return user.Id;
    }

    /// <summary>
    /// The authorize URL the SPA uses. Exposed because it is also how a test checks whether a
    /// cookie session exists at all — that endpoint is the only thing that consumes one.
    /// </summary>
    public static string AuthorizeUrl(string? codeChallenge = null) =>
        "/connect/authorize?client_id=silexgis-spa" +
        "&redirect_uri=" + Uri.EscapeDataString("http://localhost/auth/callback") +
        "&response_type=code" +
        "&scope=" + Uri.EscapeDataString("openid profile email roles offline_access") +
        $"&code_challenge={codeChallenge ?? "x"}&code_challenge_method=S256&state=s";

    /// <summary>Returns a client whose default Authorization header carries a valid access token.</summary>
    public static async Task<HttpClient> BearerClientAsync(SilexGisApiFactory factory, string email)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK, $"login failed for {email}");

        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        var authorize = await client.GetAsync(AuthorizeUrl(challenge));
        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var code = QueryHelpers.ParseQuery(authorize.Headers.Location!.Query)["code"].ToString();

        var tokenResponse = await client.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = "http://localhost/auth/callback",
                ["client_id"] = "silexgis-spa",
                ["code_verifier"] = verifier,
            }));
        var payload = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync()).RootElement;
        tokenResponse.StatusCode.ShouldBe(HttpStatusCode.OK, payload.ToString());

        client.DefaultRequestHeaders.Authorization = new(
            "Bearer", payload.GetProperty("access_token").GetString());
        return client;
    }
}
