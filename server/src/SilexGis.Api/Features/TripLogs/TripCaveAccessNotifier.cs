// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>
/// Tells somebody who can grant it when a person asked on a trip cannot open a cave that trip is
/// about.
/// </summary>
/// <remarks>
/// <para>
/// Being asked on a trip grants nothing. Somebody organising one therefore has no way to open the
/// cave to the person they just asked, and the person asked has no way to ask for it — so this
/// closes the loop the only way that hands nobody anything they did not already have: it tells
/// people who can already open the cave that somebody else cannot, and links them to the page
/// where they may change that. There is no button here that grants: a grant made from a message
/// would have no expiry to give it, and no mark on it saying where it came from, so the next
/// person to save that cave's permissions would silently delete it.
/// </para>
/// <para>
/// Either half of the pairing can arrive second, so both openings are watched: a person asked
/// onto a trip whose caves are already named, and a cave named onto a trip whose people are
/// already asked. A plan floated before its objective is settled is at least as ordinary as the
/// other way round, and watching only the first would leave the loop closed only when the caves
/// happened to come first.
/// </para>
/// <para>
/// <b>The audience is sound but not complete, and the message says so.</b> Everybody told really
/// can grant — the cave's owner holds every action on it by owning it, and a full administrator
/// holds every action on everything. But asking the opposite question, "who may grant on this
/// cave", is not something the access rules can answer: they decide one person against one row,
/// and inverting them means a fourth copy of a walk that already exists three times over with
/// tests pinning the three against each other. So somebody holding the right to change this
/// cave's permissions through a club is missed, and the wording tells its reader to pass the
/// message on rather than letting them assume everybody who could act has been told.
/// </para>
/// <para>
/// This is the one message about a trip that names a cave, and it is only ever sent to somebody
/// whose own access already opens that cave, decided freshly for each of them. A notice that
/// disclosed a cave in the course of protecting it would be worse than sending nothing.
/// </para>
/// <para>
/// Somebody asked who holds no account is told nothing and can be granted nothing: the list is a
/// directory entry, most of which are people who never sign in, and a rule can only be written
/// for an account or a club. Nothing fails for them — there is simply nobody to tell, which is
/// what the trip's own list is for.
/// </para>
/// </remarks>
internal static class TripCaveAccessNotifier
{
    /// <summary>
    /// One person has just been asked onto a trip: weighs them against every cave the trip is
    /// about.
    /// </summary>
    public static async Task InviteeCannotOpenCavesAsync(
        SilexGisDbContext db,
        IAccessService access,
        UserContext user,
        TripLog trip,
        Guid? inviteeUserId,
        CancellationToken ct)
    {
        if (inviteeUserId is not { } invitee)
        {
            return;
        }

        // Read past every visibility filter on purpose: the question is which caves the trip is
        // about, not which of them the person asking happens to see. Nothing read here is put in
        // a message to anybody who could not already read it.
        var caveIds = (await TripRoleLinks.PairsForAsync(db, [trip.Id], FeatureKind.Cave, ct))
            .Select(pair => pair.FeatureId)
            .Distinct()
            .ToList();

        await SendAsync(db, access, user, trip, caveIds, [invitee], ct);
    }

    /// <summary>
    /// Caves have just been named onto a trip: weighs everybody already asked onto it against the
    /// caves that were added, and only those — the people asked were already weighed against the
    /// rest when they were asked, and saying the same thing twice about an unchanged pairing is
    /// how a category earns being muted.
    /// </summary>
    public static async Task CavesAddedAsync(
        SilexGisDbContext db,
        IAccessService access,
        UserContext user,
        TripLog trip,
        IReadOnlyCollection<Guid> addedCaveIds,
        CancellationToken ct)
    {
        if (addedCaveIds.Count == 0)
        {
            return;
        }

        var invitees = await (
            from invitation in db.TripInvitations.AsNoTracking()
            join caver in db.Cavers.AsNoTracking() on invitation.CaverId equals caver.Id
            where invitation.TripLogId == trip.Id && caver.UserId != null
            select caver.UserId!.Value)
            .Distinct()
            .ToListAsync(ct);

        await SendAsync(db, access, user, trip, [.. addedCaveIds], invitees, ct);
    }

    /// <summary>
    /// The whole rule, for any pairing of caves with people asked: for each cave, whoever of them
    /// cannot open it, told to whoever of the cave's own audience can.
    /// </summary>
    /// <remarks>
    /// Every account's access is resolved once for the whole send and weighed against each cave
    /// in turn. Resolving one costs several round trips and depends on nothing but the account,
    /// so re-resolving the same handful of people per cave would multiply the cost of a trip
    /// naming six places by six for no different answer.
    /// </remarks>
    private static async Task SendAsync(
        SilexGisDbContext db,
        IAccessService access,
        UserContext user,
        TripLog trip,
        IReadOnlyList<Guid> caveIds,
        IReadOnlyList<Guid> inviteeUserIds,
        CancellationToken ct)
    {
        if (caveIds.Count == 0 || inviteeUserIds.Count == 0)
        {
            return;
        }

        var caves = await db.Features.AsNoTracking()
            .Where(f => caveIds.Contains(f.Id))
            .ToListAsync(ct);
        if (caves.Count == 0)
        {
            return;
        }

        var inviteeContexts = new Dictionary<Guid, AccessContext>(inviteeUserIds.Count);
        foreach (var invitee in inviteeUserIds.Distinct())
        {
            inviteeContexts[invitee] = await AccessContextResolver.ResolveAsync(db, invitee, ct);
        }

        // Both of these are read on the first cave that turns out to be shut, and never when none
        // is: the ordinary asking is of somebody who can open everything the trip is about, and it
        // should cost nothing beyond deciding that.
        List<Guid>? administrators = null;
        var audienceContexts = new Dictionary<Guid, AccessContext>();
        Dictionary<Guid, string>? labels = null;

        foreach (var cave in caves)
        {
            var shutOut = new List<Guid>();
            foreach (var (invitee, theirs) in inviteeContexts)
            {
                if (!(await access.DecideAsync(theirs, AccessAction.Read, cave, ct)).Allowed)
                {
                    shutOut.Add(invitee);
                }
            }

            if (shutOut.Count == 0)
            {
                continue;
            }

            administrators ??= await FullAdministrators.LiveMemberIdsAsync(db, ct);

            // A row whose owner is gone leaves an empty id behind rather than a null one; there is
            // no account to tell, and the administrators still are the audience.
            List<Guid> candidates = cave.OwnerUserId == Guid.Empty
                ? administrators
                : [cave.OwnerUserId, .. administrators];

            // Each account's own access, resolved once and reused across the caves that follow.
            foreach (var candidate in candidates)
            {
                if (candidate != user.UserId && !audienceContexts.ContainsKey(candidate))
                {
                    audienceContexts[candidate] = await AccessContextResolver.ResolveAsync(db, candidate, ct);
                }
            }

            var recipients = await NotificationRecipients.WhoMayReadAsync(
                access,
                cave,
                candidates.Where(id => id != user.UserId),
                audienceContexts,
                ct);
            if (recipients.Count == 0)
            {
                continue;
            }

            // Resolved on the first send that needs them and no earlier: a pairing where nobody is
            // shut out sends nothing and should cost no directory read.
            labels ??= await ProfileDirectory.ResolveLabelsAsync(
                db, user, [user.UserId, .. inviteeContexts.Keys], ct);
            var actorName = labels.GetValueOrDefault(user.UserId) ?? string.Empty;

            foreach (var invitee in shutOut)
            {
                foreach (var recipient in recipients)
                {
                    NotificationQueue.Enqueue(
                        db,
                        recipient,
                        NotificationCategory.TripPlanning,
                        MessageTemplateCatalog.NotifyTripInviteeCannotOpenCave,
                        new Dictionary<string, string>
                        {
                            ["actorName"] = actorName,
                            ["inviteeName"] = labels.GetValueOrDefault(invitee) ?? string.Empty,
                            // A cave need never have been named; an unnamed one is identified by
                            // the head of its id, the way every list on the site identifies it.
                            ["caveName"] = cave.Name is { Length: > 0 } name
                                ? name
                                : $"{cave.Kind} {cave.Id.ToString("N")[..8]}",
                            // The cave's own page, which is where its permissions are changed from.
                            ["url"] = $"/caves/{cave.Id}",
                        });
                }
            }
        }
    }
}
