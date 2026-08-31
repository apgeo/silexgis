// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Auth;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Settings;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Api.Auth;

/// <summary>
/// Session and account endpoints. All anonymous by design (documented allow-list):
/// they establish the session the OIDC authorize flow relies on.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>Prefix marking a recovery code, which is accepted whatever the enabled methods are.</summary>
    private const string RecoveryPrefix = "recovery:";

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth").AllowAnonymous().RequireRateLimiting("auth");

        auth.MapPost("/login", LoginAsync)
            .WithSummary("Signs in with email + password, establishing the cookie session used by the OIDC authorize flow.");
        auth.MapPost("/logout", LogoutAsync)
            .WithSummary("Ends the cookie session.");
        auth.MapPost("/register", RegisterAsync)
            .WithSummary("Creates an account (enabled per installation via Auth:OpenRegistration).");
        auth.MapPost("/password/forgot", ForgotPasswordAsync)
            .WithSummary("Requests a password-reset link by email. Always returns 202.");
        auth.MapPost("/password/reset", ResetPasswordAsync)
            .WithSummary("Resets the password using a reset token.");
        auth.MapPost("/email/confirm", ConfirmEmailAsync)
            .WithSummary("Confirms an account's address from the emailed link, without a session.");
        auth.MapPost("/email/resend", ResendConfirmationAsync)
            .WithSummary("Sends the address-confirmation message again. Always returns 202.");

        return api;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        SignInManager<SilexGisUser> signInManager,
        UserManager<SilexGisUser> userManager,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        CancellationToken ct)
    {
        var user = string.IsNullOrWhiteSpace(request.Email) ? null : await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return AuthProblem(StatusCodes.Status401Unauthorized, "auth.invalid_credentials", "Invalid email or password.");
        }

        var result = await signInManager.PasswordSignInAsync(user, request.Password, isPersistent: true, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            return AuthProblem(StatusCodes.Status401Unauthorized, "auth.locked_out", "Account temporarily locked after repeated failures.");
        }

        var policy = await settings.GetSecurityAsync(ct);
        var mailConfigured = await emailDelivery.IsConfiguredAsync(ct);

        // Only once the password has been accepted — either outright or by being held at the
        // second factor — so an unconfirmed address is never disclosed to someone guessing. And
        // only while mail can actually be sent: enforcing it with no mail server would lock out
        // every account that has no way to become confirmed.
        if ((result.Succeeded || result.RequiresTwoFactor)
            && policy.RequireConfirmedEmail && !user.EmailConfirmed && mailConfigured)
        {
            // PasswordSignInAsync has already established something — the session cookie, or the
            // partial one that carries the second-factor step. Neither may survive a refusal.
            await signInManager.SignOutAsync();
            return AuthProblem(
                StatusCodes.Status401Unauthorized,
                "auth.email_not_confirmed",
                "Confirm your email address before signing in.");
        }

        if (result.RequiresTwoFactor)
        {
            return await TwoFactorAsync(request, user, signInManager, policy, mailConfigured, smsDelivery, ct);
        }

        if (!result.Succeeded)
        {
            return AuthProblem(StatusCodes.Status401Unauthorized, "auth.invalid_credentials", "Invalid email or password.");
        }

        return TypedResults.Ok(new SessionDto(user.Id, user.Email!, user.DisplayName));
    }

    /// <summary>
    /// The password was right and a second factor is due. With no code supplied this answers which
    /// methods the account can use, so the client can offer a choice and ask for the code to be
    /// sent; with a code it completes the sign-in.
    /// </summary>
    private static async Task<IResult> TwoFactorAsync(
        LoginRequest request,
        SilexGisUser user,
        SignInManager<SilexGisUser> signInManager,
        SecuritySettings policy,
        bool mailConfigured,
        ISmsDelivery smsDelivery,
        CancellationToken ct)
    {
        var state = user.ToTwoFactorState();
        var smsConfigured = await smsDelivery.IsConfiguredAsync(ct);
        var available = TwoFactorPolicy.AvailableMethods(state, policy, mailConfigured, smsConfigured);

        if (string.IsNullOrWhiteSpace(request.TwoFactorCode))
        {
            return AuthProblem(
                StatusCodes.Status401Unauthorized,
                "auth.mfa_required",
                "A two-factor code is required.",
                new Dictionary<string, object?>
                {
                    ["methods"] = available.Select(m => m.ToString().ToLowerInvariant()).ToArray(),
                    ["preferredMethod"] = TwoFactorPolicy
                        .PreferredMethod(state, policy, mailConfigured, smsConfigured)?.ToString().ToLowerInvariant(),
                    // Every method can be taken away by someone other than the person signing in:
                    // they turn one off, an administrator disallows it, or a channel stops working.
                    // Recovery codes are what stops that from being a lockout, so the client is
                    // always told they will be accepted.
                    ["recoveryAccepted"] = true,
                });
        }

        var code = request.TwoFactorCode.Trim();
        var signIn = code.StartsWith(RecoveryPrefix, StringComparison.OrdinalIgnoreCase)
            ? await signInManager.TwoFactorRecoveryCodeSignInAsync(
                code[RecoveryPrefix.Length..].Replace(" ", string.Empty))
            : await SignInWithMethodAsync(signInManager, request, code, available);

        if (!signIn.Succeeded)
        {
            return signIn.IsLockedOut
                ? AuthProblem(StatusCodes.Status401Unauthorized, "auth.locked_out", "Account temporarily locked after repeated failures.")
                : AuthProblem(StatusCodes.Status401Unauthorized, "auth.mfa_invalid", "The two-factor code is not valid.");
        }

        return TypedResults.Ok(new SessionDto(user.Id, user.Email!, user.DisplayName));
    }

    private static async Task<SignInResult> SignInWithMethodAsync(
        SignInManager<SilexGisUser> signInManager,
        LoginRequest request,
        string code,
        IReadOnlyList<TwoFactorMethod> available)
    {
        // No stated method means the authenticator, which is the only one that needs no prior
        // request and the only one older clients ever sent.
        var method = request.TwoFactorMethod ?? TwoFactorMethod.Authenticator;
        if (!available.Contains(method))
        {
            return SignInResult.Failed;
        }

        var stripped = code.Replace(" ", string.Empty);
        return method == TwoFactorMethod.Authenticator
            ? await signInManager.TwoFactorAuthenticatorSignInAsync(stripped, isPersistent: true, rememberClient: false)
            : await signInManager.TwoFactorSignInAsync(
                TwoFactorProviders.For(method), stripped, isPersistent: true, rememberClient: false);
    }

    private static async Task<IResult> LogoutAsync(SignInManager<SilexGisUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        UserManager<SilexGisUser> userManager,
        IOptions<AuthOptions> options,
        IAppSettingsService settings,
        IMessageDispatcher dispatcher,
        IConfiguration configuration,
        SilexGisDbContext db,
        CancellationToken ct)
    {
        if (!options.Value.OpenRegistration)
        {
            return AuthProblem(StatusCodes.Status403Forbidden, "auth.registration_disabled", "Self-registration is disabled on this installation.");
        }

        var user = new SilexGisUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName,
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return IdentityProblem(result);
        }

        // Capabilities live in permission groups; a new account joins the configured
        // default list (none by default — the implicit All Users membership and the
        // built-ins are a complete starting point). No global role is assigned: roles
        // survive only as the seed-time mapping for pre-existing accounts.
        await PermissionGroupSeeder.EnsureMembershipsAsync(
            db, user.Id, options.Value.DefaultPermissionGroupSlugs, ct);

        CaverDirectory.CreateForNewAccount(db, user.Id, user.DisplayName, user.UserName, user.Email);
        await db.SaveChangesAsync(ct);

        var policy = await settings.GetSecurityAsync(ct);
        var confirmationSent = false;
        if (policy.SendConfirmationOnRegistration)
        {
            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            var sent = await AccountMessages.SendEmailConfirmationAsync(dispatcher, configuration, user, token, ct);
            confirmationSent = sent.Sent && sent.ChannelConfigured;
        }

        return TypedResults.Created(
            $"/api/v1/users/{user.Id}",
            new RegistrationDto(user.Id, user.Email!, user.DisplayName, confirmationSent, policy.RequireConfirmedEmail));
    }

    private static async Task<IResult> ForgotPasswordAsync(
        ForgotPasswordRequest request,
        UserManager<SilexGisUser> userManager,
        IMessageDispatcher dispatcher,
        IConfiguration configuration,
        CancellationToken ct)
    {
        // Always 202 — never reveal whether an account exists.
        if (!string.IsNullOrWhiteSpace(request.Email)
            && await userManager.FindByEmailAsync(request.Email) is { } user)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            await AccountMessages.SendPasswordResetAsync(dispatcher, configuration, user, token, ct);
        }

        return TypedResults.Accepted((string?)null);
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest request,
        UserManager<SilexGisUser> userManager,
        IOpenIddictTokenManager issuedTokens,
        IOpenIddictAuthorizationManager grants,
        SilexGisDbContext db,
        CancellationToken ct)
    {
        var user = string.IsNullOrWhiteSpace(request.Email) ? null : await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return AuthProblem(StatusCodes.Status400BadRequest, "auth.reset_invalid", "The reset token is invalid or expired.");
        }

        // The reset path is the one an attacker holding a stolen mailbox would use, so it warns
        // exactly as the signed-in change does — and, like that one, queues the warning before the
        // change so the Identity store's own save commits both or neither. A rejected token never
        // reaches that save, so the tracked row is discarded with the request.
        NotificationQueue.Enqueue(
            db, user.Id, NotificationCategory.SecurityAlerts,
            MessageTemplateCatalog.NotifySecurityPasswordChanged,
            new Dictionary<string, string>());

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            return AuthProblem(StatusCodes.Status400BadRequest, "auth.reset_invalid", "The reset token is invalid or expired.");
        }

        // This is the path taken by somebody who has lost the device the sessions live on, so it is
        // the path where ending them matters most: whoever completes a reset is not going to be
        // able to sign the lost device out afterwards, and cannot list what it holds.
        await SessionRevocation.EndAllSessionsAsync(issuedTokens, grants, user.Id, ct);

        // The reset path is the one an attacker holding a stolen mailbox would use, so it warns
        // exactly as the signed-in change does.
        NotificationQueue.Enqueue(
            db, user.Id, NotificationCategory.SecurityAlerts,
            MessageTemplateCatalog.NotifySecurityPasswordChanged,
            new Dictionary<string, string>());
        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Confirms an address from the emailed link. Anonymous on purpose: the link is often opened on
    /// a device that has never signed in, and the token is the proof — requiring a session as well
    /// would only mean fewer confirmed addresses.
    /// </summary>
    private static async Task<IResult> ConfirmEmailAsync(
        ConfirmEmailRequest request, UserManager<SilexGisUser> userManager)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
        {
            return AuthProblem(StatusCodes.Status400BadRequest, "auth.confirm_invalid", "The link is invalid or has expired.");
        }

        if (user.EmailConfirmed)
        {
            // Mail clients prefetch links and people click twice; a second confirmation is a
            // success, not an error to puzzle over.
            return TypedResults.NoContent();
        }

        var result = await userManager.ConfirmEmailAsync(user, request.Token);
        return result.Succeeded
            ? TypedResults.NoContent()
            : AuthProblem(StatusCodes.Status400BadRequest, "auth.confirm_invalid", "The link is invalid or has expired.");
    }

    private static async Task<IResult> ResendConfirmationAsync(
        ForgotPasswordRequest request,
        UserManager<SilexGisUser> userManager,
        IMessageDispatcher dispatcher,
        IConfiguration configuration,
        CancellationToken ct)
    {
        // Always 202, for the same reason the forgotten-password endpoint is: the response must
        // not say whether an address has an account, nor whether it has been confirmed.
        if (!string.IsNullOrWhiteSpace(request.Email)
            && await userManager.FindByEmailAsync(request.Email) is { EmailConfirmed: false } user)
        {
            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            await AccountMessages.SendEmailConfirmationAsync(dispatcher, configuration, user, token, ct);
        }

        return TypedResults.Accepted((string?)null);
    }

    private static IResult AuthProblem(
        int status, string code, string detail, Dictionary<string, object?>? extensions = null)
    {
        var payload = extensions ?? [];
        payload["code"] = code;
        return Results.Problem(detail: detail, statusCode: status, extensions: payload);
    }

    private static IResult IdentityProblem(IdentityResult result) =>
        Results.Problem(
            detail: string.Join(" ", result.Errors.Select(e => e.Description)),
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "auth.registration_invalid",
                ["errors"] = result.Errors.ToDictionary(e => e.Code, e => e.Description),
            });
}

public sealed record LoginRequest(
    string Email, string Password, string? TwoFactorCode = null, TwoFactorMethod? TwoFactorMethod = null);

public sealed record RegisterRequest(string Email, string Password, string? DisplayName);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

public sealed record ConfirmEmailRequest(Guid UserId, string Token);

public sealed record SessionDto(Guid UserId, string Email, string? DisplayName);

/// <summary>
/// What a new account needs to be told: whether a confirmation message actually went out, and
/// whether signing in will require it.
/// </summary>
public sealed record RegistrationDto(
    Guid UserId, string Email, string? DisplayName, bool ConfirmationSent, bool ConfirmationRequired);
