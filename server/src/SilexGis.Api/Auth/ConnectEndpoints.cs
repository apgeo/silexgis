// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using SilexGis.Domain;
using SilexGis.Domain.Profiles;
using SilexGis.Domain.Settings;
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
        HttpContext context,
        UserManager<SilexGisUser> userManager,
        SignInManager<SilexGisUser> signInManager,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        CancellationToken ct)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        var session = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (session.Succeeded is not true || await userManager.GetUserAsync(session.Principal) is not { } user)
        {
            // No session → SPA login route, which returns here after sign-in.
            return RedirectToLogin(context);
        }

        // Every token for an interactive client is minted here, which makes this — not the login
        // endpoint — the place a sign-in policy has to hold. A cookie session outlives the policy
        // that allowed it: an administrator who switches confirmation on would otherwise leave
        // every already-signed-in unconfirmed account minting tokens for the life of the cookie.
        if (await ConfirmationRequiredAsync(user, settings, emailDelivery, ct))
        {
            await signInManager.SignOutAsync();
            return RedirectToLogin(context);
        }

        var principal = await AuthPrincipal.CreateAsync(userManager, user, request.GetScopes());
        return Results.SignIn(principal, properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Whether this account is barred by the address-confirmation policy. Inert while no mail
    /// server is configured, exactly as the sign-in check is — the toggle must never be able to
    /// shut out an installation that has no way to confirm anything.
    /// </summary>
    private static async Task<bool> ConfirmationRequiredAsync(
        SilexGisUser user, IAppSettingsService settings, IEmailDelivery emailDelivery, CancellationToken ct) =>
        !user.EmailConfirmed
        && (await settings.GetSecurityAsync(ct)).RequireConfirmedEmail
        && await emailDelivery.IsConfiguredAsync(ct);

    private static IResult RedirectToLogin(HttpContext context)
    {
        var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
        return Results.Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
    }

    private static async Task<IResult> ExchangeAsync(
        HttpContext context,
        UserManager<SilexGisUser> userManager,
        SignInManager<SilexGisUser> signInManager,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        CancellationToken ct)
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

        // Lockout has to be asked about separately: `CanSignInAsync` consults the confirmed-email,
        // confirmed-phone and account requirements and never looks at `LockoutEnd`. Without this a
        // lock stopped new sign-ins and nothing else — the locked account went on renewing its own
        // tokens for as long as its refresh token lived, which is weeks. That makes the control
        // indistinguishable from not having locked the account at all, whether the lock came from
        // failed passwords or from an administrator turning the key on somebody.
        if (await userManager.IsLockedOutAsync(user))
        {
            return Forbidden(Errors.InvalidGrant, "The account is locked.");
        }

        // A refresh token outlives this check by weeks — how many depends on the client it was
        // issued to — so the same policy is re-checked here rather than trusted from whenever
        // the token was first issued.
        if (await ConfirmationRequiredAsync(user, settings, emailDelivery, ct))
        {
            return Forbidden(Errors.InvalidGrant, "The account's email address is not confirmed.");
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

        // The same protected label the token carries, for the same reason: neither name claim
        // may be the email address, which has its own claim and its own scope.
        var label = ProfileProtection.Label(user);

        return Results.Ok(new Dictionary<string, object?>
        {
            [Claims.Subject] = user.Id.ToString(),
            [Claims.Email] = user.Email,
            [Claims.Name] = label,
            [Claims.PreferredUsername] = label,
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
