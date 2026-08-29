// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>Derivations over caving-group membership that more than one caller needs.</summary>
/// <remarks>
/// The one place membership crosses from people to accounts. Rosters record cavers, most of whom
/// never sign in; anything that asks "which users are in this group" goes through here, so a
/// member without an account is excluded once rather than in every caller.
/// <para>
/// These run the query instead of handing back a composable one, deliberately: the join has to be
/// filtered before it is projected into pairs, and returning the projection invites a caller to
/// filter after it — a shape the provider cannot translate.
/// </para>
/// </remarks>
public static class CavingGroupQueries
{
    /// <summary>The account-holding members of the given caving groups.</summary>
    public static async Task<List<CavingGroupUser>> UsersOfCavingGroupsAsync(
        this SilexGisDbContext db, IReadOnlyCollection<Guid> cavingGroupIds, CancellationToken ct = default)
    {
        if (cavingGroupIds.Count == 0)
        {
            return [];
        }

        return await (
            from membership in db.CavingGroupMemberships.AsNoTracking()
            join caver in db.Cavers.AsNoTracking() on membership.CaverId equals caver.Id
            where cavingGroupIds.Contains(membership.CavingGroupId) && caver.UserId != null
            select new CavingGroupUser(caver.UserId!.Value, membership.CavingGroupId))
            .ToListAsync(ct);
    }

    /// <summary>Who an announcement to one caving group reaches.</summary>
    /// <remarks>
    /// <b>The one rule, asked once.</b> The number shown to the person writing, the notices the
    /// request goes on to write, and the notices a background pass writes for a roster too large
    /// to do inline are all the same set decided here. Two pieces of code answering "who is on
    /// this roster" separately is how somebody is shown one number and sends to another — and
    /// neither answer would look wrong on its own, least of all for the large club that is the
    /// only one taking the second path.
    /// <para>
    /// Never the roster's size: a member without an account has nowhere to receive anything. And
    /// announcing to a group you are on is ordinary, while being told what you just wrote is not,
    /// so the sender is removed here rather than by each caller remembering to.
    /// </para>
    /// </remarks>
    public static async Task<HashSet<Guid>> AnnouncementRecipientsAsync(
        this SilexGisDbContext db, Guid cavingGroupId, Guid senderUserId, CancellationToken ct = default)
    {
        var members = await db.UsersOfCavingGroupsAsync([cavingGroupId], ct);
        var recipients = new HashSet<Guid>(members.Select(m => m.UserId));
        recipients.Remove(senderUserId);
        return recipients;
    }

    /// <summary>The caving groups each of the given accounts belongs to.</summary>
    public static async Task<List<CavingGroupUser>> CavingGroupsOfUsersAsync(
        this SilexGisDbContext db, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        return await (
            from membership in db.CavingGroupMemberships.AsNoTracking()
            join caver in db.Cavers.AsNoTracking() on membership.CaverId equals caver.Id
            where caver.UserId != null && userIds.Contains(caver.UserId.Value)
            select new CavingGroupUser(caver.UserId!.Value, membership.CavingGroupId))
            .ToListAsync(ct);
    }
}
