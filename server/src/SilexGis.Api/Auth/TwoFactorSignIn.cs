// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Identity;
using SilexGis.Domain.Auth;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Api.Auth;

/// <summary>
/// The second half of a two-factor sign-in, shared by every way of arriving at it: a password that
/// was accepted, or an external provider that vouched for the account. Either way Identity is
/// holding the half-finished sign-in in its own short-lived cookie, and what happens next must not
/// depend on how the first half went — the same methods are offered, the same codes accepted, and
/// a wrong code costs the same, so that adding a way in never adds a way around the second factor.
/// </summary>
internal static class TwoFactorSignIn
{
    /// <summary>Prefix marking a recovery code, which is accepted whatever the enabled methods are.</summary>
    public const string RecoveryPrefix = "recovery:";

    /// <summary>
    /// With no code this answers which methods the account can use, so the client can offer a
    /// choice and ask for the code to be sent; with a code it completes the pending sign-in.
    /// </summary>
    /// <remarks>
    /// Wrong codes are counted by the sign-in manager itself: each one is a failed access attempt
    /// under Identity's lockout threshold, and a locked-out account is refused before its code is
    /// looked at. Recovery codes are random and long, and are redeemed without that count, as the
    /// framework does.
    /// </remarks>
    public static async Task<IResult> AnswerAsync(
        SignInManager<SilexGisUser> signInManager,
        SilexGisUser user,
        string? code,
        TwoFactorMethod? method,
        SecuritySettings policy,
        bool mailConfigured,
        ISmsDelivery smsDelivery,
        CancellationToken ct)
    {
        var state = user.ToTwoFactorState();
        var smsConfigured = await smsDelivery.IsConfiguredAsync(ct);
        var available = TwoFactorPolicy.AvailableMethods(state, policy, mailConfigured, smsConfigured);

        if (string.IsNullOrWhiteSpace(code))
        {
            return AuthEndpoints.AuthProblem(
                StatusCodes.Status401Unauthorized,
                "auth.mfa_required",
                "A two-factor code is required.",
                new Dictionary<string, object?>
                {
                    ["methods"] = available.Select(m => m.ToString().ToLowerInvariant()).ToArray(),
                    ["preferredMethod"] = TwoFactorPolicy
                        .PreferredMethod(state, policy, mailConfigured, smsConfigured)?.ToString().ToLowerInvariant(),
                    // Every method can be taken away by someone other than the person signing in:
                    // they turn one off, an administrator disallows it, or a channel stops working.
                    // Recovery codes are what stops that from being a lockout, so the client is
                    // always told they will be accepted.
                    ["recoveryAccepted"] = true,
                });
        }

        var trimmed = code.Trim();
        var signIn = trimmed.StartsWith(RecoveryPrefix, StringComparison.OrdinalIgnoreCase)
            ? await signInManager.TwoFactorRecoveryCodeSignInAsync(
                trimmed[RecoveryPrefix.Length..].Replace(" ", string.Empty))
            : await SignInWithMethodAsync(signInManager, method, trimmed, available);

        if (!signIn.Succeeded)
        {
            return signIn.IsLockedOut
                ? AuthEndpoints.AuthProblem(
                    StatusCodes.Status401Unauthorized, "auth.locked_out", "Account temporarily locked after repeated failures.")
                : AuthEndpoints.AuthProblem(
                    StatusCodes.Status401Unauthorized, "auth.mfa_invalid", "The two-factor code is not valid.");
        }

        return TypedResults.Ok(new SessionDto(user.Id, user.Email!, user.DisplayName));
    }

    private static async Task<SignInResult> SignInWithMethodAsync(
        SignInManager<SilexGisUser> signInManager,
        TwoFactorMethod? requested,
        string code,
        IReadOnlyList<TwoFactorMethod> available)
    {
        // No stated method means the authenticator, which is the only one that needs no prior
        // request and the only one older clients ever sent.
        var method = requested ?? TwoFactorMethod.Authenticator;
        if (!available.Contains(method))
        {
            return SignInResult.Failed;
        }

        var stripped = code.Replace(" ", string.Empty);
        return method == TwoFactorMethod.Authenticator
            ? await signInManager.TwoFactorAuthenticatorSignInAsync(stripped, isPersistent: true, rememberClient: false)
            : await signInManager.TwoFactorSignInAsync(
                TwoFactorProviders.For(method), stripped, isPersistent: true, rememberClient: false);
    }
}
