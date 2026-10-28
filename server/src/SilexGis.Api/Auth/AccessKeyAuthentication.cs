// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Auth;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Auth;

/// <summary>Names the personal access key machinery is known by.</summary>
public static class AccessKeySchemes
{
    /// <summary>
    /// The API's default scheme: it looks at the bearer value and hands it to the key handler or
    /// to the sign-in token validation, and does nothing else itself.
    /// </summary>
    public const string Bearer = "SilexGis.Bearer";

    /// <summary>The scheme that checks a personal access key.</summary>
    public const string AccessKey = "SilexGis.AccessKey";

    /// <summary>
    /// The claim a key-authenticated principal carries and a signed-in one never does: which
    /// purpose the key was made for. Its presence is what the route gate reads.
    /// </summary>
    public const string PurposeClaim = "silexgis:access_key_purpose";

    /// <summary>The key a request was authenticated with, so a later reader can name it.</summary>
    public const string KeyIdClaim = "silexgis:access_key_id";

    /// <summary>
    /// The bearer value of a request's Authorization header, or null when it carries none. Only
    /// the header is read: a key is never accepted from the address, where it would be logged.
    /// </summary>
    public static string? BearerValue(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : null;
    }

    /// <summary>Picks the scheme a request's bearer value belongs to.</summary>
    public static string Select(HttpContext context) =>
        AccessKeyText.IsKey(BearerValue(context.Request))
            ? AccessKey
            : OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
}

/// <summary>
/// Authenticates a request carrying a personal access key as the account that made the key.
/// </summary>
/// <remarks>
/// <para>
/// Everything is re-read on every request — the key row and the account — so revoking a key,
/// revoking every key (a password change or reset with key revocation asked for, or an
/// administrator's reset of the second factor) or locking the account takes effect on the very next
/// call. A plain password change or reset, without key revocation asked for, leaves keys working. That is the difference from a sign-in access token, which is a self-contained signed
/// value checked against its signature and keeps working for up to its fifteen minutes.
/// </para>
/// <para>
/// A key that is unknown, revoked, expired, or belongs to an account that may not sign in is not
/// refused here in words of its own: the value is handed to the sign-in token validation, which
/// rejects it exactly as it rejects any other bad bearer value. The refusal a caller sees is
/// therefore the same 401, header and body, whatever was wrong — nothing tells a guessed key from
/// a revoked one.
/// </para>
/// <para>
/// The principal is built by the same code a sign-in uses, so audit rows, access resolution and
/// every other reader of the caller see the account exactly as they would after a sign-in. It
/// carries two claims more — the key's purpose and its id — and no security stamp, because the
/// sign-in principal carries none either.
/// </para>
/// </remarks>
internal sealed class AccessKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    SilexGisDbContext db,
    UserManager<SilexGisUser> userManager,
    SignInManager<SilexGisUser> signInManager,
    IAppSettingsService settings,
    IEmailDelivery emailDelivery,
    TimeProvider clock)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>
    /// How finely the last use is recorded. A device polling every few seconds would otherwise
    /// write the row on every poll.
    /// </summary>
    internal static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(1);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var value = AccessKeySchemes.BearerValue(Request);
        if (!AccessKeyText.IsPlausible(value))
        {
            return await AsInvalidBearerAsync();
        }

        var ct = Context.RequestAborted;
        var now = clock.GetUtcNow();
        var hash = AccessKeyText.Hash(value!);
        var key = await db.ApiAccessKeys.AsNoTracking()
            .Where(k => k.SecretHash == hash && k.RevokedAt == null && k.ExpiresAt > now)
            .Select(k => new { k.Id, k.UserId, k.Purpose, k.LastUsedAt })
            .FirstOrDefaultAsync(ct);
        if (key is null)
        {
            return await AsInvalidBearerAsync();
        }

        // The same three questions the token exchange asks before renewing a sign-in: the account
        // still exists and may sign in, it is not locked (which is how an administrator disables
        // an account), and the address-confirmation policy does not bar it.
        var user = await userManager.FindByIdAsync(key.UserId.ToString());
        if (user is null
            || !await signInManager.CanSignInAsync(user)
            || await userManager.IsLockedOutAsync(user)
            || await ConnectEndpoints.ConfirmationRequiredAsync(user, settings, emailDelivery, ct))
        {
            return await AsInvalidBearerAsync();
        }

        if (key.LastUsedAt is null || now - key.LastUsedAt.Value >= LastUsedResolution)
        {
            // A direct update: when a key was last used is bookkeeping, not an act, and a tracked
            // save would write a history row for every minute a device is busy. The predicate
            // repeats the throttle so two requests racing each other write once.
            var floor = now - LastUsedResolution;
            await db.ApiAccessKeys
                .Where(k => k.Id == key.Id && (k.LastUsedAt == null || k.LastUsedAt <= floor))
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now), ct);
        }

        var identity = await AuthPrincipal.IdentityAsync(userManager, user, Scheme.Name);
        identity.SetClaim(AccessKeySchemes.PurposeClaim, key.Purpose.ToString());
        identity.SetClaim(AccessKeySchemes.KeyIdClaim, key.Id.ToString());
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    // The bad value goes to the sign-in token validation, which refuses it as it refuses every
    // other bad bearer value — so its own challenge, which the default scheme forwards to it,
    // answers this request exactly as it answers those.
    private Task<AuthenticateResult> AsInvalidBearerAsync() =>
        Context.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
}

/// <summary>Declares that a route accepts personal access keys made for one purpose.</summary>
public sealed record AcceptsAccessKeyMetadata(AccessKeyPurpose Purpose);

public static class AccessKeyEndpointExtensions
{
    /// <summary>
    /// Opens this route, or every route of this group, to personal access keys made for
    /// <paramref name="purpose"/>. Every route that does not say so refuses a key-authenticated
    /// caller, centrally, whatever else it accepts.
    /// </summary>
    public static TBuilder AcceptsAccessKey<TBuilder>(this TBuilder builder, AccessKeyPurpose purpose)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new AcceptsAccessKeyMetadata(purpose));
}

/// <summary>
/// Keeps a personal access key to the routes that declare its purpose — one rule for the whole
/// application, so no route can be forgotten.
/// </summary>
/// <remarks>
/// <para>
/// A key is pasted into a device that lives in a pocket, a car and a tent, so it must never be a
/// whole-account credential. A route that needs an account and does not declare the key's purpose
/// answers <c>403</c> with <see cref="NotAcceptedCode"/> — whatever the route is, including the
/// key routes themselves (a key cannot make or list keys), changing a password, and the sign-in
/// server's own user-information route outside the versioned API.
/// </para>
/// <para>
/// A route that needs no account at all — the sign-in routes, the application's identity, the
/// tokenised file deliveries a device downloads results from — is served to the request as to an
/// anonymous one: the key's principal is taken away before the route runs, so a route that says
/// more to a signed-in caller can never be made to say it to a key.
/// </para>
/// <para>
/// It runs between authentication and authorization, where the matched route and the caller are
/// both known and nothing has been decided yet.
/// </para>
/// </remarks>
internal sealed class AccessKeyRouteGate(RequestDelegate next)
{
    public const string NotAcceptedCode = "auth.access_key_not_accepted";

    public async Task InvokeAsync(HttpContext context)
    {
        var claim = context.User.FindFirst(AccessKeySchemes.PurposeClaim);
        if (claim is null)
        {
            await next(context);
            return;
        }

        var endpoint = context.GetEndpoint();
        var accepted = Enum.TryParse<AccessKeyPurpose>(claim.Value, out var purpose)
            && endpoint?.Metadata.GetOrderedMetadata<AcceptsAccessKeyMetadata>().Any(m => m.Purpose == purpose) == true;
        if (accepted)
        {
            await next(context);
            return;
        }

        if (endpoint is null || !NeedsAccount(endpoint))
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
            await next(context);
            return;
        }

        await ApiProblems.Forbidden(
                NotAcceptedCode,
                "A personal access key does not open this route. Sign in to use it.")
            .ExecuteAsync(context);
    }

    // The same reading the authorization middleware makes: any anonymous marker wins.
    private static bool NeedsAccount(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null
        && endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null;
}
