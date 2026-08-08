// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Import;

namespace SilexGis.Domain.Settings;

/// <summary>How the SMTP conversation is secured.</summary>
public enum MailTransportSecurity : short
{
    /// <summary>Upgrade with STARTTLS when the server offers it; used unless told otherwise.</summary>
    Auto = 0,

    /// <summary>Plain text. Only sensible for a relay on the same host or a trusted network.</summary>
    None = 1,

    /// <summary>Connect in the clear and require the STARTTLS upgrade (typically port 587).</summary>
    StartTls = 2,

    /// <summary>TLS from the first byte (typically port 465).</summary>
    SslOnConnect = 3,
}

/// <summary>
/// Where mail goes. Blank host means no mail server: the application still works, and everything
/// that would have been sent is written to the log for the operator to pick up.
/// </summary>
public sealed record MailSettings
{
    public bool Enabled { get; init; }

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 587;

    public MailTransportSecurity Security { get; init; } = MailTransportSecurity.Auto;

    public string? Username { get; init; }

    /// <summary>Secret: accepted on write, never returned by the API.</summary>
    public string? Password { get; init; }

    public string FromAddress { get; init; } = string.Empty;

    public string FromName { get; init; } = "SilexGIS";

    /// <summary>Where replies should go, when that is not the sending address.</summary>
    public string? ReplyTo { get; init; }

    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Accept a certificate that does not validate. Needed by installations relaying through an
    /// internal server with a self-signed certificate; off by default because it removes the only
    /// protection against someone intercepting the credentials below.
    /// </summary>
    public bool AcceptInvalidCertificate { get; init; }

    /// <summary>Usable only when switched on and pointed at a host with a sender address.</summary>
    public bool IsUsable =>
        Enabled && !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress) && Port > 0;
}

/// <summary>
/// How text messages leave the building. Deliberately a description of an HTTP request rather
/// than a named vendor: every SMS gateway worth using accepts one, and an installation in a
/// country the big providers ignore can still point this at a local one.
/// </summary>
public sealed record SmsSettings
{
    public bool Enabled { get; init; }

    /// <summary>Gateway endpoint. May itself contain <c>{to}</c>, <c>{text}</c> and <c>{from}</c>.</summary>
    public string Url { get; init; } = string.Empty;

    public string Method { get; init; } = "POST";

    public string ContentType { get; init; } = "application/x-www-form-urlencoded";

    /// <summary>
    /// Request body with <c>{to}</c>, <c>{text}</c> and <c>{from}</c> substituted in. Values are
    /// escaped for the content type, so a message containing an ampersand or a quote cannot break
    /// the request apart.
    /// </summary>
    public string BodyTemplate { get; init; } = "To={to}&From={from}&Body={text}";

    /// <summary>Extra headers sent with every request (API keys that are not an Authorization).</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Secret: the whole <c>Authorization</c> value. Accepted on write, never returned.</summary>
    public string? AuthHeader { get; init; }

    /// <summary>Sender id or number, substituted as <c>{from}</c>.</summary>
    public string? From { get; init; }

    public int TimeoutSeconds { get; init; } = 15;

    public bool IsUsable => Enabled && !string.IsNullOrWhiteSpace(Url);
}

/// <summary>Sign-in policy an administrator can change without redeploying.</summary>
public sealed record SecuritySettings
{
    /// <summary>
    /// Refuse sign-in until the address is confirmed. Inert while no mail server is configured —
    /// switching it on with no way to send the confirmation would lock out the whole installation,
    /// which is exactly the mistake this guard exists to prevent.
    /// </summary>
    public bool RequireConfirmedEmail { get; init; }

    /// <summary>Send a confirmation message when an account is created.</summary>
    public bool SendConfirmationOnRegistration { get; init; } = true;

    public bool AuthenticatorTwoFactorEnabled { get; init; } = true;

    public bool EmailTwoFactorEnabled { get; init; } = true;

    /// <summary>
    /// Off by default. A texted code is the weakest of the three — it can be intercepted by
    /// persuading a mobile operator to move a number to a new SIM — so an installation opts in
    /// rather than inherits it.
    /// </summary>
    public bool SmsTwoFactorEnabled { get; init; }

    /// <summary>How long a delivered code stays valid.</summary>
    public int TwoFactorCodeLifetimeMinutes { get; init; } = 5;

    /// <summary>Gap enforced between two requests for a delivered code, in seconds.</summary>
    public int TwoFactorResendIntervalSeconds { get; init; } = 60;
}

/// <summary>How much of a protected position's surroundings an installation gives away.</summary>
public sealed record ProtectionSettings
{
    /// <summary>
    /// Show a caller without exact-location rights that a document is attached or linked to a
    /// position-protected feature. Off by default: naming the cave a report is about is a much
    /// smaller disclosure than where the cave is, but it is still one, and an installation should
    /// have to choose it rather than inherit it. Switching it on never reveals a position — the
    /// feature is authorised exactly as always, so following the association still yields the
    /// protected view — and it never opens the one pairing that would be a position: a document
    /// stamped with coordinates of its own stays unpaired with a protected feature either way.
    /// </summary>
    public bool RevealProtectedAssociations { get; init; }
}

/// <summary>How much an installation trusts a vector file to become registry objects on its own.</summary>
public sealed record ImportSettings
{
    /// <summary>
    /// Let an importer skip the review and have the rules create objects directly.
    ///
    /// <para>
    /// Off, so a fresh installation always reviews. Rules are a guess about somebody's naming
    /// habits; the first import against a set nobody has tuned is exactly where that guess is
    /// worst, and the difference between a bad review and a bad auto-import is four hundred
    /// objects in the registry. Switching it on never widens *who* may create — the importer
    /// still needs the right to create features — it only removes the step in between.
    /// </para>
    /// </summary>
    public bool AllowCreateWithoutReview { get; init; }

    /// <summary>How far duplicate detection looks around a candidate by default, in metres.</summary>
    public double DuplicateRadiusMeters { get; init; } = 50;

    /// <summary>
    /// How alike two names must be, 0 to 1, before duplicate detection calls a nearby object the
    /// same thing rather than merely close to it.
    /// </summary>
    public double DuplicateNameSimilarity { get; init; } = 0.8;

    /// <summary>
    /// How far a photograph looks for objects already in the registry, in metres, before the
    /// reviewer changes it. Wider than the vector-file default on purpose: a picture is taken
    /// from where the photographer stood, which is rarely where the thing they photographed is.
    /// </summary>
    public double PhotoProximityRadiusMeters { get; init; } = PhotoImportOptions.DefaultProximityRadiusMeters;

    /// <summary>
    /// How far apart two photographs can be, in metres, and still be proposed as one place.
    /// </summary>
    public double PhotoClusterRadiusMeters { get; init; } = PhotoImportOptions.DefaultClusterRadiusMeters;
}

/// <summary>
/// Section names under which the settings above are stored, one JSON document each. Keys are a
/// schema contract — renaming one abandons the operator's saved configuration.
/// </summary>
public static class AppSettingSections
{
    public const string Mail = "mail";

    public const string Sms = "sms";

    public const string Security = "security";

    public const string Protection = "protection";

    public const string Import = "import";

    public static IReadOnlyList<string> All { get; } = [Mail, Sms, Security, Protection, Import];
}
