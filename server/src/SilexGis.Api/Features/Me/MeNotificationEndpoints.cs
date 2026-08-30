// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Notifications;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Me;

/// <summary>
/// One cell of the matrix: what the caller has chosen for one category on one channel, and the
/// three facts a client needs to render it without guessing at a rule.
/// </summary>
/// <param name="Choice">Nothing, as it happens, or gathered into a daily summary.</param>
/// <param name="Locked">
/// A cell the caller may not switch off. Asked of the same rule the write path enforces rather
/// than read off the category, because the answer is per cell: a category with a safety argument
/// behind it is held on wherever it costs nothing, but on a channel billed per message it is the
/// account's own choice, so that cell is offered rather than locked.
/// </param>
/// <param name="CanDefer">
/// Whether a daily summary is offerable here at all. False for a channel that cannot hold anything
/// back, and for a category that refuses to be held back.
/// </param>
/// <param name="Available">
/// Whether this installation has the channel configured. A cell that is not available is still
/// saved and still read back — the choice simply has nothing to act on until somebody configures
/// the transport, which is worth saying plainly rather than silently doing nothing.
/// </param>
public sealed record NotificationChannelDto(
    NotificationChannelKind Channel,
    NotificationChannelChoice Choice,
    bool Locked,
    bool CanDefer,
    bool Available);

/// <summary>
/// One category and every channel it may ever use. <paramref name="ReachesNobody"/> is the state
/// switching every channel off reaches: legitimate, and never one to arrive at without being told.
/// </summary>
public sealed record NotificationCategoryDto(
    NotificationCategory Category,
    bool ReachesNobody,
    IReadOnlyList<NotificationChannelDto> Channels);

/// <summary>
/// The whole matrix, plus what this installation can actually send on.
/// </summary>
/// <param name="ConfiguredChannels">
/// The channels this installation has configured, each named once. The inbox is always among them;
/// a transport is there only when its settings are usable.
/// </param>
public sealed record NotificationPreferencesDto(
    IReadOnlyList<NotificationChannelKind> ConfiguredChannels,
    IReadOnlyList<NotificationCategoryDto> Categories);

public sealed record NotificationChannelWrite(
    NotificationChannelKind Channel, NotificationChannelChoice Choice);

public sealed record NotificationCategoryWrite(
    NotificationCategory Category, IReadOnlyList<NotificationChannelWrite> Channels);

public sealed record NotificationPreferencesWriteRequest(
    IReadOnlyList<NotificationCategoryWrite> Categories);

public sealed class NotificationPreferencesWriteRequestValidator
    : AbstractValidator<NotificationPreferencesWriteRequest>
{
    public NotificationPreferencesWriteRequestValidator()
    {
        RuleFor(x => x.Categories).NotNull();
        RuleFor(x => x.Categories)
            .Must(c => c.Select(x => x.Category).Distinct().Count() == c.Count)
            .WithMessage("Each category may appear only once.")
            .When(x => x.Categories is not null);

        RuleForEach(x => x.Categories).ChildRules(category =>
        {
            category.RuleFor(x => x.Category).IsInEnum();
            category.RuleFor(x => x.Channels).NotNull();
            category.RuleFor(x => x.Channels)
                .Must(c => c.Select(x => x.Channel).Distinct().Count() == c.Count)
                .WithMessage("Each channel may appear only once in a category.")
                .When(x => x.Channels is not null);

            category.RuleForEach(x => x.Channels).ChildRules(channel =>
            {
                // Not IsInEnum: the channel type is bit flags, and IsInEnum on a flags enum
                // accepts any combination of bits. Exactly one channel is the rule here.
                channel.RuleFor(x => x.Channel)
                    .Must(NotificationChannelKinds.IsSingle)
                    .WithMessage("A choice names exactly one channel.");
                channel.RuleFor(x => x.Choice).IsInEnum();
            });
        });
    }
}

/// <summary>
/// What the caller wants to be notified about, and where.
/// </summary>
/// <remarks>
/// The read always returns the complete matrix — every category, every channel that category may
/// ever use, stored rows merged over the documented defaults — so a client never has to reason
/// about a partially-saved state, and an account that has never opened this page reads back as the
/// defaults rather than as silence.
/// </remarks>
public static class MeNotificationEndpoints
{
    public static RouteGroupBuilder MapMeNotificationEndpoints(this RouteGroupBuilder api)
    {
        var notifications = api.MapGroup("/me/notifications").WithTags("Me");

        notifications.MapGet("/", GetAsync)
            .WithSummary(
                "The caller's notification settings as a category by channel matrix, with every " +
                "cell present, and the channels this installation has configured.");
        notifications.MapPut("/", UpdateAsync)
            .WithValidation<NotificationPreferencesWriteRequest>()
            .WithSummary("Saves the caller's notification settings.");

        return api;
    }

    private static async Task<Results<Ok<NotificationPreferencesDto>, UnauthorizedHttpResult>> GetAsync(
        IUserContextAccessor userAccessor,
        SilexGisDbContext db,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        IAppSettingsService settings,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var stored = await StoredAsync(db, user.UserId, ct);
        return TypedResults.Ok(await ReadAsync(stored, emailDelivery, smsDelivery, settings, ct));
    }

    private static async Task<Results<Ok<NotificationPreferencesDto>, ProblemHttpResult, UnauthorizedHttpResult>>
        UpdateAsync(
            NotificationPreferencesWriteRequest request,
            IUserContextAccessor userAccessor,
            SilexGisDbContext db,
            IEmailDelivery emailDelivery,
            ISmsDelivery smsDelivery,
            IAppSettingsService settings,
            CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        // What the installation has agreed to pay for. Asked here as well as where a message is
        // routed, so a channel this installation will not pay for cannot be stored either — a cell
        // nobody reads is how a preference silently stops meaning anything.
        var paidChannelsAllowed = (await settings.GetAnnouncementsAsync(ct)).PaidChannelsAllowed;

        var requested = new Dictionary<(NotificationCategory, NotificationChannelKind), NotificationChannelChoice>();
        foreach (var category in request.Categories)
        {
            foreach (var channel in category.Channels)
            {
                // Refused rather than quietly rewritten: somebody who asked for something the
                // rules forbid is told so, instead of saving a page that reads back differently
                // from what they left it at.
                if (!NotificationMatrix.CanChoose(
                        category.Category,
                        channel.Channel,
                        channel.Choice,
                        NotificationChannelKinds.Everything,
                        paidChannelsAllowed))
                {
                    return ApiProblems.BadRequest(
                        "me.notification_choice_refused",
                        "That kind of notification cannot be set that way on that channel.");
                }

                requested[(category.Category, channel.Channel)] = channel.Choice;
            }
        }

        var existing = await db.UserNotificationPreferences
            .Where(p => p.UserId == user.UserId)
            .ToListAsync(ct);
        var byCell = existing.ToDictionary(p => (p.Category, p.Channel));

        // Every cell the vocabulary allows is written, so after one save the stored matrix is
        // complete. Reconciled through the change tracker rather than deleted and recreated: a
        // bulk delete would bypass the interceptors that maintain the timestamps. Cells outside a
        // category's ceiling are left exactly as they are — a row a narrowed ceiling stranded is
        // worth nothing when it is read, which is cheaper than hunting it down here.
        //
        // Narrowed by the same rule the validation above and the read-back below use, and with the
        // same answer about what this installation will pay for. Written any wider, a choice just
        // accepted on a charging channel would be resolved back to "off" and stored that way, so
        // the page would read back differently from what its owner left it at — and every account
        // that ever saved would carry an inert row for a channel it may not use.
        foreach (var category in NotificationCategories.All)
        {
            var writable = NotificationMatrix.Usable(
                category, NotificationChannelKinds.Everything, paidChannelsAllowed);
            foreach (var channel in NotificationChannelKinds.Split(writable))
            {
                var cell = (category, channel);
                var stored = byCell.TryGetValue(cell, out var row) ? row.Choice : (NotificationChannelChoice?)null;
                var choice = NotificationMatrix.Resolve(
                    category,
                    channel,
                    requested.TryGetValue(cell, out var wanted) ? wanted : stored,
                    NotificationChannelKinds.Everything,
                    paidChannelsAllowed);

                if (row is null)
                {
                    db.UserNotificationPreferences.Add(new UserNotificationPreference
                    {
                        UserId = user.UserId,
                        Category = category,
                        Channel = channel,
                        Choice = choice,
                    });
                }
                else
                {
                    row.Choice = choice;
                }
            }
        }

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(await ReadAsync(
            await StoredAsync(db, user.UserId, ct), emailDelivery, smsDelivery, settings, ct));
    }

    /// <summary>
    /// The matrix as the client reads it.
    /// </summary>
    /// <remarks>
    /// A cell is resolved against every channel the vocabulary has, not against the channels this
    /// installation happens to have configured, and what is configured is reported separately.
    /// Otherwise an installation whose mail server is momentarily unset would show every mail
    /// choice as switched off, which is a different statement from "nothing is sent here yet" and
    /// the one that loses somebody's settings the moment they press save.
    /// </remarks>
    private static async Task<NotificationPreferencesDto> ReadAsync(
        IReadOnlyDictionary<(NotificationCategory, NotificationChannelKind), NotificationChannelChoice> stored,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        IAppSettingsService settings,
        CancellationToken ct)
    {
        // The inbox is always here: it has no transport that could be missing. Each of the others
        // is asked whether this installation has actually been given what it needs — including the
        // one that costs money, which nothing on this surface used to ask.
        var configured = NotificationChannelKind.InApp;
        if (await emailDelivery.IsConfiguredAsync(ct))
        {
            configured |= NotificationChannelKind.Email;
        }

        if (await smsDelivery.IsConfiguredAsync(ct))
        {
            configured |= NotificationChannelKind.Sms;
        }

        // A channel that costs money is listed only once this installation has said it will pay
        // for it. Listing it and marking it unavailable would be a switch somebody could set and
        // nothing would ever read.
        var paidChannelsAllowed = (await settings.GetAnnouncementsAsync(ct)).PaidChannelsAllowed;

        var categories = new List<NotificationCategoryDto>();
        foreach (var category in NotificationCategories.All)
        {
            var cells = NotificationChannelKinds
                .Split(NotificationMatrix.Usable(
                    category, NotificationChannelKinds.Everything, paidChannelsAllowed))
                .Select(channel => new NotificationChannelDto(
                    channel,
                    Cell(category, channel),
                    Locked: !NotificationMatrix.CanChoose(
                        category,
                        channel,
                        NotificationChannelChoice.Off,
                        NotificationChannelKinds.Everything,
                        paidChannelsAllowed),
                    CanDefer: NotificationChannelKinds.CanDefer(channel) &&
                        !NotificationCategories.IsAlwaysImmediate(category),
                    Available: (configured & channel) == channel))
                .ToList();

            categories.Add(new NotificationCategoryDto(
                category,
                NotificationMatrix.ReachesNobody(
                    category,
                    channel => Cell(category, channel),
                    NotificationChannelKinds.Everything,
                    paidChannelsAllowed),
                cells));
        }

        return new NotificationPreferencesDto([.. NotificationChannelKinds.Split(configured)], categories);

        NotificationChannelChoice Cell(NotificationCategory category, NotificationChannelKind channel) =>
            NotificationMatrix.Resolve(
                category,
                channel,
                stored.TryGetValue((category, channel), out var choice) ? choice : null,
                NotificationChannelKinds.Everything,
                paidChannelsAllowed);
    }

    private static async Task<Dictionary<(NotificationCategory, NotificationChannelKind), NotificationChannelChoice>>
        StoredAsync(SilexGisDbContext db, Guid userId, CancellationToken ct) =>
        await db.UserNotificationPreferences
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToDictionaryAsync(p => (p.Category, p.Channel), p => p.Choice, ct);
}
