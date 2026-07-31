// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Microsoft.AspNetCore.Identity;
using SilexGis.Domain.Auth;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Api.Auth;

/// <summary>
/// Bounds how often one account can be made to send a code, per method.
/// </summary>
/// <remarks>
/// The endpoint rate limiter is keyed on the caller's IP, which is the right shape for credential
/// guessing and the wrong shape for this: it does not stop one address or one phone number being
/// flooded from a handful of hosts, and a texted message costs the operator real money. The marker
/// lives in Identity's own user-token table, so no schema exists for it.
/// </remarks>
public static class SendThrottle
{
    private const string Provider = "[SilexGisTwoFactorSend]";

    /// <summary>Codes sent to complete a sign-in that is waiting on a second factor.</summary>
    public const string SignIn = "signin";

    /// <summary>Codes sent to prove a method works before it is switched on.</summary>
    public const string Enrolment = "enrol";

    public static async Task<bool> TooSoonAsync(
        UserManager<SilexGisUser> userManager,
        SilexGisUser user,
        TwoFactorMethod method,
        SecuritySettings policy,
        string purpose)
    {
        var stored = await userManager.GetAuthenticationTokenAsync(user, Provider, Bucket(method, purpose));
        if (stored is null || !long.TryParse(stored, CultureInfo.InvariantCulture, out var lastUnix))
        {
            return false;
        }

        var interval = TimeSpan.FromSeconds(Math.Clamp(policy.TwoFactorResendIntervalSeconds, 0, 600));
        return DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(lastUnix) < interval;
    }

    public static Task MarkSentAsync(
        UserManager<SilexGisUser> userManager, SilexGisUser user, TwoFactorMethod method, string purpose) =>
        userManager.SetAuthenticationTokenAsync(
            user,
            Provider,
            Bucket(method, purpose),
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Each purpose is throttled on its own. Sharing one bucket would let setting a method up make
    /// the user wait to sign in with it, which is the one moment they are most likely to try.
    /// </summary>
    private static string Bucket(TwoFactorMethod method, string purpose) => $"{purpose}:{method}";
}
