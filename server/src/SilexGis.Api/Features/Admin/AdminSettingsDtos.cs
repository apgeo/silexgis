// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Settings;

namespace SilexGis.Api.Features.Admin;

/// <summary>
/// The mail settings as the admin page sees them — everything except the password, which is
/// replaced by whether one is stored. A secret that has been saved is never sent back out, so it
/// cannot be read off the wire or out of a browser's memory by anyone who reaches the page.
/// </summary>
public sealed record MailSettingsDto(
    bool Enabled,
    string Host,
    int Port,
    MailTransportSecurity Security,
    string? Username,
    bool HasPassword,
    string FromAddress,
    string FromName,
    string? ReplyTo,
    int TimeoutSeconds,
    bool AcceptInvalidCertificate);

/// <summary>
/// A mail settings save. <see cref="Password"/> follows the rule a write-only secret needs:
/// null leaves the stored one alone (the page never had it to send back), and an empty string
/// clears it — which is how an operator moves to a relay that wants no authentication.
/// </summary>
public sealed record MailSettingsWriteRequest(
    bool Enabled,
    string Host,
    int Port,
    MailTransportSecurity Security,
    string? Username,
    string? Password,
    string FromAddress,
    string FromName,
    string? ReplyTo,
    int TimeoutSeconds,
    bool AcceptInvalidCertificate);

public sealed record SmsSettingsDto(
    bool Enabled,
    string Url,
    string Method,
    string ContentType,
    string BodyTemplate,
    IReadOnlyDictionary<string, string> Headers,
    bool HasAuthHeader,
    string? From,
    int TimeoutSeconds);

public sealed record SmsSettingsWriteRequest(
    bool Enabled,
    string Url,
    string Method,
    string ContentType,
    string BodyTemplate,
    IReadOnlyDictionary<string, string>? Headers,
    string? AuthHeader,
    string? From,
    int TimeoutSeconds);

public sealed record SecuritySettingsDto(
    bool RequireConfirmedEmail,
    bool SendConfirmationOnRegistration,
    bool AuthenticatorTwoFactorEnabled,
    bool EmailTwoFactorEnabled,
    bool SmsTwoFactorEnabled,
    int TwoFactorCodeLifetimeMinutes,
    int TwoFactorResendIntervalSeconds);

/// <summary>
/// Everything the messaging admin page needs in one read, including whether each channel is
/// actually working — a page that only echoed the saved values could show a fully configured mail
/// server that has never delivered anything.
/// </summary>
public sealed record AdminSettingsDto(
    MailSettingsDto Mail,
    SmsSettingsDto Sms,
    SecuritySettingsDto Security,
    bool MailConfigured,
    bool SmsConfigured);

/// <summary>A diagnostic send, to prove the channel works before anyone depends on it.</summary>
public sealed record TestMessageRequest(string Recipient);

public sealed record TestMessageResultDto(bool Sent, string? Error);

public sealed class MailSettingsWriteRequestValidator : AbstractValidator<MailSettingsWriteRequest>
{
    public MailSettingsWriteRequestValidator()
    {
        // Host and sender are only required once the channel is switched on: an operator must be
        // able to save a half-filled form and come back to it.
        RuleFor(x => x.Host).NotEmpty().MaximumLength(255).When(x => x.Enabled);
        RuleFor(x => x.FromAddress).NotEmpty().EmailAddress().MaximumLength(256).When(x => x.Enabled);
        RuleFor(x => x.FromAddress).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.FromAddress));
        RuleFor(x => x.ReplyTo).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.ReplyTo));
        RuleFor(x => x.Port).InclusiveBetween(1, 65535);
        RuleFor(x => x.FromName).MaximumLength(100);
        RuleFor(x => x.Username).MaximumLength(256);
        RuleFor(x => x.TimeoutSeconds).InclusiveBetween(1, 300);
    }
}

public sealed class SmsSettingsWriteRequestValidator : AbstractValidator<SmsSettingsWriteRequest>
{
    private static readonly string[] AllowedMethods = ["GET", "POST", "PUT"];

    public SmsSettingsWriteRequestValidator()
    {
        RuleFor(x => x.Url).NotEmpty().When(x => x.Enabled);
        RuleFor(x => x.Url)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var parsed)
                && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
            .WithMessage("Enter an absolute http or https URL.")
            .When(x => !string.IsNullOrWhiteSpace(x.Url));
        RuleFor(x => x.Method)
            .Must(method => AllowedMethods.Contains(method?.ToUpperInvariant()))
            .WithMessage("Use GET, POST or PUT.");
        RuleFor(x => x.ContentType).MaximumLength(100);
        RuleFor(x => x.BodyTemplate).MaximumLength(2000);
        RuleFor(x => x.From).MaximumLength(32);
        RuleFor(x => x.TimeoutSeconds).InclusiveBetween(1, 120);
        RuleFor(x => x.Headers)
            .Must(headers => headers is null || headers.Count <= 10)
            .WithMessage("At most ten extra headers.");
    }
}

public sealed class SecuritySettingsDtoValidator : AbstractValidator<SecuritySettingsDto>
{
    public SecuritySettingsDtoValidator()
    {
        RuleFor(x => x.TwoFactorCodeLifetimeMinutes).InclusiveBetween(1, 60);
        RuleFor(x => x.TwoFactorResendIntervalSeconds).InclusiveBetween(0, 600);
    }
}

public sealed class TestMessageRequestValidator : AbstractValidator<TestMessageRequest>
{
    public TestMessageRequestValidator() => RuleFor(x => x.Recipient).NotEmpty().MaximumLength(256);
}
