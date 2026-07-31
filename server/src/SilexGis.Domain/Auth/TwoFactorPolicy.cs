// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Settings;

namespace SilexGis.Domain.Auth;

/// <summary>
/// What one account has switched on, and what the account is in a position to use. Enabling a
/// method and being able to receive it are separate: an address or number can lose its confirmed
/// standing after the method was enabled.
/// </summary>
public sealed record TwoFactorState(
    bool AuthenticatorEnabled,
    bool EmailEnabled,
    bool SmsEnabled,
    bool EmailConfirmed,
    bool PhoneConfirmed,
    TwoFactorMethod? Preferred);

/// <summary>
/// Which second factors an account may actually use right now — the intersection of what the user
/// enabled, what the installation permits, and which delivery channels are configured.
/// </summary>
/// <remarks>
/// Three things can each remove a method: the user turning it off, an administrator disallowing it
/// for the whole installation, or the channel it needs going away. Because all three are outside
/// the signing-in user's control at the moment they matter, recovery codes stay valid whenever
/// two-factor is on at all — see <see cref="RequiresRecoveryCode"/>. Without that an operator who
/// switched off SMS would have locked out everyone who had chosen it.
/// </remarks>
public static class TwoFactorPolicy
{
    /// <summary>
    /// Methods usable for this sign-in, strongest first. Empty is a legitimate answer and means
    /// the account must fall back to a recovery code.
    /// </summary>
    public static IReadOnlyList<TwoFactorMethod> AvailableMethods(
        TwoFactorState state, SecuritySettings policy, bool mailConfigured, bool smsConfigured)
    {
        var methods = new List<TwoFactorMethod>(3);

        if (state.AuthenticatorEnabled && policy.AuthenticatorTwoFactorEnabled)
        {
            methods.Add(TwoFactorMethod.Authenticator);
        }

        // A delivered code is only offered when there is somewhere confirmed to deliver it and
        // something configured to do the delivering.
        if (state.EmailEnabled && policy.EmailTwoFactorEnabled && state.EmailConfirmed && mailConfigured)
        {
            methods.Add(TwoFactorMethod.Email);
        }

        if (state.SmsEnabled && policy.SmsTwoFactorEnabled && state.PhoneConfirmed && smsConfigured)
        {
            methods.Add(TwoFactorMethod.Sms);
        }

        return methods;
    }

    /// <summary>
    /// Whether the account has asked for a second factor at all, regardless of whether any of its
    /// methods can be used today. This is what Identity's own two-factor flag must mirror.
    /// </summary>
    public static bool AnyEnabled(TwoFactorState state) =>
        state.AuthenticatorEnabled || state.EmailEnabled || state.SmsEnabled;

    /// <summary>
    /// True when two-factor is on but nothing can carry it, so the only way in is a recovery code.
    /// The sign-in response says this plainly rather than presenting an empty method list.
    /// </summary>
    public static bool RequiresRecoveryCode(
        TwoFactorState state, SecuritySettings policy, bool mailConfigured, bool smsConfigured) =>
        AnyEnabled(state) && AvailableMethods(state, policy, mailConfigured, smsConfigured).Count == 0;

    /// <summary>
    /// The method to offer first: the user's choice when it is usable, otherwise the strongest one
    /// that is. Null when only a recovery code is left.
    /// </summary>
    public static TwoFactorMethod? PreferredMethod(
        TwoFactorState state, SecuritySettings policy, bool mailConfigured, bool smsConfigured)
    {
        var available = AvailableMethods(state, policy, mailConfigured, smsConfigured);
        if (available.Count == 0)
        {
            return null;
        }

        return state.Preferred is { } preferred && available.Contains(preferred) ? preferred : available[0];
    }

    /// <summary>
    /// Whether an installation lets an account turn this method on at all. Checked when enrolling
    /// so a user cannot enable a method the operator has disallowed.
    /// </summary>
    public static bool IsMethodAllowed(TwoFactorMethod method, SecuritySettings policy) => method switch
    {
        TwoFactorMethod.Authenticator => policy.AuthenticatorTwoFactorEnabled,
        TwoFactorMethod.Email => policy.EmailTwoFactorEnabled,
        TwoFactorMethod.Sms => policy.SmsTwoFactorEnabled,
        _ => false,
    };
}
