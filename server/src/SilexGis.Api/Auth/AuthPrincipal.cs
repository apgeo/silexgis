// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Immutable;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using SilexGis.Domain.Profiles;
using SilexGis.Infrastructure.Identity;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SilexGis.Api.Auth;

/// <summary>Builds the OpenIddict token principal for a user (claims, scopes, destinations).</summary>
internal static class AuthPrincipal
{
    public static async Task<ClaimsPrincipal> CreateAsync(
        UserManager<SilexGisUser> userManager, SilexGisUser user, ImmutableArray<string> scopes)
    {
        var identity = await IdentityAsync(
            userManager, user, TokenValidationParameters.DefaultAuthenticationType);

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(scopes);
        principal.SetDestinations(GetDestinations);
        return principal;
    }

    /// <summary>
    /// The account's claims — subject, address, the protected label and roles — with nothing
    /// token-specific attached. Shared with the personal access key handler, so a request made with
    /// a key is seen by every reader of the principal exactly as one made after signing in.
    /// </summary>
    public static async Task<ClaimsIdentity> IdentityAsync(
        UserManager<SilexGisUser> userManager, SilexGisUser user, string authenticationType)
    {
        var identity = new ClaimsIdentity(authenticationType, Claims.Name, Claims.Role);

        // Both name claims are the same protected label every other surface shows. A plain
        // "display name, or else user name" fallback would publish the email address of every
        // account that never chose a display name, because registration and external federation
        // both create accounts with the address as the user name. The address travels in its own
        // claim, which the email scope gates; these two are gated only by the profile scope.
        var label = ProfileProtection.Label(user);

        identity.SetClaim(Claims.Subject, user.Id.ToString())
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(Claims.Name, label)
            .SetClaim(Claims.PreferredUsername, label);

        identity.SetClaims(Claims.Role, [.. await userManager.GetRolesAsync(user)]);
        return identity;
    }

    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        switch (claim.Type)
        {
            case Claims.Name or Claims.PreferredUsername:
                yield return Destinations.AccessToken;
                if (claim.Subject?.HasScope(Scopes.Profile) == true)
                {
                    yield return Destinations.IdentityToken;
                }

                yield break;

            case Claims.Email:
                yield return Destinations.AccessToken;
                if (claim.Subject?.HasScope(Scopes.Email) == true)
                {
                    yield return Destinations.IdentityToken;
                }

                yield break;

            case Claims.Role:
                yield return Destinations.AccessToken;
                if (claim.Subject?.HasScope(Scopes.Roles) == true)
                {
                    yield return Destinations.IdentityToken;
                }

                yield break;

            // Never leak the security stamp into tokens.
            case "AspNet.Identity.SecurityStamp":
                yield break;

            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }
}
