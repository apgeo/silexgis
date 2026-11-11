// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The protections around signing in that only show when somebody is attacking them: the lock
/// after repeated wrong passwords or codes, the recovery code meeting a locked account, the reset
/// token's single use and its owner, the session cookie's flags behind the TLS proxy, and the
/// refusals registration gives.
/// </summary>
/// <remarks>
/// Every account is new to its test, so the lock and failure count one test drives never reach
/// another sharing the application. The lock's expiry is moved through Identity rather than waited
/// for: Identity reads the wall clock directly, so the application's test clock cannot move it.
/// </remarks>
public sealed class SignInProtectionTests : IDisposable, IClassFixture<PostgresFixture>
{
    private const string Login = "/api/v1/auth/login";
    private const string WrongPassword = "not-the-password-1";

    private readonly PostgresFixture postgres;
    private readonly SilexGisApiFactory factory;

    public SignInProtectionTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
        factory = new SilexGisApiFactory(postgres.ConnectionString, Settings());
    }

    private static Dictionary<string, string?> Settings() => new()
    {
        ["PublicUrl"] = "http://localhost",
        ["Auth:OpenRegistration"] = "true",
        // Every test here makes many sign-in calls from the one test address on purpose; the
        // per-address limiter has its own tests and must not be what refuses them.
        ["Auth:RateLimitPerMinute"] = "1000",
    };

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_and_the_lock_ends_when_it_expires()
    {
        var email = Email("lock");
        const string password = "a-long-password-1";
        (await Browser().PostAsJsonAsync("/api/v1/auth/register", new { email, password }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // The positive half: the account signs in, so every refusal below is the lock's.
        (await Browser().PostAsJsonAsync(Login, new { email, password })).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var client = Browser();
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var wrong = await client.PostAsJsonAsync(Login, new { email, password = WrongPassword });
            wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await CodeOfAsync(wrong)).ShouldBe("auth.invalid_credentials", $"attempt {attempt}");
        }

        (await FailedAttemptsAsync(email)).ShouldBe(4);

        // The fifth wrong password is the one that locks.
        var fifth = await client.PostAsJsonAsync(Login, new { email, password = WrongPassword });
        fifth.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeOfAsync(fifth)).ShouldBe("auth.locked_out");

        // Locked means locked: the right password is refused too, and no session is made.
        var right = await client.PostAsJsonAsync(Login, new { email, password });
        right.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeOfAsync(right)).ShouldBe("auth.locked_out");
        await ShouldHaveNoSessionAsync(client);

        // The lock is the configured five minutes from the fifth failure, not a permanent one.
        var lockEnd = await LockoutEndAsync(email);
        lockEnd.ShouldNotBeNull();
        lockEnd.Value.ShouldBeInRange(DateTimeOffset.UtcNow.AddMinutes(4), DateTimeOffset.UtcNow.AddMinutes(5).AddSeconds(5));

        await SetLockoutEndAsync(email, DateTimeOffset.UtcNow.AddSeconds(-1));

        var afterExpiry = await client.PostAsJsonAsync(Login, new { email, password });
        afterExpiry.StatusCode.ShouldBe(HttpStatusCode.OK, await afterExpiry.Content.ReadAsStringAsync());
        await ShouldHaveSessionAsync(client);
        (await FailedAttemptsAsync(email)).ShouldBe(0);
    }

    [Fact]
    public async Task A_successful_sign_in_clears_the_count_of_wrong_passwords()
    {
        var email = Email("count");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);
        using var client = Browser();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            (await client.PostAsJsonAsync(Login, new { email, password = WrongPassword }))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        (await FailedAttemptsAsync(email)).ShouldBe(4);

        (await client.PostAsJsonAsync(Login, new { email, password = AuthHelper.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await FailedAttemptsAsync(email)).ShouldBe(0);

        // Four more do not lock: the count started again from the successful sign-in.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var wrong = await client.PostAsJsonAsync(Login, new { email, password = WrongPassword });
            (await CodeOfAsync(wrong)).ShouldBe("auth.invalid_credentials");
        }

        (await FailedAttemptsAsync(email)).ShouldBe(4);
    }

    [Fact]
    public async Task Five_wrong_authenticator_codes_at_sign_in_lock_the_account_as_wrong_passwords_do()
    {
        var (email, sharedKey, _) = await TwoFactorAccountAsync("totp");
        using var client = Browser();

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var wrong = await client.PostAsJsonAsync(
                Login, new { email, password = AuthHelper.Password, twoFactorCode = "000000" });
            wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await CodeOfAsync(wrong)).ShouldBe("auth.mfa_invalid", $"attempt {attempt}");
        }

        var fifth = await client.PostAsJsonAsync(
            Login, new { email, password = AuthHelper.Password, twoFactorCode = "000000" });
        (await CodeOfAsync(fifth)).ShouldBe("auth.locked_out");

        // The right password with the right code is refused while the lock lasts…
        var right = await client.PostAsJsonAsync(
            Login, new { email, password = AuthHelper.Password, twoFactorCode = TotpCodes.Generate(sharedKey) });
        right.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeOfAsync(right)).ShouldBe("auth.locked_out");
        await ShouldHaveNoSessionAsync(client);

        // …and accepted once it has ended.
        await SetLockoutEndAsync(email, DateTimeOffset.UtcNow.AddSeconds(-1));
        var afterExpiry = await client.PostAsJsonAsync(
            Login, new { email, password = AuthHelper.Password, twoFactorCode = TotpCodes.Generate(sharedKey) });
        afterExpiry.StatusCode.ShouldBe(HttpStatusCode.OK, await afterExpiry.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_recovery_code_is_refused_while_the_account_is_locked_and_is_not_spent()
    {
        var (email, _, recoveryCodes) = await TwoFactorAccountAsync("recovery");
        using var client = Browser();

        // The password is accepted and the sign-in waits for the second factor…
        var pending = await client.PostAsJsonAsync(Login, new { email, password = AuthHelper.Password });
        (await CodeOfAsync(pending)).ShouldBe("auth.mfa_required");

        // …and the account is locked while it waits (an administrator, or wrong codes elsewhere).
        await SetLockoutEndAsync(email, DateTimeOffset.UtcNow.AddHours(1));

        var onPending = await client.PostAsJsonAsync(
            "/api/v1/auth/2fa/verify", new { code = $"recovery:{recoveryCodes[0]}" });
        onPending.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeOfAsync(onPending)).ShouldBe("auth.locked_out");
        await ShouldHaveNoSessionAsync(client);

        var withPassword = await Browser().PostAsJsonAsync(
            Login, new { email, password = AuthHelper.Password, twoFactorCode = $"recovery:{recoveryCodes[0]}" });
        (await CodeOfAsync(withPassword)).ShouldBe("auth.locked_out");

        // The refusals did not spend the code: once the lock ends, the same code signs in.
        await SetLockoutEndAsync(email, null);
        using var later = Browser();
        var unlocked = await later.PostAsJsonAsync(
            Login, new { email, password = AuthHelper.Password, twoFactorCode = $"recovery:{recoveryCodes[0]}" });
        unlocked.StatusCode.ShouldBe(HttpStatusCode.OK, await unlocked.Content.ReadAsStringAsync());
        await ShouldHaveSessionAsync(later);
    }

    [Fact]
    public async Task The_session_cookie_is_secure_behind_a_trusted_tls_proxy_and_only_there()
    {
        var email = Email("cookie");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);

        // The test server has no peer of its own, which the forwarded-headers middleware treats
        // as the trusted proxy in front of it — the packaged nginx in a real deployment.
        var behindProxy = await SessionCookieAsync(email, peer: null);
        behindProxy.ShouldContain("secure");
        behindProxy.ShouldContain("httponly");
        behindProxy.ShouldContain("samesite=lax");

        // The same header from a peer outside the trusted networks is ignored: the request stays
        // plain HTTP, and the cookie is issued for what the request really was.
        var direct = await SessionCookieAsync(email, peer: IPAddress.Parse("203.0.113.9"));
        direct.ShouldNotContain("secure");
        direct.ShouldContain("httponly");
        direct.ShouldContain("samesite=lax");
    }

    [Fact]
    public async Task A_reset_token_works_once_and_only_for_the_account_it_was_mailed_to()
    {
        var owner = Email("reset-a");
        var other = Email("reset-b");
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, owner);
        var otherId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, other);
        using var client = Browser();

        factory.Messages.Clear();
        (await client.PostAsJsonAsync("/api/v1/auth/password/forgot", new { email = owner }))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var token = QueryHelpers.ParseQuery(factory.Messages.LastTo(owner).Link.Query)["token"].ToString();
        token.ShouldNotBeNullOrWhiteSpace();

        // Another account's address with that token, a made-up token, and an address with no
        // account all answer the same refusal — and none of them warns anybody of a change.
        foreach (var (email, attempt) in new[] { (other, token), (owner, "bogus"), (Email("nobody"), token) })
        {
            var refused = await Reset(client, email, attempt, "chosen-by-attacker-1");
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await CodeOfAsync(refused)).ShouldBe("auth.reset_invalid");
        }

        (await PasswordWarningsAsync(otherId)).ShouldBe(0);
        (await PasswordWarningsAsync(ownerId)).ShouldBe(0);
        (await client.PostAsJsonAsync(Login, new { email = other, password = AuthHelper.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The mailed token resets the password it was mailed for, once.
        (await Reset(client, owner, token, "a-new-long-password-1")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PasswordWarningsAsync(ownerId)).ShouldBe(1);
        (await client.PostAsJsonAsync(Login, new { email = owner, password = "a-new-long-password-1" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsJsonAsync(Login, new { email = owner, password = AuthHelper.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var replay = await Reset(client, owner, token, "chosen-by-attacker-1");
        replay.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(replay)).ShouldBe("auth.reset_invalid");
        (await PasswordWarningsAsync(ownerId)).ShouldBe(1);
        (await client.PostAsJsonAsync(Login, new { email = owner, password = "chosen-by-attacker-1" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_reset_token_past_its_lifetime_is_refused()
    {
        // The shipped lifetime is a day; this host shortens it to nothing so a token is already
        // past it when it arrives, which is the same check without waiting a day.
        factory.Services.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>()
            .Value.TokenLifespan.ShouldBe(TimeSpan.FromHours(24));
        using var expiring = new SilexGisApiFactory(
            postgres.ConnectionString,
            Settings(),
            services => services.PostConfigure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.Zero));

        var email = Email("expired");
        _ = await AuthHelper.CreateUserAsync(expiring, GlobalRoles.Editor, email);
        using var client = expiring.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        expiring.Messages.Clear();
        (await client.PostAsJsonAsync("/api/v1/auth/password/forgot", new { email }))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var token = QueryHelpers.ParseQuery(expiring.Messages.LastTo(email).Link.Query)["token"].ToString();

        var refused = await Reset(client, email, token, "a-new-long-password-1");
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(refused)).ShouldBe("auth.reset_invalid");
        (await client.PostAsJsonAsync(Login, new { email, password = AuthHelper.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Registration_refuses_a_short_password_and_a_malformed_address_with_its_code()
    {
        // The positive half: a well-formed request registers.
        (await Browser().PostAsJsonAsync(
            "/api/v1/auth/register", new { email = Email("reg-ok"), password = "a-long-password-1" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var shortEmail = Email("reg-short");
        var tooShort = await Browser().PostAsJsonAsync(
            "/api/v1/auth/register", new { email = shortEmail, password = "nine-char" });
        tooShort.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var shortBody = await tooShort.Content.ReadFromJsonAsync<JsonElement>();
        shortBody.GetProperty("code").GetString().ShouldBe("auth.registration_invalid");
        shortBody.GetProperty("errors").TryGetProperty("PasswordTooShort", out _).ShouldBeTrue(shortBody.ToString());

        var malformed = await Browser().PostAsJsonAsync(
            "/api/v1/auth/register", new { email = "not-an-address", password = "a-long-password-1" });
        malformed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var malformedBody = await malformed.Content.ReadFromJsonAsync<JsonElement>();
        malformedBody.GetProperty("code").GetString().ShouldBe("auth.registration_invalid");
        malformedBody.GetProperty("errors").TryGetProperty("InvalidEmail", out _).ShouldBeTrue(malformedBody.ToString());

        // Neither refusal left an account behind.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Users.CountAsync(u => u.Email == shortEmail || u.Email == "not-an-address")).ShouldBe(0);
    }

    [Fact]
    public async Task A_registration_that_fails_after_the_account_is_saved_leaves_no_account_behind()
    {
        // The roster entry is saved after Identity has saved the account. A constraint the roster
        // row cannot meet stands in for any failure at that point; NOT VALID leaves existing rows
        // alone, and the constraint is dropped again whatever the outcome.
        const string refusedName = "A roster name this database refuses";
        const string constraint = "test_cavers_refuse_one_name";
        await ExecuteSqlAsync(
            "ALTER TABLE cavers ADD CONSTRAINT " + constraint
            + " CHECK (full_name <> '" + refusedName + "') NOT VALID");

        var email = Email("atomic");
        const string password = "a-long-password-1";
        try
        {
            var failed = await Browser().PostAsJsonAsync(
                "/api/v1/auth/register", new { email, password, displayName = refusedName });
            failed.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        }
        finally
        {
            await ExecuteSqlAsync("ALTER TABLE cavers DROP CONSTRAINT " + constraint);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Users.CountAsync(u => u.Email == email)).ShouldBe(0, "the account outlived its roster entry");
        }

        // So the person can simply try again, rather than being told the address is taken.
        var retry = await Browser().PostAsJsonAsync(
            "/api/v1/auth/register", new { email, password, displayName = "Ana" });
        retry.StatusCode.ShouldBe(HttpStatusCode.Created, await retry.Content.ReadAsStringAsync());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var userId = (await db.Users.SingleAsync(u => u.Email == email)).Id;
            (await db.Cavers.CountAsync(c => c.UserId == userId)).ShouldBe(1);
        }
    }

    [Fact]
    public async Task A_request_missing_its_password_is_refused_alike_for_known_and_unknown_addresses()
    {
        var known = Email("body-known");
        var id = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, known);
        using var client = Browser();

        // The positive half: the full request signs in.
        (await client.PostAsJsonAsync(Login, new { email = known, password = AuthHelper.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // An omitted password reached Identity's hasher as null and failed for a known address only,
        // so the answer told an anonymous caller which addresses have accounts.
        foreach (var email in new[] { known, Email("body-unknown") })
        {
            var missing = await client.PostAsJsonAsync(Login, new { email });
            missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest, email);
            (await CodeOfAsync(missing)).ShouldBe("validation.failed", email);

            var huge = await client.PostAsJsonAsync(Login, new { email, password = new string('p', 10_000) });
            huge.StatusCode.ShouldBe(HttpStatusCode.BadRequest, email);
            (await CodeOfAsync(huge)).ShouldBe("validation.failed", email);
        }

        // Nor did either refusal count as a wrong password.
        (await FailedAttemptsAsync(known)).ShouldBe(0);

        var bodies = new (string Route, object Body)[]
        {
            ("/api/v1/auth/register", new { email = Email("body-reg") }),
            // Longer than the display-name column: refused here rather than failing the save.
            ("/api/v1/auth/register", new { email = Email("body-name"), password = "a-long-password-1", displayName = new string('n', 101) }),
            ("/api/v1/auth/password/reset", new { email = known, token = "a-token" }),
            ("/api/v1/auth/password/reset", new { email = known, newPassword = "a-long-password-1" }),
            ("/api/v1/auth/email/confirm", new { userId = id }),
            ("/api/v1/auth/password/forgot", new { email = new string('e', 300) + "@t.local" }),
        };
        foreach (var (route, body) in bodies)
        {
            var refused = await client.PostAsJsonAsync(route, body);
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, route);
            (await CodeOfAsync(refused)).ShouldBe("validation.failed", route);
        }

        // An address with no account still gets the deliberately uninformative answer.
        (await client.PostAsJsonAsync("/api/v1/auth/password/forgot", new { email = Email("body-none") }))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task A_reset_refused_for_its_password_says_so_and_leaves_the_link_usable()
    {
        var email = Email("reset-weak");
        var id = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);
        using var client = Browser();
        factory.Messages.Clear();
        (await client.PostAsJsonAsync("/api/v1/auth/password/forgot", new { email }))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var token = QueryHelpers.ParseQuery(factory.Messages.LastTo(email).Link.Query)["token"].ToString();

        var weak = await Reset(client, email, token, "short");
        weak.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await weak.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().ShouldBe("validation.failed", body.ToString());
        body.GetProperty("errors").TryGetProperty("NewPassword", out _).ShouldBeTrue(body.ToString());
        (await PasswordWarningsAsync(id)).ShouldBe(0);

        // The link was good all along: the same token now resets the password.
        (await Reset(client, email, token, "a-new-long-password-1")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync(Login, new { email, password = "a-new-long-password-1" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Taking_up_an_invitation_confirms_the_address_so_the_first_sign_in_is_let_in()
    {
        // An invited account as the administrators' page leaves it: no password, no provider, an
        // address never confirmed.
        var invited = Email("invited");
        var uninvited = Email("unconfirmed");
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
            (await users.CreateAsync(new SilexGisUser { UserName = invited, Email = invited })).Succeeded.ShouldBeTrue();
            (await users.CreateAsync(new SilexGisUser { UserName = uninvited, Email = uninvited }, AuthHelper.Password))
                .Succeeded.ShouldBeTrue();
        }

        await SetSecurityAsync(policy => policy with { RequireConfirmedEmail = true });
        try
        {
            using var client = Browser();
            // The policy really is on: an unconfirmed account with a password is held back.
            var held = await client.PostAsJsonAsync(Login, new { email = uninvited, password = AuthHelper.Password });
            held.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await CodeOfAsync(held)).ShouldBe("auth.email_not_confirmed");

            factory.Messages.Clear();
            (await client.PostAsJsonAsync("/api/v1/auth/password/forgot", new { email = invited }))
                .StatusCode.ShouldBe(HttpStatusCode.Accepted);
            var token = QueryHelpers.ParseQuery(factory.Messages.LastTo(invited).Link.Query)["token"].ToString();
            (await Reset(client, invited, token, "a-first-password-1")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

            var first = await client.PostAsJsonAsync(Login, new { email = invited, password = "a-first-password-1" });
            first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        }
        finally
        {
            await SetSecurityAsync(policy => policy with { RequireConfirmedEmail = false });
        }
    }

    [Fact]
    public async Task Setting_up_the_authenticator_again_while_it_is_on_is_refused_and_keeps_the_old_key()
    {
        var email = Email("reenrol");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);
        using var client = await AuthHelper.BearerClientAsync(factory, email);

        // The positive half: the first setup works and switches the method on.
        var enroll = await client.PostAsync("/api/v1/me/mfa/enroll", null);
        enroll.StatusCode.ShouldBe(HttpStatusCode.OK);
        var key = (await enroll.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("sharedKey").GetString()!.Replace(" ", string.Empty);
        (await client.PostAsJsonAsync("/api/v1/me/mfa/methods/authenticator", new { code = TotpCodes.Generate(key) }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Opening the setup again used to rotate the key under a method still switched on.
        var again = await client.PostAsync("/api/v1/me/mfa/enroll", null);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(again)).ShouldBe("auth.mfa_already_enabled");

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
            var user = (await users.FindByEmailAsync(email))!;
            // The set-up answer prints the key in lower case for reading; Base32 ignores case.
            (await users.GetAuthenticatorKeyAsync(user)).ShouldBe(key, StringCompareShould.IgnoreCase);
            (await users.VerifyTwoFactorTokenAsync(
                user, users.Options.Tokens.AuthenticatorTokenProvider, TotpCodes.Generate(key))).ShouldBeTrue();
        }

        // Switched off, it can be set up afresh.
        (await client.DeleteAsync("/api/v1/me/mfa/methods/authenticator")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsync("/api/v1/me/mfa/enroll", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    public void Dispose() => factory.Dispose();

    private static string Email(string what) => $"{what}-{Guid.NewGuid():N}@t.local";

    private HttpClient Browser() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static Task<HttpResponseMessage> Reset(HttpClient client, string email, string token, string newPassword) =>
        client.PostAsJsonAsync("/api/v1/auth/password/reset", new { email, token, newPassword });

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    /// <summary>Signs in with the TLS proxy's scheme header and returns the session cookie's attributes.</summary>
    private async Task<string> SessionCookieAsync(string email, IPAddress? peer)
    {
        using var handler = factory.Server.CreateHandler(context =>
        {
            if (peer is not null)
            {
                context.Connection.RemoteIpAddress = peer;
            }
        });
        using var client = new HttpClient(handler) { BaseAddress = factory.Server.BaseAddress };
        using var request = new HttpRequestMessage(HttpMethod.Post, Login)
        {
            Content = JsonContent.Create(new { email, password = AuthHelper.Password }),
        };
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "198.51.100.7");

        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.TryGetValues("Set-Cookie", out var cookies).ShouldBeTrue();
        var session = cookies!.SingleOrDefault(c => c.StartsWith("silexgis.session=", StringComparison.Ordinal));
        session.ShouldNotBeNull(string.Join(" | ", cookies!.Select(c => c.Split('=')[0])));
        return session.ToLowerInvariant();
    }

    private static async Task ShouldHaveNoSessionAsync(HttpClient client)
    {
        var authorize = await client.GetAsync(AuthHelper.AuthorizeUrl());
        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        authorize.Headers.Location!.OriginalString.ShouldStartWith("/login?returnUrl=");
    }

    private static async Task ShouldHaveSessionAsync(HttpClient client)
    {
        var authorize = await client.GetAsync(AuthHelper.AuthorizeUrl());
        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        authorize.Headers.Location!.ToString().ShouldContain("code=");
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private async Task<int> FailedAttemptsAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
        return (await users.FindByEmailAsync(email))!.AccessFailedCount;
    }

    private async Task<DateTimeOffset?> LockoutEndAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
        return (await users.FindByEmailAsync(email))!.LockoutEnd;
    }

    private async Task SetLockoutEndAsync(string email, DateTimeOffset? end)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        user.LockoutEnabled.ShouldBeTrue("an account that cannot be locked would make every lock test vacuous");
        (await users.SetLockoutEndDateAsync(user, end)).Succeeded.ShouldBeTrue();
    }

    private async Task SetSecurityAsync(Func<SecuritySettings, SecuritySettings> change)
    {
        using var scope = factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IAppSettingsService>();
        await settings.SaveAsync(AppSettingSections.Security, change(await settings.GetSecurityAsync()));
    }

    private async Task<int> PasswordWarningsAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Notifications.AsNoTracking().CountAsync(n =>
            n.RecipientUserId == userId && n.TemplateKey == MessageTemplateCatalog.NotifySecurityPasswordChanged);
    }

    /// <summary>An account with the authenticator switched on, set up as the enrolment endpoints leave it.</summary>
    private async Task<(string Email, string SharedKey, IReadOnlyList<string> RecoveryCodes)> TwoFactorAccountAsync(string what)
    {
        var email = Email(what);
        var id = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
        var user = (await users.FindByIdAsync(id.ToString()))!;
        await users.ResetAuthenticatorKeyAsync(user);
        var key = (await users.GetAuthenticatorKeyAsync(user))!;
        user.TwoFactorAuthenticatorEnabled = true;
        user.TwoFactorEnabled = true;
        (await users.UpdateAsync(user)).Succeeded.ShouldBeTrue();
        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))!.ToList();
        return (email, key, codes);
    }
}
