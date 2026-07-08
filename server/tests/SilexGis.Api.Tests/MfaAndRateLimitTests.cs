// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
[Collection(PostgresCollection.Name)]
public sealed class MfaAndRateLimitTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private string email = null!;

    public MfaAndRateLimitTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            // Tight limit so the limiter is testable without hammering.
            ["Auth:RateLimitPerMinute"] = "8",
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
        (await client.PostAsJsonAsync("/api/v1/me/mfa/confirm", new { code = "000000" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var confirm = await client.PostAsJsonAsync("/api/v1/me/mfa/confirm", new { code = Totp(sharedKey) });
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
        (await withoutCode.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("auth.mfa_required");

        // …and succeeds with the authenticator code.
        var withCode = await second.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = AuthHelper.Password,
            twoFactorCode = Totp(sharedKey),
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

    /// <summary>RFC 6238 TOTP (SHA1, 6 digits, 30 s step) over a base32 key.</summary>
    private static string Totp(string base32Key)
    {
        var key = Base32Decode(base32Key);
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        Span<byte> counterBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);
        var hash = HMACSHA1.HashData(key, counterBytes);
        var offset = hash[^1] & 0x0f;
        var code = ((hash[offset] & 0x7f) << 24)
            | (hash[offset + 1] << 16)
            | (hash[offset + 2] << 8)
            | hash[offset + 3];
        return (code % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var value = 0;
        var output = new List<byte>();
        foreach (var c in input.TrimEnd('=').ToUpperInvariant())
        {
            value = (value << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(value >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. output];
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
