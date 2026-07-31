// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Messaging;

/// <summary>The text of one template in one language. <see cref="Subject"/> is null for SMS.</summary>
public sealed record MessageTemplateText(string? Subject, string Body);

/// <summary>
/// Everything the application knows about one kind of message: which channel carries it, which
/// placeholders it may use, and the wording shipped with the product.
/// </summary>
public sealed record MessageTemplateDefinition(
    string Key,
    MessageChannel Channel,
    string Description,
    IReadOnlyList<string> Placeholders,
    IReadOnlyDictionary<string, MessageTemplateText> Defaults);

/// <summary>
/// The message vocabulary, in one place. An operator may rewrite any of these from the admin
/// settings, but may not invent new ones: a template exists only because some code path sends it.
/// </summary>
/// <remarks>
/// <para>
/// Placeholders are written <c>{name}</c> and are declared per template. Editing rejects a
/// placeholder that is not on the template's list, because an unknown one would silently render
/// as nothing — the sort of mistake only discovered when a user cannot find the code they were
/// promised. The renderer leaves declared-but-unsupplied placeholders empty for the same reason
/// it does not throw: a broken template must not stop a password reset.
/// </para>
/// <para>
/// Bodies are plain text. HTML mail would need a sanitiser, a multipart builder and a second
/// editing surface for what is, in every case here, four lines telling someone a code or a link.
/// </para>
/// </remarks>
public static class MessageTemplateCatalog
{
    /// <summary>Confirms the address an account already holds (registration, or a re-send).</summary>
    public const string EmailConfirmAddress = "email.confirm-address";

    /// <summary>Confirms a new address before it replaces the live one.</summary>
    public const string EmailChangeAddress = "email.change-address";

    public const string EmailPasswordReset = "email.password-reset";

    /// <summary>Second-factor code delivered by mail.</summary>
    public const string EmailTwoFactorCode = "email.two-factor-code";

    /// <summary>Second-factor code delivered by text message.</summary>
    public const string SmsTwoFactorCode = "sms.two-factor-code";

    /// <summary>Confirms a phone number before it may carry second-factor codes.</summary>
    public const string SmsVerifyPhone = "sms.verify-phone";

    /// <summary>Languages every template ships in. A locale outside this set falls back to English.</summary>
    public static IReadOnlyList<string> Locales { get; } = ["en", "ro"];

    public const string FallbackLocale = "en";

    /// <summary>Placeholder every template may use: the installation's name.</summary>
    private const string AppName = "appName";

    /// <summary>Placeholder every template may use: how the recipient is addressed.</summary>
    private const string DisplayName = "displayName";

    public static IReadOnlyList<MessageTemplateDefinition> All { get; } =
    [
        new(
            EmailConfirmAddress,
            MessageChannel.Email,
            "Sent when an account's own address needs confirming — after registration, or on request.",
            [AppName, DisplayName, "url", "expiresHours"],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "Confirm your {appName} address",
                    """
                    Hello {displayName},

                    Confirm this address to finish setting up your {appName} account:

                    {url}

                    The link is valid for {expiresHours} hours. If you did not create this account,
                    ignore this message — nothing further will happen.
                    """),
                ["ro"] = new(
                    "Confirmați adresa dumneavoastră {appName}",
                    """
                    Bună ziua {displayName},

                    Confirmați această adresă pentru a finaliza configurarea contului {appName}:

                    {url}

                    Linkul este valabil {expiresHours} ore. Dacă nu dumneavoastră ați creat acest cont,
                    ignorați acest mesaj — nu se va întâmpla nimic.
                    """),
            }),

        new(
            EmailChangeAddress,
            MessageChannel.Email,
            "Sent to the proposed new address when someone changes the address on their account.",
            [AppName, DisplayName, "url", "expiresHours"],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "Confirm your new {appName} address",
                    """
                    Hello {displayName},

                    Confirm this address to make it the one on your {appName} account:

                    {url}

                    The link is valid for {expiresHours} hours. Until then your current address is
                    unchanged. If you did not ask for this, ignore this message — nothing has changed.
                    """),
                ["ro"] = new(
                    "Confirmați noua adresă {appName}",
                    """
                    Bună ziua {displayName},

                    Confirmați această adresă pentru a deveni adresa contului dumneavoastră {appName}:

                    {url}

                    Linkul este valabil {expiresHours} ore. Până atunci adresa actuală rămâne neschimbată.
                    Dacă nu dumneavoastră ați cerut acest lucru, ignorați mesajul — nimic nu s-a schimbat.
                    """),
            }),

        new(
            EmailPasswordReset,
            MessageChannel.Email,
            "Sent when someone asks to reset a forgotten password.",
            [AppName, DisplayName, "url", "expiresHours"],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "Reset your {appName} password",
                    """
                    Hello {displayName},

                    Open this link to choose a new password:

                    {url}

                    The link is valid for {expiresHours} hours and can be used once. If you did not
                    ask for a reset, ignore this message — your password stays as it is.
                    """),
                ["ro"] = new(
                    "Resetați parola {appName}",
                    """
                    Bună ziua {displayName},

                    Deschideți acest link pentru a alege o parolă nouă:

                    {url}

                    Linkul este valabil {expiresHours} ore și poate fi folosit o singură dată. Dacă nu
                    dumneavoastră ați cerut resetarea, ignorați mesajul — parola rămâne neschimbată.
                    """),
            }),

        new(
            EmailTwoFactorCode,
            MessageChannel.Email,
            "Carries a sign-in code to someone whose second factor is their email address.",
            [AppName, DisplayName, "code", "expiresMinutes"],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "{code} is your {appName} sign-in code",
                    """
                    Hello {displayName},

                    Your sign-in code is {code}. It expires in {expiresMinutes} minutes.

                    If you are not signing in right now, someone else knows your password — change it
                    as soon as you can.
                    """),
                ["ro"] = new(
                    "{code} este codul dumneavoastră de autentificare {appName}",
                    """
                    Bună ziua {displayName},

                    Codul de autentificare este {code}. Expiră în {expiresMinutes} minute.

                    Dacă nu vă autentificați chiar acum, altcineva vă cunoaște parola — schimbați-o
                    cât mai curând.
                    """),
            }),

        new(
            SmsTwoFactorCode,
            MessageChannel.Sms,
            "Carries a sign-in code to someone whose second factor is their phone.",
            [AppName, "code", "expiresMinutes"],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(null, "{code} is your {appName} sign-in code. It expires in {expiresMinutes} minutes."),
                ["ro"] = new(null, "{code} este codul dumneavoastră de autentificare {appName}. Expiră în {expiresMinutes} minute."),
            }),

        new(
            SmsVerifyPhone,
            MessageChannel.Sms,
            "Confirms a phone number before it is allowed to carry sign-in codes.",
            [AppName, "code", "expiresMinutes"],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(null, "{code} is your {appName} phone verification code. It expires in {expiresMinutes} minutes."),
                ["ro"] = new(null, "{code} este codul dumneavoastră de verificare a telefonului {appName}. Expiră în {expiresMinutes} minute."),
            }),
    ];

    public static MessageTemplateDefinition? Find(string key) =>
        All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));

    /// <summary>The shipped wording, falling back to English for a locale a template lacks.</summary>
    public static MessageTemplateText Default(MessageTemplateDefinition definition, string locale) =>
        definition.Defaults.TryGetValue(Normalise(locale), out var text)
            ? text
            : definition.Defaults[FallbackLocale];

    /// <summary>
    /// Reduces "ro-RO" to "ro" and anything unrecognised to English, so a user whose locale
    /// drifted (or arrived from a provider claim) still receives readable mail.
    /// </summary>
    public static string Normalise(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return FallbackLocale;
        }

        var primary = locale.Split('-')[0].Trim().ToLowerInvariant();
        return Locales.Contains(primary) ? primary : FallbackLocale;
    }
}
