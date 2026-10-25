// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Auth;
using SilexGis.Api.Common;
using SilexGis.Domain.Auth;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Persistence.Configurations;

namespace SilexGis.Api.Features.Me;

/// <summary>What the holder asks for when making a key.</summary>
/// <param name="Name">Which device the key is for, in the holder's own words — shown in the list.</param>
/// <param name="ExpiresInDays">
/// How many days the key lasts, from one to the installation's ceiling. Omitted, the key lasts a
/// year, or the ceiling when that is shorter. There is no "never".
/// </param>
public sealed record AccessKeyCreateRequest(string Name, int? ExpiresInDays);

public sealed class AccessKeyCreateRequestValidator : AbstractValidator<AccessKeyCreateRequest>
{
    public AccessKeyCreateRequestValidator()
    {
        RuleFor(x => x.Name).Must(n => !string.IsNullOrWhiteSpace(n))
            .WithMessage("Give the key a name, so you can tell it apart later.");
        RuleFor(x => x.Name).MaximumLength(ApiAccessKeyConfiguration.NameMaxLength);
    }
}

/// <summary>A key the account holds — its description only. The key itself is in the creation answer and nowhere else.</summary>
/// <param name="Prefix">The key's first characters, so the holder can match a device's key to its row.</param>
/// <param name="State">Where the key stands now: active, expired or revoked.</param>
public sealed record AccessKeyDto(
    Guid Id,
    string Name,
    AccessKeyPurpose Purpose,
    string Prefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt,
    AccessKeyState State);

/// <summary>The creation answer: the one time the key is ever shown.</summary>
/// <param name="Secret">The whole key, to paste into the device. It cannot be shown again.</param>
public sealed record AccessKeyCreatedDto(AccessKeyDto Key, string Secret);

/// <summary>
/// The account's keys, newest first, with the lifetimes the creation form may offer — the
/// installation's ceiling decides those, and the page has to know it to draw the form.
/// </summary>
public sealed record AccessKeyListDto(
    int MaxDays, IReadOnlyList<int> OfferedDays, int DefaultDays, List<AccessKeyDto> Keys);

/// <summary>
/// An account's personal access keys: made, listed and revoked here; used by field devices on the
/// routes that accept them.
/// </summary>
/// <remarks>
/// <para>
/// None of these routes accepts a key, so a key can neither make another key nor list the
/// account's keys: whoever holds a lost device holds only what that one key opens. That follows
/// from the routes not declaring a key purpose; the route gate refuses a key on them.
/// </para>
/// <para>
/// Each account sees and revokes only its own keys. Behind the sign-in rate limit, like the other
/// routes that hand out or change a credential.
/// </para>
/// </remarks>
public static class MeAccessKeyEndpoints
{
    public const string NotFoundCode = "access_key.not_found";

    /// <summary>A lifetime outside one day to the installation's ceiling.</summary>
    public const string LifetimeInvalidCode = "access_key.lifetime_invalid";

    public static RouteGroupBuilder MapMeAccessKeyEndpoints(this RouteGroupBuilder api)
    {
        var keys = api.MapGroup("/me/access-keys").WithTags("Me").RequireRateLimiting("auth");

        keys.MapGet("/", ListAsync)
            .WithSummary("The caller's personal access keys — descriptions only, never a key — and the lifetimes a new one may have.");
        keys.MapPost("/", CreateAsync).WithValidation<AccessKeyCreateRequest>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("Makes a personal access key for a field device; the key is returned once and never stored.");
        keys.MapDelete("/{id:guid}", RevokeAsync)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Revokes one of the caller's keys. The device holding it is refused on its next call.");

        return api;
    }

    private static async Task<Results<Ok<AccessKeyListDto>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        IOptions<AuthOptions> options,
        TimeProvider clock,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var now = clock.GetUtcNow();
        var rows = await db.ApiAccessKeys.AsNoTracking()
            .Where(k => k.UserId == user.UserId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);
        var configured = options.Value.AccessKeyMaxDays;
        return TypedResults.Ok(new AccessKeyListDto(
            AccessKeyLifetime.Ceiling(configured),
            AccessKeyLifetime.Offered(configured),
            AccessKeyLifetime.Resolve(null, configured)!.Value,
            [.. rows.Select(k => ToDto(k, now))]));
    }

    private static async Task<Results<Created<AccessKeyCreatedDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        AccessKeyCreateRequest request,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        IOptions<AuthOptions> options,
        TimeProvider clock,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var configured = options.Value.AccessKeyMaxDays;
        if (AccessKeyLifetime.Resolve(request.ExpiresInDays, configured) is not { } days)
        {
            return ApiProblems.BadRequest(
                LifetimeInvalidCode,
                $"A key lasts from 1 to {AccessKeyLifetime.Ceiling(configured)} days on this installation.");
        }

        var now = clock.GetUtcNow();
        var (secret, hash, prefix) = AccessKeyText.Mint();
        var row = new ApiAccessKey
        {
            UserId = user.UserId,
            Name = request.Name.Trim(),
            Purpose = AccessKeyPurpose.SurveyUpload,
            Prefix = prefix,
            SecretHash = hash,
            CreatedAt = now,
            ExpiresAt = now.AddDays(days),
        };
        db.ApiAccessKeys.Add(row);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/v1/me/access-keys/{row.Id}",
            new AccessKeyCreatedDto(ToDto(row, now), secret));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> RevokeAsync(
        Guid id,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        TimeProvider clock,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        // Scoped to the caller's own rows in the predicate, so another account's key is "not
        // found" here exactly as an invented one is.
        var row = await db.ApiAccessKeys
            .FirstOrDefaultAsync(k => k.Id == id && k.UserId == user.UserId, ct);
        if (row is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        // Idempotent: a second revoke keeps the first stamp.
        if (row.RevokedAt is null)
        {
            row.RevokedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    private static AccessKeyDto ToDto(ApiAccessKey k, DateTimeOffset now) => new(
        k.Id, k.Name, k.Purpose, k.Prefix, k.CreatedAt, k.ExpiresAt, k.LastUsedAt, k.RevokedAt, k.StateAt(now));
}
