// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The two delivered second factors end to end: enrolling with a code that really arrives,
/// signing in with one, and the rules that stop a method being switched on when it could not work.
/// </summary>
public sealed class TwoFactorChannelTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private string email = null!;

    public TwoFactorChannelTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Auth:RateLimitPerMinute"] = "200",
        });

    public async Task InitializeAsync()
    {
        email = $"tfa-{Guid.NewGuid():N}@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);
        // SMS is off by default; every test here needs the installation to permit it.
        await SetSecurityAsync(policy => policy with { SmsTwoFactorEnabled = true });
    }

    [Fact]
    public async Task Email_two_factor_is_enrolled_with_a_delivered_code_and_then_signs_in()
    {
        var client = await AuthHelper.BearerClientAsync(factory, email);

        // Enrolling starts by proving the code arrives.
        var challenge = await client.PostAsync("/api/v1/me/mfa/methods/email/challenge", null);
        challenge.StatusCode.ShouldBe(HttpStatusCode.OK, await challenge.Content.ReadAsStringAsync());
        (await challenge.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("destination").GetString()!.ShouldContain("@");

        var mailed = factory.Messages.LastTo(email);
        mailed.Channel.ShouldBe("email");

        // A wrong code does not switch anything on.
        (await client.PostAsJsonAsync("/api/v1/me/mfa/methods/email", new { code = "000000" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var enable = await client.PostAsJsonAsync("/api/v1/me/mfa/methods/email", new { code = mailed.Code });
        enable.StatusCode.ShouldBe(HttpStatusCode.OK, await enable.Content.ReadAsStringAsync());
        // First method on → recovery codes issued.
        (await enable.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("codes").EnumerateArray().Count().ShouldBe(10);

        // Signing in now stops and offers email.
        using var fresh = factory.CreateClient();
        var refused = await fresh.PostAsJsonAsync("/api/v1/auth/login", new { email, password = AuthHelper.Password });
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe("auth.mfa_required");
        problem.GetProperty("methods").EnumerateArray().Select(x => x.GetString()).ShouldBe(["email"]);

        // The code is requested against the half-finished sign-in, then completes it.
        factory.Messages.Clear();
        var sent = await fresh.PostAsJsonAsync("/api/v1/auth/2fa/send", new { method = "email" });
        sent.StatusCode.ShouldBe(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());

        var signIn = await fresh.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = AuthHelper.Password,
            twoFactorCode = factory.Messages.LastTo(email).Code,
            twoFactorMethod = "email",
        });
        signIn.StatusCode.ShouldBe(HttpStatusCode.OK, await signIn.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_delivered_code_cannot_be_used_twice()
    {
        var client = await AuthHelper.BearerClientAsync(factory, email);
        await client.PostAsync("/api/v1/me/mfa/methods/email/challenge", null);
        var code = factory.Messages.LastTo(email).Code;

        (await client.PostAsJsonAsync("/api/v1/me/mfa/methods/email", new { code }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Replaying it must fail — the whole reason these codes are stored rather than derived
        // from a rolling time window.
        await client.DeleteAsync("/api/v1/me/mfa/methods/email");
        (await client.PostAsJsonAsync("/api/v1/me/mfa/methods/email", new { code }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Email_two_factor_is_refused_while_the_address_is_unconfirmed()
    {
        var unconfirmed = $"tfa-unconf-{Guid.NewGuid():N}@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, unconfirmed);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.Users.Where(u => u.Email == unconfirmed)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailConfirmed, false));
        }

        var client = await AuthHelper.BearerClientAsync(factory, unconfirmed);
        var challenge = await client.PostAsync("/api/v1/me/mfa/methods/email/challenge", null);

        challenge.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await challenge.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("auth.mfa_email_unconfirmed");
    }

    [Fact]
    public async Task Sms_two_factor_needs_a_confirmed_number_first()
    {
        var client = await AuthHelper.BearerClientAsync(factory, email);

        var tooEarly = await client.PostAsync("/api/v1/me/mfa/methods/sms/challenge", null);
        tooEarly.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooEarly.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("auth.mfa_phone_unconfirmed");

        // Verify a number: the live one is not touched until the texted code comes back.
        var number = "+40712345678";
        var change = await client.PostAsJsonAsync("/api/v1/me/phone/change", new { phoneNumber = number });
        change.StatusCode.ShouldBe(HttpStatusCode.OK, await change.Content.ReadAsStringAsync());

        var texted = factory.Messages.LastTo(number);
        texted.Channel.ShouldBe("sms");

        var status = await client.GetFromJsonAsync<JsonElement>("/api/v1/me/phone/");
        status.GetProperty("confirmed").GetBoolean().ShouldBeFalse();
        status.GetProperty("pendingPhoneNumber").GetString().ShouldBe(number);

        (await client.PostAsJsonAsync("/api/v1/me/phone/confirm", new { code = "000000" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var confirm = await client.PostAsJsonAsync("/api/v1/me/phone/confirm", new { code = texted.Code });
        confirm.StatusCode.ShouldBe(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());
        (await confirm.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("confirmed").GetBoolean().ShouldBeTrue();

        // Now the method can be switched on.
        await client.PostAsync("/api/v1/me/mfa/methods/sms/challenge", null);
        var enable = await client.PostAsJsonAsync(
            "/api/v1/me/mfa/methods/sms", new { code = factory.Messages.LastTo(number).Code });
        enable.StatusCode.ShouldBe(HttpStatusCode.OK, await enable.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_national_phone_number_is_rejected_before_anything_is_texted()
    {
        var client = await AuthHelper.BearerClientAsync(factory, email);
        factory.Messages.Clear();

        var response = await client.PostAsJsonAsync("/api/v1/me/phone/change", new { phoneNumber = "0712345678" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        factory.Messages.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Removing_the_number_switches_texted_codes_off_with_it()
    {
        var client = await AuthHelper.BearerClientAsync(factory, email);
        var number = "+40722222222";
        await client.PostAsJsonAsync("/api/v1/me/phone/change", new { phoneNumber = number });
        await client.PostAsJsonAsync("/api/v1/me/phone/confirm", new { code = factory.Messages.LastTo(number).Code });
        await client.PostAsync("/api/v1/me/mfa/methods/sms/challenge", null);
        await client.PostAsJsonAsync("/api/v1/me/mfa/methods/sms", new { code = factory.Messages.LastTo(number).Code });

        (await client.DeleteAsync("/api/v1/me/phone")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Otherwise the account would still be asked for a texted code that can never arrive.
        var status = await client.GetFromJsonAsync<JsonElement>("/api/v1/me/mfa/");
        status.GetProperty("enabled").GetBoolean().ShouldBeFalse();
        status.GetProperty("methods").EnumerateArray()
            .First(m => m.GetProperty("method").GetString() == "sms")
            .GetProperty("enabled").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_method_the_installation_disallows_cannot_be_enrolled()
    {
        await SetSecurityAsync(policy => policy with { EmailTwoFactorEnabled = false });
        var client = await AuthHelper.BearerClientAsync(factory, email);

        var challenge = await client.PostAsync("/api/v1/me/mfa/methods/email/challenge", null);

        challenge.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await challenge.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("auth.mfa_method_not_allowed");
    }

    [Fact]
    public async Task Disallowing_a_method_afterwards_leaves_the_recovery_code_as_the_way_in()
    {
        var client = await AuthHelper.BearerClientAsync(factory, email);
        await client.PostAsync("/api/v1/me/mfa/methods/email/challenge", null);
        var enable = await client.PostAsJsonAsync(
            "/api/v1/me/mfa/methods/email", new { code = factory.Messages.LastTo(email).Code });
        var recoveryCodes = (await enable.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("codes").EnumerateArray().Select(x => x.GetString()!).ToList();

        // The administrator withdraws the only method this account had.
        await SetSecurityAsync(policy => policy with { EmailTwoFactorEnabled = false });

        using var fresh = factory.CreateClient();
        var refused = await fresh.PostAsJsonAsync("/api/v1/auth/login", new { email, password = AuthHelper.Password });
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("methods").EnumerateArray().ShouldBeEmpty();
        problem.GetProperty("recoveryAccepted").GetBoolean().ShouldBeTrue();

        // Not a lockout: the recovery code still works.
        var viaRecovery = await fresh.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = AuthHelper.Password,
            twoFactorCode = $"recovery:{recoveryCodes[0]}",
        });
        viaRecovery.StatusCode.ShouldBe(HttpStatusCode.OK, await viaRecovery.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Asking_for_a_code_without_a_pending_sign_in_is_unauthorised()
    {
        using var anonymous = factory.CreateClient();

        // No password has been accepted, so there is nobody to send a code to — and no way to
        // use this endpoint to mail an address of the caller's choosing.
        (await anonymous.PostAsJsonAsync("/api/v1/auth/2fa/send", new { method = "email" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Codes_cannot_be_requested_again_immediately()
    {
        var client = await AuthHelper.BearerClientAsync(factory, email);
        await client.PostAsync("/api/v1/me/mfa/methods/email/challenge", null);
        await client.PostAsJsonAsync("/api/v1/me/mfa/methods/email", new { code = factory.Messages.LastTo(email).Code });

        using var fresh = factory.CreateClient();
        await fresh.PostAsJsonAsync("/api/v1/auth/login", new { email, password = AuthHelper.Password });

        (await fresh.PostAsJsonAsync("/api/v1/auth/2fa/send", new { method = "email" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var again = await fresh.PostAsJsonAsync("/api/v1/auth/2fa/send", new { method = "email" });

        again.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await again.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("auth.mfa_resend_too_soon");
    }

    [Fact]
    public async Task An_emailed_code_and_a_texted_code_do_not_overwrite_each_other()
    {
        var client = await AuthHelper.BearerClientAsync(factory, email);

        // Confirm a number so both delivered methods are usable.
        var number = "+40733333333";
        await client.PostAsJsonAsync("/api/v1/me/phone/change", new { phoneNumber = number });
        await client.PostAsJsonAsync("/api/v1/me/phone/confirm", new { code = factory.Messages.LastTo(number).Code });

        await client.PostAsync("/api/v1/me/mfa/methods/email/challenge", null);
        var emailCode = factory.Messages.LastTo(email).Code;

        await client.PostAsync("/api/v1/me/mfa/methods/sms/challenge", null);
        var smsCode = factory.Messages.LastTo(number).Code;

        // Both providers answer the same Identity purpose, so a shared storage key would have
        // let the second code replace the first — and each validate against the other's channel.
        (await client.PostAsJsonAsync("/api/v1/me/mfa/methods/sms", new { code = emailCode }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await client.PostAsJsonAsync("/api/v1/me/mfa/methods/email", new { code = emailCode }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/v1/me/mfa/methods/sms", new { code = smsCode }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Enrolment_codes_cannot_be_requested_in_a_tight_loop()
    {
        var client = await AuthHelper.BearerClientAsync(factory, email);

        (await client.PostAsync("/api/v1/me/mfa/methods/email/challenge", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Without a per-account throttle one signed-in account could make the installation send
        // unbounded mail — and, once SMS is configured, spend the operator's money doing it.
        var again = await client.PostAsync("/api/v1/me/mfa/methods/email/challenge", null);
        again.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await again.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("auth.mfa_resend_too_soon");
    }

    private async Task SetSecurityAsync(Func<SecuritySettings, SecuritySettings> change)
    {
        using var scope = factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IAppSettingsService>();
        await settings.SaveAsync(AppSettingSections.Security, change(await settings.GetSecurityAsync()));
    }

    /// <summary>The settings row is shared with every other class in this collection.</summary>
    public async Task DisposeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.AppSettings.Where(s => s.Key == AppSettingSections.Security).ExecuteDeleteAsync();
    }

    public void Dispose() => factory.Dispose();
}
