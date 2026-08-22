// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Notifications;

/// <summary>One thing that happened, as the person it happened to reads it.</summary>
/// <param name="Category">
/// What kind of thing this is. Always present, including on a row whose target is withheld — it
/// is what such a row degrades to, together with its date.
/// </param>
/// <param name="TemplateKey">
/// Which message this is, so a client that would rather write its own wording can.
/// </param>
/// <param name="Title">
/// What happened, in one line, in the language this page was rendered in. Null when there is
/// nothing that may be said: the target is withheld, or the message is one this installation no
/// longer has wording for.
/// </param>
/// <param name="Url">
/// Where to go to see it, as a path inside this application. Null when the notification is about
/// nothing openable, or about something this reader may no longer open.
/// </param>
/// <param name="TargetWithheld">
/// True when this notification is about something the reader may not currently see. The row is
/// still listed — that it happened is not the secret — but it carries neither the name nor the
/// link, and a client should render it as its category and its date rather than as broken.
/// </param>
public sealed record NotificationDto(
    long Id,
    NotificationCategory Category,
    string TemplateKey,
    string? Title,
    string? Url,
    bool TargetWithheld,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

/// <summary>How many notifications the caller has not opened yet.</summary>
public sealed record UnreadNotificationCountDto(int Unread);

/// <summary>
/// A person's own notifications: what happened, whether they have read it, and where to go to see
/// it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Being the recipient is the whole of the authorisation.</b> These rows are not shared content
/// with an owner and an audience — each one belongs to exactly one account — so the listing is not
/// composed with the visibility filter that guards content; it is filtered on the recipient's own
/// id, taken from the bearer token and never from the request. Naming somebody else's notification
/// therefore answers exactly as naming one that does not exist: there is no code path in which a
/// caller-supplied value chooses whose inbox is read.
/// </para>
/// <para>
/// The wording is rendered by the server. What a notification says has one home, an operator who
/// rewrites a message sees the rewrite here as well as in the mail, and a reader gets the language
/// they are reading the site in rather than the one their profile remembers. The row still carries
/// its category and its template key, so a client is free to render its own.
/// </para>
/// <para>
/// What a notification is about is re-decided against the reader's access every time it is read.
/// The name and the path a producer froze when it queued the row are not trusted: a reader may
/// have lost the right to open the thing since, and a row in that state is listed with its
/// category and its date and nothing else.
/// </para>
/// </remarks>
public static class NotificationInboxEndpoints
{
    public static RouteGroupBuilder MapNotificationInboxEndpoints(this RouteGroupBuilder api)
    {
        var notifications = api.MapGroup("/notifications").WithTags("Notifications");

        notifications.MapGet("/", ListAsync)
            .WithSummary("The caller's own notifications, newest first, filterable by category and by unread.")
            .WithDescription("category names one of the notification categories, spelled as the answers spell it.");
        notifications.MapGet("/unread-count", UnreadCountAsync)
            .WithSummary("How many of the caller's notifications are unread.");
        notifications.MapGet("/{id:long}", GetAsync)
            .WithSummary("One of the caller's own notifications; anybody else's answers as missing.");
        notifications.MapPost("/{id:long}/read", MarkReadAsync)
            .WithSummary("Marks one notification read. Reading it again does not move the stamp.");
        notifications.MapPost("/read-all", MarkAllReadAsync)
            .WithSummary("Marks every unread notification of the caller's read.");

        return api;
    }

    private static async Task<Results<Ok<PagedResult<NotificationDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        NotificationInboxRenderer renderer,
        HttpContext http,
        string? category,
        bool? unreadOnly,
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

        // Read as text and parsed here rather than bound straight to the enum, for two reasons.
        // A category is written one way everywhere else on the wire — the answers below spell it
        // in camel case, and so does every request body — and binding an enum straight from a
        // query string accepts only the exact declared spelling, so the name a client just read
        // out of a response would not be a name it could filter by. And an unrecognised one has
        // to be an ordinary refusal a caller can read: bound directly it is an unhandled failure
        // rather than a bad request.
        if (!TryParseCategory(category, out var wanted))
        {
            return ApiProblems.BadRequest("notification.category_unknown", $"No notification category named '{category}'.");
        }

        var query = db.Notifications.AsNoTracking().Where(n => n.RecipientUserId == user.UserId);
        if (wanted is { } only)
        {
            query = query.Where(n => n.Category == only);
        }

        if (unreadOnly is true)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(ct);

        // The identity column is the arrival order, so this is also the index's own order.
        var rows = await query.OrderByDescending(n => n.Id)
            .Skip((p - 1) * size).Take(size).ToListAsync(ct);

        var items = await PresentAsync(db, access, renderer, ctx, user.UserId, http, rows, ct);
        return TypedResults.Ok(new PagedResult<NotificationDto>(items, p, size, total));
    }

    private static async Task<Results<Ok<UnreadNotificationCountDto>, UnauthorizedHttpResult>> UnreadCountAsync(
        SilexGisDbContext db, IUserContextAccessor userAccessor, CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        // Counted rather than derived from a page, because this is asked far more often than the
        // list is opened and has an index of its own over exactly the rows it can count.
        var unread = await db.Notifications
            .CountAsync(n => n.RecipientUserId == user.UserId && n.ReadAt == null, ct);
        return TypedResults.Ok(new UnreadNotificationCountDto(unread));
    }

    private static async Task<Results<Ok<NotificationDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        long id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        NotificationInboxRenderer renderer,
        HttpContext http,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        // Whose it is, is part of the lookup rather than a check after it: somebody else's
        // notification must read exactly like one that was never written, so that an id cannot be
        // used to find out whether a particular person was told a particular thing.
        var row = await db.Notifications.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id && n.RecipientUserId == user.UserId, ct);
        if (row is null)
        {
            return ApiProblems.NotFound("notification.not_found");
        }

        var items = await PresentAsync(db, access, renderer, ctx, user.UserId, http, [row], ct);
        return TypedResults.Ok(items[0]);
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> MarkReadAsync(
        long id,
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

        var row = await db.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.RecipientUserId == user.UserId, ct);
        if (row is null)
        {
            return ApiProblems.NotFound("notification.not_found");
        }

        // When it was first opened, not most recently: opening something twice does not make it
        // newer news, and a client that marks on render would otherwise rewrite the stamp on every
        // scroll past.
        row.ReadAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult>> MarkAllReadAsync(
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

        // One statement rather than a page at a time: somebody who reads everything by mail can
        // have a great many unread rows, and this is the one click that answers that.
        var now = clock.GetUtcNow();
        await db.Notifications
            .Where(n => n.RecipientUserId == user.UserId && n.ReadAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.ReadAt, now), ct);
        return TypedResults.NoContent();
    }

    private static bool TryParseCategory(string? value, out NotificationCategory? category)
    {
        category = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!Enum.TryParse<NotificationCategory>(value, ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            return false;
        }

        category = parsed;
        return true;
    }

    /// <summary>
    /// Turns rows into what may be said about them: the target is re-decided against the reader's
    /// access first, and only what survives that is rendered.
    /// </summary>
    private static async Task<List<NotificationDto>> PresentAsync(
        SilexGisDbContext db,
        IAccessService access,
        NotificationInboxRenderer renderer,
        AccessContext ctx,
        Guid readerId,
        HttpContext http,
        IReadOnlyList<Notification> rows,
        CancellationToken ct)
    {
        var targets = new Dictionary<long, NotificationTarget>();
        foreach (var row in rows)
        {
            // A kind without an id names nothing that can be checked, so it is treated as
            // withheld: protection that cannot be applied fails closed.
            if (row is { TargetKind: { } kind, TargetId: { } id })
            {
                targets[row.Id] = new NotificationTarget(kind, id);
            }
        }

        var readable = await NotificationTargets.ReadableAsync(
            db, access, ctx, [.. targets.Values.Distinct()], ct);

        var withheld = rows
            .Where(row => row.TargetKind is not null
                && !(targets.TryGetValue(row.Id, out var target) && readable.Contains(target)))
            .Select(row => row.Id)
            .ToHashSet();

        var rendering = await renderer.RenderAsync(
            readerId, http.Request.Headers.AcceptLanguage.ToString(), rows, withheld, ct);

        return
        [
            .. rows.Select(row => new NotificationDto(
                row.Id,
                row.Category,
                row.TemplateKey,
                rendering.Titles.GetValueOrDefault(row.Id),
                !withheld.Contains(row.Id) && targets.TryGetValue(row.Id, out var target)
                    ? NotificationTargets.RouteTo(target)
                    : null,
                withheld.Contains(row.Id),
                row.CreatedAt,
                row.ReadAt)),
        ];
    }
}
