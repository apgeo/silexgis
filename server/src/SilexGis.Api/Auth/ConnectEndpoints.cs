// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using SilexGis.Infrastructure.Identity;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SilexGis.Api.Auth;

/// <summary>
/// OIDC protocol endpoints. Authorization Code + PKCE with the SPA as a
/// first-party client: the authorize endpoint relies on the Identity cookie session and
/// auto-consents; unauthenticated callers are redirected to the SPA login route.
/// </summary>
public static class ConnectEndpoints
{
    public static IEndpointRouteBuilder MapConnectEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/connect/authorize", [HttpMethods.Get, HttpMethods.Post], AuthorizeAsync);
        app.MapPost("/connect/token", ExchangeAsync).RequireRateLimiting("auth");
        app.MapGet("/connect/userinfo", UserInfoAsync).RequireAuthorization();
        app.MapMethods("/connect/logout", [HttpMethods.Get, HttpMethods.Post], LogoutAsync);
        return app;
    }

    private static async Task<IResult> AuthorizeAsync(
        HttpContext context, UserManager<SilexGisUser> userManager)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        var session = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (session.Succeeded is not true || await userManager.GetUserAsync(session.Principal) is not { } user)
        {
            // No session → SPA login route, which returns here after sign-in.
            var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
            return Results.Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        var principal = await AuthPrincipal.CreateAsync(userManager, user, request.GetScopes());
        return Results.SignIn(principal, properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> ExchangeAsync(
        HttpContext context,
        UserManager<SilexGisUser> userManager,
        SignInManager<SilexGisUser> signInManager)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            return Forbidden(Errors.UnsupportedGrantType, "The specified grant type is not supported.");
        }

        // Principal from the authorization code / refresh token.
        var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var subject = result.Principal?.GetClaim(Claims.Subject);
        var user = subject is null ? null : await userManager.FindByIdAsync(subject);
        if (user is null || !await signInManager.CanSignInAsync(user))
        {
            return Forbidden(Errors.InvalidGrant, "The token is no longer valid.");
        }

        // Re-issue with fresh claims so role/profile changes propagate on refresh.
        var principal = await AuthPrincipal.CreateAsync(userManager, user, result.Principal!.GetScopes());
        return Results.SignIn(principal, properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> UserInfoAsync(
        HttpContext context, UserManager<SilexGisUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user is null)
        {
            return Forbidden(Errors.InvalidToken, "The access token is no longer valid.");
        }

        return Results.Ok(new Dictionary<string, object?>
        {
            [Claims.Subject] = user.Id.ToString(),
            [Claims.Email] = user.Email,
            [Claims.Name] = user.UserName,
            [Claims.PreferredUsername] = user.DisplayName ?? user.UserName,
            [Claims.Role] = await userManager.GetRolesAsync(user),
        });
    }

    private static async Task<IResult> LogoutAsync(HttpContext context, SignInManager<SilexGisUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return Results.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    private static IResult Forbidden(string error, string description) =>
        Results.Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
            }),
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
}
