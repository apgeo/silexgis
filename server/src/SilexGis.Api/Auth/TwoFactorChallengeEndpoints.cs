// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Auth;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Api.Auth;

public sealed record TwoFactorSendRequest(TwoFactorMethod Method);

/// <summary>Where the code went, masked, and how long it is good for.</summary>
public sealed record TwoFactorSentDto(TwoFactorMethod Method, string Destination, int ExpiresMinutes);

/// <summary>
/// Delivers the second-factor code for the sign-in currently waiting on one.
/// </summary>
/// <remarks>
/// Sits between the two halves of a two-factor sign-in: the password has been accepted and
/// Identity is holding the half-finished sign-in in its own short-lived cookie, which is what
/// identifies the account here. No account is named in the request, so this cannot be used to
/// discover whether an address exists or to send mail to someone else's.
/// </remarks>
public static class TwoFactorChallengeEndpoints
{
    public static RouteGroupBuilder MapTwoFactorChallengeEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/auth/2fa").WithTags("Auth").AllowAnonymous().RequireRateLimiting("auth");

        group.MapPost("/send", SendAsync)
            .WithValidation<TwoFactorSendRequest>()
            .WithSummary("Sends a sign-in code to the pending account's email or phone.");

        return api;
    }

    private static async Task<Results<Ok<TwoFactorSentDto>, UnauthorizedHttpResult, ProblemHttpResult>> SendAsync(
        TwoFactorSendRequest request,
        SignInManager<SilexGisUser> signInManager,
        UserManager<SilexGisUser> userManager,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        IMessageDispatcher dispatcher,
        CancellationToken ct)
    {
        // Identity's half-finished sign-in is the only thing that says who this is.
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!TwoFactorProviders.IsDelivered(request.Method))
        {
            return ApiProblems.BadRequest(
                "auth.mfa_method_not_delivered", "An authenticator code is not sent — read it from your app.");
        }

        var policy = await settings.GetSecurityAsync(ct);
        var available = TwoFactorPolicy.AvailableMethods(
            user.ToTwoFactorState(),
            policy,
            await emailDelivery.IsConfiguredAsync(ct),
            await smsDelivery.IsConfiguredAsync(ct));
        if (!available.Contains(request.Method))
        {
            return ApiProblems.BadRequest(
                "auth.mfa_method_unavailable", "That sign-in method is not available on this account.");
        }

        // Per account, not per address: the endpoint rate limit is per IP, which would still let
        // one account's address be flooded from a handful of them.
        if (await SendThrottle.TooSoonAsync(userManager, user, request.Method, policy, SendThrottle.SignIn))
        {
            return ApiProblems.BadRequest(
                "auth.mfa_resend_too_soon", "A code was just sent. Wait a moment before asking for another.");
        }

        var code = await userManager.GenerateTwoFactorTokenAsync(user, TwoFactorProviders.For(request.Method));
        var lifetime = Math.Clamp(policy.TwoFactorCodeLifetimeMinutes, 1, 60);
        var result = await AccountMessages.SendTwoFactorCodeAsync(dispatcher, user, request.Method, code, lifetime, ct);

        if (!result.Sent)
        {
            return ApiProblems.BadRequest(
                "auth.mfa_send_failed", "The code could not be sent. Try another method or ask an administrator.");
        }

        await SendThrottle.MarkSentAsync(userManager, user, request.Method, SendThrottle.SignIn);
        return TypedResults.Ok(new TwoFactorSentDto(request.Method, Mask(user, request.Method), lifetime));
    }

    private static string Mask(SilexGisUser user, TwoFactorMethod method) =>
        method == TwoFactorMethod.Email ? MaskEmail(user.Email) : MaskPhone(user.PhoneNumber);

    /// <summary>
    /// Enough of the address to recognise, not enough to learn. The caller has already given the
    /// right password, so this confirms where to look rather than revealing anything new.
    /// </summary>
    internal static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return string.Empty;
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0)
        {
            return "•••";
        }

        var local = email[..at];
        var domain = email[at..];
        return local.Length <= 2
            ? $"{local[0]}•••{domain}"
            : $"{local[0]}•••{local[^1]}{domain}";
    }

    internal static string MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return string.Empty;
        }

        var digits = new string([.. phone.Where(char.IsDigit)]);
        return digits.Length <= 2 ? "•••" : $"•••{digits[^2..]}";
    }
}
