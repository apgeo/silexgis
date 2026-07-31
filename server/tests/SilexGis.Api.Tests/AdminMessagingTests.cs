// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The administrator's messaging settings and message wording: who may see them, what happens to
/// the secrets, and the rules the template editor enforces.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdminMessagingTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private string adminEmail = null!;
    private string editorEmail = null!;

    public AdminMessagingTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Auth:RateLimitPerMinute"] = "200",
        });

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        adminEmail = $"admin-msg-{suffix}@t.local";
        editorEmail = $"editor-msg-{suffix}@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, adminEmail);
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, editorEmail);
    }

    [Fact]
    public async Task Only_an_administrator_may_read_or_change_the_settings()
    {
        var editor = await AuthHelper.BearerClientAsync(factory, editorEmail);

        (await editor.GetAsync("/api/v1/admin/settings/")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await editor.GetAsync("/api/v1/admin/message-templates/")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await editor.PutAsJsonAsync("/api/v1/admin/settings/security", Policy()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/admin/settings/")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_saved_password_is_never_sent_back_and_survives_a_save_that_omits_it()
    {
        var admin = await AuthHelper.BearerClientAsync(factory, adminEmail);

        var saved = await admin.PutAsJsonAsync("/api/v1/admin/settings/mail", Mail(password: "s3cret-relay-pass"));
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        var body = await saved.Content.ReadAsStringAsync();
        body.ShouldNotContain("s3cret-relay-pass");
        var mail = JsonDocument.Parse(body).RootElement.GetProperty("mail");
        mail.TryGetProperty("password", out _).ShouldBeFalse();
        mail.GetProperty("hasPassword").GetBoolean().ShouldBeTrue();

        // Saving again with no password — which is all the page can send — keeps the stored one.
        var resaved = await admin.PutAsJsonAsync("/api/v1/admin/settings/mail", Mail(password: null, fromName: "Renamed"));
        resaved.StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = (await resaved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mail");
        after.GetProperty("hasPassword").GetBoolean().ShouldBeTrue();
        after.GetProperty("fromName").GetString().ShouldBe("Renamed");

        await AssertStoredPasswordAsync("s3cret-relay-pass");

        // An explicit empty string is the way to clear it.
        var cleared = await admin.PutAsJsonAsync("/api/v1/admin/settings/mail", Mail(password: string.Empty));
        (await cleared.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("mail").GetProperty("hasPassword").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Saved_settings_replace_the_configured_ones_for_the_next_request()
    {
        var admin = await AuthHelper.BearerClientAsync(factory, adminEmail);

        await admin.PutAsJsonAsync("/api/v1/admin/settings/security", Policy() with
        {
            SmsTwoFactorEnabled = true,
            TwoFactorCodeLifetimeMinutes = 9,
        });

        var read = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/settings/");
        var security = read.GetProperty("security");
        security.GetProperty("smsTwoFactorEnabled").GetBoolean().ShouldBeTrue();
        security.GetProperty("twoFactorCodeLifetimeMinutes").GetInt32().ShouldBe(9);
    }

    [Fact]
    public async Task An_out_of_range_policy_is_rejected()
    {
        var admin = await AuthHelper.BearerClientAsync(factory, adminEmail);

        (await admin.PutAsJsonAsync("/api/v1/admin/settings/security", Policy() with { TwoFactorCodeLifetimeMinutes = 0 }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutAsJsonAsync("/api/v1/admin/settings/sms", Sms(url: "not-a-url")))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutAsJsonAsync("/api/v1/admin/settings/sms", Sms(method: "DELETE")))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Every_message_is_listed_with_the_wording_that_ships_with_it()
    {
        var admin = await AuthHelper.BearerClientAsync(factory, adminEmail);

        var templates = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/message-templates/");
        var keys = templates.EnumerateArray().Select(x => x.GetProperty("key").GetString()).ToList();

        keys.Count.ShouldBe(MessageTemplateCatalog.All.Count);
        keys.ShouldContain(MessageTemplateCatalog.EmailTwoFactorCode);
        keys.ShouldContain(MessageTemplateCatalog.SmsVerifyPhone);

        var first = templates.EnumerateArray().First();
        first.GetProperty("locales").EnumerateArray().Count().ShouldBe(MessageTemplateCatalog.Locales.Count);
        first.GetProperty("locales").EnumerateArray()
            .All(l => !l.GetProperty("customised").GetBoolean()).ShouldBeTrue();
    }

    [Fact]
    public async Task A_rewritten_message_is_the_one_that_actually_gets_sent()
    {
        var admin = await AuthHelper.BearerClientAsync(factory, adminEmail);
        // This test sends the same message twice on purpose; the resend throttle is not what is
        // under test here, so it is turned off rather than waited out.
        await admin.PutAsJsonAsync(
            "/api/v1/admin/settings/security", Policy() with { TwoFactorResendIntervalSeconds = 0 });

        var save = await admin.PutAsJsonAsync(
            $"/api/v1/admin/message-templates/{MessageTemplateCatalog.EmailTwoFactorCode}/en",
            new { subject = "Your club code", body = "Cod: {code}. Expires in {expiresMinutes} minutes." });
        save.StatusCode.ShouldBe(HttpStatusCode.NoContent, await save.Content.ReadAsStringAsync());

        factory.Messages.Clear();
        var user = await AuthHelper.BearerClientAsync(factory, adminEmail);
        (await user.PostAsync("/api/v1/me/mfa/methods/email/challenge", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var sent = factory.Messages.LastTo(adminEmail);
        sent.Subject.ShouldBe("Your club code");
        sent.Body.ShouldStartWith("Cod: ");

        // Resetting brings the shipped wording back.
        (await admin.DeleteAsync($"/api/v1/admin/message-templates/{MessageTemplateCatalog.EmailTwoFactorCode}/en"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        factory.Messages.Clear();
        await user.PostAsync("/api/v1/me/mfa/methods/email/challenge", null);
        factory.Messages.LastTo(adminEmail).Subject.ShouldNotBe("Your club code");
    }

    [Fact]
    public async Task A_placeholder_the_message_never_receives_is_refused()
    {
        var admin = await AuthHelper.BearerClientAsync(factory, adminEmail);

        var response = await admin.PutAsJsonAsync(
            $"/api/v1/admin/message-templates/{MessageTemplateCatalog.SmsTwoFactorCode}/en",
            new { body = "Your code {code} for {caveName}." });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe("admin.template_placeholder_unknown");
        // The message names the offender rather than leaving the operator to guess.
        problem.GetProperty("detail").GetString()!.ShouldContain("{caveName}");
    }

    [Fact]
    public async Task An_unknown_message_or_language_is_a_not_found()
    {
        var admin = await AuthHelper.BearerClientAsync(factory, adminEmail);

        (await admin.PutAsJsonAsync("/api/v1/admin/message-templates/email.invented/en", new { subject = "x", body = "y" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.PutAsJsonAsync(
                $"/api/v1/admin/message-templates/{MessageTemplateCatalog.EmailPasswordReset}/de",
                new { subject = "x", body = "y" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_email_template_must_keep_a_subject()
    {
        var admin = await AuthHelper.BearerClientAsync(factory, adminEmail);

        var response = await admin.PutAsJsonAsync(
            $"/api/v1/admin/message-templates/{MessageTemplateCatalog.EmailPasswordReset}/en",
            new { subject = "   ", body = "Open {url}." });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("admin.template_subject_required");
    }

    [Fact]
    public async Task The_test_send_reports_the_failure_rather_than_hiding_it()
    {
        var admin = await AuthHelper.BearerClientAsync(factory, adminEmail);

        factory.Messages.FailNextSend = true;
        var failed = await admin.PostAsJsonAsync("/api/v1/admin/settings/mail/test", new { recipient = adminEmail });
        failed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await failed.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("sent").GetBoolean().ShouldBeFalse();
        body.GetProperty("error").GetString().ShouldNotBeNullOrWhiteSpace();

        var succeeded = await admin.PostAsJsonAsync("/api/v1/admin/settings/mail/test", new { recipient = adminEmail });
        (await succeeded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sent").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task A_test_send_on_an_unconfigured_channel_says_so()
    {
        var admin = await AuthHelper.BearerClientAsync(factory, adminEmail);
        factory.Messages.SmsConfigured = false;
        try
        {
            var response = await admin.PostAsJsonAsync("/api/v1/admin/settings/sms/test", new { recipient = "+40712345678" });

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code").GetString().ShouldBe("admin.sms_not_configured");
        }
        finally
        {
            factory.Messages.SmsConfigured = true;
        }
    }

    private static SecuritySettingsBody Policy() => new(false, true, true, true, false, 5, 60);

    private static object Mail(string? password, string fromName = "SilexGIS") => new
    {
        enabled = true,
        host = "smtp.example.org",
        port = 587,
        security = "auto",
        username = "relay-user",
        password,
        fromAddress = "silexgis@example.org",
        fromName,
        replyTo = (string?)null,
        timeoutSeconds = 30,
        acceptInvalidCertificate = false,
    };

    private static object Sms(string url = "https://gateway.example.org/send", string method = "POST") => new
    {
        enabled = true,
        url,
        method,
        contentType = "application/x-www-form-urlencoded",
        bodyTemplate = "To={to}&Body={text}",
        headers = (Dictionary<string, string>?)null,
        authHeader = (string?)null,
        from = "+40700000000",
        timeoutSeconds = 15,
    };

    /// <summary>Reads the stored section straight from the database, past the redacting DTO.</summary>
    private async Task AssertStoredPasswordAsync(string expected)
    {
        using var scope = factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IAppSettingsService>();
        (await settings.GetMailAsync()).Password.ShouldBe(expected);
    }

    public async Task DisposeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.AppSettings.ExecuteDeleteAsync();
        await db.MessageTemplates.ExecuteDeleteAsync();
    }

    public void Dispose() => factory.Dispose();
}

/// <summary>Mirrors the security DTO so a test can build one with <c>with</c>.</summary>
public sealed record SecuritySettingsBody(
    bool RequireConfirmedEmail,
    bool SendConfirmationOnRegistration,
    bool AuthenticatorTwoFactorEnabled,
    bool EmailTwoFactorEnabled,
    bool SmsTwoFactorEnabled,
    int TwoFactorCodeLifetimeMinutes,
    int TwoFactorResendIntervalSeconds);
