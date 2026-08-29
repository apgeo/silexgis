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

    /// <summary>
    /// Codes texted to verify a number the account holder is trying to move to. Kept here rather
    /// than on the user row because a marker stored on the row is cleared by removing the number,
    /// and removing a number is a free, unauthenticated-by-cooldown act: change, delete, change
    /// would otherwise text a caller-chosen international number once per two requests.
    /// </summary>
    public const string PhoneChange = "phone";

    /// <summary>
    /// Test texts an operator sends to prove a gateway works. Not a code and not a second factor,
    /// but the same money: one call, one text, to a number typed into a box.
    /// </summary>
    public const string AdminTest = "admintest";

    /// <summary>
    /// How long an operator waits between test texts. Not read from the sign-in policy, which is
    /// about how quickly somebody may ask for their own code again: proving a gateway works is
    /// something done once after configuring it, so the bound is measured in texts per hour.
    /// </summary>
    public static readonly TimeSpan AdminTestInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Announcements one person sends to a whole caving group. Not a code and not tied to any one
    /// method: what is bounded here is how often an account may make the installation write to a
    /// roster, whichever way each of those people ends up hearing about it.
    /// </summary>
    public const string GroupAnnouncement = "announce";

    /// <summary>
    /// How long an account waits between announcements to caving groups. Long enough that a
    /// double-click, a retried request or a script cannot turn one notice into a stream, short
    /// enough that somebody correcting a time they got wrong is not stuck for the evening. It is
    /// deliberately per account and not per group: the cost being bounded is the sending, and
    /// somebody who may write to three clubs can reach three rosters just as fast.
    /// </summary>
    public static readonly TimeSpan GroupAnnouncementInterval = TimeSpan.FromMinutes(5);

    public static Task<bool> TooSoonAsync(
        UserManager<SilexGisUser> userManager,
        SilexGisUser user,
        TwoFactorMethod method,
        SecuritySettings policy,
        string purpose) =>
        TooSoonAsync(
            userManager,
            user,
            method,
            TimeSpan.FromSeconds(Math.Clamp(policy.TwoFactorResendIntervalSeconds, 0, 600)),
            purpose);

    public static async Task<bool> TooSoonAsync(
        UserManager<SilexGisUser> userManager,
        SilexGisUser user,
        TwoFactorMethod method,
        TimeSpan interval,
        string purpose)
    {
        var stored = await userManager.GetAuthenticationTokenAsync(user, Provider, Bucket(method, purpose));
        if (stored is null || !long.TryParse(stored, CultureInfo.InvariantCulture, out var lastUnix))
        {
            return false;
        }

        return DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(lastUnix) < interval;
    }

    /// <summary>
    /// The same marker for something that is not sent by any one method, so the bucket is the
    /// purpose alone. Kept apart from the method-keyed buckets by the separator those carry.
    /// </summary>
    public static async Task<bool> TooSoonAsync(
        UserManager<SilexGisUser> userManager, SilexGisUser user, TimeSpan interval, string purpose)
    {
        var stored = await userManager.GetAuthenticationTokenAsync(user, Provider, purpose);
        if (stored is null || !long.TryParse(stored, CultureInfo.InvariantCulture, out var lastUnix))
        {
            return false;
        }

        return DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(lastUnix) < interval;
    }

    public static Task MarkSentAsync(
        UserManager<SilexGisUser> userManager, SilexGisUser user, string purpose) =>
        userManager.SetAuthenticationTokenAsync(
            user,
            Provider,
            purpose,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));

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
