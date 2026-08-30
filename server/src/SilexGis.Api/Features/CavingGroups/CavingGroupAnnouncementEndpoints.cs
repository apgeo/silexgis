// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Auth;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.CavingGroups;

/// <summary>What somebody wants to tell a whole caving group.</summary>
/// <param name="Message">
/// The notice itself, in the sender's own words. One line: it arrives as the subject of the
/// message people receive and as the single line the application's own list shows, and there is
/// nowhere else to read it, so anything that does not fit in those two places would be written
/// and never seen.
/// </param>
public sealed record CavingGroupAnnouncementRequest(string Message);

/// <summary>What an announcement actually did.</summary>
/// <param name="Recipients">
/// How many people were told. Not the roster's size: a member without an account has nowhere to
/// receive anything, and the sender is never told about their own announcement.
/// </param>
/// <param name="Queued">
/// Whether the notices are still being written. A roster small enough to write to inside the
/// request is already done when this comes back; a large one is recorded and handed out by a
/// background pass moments later, and the person who sent it should be told which happened rather
/// than wondering why nobody has answered yet.
/// </param>
public sealed record CavingGroupAnnouncementResultDto(int Recipients, bool Queued);

/// <summary>Who an announcement written now would reach.</summary>
/// <param name="Recipients">
/// The same number the send itself would report, decided by the same rule over the same roster:
/// the members who hold an account, without the person asking. Shown before anything is written,
/// because somebody about to tell two hundred people something should see two hundred first.
/// </param>
public sealed record CavingGroupAnnouncementAudienceDto(int Recipients);

public sealed class CavingGroupAnnouncementRequestValidator : AbstractValidator<CavingGroupAnnouncementRequest>
{
    /// <summary>
    /// Long enough for a notice a club would actually send — a change of time, a meeting place, a
    /// closure — and short enough to survive whole in an email subject line and one row of a list.
    /// </summary>
    private const int MaxMessageLength = 200;

    public CavingGroupAnnouncementRequestValidator()
    {
        RuleFor(x => x.Message).NotEmpty().MaximumLength(MaxMessageLength);

        // A line break would be dropped by every mail client rendering the subject and would
        // break the row it is shown in, so it is refused at the form rather than silently
        // flattened: somebody who typed two paragraphs should be told they cannot.
        RuleFor(x => x.Message)
            .Must(message => message is null || !message.Any(c => c is '\r' or '\n'))
            .WithMessage("An announcement is one line.");
    }
}

/// <summary>
/// Writing to everybody on a caving group's roster.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the first message in the application somebody sends on purpose to many people.</b>
/// Everything else that reaches an inbox is a side effect of a change — a grant, a roster edit, a
/// remark — and its audience is whatever the change touched. Here a person chooses both the
/// wording and the size of the audience, which is why the right to do it is separate from the
/// right to edit the roster.
/// </para>
/// <para>
/// The right is <b>Execute on the caving-group domain, decided against this group</b>. Being able
/// to edit a club's roster is not obviously the same as being able to write to two hundred
/// people, and the two can now be granted apart: Execute is the domain's word for running an
/// operation against a row, which is exactly what this is. The roster's own role field stays what
/// it has always been — a label saying who runs the club, which no authorization decision reads.
/// </para>
/// <para>
/// It reaches people the way each of them chose. One notification per member is queued, and what
/// leaves the installation for any one of them is decided later against that member's own
/// settings — so this is a broadcast in intent and never a mailing list in effect.
/// </para>
/// </remarks>
public static class CavingGroupAnnouncementEndpoints
{
    public static RouteGroupBuilder MapCavingGroupAnnouncementEndpoints(this RouteGroupBuilder api)
    {
        // The per-address request budget the credential surfaces use, on both routes. Neither is
        // free to ask for: the count runs a roster join per call, and the send writes one row per
        // member and may cost the operator money on the way out. It is the outer of two bounds —
        // the durable cooldown inside the handler is the one that survives a second replica and a
        // changed address — and it is here because a bound that lives only in one process is a
        // bound only while there is one process.
        var announcements = api.MapGroup("/caving-groups")
            .WithTags("CavingGroups")
            .RequireRateLimiting("auth");

        announcements.MapGet("/{id:guid}/announcements/audience", AudienceAsync)
            .WithSummary("Says how many people an announcement to this caving group would reach.");

        announcements.MapPost("/{id:guid}/announcements", AnnounceAsync)
            .WithValidation<CavingGroupAnnouncementRequest>()
            .WithSummary("Tells everyone on a caving group's roster something, each of them the way they chose.");

        return api;
    }

    /// <summary>
    /// How many people an announcement may be written to inside the request that sends it.
    /// </summary>
    /// <remarks>
    /// <b>Budgeted from the size of a real club, because no roster exists to measure.</b> The
    /// sample data this application ships with contains no caving group at all and not one
    /// membership, so nothing in it could inform this number. What it is sized against instead is
    /// what a caving club actually is: a local club runs to a few dozen people and a national
    /// federation to several hundred, so fifty puts the ordinary club on the direct path and hands
    /// off only the ones where the cost is real. Fifty rows in one batched insert is a few
    /// milliseconds; five hundred inside somebody's HTTP call is a slow request holding a write
    /// transaction open while it runs, and it gets worse exactly as the club gets bigger.
    /// Overridable, because a number budgeted from an assumption should be cheap to correct.
    /// </remarks>
    private const int DefaultFanOutLimit = 50;

    /// <summary>
    /// The count shown before anything is sent, behind the same right as the send.
    /// </summary>
    /// <remarks>
    /// Gated on Execute rather than on Read, and deliberately: how many people can be reached
    /// through a club in one act is a fact about the club that only somebody entitled to do it
    /// has any business asking. It also keeps the page honest — a composer that could count but
    /// not send would have been built on a right that does not exist.
    /// </remarks>
    private static async Task<Results<Ok<CavingGroupAnnouncementAudienceDto>, UnauthorizedHttpResult, ProblemHttpResult>> AudienceAsync(
        Guid id,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var ctx = await accessAccessor.GetAsync(ct);
        if (user is null || ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!await db.CavingGroups.AnyAsync(t => t.Id == id, ct))
        {
            return ApiProblems.NotFound("caving_group.not_found");
        }

        if (!CavingGroupEndpoints.Holds(ctx, AccessAction.Execute, id))
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        var recipients = await db.AnnouncementRecipientsAsync(id, user.UserId, ct);
        return TypedResults.Ok(new CavingGroupAnnouncementAudienceDto(recipients.Count));
    }

    private static async Task<Results<Ok<CavingGroupAnnouncementResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> AnnounceAsync(
        Guid id,
        CavingGroupAnnouncementRequest request,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        IAccessContextAccessor accessAccessor,
        GroupAnnouncementThrottle throttle,
        IAppSettingsService settings,
        NotificationChannels channels,
        IConfiguration configuration,
        TimeProvider clock,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var ctx = await accessAccessor.GetAsync(ct);
        if (user is null || ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Read rather than checked for existence, because every message names the group.
        var cavingGroupName = await db.CavingGroups
            .Where(t => t.Id == id).Select(t => t.Name).FirstOrDefaultAsync(ct);
        if (cavingGroupName is null)
        {
            return ApiProblems.NotFound("caving_group.not_found");
        }

        if (!CavingGroupEndpoints.Holds(ctx, AccessAction.Execute, id))
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        // Asked before the roster is even read, so how often somebody may do this never depends on
        // how their club happens to look. It is a durable marker rather than the endpoint's request
        // budget: that budget is keyed on the caller's address, refills every minute, and a second
        // replica of this application keeps a second copy of it — while every call it lets through
        // writes one row per member and may cost the operator money on the way out.
        if (await throttle.TooSoonAsync(user.UserId))
        {
            return ApiProblems.BadRequest(
                "caving_group.announcement_too_soon",
                "Wait a few minutes before writing to a caving group again.");
        }

        var recipients = await db.AnnouncementRecipientsAsync(id, user.UserId, ct);
        if (recipients.Count == 0)
        {
            return TypedResults.Ok(new CavingGroupAnnouncementResultDto(0, false));
        }

        var announcements = await settings.GetAnnouncementsAsync(ct);
        var refusal = await PaidSpendingRefusalAsync(db, channels, announcements, clock, recipients.Count, ct);
        if (refusal is not null)
        {
            return refusal;
        }

        var message = request.Message.Trim();
        var labels = await ProfileDirectory.ResolveLabelsAsync(db, user, [user.UserId], ct);
        var senderName = labels.GetValueOrDefault(user.UserId) ?? string.Empty;

        // Stamped before the work rather than after. A save that reports a failure may still have
        // committed, and somebody retrying a "failure" in a loop is exactly what this bounds.
        await throttle.MarkSentAsync(user.UserId);

        var fanOutLimit = configuration.GetValue("Notifications:AnnouncementFanOutLimit", DefaultFanOutLimit);
        var queued = recipients.Count > fanOutLimit;
        if (queued)
        {
            var announcement = new CavingGroupAnnouncement
            {
                CavingGroupId = id,
                SenderUserId = user.UserId,
                Message = message,
                CavingGroupName = cavingGroupName,
                SenderName = senderName,
                RecipientCount = recipients.Count,
            };
            db.CavingGroupAnnouncements.Add(announcement);

            // The notice and the job in one save: a queued job whose notice never committed would
            // fail forever, and a notice nothing was ever asked to hand out would sit unread.
            // Nobody is named as having requested it — this queue tells its requester how every
            // job it runs turned out, and the person who sent the announcement was already told.
            db.ProcessingJobs.Add(new ProcessingJob
            {
                Kind = ProcessingJobKinds.CavingGroupAnnouncement,
                Payload = JsonSerializer.Serialize(
                    new CavingGroupAnnouncementPayload(announcement.Id), JsonSerializerOptions.Web),
                RequestedBy = null,
            });
        }
        else
        {
            var placeholders = new Dictionary<string, string>
            {
                ["actorName"] = senderName,
                ["cavingGroupName"] = cavingGroupName,
                ["announcement"] = message,
                // The inbox, because that is the one page an announcement can be read on. The
                // group's own page is where one is written, not where one arrives, and a text
                // message carries nothing but this link — so a link that landed anywhere else
                // would be the whole message failing to keep its promise.
                ["url"] = "/notifications",
            };

            foreach (var recipient in recipients)
            {
                NotificationQueue.Enqueue(
                    db,
                    recipient,
                    NotificationCategory.GroupAnnouncement,
                    MessageTemplateCatalog.NotifyGroupAnnouncement,
                    placeholders,
                    // The group is what this is about, and the group is what a reader can still open
                    // when everything else has moved on. Whether they may be shown its name is asked
                    // again against this reference at the moment they read, which is the same
                    // question, of the same row, that decided they were told in the first place.
                    NotificationTargetKind.CavingGroup,
                    id);
            }
        }

        // One save, so the whole announcement either happened or did not: a partial broadcast is
        // worse than none, because nobody can tell which half they are in.
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new CavingGroupAnnouncementResultDto(recipients.Count, queued));
    }

    /// <summary>
    /// Whether sending this would take the installation past what it has said it will spend in a
    /// day, and the refusal to return if so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ceiling is counted in messages that leave by a channel charging for each one, over rows
    /// already written today — pending ones included, because a message the installation has
    /// committed to sending is money committed whether or not the gateway has taken it yet.
    /// </para>
    /// <para>
    /// What this announcement would cost is counted at its worst: everybody it reaches, on every
    /// paid channel the category may use here. Each of those people may have switched that channel
    /// off, so fewer messages usually leave — but a bound on spending that assumed the usual case
    /// would let the unusual one through, and the unusual one is the expensive one.
    /// </para>
    /// <para>
    /// What has already been promised counts as well as what has already left, because an
    /// announcement becomes outbound copies moments after it is accepted rather than inside the
    /// request that accepts it. Reading only what has left would hand the same headroom to every
    /// announcement made before the first one was routed, and two announcements of sixty would
    /// pass a ceiling of a hundred between them.
    /// </para>
    /// <para>
    /// While the installation has agreed to no paid channel, the set is empty, this costs nothing
    /// and refuses nothing. That is the honest shape rather than a special case: the ceiling is
    /// real and simply never binds until there is something to spend.
    /// </para>
    /// </remarks>
    private static async Task<ProblemHttpResult?> PaidSpendingRefusalAsync(
        SilexGisDbContext db,
        NotificationChannels channels,
        AnnouncementSettings announcements,
        TimeProvider clock,
        int recipientCount,
        CancellationToken ct)
    {
        var paidChannels = PaidMessageBudget.ChannelsFor(channels, announcements);
        if (paidChannels.Count == 0)
        {
            return null;
        }

        var wouldSend = recipientCount * paidChannels.Count;
        var committedToday = await PaidMessageBudget.CommittedTodayAsync(db, paidChannels, clock, ct);

        return committedToday + wouldSend > announcements.EffectiveDailyPaidMessageCap
            ? ApiProblems.BadRequest(
                "caving_group.announcement_paid_cap_reached",
                "This installation has reached what it will spend on messages today.")
            : null;
    }
}
