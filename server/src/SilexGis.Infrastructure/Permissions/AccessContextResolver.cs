// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Permissions;

/// <summary>
/// Builds a user's <see cref="AccessContext"/>: caving groups through their caver row,
/// permission-group reach (direct membership, caving-group-mediated membership, the
/// implicit All Users), the Full Administrators short-circuit, and every access entry
/// reaching them. One home for the resolution so the request accessor and tests cannot
/// drift apart. An account-less caver contributes nothing anywhere here — memberships
/// only reach the context through <c>cavers.user_id</c>.
/// </summary>
public static class AccessContextResolver
{
    public static async Task<AccessContext> ResolveAsync(
        SilexGisDbContext db, Guid userId, CancellationToken ct = default)
    {
        var cavingGroupIds = await (
            from membership in db.CavingGroupMemberships.AsNoTracking()
            join caver in db.Cavers.AsNoTracking() on membership.CaverId equals caver.Id
            where caver.UserId == userId
            select membership.CavingGroupId)
            .Distinct()
            .ToArrayAsync(ct);

        var wellKnown = await db.PermissionGroups.AsNoTracking()
            .Where(g => g.Slug == SeededPermissionGroups.FullAdministratorsSlug
                || g.Slug == SeededPermissionGroups.AllUsersSlug)
            .Select(g => new { g.Id, g.Slug })
            .ToListAsync(ct);

        var memberOf = await db.PermissionGroupMembers.AsNoTracking()
            .Where(m => (m.MemberKind == AccessSubjectKind.User && m.MemberId == userId)
                || (m.MemberKind == AccessSubjectKind.CavingGroup && cavingGroupIds.Contains(m.MemberId)))
            .Select(m => m.PermissionGroupId)
            .Distinct()
            .ToListAsync(ct);

        var fullAdminsId = wellKnown
            .FirstOrDefault(g => g.Slug == SeededPermissionGroups.FullAdministratorsSlug)?.Id;
        var isFullAdmin = fullAdminsId is { } fa && memberOf.Contains(fa);

        // Every account is an implicit member of All Users — no membership rows exist.
        var reachableGroups = memberOf.ToHashSet();
        if (wellKnown.FirstOrDefault(g => g.Slug == SeededPermissionGroups.AllUsersSlug)?.Id is { } allUsers)
        {
            reachableGroups.Add(allUsers);
        }

        var groupIds = reachableGroups.ToArray();
        var entries = await db.AccessEntries.AsNoTracking()
            .Where(e => (e.PermissionGroupId != null && groupIds.Contains(e.PermissionGroupId.Value))
                || (e.SubjectKind == AccessSubjectKind.User && e.SubjectId == userId)
                || (e.SubjectKind == AccessSubjectKind.CavingGroup
                    && e.SubjectId != null && cavingGroupIds.Contains(e.SubjectId.Value)))
            .ToListAsync(ct);

        return new AccessContext(
            userId,
            isFullAdmin,
            cavingGroupIds,
            [.. entries.Select(e => e.ToSnapshot())]);
    }
}
