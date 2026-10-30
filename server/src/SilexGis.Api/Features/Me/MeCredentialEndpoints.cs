// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using SilexGis.Api.Auth;
using SilexGis.Api.Common;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Me;

public sealed record UsernameChangeRequest(string Username);

/// <param name="RevokeAccessKeys">Also revoke every personal access key of the account. Off unless
/// asked for: sessions end either way, and a key lives on a device the holder may still have.</param>
public sealed record PasswordChangeRequest(string CurrentPassword, string NewPassword, bool RevokeAccessKeys = false);

public sealed class UsernameChangeRequestValidator : AbstractValidator<UsernameChangeRequest>
{
    public UsernameChangeRequestValidator() =>
        RuleFor(x => x.Username)
            .NotEmpty()
            .MaximumLength(64)
            // Identity's own allowed set; a name outside it cannot be saved anyway.
            .Matches("^[A-Za-z0-9._@+-]+$")
            .WithMessage("Use letters, digits and . _ @ + - only.");
}

public sealed class PasswordChangeRequestValidator : AbstractValidator<PasswordChangeRequest>
{
    public PasswordChangeRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(10);
    }
}

/// <summary>
/// The caller's own credentials: the sign-in name and the password.
/// </summary>
/// <remarks>
/// Signing in resolves an account by email address, so a chosen user name is a display handle
/// and never a second way to log in. Both operations rotate the security stamp, so both refresh
/// the sign-in cookie — and a password change additionally ends every issued session, which the
/// rotated stamp on its own would not do, because issued tokens carry no stamp to compare.
/// </remarks>
public static class MeCredentialEndpoints
{
    public static RouteGroupBuilder MapMeCredentialEndpoints(this RouteGroupBuilder api)
    {
        var me = api.MapGroup("/me").WithTags("Me").RequireRateLimiting("auth");

        me.MapPut("/username", ChangeUsernameAsync)
            .WithValidation<UsernameChangeRequest>()
            .WithSummary("Changes the caller's user name.");
        me.MapPut("/password", ChangePasswordAsync)
            .WithValidation<PasswordChangeRequest>()
            .WithSummary("Changes the caller's password, verifying the current one.");

        return api;
    }

    private static async Task<Results<Ok<MeDto>, UnauthorizedHttpResult, ProblemHttpResult>> ChangeUsernameAsync(
        UsernameChangeRequest request,
        ClaimsPrincipal principal,
        UserManager<SilexGisUser> userManager,
        SignInManager<SilexGisUser> signInManager,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var username = request.Username.Trim();
        if (string.Equals(username, user.UserName, StringComparison.Ordinal))
        {
            return TypedResults.Ok(MeMapping.ToDto(
                user, await MeEndpoints.AddressesAsync(db, user.Id, ct), tokens,
                await MeEndpoints.CaverIdAsync(db, user.Id, ct)));
        }

        // Unlike an email address, a user name is a public handle — saying it is taken discloses
        // nothing that the member directory does not already show.
        if (await userManager.FindByNameAsync(username) is not null)
        {
            return ApiProblems.BadRequest("me.username_taken", "That name is already in use.");
        }

        var result = await userManager.SetUserNameAsync(user, username);
        if (!result.Succeeded)
        {
            return result.Has("DuplicateUserName")
                ? ApiProblems.BadRequest("me.username_taken", "That name is already in use.")
                : IdentityProblems.From(result, "me.username_invalid");
        }

        await signInManager.RefreshSignInAsync(user);

        return TypedResults.Ok(MeMapping.ToDto(
            user, await MeEndpoints.AddressesAsync(db, user.Id, ct), tokens,
                await MeEndpoints.CaverIdAsync(db, user.Id, ct)));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> ChangePasswordAsync(
        PasswordChangeRequest request,
        ClaimsPrincipal principal,
        UserManager<SilexGisUser> userManager,
        SignInManager<SilexGisUser> signInManager,
        IOpenIddictTokenManager issuedTokens,
        IOpenIddictAuthorizationManager grants,
        SilexGisDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        // An account that only ever signed in through an external provider has no password to
        // verify; it has to go through the reset flow to acquire one.
        if (!await userManager.HasPasswordAsync(user))
        {
            return ApiProblems.BadRequest("me.password_not_set", "This account signs in without a password.");
        }

        // Warns the account holder that this happened, which is the whole point of a security
        // alert: if it was not them, someone else knows their password. Queued *before* the change
        // so that the two commit together: the Identity store writes through this same scoped
        // context, so its own save is what persists this row, and a change that is refused never
        // reaches that save — leaving the row tracked, unsaved and discarded with the request.
        // An alert that outlived a rejected password change would be a lie in the other direction.
        NotificationQueue.Enqueue(
            db, user.Id, NotificationCategory.SecurityAlerts,
            MessageTemplateCatalog.NotifySecurityPasswordChanged,
            new Dictionary<string, string>());

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);

        // The account row's concurrency stamp moves on every write to the account, and the client
        // writes interface preferences on a timer, so one can land between loading the user above
        // and saving the new hash. Identity reports that as a concurrency failure, which would
        // reach the caller as a refused password change although nothing about it was wrong. So
        // the change is tried again from a freshly loaded user (and a freshly queued alert, as the
        // cleared tracker drops the first) a few times before a failure is believed.
        for (var retry = 0; retry < 4 && result.Has("ConcurrencyFailure"); retry++)
        {
            db.ChangeTracker.Clear();
            user = await userManager.GetUserAsync(principal);
            if (user is null)
            {
                return TypedResults.Unauthorized();
            }

            NotificationQueue.Enqueue(
                db, user.Id, NotificationCategory.SecurityAlerts,
                MessageTemplateCatalog.NotifySecurityPasswordChanged,
                new Dictionary<string, string>());
            result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        }

        if (!result.Succeeded)
        {
            return result.Has("PasswordMismatch")
                ? ApiProblems.BadRequest("me.password_incorrect", "The current password is not right.")
                : IdentityProblems.From(result, "me.password_invalid");
        }

        // Changing a password is what somebody does when they think a credential has escaped, so
        // it has to end the sessions that credential opened — on every client and every device,
        // not merely on the one asking. Nothing else reaches them: an issued token carries no
        // security stamp, and the exchange that renews one never asks when the password changed.
        await SessionRevocation.EndAllSessionsAsync(issuedTokens, grants, db, clock.GetUtcNow(), user.Id, request.RevokeAccessKeys, ct);

        await signInManager.RefreshSignInAsync(user);
        return TypedResults.NoContent();
    }
}
