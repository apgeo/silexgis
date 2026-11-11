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

    /// <summary>Refused when a roster edit would take full administration away from an account.</summary>
    public const string RevokesFullAdminCode = "caver.revokes_full_admin";

    /// <summary>
    /// The one question every roster write asks before it moves an account onto or off a grant
    /// path: is this a change to who holds full administration that the caller may not make?
    /// A caving-group membership is a grant path — a caving group can be a trustee of Full
    /// Administrators — so linking an account to a person, folding one person into another,
    /// enrolling someone in a group and taking them out again are all permission edits. Changing
    /// who holds the escape hatch is reserved to people who already hold it, in both directions,
    /// exactly as the Full Administrators trustee list itself is.
    /// </summary>
    /// <param name="caller">Who is asking. A full administrator is never refused.</param>
    /// <param name="accountId">
    /// The account whose paths the write moves. A person with no account has nothing that
    /// reaches a decision, so a write that moves no account is never refused here — the link
    /// that would later attach one asks this same question.
    /// </param>
    /// <param name="caverIds">
    /// Roster entries whose caving-group memberships the account gains or loses: the person an
    /// account is linked to or unlinked from, or the entry a merge attaches the account to.
    /// </param>
    /// <param name="cavingGroupIds">Caving groups the account's person joins or leaves.</param>
    public async Task<bool> ReservedToFullAdministratorsAsync(
        AccessContext caller,
        Guid? accountId,
        IReadOnlyCollection<Guid> caverIds,
        IReadOnlyCollection<Guid> cavingGroupIds,
        CancellationToken ct = default)
    {
        if (caller.IsFullAdmin || accountId is null || (caverIds.Count == 0 && cavingGroupIds.Count == 0))
        {
            return false;
        }

        var trustees =
            from member in db.PermissionGroupMembers.AsNoTracking()
            where member.MemberKind == AccessSubjectKind.CavingGroup
            join group_ in db.PermissionGroups.AsNoTracking() on member.PermissionGroupId equals group_.Id
            where group_.Slug == SeededPermissionGroups.FullAdministratorsSlug
            select member.MemberId;

        return await trustees.AnyAsync(groupId =>
            cavingGroupIds.Contains(groupId)
            || db.CavingGroupMemberships.Any(m => m.CavingGroupId == groupId && caverIds.Contains(m.CaverId)), ct);
    }

    /// <summary>
    /// The same question for a write that removes a caving group outright: when the group is a
    /// trustee of Full Administrators, deleting it takes the row off that trustee list and full
    /// administration away from every account whose person sits in it. Removing that trustee row
    /// on its own is reserved to full administrators, and so is removing it in bulk.
    /// </summary>
    /// <param name="caller">Who is asking. A full administrator is never refused.</param>
    /// <param name="cavingGroupId">The caving group about to go.</param>
    public async Task<bool> ReservedToFullAdministratorsAsync(
        AccessContext caller, Guid cavingGroupId, CancellationToken ct = default)
    {
        if (caller.IsFullAdmin)
        {
            return false;
        }

        return await (
            from member in db.PermissionGroupMembers.AsNoTracking()
            where member.MemberKind == AccessSubjectKind.CavingGroup && member.MemberId == cavingGroupId
            join group_ in db.PermissionGroups.AsNoTracking() on member.PermissionGroupId equals group_.Id
            where group_.Slug == SeededPermissionGroups.FullAdministratorsSlug
            select member.Id).AnyAsync(ct);
    }

    /// <summary>
    /// Whether any account holding full administration can actually sign in. Answered from the
    /// one query that defines that membership, so the guard and everything else that needs the
    /// same audience cannot answer the question differently.
    /// </summary>
    public async Task<bool> AnyLiveFullAdminAsync(CancellationToken ct = default)
    {
        if (await FullAdministrators.GroupIdAsync(db, ct) is not { } fullAdminsId)
        {
            // Not seeded yet (first boot mid-seed) — nothing to guard.
            return true;
        }

        // Concurrent removals are decided one after another, each seeing the ones before it.
        await FullAdminGuardSql.TakeLastFullAdminLockAsync(db, ct);
        return await FullAdministrators.LiveMemberIds(db, fullAdminsId).AnyAsync(ct);
    }
}
