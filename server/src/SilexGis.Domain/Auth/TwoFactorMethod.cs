// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Auth;

/// <summary>
/// A way of proving the second factor. Stored as smallint on the user row; values are part of
/// the schema contract — do not renumber.
/// </summary>
public enum TwoFactorMethod : short
{
    /// <summary>A TOTP authenticator app. The strongest of the three and the default offer.</summary>
    Authenticator = 0,

    /// <summary>A code mailed to the account's confirmed address.</summary>
    Email = 1,

    /// <summary>A code texted to the account's confirmed phone number.</summary>
    Sms = 2,
}

/// <summary>
/// Names Identity knows these methods by. Email and phone codes are issued by Identity's own
/// token providers, so the provider strings have to match what <c>AddDefaultTokenProviders</c>
/// registered — spelling them once here keeps the sign-in path and the enrolment path in step.
/// </summary>
public static class TwoFactorProviders
{
    /// <summary>Matches <c>TokenOptions.DefaultEmailProvider</c>.</summary>
    public const string Email = "Email";

    /// <summary>Matches <c>TokenOptions.DefaultPhoneProvider</c>.</summary>
    public const string Phone = "Phone";

    /// <summary>Matches <c>TokenOptions.DefaultAuthenticatorProvider</c>.</summary>
    public const string Authenticator = "Authenticator";

    public static string For(TwoFactorMethod method) => method switch
    {
        TwoFactorMethod.Authenticator => Authenticator,
        TwoFactorMethod.Email => Email,
        TwoFactorMethod.Sms => Phone,
        _ => throw new ArgumentOutOfRangeException(nameof(method)),
    };

    /// <summary>
    /// Whether the code for a method is delivered by the application rather than produced by
    /// something the user already holds. Only these need a "send it to me" step before sign-in.
    /// </summary>
    public static bool IsDelivered(TwoFactorMethod method) =>
        method is TwoFactorMethod.Email or TwoFactorMethod.Sms;
}
