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
    /// <summary>
    /// The context a hypothetical member of one caving group would have — what the rule
    /// editor previews before saving. It carries no user identity on purpose: an answer
    /// that depended on whose account it was would not be an answer about the group.
    /// Rules reaching the group directly, and every ruleset it is a trustee of, apply;
    /// so does All Users, since any such member would hold it.
    /// </summary>
    public static async Task<AccessContext> ResolveForCavingGroupAsync(
        SilexGisDbContext db, Guid cavingGroupId, CancellationToken ct = default)
    {
        var wellKnown = await WellKnownAsync(db, ct);
        var memberOf = await db.PermissionGroupMembers.AsNoTracking()
            .Where(m => m.MemberKind == AccessSubjectKind.CavingGroup && m.MemberId == cavingGroupId)
            .Select(m => m.PermissionGroupId)
            .Distinct()
            .ToListAsync(ct);

        var isFullAdmin = wellKnown.FullAdministratorsId is { } fa && memberOf.Contains(fa);
        var reachable = memberOf.ToHashSet();
        if (wellKnown.AllUsersId is { } allUsers)
        {
            reachable.Add(allUsers);
        }

        var groupIds = reachable.ToArray();
        var entries = await db.AccessEntries.AsNoTracking()
            .Where(e => (e.PermissionGroupId != null && groupIds.Contains(e.PermissionGroupId.Value))
                || (e.SubjectKind == AccessSubjectKind.CavingGroup && e.SubjectId == cavingGroupId))
            .ToListAsync(ct);

        return new AccessContext(
            Guid.Empty, isFullAdmin, [cavingGroupId], [.. entries.Select(e => e.ToSnapshot())]);
    }

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

        var wellKnown = await WellKnownAsync(db, ct);

        var memberOf = await db.PermissionGroupMembers.AsNoTracking()
            .Where(m => (m.MemberKind == AccessSubjectKind.User && m.MemberId == userId)
                || (m.MemberKind == AccessSubjectKind.CavingGroup && cavingGroupIds.Contains(m.MemberId)))
            .Select(m => m.PermissionGroupId)
            .Distinct()
            .ToListAsync(ct);

        var isFullAdmin = wellKnown.FullAdministratorsId is { } fa && memberOf.Contains(fa);

        // Every account is an implicit member of All Users — no membership rows exist.
        var reachableGroups = memberOf.ToHashSet();
        if (wellKnown.AllUsersId is { } allUsers)
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

    /// <summary>
    /// The two groups the resolver keys on by slug: the escape hatch, whose membership is
    /// the grant, and the implicit one every account holds.
    /// </summary>
    private static async Task<(Guid? FullAdministratorsId, Guid? AllUsersId)> WellKnownAsync(
        SilexGisDbContext db, CancellationToken ct)
    {
        var rows = await db.PermissionGroups.AsNoTracking()
            .Where(g => g.Slug == SeededPermissionGroups.FullAdministratorsSlug
                || g.Slug == SeededPermissionGroups.AllUsersSlug)
            .Select(g => new { g.Id, g.Slug })
            .ToListAsync(ct);
        return (
            rows.FirstOrDefault(g => g.Slug == SeededPermissionGroups.FullAdministratorsSlug)?.Id,
            rows.FirstOrDefault(g => g.Slug == SeededPermissionGroups.AllUsersSlug)?.Id);
    }
}
