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
using SilexGis.Infrastructure.Identity;

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
        return user.Id;
    }

    /// <summary>Returns a client whose default Authorization header carries a valid access token.</summary>
    public static async Task<HttpClient> BearerClientAsync(SilexGisApiFactory factory, string email)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK, $"login failed for {email}");

        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var authorizeUrl =
            "/connect/authorize?client_id=silexgis-spa" +
            "&redirect_uri=" + Uri.EscapeDataString("http://localhost/auth/callback") +
            "&response_type=code" +
            "&scope=" + Uri.EscapeDataString("openid profile email roles offline_access") +
            $"&code_challenge={challenge}&code_challenge_method=S256&state=s";

        var authorize = await client.GetAsync(authorizeUrl);
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
