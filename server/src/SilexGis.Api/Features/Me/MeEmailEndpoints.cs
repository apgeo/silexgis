// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using SilexGis.Api.Common;
using SilexGis.Api.Auth;
using SilexGis.Domain.Messaging;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Me;

public sealed record EmailChangeRequest(string NewEmail);

public sealed record EmailConfirmRequest(string Token);

public sealed class EmailChangeRequestValidator : AbstractValidator<EmailChangeRequest>
{
    public EmailChangeRequestValidator() =>
        RuleFor(x => x.NewEmail).NotEmpty().MaximumLength(256).EmailAddress();
}

public sealed class EmailConfirmRequestValidator : AbstractValidator<EmailConfirmRequest>
{
    public EmailConfirmRequestValidator() => RuleFor(x => x.Token).NotEmpty();
}

/// <summary>
/// The account's single email address and the verified flow for changing it. The live address is
/// never touched until the token sent to the new one comes back, so a typo cannot lock anyone out.
/// </summary>
public static class MeEmailEndpoints
{
    /// <summary>Gap between messages, so the endpoint cannot be used to mail-bomb an address.</summary>
    private static readonly TimeSpan ResendInterval = TimeSpan.FromSeconds(60);

    public static RouteGroupBuilder MapMeEmailEndpoints(this RouteGroupBuilder api)
    {
        // The only rate-limited part of /me: this is a credential surface, and it sends mail to
        // an address the caller chooses.
        var email = api.MapGroup("/me/email").WithTags("Me").RequireRateLimiting("auth");

        email.MapPost("/change", RequestChangeAsync)
            .WithValidation<EmailChangeRequest>()
            .WithSummary("Starts a verified change of the caller's email address.");
        email.MapPost("/confirm", ConfirmAsync)
            .WithValidation<EmailConfirmRequest>()
            .WithSummary("Completes a pending email change, or confirms the current address.");
        email.MapPost("/resend", ResendAsync)
            .WithSummary("Sends the pending change message again.");
        email.MapPost("/verify", VerifyCurrentAsync)
            .WithSummary("Sends a confirmation message for the caller's current, unconfirmed address.");
        email.MapDelete("/pending", CancelAsync)
            .WithSummary("Abandons a pending email change.");

        return api;
    }

    private static async Task<Results<Accepted, UnauthorizedHttpResult, ProblemHttpResult>> RequestChangeAsync(
        EmailChangeRequest request,
        ClaimsPrincipal principal,
        UserManager<SilexGisUser> userManager,
        SilexGisDbContext db,
        IMessageDispatcher dispatcher,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var newEmail = request.NewEmail.Trim();
        if (string.Equals(newEmail, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            return ApiProblems.BadRequest("me.email_unchanged", "That is already your address.");
        }

        // Each call mails an address the caller chose, so the gap the resend route keeps applies
        // here too; the per-address rate limit alone lets one session mail a stranger every second.
        // Checked before the address is looked up, and the time is recorded below for every request
        // that gets past the checks, taken address or not — otherwise whether the NEXT request is
        // refused would tell the caller whether this address has an account.
        if (TooSoon(user.PendingEmailRequestedAt))
        {
            return ApiProblems.BadRequest("me.email_resend_too_soon", "Wait a minute before asking again.");
        }

        // An account whose user name is its address gets the new address as its user name too, and
        // Identity accepts fewer characters in a user name than in an address. Refused here, before
        // anything is mailed, so the change can never confirm with the rename failing behind it.
        if (FollowsAddress(user) && !CanBeUserName(newEmail, userManager))
        {
            return ApiProblems.BadRequest(
                "me.email_not_usable", "That address cannot be used to sign in here.");
        }

        // Always 202, exactly as the password-reset flow does: answering "that address is taken"
        // would tell any signed-in caller whether an address has an account here, which is the
        // probe the profile visibility rules exist to remove. A user who mistypes someone else's
        // address simply never receives the message.
        var free = await userManager.FindByEmailAsync(newEmail) is null;
        user.PendingEmailRequestedAt = DateTimeOffset.UtcNow;
        if (free)
        {
            user.PendingEmail = newEmail;
        }

        await db.SaveChangesAsync(ct);
        if (free)
        {
            await SendChangeMessageAsync(user, newEmail, userManager, dispatcher, configuration, ct);
        }

        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<Ok<MeDto>, UnauthorizedHttpResult, ProblemHttpResult>> ConfirmAsync(
        EmailConfirmRequest request,
        ClaimsPrincipal principal,
        UserManager<SilexGisUser> userManager,
        SignInManager<SilexGisUser> signInManager,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IMessageDispatcher dispatcher,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var pending = user.PendingEmail;
        if (pending is null && user.EmailConfirmed)
        {
            return ApiProblems.BadRequest("me.email_change_missing", "There is nothing to confirm.");
        }

        var previousEmail = user.Email;
        var renames = pending is not null && FollowsAddress(user);

        // The address and the user name move together or not at all. A user name left on the old
        // address no longer equals the address, so it would read as a chosen handle and the old
        // address would be shown as the account's name wherever it has no display name.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // One token type per situation: a pending change carries a token bound to the new
        // address, while a never-confirmed account is confirming the address it already has.
        var result = pending is not null
            ? await userManager.ChangeEmailAsync(user, pending, request.Token)
            : await userManager.ConfirmEmailAsync(user, request.Token);

        if (!result.Succeeded)
        {
            // Two accounts may queue the same address; the first to confirm wins and the other
            // gets a clear answer rather than a 500.
            return result.Has("DuplicateEmail")
                ? ApiProblems.BadRequest("me.email_taken", "That address now belongs to another account.")
                : ApiProblems.BadRequest("me.email_confirm_invalid", "The link is invalid or has expired.");
        }

        // Accounts are created with the address as the user name, so a change would otherwise
        // leave people logging in under an address that no longer exists. Someone who chose a
        // handle of their own keeps it.
        if (renames && !(await userManager.SetUserNameAsync(user, pending!)).Succeeded)
        {
            // Another account took the address as its handle after the change was asked for.
            await transaction.RollbackAsync(ct);
            return ApiProblems.BadRequest("me.email_taken", "That address now belongs to another account.");
        }

        user.PendingEmail = null;
        user.PendingEmailRequestedAt = null;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        // Warns the address the account just left. Sent straight rather than queued, because the
        // queue addresses a recipient by user id and would resolve that to the *new* address —
        // and the whole value of this warning is that it reaches the old mailbox, which is the one
        // an attacker changing the address is trying to cut off.
        if (pending is not null && previousEmail is not null)
        {
            await TrySendAsync(
                dispatcher,
                loggerFactory,
                MessageTemplateCatalog.NotifySecurityEmailChanged,
                previousEmail,
                user.Locale,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["displayName"] = user.DisplayName ?? user.FirstName ?? "there",
                    ["newEmail"] = pending,
                });
        }

        // Changing the address rotates the security stamp, which invalidates the sign-in cookie
        // the authorization endpoint needs to issue every future token. Without this the user is
        // quietly signed out when the stamp is next revalidated.
        await signInManager.RefreshSignInAsync(user);

        return TypedResults.Ok(MeMapping.ToDto(
            user, await MeEndpoints.AddressesAsync(db, user.Id, ct), tokens,
            await MeEndpoints.CaverIdAsync(db, user.Id, ct)));
    }

    private static async Task<Results<Accepted, UnauthorizedHttpResult, ProblemHttpResult>> ResendAsync(
        ClaimsPrincipal principal,
        UserManager<SilexGisUser> userManager,
        SilexGisDbContext db,
        IMessageDispatcher dispatcher,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (user.PendingEmail is not { } pending)
        {
            return ApiProblems.BadRequest("me.email_change_missing", "There is no pending change.");
        }

        if (TooSoon(user.PendingEmailRequestedAt))
        {
            return ApiProblems.BadRequest("me.email_resend_too_soon", "Wait a minute before asking again.");
        }

        user.PendingEmailRequestedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await SendChangeMessageAsync(user, pending, userManager, dispatcher, configuration, ct);
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<Accepted, UnauthorizedHttpResult, ProblemHttpResult>> VerifyCurrentAsync(
        ClaimsPrincipal principal,
        UserManager<SilexGisUser> userManager,
        SilexGisDbContext db,
        IMessageDispatcher dispatcher,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (user.EmailConfirmed)
        {
            return ApiProblems.BadRequest("me.email_already_confirmed", "That address is already confirmed.");
        }

        if (TooSoon(user.PendingEmailRequestedAt))
        {
            return ApiProblems.BadRequest("me.email_resend_too_soon", "Wait a minute before asking again.");
        }

        user.PendingEmailRequestedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        await AccountMessages.SendEmailConfirmationAsync(dispatcher, configuration, user, token, ct);
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult>> CancelAsync(
        ClaimsPrincipal principal,
        UserManager<SilexGisUser> userManager,
        SilexGisDbContext db,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        // The request time stays: it is what holds the gap between change messages, and clearing
        // it here would let a session mail a stranger once per change-and-cancel pair.
        user.PendingEmail = null;
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static bool FollowsAddress(SilexGisUser user) =>
        string.Equals(user.UserName, user.Email, StringComparison.OrdinalIgnoreCase);

    private static bool CanBeUserName(string address, UserManager<SilexGisUser> userManager) =>
        userManager.Options.User.AllowedUserNameCharacters is not { Length: > 0 } allowed
        || address.All(allowed.Contains);

    private static bool TooSoon(DateTimeOffset? lastRequestedAt) =>
        lastRequestedAt is { } last && DateTimeOffset.UtcNow - last < ResendInterval;

    /// <summary>
    /// Sends without letting a mail failure fail the request. The address change is already
    /// committed by the time this runs, so throwing here would report an error for something that
    /// succeeded.
    /// </summary>
    private static async Task TrySendAsync(
        IMessageDispatcher dispatcher,
        ILoggerFactory loggerFactory,
        string templateKey,
        string recipient,
        string? locale,
        IReadOnlyDictionary<string, string> values)
    {
        try
        {
            await dispatcher.SendAsync(templateKey, recipient, locale, values);
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger(typeof(MeEmailEndpoints))
                .LogWarning(ex, "Could not warn the previous address about an account change");
        }
    }

    private static async Task SendChangeMessageAsync(
        SilexGisUser user,
        string newEmail,
        UserManager<SilexGisUser> userManager,
        IMessageDispatcher dispatcher,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var token = await userManager.GenerateChangeEmailTokenAsync(user, newEmail);
        await AccountMessages.SendEmailChangeAsync(dispatcher, configuration, user, newEmail, token, ct);
    }
}
