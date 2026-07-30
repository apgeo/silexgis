// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using SilexGis.Api.Common;
using SilexGis.Domain;
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
        IEmailSender emailSender,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
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

        // Always 202, exactly as the password-reset flow does: answering "that address is taken"
        // would tell any signed-in caller whether an address has an account here, which is the
        // probe the profile visibility rules exist to remove. A user who mistypes someone else's
        // address simply never receives the message.
        if (await userManager.FindByEmailAsync(newEmail) is null)
        {
            user.PendingEmail = newEmail;
            user.PendingEmailRequestedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            await SendChangeMessageAsync(user, newEmail, userManager, emailSender, configuration, loggerFactory);
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
        if (pending is not null && string.Equals(user.UserName, previousEmail, StringComparison.OrdinalIgnoreCase))
        {
            await userManager.SetUserNameAsync(user, pending);
        }

        user.PendingEmail = null;
        user.PendingEmailRequestedAt = null;
        await db.SaveChangesAsync(ct);

        // Changing the address rotates the security stamp, which invalidates the sign-in cookie
        // the authorization endpoint needs to issue every future token. Without this the user is
        // quietly signed out when the stamp is next revalidated.
        await signInManager.RefreshSignInAsync(user);

        return TypedResults.Ok(MeMapping.ToDto(
            user, await MeEndpoints.AddressesAsync(db, user.Id, ct), await userManager.GetRolesAsync(user), tokens));
    }

    private static async Task<Results<Accepted, UnauthorizedHttpResult, ProblemHttpResult>> ResendAsync(
        ClaimsPrincipal principal,
        UserManager<SilexGisUser> userManager,
        SilexGisDbContext db,
        IEmailSender emailSender,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
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
        await SendChangeMessageAsync(user, pending, userManager, emailSender, configuration, loggerFactory);
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<Accepted, UnauthorizedHttpResult, ProblemHttpResult>> VerifyCurrentAsync(
        ClaimsPrincipal principal,
        UserManager<SilexGisUser> userManager,
        SilexGisDbContext db,
        IEmailSender emailSender,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
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
        await TrySendAsync(
            emailSender,
            loggerFactory,
            user.Email!,
            "Confirm your SilexGIS address",
            $"Confirm this address by opening {ConfirmUrl(configuration, token)}");
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

        user.PendingEmail = null;
        user.PendingEmailRequestedAt = null;
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static bool TooSoon(DateTimeOffset? lastRequestedAt) =>
        lastRequestedAt is { } last && DateTimeOffset.UtcNow - last < ResendInterval;

    private static async Task SendChangeMessageAsync(
        SilexGisUser user,
        string newEmail,
        UserManager<SilexGisUser> userManager,
        IEmailSender emailSender,
        IConfiguration configuration,
        ILoggerFactory loggerFactory)
    {
        var token = await userManager.GenerateChangeEmailTokenAsync(user, newEmail);
        await TrySendAsync(
            emailSender,
            loggerFactory,
            newEmail,
            "Confirm your new SilexGIS address",
            $"Confirm this address by opening {ConfirmUrl(configuration, token)}"
            + " If you did not ask for this, ignore this message — nothing has changed.");
    }

    private static string ConfirmUrl(IConfiguration configuration, string token)
    {
        var publicUrl = configuration.GetValue("PublicUrl", "http://localhost:8080")!.TrimEnd('/');
        return $"{publicUrl}/settings/emails?confirm={Uri.EscapeDataString(token)}";
    }

    /// <summary>
    /// Sends without letting a broken mail server fail the request. The pending change is already
    /// committed by the time this runs, so a failure is recoverable with a resend.
    /// </summary>
    private static async Task TrySendAsync(
        IEmailSender emailSender, ILoggerFactory loggerFactory, string to, string subject, string body)
    {
        try
        {
            await emailSender.SendAsync(to, subject, body);
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger(typeof(MeEmailEndpoints))
                .LogWarning(ex, "Could not send the address-confirmation message");
        }
    }
}
