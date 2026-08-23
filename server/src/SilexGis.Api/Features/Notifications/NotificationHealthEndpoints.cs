// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Notifications;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Notifications;

/// <summary>How many deliveries sit in one status on one channel.</summary>
public sealed record NotificationDeliveryCountDto(
    NotificationChannel Channel,
    NotificationDeliveryStatus Status,
    int Count);

/// <summary>
/// Whether messages are getting out of this installation.
/// </summary>
/// <param name="Counts">
/// One entry per channel and status that has at least one row. A pair with nothing in it is
/// absent rather than zero — an operator reads the shape of the backlog, not a fixed grid.
/// </param>
/// <param name="OldestPendingCreatedAt">
/// When the longest-waiting unsent delivery was written, or null when nothing is waiting.
/// </param>
/// <param name="OldestPendingAgeSeconds">
/// How long that one has been waiting, measured on the server. This is the number that says the
/// mail server has stopped answering: a pending count alone is healthy at any size as long as it
/// keeps moving, and a client's own clock cannot be trusted to work the age out.
/// </param>
public sealed record NotificationHealthDto(
    IReadOnlyList<NotificationDeliveryCountDto> Counts,
    DateTimeOffset? OldestPendingCreatedAt,
    long? OldestPendingAgeSeconds);

/// <summary>
/// One delivery, as the operator diagnosing it needs to see it.
/// </summary>
/// <param name="RecipientLabel">
/// Who it was for, under the name they may be shown under — never their address. Null when the
/// account behind the id no longer exists.
/// </param>
/// <param name="Error">
/// What went wrong, shortened, with anything address-shaped removed.
/// </param>
public sealed record NotificationDeliveryDto(
    long Id,
    long NotificationId,
    Guid RecipientUserId,
    string? RecipientLabel,
    NotificationCategory Category,
    string TemplateKey,
    NotificationChannel Channel,
    NotificationDeliveryStatus Status,
    int Attempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset NotBefore,
    DateTimeOffset? SentAt,
    string? Error);

/// <summary>
/// What became of one hand-driven retry.
/// </summary>
/// <param name="DueAt">
/// When the next attempt is due, once there is going to be one. Null for every outcome that did
/// not put the delivery back — an operator needs to be told that as plainly as they are told the
/// good outcome, so the answer is never a bare success.
/// </param>
public sealed record NotificationRetryDto(NotificationRetryOutcome Outcome, DateTimeOffset? DueAt);

/// <summary>
/// The operator's view of what is leaving the installation and what is stuck.
/// </summary>
/// <remarks>
/// <para>
/// Governed by the Settings domain, whose sibling page is the mail server this view reports on:
/// the operator who configured it is the operator who wants to know whether mail is going out.
/// Reading needs Read over that domain, held globally — these are everybody's deliveries, so the
/// right to look at them cannot be derived from any one row.
/// </para>
/// <para>
/// <b>It never shows a recipient's address.</b> The rule that a producer writes an id and never an
/// address holds on the read side too: the label comes from the shared resolver, which refuses to
/// fall back to the address, and the failure text has addresses stripped out of it because a mail
/// server quotes the address it is refusing. This page lists other people's messages by
/// construction, which makes it the likeliest place in the product to publish one.
/// </para>
/// <para>
/// Every row here is one somebody was meant to receive. A channel a recipient has switched off for
/// a category produces no delivery row at all — the choice is made when the notification is routed,
/// before this table is written — so nothing listed here is a message that was deliberately not
/// sent, and a backlog is always a backlog.
/// </para>
/// </remarks>
public static class NotificationHealthEndpoints
{
    /// <summary>
    /// How old an unsent delivery must be before it is worth an operator's attention when they
    /// have not said otherwise. A day, because two ordinary things already hold a delivery back
    /// for hours and neither is a fault: the daily summary waits for its window, and a message
    /// due during the recipient's quiet hours waits for morning.
    /// </summary>
    private const int DefaultOverdueHours = 24;

    /// <summary>A year — past this the filter says nothing the retention window has not already said.</summary>
    private const int MaxOverdueHours = 24 * 365;

    public static RouteGroupBuilder MapNotificationHealthEndpoints(this RouteGroupBuilder api)
    {
        var health = api.MapGroup("/admin/notifications").WithTags("Admin");

        health.MapGet("/health", HealthAsync)
            .WithSummary("Delivery counts by channel and status, and how long the oldest unsent delivery has been waiting.");
        health.MapPost("/deliveries/{id:long}/retry", RetryAsync)
            .WithSummary("Puts one dead delivery back in the queue.")
            .WithDescription(
                "Needs Execute over the settings domain, not Read: this one sends somebody else's message. "
                + "The recipient's current preferences are read again first, so a category they have since "
                + "switched off answers 'suppressed' and drops the delivery rather than sending it. "
                + "Refused for a delivery that is not dead, and for one that died because nothing knows how "
                + "to write its message.");
        health.MapGet("/deliveries", ListAsync)
            .WithSummary("Deliveries that need attention: dead ones, and unsent ones older than the overdue window.")
            .WithDescription(
                "status narrows to one delivery status, spelled as the answers spell it. "
                + "attentionOnly (default true) keeps only dead deliveries and unsent ones older than overdueHours. "
                + "overdueHours defaults to 24, which is longer than both the daily summary window and a night of quiet hours.");

        return api;
    }

    private static async Task<Results<Ok<NotificationHealthDto>, UnauthorizedHttpResult, ProblemHttpResult>> HealthAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        TimeProvider clock,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        var counts = await db.NotificationDeliveries.AsNoTracking()
            .GroupBy(d => new { d.Channel, d.Status })
            .Select(g => new { g.Key.Channel, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        // The oldest by when it was written, not by when it is next due: every claim pushes
        // not_before five minutes into the future and every backoff pushes it hours further, so a
        // queue that is failing over and over would report itself as freshly scheduled.
        var oldestPending = await db.NotificationDeliveries.AsNoTracking()
            .Where(d => d.Status == NotificationDeliveryStatus.Pending)
            .OrderBy(d => d.CreatedAt)
            .Select(d => (DateTimeOffset?)d.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var age = oldestPending is { } oldest
            ? (long)Math.Max(0, (clock.GetUtcNow() - oldest).TotalSeconds)
            : (long?)null;

        return TypedResults.Ok(new NotificationHealthDto(
            [
                .. counts
                    .OrderBy(c => c.Channel).ThenBy(c => c.Status)
                    .Select(c => new NotificationDeliveryCountDto(c.Channel, c.Status, c.Count)),
            ],
            oldestPending,
            age));
    }

    private static async Task<Results<Ok<PagedResult<NotificationDeliveryDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        TimeProvider clock,
        string? status,
        bool? attentionOnly,
        int? overdueHours,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        // Parsed here rather than bound straight to the enum, for the same reason the inbox does
        // it: a status is spelled in camel case everywhere on the wire, and an unrecognised one
        // has to be a refusal a caller can read rather than an unhandled binding failure.
        if (!TryParseStatus(status, out var wanted))
        {
            return ApiProblems.BadRequest(
                "notification.delivery_status_unknown", $"No delivery status named '{status}'.");
        }

        var hours = overdueHours ?? DefaultOverdueHours;
        if (hours < 0 || hours > MaxOverdueHours)
        {
            return ApiProblems.BadRequest(
                "notification.overdue_window_invalid",
                $"The overdue window must be between 0 and {MaxOverdueHours} hours.");
        }

        var rows = from delivery in db.NotificationDeliveries.AsNoTracking()
                   join notification in db.Notifications.AsNoTracking()
                       on delivery.NotificationId equals notification.Id
                   select new { Delivery = delivery, notification.Category, notification.TemplateKey };

        if (wanted is { } only)
        {
            rows = rows.Where(r => r.Delivery.Status == only);
        }

        if (attentionOnly ?? true)
        {
            // Dead is always worth looking at; anything else is only worth looking at once it has
            // been waiting longer than the slowest thing that holds a healthy delivery back. Sent
            // rows are excluded whatever their age — a message that left is not a backlog.
            var cutoff = clock.GetUtcNow().AddHours(-hours);
            rows = rows.Where(r => r.Delivery.Status == NotificationDeliveryStatus.Dead
                || (r.Delivery.Status != NotificationDeliveryStatus.Sent && r.Delivery.CreatedAt < cutoff));
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await rows.CountAsync(ct);

        // The identity column is the arrival order, so the newest failure is at the top and this
        // is also the index's own order.
        var listed = await rows.OrderByDescending(r => r.Delivery.Id)
            .Skip((p - 1) * size).Take(size).ToListAsync(ct);

        // Names are resolved after the page materialises rather than joined in: what a user may be
        // shown as is a rule with one home, and this slice may not read a user row itself.
        var labels = await ProfileDirectory.ResolveLabelsAsync(
            db, user, listed.Select(r => r.Delivery.RecipientUserId), ct);

        var items = listed.Select(r => new NotificationDeliveryDto(
            r.Delivery.Id,
            r.Delivery.NotificationId,
            r.Delivery.RecipientUserId,
            labels.GetValueOrDefault(r.Delivery.RecipientUserId),
            r.Category,
            r.TemplateKey,
            r.Delivery.Channel,
            r.Delivery.Status,
            r.Delivery.Attempts,
            r.Delivery.CreatedAt,
            r.Delivery.NotBefore,
            r.Delivery.SentAt,
            DeliveryErrorText.ForOperator(r.Delivery.Error))).ToList();

        return TypedResults.Ok(new PagedResult<NotificationDeliveryDto>(items, p, size, total));
    }

    private static async Task<Results<Ok<NotificationRetryDto>, UnauthorizedHttpResult, ProblemHttpResult>> RetryAsync(
        long id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        ICurrentUser currentUser,
        NotificationDeliveryService deliveries,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Execute, not Read. Looking at a backlog and putting a message back into it are different
        // acts: the second one sends somebody else's message, and an installation may well want an
        // operator who can diagnose without being able to do that.
        if (!AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Execute, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        var result = await deliveries.RetryAsync(id, ct);

        switch (result.Outcome)
        {
            case NotificationRetryOutcome.NotFound:
                return ApiProblems.NotFound("notification.delivery_not_found");
            case NotificationRetryOutcome.NotDead:
                return ApiProblems.BadRequest(
                    "notification.delivery_not_dead",
                    "Only a delivery that has given up can be put back.");
            case NotificationRetryOutcome.TemplateUnknown:
                return ApiProblems.BadRequest(
                    "notification.delivery_template_unknown",
                    $"Nothing here can carry '{result.TemplateKey}', so retrying it would fail the same way.");
            default:
                break;
        }

        if (result.Changed)
        {
            // Written by hand because nothing else would write it: a delivery is not one of the
            // rows whose edits are recorded automatically, and if it were, every one of the
            // sender's own status changes would be recorded as somebody's edit. What is worth
            // recording is exactly this — a person deciding that a message meant for somebody else
            // should be attempted again — so it names them, the delivery, and what became of it.
            db.Set<AuditEntry>().Add(new AuditEntry
            {
                UserId = currentUser.UserId,
                Action = AuditActions.NotificationRetried,
                EntityType = nameof(NotificationDelivery),
                EntityId = id.ToString(CultureInfo.InvariantCulture),
                RootEntityType = nameof(Notification),
                RootEntityId = result.NotificationId?.ToString(CultureInfo.InvariantCulture),
                Changes = JsonSerializer.Serialize(
                    new Dictionary<string, Dictionary<string, object?>>
                    {
                        ["Status"] = new()
                        {
                            ["old"] = Wire(NotificationDeliveryStatus.Dead),
                            ["new"] = Wire(result.Outcome),
                        },
                        ["TemplateKey"] = new() { ["new"] = result.TemplateKey },
                    }),
            });
        }

        // One save for both: the record of who asked for this and the change it made commit
        // together or not at all.
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new NotificationRetryDto(result.Outcome, result.DueAt));
    }

    /// <summary>The name a value is written under everywhere else it crosses the wire.</summary>
    private static string Wire<T>(T value)
        where T : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString() ?? string.Empty);

    private static bool TryParseStatus(string? value, out NotificationDeliveryStatus? status)
    {
        status = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!Enum.TryParse<NotificationDeliveryStatus>(value, ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            return false;
        }

        status = parsed;
        return true;
    }
}
