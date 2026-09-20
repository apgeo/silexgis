// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// Tells the party that their trip has been published.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gap this closes.</b> Publishing a tracked trip puts a page on the internet that anybody
/// holding the link can read, and — on an installation that has not turned the setting off, which
/// is the shipped default — that page names the party for real. The people named are not the person
/// who pressed the button: they are the rest of the club. Until this existed there was no way for
/// any of them to find out. The list of links needs write access to the trip, the trip's own
/// tracking read carried no fact about publication, and nothing told anybody. Somebody's real name
/// was on the public internet, by a default they did not choose, with no way to learn it and no way
/// to be told.
/// </para>
/// <para>
/// <b>Why a message and not only a page.</b> The trip's own read now says a trip is published, to
/// everybody who can read the trip, and that is the part that cannot be muted or missed. But it
/// only helps somebody who goes and looks — and nobody opens a trip they were on last week to check
/// whether something changed about it. A publication is a discrete event with a moment; the one
/// thing that matches that shape is a message sent at that moment. The existing notification
/// mechanism carries it for the cost of one extra row in the save the mint was already making, so
/// the alternative was not "cheaper" — it was silence.
/// </para>
/// <para>
/// <b>Who is told, and the rule that decides it.</b> Everybody on the trip's roster who holds an
/// account, narrowed by the shared recipient rule to those whose own access lets them read the
/// trip, and never the person who published it. That narrowing is not a courtesy: a message naming
/// a trip is an outbound copy of the trip, and telling somebody about one they could not open would
/// hand them its title and date outside every filter the read paths apply. A caver with no account
/// is reached by nothing here and cannot be — which is a real limit of this and is why the page
/// exists as well.
/// </para>
/// <para>
/// <b>What it does not do.</b> It does not wait for the trip to be in a particular state. The roster
/// announcer next door stays quiet for a trip being drafted or called off, because a name arriving
/// on a roster that nobody has published yet is administration rather than news — but a publication
/// has already happened by the time this runs, whatever the trip's own state says, and a disclosure
/// that went out silently because the trip was a draft is the exact failure this exists to prevent.
/// </para>
/// </remarks>
public sealed class TripPublicationAnnouncer(
    SilexGisDbContext db, IAccessService access, IUserContextAccessor userAccessor)
{
    /// <summary>
    /// Queues one notice per person the trip names, to be committed by the caller's own save.
    /// </summary>
    public async Task AnnounceAsync(TripLog trip, CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return;
        }

        // The roster, as accounts. A caver row that names no user is somebody this installation
        // cannot reach at all — a guest, a member of a visiting club — and they are left out here
        // rather than approximated by an address on the caver record: a notification is addressed
        // to an account, whose language and preferences decide how it travels.
        var rosterCavers = db.TripLogParticipants.AsNoTracking()
            .Where(p => p.TripLogId == trip.Id)
            .Select(p => p.CaverId);
        var candidates = await db.Cavers.AsNoTracking()
            .Where(c => rosterCavers.Contains(c.Id) && c.UserId != null)
            .Select(c => c.UserId!.Value)
            .Distinct()
            .ToListAsync(ct);
        if (candidates.Count == 0)
        {
            return;
        }

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
                NotificationCategory.TripPublished,
                MessageTemplateCatalog.NotifyTripPublished,
                new Dictionary<string, string>
                {
                    ["actorName"] = actorName,
                    ["tripTitle"] = trip.Title,
                    ["tripDate"] = trip.TripDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["url"] = $"/trip-logs/{trip.Id}",
                },
                // The trip, so that the reader's right to open what this names is decided again when
                // they read it rather than trusted from the moment it was queued.
                NotificationTargetKind.TripLog,
                trip.Id);
        }
    }
}
