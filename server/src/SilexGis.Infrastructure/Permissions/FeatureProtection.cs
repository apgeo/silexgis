// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
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
    /// Of the given features, the ones with no protection root anywhere above them — self
    /// included — resolved from the rows' own <c>location_protected</c> and their ancestor
    /// arrays, deliberately WITHOUT consulting the derived <c>is_protected_effective</c>
    /// column. No caller: this is the structural question about the feature alone.
    /// </summary>
    /// <remarks>
    /// For the surfaces that must not lean on the derived column by itself.
    /// <see cref="ExactViewIdsAsync"/> answers a caller's question and takes a fast path off
    /// that column whenever nothing in the candidate set is marked protected — the right trade
    /// for a map read, and the wrong one for a decision whose entire purpose is to hold when
    /// the column has gone stale, because there the fast path would agree with the column by
    /// construction rather than check it. Soft-deleted rows are read here (filters ignored) so
    /// a deleted protected root still refuses; protection is most-restrictive until purge.
    /// </remarks>
    /// <summary>
    /// Of a batch of rows, the ids the caller may place exactly — given each row's stored
    /// protection flag, so the expensive walk runs only over the rows that could fail it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The narrowing is an optimisation and never the verdict, and the distinction is the whole
    /// reason this has a home: the flag is a cheap column that says a row <i>might</i> be
    /// withheld, and the walk is what decides whether it is. Re-admitting on the walk's answer
    /// alone would drop every unprotected row, because the walk was never asked about them; the
    /// re-admission therefore consults the flag again. Written out per route, that subtlety is one
    /// inverted condition away from either leaking every protected row or withholding every
    /// ordinary one, and both look plausible in a diff.
    /// </para>
    /// <para>
    /// Rows are described by a flag and an id rather than by an entity, so a caller can pass a
    /// projection it has already narrowed rather than loading features it does not otherwise want.
    /// </para>
    /// </remarks>
    public async Task<HashSet<Guid>> PlaceableIdsAsync(
        AccessContext? ctx,
        IReadOnlyCollection<(Guid Id, bool IsProtected)> rows,
        CancellationToken ct = default)
    {
        var walked = await ExactViewIdsAsync(
            ctx, [.. rows.Where(r => r.IsProtected).Select(r => r.Id)], ct);

        return [.. rows.Where(r => !r.IsProtected || walked.Contains(r.Id)).Select(r => r.Id)];
    }

    /// <summary>
    /// Whether a caller may scope a question by this feature — the same rule as
    /// <see cref="ScopeGeometryAsync"/>, for the callers that need the answer and not the shape.
    /// </summary>
    /// <remarks>
    /// Separate so a route measuring <i>against</i> an outline does not load the outline in order
    /// to throw it away: a karst area is a polygon of real size, and the two routes that only need
    /// permission would otherwise fetch one per request for nothing. The rule itself is not
    /// repeated — both ask the same two questions in the same order and answer alike.
    /// </remarks>
    public async Task<bool> MayScopeByAsync(
        AccessContext ctx, Guid featureId, CancellationToken ct = default)
    {
        var readable = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .AnyAsync(f => f.Id == featureId, ct);

        return readable && (await ExactViewIdsAsync(ctx, [featureId], ct)).Contains(featureId);
    }

    /// <summary>
    /// The geometry of a feature a caller may use to scope a question by place, or null when they
    /// may not — which covers a feature that does not exist, one they cannot read, and one they can
    /// read but cannot place exactly.
    ///
    /// <para>
    /// The three are one answer deliberately, and the answer is the same words in every case: an
    /// outline scopes a question by where it is, so admitting one the caller cannot place would
    /// draw its edges for them a cell or a count at a time, and a refusal that distinguished "no
    /// such outline" from "not yours to place" would say which outlines are protected. That
    /// uniformity is the whole property, which is why it is decided here rather than in each route
    /// that takes an area: four copies of a refusal ladder is four chances for one to answer
    /// differently, and nothing fails when one does.
    /// </para>
    /// <para>
    /// The geometry is returned rather than a yes: every caller needs it next, and a second query
    /// for a row just fetched is both waste and a second place for the two to disagree about which
    /// row they meant.
    /// </para>
    /// </summary>
    public async Task<Geometry?> ScopeGeometryAsync(
        AccessContext ctx, Guid featureId, CancellationToken ct = default)
    {
        // Projected rather than selected bare so that a readable outline carrying no geometry is
        // distinguishable here from one the caller cannot read at all — the caller decides what to
        // say about each, and they are not the same thing.
        var found = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => f.Id == featureId)
            .Select(f => new { f.Geom })
            .FirstOrDefaultAsync(ct);

        if (found is null)
        {
            return null;
        }

        return (await ExactViewIdsAsync(ctx, [featureId], ct)).Contains(featureId)
            ? found.Geom
            : null;
    }

    public async Task<HashSet<Guid>> UnprotectedByAncestryIdsAsync(
        IReadOnlyCollection<Guid> featureIds, CancellationToken ct = default)
    {
        var ids = featureIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await db.Features.AsNoTracking().IgnoreQueryFilters()
            .Where(f => ids.Contains(f.Id))
            .Select(f => new { f.Id, f.LocationProtected, f.AncestorIds })
            .ToListAsync(ct);

        var involvedAncestors = rows.SelectMany(r => r.AncestorIds).ToHashSet();
        var protectedRootIds = involvedAncestors.Count == 0
            ? []
            : (await db.Features.AsNoTracking().IgnoreQueryFilters()
                .Where(f => involvedAncestors.Contains(f.Id) && f.LocationProtected)
                .Select(f => f.Id)
                .ToListAsync(ct)).ToHashSet();

        // The row's own flag as well as the walk: ancestor_ids carries self, but it is itself
        // derived, and a rule that has to survive one derived column being wrong must not be
        // written so that another one silently carries it.
        return
        [
            .. rows
                .Where(r => !r.LocationProtected && !r.AncestorIds.Any(protectedRootIds.Contains))
                .Select(r => r.Id),
        ];
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
