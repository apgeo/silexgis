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
/// What the installation gives away about a protected position's surroundings.
/// </summary>
public sealed record ProtectionSettingsDto(bool RevealProtectedAssociations);

/// <summary>
/// Everything the installation-settings page needs in one read, including whether each channel is
/// actually working — a page that only echoed the saved values could show a fully configured mail
/// server that has never delivered anything.
/// </summary>
public sealed record AdminSettingsDto(
    MailSettingsDto Mail,
    SmsSettingsDto Sms,
    SecuritySettingsDto Security,
    ProtectionSettingsDto Protection,
    ImportSettingsDto Import,
    InterfaceSettingsDto Interface,
    bool MailConfigured,
    bool SmsConfigured);

/// <summary>
/// How much the installation trusts a vector file to become registry objects on its own, and
/// how far the two importers look around a candidate by default.
/// </summary>
/// <param name="PhotoProximityRadiusMeters">
/// How far a photograph looks for objects already in the registry. Wider than the vector-file
/// default on purpose: a picture is taken from where the photographer stood, which is rarely
/// where the thing they photographed is.
/// </param>
/// <param name="PhotoClusterRadiusMeters">
/// How far apart two photographs can be and still be proposed as one place.
/// </param>
public sealed record ImportSettingsDto(
    bool AllowCreateWithoutReview,
    double DuplicateRadiusMeters,
    double DuplicateNameSimilarity,
    double PhotoProximityRadiusMeters,
    double PhotoClusterRadiusMeters);

/// <summary>
/// The starting interface arrangement this installation publishes. The document is the client's
/// own preferences shape and is opaque here — the server stores it and never reads inside it.
/// </summary>
public sealed record InterfaceSettingsDto(string PanelDefaults);

public sealed class InterfaceSettingsDtoValidator : AbstractValidator<InterfaceSettingsDto>
{
    /// <summary>Room for an arrangement several times over, but not for using this as storage.</summary>
    private const int MaxRawLength = 8000;

    public InterfaceSettingsDtoValidator()
    {
        RuleFor(x => x.PanelDefaults).NotNull().MaximumLength(MaxRawLength);
        RuleFor(x => x.PanelDefaults)
            .Must(BeAJsonObject)
            .WithMessage("The starting arrangement must be a JSON object.");
    }

    private static bool BeAJsonObject(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        try
        {
            return System.Text.Json.JsonDocument.Parse(value).RootElement.ValueKind
                == System.Text.Json.JsonValueKind.Object;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}

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

/// <summary>
/// Nothing to reject: the section is a single switch, and both of its positions are valid
/// installation policy. The validator exists so the route is validated like every other one
/// rather than being the single exception someone later has to explain.
/// </summary>
public sealed class ProtectionSettingsDtoValidator : AbstractValidator<ProtectionSettingsDto>;

public sealed class ImportSettingsDtoValidator : AbstractValidator<ImportSettingsDto>
{
    public ImportSettingsDtoValidator()
    {
        // The radius bounds a proximity read that runs per page of candidates; the similarity
        // is a ratio and only means anything between the two ends of one.
        RuleFor(x => x.DuplicateRadiusMeters).InclusiveBetween(0, Domain.Import.ImportOptions.MaxDuplicateRadiusMeters);
        RuleFor(x => x.DuplicateNameSimilarity).InclusiveBetween(0, 1);
        RuleFor(x => x.PhotoProximityRadiusMeters)
            .InclusiveBetween(0, Domain.Import.PhotoImportOptions.MaxProximityRadiusMeters);
        RuleFor(x => x.PhotoClusterRadiusMeters)
            .InclusiveBetween(0, Domain.Import.PhotoImportOptions.MaxClusterRadiusMeters);
    }
}

public sealed class TestMessageRequestValidator : AbstractValidator<TestMessageRequest>
{
    public TestMessageRequestValidator() => RuleFor(x => x.Recipient).NotEmpty().MaximumLength(256);
}
