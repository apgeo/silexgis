// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SilexGis.Api.Tests;

/// <summary>
/// Registration confirmation, the anonymous confirm link, and the administrator's switch that
/// makes a confirmed address a condition of signing in.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class EmailVerificationTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    public EmailVerificationTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Auth:OpenRegistration"] = "true",
            ["Auth:RateLimitPerMinute"] = "200",
        });

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Registering_sends_a_confirmation_that_the_anonymous_link_completes()
    {
        using var client = factory.CreateClient();
        var email = Unique();

        var register = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = AuthHelper.Password,
            displayName = "New Caver",
        });
        register.StatusCode.ShouldBe(HttpStatusCode.Created, await register.Content.ReadAsStringAsync());
        var created = await register.Content.ReadFromJsonAsync<JsonElement>();
        created.GetProperty("confirmationSent").GetBoolean().ShouldBeTrue();
        created.GetProperty("confirmationRequired").GetBoolean().ShouldBeFalse();

        // The message really went to the new address, and carries a usable link.
        var message = factory.Messages.LastTo(email);
        message.Channel.ShouldBe("email");
        message.Subject!.ShouldContain("SilexGIS");
        message.Body.ShouldContain("New Caver");

        var query = QueryHelpers.ParseQuery(message.Link.Query);
        query["user"].ToString().ShouldNotBeNullOrWhiteSpace();

        (await UserAsync(email)).EmailConfirmed.ShouldBeFalse();

        var confirm = await client.PostAsJsonAsync("/api/v1/auth/email/confirm", new
        {
            userId = query["user"].ToString(),
            token = query["token"].ToString(),
        });
        confirm.StatusCode.ShouldBe(HttpStatusCode.NoContent, await confirm.Content.ReadAsStringAsync());
        (await UserAsync(email)).EmailConfirmed.ShouldBeTrue();
    }

    [Fact]
    public async Task Confirming_twice_succeeds_because_mail_clients_prefetch_links()
    {
        using var client = factory.CreateClient();
        var email = Unique();
        await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = AuthHelper.Password });

        var query = QueryHelpers.ParseQuery(factory.Messages.LastTo(email).Link.Query);
        var body = new { userId = query["user"].ToString(), token = query["token"].ToString() };

        (await client.PostAsJsonAsync("/api/v1/auth/email/confirm", body))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync("/api/v1/auth/email/confirm", body))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_tampered_confirmation_token_is_refused()
    {
        using var client = factory.CreateClient();
        var email = Unique();
        await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = AuthHelper.Password });
        var query = QueryHelpers.ParseQuery(factory.Messages.LastTo(email).Link.Query);

        var confirm = await client.PostAsJsonAsync("/api/v1/auth/email/confirm", new
        {
            userId = query["user"].ToString(),
            token = "not-the-token",
        });

        confirm.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await confirm.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("auth.confirm_invalid");
        (await UserAsync(email)).EmailConfirmed.ShouldBeFalse();
    }

    [Fact]
    public async Task Resending_never_says_whether_the_address_has_an_account()
    {
        using var client = factory.CreateClient();
        var known = Unique();
        await client.PostAsJsonAsync("/api/v1/auth/register", new { email = known, password = AuthHelper.Password });
        factory.Messages.Clear();

        (await client.PostAsJsonAsync("/api/v1/auth/email/resend", new { email = known }))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await client.PostAsJsonAsync("/api/v1/auth/email/resend", new { email = "nobody@t.local" }))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // Same answer either way; only one of them actually produced a message.
        factory.Messages.Messages.Count.ShouldBe(1);
        factory.Messages.Last.Recipient.ShouldBe(known);
    }

    [Fact]
    public async Task Enforcement_blocks_an_unconfirmed_sign_in_and_leaves_no_session()
    {
        using var client = factory.CreateClient();
        var email = Unique();
        await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = AuthHelper.Password });

        // Unconfirmed but permitted: the default policy lets them in.
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = AuthHelper.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await SetSecurityAsync(policy => policy with { RequireConfirmedEmail = true });

        using var fresh = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var refused = await fresh.PostAsJsonAsync("/api/v1/auth/login", new { email, password = AuthHelper.Password });
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await refused.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("auth.email_not_confirmed");

        // The refusal must not leave a usable cookie session behind. The cookie is what the
        // authorize endpoint consumes, so that is where a surviving one would show up as an
        // issued authorization code rather than a bounce back to the sign-in page.
        var authorize = await fresh.GetAsync(AuthHelper.AuthorizeUrl());
        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        // With a session it would be an absolute redirect back to the client carrying "code=";
        // without one the authorize endpoint bounces to the SPA sign-in page instead.
        var location = authorize.Headers.Location!.ToString();
        location.ShouldNotContain("code=");
        location.ShouldStartWith("/login");
    }

    [Fact]
    public async Task Enforcement_is_inert_while_no_mail_server_can_send_the_confirmation()
    {
        using var client = factory.CreateClient();
        var email = Unique();
        await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = AuthHelper.Password });
        await SetSecurityAsync(policy => policy with { RequireConfirmedEmail = true });

        factory.Messages.MailConfigured = false;
        try
        {
            // Otherwise switching the toggle on with no way to send the confirmation would shut
            // out every account that has no means of ever becoming confirmed.
            using var fresh = factory.CreateClient();
            (await fresh.PostAsJsonAsync("/api/v1/auth/login", new { email, password = AuthHelper.Password }))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            factory.Messages.MailConfigured = true;
        }
    }

    [Fact]
    public async Task A_wrong_password_on_an_unconfirmed_account_gives_nothing_away()
    {
        using var client = factory.CreateClient();
        var email = Unique();
        await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = AuthHelper.Password });
        await SetSecurityAsync(policy => policy with { RequireConfirmedEmail = true });

        using var fresh = factory.CreateClient();
        var refused = await fresh.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "wrong-password-9" });

        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        // "invalid_credentials", not "email_not_confirmed" — the confirmation state of an address
        // is only disclosed to someone who has already proved they hold the password.
        (await refused.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("auth.invalid_credentials");
    }

    [Fact]
    public async Task A_password_reset_arrives_as_a_link_that_carries_the_token()
    {
        using var client = factory.CreateClient();
        var email = Unique();
        await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = AuthHelper.Password });
        factory.Messages.Clear();

        (await client.PostAsJsonAsync("/api/v1/auth/password/forgot", new { email }))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var link = factory.Messages.LastTo(email).Link;
        link.AbsolutePath.ShouldBe("/reset-password");
        var query = QueryHelpers.ParseQuery(link.Query);

        var reset = await client.PostAsJsonAsync("/api/v1/auth/password/reset", new
        {
            email = query["email"].ToString(),
            token = query["token"].ToString(),
            newPassword = "a-brand-new-password-2",
        });
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent, await reset.Content.ReadAsStringAsync());

        using var fresh = factory.CreateClient();
        (await fresh.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "a-brand-new-password-2" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_session_that_predates_the_policy_stops_minting_tokens()
    {
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = Unique();
        await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = AuthHelper.Password });

        // Signed in while the policy still allowed it.
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = AuthHelper.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var before = await client.GetAsync(AuthHelper.AuthorizeUrl());
        before.Headers.Location!.ToString().ShouldContain("code=");

        await SetSecurityAsync(policy => policy with { RequireConfirmedEmail = true });

        // The cookie outlives the policy that allowed it, so the authorize endpoint — where every
        // token is actually minted — has to be the thing that stops.
        var after = await client.GetAsync(AuthHelper.AuthorizeUrl());
        after.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        after.Headers.Location!.ToString().ShouldStartWith("/login");
    }

    private static string Unique() => $"verify-{Guid.NewGuid():N}@t.local";

    private async Task<SilexGisUser> UserAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
        return (await userManager.FindByEmailAsync(email))!;
    }

    /// <summary>
    /// Writes the policy the way the admin endpoint does, so the cached copy is dropped and the
    /// next request sees it.
    /// </summary>
    private async Task SetSecurityAsync(Func<SecuritySettings, SecuritySettings> change)
    {
        using var scope = factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IAppSettingsService>();
        await settings.SaveAsync(AppSettingSections.Security, change(await settings.GetSecurityAsync()));
    }

    /// <summary>
    /// The settings live in the database, which is shared with every other test class in this
    /// collection — a policy left switched on here would silently change what they are testing.
    /// </summary>
    public async Task DisposeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.AppSettings.Where(s => s.Key == AppSettingSections.Security).ExecuteDeleteAsync();
    }

    public void Dispose() => factory.Dispose();
}
