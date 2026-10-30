// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Expeditions;

/// <summary>
/// Tells the people on an expedition's roster that it changed or that it is not happening.
/// </summary>
/// <remarks>
/// <para>
/// The same rule the trip notices follow, applied to a camp: a draft tells nobody, because it has
/// been shown to nobody; a camp that already happened or was already called off is housekeeping;
/// the audience is the accounts the roster names, narrowed to those who may read the camp, and the
/// person who made the change is never told about their own act.
/// </para>
/// <para>
/// Neither message says what changed or where the camp works — the fields of a camp include the
/// area it is drawn over, and a message is as much an outbound copy of that as anything the API
/// returns.
/// </para>
/// <para>
/// Every send queues before the caller's own <c>SaveChangesAsync</c>, so a message exists only if
/// the change it describes actually committed.
/// </para>
/// </remarks>
internal static class ExpeditionNotifier
{
    /// <summary>Tells the roster that the camp was changed.</summary>
    public static Task ChangedAsync(
        SilexGisDbContext db,
        IAccessService access,
        UserContext user,
        Expedition expedition,
        ActivityState stateBefore,
        CancellationToken ct) =>
        // Both ends of the move: a camp going back into the workshop or off to be written up
        // stays silent, exactly as a trip making the same move does.
        TripPlanNotices.AnnouncesChanges(stateBefore) && TripPlanNotices.AnnouncesChanges(expedition.State)
            ? SendAsync(db, access, user, expedition, MessageTemplateCatalog.NotifyExpeditionUpdate, ct)
            : Task.CompletedTask;

    /// <summary>Tells the roster that the camp was called off.</summary>
    /// <param name="stateBefore">
    /// Where the camp stood when it was called off. Calling off a draft tells nobody, for the
    /// reason a draft's edits tell nobody.
    /// </param>
    public static Task CancelledAsync(
        SilexGisDbContext db,
        IAccessService access,
        UserContext user,
        Expedition expedition,
        ActivityState stateBefore,
        CancellationToken ct) =>
        TripPlanNotices.AnnouncesChanges(stateBefore)
            ? SendAsync(db, access, user, expedition, MessageTemplateCatalog.NotifyExpeditionCancelled, ct)
            : Task.CompletedTask;

    /// <summary>
    /// Tells one account it was put on the camp's roster. Silent while the camp is a draft or
    /// called off, exactly as naming somebody on a trip in those states is.
    /// </summary>
    public static async Task AddedAsync(
        SilexGisDbContext db,
        IAccessService access,
        UserContext user,
        Expedition expedition,
        Guid addedUserId,
        CancellationToken ct)
    {
        if (ActivityStates.SuppressesParticipantNotification(expedition.State))
        {
            return;
        }

        await SendAsync(
            db, access, user, expedition, MessageTemplateCatalog.NotifyExpeditionParticipation, ct, [addedUserId]);
    }

    private static async Task SendAsync(
        SilexGisDbContext db,
        IAccessService access,
        UserContext user,
        Expedition expedition,
        string templateKey,
        CancellationToken ct,
        IReadOnlyList<Guid>? only = null)
    {
        var concerned = only ?? await ExpeditionAudience.PeopleConcernedAsync(db, expedition.Id, ct);
        var recipients = await NotificationRecipients.WhoMayReadAsync(
            db, access, expedition, concerned, user.UserId, ct);
        if (recipients.Count == 0)
        {
            return;
        }

        var actorLabels = await ProfileDirectory.ResolveLabelsAsync(db, user, [user.UserId], ct);
        var actorName = actorLabels.GetValueOrDefault(user.UserId) ?? string.Empty;

        foreach (var recipient in recipients)
        {
            NotificationQueue.Enqueue(
                db,
                recipient,
                NotificationCategory.ExpeditionUpdate,
                templateKey,
                new Dictionary<string, string>
                {
                    ["actorName"] = actorName,
                    ["expeditionName"] = expedition.Name,
                    ["expeditionDate"] = expedition.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["url"] = $"/expeditions/{expedition.Id}",
                },
                // The camp, so the reader's right to open what this names is decided again when
                // they read it rather than trusted from the moment it was queued.
                NotificationTargetKind.Expedition,
                expedition.Id);
        }
    }
}
