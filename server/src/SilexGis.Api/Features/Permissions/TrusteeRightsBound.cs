// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Permissions;

/// <summary>
/// The permission area's answer to the roster's bound question: the same entry-by-entry
/// comparison that bounds adding a trustee, run over every permission group the account would
/// newly reach through the caving groups it comes to sit in.
/// </summary>
public sealed class TrusteeRightsBound(SilexGisDbContext db) : ITrusteeRightsBound
{
    public async Task<bool> ExceedsCallerAsync(
        AccessContext caller,
        Guid? accountId,
        IReadOnlyCollection<Guid> caverIds,
        IReadOnlyCollection<Guid> cavingGroupIds,
        CancellationToken ct)
    {
        if (caller.IsFullAdmin || accountId is not { } account || (caverIds.Count == 0 && cavingGroupIds.Count == 0))
        {
            return false;
        }

        var viaEntries = caverIds.Count == 0
            ? []
            : await db.CavingGroupMemberships.AsNoTracking()
                .Where(m => caverIds.Contains(m.CaverId))
                .Select(m => m.CavingGroupId)
                .ToListAsync(ct);
        List<Guid> joined = [.. cavingGroupIds.Concat(viaEntries).Distinct()];
        if (joined.Count == 0)
        {
            return false;
        }

        // A ruleset the account already reaches — as a direct trustee, or through a caving group
        // its own person already sits in — gives it nothing new, whoever the caller is.
        var seatedIn = db.CavingGroupMemberships.AsNoTracking()
            .Where(m => db.Cavers.Any(c => c.Id == m.CaverId && c.UserId == account))
            .Select(m => m.CavingGroupId);
        var alreadyReached = db.PermissionGroupMembers.AsNoTracking()
            .Where(m => (m.MemberKind == AccessSubjectKind.User && m.MemberId == account)
                || (m.MemberKind == AccessSubjectKind.CavingGroup && seatedIn.Contains(m.MemberId)))
            .Select(m => m.PermissionGroupId);

        var reached = await db.PermissionGroupMembers.AsNoTracking()
            .Where(m => m.MemberKind == AccessSubjectKind.CavingGroup
                && joined.Contains(m.MemberId)
                && !alreadyReached.Contains(m.PermissionGroupId))
            .Select(m => new { m.PermissionGroupId, CavingGroupId = m.MemberId })
            .ToListAsync(ct);

        foreach (var ruleset in reached.GroupBy(r => r.PermissionGroupId))
        {
            // The clubs through which this ruleset is reached and whose roster the caller keeps:
            // rules over their own content are what membership of them means.
            var ownClubs = ruleset
                .Select(r => r.CavingGroupId)
                .Where(clubId => AccessEvaluator.Decide(
                    caller,
                    AccessDomain.CavingGroups,
                    AccessAction.Write,
                    new AccessTargetFacts { ObjectId = clubId }).Allowed)
                .ToHashSet();

            if (await PermissionGroupEndpoints.ExceedsOwnRightsAsync(
                    db,
                    caller,
                    ruleset.Key,
                    entry => entry.ScopeKind == AccessScopeKind.CavingGroup
                        && entry.ScopeId is { } clubId
                        && ownClubs.Contains(clubId),
                    ct))
            {
                return true;
            }
        }

        return false;
    }
}
