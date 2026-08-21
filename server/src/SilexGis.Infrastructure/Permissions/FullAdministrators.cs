// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Permissions;

/// <summary>
/// Who the installation's full administrators are, as accounts that could actually act.
/// </summary>
/// <remarks>
/// <para>
/// The escape-hatch group is the one membership that is itself the grant, so its members hold
/// every action on every row without an entry anywhere — which makes them the one audience that
/// can always be named without asking a permission question first, and the reason this is worth
/// its own read.
/// </para>
/// <para>
/// It lives here rather than beside any caller because it reads account rows, which a feature
/// slice may not, and because the callers are not all requests: something running on a timer
/// needs the same answer and has no request to borrow it from.
/// </para>
/// <para>
/// Membership is reached two ways — an account named directly, or an account whose directory
/// entry belongs to a caving group that is named — and locked-out accounts are left out of both,
/// because telling somebody who cannot sign in is telling nobody.
/// </para>
/// <para>
/// <b>This is the single home of that rule.</b> The lockout guard asks the same question in the
/// narrower form "is there at least one", and asks it of the query built here rather than of a
/// second copy: two copies would let tightening what counts as a live administrator in one place
/// silently keep an installation alive on an account the other never writes to.
/// </para>
/// </remarks>
public static class FullAdministrators
{
    /// <summary>
    /// The escape-hatch group's id, or null when the installation has not been seeded yet.
    /// </summary>
    public static async Task<Guid?> GroupIdAsync(SilexGisDbContext db, CancellationToken ct = default) =>
        await db.PermissionGroups.AsNoTracking()
            .Where(g => g.Slug == SeededPermissionGroups.FullAdministratorsSlug)
            .Select(g => (Guid?)g.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Every account that holds full administration through <paramref name="groupId"/> and can
    /// sign in, as an unexecuted query so a caller that only needs to know whether there is one
    /// can ask that instead of materialising the list. Ids may repeat: an account named directly
    /// and reachable through a caving group appears on both halves.
    /// </summary>
    public static IQueryable<Guid> LiveMemberIds(SilexGisDbContext db, Guid groupId)
    {
        var now = DateTimeOffset.UtcNow;
        var liveUsers = db.Users.AsNoTracking()
            .Where(u => u.LockoutEnd == null || u.LockoutEnd <= now);

        var direct = db.PermissionGroupMembers.AsNoTracking()
            .Where(m => m.PermissionGroupId == groupId && m.MemberKind == AccessSubjectKind.User)
            .Join(liveUsers, m => m.MemberId, u => u.Id, (m, u) => u.Id);

        var throughCavingGroups =
            from member in db.PermissionGroupMembers.AsNoTracking()
            where member.PermissionGroupId == groupId
                && member.MemberKind == AccessSubjectKind.CavingGroup
            join membership in db.CavingGroupMemberships.AsNoTracking()
                on member.MemberId equals membership.CavingGroupId
            join caver in db.Cavers.AsNoTracking() on membership.CaverId equals caver.Id
            where caver.UserId != null
            join user in liveUsers on caver.UserId!.Value equals user.Id
            select user.Id;

        return direct.Concat(throughCavingGroups);
    }

    /// <summary>
    /// Every account that holds full administration and can sign in, deduplicated. Empty when the
    /// group has not been seeded yet: an audience nobody is in is the safe answer, where the
    /// lockout guard's "is there at least one" question has to answer the opposite way.
    /// </summary>
    public static async Task<List<Guid>> LiveMemberIdsAsync(
        SilexGisDbContext db, CancellationToken ct = default)
    {
        if (await GroupIdAsync(db, ct) is not { } groupId)
        {
            return [];
        }

        return await LiveMemberIds(db, groupId).Distinct().ToListAsync(ct);
    }
}
