// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Shouldly;
using SilexGis.Api.Auth;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;

namespace SilexGis.Api.Tests;

/// <summary>
/// The sign-in an installed application performs against this server, played end to end under its
/// own client id: password login for a cookie, an authorization request carrying a code challenge,
/// the code, the form-encoded exchange for tokens, a call made with the bearer, and a refresh.
/// </summary>
/// <remarks>
/// <para>
/// This class exists because four of the things a client must get right are invisible in the
/// generated API description, which documents the JSON surface and not the OAuth endpoints: that a
/// refresh token is issued only when <c>offline_access</c> is asked for, that the authorization code
/// arrives in a redirect header and is lost by any HTTP stack that follows redirects on its own,
/// that the token endpoint takes a form body rather than JSON, and the exact shape of the refusal a
/// caller meets when the account has two-factor sign-in turned on.
/// </para>
/// <para>
/// Each flow records the real requests and responses it made into a transcript file beside the test
/// assembly. The client-facing documentation is transcribed from those files rather than written
/// from memory, so it cannot drift into a description of what the server was believed to do.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class SyncAuthTests : IDisposable
{
    /// <summary>
    /// A loopback address with the ephemeral port an installed application would have bound. The
    /// registration carries no port and the comparison ignores it, so any port reaches the same
    /// registered destination.
    /// </summary>
    private const string RedirectUri = "http://127.0.0.1:54321/callback";

    private const string Scopes = "openid profile email roles offline_access";

    private readonly SilexGisApiFactory factory;

    public SyncAuthTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                // The auth surface is rate-limited per IP, and every flow in this class runs several
                // calls across it from the one address the test host presents.
                ["Auth:RateLimitPerMinute"] = "200",
            });

    [Fact]
    public async Task A_device_signs_in_with_a_code_and_keeps_working_by_refreshing()
    {
        var email = await NewAccountAsync("device");
        var transcript = new AuthTranscript(
            "The sign-in an installed application performs, and the refresh that keeps it signed in.");

        using var client = NoRedirectClient();

        var login = await transcript.SendAsync(
            client,
            JsonRequest(HttpMethod.Post, "/api/v1/auth/login", new { email, password = AuthHelper.Password }),
            "Password login. The response sets the session cookie the authorization request consumes.");
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        login.Headers.TryGetValues("Set-Cookie", out var cookies).ShouldBeTrue();
        cookies!.ShouldContain(c => c.StartsWith("silexgis.session", StringComparison.Ordinal));

        var (verifier, challenge) = CreatePkce();

        var authorize = await transcript.SendAsync(
            client,
            new HttpRequestMessage(HttpMethod.Get, AuthorizeUrl(challenge, Scopes)),
            "The authorization request, carrying the code challenge and the session cookie.");
        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var location = authorize.Headers.Location.ShouldNotBeNull();
        location.GetLeftPart(UriPartial.Path).ShouldBe("http://127.0.0.1:54321/callback");
        var query = QueryHelpers.ParseQuery(location.Query);
        var code = query["code"].ToString();
        code.ShouldNotBeNullOrEmpty();
        query["state"].ToString().ShouldBe("device-state");

        // The code exists nowhere but that header: the response has no body to read it out of, so a
        // client that lets its HTTP stack follow the redirect never sees the code at all.
        (await authorize.Content.ReadAsStringAsync()).ShouldBeNullOrWhiteSpace();

        var token = await transcript.SendAsync(
            client,
            FormRequest("/connect/token", new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
                ["client_id"] = IdentitySeeder.SpeleoLocClientId,
                ["code_verifier"] = verifier,
            }),
            "The code exchange. A public client sends no secret; the verifier is the proof.");
        token.StatusCode.ShouldBe(HttpStatusCode.OK);

        var tokens = await ReadJsonAsync(token);
        var accessToken = tokens.GetProperty("access_token").GetString().ShouldNotBeNull();
        var refreshToken = tokens.GetProperty("refresh_token").GetString().ShouldNotBeNull();
        tokens.GetProperty("token_type").GetString().ShouldBe("Bearer");
        tokens.GetProperty("expires_in").GetInt32().ShouldBeGreaterThan(0);

        using var bearer = NoRedirectClient();
        bearer.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);
        var me = await transcript.SendAsync(
            bearer,
            new HttpRequestMessage(HttpMethod.Get, "/api/v1/me/"),
            "A call made with the access token. Every route this server offers takes the same header.");
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadJsonAsync(me)).GetProperty("email").GetString().ShouldBe(email);

        var refreshed = await transcript.SendAsync(
            client,
            FormRequest("/connect/token", new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = IdentitySeeder.SpeleoLocClientId,
            }),
            "The refresh. Both tokens are replaced, so the answer must be stored, not just read.");
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var rotated = await ReadJsonAsync(refreshed);
        rotated.GetProperty("access_token").GetString().ShouldNotBe(accessToken);
        rotated.GetProperty("refresh_token").GetString().ShouldNotBe(refreshToken);

        // The refreshed access token has to be usable, or a device would refresh its way out of a
        // working session without anything answering non-2xx to tell it so.
        using var refreshedBearer = NoRedirectClient();
        refreshedBearer.DefaultRequestHeaders.Authorization =
            new("Bearer", rotated.GetProperty("access_token").GetString());
        (await refreshedBearer.GetAsync("/api/v1/me/")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await transcript.WriteAsync("speleoloc-auth-flow.txt");
    }

    [Fact]
    public async Task A_refresh_token_is_issued_only_to_a_request_that_asked_for_offline_access()
    {
        var email = await NewAccountAsync("offline");
        var transcript = new AuthTranscript(
            "The same exchange with and without offline_access in the requested scopes.");

        using var client = NoRedirectClient();
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = AuthHelper.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Asking for everything except offline_access. The exchange succeeds and looks entirely
        // healthy — which is why this is worth a test: a client that never asked would conclude the
        // server does not issue refresh tokens, and would send the user back to a password prompt
        // every fifteen minutes.
        var without = await ExchangeAsync(client, transcript, "openid profile email roles");
        without.Status.ShouldBe(HttpStatusCode.OK);
        without.Body.TryGetProperty("refresh_token", out _)
            .ShouldBeFalse("no refresh token may be issued to a request that did not ask for one");
        without.Body.GetProperty("access_token").GetString().ShouldNotBeNullOrEmpty();

        var with = await ExchangeAsync(client, transcript, Scopes);
        with.Status.ShouldBe(HttpStatusCode.OK);
        with.Body.GetProperty("refresh_token").GetString().ShouldNotBeNullOrEmpty();

        await transcript.WriteAsync("speleoloc-auth-offline-access.txt");
    }

    [Fact]
    public async Task The_token_endpoint_takes_a_form_body_and_refuses_a_json_one()
    {
        var email = await NewAccountAsync("form");
        var transcript = new AuthTranscript(
            "The token endpoint's encoding. A JSON body is refused; the same values as a form are accepted.");

        using var client = NoRedirectClient();
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = AuthHelper.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var (verifier, challenge) = CreatePkce();
        var authorize = await client.GetAsync(AuthorizeUrl(challenge, Scopes));
        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var code = QueryHelpers.ParseQuery(authorize.Headers.Location!.Query)["code"].ToString();

        var asJson = await transcript.SendAsync(
            client,
            JsonRequest(HttpMethod.Post, "/connect/token", new
            {
                grant_type = "authorization_code",
                code,
                redirect_uri = RedirectUri,
                client_id = IdentitySeeder.SpeleoLocClientId,
                code_verifier = verifier,
            }),
            "The exchange sent as JSON. This is the shape every other route on this server takes.");
        asJson.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The same code, unspent, is then accepted as a form — so the refusal above was about the
        // encoding and not about anything wrong with the request's contents.
        var asForm = await transcript.SendAsync(
            client,
            FormRequest("/connect/token", new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
                ["client_id"] = IdentitySeeder.SpeleoLocClientId,
                ["code_verifier"] = verifier,
            }),
            "The identical exchange as a form body.");
        asForm.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadJsonAsync(asForm)).GetProperty("access_token").GetString().ShouldNotBeNullOrEmpty();

        await transcript.WriteAsync("speleoloc-auth-encoding.txt");
    }

    [Fact]
    public async Task An_account_with_two_factor_sign_in_refuses_the_password_and_says_what_it_accepts()
    {
        var email = await NewAccountAsync("mfa");
        var transcript = new AuthTranscript(
            "The two-factor branch of the login step, and the sign-in that follows a code.");

        // Turn two-factor on the way the account holder would, through the signed-in surface.
        using var enrolled = await DeviceBearerClientAsync(email);
        var enroll = await enrolled.PostAsync("/api/v1/me/mfa/enroll", null);
        enroll.StatusCode.ShouldBe(HttpStatusCode.OK);
        var sharedKey = (await ReadJsonAsync(enroll)).GetProperty("sharedKey").GetString()!
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        (await enrolled.PostAsJsonAsync(
            "/api/v1/me/mfa/methods/authenticator", new { code = TotpCodes.Generate(sharedKey) }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        using var client = NoRedirectClient();

        var refused = await transcript.SendAsync(
            client,
            JsonRequest(HttpMethod.Post, "/api/v1/auth/login", new { email, password = AuthHelper.Password }),
            "The password alone, once two-factor sign-in is on. The refusal names what would be accepted.");
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var refusal = await ReadJsonAsync(refused);
        refusal.GetProperty("code").GetString().ShouldBe("auth.mfa_required");
        refusal.GetProperty("methods").EnumerateArray().Select(m => m.GetString())
            .ShouldContain("authenticator");
        refusal.GetProperty("preferredMethod").GetString().ShouldBe("authenticator");

        // The third field is the one a client is most likely to miss. Every method can be taken
        // away by somebody other than the person signing in, so a client that offers only the
        // methods listed can strand a user with no way in; recovery codes are always accepted.
        refusal.GetProperty("recoveryAccepted").GetBoolean().ShouldBeTrue();

        var accepted = await transcript.SendAsync(
            client,
            JsonRequest(HttpMethod.Post, "/api/v1/auth/login", new
            {
                email,
                password = AuthHelper.Password,
                twoFactorCode = TotpCodes.Generate(sharedKey),
            }),
            "The same login with the code, which yields the session cookie as usual.");
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);

        // From here the flow is the ordinary one: the two-factor gate is on the login step alone
        // and changes nothing about the authorization request or the exchange.
        var exchanged = await ExchangeAsync(client, transcript, Scopes);
        exchanged.Status.ShouldBe(HttpStatusCode.OK);
        exchanged.Body.GetProperty("refresh_token").GetString().ShouldNotBeNullOrEmpty();

        await transcript.WriteAsync("speleoloc-auth-two-factor.txt");
    }

    [Fact]
    public async Task An_authorization_request_without_a_session_is_sent_to_the_sign_in_page()
    {
        // What a device meets when its refresh token has lapsed or been revoked: the authorization
        // request does not fail, it redirects to a page a device has no way to render. A client has
        // to read the destination, not the status, to know it must ask for a password again.
        using var client = NoRedirectClient();

        var (_, challenge) = CreatePkce();
        var authorize = await client.GetAsync(AuthorizeUrl(challenge, Scopes));

        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        authorize.Headers.Location.ShouldNotBeNull().ToString().ShouldContain("/login");
    }

    private async Task<string> NewAccountAsync(string prefix)
    {
        var email = $"speleoloc-{prefix}-{Guid.NewGuid().ToString("N")[..8]}@test.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, email);
        return email;
    }

    /// <summary>
    /// The authorization code arrives in the <c>Location</c> header of a redirect, so a client that
    /// follows redirects automatically consumes it into a request to an address this server does not
    /// answer and never sees it. Every client here is built with that turned off for that reason.
    /// </summary>
    private HttpClient NoRedirectClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<HttpClient> DeviceBearerClientAsync(string email)
    {
        using var cookieClient = NoRedirectClient();
        (await cookieClient.PostAsJsonAsync(
            "/api/v1/auth/login", new { email, password = AuthHelper.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var exchanged = await ExchangeAsync(cookieClient, transcript: null, Scopes);
        exchanged.Status.ShouldBe(HttpStatusCode.OK);

        var bearer = NoRedirectClient();
        bearer.DefaultRequestHeaders.Authorization =
            new("Bearer", exchanged.Body.GetProperty("access_token").GetString());
        return bearer;
    }

    /// <summary>Authorization request through code exchange on an already-signed-in client.</summary>
    private async Task<(HttpStatusCode Status, JsonElement Body)> ExchangeAsync(
        HttpClient client, AuthTranscript? transcript, string scopes)
    {
        var (verifier, challenge) = CreatePkce();

        var authorizeRequest = new HttpRequestMessage(HttpMethod.Get, AuthorizeUrl(challenge, scopes));
        var authorize = transcript is null
            ? await client.SendAsync(authorizeRequest)
            : await transcript.SendAsync(
                client, authorizeRequest, $"The authorization request, asking for: {scopes}");
        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var code = QueryHelpers.ParseQuery(authorize.Headers.Location!.Query)["code"].ToString();

        var tokenRequest = FormRequest("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = IdentitySeeder.SpeleoLocClientId,
            ["code_verifier"] = verifier,
        });
        var token = transcript is null
            ? await client.SendAsync(tokenRequest)
            : await transcript.SendAsync(client, tokenRequest, "The code exchange.");

        return (token.StatusCode, await ReadJsonAsync(token));
    }

    private static string AuthorizeUrl(string challenge, string scopes) =>
        $"/connect/authorize?client_id={IdentitySeeder.SpeleoLocClientId}" +
        "&redirect_uri=" + Uri.EscapeDataString(RedirectUri) +
        "&response_type=code" +
        "&scope=" + Uri.EscapeDataString(scopes) +
        $"&code_challenge={challenge}&code_challenge_method=S256&state=device-state";

    private static (string Verifier, string Challenge) CreatePkce()
    {
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    private static HttpRequestMessage JsonRequest(HttpMethod method, string path, object body) =>
        new(method, path) { Content = JsonContent.Create(body) };

    private static HttpRequestMessage FormRequest(string path, Dictionary<string, string> fields) =>
        new(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(fields) };

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    public void Dispose() => factory.Dispose();
}
