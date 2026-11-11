// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Auth;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Settings;

namespace SilexGis.Api.Tests;

/// <summary>
/// Every per-account send cooldown — sign-in codes, enrolment codes, phone-change texts and the
/// operator's test text — is measured on the application's clock. Each test sends once, is refused
/// just inside the interval and let through just past it, on a clock the test moves. A path still
/// reading the system clock stays refused after the move and fails here, where otherwise only a
/// wall-clock wait would show it.
/// </summary>
/// <remarks>
/// Its own class because the host runs on a clock that stands still unless a test moves it, which
/// every other test sharing a factory would trip over.
/// </remarks>
public sealed class SendThrottleClockTests : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly TestTimeProvider clock = new(DateTimeOffset.UtcNow);
    private readonly SilexGisApiFactory host;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    public SendThrottleClockTests(PostgresFixture postgres) =>
        host = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                // Each test makes several calls across the rate-limited credential surface.
                ["Auth:RateLimitPerMinute"] = "200",
            },
            services =>
            {
                JobWorkers.RemoveFrom(services);
                services.AddSingleton<TimeProvider>(clock);
            });

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task A_sign_in_code_can_be_asked_for_again_once_the_interval_has_passed()
    {
        var email = $"thr-signin-{suffix}@t.local";
        _ = await AuthHelper.CreateUserAsync(host, GlobalRoles.Editor, email);
        using (var owner = await AuthHelper.BearerClientAsync(host, email))
        {
            (await owner.PostAsync("/api/v1/me/mfa/methods/email/challenge", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await owner.PostAsJsonAsync("/api/v1/me/mfa/methods/email", new { code = host.Messages.LastTo(email).Code }))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var signingIn = host.CreateClient();
        (await signingIn.PostAsJsonAsync("/api/v1/auth/login", new { email, password = AuthHelper.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        await ShouldOpenAfterIntervalAsync(
            await ResendIntervalAsync(),
            () => signingIn.PostAsJsonAsync("/api/v1/auth/2fa/send", new { method = "email" }),
            "auth.mfa_resend_too_soon");
    }

    [Fact]
    public async Task An_enrolment_code_can_be_asked_for_again_once_the_interval_has_passed()
    {
        var email = $"thr-enrol-{suffix}@t.local";
        _ = await AuthHelper.CreateUserAsync(host, GlobalRoles.Editor, email);
        using var owner = await AuthHelper.BearerClientAsync(host, email);

        await ShouldOpenAfterIntervalAsync(
            await ResendIntervalAsync(),
            () => owner.PostAsync("/api/v1/me/mfa/methods/email/challenge", null),
            "auth.mfa_resend_too_soon");
    }

    [Fact]
    public async Task A_phone_change_text_can_be_asked_for_again_once_the_interval_has_passed()
    {
        var email = $"thr-phone-{suffix}@t.local";
        _ = await AuthHelper.CreateUserAsync(host, GlobalRoles.Editor, email);
        using var owner = await AuthHelper.BearerClientAsync(host, email);
        var attempt = 0;

        // A different number each time, so each call is a fresh change request rather than a
        // repeat of one the route might answer some other way.
        await ShouldOpenAfterIntervalAsync(
            await ResendIntervalAsync(),
            () => owner.PostAsJsonAsync("/api/v1/me/phone/change", new { phoneNumber = Number(++attempt) }),
            "me.phone_resend_too_soon");
    }

    [Fact]
    public async Task An_operator_test_text_can_be_sent_again_once_the_interval_has_passed()
    {
        var email = $"thr-admin-{suffix}@t.local";
        _ = await AuthHelper.CreateUserAsync(host, GlobalRoles.Admin, email);
        using var admin = await AuthHelper.BearerClientAsync(host, email);

        await ShouldOpenAfterIntervalAsync(
            SendThrottle.AdminTestInterval,
            () => admin.PostAsJsonAsync("/api/v1/admin/settings/sms/test", new { recipient = Number(1) }),
            "admin.sms_test_too_soon");
    }

    /// <summary>
    /// First send allowed; a send a little inside the interval refused with the cooldown's code;
    /// a send just past it allowed — measured from the send that went out, not the refusal.
    /// </summary>
    private async Task ShouldOpenAfterIntervalAsync(
        TimeSpan interval, Func<Task<HttpResponseMessage>> send, string tooSoonCode)
    {
        interval.ShouldBeGreaterThan(TimeSpan.FromSeconds(2));
        var start = clock.Now;

        var first = await send();
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());

        clock.Now = start + interval - TimeSpan.FromSeconds(2);
        var early = await send();
        early.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await early.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe(tooSoonCode);

        clock.Now = start + interval + TimeSpan.FromSeconds(1);
        var later = await send();
        later.StatusCode.ShouldBe(HttpStatusCode.OK, await later.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Read rather than assumed: the security settings row is shared with every other class, and
    /// the interval these routes use is the one stored there.
    /// </summary>
    private async Task<TimeSpan> ResendIntervalAsync()
    {
        using var scope = host.Services.CreateScope();
        var policy = await scope.ServiceProvider.GetRequiredService<IAppSettingsService>().GetSecurityAsync();
        return TimeSpan.FromSeconds(Math.Clamp(policy.TwoFactorResendIntervalSeconds, 0, 600));
    }

    /// <summary>
    /// A number unique to this class and this test run: the number is unique across accounts and
    /// every class shares one database.
    /// </summary>
    private string Number(int index) =>
        "+4" + ((uint)suffix.GetHashCode(StringComparison.Ordinal) % 100000000U)
            .ToString("D8", CultureInfo.InvariantCulture) + (50 + index).ToString("D2", CultureInfo.InvariantCulture);

    public async ValueTask DisposeAsync() => await host.DisposeAsync();
}
