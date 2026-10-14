// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Auth;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Api.Tests;

/// <summary>
/// External-login federation: the account-takeover rules (link only on verified email,
/// provision only when allowed) exercised directly against the service, plus the public
/// sign-in config endpoint and the linked-providers management surface. The provider's own
/// half of the redirect handshake is framework code and is stood in for; the callback after it,
/// and the second factor it must not skip, are driven for real.
/// </summary>
public sealed class ExternalAuthTests : IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string connectionString;

    public ExternalAuthTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        // One provider configured, and a stand-in for its handshake: the stand-in leaves exactly
        // what a real provider's handler leaves on return — Identity's external cookie naming the
        // provider and the account's key there — so the callback below it is the real one.
        factory = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?>
            {
                ["Auth:ExternalProviders:0:Name"] = "github",
                ["Auth:ExternalProviders:0:Type"] = "github",
                ["Auth:ExternalProviders:0:DisplayName"] = "GitHub",
                ["Auth:ExternalProviders:0:ClientId"] = "test-client",
                ["Auth:ExternalProviders:0:ClientSecret"] = "test-secret",
            },
            services => services.AddSingleton<IStartupFilter, ProviderReturnStandIn>());
    }

    [Fact]
    public async Task A_provider_sign_in_to_an_account_with_a_second_factor_waits_for_the_code()
    {
        var (email, sharedKey, _) = await TwoFactorAccountAsync("gh-2fa-1");
        using var client = Browser();

        var landed = await ReturnFromProviderAsync(client, "gh-2fa-1");

        // Sent to the code step, still carrying where the sign-in was going.
        landed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = landed.Headers.Location!.OriginalString;
        location.ShouldStartWith("/login?mfa=required");
        QueryHelpers.ParseQuery(new Uri("http://localhost" + location).Query)["returnUrl"].ToString()
            .ShouldBe(AuthHelper.AuthorizeUrl());

        // Not signed in: the authorize endpoint still sends the browser to the sign-in page.
        await ShouldHaveNoSessionAsync(client);

        // The pending sign-in names the account's methods, as the password path does.
        var asked = await client.PostAsJsonAsync("/api/v1/auth/2fa/verify", new { });
        asked.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var challenge = await asked.Content.ReadFromJsonAsync<JsonElement>();
        challenge.GetProperty("code").GetString().ShouldBe("auth.mfa_required");
        challenge.GetProperty("methods").EnumerateArray().Select(m => m.GetString()).ShouldBe(["authenticator"]);
        challenge.GetProperty("recoveryAccepted").GetBoolean().ShouldBeTrue();

        // A wrong code is refused and counted against the account.
        var wrong = await client.PostAsJsonAsync("/api/v1/auth/2fa/verify", new { code = "000000", method = "authenticator" });
        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await wrong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("auth.mfa_invalid");
        (await FailedAttemptsAsync(email)).ShouldBe(1);
        await ShouldHaveNoSessionAsync(client);

        // The right one signs in, clears the count, and the authorize flow now issues a code.
        var right = await client.PostAsJsonAsync(
            "/api/v1/auth/2fa/verify", new { code = TotpCodes.Generate(sharedKey), method = "authenticator" });
        right.StatusCode.ShouldBe(HttpStatusCode.OK, await right.Content.ReadAsStringAsync());
        (await right.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("email").GetString().ShouldBe(email);
        (await FailedAttemptsAsync(email)).ShouldBe(0);
        var authorize = await client.GetAsync(AuthHelper.AuthorizeUrl());
        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        authorize.Headers.Location!.ToString().ShouldContain("code=");
    }

    [Fact]
    public async Task A_recovery_code_or_an_emailed_code_completes_a_provider_sign_in()
    {
        var (email, _, recoveryCodes) = await TwoFactorAccountAsync("gh-2fa-2", alsoEmail: true);

        using (var viaRecovery = Browser())
        {
            await ReturnFromProviderAsync(viaRecovery, "gh-2fa-2");
            var done = await viaRecovery.PostAsJsonAsync(
                "/api/v1/auth/2fa/verify", new { code = $"recovery:{recoveryCodes[0]}" });
            done.StatusCode.ShouldBe(HttpStatusCode.OK, await done.Content.ReadAsStringAsync());
            (await viaRecovery.GetAsync(AuthHelper.AuthorizeUrl())).Headers.Location!.ToString().ShouldContain("code=");
        }

        // The same recovery code is spent.
        using (var reuse = Browser())
        {
            await ReturnFromProviderAsync(reuse, "gh-2fa-2");
            (await reuse.PostAsJsonAsync("/api/v1/auth/2fa/verify", new { code = $"recovery:{recoveryCodes[0]}" }))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var viaEmail = Browser();
        await ReturnFromProviderAsync(viaEmail, "gh-2fa-2");
        factory.Messages.Clear();
        (await viaEmail.PostAsJsonAsync("/api/v1/auth/2fa/send", new { method = "email" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var mailed = await viaEmail.PostAsJsonAsync(
            "/api/v1/auth/2fa/verify", new { code = factory.Messages.LastTo(email).Code, method = "email" });
        mailed.StatusCode.ShouldBe(HttpStatusCode.OK, await mailed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Repeated_wrong_codes_after_a_provider_sign_in_lock_the_account_as_after_a_password()
    {
        var (_, sharedKey, _) = await TwoFactorAccountAsync("gh-2fa-3");
        using var client = Browser();
        await ReturnFromProviderAsync(client, "gh-2fa-3");

        string? last = null;
        for (var i = 0; i < 5; i++)
        {
            var wrong = await client.PostAsJsonAsync("/api/v1/auth/2fa/verify", new { code = "000000" });
            last = (await wrong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
        }

        last.ShouldBe("auth.locked_out");

        // Locked means locked: even the right code is refused now.
        var right = await client.PostAsJsonAsync("/api/v1/auth/2fa/verify", new { code = TotpCodes.Generate(sharedKey) });
        (await right.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("auth.locked_out");

        // And a fresh provider return is turned away at the callback.
        using var again = Browser();
        var landed = await ReturnFromProviderAsync(again, "gh-2fa-3");
        landed.Headers.Location!.OriginalString.ShouldBe("/login?error=auth.locked_out");
    }

    [Fact]
    public async Task A_provider_sign_in_to_an_account_without_a_second_factor_signs_straight_in()
    {
        var email = Email();
        await LinkAsync(await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email), "gh-plain");
        using var client = Browser();

        var landed = await ReturnFromProviderAsync(client, "gh-plain");

        landed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        landed.Headers.Location!.OriginalString.ShouldBe(AuthHelper.AuthorizeUrl());
        (await client.GetAsync(AuthHelper.AuthorizeUrl())).Headers.Location!.ToString().ShouldContain("code=");
    }

    [Fact]
    public async Task Completing_a_sign_in_with_none_pending_is_refused()
    {
        using var client = Browser();

        var response = await client.PostAsJsonAsync("/api/v1/auth/2fa/verify", new { code = "123456" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe("auth.mfa_not_pending");
        await ShouldHaveNoSessionAsync(client);
    }

    [Fact]
    public async Task Federation_provisions_a_new_account_and_reuses_it_on_return()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ExternalAuthService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
        var email = Email();

        var first = await service.FederateAsync("google", "sub-1", Principal(email, verified: true, name: "Ada"), Provider(allowCreate: true));

        first.User.ShouldNotBeNull();
        first.ErrorCode.ShouldBeNull();
        first.User!.Email.ShouldBe(email);
        first.User.DisplayName.ShouldBe("Ada");
        // No Identity role any more: what an account may do lives in permission groups,
        // and with no configured registration default the account joins none explicitly.
        (await userManager.GetRolesAsync(first.User)).ShouldBeEmpty();

        // Same external identity → same local account (found by its stored login).
        var again = await service.FederateAsync("google", "sub-1", Principal(email, verified: true), Provider(allowCreate: true));
        again.User!.Id.ShouldBe(first.User.Id);
    }

    [Fact]
    public async Task Federation_links_to_an_existing_account_only_when_email_is_verified()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ExternalAuthService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
        var email = Email();
        var localId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);

        // Unverified provider email must NOT auto-link — that would be an account-takeover path.
        var unverified = await service.FederateAsync("github", "gh-1", Principal(email, verified: false), Provider(allowCreate: false));
        unverified.User.ShouldBeNull();
        unverified.ErrorCode.ShouldBe("auth.external_link_required");

        // Verified provider email links into the pre-existing account.
        var verified = await service.FederateAsync("github", "gh-1", Principal(email, verified: true), Provider(allowCreate: false));
        verified.User!.Id.ShouldBe(localId);

        var linked = await userManager.FindByLoginAsync("github", "gh-1");
        linked!.Id.ShouldBe(localId);
    }

    [Fact]
    public async Task Federation_refuses_to_provision_when_the_provider_disallows_it()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ExternalAuthService>();

        var result = await service.FederateAsync("google", "sub-x", Principal(Email(), verified: true), Provider(allowCreate: false));

        result.User.ShouldBeNull();
        result.ErrorCode.ShouldBe("auth.external_no_account");
    }

    [Fact]
    public async Task Federation_requires_a_verified_email_to_provision()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ExternalAuthService>();

        var noEmail = await service.FederateAsync("google", "sub-y", Principal(email: null, verified: true), Provider(allowCreate: true));
        noEmail.ErrorCode.ShouldBe("auth.external_no_email");

        var unverified = await service.FederateAsync("google", "sub-z", Principal(Email(), verified: false), Provider(allowCreate: true));
        unverified.ErrorCode.ShouldBe("auth.external_email_unverified");
    }

    [Fact]
    public async Task Auth_config_lists_configured_providers_and_the_external_only_flag()
    {
        using var configured = new SilexGisApiFactory(connectionString, new Dictionary<string, string?>
        {
            ["Auth:OpenRegistration"] = "true",
            ["Auth:ExternalOnly"] = "true",
            ["Auth:ExternalProviders:0:Name"] = "google",
            ["Auth:ExternalProviders:0:Type"] = "google",
            ["Auth:ExternalProviders:0:DisplayName"] = "Google",
            ["Auth:ExternalProviders:0:ClientId"] = "test-client",
            ["Auth:ExternalProviders:0:ClientSecret"] = "test-secret",
        });

        var config = await configured.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/auth/config");

        config.GetProperty("openRegistration").GetBoolean().ShouldBeTrue();
        config.GetProperty("externalOnly").GetBoolean().ShouldBeTrue();
        var providers = config.GetProperty("providers").EnumerateArray().ToList();
        providers.Count.ShouldBe(1);
        providers[0].GetProperty("name").GetString().ShouldBe("google");
        providers[0].GetProperty("displayName").GetString().ShouldBe("Google");
    }

    [Fact]
    public async Task Auth_config_never_reports_external_only_without_a_provider()
    {
        // The lockout guard: flipping ExternalOnly with no provider keeps the password form.
        using var stranded = new SilexGisApiFactory(connectionString, new Dictionary<string, string?>
        {
            ["Auth:ExternalOnly"] = "true",
        });

        var config = await stranded.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/auth/config");

        config.GetProperty("externalOnly").GetBoolean().ShouldBeFalse();
        config.GetProperty("providers").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Starting_an_unknown_provider_is_a_bad_request()
    {
        var response = await factory
            .CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            .GetAsync("/api/v1/auth/external/nope");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("auth.external_provider_unknown");
    }

    [Fact]
    public async Task Linked_providers_can_be_listed_and_removed()
    {
        var email = Email();
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);

        // Simulate a prior federation by storing the external login directly.
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
            var user = await userManager.FindByEmailAsync(email);
            (await userManager.AddLoginAsync(user!, new UserLoginInfo("github", "gh-42", "GitHub"))).Succeeded.ShouldBeTrue();
        }

        var client = await AuthHelper.BearerClientAsync(factory, email);

        var listed = await client.GetFromJsonAsync<JsonElement>("/api/v1/me/external-logins/");
        listed.GetProperty("hasPassword").GetBoolean().ShouldBeTrue();
        var logins = listed.GetProperty("logins").EnumerateArray().ToList();
        logins.Count.ShouldBe(1);
        logins[0].GetProperty("provider").GetString().ShouldBe("github");

        (await client.DeleteAsync("/api/v1/me/external-logins/github")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/me/external-logins/"))
            .GetProperty("logins").GetArrayLength().ShouldBe(0);

        (await client.DeleteAsync("/api/v1/me/external-logins/github")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    public void Dispose() => factory.Dispose();

    private static string Email() => $"ext-{Guid.NewGuid():N}@t.local";

    private HttpClient Browser() =>
        factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Returns from the provider as the browser would, then follows into the real callback.</summary>
    private static async Task<HttpResponseMessage> ReturnFromProviderAsync(HttpClient client, string providerKey)
    {
        var stood = await client.PostAsync(
            $"{ProviderReturnStandIn.Path}?key={Uri.EscapeDataString(providerKey)}"
            + $"&returnUrl={Uri.EscapeDataString(AuthHelper.AuthorizeUrl())}",
            null);
        stood.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return await client.GetAsync("/api/v1/auth/external/github/callback");
    }

    private static async Task ShouldHaveNoSessionAsync(HttpClient client)
    {
        var authorize = await client.GetAsync(AuthHelper.AuthorizeUrl());
        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        authorize.Headers.Location!.OriginalString.ShouldStartWith("/login?returnUrl=");
    }

    private async Task LinkAsync(Guid userId, string providerKey)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        (await userManager.AddLoginAsync(user!, new UserLoginInfo("github", providerKey, "GitHub"))).Succeeded.ShouldBeTrue();
    }

    /// <summary>
    /// An account linked to the stand-in provider with the authenticator switched on (and the
    /// emailed code too, when asked), set up through Identity as the enrolment endpoints leave it.
    /// </summary>
    private async Task<(string Email, string SharedKey, IReadOnlyList<string> RecoveryCodes)> TwoFactorAccountAsync(
        string providerKey, bool alsoEmail = false)
    {
        var email = Email();
        var id = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);
        await LinkAsync(id, providerKey);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
        var user = (await userManager.FindByIdAsync(id.ToString()))!;
        await userManager.ResetAuthenticatorKeyAsync(user);
        var key = (await userManager.GetAuthenticatorKeyAsync(user))!;
        user.TwoFactorAuthenticatorEnabled = true;
        user.TwoFactorEmailEnabled = alsoEmail;
        user.TwoFactorEnabled = true;
        (await userManager.UpdateAsync(user)).Succeeded.ShouldBeTrue();
        var codes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))!.ToList();
        return (email, key, codes);
    }

    private async Task<int> FailedAttemptsAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
        return (await userManager.FindByEmailAsync(email))!.AccessFailedCount;
    }

    /// <summary>
    /// Stands in for the provider's half of the handshake, which needs the provider itself: it
    /// signs the browser in to Identity's external cookie with the provider's name and the
    /// account's key there — what the provider's handler does before redirecting to the callback.
    /// </summary>
    private sealed class ProviderReturnStandIn : IStartupFilter
    {
        public const string Path = "/test/provider-return";

        // Matched by hand rather than with a mapped branch: a branch moves the path into the
        // request's base path, and the cookie would then be scoped to this path and never sent to
        // the callback.
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                if (!context.Request.Path.Equals(Path, StringComparison.Ordinal))
                {
                    await following(context);
                    return;
                }

                var key = context.Request.Query["key"].ToString();
                var principal = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, key), new Claim("email_verified", "true")], "github"));
                var properties = new AuthenticationProperties();
                properties.Items["LoginProvider"] = "github";
                properties.Items["returnUrl"] = context.Request.Query["returnUrl"].ToString();
                await context.SignInAsync(IdentityConstants.ExternalScheme, principal, properties);
                context.Response.StatusCode = StatusCodes.Status204NoContent;
            });
            next(app);
        };
    }

    private static ExternalProviderOptions Provider(bool allowCreate) => new()
    {
        Name = "github",
        Type = "github",
        DisplayName = "GitHub",
        ClientId = "id",
        ClientSecret = "secret",
        AllowCreate = allowCreate,
    };

    private static ClaimsPrincipal Principal(string? email, bool verified, string? name = null)
    {
        var claims = new List<Claim> { new("email_verified", verified ? "true" : "false") };
        if (email is not null)
        {
            claims.Add(new Claim(ClaimTypes.Email, email));
        }

        if (name is not null)
        {
            claims.Add(new Claim(ClaimTypes.Name, name));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
