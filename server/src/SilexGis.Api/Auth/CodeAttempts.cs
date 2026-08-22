// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Identity;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Api.Auth;

/// <summary>
/// Makes a wrong six-digit code cost something, on the paths a signed-in person uses to prove a
/// channel works.
/// </summary>
/// <remarks>
/// Signing in drives the failed-attempt counter for free, because it goes through the sign-in
/// manager. The self-service paths — confirming a new phone number, switching a second factor on —
/// verify a code through the user manager directly, which does not touch the counter, so a code
/// with six digits and a lifetime measured in minutes was guessable for its whole lifetime against
/// nothing but a per-IP request budget shared with every other caller behind the same address.
/// Driving the same counter those paths already trust is the bound; a second, parallel lockout
/// mechanism would only give the two a way to disagree.
/// </remarks>
public static class CodeAttempts
{
    /// <summary>Whether the account is currently locked out and must be refused before verifying.</summary>
    public static Task<bool> LockedOutAsync(UserManager<SilexGisUser> userManager, SilexGisUser user) =>
        userManager.IsLockedOutAsync(user);

    /// <summary>Records a wrong code. Locks the account once the configured threshold is reached.</summary>
    public static Task FailedAsync(UserManager<SilexGisUser> userManager, SilexGisUser user) =>
        userManager.AccessFailedAsync(user);

    /// <summary>
    /// Records a correct code. Clearing the count matters as much as raising it: without this a
    /// few mistyped codes spread over a week would eventually lock someone out for no reason.
    /// </summary>
    public static Task SucceededAsync(UserManager<SilexGisUser> userManager, SilexGisUser user) =>
        userManager.ResetAccessFailedCountAsync(user);
}
