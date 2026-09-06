// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>
/// Tells the people newly listed on a trip. Queued before the caller's save, so a notification
/// exists only if the trip write it describes actually committed.
/// </summary>
/// <remarks>
/// <para>
/// The message names the trip and its date and nothing else. A trip's linked caves are
/// deliberately absent: a cave link beside a trip's own location is exactly the disclosure the
/// cave-link redaction rules exist to prevent, and a notification is no less an outbound copy
/// of that data than a DTO is.
/// </para>
/// <para>
/// Every path that puts somebody's name on a trip comes through here, so the state rule is
/// checked once for every caller alike: a trip being written and a trip called off both keep
/// their silence however their roster is edited. Whether each recipient may actually read the
/// trip is the shared recipient rule, applied here as everywhere else — freshly, from that
/// person's own access, against the trip as it now stands, rather than assumed from their
/// being named on it.
/// </para>
/// <para>
/// The notices about a trip <i>being planned</i> — asked on it, changed, called off — are
/// deliberate acts by a person rather than a name arriving on a roster, and are sent by the
/// paths that cause them.
/// </para>
/// <para>
/// It lives beside the request surfaces rather than with the write core because it needs the
/// acting person's display label, and which label a given reader may be shown is a rule that
/// belongs with the surfaces that render people. A write with nobody acting behind it — a bulk
/// load of a club's old trips — asks the core for silence and never reaches here.
/// </para>
/// </remarks>
public sealed class TripRosterAnnouncer(
    SilexGisDbContext db, IAccessService access, IUserContextAccessor userAccessor) : ITripRosterAnnouncer
{
    public async Task AnnounceAsync(TripLog trip, IReadOnlyList<Guid> newlyNamedUserIds, CancellationToken ct)
    {
        if (ActivityStates.SuppressesParticipantNotification(trip.State))
        {
            return;
        }

        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return;
        }

        var recipients = await NotificationRecipients.WhoMayReadAsync(
            db, access, trip, newlyNamedUserIds, user.UserId, ct);
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
                NotificationCategory.TripParticipation,
                MessageTemplateCatalog.NotifyTripParticipation,
                new Dictionary<string, string>
                {
                    ["actorName"] = actorName,
                    ["tripTitle"] = trip.Title,
                    ["tripDate"] = trip.TripDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["url"] = $"/trip-logs/{trip.Id}",
                });
        }
    }
}
