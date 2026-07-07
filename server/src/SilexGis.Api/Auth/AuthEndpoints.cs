// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SilexGis.Domain;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Api.Auth;

/// <summary>
/// Session and account endpoints. All anonymous by design (documented allow-list):
/// they establish the session the OIDC authorize flow relies on.
/// </summary>
public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth").AllowAnonymous();

        auth.MapPost("/login", LoginAsync)
            .WithSummary("Signs in with email + password, establishing the cookie session used by the OIDC authorize flow.");
        auth.MapPost("/logout", LogoutAsync)
            .WithSummary("Ends the cookie session.");
        auth.MapPost("/register", RegisterAsync)
            .WithSummary("Creates an account (enabled per installation via Auth:OpenRegistration).");
        auth.MapPost("/password/forgot", ForgotPasswordAsync)
            .WithSummary("Requests a password-reset token by email. Always returns 202.");
        auth.MapPost("/password/reset", ResetPasswordAsync)
            .WithSummary("Resets the password using a reset token.");

        return api;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, SignInManager<SilexGisUser> signInManager, UserManager<SilexGisUser> userManager)
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

        if (!result.Succeeded)
        {
            return AuthProblem(StatusCodes.Status401Unauthorized, "auth.invalid_credentials", "Invalid email or password.");
        }

        return TypedResults.Ok(new SessionDto(user.Id, user.Email!, user.DisplayName));
    }

    private static async Task<IResult> LogoutAsync(SignInManager<SilexGisUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        UserManager<SilexGisUser> userManager,
        IOptions<AuthOptions> options)
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

        await userManager.AddToRoleAsync(user, options.Value.DefaultRole);
        return TypedResults.Created($"/api/v1/users/{user.Id}", new SessionDto(user.Id, user.Email!, user.DisplayName));
    }

    private static async Task<IResult> ForgotPasswordAsync(
        ForgotPasswordRequest request, UserManager<SilexGisUser> userManager, IEmailSender emailSender)
    {
        // Always 202 — never reveal whether an account exists.
        if (!string.IsNullOrWhiteSpace(request.Email)
            && await userManager.FindByEmailAsync(request.Email) is { } user)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            await emailSender.SendAsync(
                user.Email!,
                "SilexGIS password reset",
                $"Use this token to reset your password: {token}");
        }

        return TypedResults.Accepted((string?)null);
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest request, UserManager<SilexGisUser> userManager)
    {
        var user = string.IsNullOrWhiteSpace(request.Email) ? null : await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return AuthProblem(StatusCodes.Status400BadRequest, "auth.reset_invalid", "The reset token is invalid or expired.");
        }

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        return result.Succeeded
            ? TypedResults.NoContent()
            : AuthProblem(StatusCodes.Status400BadRequest, "auth.reset_invalid", "The reset token is invalid or expired.");
    }

    private static IResult AuthProblem(int status, string code, string detail) =>
        Results.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });

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

public sealed record LoginRequest(string Email, string Password);

public sealed record RegisterRequest(string Email, string Password, string? DisplayName);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

public sealed record SessionDto(Guid UserId, string Email, string? DisplayName);
