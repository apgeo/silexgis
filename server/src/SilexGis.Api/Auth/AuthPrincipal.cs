// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Immutable;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using SilexGis.Infrastructure.Identity;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SilexGis.Api.Auth;

/// <summary>Builds the OpenIddict token principal for a user (claims, scopes, destinations).</summary>
internal static class AuthPrincipal
{
    public static async Task<ClaimsPrincipal> CreateAsync(
        UserManager<SilexGisUser> userManager, SilexGisUser user, ImmutableArray<string> scopes)
    {
        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);

        identity.SetClaim(Claims.Subject, user.Id.ToString())
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(Claims.Name, user.UserName)
            .SetClaim(Claims.PreferredUsername, user.DisplayName ?? user.UserName);

        identity.SetClaims(Claims.Role, [.. await userManager.GetRolesAsync(user)]);

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(scopes);
        principal.SetDestinations(GetDestinations);
        return principal;
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
