// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Permissions;

/// <summary>
/// The accounts that may decide about held uploads right now: the people told that an upload
/// waits, and the people who look after what phones send.
/// </summary>
/// <remarks>
/// <para>
/// The right can arrive by every road a right arrives by — full administration, a ruleset the
/// account belongs to directly or through a caving group, a direct entry on the account or on one
/// of its caving groups — and can be taken away by a deny entry on any of them. Rather than
/// restate how those combine, every account any road could reach is gathered first, cheaply, and
/// then each is asked the one question through the evaluator every route asks it through. What
/// is gathered is only who might hold it; whether they do is never decided here.
/// </para>
/// <para>
/// Locked accounts are left out, as they are of every recipient list: an account that cannot sign
/// in cannot decide anything, and a message to it is a message to whoever reads a stale mailbox.
/// </para>
/// </remarks>
public static class UploadReviewers
{
    /// <summary>The live accounts that hold <see cref="AccessDomain.UploadReview"/>·Write.</summary>
    public static async Task<IReadOnlyList<Guid>> WriteHolderIdsAsync(SilexGisDbContext db, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var liveUsers = db.Users.AsNoTracking().Where(u => u.LockoutEnd == null || u.LockoutEnd <= now);
        var entries = db.AccessEntries.AsNoTracking().Where(e =>
            e.Domain == AccessDomain.UploadReview
            && e.Effect == AccessEffect.Allow
            && (e.Actions & AccessAction.Write) == AccessAction.Write);

        var rulesets = entries.Where(e => e.PermissionGroupId != null).Select(e => e.PermissionGroupId!.Value);
        var groupMembers = db.PermissionGroupMembers.AsNoTracking().Where(m => rulesets.Contains(m.PermissionGroupId));
        var directUsers = entries
            .Where(e => e.PermissionGroupId == null && e.SubjectKind == AccessSubjectKind.User && e.SubjectId != null)
            .Select(e => e.SubjectId!.Value);
        var directCavingGroups = entries
            .Where(e => e.PermissionGroupId == null && e.SubjectKind == AccessSubjectKind.CavingGroup && e.SubjectId != null)
            .Select(e => e.SubjectId!.Value);

        var userIds = groupMembers.Where(m => m.MemberKind == AccessSubjectKind.User).Select(m => m.MemberId)
            .Concat(directUsers);
        var cavingGroupIds = groupMembers.Where(m => m.MemberKind == AccessSubjectKind.CavingGroup).Select(m => m.MemberId)
            .Concat(directCavingGroups);
        var throughCavingGroups =
            from membership in db.CavingGroupMemberships.AsNoTracking()
            where cavingGroupIds.Contains(membership.CavingGroupId)
            join caver in db.Cavers.AsNoTracking() on membership.CaverId equals caver.Id
            where caver.UserId != null
            select caver.UserId!.Value;

        var candidates = await liveUsers
            .Where(u => userIds.Contains(u.Id) || throughCavingGroups.Contains(u.Id))
            .Select(u => u.Id)
            .ToListAsync(ct);
        candidates.AddRange(await FullAdministrators.LiveMemberIdsAsync(db, ct));

        var holders = new List<Guid>();
        foreach (var candidate in candidates.Distinct())
        {
            var ctx = await AccessContextResolver.ResolveAsync(db, candidate, ct);
            if (UploadReviewRules.MayDecide(ctx))
            {
                holders.Add(candidate);
            }
        }

        return holders;
    }
}
