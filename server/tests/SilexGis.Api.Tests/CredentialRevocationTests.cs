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
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Shouldly;
using SilexGis.Api.Auth;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Identity;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SilexGis.Api.Tests;

/// <summary>
/// What changing or resetting a password does to credentials already handed out, and what happens
/// when a rotated refresh token is presented a second time.
/// </summary>
/// <remarks>
/// The account holder whose phone was taken cannot enumerate the sessions it holds, so the only
/// action they can reach — changing the password, or resetting it from a mailbox — has to end every
/// session on every device. That makes these assertions the thing that keeps a long-lived mobile
/// refresh window defensible: without them the credential on the lost device outlives the password
/// it was obtained with by weeks.
/// </remarks>
public sealed class CredentialRevocationTests : IDisposable, IClassFixture<PostgresFixture>
{
    private const string NewPassword = "integration-test-pass-2";
    private const string DeviceRedirectUri = "http://127.0.0.1:54321/callback";

    private readonly PostgresFixture postgres;
    private readonly SilexGisApiFactory factory;
    private SilexGisApiFactory? strictReuse;

    public CredentialRevocationTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
        factory = new SilexGisApiFactory(postgres.ConnectionString, RateLimitHeadroom());
    }

    [Fact]
    public async Task Password_change_kills_existing_refresh_tokens()
    {
        const string Email = "revocation-change@test.local";
        var caverId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, Email);
        var device = await DeviceRefreshTokenAsync(factory, Email, AuthHelper.Password);

        // The positive half: the credential this test is about is genuinely usable first, so a
        // later refusal is the password change refusing it rather than a flow that never worked.
        var rotated = await RefreshAsync(factory, device);
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stillLive = await RefreshTokenOf(rotated);
        (await SubjectTokenStatusesAsync(factory, caverId)).ShouldContain(Statuses.Valid);

        // Deliberately performed from the web session, on the other client entirely: the account
        // holder acts on whatever is to hand, and what has to die is what the lost device holds.
        var web = await AuthHelper.BearerClientAsync(factory, Email);
        var change = await web.PutAsJsonAsync(
            "/api/v1/me/password",
            new { currentPassword = AuthHelper.Password, newPassword = NewPassword });
        change.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterwards = await RefreshAsync(factory, stillLive);
        afterwards.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Asserted at the store as well as at the endpoint, because the exchange has other reasons
        // to refuse a refresh — an unconfirmed address, a missing account — and a status assertion
        // is the only one that says the refusal came from revocation.
        (await SubjectTokenStatusesAsync(factory, caverId)).ShouldNotContain(Statuses.Valid);
        (await SubjectAuthorizationStatusesAsync(factory, caverId)).ShouldNotContain(Statuses.Valid);
    }

    [Fact]
    public async Task Password_reset_kills_existing_refresh_tokens()
    {
        const string Email = "revocation-reset@test.local";
        var caverId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, Email);
        var device = await DeviceRefreshTokenAsync(factory, Email, AuthHelper.Password);

        var rotated = await RefreshAsync(factory, device);
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stillLive = await RefreshTokenOf(rotated);
        (await SubjectTokenStatusesAsync(factory, caverId)).ShouldContain(Statuses.Valid);

        // The recovery path matters more than the signed-in one: it is the path taken by someone
        // who no longer holds the device, and by an attacker who has taken the mailbox.
        var anonymous = factory.CreateClient();
        var reset = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/password/reset",
            new { email = Email, token = await ResetTokenAsync(factory, Email), newPassword = NewPassword });
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterwards = await RefreshAsync(factory, stillLive);
        afterwards.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await SubjectTokenStatusesAsync(factory, caverId)).ShouldNotContain(Statuses.Valid);
        (await SubjectAuthorizationStatusesAsync(factory, caverId)).ShouldNotContain(Statuses.Valid);
    }

    [Fact]
    public async Task Redeemed_refresh_token_is_rejected_when_replayed()
    {
        // Rolling refresh tokens are deliberately still accepted for a short window after being
        // redeemed, so that a client whose network dropped mid-exchange can retry rather than be
        // signed out. That window defaults to thirty seconds, and a test that replays a token in
        // the same process replays it inside it — so the naive version of this test passes while
        // proving nothing at all. The window is closed here rather than waited out: waiting would
        // add half a minute to the suite, and a test that must sleep to be true is one that gets
        // shortened later by someone who does not know why the sleep was there.
        var strict = StrictReuseFactory();

        const string Email = "revocation-replay@test.local";
        var caverId = await AuthHelper.CreateUserAsync(strict, GlobalRoles.Viewer, Email);
        var first = await DeviceRefreshTokenAsync(strict, Email, AuthHelper.Password);

        var rotation = await RefreshAsync(strict, first);
        rotation.StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = await RefreshTokenOf(rotation);
        second.ShouldNotBe(first, "rotation must hand back a different token, or nothing was rotated");

        var replay = await RefreshAsync(strict, first);
        replay.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Rotation that merely refuses the presented token is rotation, not reuse detection: an
        // attacker who replayed a stolen token would keep whatever the legitimate client was handed
        // next. What makes it detection is that the replay ends the whole chain, so the assertion
        // is on the token the legitimate holder still has, not on the one that was replayed.
        var successor = await RefreshAsync(strict, second);
        successor.StatusCode.ShouldBe(
            HttpStatusCode.BadRequest,
            "a replay must revoke the whole chain, not merely reject the presentation");

        // "Revoked" and not merely "redeemed" is the distinction the paragraph above turns on, so
        // it is read off the store rather than inferred from two refusals that could have had
        // different causes.
        var statuses = await SubjectTokenStatusesAsync(strict, caverId);
        statuses.ShouldNotContain(Statuses.Valid);
        statuses.ShouldContain(Statuses.Revoked);
    }

    [Fact]
    public async Task A_replay_inside_the_reuse_window_is_forgiven_rather_than_detected()
    {
        // The counterpart to the test above, and the reason it needs a server of its own: on the
        // shipped configuration the same replay succeeds, because it lands inside the window that
        // exists so a client whose connection dropped mid-exchange can retry. Asserted rather than
        // assumed, so that nobody reading the strict factory takes it for ceremony — without it
        // that test would pass on any build, including one where rotation had been turned off.
        const string Email = "revocation-leeway@test.local";
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, Email);

        var first = await DeviceRefreshTokenAsync(factory, Email, AuthHelper.Password);
        (await RefreshAsync(factory, first)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var promptReplay = await RefreshAsync(factory, first);
        promptReplay.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            "the shipped reuse window is what makes an in-process replay test meaningless "
            + "unless the window is closed first");
    }

    [Fact]
    public async Task A_cookie_from_before_a_password_change_can_no_longer_mint_a_code()
    {
        const string Email = "revocation-cookie-change@test.local";
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, Email);
        using var session = await CookieSessionAsync(factory, Email, AuthHelper.Password);

        // The positive half. Emptying the token stores means nothing on its own while the browser
        // session that obtained them is still able to ask for more: the authorize endpoint trusts
        // this cookie and consents without asking, so a code minted here becomes a refresh token
        // whose window is measured in weeks. This asserts the cookie genuinely could do that
        // before, so the refusal below is the password change refusing it.
        var before = await AuthorizeAsync(session);
        before.Response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        before.Response.Headers.Location!.OriginalString.ShouldContain("code=");

        var web = await AuthHelper.BearerClientAsync(factory, Email);
        var change = await web.PutAsJsonAsync(
            "/api/v1/me/password",
            new { currentPassword = AuthHelper.Password, newPassword = NewPassword });
        change.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await AuthorizeAsync(session);
        after.Response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        after.Response.Headers.Location!.OriginalString.ShouldStartWith(
            "/login",
            Case.Sensitive,
            "a cookie held from before the change must be sent back to sign in, not handed a code");
    }

    [Fact]
    public async Task A_cookie_from_before_a_password_reset_can_no_longer_mint_a_code()
    {
        // The reset path is the one that matters most and the one nothing else can cover: it is
        // performed by someone who is not signed in, from a mailbox, so it has no way to reach the
        // cookie of whoever holds the device — only the rotated stamp can.
        const string Email = "revocation-cookie-reset@test.local";
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, Email);
        using var session = await CookieSessionAsync(factory, Email, AuthHelper.Password);

        var before = await AuthorizeAsync(session);
        before.Response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        before.Response.Headers.Location!.OriginalString.ShouldContain("code=");

        var anonymous = factory.CreateClient();
        var reset = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/password/reset",
            new { email = Email, token = await ResetTokenAsync(factory, Email), newPassword = NewPassword });
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await AuthorizeAsync(session);
        after.Response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        after.Response.Headers.Location!.OriginalString.ShouldStartWith(
            "/login",
            Case.Sensitive,
            "a cookie held from before the reset must be sent back to sign in, not handed a code");
    }

    /// <summary>
    /// Signs in the way an installed app does and returns the client holding the session cookie.
    /// </summary>
    private static async Task<HttpClient> CookieSessionAsync(
        SilexGisApiFactory factory, string email, string password)
    {
        // The authorization code arrives in the Location header of a redirect, so redirects must
        // not be followed or it is consumed before it can be read.
        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { email, password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        return client;
    }

    /// <summary>
    /// Asks the authorize endpoint for a code under the device client, returning the response
    /// unexamined along with the PKCE verifier that matches the challenge it was sent.
    /// </summary>
    private static async Task<(HttpResponseMessage Response, string Verifier)> AuthorizeAsync(
        HttpClient session)
    {
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        var challenge = WebEncoders.Base64UrlEncode(
            SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        // offline_access is what makes the exchange return a refresh token at all; without it the
        // exchange still succeeds and simply hands back none.
        var response = await session.GetAsync(
            $"/connect/authorize?client_id={IdentitySeeder.SpeleoLocClientId}" +
            "&redirect_uri=" + Uri.EscapeDataString(DeviceRedirectUri) +
            "&response_type=code" +
            "&scope=" + Uri.EscapeDataString("openid profile email roles offline_access") +
            $"&code_challenge={challenge}&code_challenge_method=S256&state=s");
        return (response, verifier);
    }

    /// <summary>
    /// Signs in the way an installed app does — cookie login, authorize with PKCE, then the
    /// form-encoded exchange — and returns the refresh token it was issued.
    /// </summary>
    private static async Task<string> DeviceRefreshTokenAsync(
        SilexGisApiFactory factory, string email, string password)
    {
        using var client = await CookieSessionAsync(factory, email, password);

        var (authorize, verifier) = await AuthorizeAsync(client);
        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var code = QueryHelpers.ParseQuery(authorize.Headers.Location!.Query)["code"].ToString();

        var token = await client.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = DeviceRedirectUri,
                ["client_id"] = IdentitySeeder.SpeleoLocClientId,
                ["code_verifier"] = verifier,
            }));
        token.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await RefreshTokenOf(token);
    }

    private static Task<HttpResponseMessage> RefreshAsync(
        SilexGisApiFactory factory, string refreshToken) =>
        factory.CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = IdentitySeeder.SpeleoLocClientId,
            }));

    private static async Task<string> RefreshTokenOf(HttpResponseMessage response)
    {
        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        payload.TryGetProperty("refresh_token", out var refresh)
            .ShouldBeTrue("offline_access must yield a refresh token");
        return refresh.GetString()!;
    }

    private static async Task<string> ResetTokenAsync(SilexGisApiFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
        var user = await users.FindByEmailAsync(email);
        user.ShouldNotBeNull();
        return await users.GeneratePasswordResetTokenAsync(user);
    }

    private static async Task<List<string>> SubjectTokenStatusesAsync(
        SilexGisApiFactory factory, Guid caverId)
    {
        using var scope = factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();

        var statuses = new List<string>();
        await foreach (var token in tokens.FindBySubjectAsync(caverId.ToString()))
        {
            statuses.Add(await tokens.GetStatusAsync(token) ?? "(none)");
        }

        statuses.ShouldNotBeEmpty("this account has no stored tokens at all, so nothing was proved");
        return statuses;
    }

    private static async Task<List<string>> SubjectAuthorizationStatusesAsync(
        SilexGisApiFactory factory, Guid caverId)
    {
        using var scope = factory.Services.CreateScope();
        var authorizations = scope.ServiceProvider
            .GetRequiredService<IOpenIddictAuthorizationManager>();

        var statuses = new List<string>();
        await foreach (var authorization in authorizations.FindBySubjectAsync(caverId.ToString()))
        {
            statuses.Add(await authorizations.GetStatusAsync(authorization) ?? "(none)");
        }

        statuses.ShouldNotBeEmpty("this account has no authorizations at all, so nothing was proved");
        return statuses;
    }

    /// <summary>
    /// A server whose rolling-refresh reuse window is closed, so that a replay milliseconds after a
    /// rotation is treated as a replay rather than as the retry the window exists to forgive.
    /// </summary>
    private SilexGisApiFactory StrictReuseFactory() =>
        strictReuse ??= new SilexGisApiFactory(
            postgres.ConnectionString,
            RateLimitHeadroom(),
            services => services.PostConfigure<OpenIddictServerOptions>(
                options => options.RefreshTokenReuseLeeway = TimeSpan.Zero));

    // The sign-in surface is rate-limited per IP address and every test here makes several calls
    // against it from one.
    private static Dictionary<string, string?> RateLimitHeadroom() =>
        new() { ["Auth:RateLimitPerMinute"] = "200" };

    public void Dispose()
    {
        strictReuse?.Dispose();
        factory.Dispose();
    }
}
