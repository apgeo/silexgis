// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using SilexGis.Api.Common;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Api.Features.Me;

public sealed record MfaStatusDto(bool Enabled, int RecoveryCodesLeft);

/// <summary>Enrollment material: the shared key (manual entry) and the otpauth URI (QR).</summary>
public sealed record MfaEnrollDto(string SharedKey, string AuthenticatorUri);

public sealed record MfaConfirmRequest(string Code);

public sealed record MfaRecoveryCodesDto(IReadOnlyList<string> Codes);

/// <summary>
/// TOTP two-factor management for the signed-in account: enroll (fresh authenticator
/// key + otpauth URI), confirm with a code (enables 2FA and issues recovery codes),
/// disable, and recovery-code regeneration.
/// </summary>
public static class MfaEndpoints
{
    public static RouteGroupBuilder MapMfaEndpoints(this RouteGroupBuilder api)
    {
        var mfa = api.MapGroup("/me/mfa").WithTags("Me");

        mfa.MapGet("/", StatusAsync).WithSummary("Two-factor status of the caller.");
        mfa.MapPost("/enroll", EnrollAsync)
            .WithSummary("Starts TOTP enrollment: resets the authenticator key and returns it with the QR URI.");
        mfa.MapPost("/confirm", ConfirmAsync)
            .WithSummary("Verifies an authenticator code, enables 2FA and returns one-time recovery codes.");
        mfa.MapPost("/disable", DisableAsync)
            .WithSummary("Disables 2FA and clears the authenticator key.");
        mfa.MapPost("/recovery-codes", RegenerateRecoveryCodesAsync)
            .WithSummary("Regenerates recovery codes (invalidates previous ones).");

        return api;
    }

    private static async Task<Results<Ok<MfaStatusDto>, UnauthorizedHttpResult>> StatusAsync(
        ClaimsPrincipal principal, UserManager<SilexGisUser> userManager)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new MfaStatusDto(
            await userManager.GetTwoFactorEnabledAsync(user),
            await userManager.CountRecoveryCodesAsync(user)));
    }

    private static async Task<Results<Ok<MfaEnrollDto>, UnauthorizedHttpResult>> EnrollAsync(
        ClaimsPrincipal principal, UserManager<SilexGisUser> userManager)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        // A fresh key per enrollment: re-enrolling invalidates any earlier authenticator.
        await userManager.ResetAuthenticatorKeyAsync(user);
        var key = (await userManager.GetAuthenticatorKeyAsync(user))!;

        var issuer = Uri.EscapeDataString("SilexGIS");
        var account = Uri.EscapeDataString(user.Email ?? user.UserName ?? user.Id.ToString());
        var uri = $"otpauth://totp/{issuer}:{account}?secret={key}&issuer={issuer}&digits=6";
        return TypedResults.Ok(new MfaEnrollDto(FormatKey(key), uri));
    }

    private static async Task<Results<Ok<MfaRecoveryCodesDto>, UnauthorizedHttpResult, ProblemHttpResult>> ConfirmAsync(
        MfaConfirmRequest request, ClaimsPrincipal principal, UserManager<SilexGisUser> userManager)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var code = request.Code.Replace(" ", string.Empty);
        var valid = await userManager.VerifyTwoFactorTokenAsync(
            user, userManager.Options.Tokens.AuthenticatorTokenProvider, code);
        if (!valid)
        {
            return ApiProblems.BadRequest("auth.mfa_invalid", "The authenticator code is not valid.");
        }

        await userManager.SetTwoFactorEnabledAsync(user, true);
        var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        return TypedResults.Ok(new MfaRecoveryCodesDto([.. codes!]));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult>> DisableAsync(
        ClaimsPrincipal principal, UserManager<SilexGisUser> userManager)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        await userManager.SetTwoFactorEnabledAsync(user, false);
        await userManager.ResetAuthenticatorKeyAsync(user);
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
