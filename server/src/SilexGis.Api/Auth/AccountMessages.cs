// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Microsoft.Extensions.Configuration;
using SilexGis.Domain.Auth;
using SilexGis.Domain.Messaging;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Api.Auth;

/// <summary>
/// The account-related messages the application sends, each in one place.
/// </summary>
/// <remarks>
/// Every flow that sends something goes through here so the link construction, the recipient's
/// language and the placeholder names are decided once. Endpoints name the message and supply the
/// token or code; they never write prose, which is what made the wording impossible to configure
/// before.
/// </remarks>
public static class AccountMessages
{
    public static Task<MessageResult> SendEmailConfirmationAsync(
        IMessageDispatcher dispatcher,
        IConfiguration configuration,
        SilexGisUser user,
        string token,
        CancellationToken ct = default) =>
        dispatcher.SendAsync(
            MessageTemplateCatalog.EmailConfirmAddress,
            user.Email!,
            user.Locale,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["displayName"] = Greeting(user),
                ["url"] = ConfirmUrl(configuration, user.Id, token),
                ["expiresHours"] = LinkLifetimeHours,
            },
            ct);

    /// <summary>Goes to the proposed address, not the current one — that is the whole point of it.</summary>
    public static Task<MessageResult> SendEmailChangeAsync(
        IMessageDispatcher dispatcher,
        IConfiguration configuration,
        SilexGisUser user,
        string newEmail,
        string token,
        CancellationToken ct = default) =>
        dispatcher.SendAsync(
            MessageTemplateCatalog.EmailChangeAddress,
            newEmail,
            user.Locale,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["displayName"] = Greeting(user),
                ["url"] = SettingsConfirmUrl(configuration, token),
                ["expiresHours"] = LinkLifetimeHours,
            },
            ct);

    public static Task<MessageResult> SendPasswordResetAsync(
        IMessageDispatcher dispatcher,
        IConfiguration configuration,
        SilexGisUser user,
        string token,
        CancellationToken ct = default) =>
        dispatcher.SendAsync(
            MessageTemplateCatalog.EmailPasswordReset,
            user.Email!,
            user.Locale,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["displayName"] = Greeting(user),
                ["url"] = ResetUrl(configuration, user.Email!, token),
                ["expiresHours"] = LinkLifetimeHours,
            },
            ct);

    /// <summary>Sends a sign-in code on whichever channel the chosen method uses.</summary>
    public static Task<MessageResult> SendTwoFactorCodeAsync(
        IMessageDispatcher dispatcher,
        SilexGisUser user,
        TwoFactorMethod method,
        string code,
        int expiresMinutes,
        CancellationToken ct = default)
    {
        var email = method == TwoFactorMethod.Email;
        return dispatcher.SendAsync(
            email ? MessageTemplateCatalog.EmailTwoFactorCode : MessageTemplateCatalog.SmsTwoFactorCode,
            email ? user.Email! : user.PhoneNumber!,
            user.Locale,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["displayName"] = Greeting(user),
                ["code"] = code,
                ["expiresMinutes"] = expiresMinutes.ToString(CultureInfo.InvariantCulture),
            },
            ct);
    }

    /// <summary>Texts the code that proves someone holds the number they just entered.</summary>
    public static Task<MessageResult> SendPhoneVerificationAsync(
        IMessageDispatcher dispatcher,
        SilexGisUser user,
        string phoneNumber,
        string code,
        int expiresMinutes,
        CancellationToken ct = default) =>
        dispatcher.SendAsync(
            MessageTemplateCatalog.SmsVerifyPhone,
            phoneNumber,
            user.Locale,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["code"] = code,
                ["expiresMinutes"] = expiresMinutes.ToString(CultureInfo.InvariantCulture),
            },
            ct);

    /// <summary>
    /// How to address someone in their own mail. Their address is an acceptable last resort here
    /// in a way it never is in the directory: this message is going to that address already.
    /// </summary>
    private static string Greeting(SilexGisUser user) =>
        user.DisplayName is { Length: > 0 } display ? display
        : user.FirstName is { Length: > 0 } first ? first
        : user.Email ?? user.UserName ?? string.Empty;

    private static string LinkLifetimeHours =>
        AuthenticationSetup.EmailLinkLifetimeHours.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Confirmation lands on a page that needs no session: the recipient may be confirming from a
    /// phone that has never signed in, and requiring a login first is how confirmation links get
    /// abandoned.
    /// </summary>
    private static string ConfirmUrl(IConfiguration configuration, Guid userId, string token) =>
        $"{PublicUrl(configuration)}/confirm-email?user={userId}&token={Uri.EscapeDataString(token)}";

    /// <summary>A change is confirmed from the settings page, where the person already is.</summary>
    private static string SettingsConfirmUrl(IConfiguration configuration, string token) =>
        $"{PublicUrl(configuration)}/settings/emails?confirm={Uri.EscapeDataString(token)}";

    private static string ResetUrl(IConfiguration configuration, string email, string token) =>
        $"{PublicUrl(configuration)}/reset-password?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";

    private static string PublicUrl(IConfiguration configuration) =>
        configuration.GetValue("PublicUrl", "http://localhost:8080")!.TrimEnd('/');
}
