// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Auth;
using SilexGis.Api.Diagnostics;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Auth;
using SilexGis.Domain.Messaging;
using SilexGis.Infrastructure.Email;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Sms;

namespace SilexGis.Api.Tests;

/// <summary>
/// An installation without a mail server or an SMS gateway logs that a message was due, to whom and
/// under what subject — and never the message, which is where the links and codes are.
/// </summary>
/// <remarks>
/// Driven through the real dispatcher, the real templates and the real logging transports, once per
/// message that carries a credential, with a marker as the credential so that finding it anywhere in
/// what was logged — rendered text or structured property — is a failure.
/// </remarks>
public sealed class LoggedMessageSecretsTests : IClassFixture<PostgresFixture>
{
    private const string Secret = "S3CR3T-MARKER-0123456789";
    private readonly PostgresFixture postgres;

    public LoggedMessageSecretsTests(PostgresFixture postgres) => this.postgres = postgres;

    private static SilexGisUser Person() => new()
    {
        Email = "person@example.test",
        UserName = "person@example.test",
        DisplayName = "Person",
        PhoneNumber = "+40700000000",
        Locale = "en",
    };

    private sealed record Case(string Name, Func<IMessageDispatcher, IConfiguration, SilexGisUser, Task<MessageResult>> Send);

    public static IEnumerable<TheoryDataRow<string>> Messages() =>
        Cases.Select(c => new TheoryDataRow<string>(c.Name));

    private static readonly Case[] Cases =
    [
        new("email confirmation", (d, c, u) => AccountMessages.SendEmailConfirmationAsync(d, c, u, Secret)),
        new("email change", (d, c, u) => AccountMessages.SendEmailChangeAsync(d, c, u, "new@example.test", Secret)),
        new("password reset", (d, c, u) => AccountMessages.SendPasswordResetAsync(d, c, u, Secret)),
        new("account invitation", (d, c, u) => AccountMessages.SendAccountInvitationAsync(d, c, u, Secret)),
        new("two-factor code by email", (d, _, u) =>
            AccountMessages.SendTwoFactorCodeAsync(d, u, TwoFactorMethod.Email, Secret, 10)),
        new("two-factor code by text", (d, _, u) =>
            AccountMessages.SendTwoFactorCodeAsync(d, u, TwoFactorMethod.Sms, Secret, 10)),
        new("phone verification", (d, _, u) =>
            AccountMessages.SendPhoneVerificationAsync(d, u, u.PhoneNumber!, Secret, 10)),
        // A notification email carries an opt-out link with a signed token in its values.
        new("notification email with an unsubscribe link", (d, _, u) =>
            d.SendAsync(
                MessageTemplateCatalog.NotifyDigest,
                u.Email!,
                u.Locale,
                new Dictionary<string, string> { ["unsubscribeUrl"] = "https://caves.example.test/unsubscribe?token=" + Secret })),
    ];

    [Theory]
    [MemberData(nameof(Messages))]
    public async Task A_message_with_no_transport_configured_is_logged_without_its_credential(string name)
    {
        var logs = new LogCapture();
        using var factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>(),
            services =>
            {
                // The two logging fallbacks themselves, in place of the capturing fakes.
                services.AddScoped<IEmailSender, LoggingEmailSender>();
                services.AddScoped<HttpSmsSender>();
                services.AddScoped<ISmsSender>(sp => sp.GetRequiredService<HttpSmsSender>());
                services.AddSingleton<Serilog.Core.ILogEventSink>(logs);
            });
        await using var scope = factory.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IMessageDispatcher>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var result = await Cases.Single(c => c.Name == name).Send(dispatcher, configuration, Person());

        result.Sent.ShouldBeTrue();

        // The positive half: the send was logged, with the recipient the log is for.
        var written = logs.Events
            .Where(e => e.RenderMessage().Contains("not sent", StringComparison.Ordinal))
            .ToList();
        written.Count.ShouldBe(1, "exactly one line says that the message went nowhere");
        written[0].RenderMessage().ShouldContain(
            name.Contains("text", StringComparison.Ordinal) || name.Contains("phone", StringComparison.Ordinal)
                ? "+40700000000"
                : "example.test");

        // The negative half: the credential is in no event, in no property, in any form.
        foreach (var e in logs.Events)
        {
            e.RenderMessage().ShouldNotContain(Secret);
            foreach (var property in e.Properties.Values)
            {
                property.ToString().ShouldNotContain(Secret);
            }
        }
    }

    /// <summary>The addresses that carry a credential in the query are redacted wherever they are written.</summary>
    [Theory]
    [InlineData("/reset-password?email=a%40b.test&token=" + Secret, "/reset-password?email=a%40b.test&token=[redacted]")]
    [InlineData("/confirm-email?user=1&token=" + Secret, "/confirm-email?user=1&token=[redacted]")]
    [InlineData("/reset-password?email=a%40b.test&token=" + Secret + "&invite=1",
        "/reset-password?email=a%40b.test&token=[redacted]&invite=1")]
    [InlineData("/settings/emails?confirm=" + Secret, "/settings/emails?confirm=[redacted]")]
    public void The_credential_in_an_account_flow_address_is_redacted(string address, string expected)
    {
        CredentialUrlScrubber.Scrub(address).ShouldBe(expected);
        // And twice over is the same as once, because two scrubbing points can meet on one line.
        CredentialUrlScrubber.Scrub(CredentialUrlScrubber.Scrub(address)).ShouldBe(expected);
    }

    /// <summary>A parameter called confirm is only a credential on the page that uses it for one.</summary>
    [Fact]
    public void A_confirm_flag_elsewhere_is_left_alone()
    {
        CredentialUrlScrubber.Scrub("/api/v1/things?confirm=true").ShouldBe("/api/v1/things?confirm=true");
    }
}
