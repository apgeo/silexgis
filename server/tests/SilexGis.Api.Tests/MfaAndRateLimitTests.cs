// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;

namespace SilexGis.Api.Tests;

/// <summary>
/// Accounts hardening: TOTP enrollment/confirmation with real RFC-6238 codes, the login
/// gate (mfa_required → code → session), recovery codes, disable — plus the per-IP
/// rate limiter on the auth surface.
/// </summary>
public sealed class MfaAndRateLimitTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private string email = null!;

    public MfaAndRateLimitTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            // Headroom: /me/mfa is on the rate-limited credential surface now that it sends
            // messages, and this walkthrough makes a dozen calls across it.
            ["Auth:RateLimitPerMinute"] = "200",
        });

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        email = $"mfa-{suffix}@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);
    }

    [Fact]
    public async Task Totp_enrollment_login_gate_and_recovery_codes_work()
    {
        var client = await AuthHelper.BearerClientAsync(factory, email);

        // Status: disabled. Enroll: get the shared key.
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/me/mfa/"))
            .GetProperty("enabled").GetBoolean().ShouldBeFalse();
        var enroll = await client.PostAsync("/api/v1/me/mfa/enroll", null);
        enroll.StatusCode.ShouldBe(HttpStatusCode.OK);
        var enrollBody = await enroll.Content.ReadFromJsonAsync<JsonElement>();
        var sharedKey = enrollBody.GetProperty("sharedKey").GetString()!.Replace(" ", string.Empty);
        enrollBody.GetProperty("authenticatorUri").GetString().ShouldStartWith("otpauth://totp/");

        // Confirm with a wrong code → 400; with a real TOTP → recovery codes issued.
        (await client.PostAsJsonAsync("/api/v1/me/mfa/methods/authenticator", new { code = "000000" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var confirm = await client.PostAsJsonAsync(
            "/api/v1/me/mfa/methods/authenticator", new { code = TotpCodes.Generate(sharedKey) });
        confirm.StatusCode.ShouldBe(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());
        var recoveryCodes = (await confirm.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("codes").EnumerateArray().Select(x => x.GetString()!).ToList();
        recoveryCodes.Count.ShouldBe(10);

        // Fresh session: password alone now yields the stable mfa_required code…
        using var second = factory.CreateClient();
        var withoutCode = await second.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = AuthHelper.Password,
        });
        withoutCode.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var refusal = await withoutCode.Content.ReadFromJsonAsync<JsonElement>();
        refusal.GetProperty("code").GetString().ShouldBe("auth.mfa_required");
        refusal.GetProperty("methods").EnumerateArray().Select(x => x.GetString())
            .ShouldBe(["authenticator"]);
        refusal.GetProperty("preferredMethod").GetString().ShouldBe("authenticator");

        // …and succeeds with the authenticator code.
        var withCode = await second.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = AuthHelper.Password,
            twoFactorCode = TotpCodes.Generate(sharedKey),
        });
        withCode.StatusCode.ShouldBe(HttpStatusCode.OK, await withCode.Content.ReadAsStringAsync());

        // Recovery codes work with the "recovery:" prefix.
        using var third = factory.CreateClient();
        (await third.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = AuthHelper.Password,
        })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var viaRecovery = await third.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = AuthHelper.Password,
            twoFactorCode = $"recovery:{recoveryCodes[0]}",
        });
        viaRecovery.StatusCode.ShouldBe(HttpStatusCode.OK, await viaRecovery.Content.ReadAsStringAsync());

        // Disable → password-only login works again.
        (await client.PostAsync("/api/v1/me/mfa/disable", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var fourth = factory.CreateClient();
        (await fourth.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = AuthHelper.Password,
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}

/// <summary>
/// The per-IP fixed window on the auth surface. Its own class because it needs a limit tight
/// enough to trip deliberately, which would starve any other test sharing the factory.
/// </summary>
public sealed class AuthRateLimitTests(PostgresFixture postgres) : IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory =
        new(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Auth:RateLimitPerMinute"] = "8",
        });

    [Fact]
    public async Task Auth_surface_rate_limits_per_ip()
    {
        using var client = factory.CreateClient();
        var saw429 = false;
        for (var i = 0; i < 12 && !saw429; i++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
            {
                email = "nobody@t.local",
                password = "wrong-password-1",
            });
            saw429 = response.StatusCode == HttpStatusCode.TooManyRequests;
        }

        saw429.ShouldBeTrue("the limiter should trip within the configured window");
    }

    public void Dispose() => factory.Dispose();
}
