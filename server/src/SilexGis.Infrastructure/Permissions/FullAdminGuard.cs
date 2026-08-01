// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Permissions;

/// <summary>
/// The lockout guard: the installation must always keep at least one Full
/// Administrator who can actually sign in — an active, unlocked account reachable
/// directly or through a caving group. Call sites stage their mutation inside a
/// transaction, run <see cref="AnyLiveFullAdminAsync"/>, and roll back with
/// <see cref="LastFullAdminCode"/> when the answer is no. Live-membership aware, not
/// row-count aware: removing the last member row, locking or deleting the last such
/// user, unlinking their caver, and emptying the last reachable caving group are all
/// the same violation.
/// </summary>
public sealed class FullAdminGuard(SilexGisDbContext db)
{
    public const string LastFullAdminCode = "permission_group.last_full_admin";

    /// <summary>Refused when a roster edit would hand an account full administration.</summary>
    public const string GrantsFullAdminCode = "caver.grants_full_admin";

    /// <summary>
    /// Whether this person's caving-group memberships reach Full Administrators — i.e.
    /// whether attaching an account to them would make that account a full administrator.
    /// Roster edits are permission edits (a membership is a grant path), so the ones that
    /// would confer the escape hatch are reserved to people who already hold it.
    /// </summary>
    public Task<bool> CaverReachesFullAdministratorsAsync(Guid caverId, CancellationToken ct = default) =>
        (from membership in db.CavingGroupMemberships.AsNoTracking()
         where membership.CaverId == caverId
         join member in db.PermissionGroupMembers.AsNoTracking()
             on membership.CavingGroupId equals member.MemberId
         where member.MemberKind == AccessSubjectKind.CavingGroup
         join group_ in db.PermissionGroups.AsNoTracking() on member.PermissionGroupId equals group_.Id
         where group_.Slug == SeededPermissionGroups.FullAdministratorsSlug
         select membership.Id)
        .AnyAsync(ct);

    public async Task<bool> AnyLiveFullAdminAsync(CancellationToken ct = default)
    {
        var fullAdminsId = await db.PermissionGroups.AsNoTracking()
            .Where(g => g.Slug == SeededPermissionGroups.FullAdministratorsSlug)
            .Select(g => (Guid?)g.Id)
            .FirstOrDefaultAsync(ct);
        if (fullAdminsId is null)
        {
            // Not seeded yet (first boot mid-seed) — nothing to guard.
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        var liveUsers = db.Users.AsNoTracking()
            .Where(u => u.LockoutEnd == null || u.LockoutEnd <= now);

        var direct = await db.PermissionGroupMembers.AsNoTracking()
            .Where(m => m.PermissionGroupId == fullAdminsId && m.MemberKind == AccessSubjectKind.User)
            .Join(liveUsers, m => m.MemberId, u => u.Id, (m, u) => u.Id)
            .AnyAsync(ct);
        if (direct)
        {
            return true;
        }

        return await (
            from member in db.PermissionGroupMembers.AsNoTracking()
            where member.PermissionGroupId == fullAdminsId
                && member.MemberKind == AccessSubjectKind.CavingGroup
            join membership in db.CavingGroupMemberships.AsNoTracking()
                on member.MemberId equals membership.CavingGroupId
            join caver in db.Cavers.AsNoTracking() on membership.CaverId equals caver.Id
            where caver.UserId != null
            join user in liveUsers on caver.UserId!.Value equals user.Id
            select user.Id)
            .AnyAsync(ct);
    }
}
