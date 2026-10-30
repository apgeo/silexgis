// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Events;

/// <summary>
/// Tells one person that they have been asked to a club event.
/// </summary>
/// <remarks>
/// <para>
/// The invited person is the whole audience — that is what an invitation is. Nobody else on the
/// list is told, and neither is the person who asked: nobody is told about their own act. The
/// recipient's own right to read the event is checked as the trip invitation checks it, so asking
/// somebody who cannot open the event queues nothing rather than a message that names an event
/// they would be refused.
/// </para>
/// <para>
/// The message names the event, its date and who asked, and nothing else; an event has no cave on
/// it to name. It is queued before the caller's own <c>SaveChangesAsync</c>, so it exists only if
/// the invitation it announces was committed.
/// </para>
/// <para>
/// Only the first asking sends. Asking again is a nudge somebody makes with a person in mind, and
/// a create route that a client retries must not put a second message in the post. An invitee with
/// no account is told nothing, because there is nowhere to tell them.
/// </para>
/// </remarks>
internal static class EventInvitationNotifier
{
    public static async Task InvitedAsync(
        SilexGisDbContext db,
        IAccessService access,
        UserContext user,
        Event occasion,
        Guid? inviteeUserId,
        CancellationToken ct)
    {
        if (inviteeUserId is not { } invitee)
        {
            return;
        }

        var recipients = await NotificationRecipients.WhoMayReadAsync(
            db, access, occasion, [invitee], user.UserId, ct);
        if (recipients.Count == 0)
        {
            return;
        }

        var actorLabels = await ProfileDirectory.ResolveLabelsAsync(db, user, [user.UserId], ct);

        NotificationQueue.Enqueue(
            db,
            invitee,
            NotificationCategory.TripPlanning,
            MessageTemplateCatalog.NotifyEventInvitation,
            new Dictionary<string, string>
            {
                ["actorName"] = actorLabels.GetValueOrDefault(user.UserId) ?? string.Empty,
                ["eventTitle"] = occasion.Title,
                ["eventDate"] = occasion.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["url"] = $"/events/{occasion.Id}",
            },
            // The event, so the reader's right to open it is decided again when they read the
            // notice rather than trusted from the moment it was queued.
            NotificationTargetKind.Event,
            occasion.Id);
    }
}
