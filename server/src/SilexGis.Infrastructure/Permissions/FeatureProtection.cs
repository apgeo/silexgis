// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Permissions;

/// <summary>
/// Batch evaluation of the exact-location rule over the feature model. The decision
/// itself lives in Domain
/// (<see cref="LocationProtection.CanViewExactLocation(AccessContext?, Feature, IReadOnlyCollection{ProtectionRootGrant})"/>);
/// this class resolves each row's protected roots from its ancestor array — flat reads,
/// no recursion — asks the access walk which roots carry the caller's ViewExactLocation,
/// and applies the veto in memory. Its SQL twin is <see cref="AccessSql.ExactViewFragment"/>;
/// parity is pinned by tests — change them together or not at all.
/// </summary>
public sealed class FeatureProtection(SilexGisDbContext db, IAccessService access)
{
    /// <summary>
    /// Of the given candidate features, the ids whose exact location the caller may see.
    /// Unprotected features always qualify. Callers use the complement for
    /// snap/withhold/redaction decisions per their own policy.
    /// </summary>
    public async Task<HashSet<Guid>> ExactViewIdsAsync(
        AccessContext? ctx, IReadOnlyCollection<Guid> featureIds, CancellationToken ct = default)
    {
        var ids = featureIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await db.Features.AsNoTracking().IgnoreQueryFilters()
            .Where(f => ids.Contains(f.Id))
            .Select(f => new { f.Id, f.OwnerUserId, f.CavingGroupId, f.Visibility, f.IsProtectedEffective, f.AncestorIds })
            .ToListAsync(ct);

        // The fast path: nothing in the candidate set is protected.
        if (rows.All(r => !r.IsProtectedEffective))
        {
            return [.. rows.Select(r => r.Id)];
        }

        // Protected roots of all candidates in one read; the walk answers VEL per root.
        // Soft-deleted roots still veto — protection is most-restrictive until purge.
        var involvedAncestors = rows.SelectMany(r => r.AncestorIds).ToHashSet();
        var roots = await db.Features.AsNoTracking().IgnoreQueryFilters()
            .Where(f => involvedAncestors.Contains(f.Id) && f.LocationProtected)
            .ToDictionaryAsync(f => f.Id, ct);
        var grantedRootIds = ctx is null
            ? []
            : await access.ViewExactLocationRootIdsAsync(ctx, [.. roots.Keys], ct);

        var result = new HashSet<Guid>();
        foreach (var row in rows)
        {
            var protectedRoots = row.AncestorIds
                .Where(roots.ContainsKey)
                .Select(id => new ProtectionRootGrant(roots[id], grantedRootIds.Contains(id)))
                .ToList();

            // Row facsimile for the Domain rule (owner arm + veto over roots).
            var rowFeature = new Feature
            {
                Id = row.Id,
                OwnerUserId = row.OwnerUserId,
                CavingGroupId = row.CavingGroupId,
                Visibility = row.Visibility,
                IsProtectedEffective = row.IsProtectedEffective,
            };
            if (LocationProtection.CanViewExactLocation(ctx, rowFeature, protectedRoots))
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
        AccessContext? ctx, IReadOnlyCollection<Guid> targetFeatureIds, CancellationToken ct = default)
    {
        var ids = targetFeatureIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var exact = await ExactViewIdsAsync(ctx, ids, ct);
        return [.. ids.Where(id => !exact.Contains(id))];
    }

    /// <summary>Single-target variant for detail endpoints.</summary>
    public async Task<bool> ShouldRedactLinkAsync(
        AccessContext? ctx, Guid? targetFeatureId, CancellationToken ct = default)
    {
        if (targetFeatureId is null)
        {
            return false;
        }

        var redacted = await RedactedLinkTargetIdsAsync(ctx, [targetFeatureId.Value], ct);
        return redacted.Count > 0;
    }
}
