// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Me;

/// <summary>
/// One category's setting. <paramref name="Locked"/> marks a category the user may not switch
/// off, so the client can render it disabled instead of guessing.
/// </summary>
public sealed record NotificationCategoryDto(NotificationCategory Category, bool Enabled, bool Locked);

public sealed record NotificationPreferencesDto(
    bool EmailEnabled,
    NotificationDigest Digest,
    bool DeliveryConfigured,
    IReadOnlyList<NotificationCategoryDto> Categories);

public sealed record NotificationCategoryWrite(NotificationCategory Category, bool Enabled);

public sealed record NotificationPreferencesWriteRequest(
    bool EmailEnabled,
    NotificationDigest Digest,
    IReadOnlyList<NotificationCategoryWrite> Categories);

public sealed class NotificationPreferencesWriteRequestValidator
    : AbstractValidator<NotificationPreferencesWriteRequest>
{
    public NotificationPreferencesWriteRequestValidator()
    {
        RuleFor(x => x.Digest).IsInEnum();
        RuleFor(x => x.Categories).NotNull();
        RuleForEach(x => x.Categories).ChildRules(c => c.RuleFor(x => x.Category).IsInEnum());
        RuleFor(x => x.Categories)
            .Must(c => c.Select(x => x.Category).Distinct().Count() == c.Count)
            .WithMessage("Each category may appear only once.")
            .When(x => x.Categories is not null);
    }
}

/// <summary>
/// What the caller wants to be notified about. The read always returns the complete category
/// set — stored rows merged over the defaults — so the client never has to reason about a
/// partially-saved state.
/// </summary>
public static class MeNotificationEndpoints
{
    public static RouteGroupBuilder MapMeNotificationEndpoints(this RouteGroupBuilder api)
    {
        var notifications = api.MapGroup("/me/notifications").WithTags("Me");

        notifications.MapGet("/", GetAsync)
            .WithSummary("The caller's notification settings, with every category present.");
        notifications.MapPut("/", UpdateAsync)
            .WithValidation<NotificationPreferencesWriteRequest>()
            .WithSummary("Saves the caller's notification settings.");

        return api;
    }

    private static async Task<Results<Ok<NotificationPreferencesDto>, UnauthorizedHttpResult>> GetAsync(
        IUserContextAccessor userAccessor,
        SilexGisDbContext db,
        IEmailDelivery delivery,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var account = await db.Users.AsNoTracking().FirstAsync(u => u.Id == user.UserId, ct);
        var stored = await StoredAsync(db, user.UserId, ct);

        return TypedResults.Ok(new NotificationPreferencesDto(
            account.NotifyEmailEnabled,
            account.NotifyDigest,
            await delivery.IsConfiguredAsync(ct),
            [.. NotificationCategories.All.Select(c => new NotificationCategoryDto(
                c,
                stored.TryGetValue(c, out var enabled) ? enabled : NotificationCategories.DefaultEnabled(c),
                !NotificationCategories.IsUserConfigurable(c)))]));
    }

    private static async Task<Results<Ok<NotificationPreferencesDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        NotificationPreferencesWriteRequest request,
        IUserContextAccessor userAccessor,
        SilexGisDbContext db,
        IEmailDelivery delivery,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var requested = request.Categories.ToDictionary(c => c.Category, c => c.Enabled);
        foreach (var (category, enabled) in requested)
        {
            if (!enabled && !NotificationCategories.IsUserConfigurable(category))
            {
                return ApiProblems.BadRequest(
                    "me.notification_locked", "Security alerts cannot be switched off.");
            }
        }

        var account = await db.Users.FirstAsync(u => u.Id == user.UserId, ct);
        account.NotifyEmailEnabled = request.EmailEnabled;
        account.NotifyDigest = request.Digest;

        var rows = await db.UserNotificationPreferences
            .Where(p => p.UserId == user.UserId)
            .ToDictionaryAsync(p => p.Category, ct);

        // Every category is written, so after one save the stored set is complete. Reconciled
        // through the change tracker rather than deleted and recreated: a bulk delete would
        // bypass the interceptors that maintain the timestamps.
        foreach (var category in NotificationCategories.All)
        {
            var enabled = !NotificationCategories.IsUserConfigurable(category)
                || (requested.TryGetValue(category, out var wanted)
                    ? wanted
                    : rows.TryGetValue(category, out var existing)
                        ? existing.Enabled
                        : NotificationCategories.DefaultEnabled(category));

            if (rows.TryGetValue(category, out var row))
            {
                row.Enabled = enabled;
            }
            else
            {
                db.UserNotificationPreferences.Add(new UserNotificationPreference
                {
                    UserId = user.UserId,
                    Category = category,
                    Enabled = enabled,
                });
            }
        }

        await db.SaveChangesAsync(ct);

        var saved = await StoredAsync(db, user.UserId, ct);
        return TypedResults.Ok(new NotificationPreferencesDto(
            account.NotifyEmailEnabled,
            account.NotifyDigest,
            await delivery.IsConfiguredAsync(ct),
            [.. NotificationCategories.All.Select(c => new NotificationCategoryDto(
                c,
                saved.TryGetValue(c, out var enabled) ? enabled : NotificationCategories.DefaultEnabled(c),
                !NotificationCategories.IsUserConfigurable(c)))]));
    }

    private static async Task<Dictionary<NotificationCategory, bool>> StoredAsync(
        SilexGisDbContext db, Guid userId, CancellationToken ct) =>
        await db.UserNotificationPreferences.AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToDictionaryAsync(p => p.Category, p => p.Enabled, ct);
}
