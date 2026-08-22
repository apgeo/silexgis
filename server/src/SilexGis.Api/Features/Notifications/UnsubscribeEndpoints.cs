// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Notifications;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Notifications;

public sealed record UnsubscribeRequest(string Token);

/// <summary>What was switched off, so the page can say so in the user's own words.</summary>
/// <param name="Kind">Whether one category was switched off, or the daily summary itself.</param>
/// <param name="Category">Null when the daily summary was switched off — it names no category.</param>
public sealed record UnsubscribeResultDto(UnsubscribeKind Kind, NotificationCategory? Category);

public sealed class UnsubscribeRequestValidator : AbstractValidator<UnsubscribeRequest>
{
    public UnsubscribeRequestValidator() => RuleFor(x => x.Token).NotEmpty().MaximumLength(2000);
}

/// <summary>
/// The one-click opt-out link every notification carries.
/// </summary>
/// <remarks>
/// <para>
/// Anonymous by necessity: the link is opened from a mail client, on whatever device happens to
/// be to hand, by someone who may well be opting out precisely because they do not want to sign
/// in. The signed token is the authority, and it authorises exactly one thing.
/// </para>
/// <para>
/// A bad token answers the same way a good one does. Anything else would turn the endpoint into a
/// way to test whether a token — and so an account — is real, which is the class of probe the
/// profile visibility rules exist to close.
/// </para>
/// </remarks>
public static class UnsubscribeEndpoints
{
    public static RouteGroupBuilder MapUnsubscribeEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/notifications/unsubscribe", UnsubscribeAsync)
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithValidation<UnsubscribeRequest>()
            .WithTags("Notifications")
            .WithSummary("Switches off one notification category using the token from a message.");

        return api;
    }

    private static async Task<Results<Ok<UnsubscribeResultDto>, ProblemHttpResult>> UnsubscribeAsync(
        UnsubscribeRequest request,
        IUnsubscribeTokens tokens,
        NotificationOptOut optOut,
        SilexGisDbContext db,
        CancellationToken ct)
    {
        if (!tokens.TryRead(request.Token, out var subject))
        {
            return ApiProblems.BadRequest("notification.unsubscribe_invalid", "That link is no longer valid.");
        }

        if (subject.Kind == UnsubscribeKind.DailyDigest)
        {
            return await StopTheDailySummaryAsync(subject.UserId, optOut, ct);
        }

        var category = subject.Category;

        // Security alerts have no opt-out anywhere else either, and a token for one could only
        // come from a tampered link — the sender never puts one in those messages.
        if (!NotificationCategories.IsUserConfigurable(category))
        {
            return ApiProblems.BadRequest(
                "notification.unsubscribe_locked", "Security alerts cannot be switched off.");
        }

        var row = await db.UserNotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == subject.UserId && p.Category == category, ct);

        if (row is null)
        {
            db.UserNotificationPreferences.Add(new UserNotificationPreference
            {
                UserId = subject.UserId,
                Category = category,
                Enabled = false,
            });
        }
        else
        {
            row.Enabled = false;
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new UnsubscribeResultDto(UnsubscribeKind.Category, category));
    }

    /// <summary>
    /// Switches the account's notification email off.
    /// </summary>
    /// <remarks>
    /// The daily summary is not a category and cannot be switched off as one. Merely returning the
    /// account to one message per event would send it more mail than the link was clicked to stop,
    /// so the only honest reading of "stop sending me this summary" is to stop the mail. Alerts
    /// about the account's own credentials still go out: they ignore this switch by design, because
    /// whoever is taking an account over may be holding a live session while they do it.
    /// </remarks>
    private static async Task<Results<Ok<UnsubscribeResultDto>, ProblemHttpResult>> StopTheDailySummaryAsync(
        Guid userId, NotificationOptOut optOut, CancellationToken ct)
    {
        // An account that is gone answers exactly as one that was changed, for the same reason a
        // bad token does: anything else turns the endpoint into a way to test whether an account
        // is real.
        _ = await optOut.StopNotificationEmailAsync(userId, ct);

        return TypedResults.Ok(new UnsubscribeResultDto(UnsubscribeKind.DailyDigest, null));
    }
}
