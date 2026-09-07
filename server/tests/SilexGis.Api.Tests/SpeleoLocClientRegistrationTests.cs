// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Shouldly;
using SilexGis.Api.Auth;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SilexGis.Api.Tests;

/// <summary>
/// The mobile client's registration: the redirect forms an installed app can be handed a code
/// through, and the refresh-token window it carries in its own right rather than by widening the
/// one every client shares.
/// </summary>
/// <remarks>
/// Every assertion here reads the value the seeder actually wrote, never a value this class fed in
/// through configuration. The client registrations live in one table that every test factory in the
/// run re-seeds on startup, so a factory built with an overridden lifetime would hand its figure to
/// whichever class started next — and a test that overrode the setting would pass while the shipped
/// default was wrong, which is the only failure worth catching here.
/// </remarks>
public sealed class SpeleoLocClientRegistrationTests : IDisposable, IClassFixture<PostgresFixture>
{
    private const string CaverEmail = "speleoloc-device@test.local";

    private readonly SilexGisApiFactory factory;

    public SpeleoLocClientRegistrationTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                // The auth surface is rate-limited per IP and this class makes several calls
                // against it from one address.
                ["Auth:RateLimitPerMinute"] = "200",
            });

    [Theory]
    [InlineData(45, 45)]
    [InlineData(0, 1)]
    [InlineData(-30, 1)]
    [InlineData(4500, 365)]
    [InlineData(365, 365)]
    public void A_mistyped_refresh_window_is_brought_back_inside_one_day_to_one_year(
        int configured, int expectedDays)
    {
        // A slipped digit must not mint a credential lasting years, and a zero must not mean
        // "expires immediately" either — both ends are brought back rather than refused, so a
        // typo cannot stop an installation from starting.
        var options = new AuthOptions { SpeleoLocRefreshTokenDays = configured };

        options.SpeleoLocRefreshTokenLifetime.ShouldBe(TimeSpan.FromDays(expectedDays));
    }

    [Fact]
    public async Task Both_redirect_forms_an_installed_app_can_use_are_registered()
    {
        using var scope = factory.Services.CreateScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        var app = await applications.FindByClientIdAsync(IdentitySeeder.SpeleoLocClientId);
        app.ShouldNotBeNull("the mobile client is not registered at all");

        (await applications.GetClientTypeAsync(app)).ShouldBe(ClientTypes.Public);
        (await applications.GetApplicationTypeAsync(app)).ShouldBe(ApplicationTypes.Native);
        (await applications.GetRequirementsAsync(app))
            .ShouldContain(Requirements.Features.ProofKeyForCodeExchange);

        // A native client binds an ephemeral loopback port at the moment it starts a sign-in, so
        // the registration cannot name the port and the comparison must not care about it. This is
        // asserted directly rather than inferred from the stored string, because what matters is
        // the answer the authorize endpoint will give, not what the row happens to contain.
        (await applications.ValidateRedirectUriAsync(app, "http://127.0.0.1:54321/callback"))
            .ShouldBeTrue("a loopback callback on an ephemeral port must be accepted");
        (await applications.ValidateRedirectUriAsync(app, "speleoloc://auth"))
            .ShouldBeTrue("the custom-scheme callback must be accepted");

        // The positive answers above are only worth something if this registration refuses
        // something: an unrelated address must not be a valid destination for a code.
        (await applications.ValidateRedirectUriAsync(app, "https://attacker.example/callback"))
            .ShouldBeFalse("an unregistered address must not be accepted");

        // The grants the token endpoint may be reached with, and no others.
        var permissions = await applications.GetPermissionsAsync(app);
        permissions.ShouldContain(Permissions.GrantTypes.AuthorizationCode);
        permissions.ShouldContain(Permissions.GrantTypes.RefreshToken);
        permissions.ShouldNotContain(Permissions.GrantTypes.Password);
        permissions.ShouldNotContain(Permissions.GrantTypes.ClientCredentials);
        permissions.ShouldNotContain(Permissions.GrantTypes.DeviceCode);
    }

    [Fact]
    public async Task The_seeded_mobile_refresh_window_is_longer_than_the_web_clients()
    {
        using var scope = factory.Services.CreateScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        var mobile = await applications.FindByClientIdAsync(IdentitySeeder.SpeleoLocClientId);
        mobile.ShouldNotBeNull();

        var settings = await applications.GetSettingsAsync(mobile);
        settings.ShouldContainKey(Settings.TokenLifetimes.RefreshToken);
        TimeSpan.Parse(settings[Settings.TokenLifetimes.RefreshToken], CultureInfo.InvariantCulture)
            .ShouldBe(TimeSpan.FromDays(45));

        // The web client carries no lifetime of its own, which is what leaves it on the shorter
        // server-wide window. If a later change gave it one, the two clients would silently stop
        // differing and this whole arrangement would be pointless.
        var web = await applications.FindByClientIdAsync(IdentitySeeder.SpaClientId);
        web.ShouldNotBeNull();
        (await applications.GetSettingsAsync(web))
            .ShouldNotContainKey(Settings.TokenLifetimes.RefreshToken);
    }

    [Fact]
    public async Task A_registration_that_already_exists_is_reconciled_rather_than_left_alone()
    {
        // The value that matters here reaches an installation only through startup seeding, and
        // every installation but a brand-new one already has the row. A seeder that only created
        // a missing registration would ship a changed window to nobody who had ever started.
        using var scope = factory.Services.CreateScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        var app = await applications.FindByClientIdAsync(IdentitySeeder.SpeleoLocClientId);
        app.ShouldNotBeNull();

        var drifted = new OpenIddictApplicationDescriptor();
        await applications.PopulateAsync(drifted, app);
        drifted.SetRefreshTokenLifetime(TimeSpan.FromDays(3));
        drifted.RedirectUris.Clear();
        await applications.UpdateAsync(app, drifted);

        // Prove the drift landed, so that the assertion after re-seeding is about the seeder
        // rather than about an update that never happened.
        var strayed = await applications.FindByClientIdAsync(IdentitySeeder.SpeleoLocClientId);
        strayed.ShouldNotBeNull();
        (await applications.GetSettingsAsync(strayed))[Settings.TokenLifetimes.RefreshToken]
            .ShouldBe(TimeSpan.FromDays(3).ToString(null, CultureInfo.InvariantCulture));

        await IdentitySeeder.SeedAsync(
            scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<IConfiguration>());

        var repaired = await applications.FindByClientIdAsync(IdentitySeeder.SpeleoLocClientId);
        repaired.ShouldNotBeNull();
        TimeSpan.Parse(
            (await applications.GetSettingsAsync(repaired))[Settings.TokenLifetimes.RefreshToken],
            CultureInfo.InvariantCulture).ShouldBe(TimeSpan.FromDays(45));
        (await applications.ValidateRedirectUriAsync(repaired, "speleoloc://auth")).ShouldBeTrue();
    }

    [Fact]
    public async Task A_token_issued_to_the_mobile_client_carries_the_longer_window()
    {
        // The registration saying 45 days and a token actually lasting 45 days are two different
        // claims: the per-client setting has to win over the server-wide option for the first to
        // mean anything, and nothing in the library's documentation states which does.
        var caverId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, CaverEmail);

        var expiry = await IssuedRefreshTokenExpiryAsync(caverId);
        var window = expiry - DateTimeOffset.UtcNow;

        window.ShouldBeGreaterThan(TimeSpan.FromDays(44));
        window.ShouldBeLessThan(TimeSpan.FromDays(46));
    }

    /// <summary>
    /// Plays the sign-in an installed app performs — cookie login, authorize with PKCE, then the
    /// form-encoded code exchange — and reads back the expiry of the refresh token it was issued.
    /// </summary>
    private async Task<DateTimeOffset> IssuedRefreshTokenExpiryAsync(Guid caverId)
    {
        const string RedirectUri = "http://127.0.0.1:54321/callback";

        // The code arrives in a redirect's Location header, so redirects must not be followed.
        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { email = CaverEmail, password = AuthHelper.Password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);

        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        var challenge = WebEncoders.Base64UrlEncode(
            SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        // Without offline_access the exchange succeeds and simply returns no refresh token.
        var authorize = await client.GetAsync(
            $"/connect/authorize?client_id={IdentitySeeder.SpeleoLocClientId}" +
            "&redirect_uri=" + Uri.EscapeDataString(RedirectUri) +
            "&response_type=code" +
            "&scope=" + Uri.EscapeDataString("openid profile email roles offline_access") +
            $"&code_challenge={challenge}&code_challenge_method=S256&state=s");
        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var code = QueryHelpers.ParseQuery(authorize.Headers.Location!.Query)["code"].ToString();
        code.ShouldNotBeNullOrEmpty();

        var token = await client.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
                ["client_id"] = IdentitySeeder.SpeleoLocClientId,
                ["code_verifier"] = verifier,
            }));
        token.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = JsonDocument.Parse(await token.Content.ReadAsStringAsync()).RootElement;
        payload.TryGetProperty("refresh_token", out _)
            .ShouldBeTrue("offline_access must yield a refresh token");

        // The value handed to the client is not the store's lookup key — these are not reference
        // tokens — so the row is found by who it was issued to and what kind of token it is.
        using var scope = factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();

        object? stored = null;
        var seen = new List<string>();
        await foreach (var candidate in tokens.FindBySubjectAsync(caverId.ToString()))
        {
            // The store records the full token-type identifier, not the short hint the wire uses.
            var kind = await tokens.GetTypeAsync(candidate);
            seen.Add(kind ?? "(untyped)");
            if (kind == TokenTypeIdentifiers.RefreshToken)
            {
                stored = candidate;
            }
        }

        stored.ShouldNotBeNull(
            $"no stored refresh token for this account; the store held: {string.Join(", ", seen)}");

        var expiry = await tokens.GetExpirationDateAsync(stored);
        expiry.ShouldNotBeNull("a refresh token with no expiry would never lapse at all");
        return expiry.Value;
    }

    public void Dispose() => factory.Dispose();
}
