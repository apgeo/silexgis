// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Auth;
using SilexGis.Domain.Entities;
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
///   <item>A same-email account whose address was never confirmed is taken over by the person
///   the provider vouches for: the address is marked confirmed and every way in its unproven
///   holder set up is removed.</item>
///   <item>Creating a new local account happens only when the provider allows it AND the
///   email is provider-verified.</item>
/// </list>
///
/// The caller (endpoint) is responsible for establishing the cookie session afterwards.
/// </summary>
public sealed class ExternalAuthService(
    UserManager<SilexGisUser> userManager,
    IOptions<AuthOptions> authOptions,
    SilexGisDbContext db,
    IOpenIddictTokenManager issuedTokens,
    IOpenIddictAuthorizationManager grants,
    TimeProvider clock)
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

            if (!existing.EmailConfirmed)
            {
                return await TakeOverUnconfirmedAsync(existing, login);
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

        // Auto-provisioned accounts join the same configured default permission groups
        // as self-registrations — see the registration path.
        await PermissionGroupSeeder.EnsureMembershipsAsync(
            db, user.Id, authOptions.Value.DefaultPermissionGroupSlugs);
        CaverDirectory.CreateForNewAccount(db, user.Id, user.DisplayName, user.UserName, user.Email);
        await db.SaveChangesAsync();

        var addLogin = await userManager.AddLoginAsync(user, login);
        return addLogin.Succeeded
            ? ExternalFederationResult.Success(user)
            : ExternalFederationResult.Error("auth.external_link_failed");
    }

    /// <summary>
    /// Links the provider to an account whose address nobody ever proved, and makes the account
    /// the provider-proven holder's alone.
    /// </summary>
    /// <remarks>
    /// With open registration anyone can open an account under somebody else's address and choose
    /// its password, and until the address is confirmed nothing says the account is theirs. The
    /// provider has just proved the address belongs to the person signing in. Linking alone would
    /// let them work inside an account whose password, second factor, provider sign-ins, sessions
    /// and access keys somebody else may hold, so all of those go: the password is removed (which
    /// also rotates the security stamp, ending cookie sessions), every provider identity linked
    /// before this one is unlinked (an unconfirmed account may sign in and link its holder's own
    /// provider identity, and a link signs in whatever the address), every second factor is
    /// switched off and its secrets replaced, and every issued token, grant and access key is
    /// revoked. The address is marked
    /// confirmed, since the provider has just vouched for it. All of it commits together with the
    /// login and an audit row, or none of it does.
    /// </remarks>
    private async Task<ExternalFederationResult> TakeOverUnconfirmedAsync(SilexGisUser account, UserLoginInfo login)
    {
        var hadPassword = await userManager.HasPasswordAsync(account);
        var hadSecondFactor = account.TwoFactorEnabled || TwoFactorPolicy.AnyEnabled(account.ToTwoFactorState());

        await using var transaction = await db.Database.BeginTransactionAsync();

        // None of these is the incoming login: an existing link to it would have signed in
        // before an address was ever looked up.
        var unprovenLogins = await userManager.GetLoginsAsync(account);
        foreach (var unproven in unprovenLogins)
        {
            var unlinked = await userManager.RemoveLoginAsync(account, unproven.LoginProvider, unproven.ProviderKey);
            if (!unlinked.Succeeded)
            {
                return ExternalFederationResult.Error("auth.external_link_failed");
            }
        }

        var linked = await userManager.AddLoginAsync(account, login);
        if (!linked.Succeeded)
        {
            return ExternalFederationResult.Error("auth.external_link_failed");
        }

        // Staged before the saves below, so the first of them writes it inside the transaction.
        // Account rows are not recorded field by field, so this row is the record that the account
        // changed hands, and what it lost.
        db.AuditEntries.Add(new AuditEntry
        {
            UserId = account.Id,
            Action = AuditActions.ExternalAccountLinked,
            EntityType = nameof(SilexGisUser),
            EntityId = account.Id.ToString(),
            Changes = JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, object?>>
            {
                [nameof(SilexGisUser.EmailConfirmed)] = Change(false, true),
                ["HasPassword"] = Change(hadPassword, false),
                [nameof(SilexGisUser.TwoFactorEnabled)] = Change(hadSecondFactor, false),
                ["LoginProvider"] = Change(null, login.LoginProvider),
                ["RemovedLogins"] = Change(
                    unprovenLogins.Select(l => l.LoginProvider).Order(StringComparer.Ordinal).ToList(), null),
            }),
        });

        account.EmailConfirmed = true;
        var saved = hadPassword
            ? await userManager.RemovePasswordAsync(account)
            : await userManager.UpdateSecurityStampAsync(account);
        if (!saved.Succeeded)
        {
            return ExternalFederationResult.Error("auth.external_link_failed");
        }

        await TwoFactorSwitch.SwitchAllOffAsync(account, userManager, db, CancellationToken.None);
        await SessionRevocation.EndAllSessionsAsync(
            issuedTokens, grants, db, clock.GetUtcNow(), account.Id, revokeAccessKeys: true, CancellationToken.None);
        await transaction.CommitAsync();
        return ExternalFederationResult.Success(account);

        static Dictionary<string, object?> Change(object? was, object? now) => new() { ["old"] = was, ["new"] = now };
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
