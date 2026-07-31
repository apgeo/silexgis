// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Claims;
using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using SilexGis.Api.Auth;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Auth;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Me;

/// <summary>
/// One second factor as the account settings show it.
/// </summary>
/// <param name="Enabled">The user has switched it on.</param>
/// <param name="Allowed">The installation permits it at all.</param>
/// <param name="Ready">
/// Everything it needs is in place — a confirmed address or number, and a channel that can carry
/// the code. A method can be enabled without being ready, which is exactly the state the page has
/// to explain rather than hide.
/// </param>
/// <param name="Destination">Masked address or number the codes would go to.</param>
public sealed record MfaMethodDto(
    TwoFactorMethod Method, bool Enabled, bool Allowed, bool Ready, string? Destination);

public sealed record MfaStatusDto(
    bool Enabled,
    int RecoveryCodesLeft,
    TwoFactorMethod? PreferredMethod,
    IReadOnlyList<MfaMethodDto> Methods);

/// <summary>Enrollment material: the shared key (manual entry) and the otpauth URI (QR).</summary>
public sealed record MfaEnrollDto(string SharedKey, string AuthenticatorUri);

public sealed record MfaConfirmRequest(string Code);

public sealed record MfaPreferredRequest(TwoFactorMethod? Method);

public sealed record MfaRecoveryCodesDto(IReadOnlyList<string> Codes);

/// <summary>Where a verification code was just sent, and for how long it is good.</summary>
public sealed record MfaChallengeDto(string Destination, int ExpiresMinutes);

public sealed class MfaConfirmRequestValidator : AbstractValidator<MfaConfirmRequest>
{
    public MfaConfirmRequestValidator() => RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
}

/// <summary>
/// Two-factor management for the signed-in account, across all three methods.
/// </summary>
/// <remarks>
/// <para>
/// Enabling any method follows the same shape: prove you can produce the code before it counts.
/// An authenticator proves it from its shared key, and the two delivered methods prove it by
/// receiving one — so nobody can switch on a factor they would then be unable to satisfy.
/// </para>
/// <para>
/// Identity's own <c>TwoFactorEnabled</c> is the master switch it needs to be (it is what makes a
/// sign-in stop and ask), and is kept equal to "at least one of the three is on".
/// </para>
/// </remarks>
public static class MfaEndpoints
{
    public static RouteGroupBuilder MapMfaEndpoints(this RouteGroupBuilder api)
    {
        // This group now sends mail and text messages, so it carries the same per-IP limit as the
        // rest of the credential surface. The challenge endpoint adds a per-account throttle on
        // top, because a limit keyed on the address the request came from does not bound how much
        // one account can be made to send.
        var mfa = api.MapGroup("/me/mfa").WithTags("Me").RequireRateLimiting("auth");

        mfa.MapGet("/", StatusAsync).WithSummary("Two-factor status of the caller, method by method.");
        mfa.MapPost("/enroll", EnrollAsync)
            .WithSummary("Starts TOTP enrollment: resets the authenticator key and returns it with the QR URI.");
        mfa.MapPost("/methods/{method}/challenge", ChallengeAsync)
            .WithSummary("Sends a verification code to the address or number a delivered method would use.");
        mfa.MapPost("/methods/{method}", EnableAsync)
            .WithValidation<MfaConfirmRequest>()
            .WithSummary("Verifies a code and switches that method on, issuing recovery codes the first time.");
        mfa.MapDelete("/methods/{method}", DisableMethodAsync)
            .WithSummary("Switches one method off.");
        mfa.MapPut("/preferred", SetPreferredAsync)
            .WithSummary("Chooses which method is offered first at sign-in.");
        mfa.MapPost("/disable", DisableAllAsync)
            .WithSummary("Switches two-factor off entirely and clears the authenticator key.");
        mfa.MapPost("/recovery-codes", RegenerateRecoveryCodesAsync)
            .WithSummary("Regenerates recovery codes (invalidates previous ones).");

        return api;
    }

    private static async Task<Results<Ok<MfaStatusDto>, UnauthorizedHttpResult>> StatusAsync(
        ClaimsPrincipal principal,
        UserManager<SilexGisUser> userManager,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(await StatusForAsync(user, userManager, settings, emailDelivery, smsDelivery, ct));
    }

    private static async Task<Results<Ok<MfaEnrollDto>, UnauthorizedHttpResult, ProblemHttpResult>> EnrollAsync(
        ClaimsPrincipal principal,
        UserManager<SilexGisUser> userManager,
        IAppSettingsService settings,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var policy = await settings.GetSecurityAsync(ct);
        if (!policy.AuthenticatorTwoFactorEnabled)
        {
            return ApiProblems.BadRequest(
                "auth.mfa_method_not_allowed", "Authenticator apps are not enabled on this installation.");
        }

        // A fresh key per enrollment: re-enrolling invalidates any earlier authenticator.
        await userManager.ResetAuthenticatorKeyAsync(user);
        var key = (await userManager.GetAuthenticatorKeyAsync(user))!;

        var issuer = Uri.EscapeDataString("SilexGIS");
        var account = Uri.EscapeDataString(user.Email ?? user.UserName ?? user.Id.ToString());
        var uri = $"otpauth://totp/{issuer}:{account}?secret={key}&issuer={issuer}&digits=6";
        return TypedResults.Ok(new MfaEnrollDto(FormatKey(key), uri));
    }

    /// <summary>
    /// Sends the code that proves a delivered method works, before it is switched on. The
    /// authenticator has nothing to send — its code comes from the app.
    /// </summary>
    private static async Task<Results<Ok<MfaChallengeDto>, UnauthorizedHttpResult, ProblemHttpResult>> ChallengeAsync(
        string method,
        ClaimsPrincipal principal,
        UserManager<SilexGisUser> userManager,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        IMessageDispatcher dispatcher,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!RouteEnums.TryParseTwoFactorMethod(method, out var parsed))
        {
            return ApiProblems.NotFound("auth.mfa_method_unknown");
        }

        if (!TwoFactorProviders.IsDelivered(parsed))
        {
            return ApiProblems.BadRequest(
                "auth.mfa_method_not_delivered", "An authenticator code is not sent — read it from your app.");
        }

        var policy = await settings.GetSecurityAsync(ct);
        if (!TwoFactorPolicy.IsMethodAllowed(parsed, policy))
        {
            return ApiProblems.BadRequest(
                "auth.mfa_method_not_allowed", "That method is not enabled on this installation.");
        }

        if (Blocker(user, parsed, await emailDelivery.IsConfiguredAsync(ct), await smsDelivery.IsConfiguredAsync(ct))
            is { } blocked)
        {
            return blocked;
        }

        if (await SendThrottle.TooSoonAsync(userManager, user, parsed, policy, SendThrottle.Enrolment))
        {
            return ApiProblems.BadRequest(
                "auth.mfa_resend_too_soon", "A code was just sent. Wait a moment before asking for another.");
        }

        var code = await userManager.GenerateTwoFactorTokenAsync(user, TwoFactorProviders.For(parsed));
        var lifetime = Math.Clamp(policy.TwoFactorCodeLifetimeMinutes, 1, 60);
        var sent = await AccountMessages.SendTwoFactorCodeAsync(dispatcher, user, parsed, code, lifetime, ct);
        if (!sent.Sent)
        {
            return ApiProblems.BadRequest("auth.mfa_send_failed", "The code could not be sent.");
        }

        await SendThrottle.MarkSentAsync(userManager, user, parsed, SendThrottle.Enrolment);
        return TypedResults.Ok(new MfaChallengeDto(Destination(user, parsed) ?? string.Empty, lifetime));
    }

    private static async Task<Results<Ok<MfaRecoveryCodesDto>, UnauthorizedHttpResult, ProblemHttpResult>> EnableAsync(
        string method,
        MfaConfirmRequest request,
        ClaimsPrincipal principal,
        UserManager<SilexGisUser> userManager,
        SilexGisDbContext db,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!RouteEnums.TryParseTwoFactorMethod(method, out var parsed))
        {
            return ApiProblems.NotFound("auth.mfa_method_unknown");
        }

        var policy = await settings.GetSecurityAsync(ct);
        if (!TwoFactorPolicy.IsMethodAllowed(parsed, policy))
        {
            return ApiProblems.BadRequest(
                "auth.mfa_method_not_allowed", "That method is not enabled on this installation.");
        }

        if (Blocker(user, parsed, await emailDelivery.IsConfiguredAsync(ct), await smsDelivery.IsConfiguredAsync(ct))
            is { } blocked)
        {
            return blocked;
        }

        var code = request.Code.Replace(" ", string.Empty);
        var valid = parsed == TwoFactorMethod.Authenticator
            ? await userManager.VerifyTwoFactorTokenAsync(
                user, userManager.Options.Tokens.AuthenticatorTokenProvider, code)
            : await userManager.VerifyTwoFactorTokenAsync(user, TwoFactorProviders.For(parsed), code);
        if (!valid)
        {
            return ApiProblems.BadRequest("auth.mfa_invalid", "The code is not valid.");
        }

        // Recovery codes are issued when two-factor first comes on and never silently reissued:
        // regenerating them here would quietly invalidate the set the user has already written down.
        var first = !TwoFactorPolicy.AnyEnabled(user.ToTwoFactorState());

        SetMethod(user, parsed, true);
        user.PreferredTwoFactorMethod ??= parsed;
        await SyncMasterSwitchAsync(user, userManager, db, ct);

        var codes = first
            ? await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10)
            : null;
        return TypedResults.Ok(new MfaRecoveryCodesDto(codes is null ? [] : [.. codes]));
    }

    private static async Task<Results<Ok<MfaStatusDto>, UnauthorizedHttpResult, ProblemHttpResult>> DisableMethodAsync(
        string method,
        ClaimsPrincipal principal,
        UserManager<SilexGisUser> userManager,
        SilexGisDbContext db,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!RouteEnums.TryParseTwoFactorMethod(method, out var parsed))
        {
            return ApiProblems.NotFound("auth.mfa_method_unknown");
        }

        SetMethod(user, parsed, false);
        if (parsed == TwoFactorMethod.Authenticator)
        {
            await userManager.ResetAuthenticatorKeyAsync(user);
        }

        if (user.PreferredTwoFactorMethod == parsed)
        {
            user.PreferredTwoFactorMethod = null;
        }

        await SyncMasterSwitchAsync(user, userManager, db, ct);
        return TypedResults.Ok(await StatusForAsync(user, userManager, settings, emailDelivery, smsDelivery, ct));
    }

    private static async Task<Results<Ok<MfaStatusDto>, UnauthorizedHttpResult, ProblemHttpResult>> SetPreferredAsync(
        MfaPreferredRequest request,
        ClaimsPrincipal principal,
        UserManager<SilexGisUser> userManager,
        SilexGisDbContext db,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (request.Method is { } method && !IsEnabled(user, method))
        {
            return ApiProblems.BadRequest(
                "auth.mfa_method_not_enabled", "Switch that method on before making it the default.");
        }

        user.PreferredTwoFactorMethod = request.Method;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await StatusForAsync(user, userManager, settings, emailDelivery, smsDelivery, ct));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult>> DisableAllAsync(
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

        user.TwoFactorAuthenticatorEnabled = false;
        user.TwoFactorEmailEnabled = false;
        user.TwoFactorSmsEnabled = false;
        user.PreferredTwoFactorMethod = null;
        await userManager.ResetAuthenticatorKeyAsync(user);
        await SyncMasterSwitchAsync(user, userManager, db, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<MfaRecoveryCodesDto>, UnauthorizedHttpResult, ProblemHttpResult>> RegenerateRecoveryCodesAsync(
        ClaimsPrincipal principal, UserManager<SilexGisUser> userManager)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!await userManager.GetTwoFactorEnabledAsync(user))
        {
            return ApiProblems.BadRequest("auth.mfa_not_enabled", "Two-factor authentication is not enabled.");
        }

        var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        return TypedResults.Ok(new MfaRecoveryCodesDto([.. codes!]));
    }

    /// <summary>
    /// The reason a method cannot be used yet, or null when it can. Kept in one place because the
    /// challenge and the enable step must refuse for exactly the same reasons.
    /// </summary>
    private static ProblemHttpResult? Blocker(
        SilexGisUser user, TwoFactorMethod method, bool mailConfigured, bool smsConfigured) => method switch
        {
            TwoFactorMethod.Email when !user.EmailConfirmed => ApiProblems.BadRequest(
                "auth.mfa_email_unconfirmed", "Confirm your email address before using it as a second factor."),
            TwoFactorMethod.Email when !mailConfigured => ApiProblems.BadRequest(
                "auth.mfa_channel_unavailable", "This installation has no mail server configured."),
            TwoFactorMethod.Sms when !user.PhoneNumberConfirmed => ApiProblems.BadRequest(
                "auth.mfa_phone_unconfirmed", "Confirm a phone number before using it as a second factor."),
            TwoFactorMethod.Sms when !smsConfigured => ApiProblems.BadRequest(
                "auth.mfa_channel_unavailable", "This installation has no SMS gateway configured."),
            _ => null,
        };

    private static void SetMethod(SilexGisUser user, TwoFactorMethod method, bool enabled)
    {
        switch (method)
        {
            case TwoFactorMethod.Authenticator:
                user.TwoFactorAuthenticatorEnabled = enabled;
                break;
            case TwoFactorMethod.Email:
                user.TwoFactorEmailEnabled = enabled;
                break;
            case TwoFactorMethod.Sms:
                user.TwoFactorSmsEnabled = enabled;
                break;
        }
    }

    private static bool IsEnabled(SilexGisUser user, TwoFactorMethod method) => method switch
    {
        TwoFactorMethod.Authenticator => user.TwoFactorAuthenticatorEnabled,
        TwoFactorMethod.Email => user.TwoFactorEmailEnabled,
        TwoFactorMethod.Sms => user.TwoFactorSmsEnabled,
        _ => false,
    };

    /// <summary>
    /// Keeps Identity's single flag equal to "any method is on". Everything that makes a sign-in
    /// stop and ask for a second factor reads that flag, so it can never drift from these three.
    /// </summary>
    private static async Task SyncMasterSwitchAsync(
        SilexGisUser user, UserManager<SilexGisUser> userManager, SilexGisDbContext db, CancellationToken ct)
    {
        var any = TwoFactorPolicy.AnyEnabled(user.ToTwoFactorState());
        if (user.TwoFactorEnabled != any)
        {
            await userManager.SetTwoFactorEnabledAsync(user, any);
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<MfaStatusDto> StatusForAsync(
        SilexGisUser user,
        UserManager<SilexGisUser> userManager,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        CancellationToken ct)
    {
        var policy = await settings.GetSecurityAsync(ct);
        var mailConfigured = await emailDelivery.IsConfiguredAsync(ct);
        var smsConfigured = await smsDelivery.IsConfiguredAsync(ct);
        var state = user.ToTwoFactorState();
        var ready = TwoFactorPolicy.AvailableMethods(state, policy, mailConfigured, smsConfigured);

        var methods = Enum.GetValues<TwoFactorMethod>()
            .Select(method => new MfaMethodDto(
                method,
                IsEnabled(user, method),
                TwoFactorPolicy.IsMethodAllowed(method, policy),
                ready.Contains(method),
                Destination(user, method)))
            .ToArray();

        return new MfaStatusDto(
            TwoFactorPolicy.AnyEnabled(state),
            await userManager.CountRecoveryCodesAsync(user),
            TwoFactorPolicy.PreferredMethod(state, policy, mailConfigured, smsConfigured),
            methods);
    }

    private static string? Destination(SilexGisUser user, TwoFactorMethod method) => method switch
    {
        TwoFactorMethod.Email => TwoFactorChallengeEndpoints.MaskEmail(user.Email),
        TwoFactorMethod.Sms => TwoFactorChallengeEndpoints.MaskPhone(user.PhoneNumber),
        _ => null,
    };

    /// <summary>Groups the base32 key in fours for manual entry.</summary>
    private static string FormatKey(string key)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(key, i, Math.Min(4, key.Length - i));
        }

        return builder.ToString().ToLowerInvariant();
    }
}
