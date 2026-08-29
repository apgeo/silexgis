// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Claims;
using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SilexGis.Api.Auth;
using SilexGis.Api.Common;
using SilexGis.Domain.Auth;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Me;

public sealed record PhoneChangeRequest(string PhoneNumber);

public sealed record PhoneConfirmRequest(string Code);

public sealed record PhoneStatusDto(
    string? PhoneNumber, bool Confirmed, string? PendingPhoneNumber, bool SmsConfigured);

public sealed record PhoneChallengeDto(string Destination, int ExpiresMinutes);

public sealed partial class PhoneChangeRequestValidator : AbstractValidator<PhoneChangeRequest>
{
    public PhoneChangeRequestValidator() =>
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .MaximumLength(32)
            .Must(value => E164().IsMatch(value.Replace(" ", string.Empty)))
            .WithMessage("Enter the number in international form, for example +40712345678.");

    /// <summary>
    /// International form only. A gateway is given a number with no idea which country the sender
    /// is in, so a national one ("0712…") is not something the application can correct for them —
    /// and a code texted to the wrong country is a support ticket nobody can diagnose.
    /// </summary>
    [GeneratedRegex(@"^\+[1-9]\d{6,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex E164();
}

public sealed class PhoneConfirmRequestValidator : AbstractValidator<PhoneConfirmRequest>
{
    public PhoneConfirmRequestValidator() => RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
}

/// <summary>
/// The account's phone number and the verified flow for setting it.
/// </summary>
/// <remarks>
/// Mirrors the email-change rule deliberately: the live number is untouched until a code texted to
/// the new one comes back, so a typo cannot quietly redirect sign-in codes — which, for a number
/// that is about to become a second factor, is the difference between a small mistake and losing
/// the account.
/// </remarks>
public static class MePhoneEndpoints
{
    public static RouteGroupBuilder MapMePhoneEndpoints(this RouteGroupBuilder api)
    {
        // A credential surface that sends messages to a destination the caller chooses, so it is
        // rate limited exactly as the email one is.
        var phone = api.MapGroup("/me/phone").WithTags("Me").RequireRateLimiting("auth");

        phone.MapGet("/", StatusAsync).WithSummary("The caller's phone number and its verification state.");
        phone.MapPost("/change", RequestChangeAsync)
            .WithValidation<PhoneChangeRequest>()
            .WithSummary("Starts a verified change of the caller's phone number; texts a code to it.");
        phone.MapPost("/resend", ResendAsync)
            .WithSummary("Texts the pending verification code again.");
        phone.MapPost("/confirm", ConfirmAsync)
            .WithValidation<PhoneConfirmRequest>()
            .WithSummary("Completes a pending phone change with the texted code.");
        phone.MapDelete("/", RemoveAsync)
            .WithSummary("Removes the number, switching off SMS as a second factor with it.");

        return api;
    }

    private static async Task<Results<Ok<PhoneStatusDto>, UnauthorizedHttpResult>> StatusAsync(
        ClaimsPrincipal principal,
        UserManager<SilexGisUser> userManager,
        ISmsDelivery smsDelivery,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new PhoneStatusDto(
            user.PhoneNumber,
            user.PhoneNumberConfirmed,
            user.PendingPhoneNumber,
            await smsDelivery.IsConfiguredAsync(ct)));
    }

    private static async Task<Results<Ok<PhoneChallengeDto>, UnauthorizedHttpResult, ProblemHttpResult>> RequestChangeAsync(
        PhoneChangeRequest request,
        ClaimsPrincipal principal,
        UserManager<SilexGisUser> userManager,
        SilexGisDbContext db,
        IAppSettingsService settings,
        ISmsDelivery smsDelivery,
        IMessageDispatcher dispatcher,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!await smsDelivery.IsConfiguredAsync(ct))
        {
            return ApiProblems.BadRequest(
                "me.sms_unavailable", "This installation has no SMS gateway configured.");
        }

        var number = request.PhoneNumber.Replace(" ", string.Empty);
        if (string.Equals(number, user.PhoneNumber, StringComparison.Ordinal) && user.PhoneNumberConfirmed)
        {
            return ApiProblems.BadRequest("me.phone_unchanged", "That is already your confirmed number.");
        }

        // Deliberately not checked here. Answering "another account already uses that number"
        // before anything has been proved would let any signed-in caller ask, one number at a
        // time, whether a number belongs to a member of this installation — a question the
        // profile rules refuse to answer, since a phone number is a visibility-governed field.
        // The number is checked at confirmation instead, where the caller has returned a code
        // texted to it and so controls it.

        var policy = await settings.GetSecurityAsync(ct);
        if (await SendThrottle.TooSoonAsync(userManager, user, TwoFactorMethod.Sms, policy, SendThrottle.PhoneChange))
        {
            return ApiProblems.BadRequest("me.phone_resend_too_soon", "Wait a moment before asking again.");
        }

        user.PendingPhoneNumber = number;
        await db.SaveChangesAsync(ct);

        return await SendCodeAsync(user, number, userManager, dispatcher, policy, ct);
    }

    private static async Task<Results<Ok<PhoneChallengeDto>, UnauthorizedHttpResult, ProblemHttpResult>> ResendAsync(
        ClaimsPrincipal principal,
        UserManager<SilexGisUser> userManager,
        SilexGisDbContext db,
        IAppSettingsService settings,
        IMessageDispatcher dispatcher,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (user.PendingPhoneNumber is not { } pending)
        {
            return ApiProblems.BadRequest("me.phone_change_missing", "There is no pending number.");
        }

        var policy = await settings.GetSecurityAsync(ct);
        if (await SendThrottle.TooSoonAsync(userManager, user, TwoFactorMethod.Sms, policy, SendThrottle.PhoneChange))
        {
            return ApiProblems.BadRequest("me.phone_resend_too_soon", "Wait a moment before asking again.");
        }

        return await SendCodeAsync(user, pending, userManager, dispatcher, policy, ct);
    }

    private static async Task<Results<Ok<PhoneStatusDto>, UnauthorizedHttpResult, ProblemHttpResult>> ConfirmAsync(
        PhoneConfirmRequest request,
        ClaimsPrincipal principal,
        UserManager<SilexGisUser> userManager,
        SilexGisDbContext db,
        ISmsDelivery smsDelivery,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (user.PendingPhoneNumber is not { } pending)
        {
            return ApiProblems.BadRequest("me.phone_change_missing", "There is nothing to confirm.");
        }

        if (await CodeAttempts.LockedOutAsync(userManager, user))
        {
            return ApiProblems.BadRequest(
                "auth.locked_out", "Account temporarily locked after repeated failures.");
        }

        // The token is bound to the number it was issued for, so a code texted to one number
        // cannot be replayed to confirm another.
        var valid = await userManager.VerifyChangePhoneNumberTokenAsync(
            user, request.Code.Replace(" ", string.Empty), pending);
        if (!valid)
        {
            await CodeAttempts.FailedAsync(userManager, user);
            return ApiProblems.BadRequest("me.phone_confirm_invalid", "The code is not valid or has expired.");
        }

        // One number reaching exactly one account is what lets an inbound message ever be
        // attributed. Checked here, where the caller has proved they control the number.
        if (await db.Users.AnyAsync(u => u.Id != user.Id && u.PhoneNumber == pending, ct))
        {
            return ApiProblems.BadRequest(
                "me.phone_taken", "Another account already uses that number.");
        }

        await CodeAttempts.SucceededAsync(userManager, user);

        user.PhoneNumber = pending;
        user.PhoneNumberConfirmed = true;
        user.PendingPhoneNumber = null;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (IsPhoneNumberCollision(e))
        {
            // The read above is not a lock, so two accounts confirming the same number can both
            // pass it and the index decides. Answering with the same refusal the read gives means
            // the endpoint and the index say one thing rather than a 500 that says nothing.
            db.ChangeTracker.Clear();
            return ApiProblems.BadRequest(
                "me.phone_taken", "Another account already uses that number.");
        }

        return TypedResults.Ok(new PhoneStatusDto(
            user.PhoneNumber, true, null, await smsDelivery.IsConfiguredAsync(ct)));
    }

    /// <summary>Whether a failed save was the phone number's uniqueness index refusing a duplicate.</summary>
    private static bool IsPhoneNumberCollision(DbUpdateException e) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static async Task<Results<NoContent, UnauthorizedHttpResult>> RemoveAsync(
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

        user.PhoneNumber = null;
        user.PhoneNumberConfirmed = false;
        user.PendingPhoneNumber = null;

        // Leaving the method on would be a second factor pointing at nothing — the account would
        // still be asked for a texted code that can never arrive.
        user.TwoFactorSmsEnabled = false;
        if (user.PreferredTwoFactorMethod == TwoFactorMethod.Sms)
        {
            user.PreferredTwoFactorMethod = null;
        }

        if (!TwoFactorPolicy.AnyEnabled(user.ToTwoFactorState()) && user.TwoFactorEnabled)
        {
            await userManager.SetTwoFactorEnabledAsync(user, false);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<PhoneChallengeDto>, UnauthorizedHttpResult, ProblemHttpResult>> SendCodeAsync(
        SilexGisUser user,
        string number,
        UserManager<SilexGisUser> userManager,
        IMessageDispatcher dispatcher,
        SecuritySettings policy,
        CancellationToken ct)
    {
        var code = await userManager.GenerateChangePhoneNumberTokenAsync(user, number);
        var lifetime = Math.Clamp(policy.TwoFactorCodeLifetimeMinutes, 1, 60);
        var sent = await AccountMessages.SendPhoneVerificationAsync(dispatcher, user, number, code, lifetime, ct);
        if (!sent.Sent)
        {
            return ApiProblems.BadRequest("me.sms_send_failed", "The code could not be sent to that number.");
        }

        // Stamped only once a text really went out, so a gateway that is refusing does not lock
        // the account holder out of trying again.
        await SendThrottle.MarkSentAsync(userManager, user, TwoFactorMethod.Sms, SendThrottle.PhoneChange);
        return TypedResults.Ok(new PhoneChallengeDto(TwoFactorChallengeEndpoints.MaskPhone(number), lifetime));
    }
}
