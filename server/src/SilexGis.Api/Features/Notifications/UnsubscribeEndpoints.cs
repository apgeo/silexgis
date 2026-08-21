// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Notifications;

public sealed record UnsubscribeRequest(string Token);

/// <summary>What was switched off, so the page can say so in the user's own words.</summary>
public sealed record UnsubscribeResultDto(NotificationCategory Category);

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
        SilexGisDbContext db,
        CancellationToken ct)
    {
        if (!tokens.TryRead(request.Token, out var userId, out var category))
        {
            return ApiProblems.BadRequest("notification.unsubscribe_invalid", "That link is no longer valid.");
        }

        // The categories nobody may switch off have no opt-out anywhere else either, and a token
        // for one could only come from a tampered link — the sender never puts one in those
        // messages.
        if (!NotificationCategories.IsUserConfigurable(category))
        {
            return ApiProblems.BadRequest(
                "notification.unsubscribe_locked", "That kind of notification cannot be switched off.");
        }

        var row = await db.UserNotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Category == category, ct);

        if (row is null)
        {
            db.UserNotificationPreferences.Add(new UserNotificationPreference
            {
                UserId = userId,
                Category = category,
                Enabled = false,
            });
        }
        else
        {
            row.Enabled = false;
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new UnsubscribeResultDto(category));
    }
}
