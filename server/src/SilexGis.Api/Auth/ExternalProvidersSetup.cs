// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Identity;

namespace SilexGis.Api.Auth;

/// <summary>
/// Registers the configured external login handlers. All of them sign in to Identity's
/// external cookie scheme so <c>SignInManager.GetExternalLoginInfoAsync</c> can pick up the
/// federated identity; the local OIDC server still issues every app token.
///
/// Google and generic OIDC use the OpenID Connect handler (discovery + id_token). GitHub is
/// OAuth 2.0 only, handled by the framework's base OAuth handler with a small userinfo/email
/// fetch — no extra dependency. The callback path rides under <c>/api/v1</c> so the existing
/// reverse-proxy rules cover it without new routes; operators register
/// <c>{PublicUrl}/api/v1/signin-{name}</c> as the provider's redirect URI.
/// </summary>
public static class ExternalProvidersSetup
{
    private const string GoogleAuthority = "https://accounts.google.com";

    public static AuthenticationBuilder AddExternalProviders(
        this AuthenticationBuilder builder, IReadOnlyList<ExternalProviderOptions> providers)
    {
        foreach (var provider in providers)
        {
            switch (provider.Type?.ToLowerInvariant())
            {
                case "google":
                    AddOidc(builder, provider, provider.Authority ?? GoogleAuthority);
                    break;
                case "oidc":
                    if (string.IsNullOrWhiteSpace(provider.Authority))
                    {
                        throw new InvalidOperationException(
                            $"External provider '{provider.Name}' of type 'oidc' requires an Authority.");
                    }

                    AddOidc(builder, provider, provider.Authority);
                    break;
                case "github":
                    AddGitHub(builder, provider);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"External provider '{provider.Name}' has unknown type '{provider.Type}' " +
                        "(expected google, github or oidc).");
            }
        }

        return builder;
    }

    private static void AddOidc(AuthenticationBuilder builder, ExternalProviderOptions provider, string authority)
    {
        builder.AddOpenIdConnect(provider.Name, provider.DisplayName, options =>
        {
            options.SignInScheme = IdentityConstants.ExternalScheme;
            options.Authority = authority;
            options.ClientId = provider.ClientId;
            options.ClientSecret = provider.ClientSecret;
            options.RequireHttpsMetadata = provider.RequireHttpsMetadata;
            options.ResponseType = "code";
            options.UsePkce = true;
            options.CallbackPath = $"/api/v1/signin-{provider.Name}";
            options.SaveTokens = false;
            // Some issuers keep email/name out of the id_token; the userinfo call fills them in.
            options.GetClaimsFromUserInfoEndpoint = true;
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
            foreach (var scope in provider.Scopes)
            {
                options.Scope.Add(scope);
            }

            // Keep the email_verified claim (dropped by default) — the federation rule needs it.
            options.ClaimActions.MapJsonKey("email_verified", "email_verified");
        });
    }

    private static void AddGitHub(AuthenticationBuilder builder, ExternalProviderOptions provider)
    {
        builder.AddOAuth(provider.Name, provider.DisplayName, options =>
        {
            options.SignInScheme = IdentityConstants.ExternalScheme;
            options.ClientId = provider.ClientId;
            options.ClientSecret = provider.ClientSecret;
            options.CallbackPath = $"/api/v1/signin-{provider.Name}";
            options.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
            options.TokenEndpoint = "https://github.com/login/oauth/access_token";
            options.UserInformationEndpoint = "https://api.github.com/user";
            options.Scope.Add("read:user");
            options.Scope.Add("user:email");
            foreach (var scope in provider.Scopes)
            {
                options.Scope.Add(scope);
            }

            // Stable GitHub numeric id → provider key; login/name for display.
            options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
            options.ClaimActions.MapJsonKey(ClaimTypes.Name, "name");
            options.ClaimActions.MapJsonKey("urn:github:login", "login");

            options.Events.OnCreatingTicket = OnGitHubCreatingTicket;
        });
    }

    /// <summary>
    /// GitHub does not return the id_token/userinfo automatically for the base OAuth handler,
    /// and only exposes a verified email through the dedicated emails endpoint. Fetch both,
    /// then stamp a <c>email_verified</c> claim so the federation rule can trust the address.
    /// </summary>
    private static async Task OnGitHubCreatingTicket(OAuthCreatingTicketContext context)
    {
        var userRequest = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
        userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
        userRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        userRequest.Headers.UserAgent.ParseAdd("SilexGIS");

        using var userResponse = await context.Backchannel.SendAsync(
            userRequest, HttpCompletionOption.ResponseHeadersRead, context.HttpContext.RequestAborted);
        userResponse.EnsureSuccessStatusCode();
        using var userDoc = JsonDocument.Parse(
            await userResponse.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
        context.RunClaimActions(userDoc.RootElement);

        var (email, verified) = await FetchPrimaryEmailAsync(context);
        if (email is not null)
        {
            context.Identity!.AddClaim(new Claim(ClaimTypes.Email, email));
            context.Identity.AddClaim(new Claim("email_verified", verified ? "true" : "false"));
        }
    }

    private static async Task<(string? Email, bool Verified)> FetchPrimaryEmailAsync(OAuthCreatingTicketContext context)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("SilexGIS");

        using var response = await context.Backchannel.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, context.HttpContext.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            return (null, false);
        }

        using var doc = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));

        string? firstVerified = null;
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            var address = entry.TryGetProperty("email", out var e) ? e.GetString() : null;
            var isVerified = entry.TryGetProperty("verified", out var v) && v.GetBoolean();
            var isPrimary = entry.TryGetProperty("primary", out var p) && p.GetBoolean();
            if (address is null || !isVerified)
            {
                continue;
            }

            firstVerified ??= address;
            if (isPrimary)
            {
                return (address, true);
            }
        }

        return firstVerified is null ? (null, false) : (firstVerified, true);
    }
}
