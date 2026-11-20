// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Api.Features.Me;

/// <summary>
/// Keeps the browser signed in across a change that rotates the account's security stamp (a new
/// user name, a confirmed new address).
/// </summary>
/// <remarks>
/// The sign-in cookie is what the authorization endpoint reads to issue every later token, and it
/// carries the security stamp it was issued under. Stamps are validated on every request, so once
/// the stamp has moved, merely reading the cookie rejects it and deletes it — which is exactly what
/// <c>SignInManager.RefreshSignInAsync</c> does first when called after the change: it finds no
/// sign-in to refresh and the response carries only the cookie's deletion, signing the member out
/// at their next page load. So the cookie is read while the stamp still matches, and issued again
/// after the change with the same lifetime and the same record of how the member signed in.
/// A request that came with no cookie (a token-only client) gets none.
/// </remarks>
internal sealed class BrowserSignIn
{
    private readonly AuthenticateResult before;

    private BrowserSignIn(AuthenticateResult before) => this.before = before;

    /// <summary>Reads the browser's sign-in. Call before anything that rotates the stamp.</summary>
    public static async Task<BrowserSignIn> ReadAsync(HttpContext http) =>
        new(await http.AuthenticateAsync(IdentityConstants.ApplicationScheme));

    /// <summary>Issues the cookie again under the account's new stamp, if one was read.</summary>
    public async Task RenewAsync(SignInManager<SilexGisUser> signInManager, SilexGisUser user)
    {
        if (!before.Succeeded || before.Principal is null
            || signInManager.UserManager.GetUserId(before.Principal) != user.Id.ToString())
        {
            return;
        }

        var kept = before.Principal.Claims
            .Where(c => c.Type is ClaimTypes.AuthenticationMethod or "amr")
            .ToList();
        await signInManager.SignInWithClaimsAsync(user, before.Properties, kept);
    }
}
