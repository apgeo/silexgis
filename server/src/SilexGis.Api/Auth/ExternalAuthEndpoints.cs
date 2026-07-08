// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Api.Auth;

/// <summary>
/// External-login federation surface. The redirect flow mirrors password login: the
/// provider callback establishes the Identity cookie session, then the browser resumes the
/// OIDC authorize URL it came from (returnUrl) so the SPA still receives locally-issued
/// tokens. Linking/unlinking manage a signed-in account's external identities.
/// </summary>
public static class ExternalAuthEndpoints
{
    public static RouteGroupBuilder MapExternalAuthEndpoints(this RouteGroupBuilder api)
    {
        var pub = api.MapGroup("/auth").WithTags("Auth").AllowAnonymous();

        pub.MapGet("/config", Config)
            .WithSummary("Public sign-in configuration: open registration, external-only flag, provider buttons.");
        pub.MapGet("/external/{provider}", StartAsync)
            .RequireRateLimiting("auth")
            .WithSummary("Begins an external sign-in (or account link with ?mode=link); redirects to the provider.");
        pub.MapGet("/external/{provider}/callback", CallbackAsync)
            .WithSummary("Provider return: federates the identity into a local account, then resumes the sign-in flow.");

        var me = api.MapGroup("/me/external-logins").WithTags("Me");
        me.MapGet("/", ListLoginsAsync).WithSummary("External providers linked to the caller's account.");
        me.MapDelete("/{provider}", UnlinkAsync).WithSummary("Removes a linked external provider.");

        return api;
    }

    private static Ok<AuthConfigDto> Config(IOptions<AuthOptions> options)
    {
        var value = options.Value;
        var providers = value.ExternalProviders
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => new ExternalProviderInfo(
                p.Name, string.IsNullOrWhiteSpace(p.DisplayName) ? p.Name : p.DisplayName))
            .ToArray();

        // ExternalOnly hides the password form only when a provider actually exists, so an
        // installation can never lock itself out by flipping the flag with no provider set.
        var externalOnly = value.ExternalOnly && providers.Length > 0;
        return TypedResults.Ok(new AuthConfigDto(value.OpenRegistration, externalOnly, providers));
    }

    private static async Task<IResult> StartAsync(
        string provider,
        HttpContext http,
        string? returnUrl,
        string? mode,
        IOptions<AuthOptions> options,
        UserManager<SilexGisUser> userManager,
        SignInManager<SilexGisUser> signInManager)
    {
        if (Find(options.Value, provider) is null)
        {
            return ApiProblems.BadRequest("auth.external_provider_unknown", "Unknown external provider.");
        }

        var safeReturn = SafeLocalPath(returnUrl);
        var callback = $"/api/v1/auth/external/{provider}/callback";

        AuthenticationProperties properties;
        if (string.Equals(mode, "link", StringComparison.OrdinalIgnoreCase))
        {
            // Linking binds a provider to the signed-in account — require the cookie session
            // and pin the link to that user id (verified again on callback).
            var session = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            var userId = session.Succeeded ? userManager.GetUserId(session.Principal!) : null;
            if (userId is null)
            {
                return TypedResults.Unauthorized();
            }

            properties = signInManager.ConfigureExternalAuthenticationProperties(provider, callback, userId);
        }
        else
        {
            properties = signInManager.ConfigureExternalAuthenticationProperties(provider, callback);
        }

        properties.Items["returnUrl"] = safeReturn;
        return Results.Challenge(properties, [provider]);
    }

    private static async Task<IResult> CallbackAsync(
        string provider,
        HttpContext http,
        IOptions<AuthOptions> options,
        UserManager<SilexGisUser> userManager,
        SignInManager<SilexGisUser> signInManager,
        ExternalAuthService externalAuth)
    {
        var config = Find(options.Value, provider);
        if (config is null)
        {
            return Results.Redirect("/login?error=auth.external_provider_unknown");
        }

        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return Results.Redirect("/login?error=auth.external_failed");
        }

        var items = info.AuthenticationProperties?.Items ?? new Dictionary<string, string?>();
        var returnUrl = SafeLocalPath(items.TryGetValue("returnUrl", out var stored) ? stored : null);
        var linkUserId = items.TryGetValue("XsrfId", out var xsrf) ? xsrf : null;

        // The transient external cookie has done its job; drop it regardless of outcome.
        await http.SignOutAsync(IdentityConstants.ExternalScheme);

        if (linkUserId is not null)
        {
            return await LinkAsync(http, userManager, info, config, linkUserId, returnUrl);
        }

        var federation = await externalAuth.FederateAsync(info.LoginProvider, info.ProviderKey, info.Principal, config);
        if (federation.User is null)
        {
            return Results.Redirect($"/login?error={federation.ErrorCode}");
        }

        await signInManager.SignInAsync(federation.User, isPersistent: true);
        return Results.Redirect(returnUrl);
    }

    private static async Task<IResult> LinkAsync(
        HttpContext http,
        UserManager<SilexGisUser> userManager,
        ExternalLoginInfo info,
        ExternalProviderOptions config,
        string linkUserId,
        string returnUrl)
    {
        // Re-verify the current session owns the link so a forged callback cannot bind an
        // attacker's provider account to a victim's session.
        var session = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var currentId = session.Succeeded ? userManager.GetUserId(session.Principal!) : null;
        if (currentId is null || !string.Equals(currentId, linkUserId, StringComparison.Ordinal))
        {
            return Results.Redirect(Append(returnUrl, "linkError", "auth.external_link_denied"));
        }

        var user = await userManager.FindByIdAsync(linkUserId);
        if (user is null)
        {
            return Results.Redirect(Append(returnUrl, "linkError", "auth.external_link_denied"));
        }

        var result = await userManager.AddLoginAsync(
            user, new UserLoginInfo(info.LoginProvider, info.ProviderKey, config.DisplayName));
        return result.Succeeded
            ? Results.Redirect(Append(returnUrl, "linked", config.Name))
            : Results.Redirect(Append(returnUrl, "linkError", "auth.external_already_linked"));
    }

    private static async Task<Results<Ok<ExternalLoginsDto>, UnauthorizedHttpResult>> ListLoginsAsync(
        ClaimsPrincipal principal, UserManager<SilexGisUser> userManager)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var logins = await userManager.GetLoginsAsync(user);
        var hasPassword = await userManager.HasPasswordAsync(user);
        return TypedResults.Ok(new ExternalLoginsDto(
            [.. logins.Select(l => new ExternalLoginDto(l.LoginProvider, l.ProviderDisplayName ?? l.LoginProvider))],
            hasPassword));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> UnlinkAsync(
        string provider, ClaimsPrincipal principal, UserManager<SilexGisUser> userManager)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var logins = await userManager.GetLoginsAsync(user);
        var target = logins.FirstOrDefault(l => l.LoginProvider == provider);
        if (target is null)
        {
            return ApiProblems.NotFound("auth.external_not_linked");
        }

        // Never strip the last sign-in method from a passwordless account.
        if (!await userManager.HasPasswordAsync(user) && logins.Count <= 1)
        {
            return ApiProblems.BadRequest(
                "auth.external_last_login", "Set a password before removing your only sign-in method.");
        }

        var result = await userManager.RemoveLoginAsync(user, target.LoginProvider, target.ProviderKey);
        return result.Succeeded
            ? TypedResults.NoContent()
            : ApiProblems.BadRequest("auth.external_unlink_failed");
    }

    private static ExternalProviderOptions? Find(AuthOptions options, string provider) =>
        options.ExternalProviders.FirstOrDefault(
            p => string.Equals(p.Name, provider, StringComparison.Ordinal));

    /// <summary>Accepts only same-site absolute paths; anything else falls back to the app root.</summary>
    private static string SafeLocalPath(string? url)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith('/') || url.StartsWith("//", StringComparison.Ordinal)
            || url.StartsWith("/\\", StringComparison.Ordinal))
        {
            return "/";
        }

        return url;
    }

    private static string Append(string url, string key, string value)
    {
        var separator = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{url}{separator}{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";
    }
}

public sealed record AuthConfigDto(bool OpenRegistration, bool ExternalOnly, IReadOnlyList<ExternalProviderInfo> Providers);

public sealed record ExternalProviderInfo(string Name, string DisplayName);

public sealed record ExternalLoginsDto(IReadOnlyList<ExternalLoginDto> Logins, bool HasPassword);

public sealed record ExternalLoginDto(string Provider, string DisplayName);
