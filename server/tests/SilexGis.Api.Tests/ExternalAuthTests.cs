// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
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
/// sign-in config endpoint and the linked-providers management surface. The provider
/// redirect handshake itself is framework code and is verified manually.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ExternalAuthTests : IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string connectionString;

    public ExternalAuthTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        factory = new SilexGisApiFactory(connectionString);
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
        (await userManager.IsInRoleAsync(first.User, GlobalRoles.Viewer)).ShouldBeTrue();

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
