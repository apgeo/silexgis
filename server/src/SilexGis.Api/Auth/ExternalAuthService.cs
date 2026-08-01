// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Auth;

/// <summary>Outcome of federating an external identity into a local account.</summary>
public sealed record ExternalFederationResult(SilexGisUser? User, string? ErrorCode)
{
    public static ExternalFederationResult Success(SilexGisUser user) => new(user, null);

    public static ExternalFederationResult Error(string code) => new(null, code);
}

/// <summary>
/// Federation of an external sign-in (from an OAuth/OIDC provider) into a local Identity
/// account. This is the security boundary for external login, kept free of HttpContext so
/// the account-takeover rules are exercised directly by tests:
///
/// <list type="bullet">
///   <item>An already-linked external login always signs in its own account.</item>
///   <item>Auto-linking to an existing same-email account requires a provider-verified
///   email — otherwise an attacker who controls an unverified provider account bearing a
///   victim's address could seize it. Unverified matches must link manually while signed in.</item>
///   <item>Creating a new local account happens only when the provider allows it AND the
///   email is provider-verified.</item>
/// </list>
///
/// The caller (endpoint) is responsible for establishing the cookie session afterwards.
/// </summary>
public sealed class ExternalAuthService(
    UserManager<SilexGisUser> userManager,
    IOptions<AuthOptions> authOptions,
    SilexGisDbContext db)
{
    public async Task<ExternalFederationResult> FederateAsync(
        string loginProvider, string providerKey, ClaimsPrincipal principal, ExternalProviderOptions provider)
    {
        // 1. Existing link → that account, regardless of email state.
        if (await userManager.FindByLoginAsync(loginProvider, providerKey) is { } linked)
        {
            return ExternalFederationResult.Success(linked);
        }

        var email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email");
        if (string.IsNullOrWhiteSpace(email))
        {
            // Without an email we cannot safely match or provision an account.
            return ExternalFederationResult.Error("auth.external_no_email");
        }

        var emailVerified = IsEmailVerified(principal);
        var login = new UserLoginInfo(loginProvider, providerKey, provider.DisplayName);

        // 2. Same-email local account → link, but only on a verified email.
        if (await userManager.FindByEmailAsync(email) is { } existing)
        {
            if (!emailVerified)
            {
                return ExternalFederationResult.Error("auth.external_link_required");
            }

            var linkResult = await userManager.AddLoginAsync(existing, login);
            return linkResult.Succeeded
                ? ExternalFederationResult.Success(existing)
                : ExternalFederationResult.Error("auth.external_link_failed");
        }

        // 3. No account yet → provision only when the provider permits it and the email is verified.
        if (!provider.AllowCreate)
        {
            return ExternalFederationResult.Error("auth.external_no_account");
        }

        if (!emailVerified)
        {
            return ExternalFederationResult.Error("auth.external_email_unverified");
        }

        var user = new SilexGisUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = DisplayNameFrom(principal, email),
        };

        var created = await userManager.CreateAsync(user);
        if (!created.Succeeded)
        {
            return ExternalFederationResult.Error("auth.external_create_failed");
        }

        await userManager.AddToRoleAsync(user, authOptions.Value.DefaultRole);
        // The role by itself grants nothing now — see the registration path.
        await PermissionGroupSeeder.EnsureRoleMembershipsAsync(db, user.Id, authOptions.Value.DefaultRole);
        CaverDirectory.CreateForNewAccount(db, user.Id, user.DisplayName, user.UserName, user.Email);
        await db.SaveChangesAsync();

        var addLogin = await userManager.AddLoginAsync(user, login);
        return addLogin.Succeeded
            ? ExternalFederationResult.Success(user)
            : ExternalFederationResult.Error("auth.external_link_failed");
    }

    /// <summary>
    /// Providers assert email verification via the standard <c>email_verified</c> claim
    /// (Google and conforming OIDC issuers). The GitHub handler only ever surfaces a
    /// primary <em>verified</em> address and stamps the same claim, so this one rule covers all.
    /// </summary>
    private static bool IsEmailVerified(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("email_verified");
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string DisplayNameFrom(ClaimsPrincipal principal, string email)
    {
        var name = principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue("name")
            ?? principal.FindFirstValue("preferred_username");
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at > 0 ? email[..at] : email;
    }
}
