// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>
/// Tells the people a trip being planned concerns: that they have been asked on it, that it has
/// changed, or that it is not happening.
/// </summary>
/// <remarks>
/// <para>
/// <b>No message here names a cave.</b> Each one names the trip, its date and who acted, and
/// deliberately nothing else. The places a trip is about are readable by fewer people than its
/// list of people, and a message is no less an outbound copy of that than anything the API
/// returns — a plan naming a cave to whoever was asked on it is precisely the back door around
/// cave visibility that the trip's own reading closes. The recipient's own right to read the
/// <i>trip</i> is checked; nobody's right to read its caves is, because no cave is mentioned.
/// </para>
/// <para>
/// All three are deliberate acts by a person — somebody was asked, somebody edited, somebody
/// called it off — so none of them consults the switch that decides whether <i>entering a state</i>
/// tells the roster the activity exists. That switch answers a different question and keeps
/// answering it: a trip called off still announces nothing by being called off. The notice that it
/// was called off is this, sent by the path that called it off.
/// </para>
/// <para>
/// Every send queues before the caller's own <c>SaveChangesAsync</c>, so a message exists only if
/// the change it describes actually committed.
/// </para>
/// </remarks>
internal static class TripPlanNotifier
{
    /// <summary>
    /// Tells one person they have been asked on a trip.
    /// </summary>
    /// <remarks>
    /// Only the first asking sends. Asking again is a nudge somebody makes with a person in mind,
    /// and a create route that a client retries must not put a second message in the post; the
    /// stamps on the row keep the same rule for the same reason.
    /// <para>
    /// An invitee with no account is told nothing, because there is nowhere to tell them: the list
    /// is a directory entry, which most cavers have and few accounts do. Nothing here fails for
    /// them — they simply receive no mail, and the trip's own list is where somebody organising it
    /// sees they will have to be reached another way.
    /// </para>
    /// </remarks>
    public static Task InvitedAsync(
        SilexGisDbContext db,
        IAccessService access,
        UserContext user,
        TripLog trip,
        Guid? inviteeUserId,
        CancellationToken ct) =>
        inviteeUserId is { } invitee
            ? SendAsync(db, access, user, trip, [invitee], MessageTemplateCatalog.NotifyTripPlanInvitation, ct)
            : Task.CompletedTask;

    /// <summary>Tells the people a trip concerns that it changed.</summary>
    /// <param name="alreadyTold">
    /// Whoever the same write has just told about the trip another way — the people whose names
    /// went onto it in this edit, who are being sent the notice that they are on it. One write is
    /// one message to one person: telling them in the same breath that the trip they have just
    /// been put on has changed says nothing they do not already have.
    /// </param>
    public static async Task ChangedAsync(
        SilexGisDbContext db,
        IAccessService access,
        UserContext user,
        TripLog trip,
        IReadOnlyCollection<Guid> alreadyTold,
        CancellationToken ct)
    {
        if (!TripPlanNotices.AnnouncesChanges(trip.State))
        {
            return;
        }

        var concerned = await PeopleConcernedAsync(db, trip.Id, ct);
        await SendAsync(
            db,
            access,
            user,
            trip,
            concerned.Where(id => !alreadyTold.Contains(id)),
            MessageTemplateCatalog.NotifyTripPlanChanged,
            ct);
    }

    /// <summary>
    /// Tells the people a trip concerns that it is not happening.
    /// </summary>
    /// <param name="stateBefore">
    /// Where the trip stood when it was called off, which is what decides whether anybody was
    /// expecting it. Calling off a draft tells nobody: a draft has been shown to nobody, so the
    /// notice would be the first they heard of the trip at all.
    /// </param>
    public static async Task CancelledAsync(
        SilexGisDbContext db,
        IAccessService access,
        UserContext user,
        TripLog trip,
        ActivityState stateBefore,
        CancellationToken ct)
    {
        if (!TripPlanNotices.AnnouncesChanges(stateBefore))
        {
            return;
        }

        var concerned = await PeopleConcernedAsync(db, trip.Id, ct);
        await SendAsync(db, access, user, trip, concerned, MessageTemplateCatalog.NotifyTripPlanCancelled, ct);
    }

    /// <summary>
    /// Everybody a trip concerns and can be written to: whoever is named on it and whoever was
    /// asked about it, unioned.
    /// </summary>
    /// <remarks>
    /// Both halves, because a plan reaches people twice over — somebody asked and not yet written
    /// in cares that the date moved exactly as much as somebody already on the roster, and after
    /// the answers are written into the roster the same person is on both lists. Whether each of
    /// them may actually read the trip is decided afterwards, once, for every caller alike.
    /// </remarks>
    private static async Task<List<Guid>> PeopleConcernedAsync(
        SilexGisDbContext db, Guid tripId, CancellationToken ct)
    {
        var named = await (
            from participant in db.TripLogParticipants.AsNoTracking()
            join caver in db.Cavers.AsNoTracking() on participant.CaverId equals caver.Id
            where participant.TripLogId == tripId && caver.UserId != null
            select caver.UserId!.Value)
            .ToListAsync(ct);

        var asked = await (
            from invitation in db.TripInvitations.AsNoTracking()
            join caver in db.Cavers.AsNoTracking() on invitation.CaverId equals caver.Id
            where invitation.TripLogId == tripId && caver.UserId != null
            select caver.UserId!.Value)
            .ToListAsync(ct);

        return [.. named, .. asked];
    }

    private static async Task SendAsync(
        SilexGisDbContext db,
        IAccessService access,
        UserContext user,
        TripLog trip,
        IEnumerable<Guid> candidates,
        string templateKey,
        CancellationToken ct)
    {
        var recipients = await NotificationRecipients.WhoMayReadAsync(
            db, access, trip, candidates, user.UserId, ct);
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
                NotificationCategory.TripPlanning,
                templateKey,
                new Dictionary<string, string>
                {
                    ["actorName"] = actorName,
                    ["tripTitle"] = trip.Title,
                    ["tripDate"] = trip.TripDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    ["url"] = $"/trip-logs/{trip.Id}",
                });
        }
    }
}
