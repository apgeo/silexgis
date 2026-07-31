// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using SilexGis.Domain.Settings;

namespace SilexGis.Infrastructure.Identity;

/// <summary>
/// Issues and checks the numeric codes that are mailed or texted to someone.
/// </summary>
/// <remarks>
/// <para>
/// Identity ships providers for both channels, and they are not used here. Theirs derive a code
/// from the security stamp on a rolling time step, which has two consequences this flow cannot
/// accept: the same code stays valid for the whole window, so one observed over someone's shoulder
/// can be replayed, and the window is a framework constant — an administrator cannot be told how
/// long a code lasts if the answer is fixed in a library.
/// </para>
/// <para>
/// This provider stores a single-use code with an explicit expiry instead. It is consumed on the
/// first correct answer, so a code that has been used is worthless, and the lifetime is the one an
/// administrator set. The comparison is constant-time, and a wrong answer still goes through
/// Identity's lockout counter, which is what actually bounds guessing at a six-digit number.
/// </para>
/// </remarks>
public abstract class DeliveredCodeTokenProvider(IAppSettingsService settings, string channel)
    : IUserTwoFactorTokenProvider<SilexGisUser>
{
    /// <summary>Namespace for the stored code in the user-token table.</summary>
    private const string LoginProviderPrefix = "[SilexGisDeliveredCode]";

    /// <summary>
    /// Each channel stores its codes separately. Identity asks both providers for the same
    /// purpose ("TwoFactor"), so a shared key would mean an emailed code and a texted code
    /// overwriting one another — and each validating against the other's channel.
    /// </summary>
    private string LoginProvider { get; } = $"{LoginProviderPrefix}:{channel}";

    /// <summary>Six digits: what people expect to retype, and short enough to text.</summary>
    private const int Digits = 6;

    public async Task<string> GenerateAsync(string purpose, UserManager<SilexGisUser> manager, SilexGisUser user)
    {
        var policy = await settings.GetSecurityAsync();
        var lifetime = TimeSpan.FromMinutes(Math.Clamp(policy.TwoFactorCodeLifetimeMinutes, 1, 60));

        var code = RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, Digits))
            .ToString(CultureInfo.InvariantCulture)
            .PadLeft(Digits, '0');

        var expiresAt = DateTimeOffset.UtcNow.Add(lifetime);
        await manager.SetAuthenticationTokenAsync(
            user, LoginProvider, TokenName(purpose), $"{code}|{expiresAt.ToUnixTimeSeconds()}");

        return code;
    }

    public async Task<bool> ValidateAsync(
        string purpose, string token, UserManager<SilexGisUser> manager, SilexGisUser user)
    {
        var stored = await manager.GetAuthenticationTokenAsync(user, LoginProvider, TokenName(purpose));
        if (stored is null)
        {
            return false;
        }

        var separator = stored.IndexOf('|', StringComparison.Ordinal);
        if (separator <= 0
            || !long.TryParse(stored[(separator + 1)..], CultureInfo.InvariantCulture, out var expiresAtUnix))
        {
            await ClearAsync(manager, user, purpose);
            return false;
        }

        var expected = stored[..separator];
        var supplied = (token ?? string.Empty).Trim();

        // Expiry is checked before the comparison so an expired code is removed even when the
        // person retyping it finally gets it right.
        if (DateTimeOffset.FromUnixTimeSeconds(expiresAtUnix) <= DateTimeOffset.UtcNow)
        {
            await ClearAsync(manager, user, purpose);
            return false;
        }

        if (!FixedTimeEquals(expected, supplied))
        {
            return false;
        }

        await ClearAsync(manager, user, purpose);
        return true;
    }

    /// <summary>
    /// Whether this account is in a position to receive a code at all — a confirmed address for
    /// mail, a confirmed number for text. Identity asks before listing the method as available.
    /// </summary>
    public abstract Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<SilexGisUser> manager, SilexGisUser user);

    private static string TokenName(string purpose) => $"code:{purpose}";

    private Task ClearAsync(UserManager<SilexGisUser> manager, SilexGisUser user, string purpose) =>
        manager.RemoveAuthenticationTokenAsync(user, LoginProvider, TokenName(purpose));

    /// <summary>
    /// Compares without leaking how much of the code was right. Lengths are compared first because
    /// <see cref="CryptographicOperations.FixedTimeEquals"/> requires equal spans — a length
    /// mismatch already tells an attacker nothing they could not see by counting their own digits.
    /// </summary>
    private static bool FixedTimeEquals(string expected, string supplied)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        return expectedBytes.Length == suppliedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}

/// <summary>Codes delivered by mail. Registered under Identity's <c>Email</c> provider name.</summary>
public sealed class EmailCodeTokenProvider(IAppSettingsService settings)
    : DeliveredCodeTokenProvider(settings, "email")
{
    public override async Task<bool> CanGenerateTwoFactorTokenAsync(
        UserManager<SilexGisUser> manager, SilexGisUser user) =>
        !string.IsNullOrWhiteSpace(await manager.GetEmailAsync(user))
        && await manager.IsEmailConfirmedAsync(user);
}

/// <summary>Codes delivered by text. Registered under Identity's <c>Phone</c> provider name.</summary>
public sealed class PhoneCodeTokenProvider(IAppSettingsService settings)
    : DeliveredCodeTokenProvider(settings, "phone")
{
    public override async Task<bool> CanGenerateTwoFactorTokenAsync(
        UserManager<SilexGisUser> manager, SilexGisUser user) =>
        !string.IsNullOrWhiteSpace(await manager.GetPhoneNumberAsync(user))
        && await manager.IsPhoneNumberConfirmedAsync(user);
}
