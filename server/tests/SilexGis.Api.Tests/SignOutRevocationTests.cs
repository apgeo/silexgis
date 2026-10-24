// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Shouldly;
using SilexGis.Api.Auth;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SilexGis.Api.Tests;

/// <summary>
/// Signing out cancels the renewal key of the session that signs out, and only that one.
/// </summary>
/// <remarks>
/// <para>
/// The page signing out holds a refresh token in memory. Loading a new page discards it, and the
/// sign-in cookie is ended so the authorize endpoint cannot hand out another — but a copy taken out
/// of the page beforehand used to stay good for the rest of its window, which is weeks. The page now
/// presents it to the protocol's revocation endpoint before it forgets it.
/// </para>
/// <para>
/// What has to hold, each asserted below: the key is refused afterwards (and was accepted before, so
/// the refusal is the revocation's); so is any earlier key of the same sign-in that a copy could
/// still be holding; another sign-in of the same account renews as before; revocation needs no
/// access token, because the one the page holds may well have expired; asking twice, or about a
/// key the server never issued, still answers success, as the protocol says it must; and a caller
/// that does not name a client the key could belong to is refused.
/// </para>
/// </remarks>
public sealed class SignOutRevocationTests : IDisposable, IClassFixture<PostgresFixture>
{
    private const string SpaClient = "silexgis-spa";
    private const string SpaRedirect = "http://localhost/auth/callback";

    private readonly PostgresFixture postgres;
    private readonly SilexGisApiFactory factory;
    private SilexGisApiFactory? strictReuse;
    private readonly string tag = Guid.NewGuid().ToString("N")[..8];

    public SignOutRevocationTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
        factory = new SilexGisApiFactory(postgres.ConnectionString, RateLimitHeadroom());
    }

    [Fact]
    public async Task The_renewal_key_is_refused_after_sign_out_and_was_accepted_before()
    {
        var email = $"signout-{tag}@test.local";
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, email);
        var (session, refresh) = await SpaSignInAsync(factory, email);

        // The positive half: this key renews, so the refusal below is the sign-out's doing.
        var renewed = await RefreshAsync(factory, refresh);
        renewed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var held = await RefreshTokenOf(renewed);

        // Sign-out as the page does it: the key first, then the cookie.
        (await RevokeAsync(factory, held)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await session.PostAsync("/api/v1/auth/logout", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterwards = await RefreshAsync(factory, held);
        afterwards.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorOf(afterwards)).ShouldBe(Errors.InvalidGrant);

        // And the cookie can no longer be traded for a fresh code either.
        var authorize = await session.GetAsync(AuthHelper.AuthorizeUrl());
        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        authorize.Headers.Location!.OriginalString.ShouldStartWith("/login");
    }

    [Fact]
    public async Task An_earlier_key_of_the_same_sign_in_is_refused_too()
    {
        // A copy of the key taken before the page last renewed is the one a thief is likeliest to
        // hold. Renewal rotates the key and keeps the old one acceptable for a short while, so a
        // client whose connection dropped mid-renewal can retry; cancelling only the newest key
        // would leave that older copy able to mint a fresh chain inside that window. The sign-in
        // itself is cancelled, which refuses every key it ever issued.
        var email = $"signout-chain-{tag}@test.local";
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, email);
        var (_, first) = await SpaSignInAsync(factory, email);
        var renewed = await RefreshAsync(factory, first);
        renewed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var held = await RefreshTokenOf(renewed);

        // Inside the shipped window the earlier key is still forgiven — that is what makes the
        // assertion after the sign-out mean something.
        var forgiven = await RefreshAsync(factory, first);
        forgiven.StatusCode.ShouldBe(HttpStatusCode.OK);
        var latest = await RefreshTokenOf(forgiven);

        (await RevokeAsync(factory, latest)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await RefreshAsync(factory, first)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await RefreshAsync(factory, held)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await RefreshAsync(factory, latest)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Another_sign_in_of_the_same_account_keeps_renewing()
    {
        // Signing out of the expedition laptop is not signing out of the phone in the car.
        var email = $"signout-other-{tag}@test.local";
        var accountId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, email);
        var (_, laptop) = await SpaSignInAsync(factory, email);
        var (_, desk) = await SpaSignInAsync(factory, email);
        var phone = await DeviceRefreshTokenAsync(factory, email);

        (await RevokeAsync(factory, laptop)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await RefreshAsync(factory, laptop)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await RefreshAsync(factory, desk)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RefreshAsync(factory, phone, IdentitySeeder.SpeleoLocClientId)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Read off the store as well: at least one sign-in of the account is untouched, so this was
        // not an account-wide revocation that the two renewals above happened to outrun.
        (await AuthorizationStatusesAsync(factory, accountId)).ShouldContain(Statuses.Valid);
    }

    [Fact]
    public async Task Revocation_needs_no_access_token_and_answers_the_same_however_often_it_is_asked()
    {
        var email = $"signout-idem-{tag}@test.local";
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, email);
        var (_, refresh) = await SpaSignInAsync(factory, email);

        // A bare client: no bearer header, no cookie. The access token the page holds at sign-out
        // may have expired an hour ago, and signing out must still work.
        using var bare = factory.CreateClient();
        bare.DefaultRequestHeaders.Authorization.ShouldBeNull();

        (await RevokeAsync(factory, refresh, client: bare)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RevokeAsync(factory, refresh, client: bare)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A key this server never issued is answered exactly as a revoked one is, so the endpoint
        // tells nobody whether a string they hold was ever a key.
        (await RevokeAsync(factory, "not-a-token-" + tag, client: bare)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await RefreshAsync(factory, refresh)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_caller_that_names_no_client_or_an_unknown_one_is_refused_and_the_key_lives()
    {
        var email = $"signout-client-{tag}@test.local";
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, email);
        var (_, refresh) = await SpaSignInAsync(factory, email);

        var nameless = await RevokeAsync(factory, refresh, clientId: null);
        nameless.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ErrorOf(nameless)).ShouldBe(Errors.InvalidClient);

        var stranger = await RevokeAsync(factory, refresh, clientId: "no-such-client");
        stranger.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ErrorOf(stranger)).ShouldBe(Errors.InvalidClient);

        // A form without the key at all is malformed, not a success.
        using var bare = factory.CreateClient();
        var empty = await bare.PostAsync("/connect/revoke", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["client_id"] = SpaClient }));
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorOf(empty)).ShouldBe(Errors.InvalidRequest);

        (await RefreshAsync(factory, refresh)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task One_client_cannot_cancel_a_key_issued_to_another()
    {
        // The installed app's key is the app's: the web client presenting it gets the answer every
        // key it cannot cancel gets, and the app keeps renewing.
        var email = $"signout-cross-{tag}@test.local";
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, email);
        var phone = await DeviceRefreshTokenAsync(factory, email);

        await RevokeAsync(factory, phone, clientId: SpaClient);

        (await RefreshAsync(factory, phone, IdentitySeeder.SpeleoLocClientId)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_installed_app_can_cancel_its_own_key()
    {
        var email = $"signout-app-{tag}@test.local";
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, email);
        var phone = await DeviceRefreshTokenAsync(factory, email);

        (await RevokeAsync(factory, phone, clientId: IdentitySeeder.SpeleoLocClientId)).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        (await RefreshAsync(factory, phone, IdentitySeeder.SpeleoLocClientId)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_discovery_document_names_the_revocation_endpoint_the_page_signs_out_through()
    {
        // The browser's sign-in library finds the endpoint here and nowhere else.
        using var client = factory.CreateClient();
        var discovery = await client.GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration");
        discovery.GetProperty("revocation_endpoint").GetString().ShouldNotBeNull().ShouldEndWith("/connect/revoke");
    }

    [Fact]
    public async Task A_replayed_key_after_sign_out_does_not_revive_the_sign_in_under_strict_reuse()
    {
        // With the forgiveness window closed, presenting a cancelled key is a replay like any other
        // and must not mint anything either — the two rules compose instead of one undoing the other.
        var strict = StrictReuseFactory();
        var email = $"signout-strict-{tag}@test.local";
        await AuthHelper.CreateUserAsync(strict, GlobalRoles.Viewer, email);
        var (_, refresh) = await SpaSignInAsync(strict, email);

        (await RevokeAsync(strict, refresh)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RefreshAsync(strict, refresh)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------- helpers ----------

    /// <summary>Signs in the way the web page does and returns its cookie session and refresh token.</summary>
    private static async Task<(HttpClient Session, string Refresh)> SpaSignInAsync(
        SilexGisApiFactory factory, string email)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = AuthHelper.Password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);

        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var authorize = await client.GetAsync(AuthHelper.AuthorizeUrl(challenge));
        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var code = QueryHelpers.ParseQuery(authorize.Headers.Location!.Query)["code"].ToString();

        var token = await client.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = SpaRedirect,
                ["client_id"] = SpaClient,
                ["code_verifier"] = verifier,
            }));
        token.StatusCode.ShouldBe(HttpStatusCode.OK, await token.Content.ReadAsStringAsync());
        return (client, await RefreshTokenOf(token));
    }

    /// <summary>Signs in the way the installed app does and returns its refresh token.</summary>
    private static async Task<string> DeviceRefreshTokenAsync(SilexGisApiFactory factory, string email)
    {
        const string DeviceRedirect = "http://127.0.0.1:54321/callback";
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = AuthHelper.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var authorize = await client.GetAsync(
            $"/connect/authorize?client_id={IdentitySeeder.SpeleoLocClientId}"
            + "&redirect_uri=" + Uri.EscapeDataString(DeviceRedirect)
            + "&response_type=code&scope=" + Uri.EscapeDataString("openid profile email roles offline_access")
            + $"&code_challenge={challenge}&code_challenge_method=S256&state=s");
        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var code = QueryHelpers.ParseQuery(authorize.Headers.Location!.Query)["code"].ToString();

        var token = await client.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = DeviceRedirect,
                ["client_id"] = IdentitySeeder.SpeleoLocClientId,
                ["code_verifier"] = verifier,
            }));
        token.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await RefreshTokenOf(token);
    }

    private static Task<HttpResponseMessage> RefreshAsync(
        SilexGisApiFactory factory, string refreshToken, string clientId = SpaClient) =>
        factory.CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = clientId,
            }));

    /// <summary>What the page sends at sign-out: the refresh token, said to be one, and its client.</summary>
    private static async Task<HttpResponseMessage> RevokeAsync(
        SilexGisApiFactory factory, string token, string? clientId = SpaClient, HttpClient? client = null)
    {
        var form = new Dictionary<string, string>
        {
            ["token"] = token,
            ["token_type_hint"] = TokenTypeHints.RefreshToken,
        };
        if (clientId is not null)
        {
            form["client_id"] = clientId;
        }

        if (client is not null)
        {
            return await client.PostAsync("/connect/revoke", new FormUrlEncodedContent(form));
        }

        using var own = factory.CreateClient();
        return await own.PostAsync("/connect/revoke", new FormUrlEncodedContent(form));
    }

    private static async Task<string> RefreshTokenOf(HttpResponseMessage response)
    {
        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        payload.TryGetProperty("refresh_token", out var refresh).ShouldBeTrue("offline_access must yield a refresh token");
        return refresh.GetString()!;
    }

    private static async Task<string?> ErrorOf(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(text)
            ? null
            : JsonDocument.Parse(text).RootElement.TryGetProperty("error", out var error) ? error.GetString() : null;
    }

    private static async Task<List<string>> AuthorizationStatusesAsync(SilexGisApiFactory factory, Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var statuses = new List<string>();
        await foreach (var authorization in authorizations.FindBySubjectAsync(accountId.ToString()))
        {
            statuses.Add(await authorizations.GetStatusAsync(authorization) ?? "(none)");
        }

        return statuses;
    }

    private SilexGisApiFactory StrictReuseFactory() =>
        strictReuse ??= new SilexGisApiFactory(
            postgres.ConnectionString,
            RateLimitHeadroom(),
            services => services.PostConfigure<OpenIddictServerOptions>(
                options => options.RefreshTokenReuseLeeway = TimeSpan.Zero));

    // The sign-in surface is rate-limited per IP address and every test here makes several calls.
    private static Dictionary<string, string?> RateLimitHeadroom() =>
        new() { ["Auth:RateLimitPerMinute"] = "200" };

    public void Dispose()
    {
        strictReuse?.Dispose();
        factory.Dispose();
    }
}
