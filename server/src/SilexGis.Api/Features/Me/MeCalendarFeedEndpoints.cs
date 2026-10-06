// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Calendar;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Me;

/// <summary>What the holder asks for when minting a feed address: a name for the device it is going on.</summary>
public sealed record CalendarFeedCreateRequest(string? Label);

public sealed class CalendarFeedCreateRequestValidator : AbstractValidator<CalendarFeedCreateRequest>
{
    public CalendarFeedCreateRequestValidator() => RuleFor(x => x.Label).MaximumLength(100);
}

/// <summary>
/// A feed address the account holds — metadata only. The address itself is in the mint answer
/// and nowhere else.
/// </summary>
public sealed record CalendarFeedDto(Guid Id, string? Label, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt);

/// <summary>
/// The mint answer: the one time the address is ever shown.
/// </summary>
/// <param name="Url">The whole subscription address, composed from where this installation says it lives.</param>
public sealed record CalendarFeedCreatedDto(Guid Id, string? Label, string Url, DateTimeOffset CreatedAt);

/// <summary>
/// The account's feed addresses together with whether the installation offers feeds at all,
/// because the settings page has to know the second to draw the first.
/// </summary>
public sealed record CalendarFeedListDto(bool Enabled, List<CalendarFeedDto> Feeds);

/// <summary>
/// An account's subscription addresses for its own calendar: minted, listed and withdrawn here,
/// read by the anonymous feed route.
/// </summary>
/// <remarks>
/// <para>
/// Under the account's own slice because the feed is about the account and about nothing else:
/// there is no object to hold a Share right on, and what the address opens is the holder's own
/// reading. Any signed-in account may mint one — subject to the installation's gate — because
/// the address can never say more than the account's own calendar page already shows it.
/// </para>
/// <para>
/// The gate is the installation's decision and is read here as well as on every poll: the mint
/// refuses while feeds are off, and the list says whether they are on, so the settings page draws
/// the section only where it can work.
/// </para>
/// </remarks>
public static class MeCalendarFeedEndpoints
{
    /// <summary>The installation has not switched feeds on, so no address can be minted.</summary>
    public const string DisabledCode = "calendar_feed.disabled";

    public const string NotFoundCode = "calendar_feed.not_found";

    public static RouteGroupBuilder MapMeCalendarFeedEndpoints(this RouteGroupBuilder api)
    {
        var feeds = api.MapGroup("/me/calendar-feeds").WithTags("Me");

        feeds.MapGet("/", ListAsync)
            .WithSummary("The caller's calendar feed addresses — metadata only, never the address — and whether the installation offers feeds.");
        feeds.MapPost("/", MintAsync).WithValidation<CalendarFeedCreateRequest>()
            .WithSummary("Mints a calendar feed address for the caller's own calendar; the address is returned once and never stored.");
        feeds.MapDelete("/{id:guid}", RevokeAsync)
            .WithSummary("Withdraws one of the caller's feed addresses. A calendar still polling it is answered as though it never existed.");

        return api;
    }

    private static async Task<Results<Ok<CalendarFeedListDto>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        IAppSettingsService settings,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var enabled = (await settings.GetProtectionAsync(ct)).CalendarFeedEnabled;
        var feeds = await db.CalendarFeedTokens.AsNoTracking()
            .Where(x => x.UserId == user.UserId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new CalendarFeedDto(x.Id, x.Label, x.CreatedAt, x.RevokedAt))
            .ToListAsync(ct);
        return TypedResults.Ok(new CalendarFeedListDto(enabled, feeds));
    }

    private static async Task<Results<Created<CalendarFeedCreatedDto>, UnauthorizedHttpResult, ProblemHttpResult>> MintAsync(
        CalendarFeedCreateRequest request,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        IAppSettingsService settings,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!(await settings.GetProtectionAsync(ct)).CalendarFeedEnabled)
        {
            return ApiProblems.Conflict(
                DisabledCode,
                "This installation does not offer calendar feeds. An administrator can switch them on.");
        }

        var (token, hash) = CalendarFeedTokens.Mint();
        var row = new CalendarFeedToken
        {
            UserId = user.UserId,
            TokenHash = hash,
            Label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim(),
        };
        db.CalendarFeedTokens.Add(row);
        await db.SaveChangesAsync(ct);

        // Composed from where the installation says it lives, because a calendar application is
        // handed a whole address and the notifier's relative paths would be no use to it.
        var url = $"{NotificationLinks.SiteUrl(configuration)}/api/v1/calendar/feed/{token}.ics";
        return TypedResults.Created(
            $"/api/v1/me/calendar-feeds/{row.Id}",
            new CalendarFeedCreatedDto(row.Id, row.Label, url, row.CreatedAt));
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

        // Scoped to the caller's own rows in the predicate, so another account's address is "not
        // found" here exactly as an invented one is.
        var row = await db.CalendarFeedTokens
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == user.UserId, ct);
        if (row is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        // Idempotent: a second revoke keeps the original stamp.
        if (row.RevokedAt is null)
        {
            row.RevokedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }
}
