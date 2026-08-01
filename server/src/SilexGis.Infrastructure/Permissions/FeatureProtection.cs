// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Permissions;

/// <summary>
/// Batch evaluation of the exact-location rule over the feature model, replacing the old
/// cave-hardcoded helpers. The decision itself lives in Domain
/// (<see cref="LocationProtection.CanViewExactLocation(UserContext?, Feature, IReadOnlyCollection{ProtectionRootGrant})"/>);
/// this class resolves each row's protected roots from its ancestor array — flat reads,
/// no recursion — loads the caller's grants once, and applies the rule in memory.
/// Its SQL twin is <see cref="PermissionSql.ExactViewFragment"/>; parity is pinned by
/// tests — change them together or not at all.
/// </summary>
public sealed class FeatureProtection(SilexGisDbContext db, AclPermissionService acl)
{
    /// <summary>
    /// Of the given candidate features, the ids whose exact location the caller may see.
    /// Unprotected features always qualify. Callers use the complement for
    /// snap/withhold/redaction decisions per their own policy.
    /// </summary>
    public async Task<HashSet<Guid>> ExactViewIdsAsync(
        UserContext? user, IReadOnlyCollection<Guid> featureIds, CancellationToken ct = default)
    {
        var ids = featureIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await db.Features.AsNoTracking().IgnoreQueryFilters()
            .Where(f => ids.Contains(f.Id))
            .Select(f => new { f.Id, f.OwnerUserId, f.TeamId, f.Visibility, f.IsProtectedEffective, f.AncestorIds })
            .ToListAsync(ct);

        // The fast path: nothing in the candidate set is protected.
        if (rows.All(r => !r.IsProtectedEffective))
        {
            return [.. rows.Select(r => r.Id)];
        }

        // Protected roots of all candidates in one read, then per-row evaluation.
        var involvedAncestors = rows.SelectMany(r => r.AncestorIds).ToHashSet();
        var roots = await db.Features.AsNoTracking().IgnoreQueryFilters()
            .Where(f => involvedAncestors.Contains(f.Id) && f.LocationProtected)
            .ToDictionaryAsync(f => f.Id, ct);
        var grants = user is null
            ? []
            : await acl.ExactLocationGrantFeatureIdsAsync(user, ct);

        var result = new HashSet<Guid>();
        foreach (var row in rows)
        {
            var protectedRoots = row.AncestorIds
                .Where(roots.ContainsKey)
                .Select(id => new ProtectionRootGrant(
                    roots[id],
                    grants.Contains(id) ? ObjectPermission.ViewExactLocation : ObjectPermission.None))
                .ToList();

            // Row facsimile for the Domain rule (owner arm + veto over roots).
            var rowFeature = new Feature
            {
                Id = row.Id,
                OwnerUserId = row.OwnerUserId,
                TeamId = row.TeamId,
                Visibility = row.Visibility,
                IsProtectedEffective = row.IsProtectedEffective,
            };
            if (LocationProtection.CanViewExactLocation(user, rowFeature, protectedRoots))
            {
                result.Add(row.Id);
            }
        }

        return result;
    }

    /// <summary>
    /// Of the given LINK TARGET ids, the ones whose reference must be hidden from the
    /// caller: a locating link on a record with exact coordinates discloses a protected
    /// target's position by proximity. A target is redacted when the caller lacks exact
    /// view on it — the same rule as the coordinates themselves.
    /// </summary>
    public async Task<HashSet<Guid>> RedactedLinkTargetIdsAsync(
        UserContext? user, IReadOnlyCollection<Guid> targetFeatureIds, CancellationToken ct = default)
    {
        var ids = targetFeatureIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var exact = await ExactViewIdsAsync(user, ids, ct);
        return [.. ids.Where(id => !exact.Contains(id))];
    }

    /// <summary>Single-target variant for detail endpoints.</summary>
    public async Task<bool> ShouldRedactLinkAsync(
        UserContext? user, Guid? targetFeatureId, CancellationToken ct = default)
    {
        if (targetFeatureId is null)
        {
            return false;
        }

        var redacted = await RedactedLinkTargetIdsAsync(user, [targetFeatureId.Value], ct);
        return redacted.Count > 0;
    }
}
