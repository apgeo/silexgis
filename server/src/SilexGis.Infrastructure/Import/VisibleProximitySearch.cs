// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Import;

/// <summary>
/// An existing object a candidate could be, with everything needed to say so.
/// </summary>
/// <param name="CaveFeatureId">
/// For an entrance, the cave it belongs to — which is what lets a candidate become a *second*
/// entrance of a cave already in the registry rather than a new one of its own.
/// </param>
public sealed record ProximityHit(
    Guid FeatureId,
    string? Name,
    FeatureKind Kind,
    Point Geom,
    Guid? CaveFeatureId,
    string? CaveName);

/// <summary>
/// "What is already here?", asked safely.
///
/// <para>
/// This is the single home of a rule both importers depend on, and it is a rule rather than a
/// query because the answer is a location oracle. "There is already something within twelve
/// metres of this point" <em>is</em> a position: anyone able to upload a waypoint or a
/// photograph near a protected entrance could find it in a few rounds of asking. So the search
/// runs over what the caller may <em>read</em>, and then only over the subset whose exact
/// position they may already <em>see</em> — a protected entrance they can read but not place is
/// dropped from the comparison entirely.
/// </para>
/// <para>
/// The cost of that is real and is accepted rather than hidden: importing beside such an
/// entrance will offer to create a duplicate, because the honest alternative is answering a
/// position to somebody who may not have one. Callers shape their own answer from this pool —
/// the nearest single object, or the few nearest — but none of them re-derives who may be in it.
/// </para>
/// </summary>
public sealed class VisibleProximitySearch(SilexGisDbContext db)
{
    /// <summary>Kinds a candidate is ever compared against: the things that are places.</summary>
    private static readonly FeatureKind[] ComparableKinds = [FeatureKind.CaveEntrance, FeatureKind.Generic];

    /// <summary>
    /// A ceiling on the read: the most rows the pool takes for one candidate, and, shared out, for a
    /// page of them. A candidate with a whole massif inside its radius could otherwise sweep in
    /// every feature an installation has; its nearest neighbours still survive the cut, because
    /// each candidate's rows are ordered by distance from that candidate before they are cut.
    /// </summary>
    public const int MaxNearbyFeatures = 2000;

    /// <summary>
    /// The fewest rows any one candidate is given however many share the ceiling: more than any
    /// caller shows for one candidate, with room for the rows an upload has just written beside it,
    /// which the sync report leaves out after the cut.
    /// </summary>
    public const int MinNearbyPerCandidate = 16;

    /// <summary>
    /// The objects near any of the given points that this caller may both read and place: for each
    /// point, the nearest ones within the radius, found through the geometry index from that point.
    /// </summary>
    public async Task<IReadOnlyList<ProximityHit>> NearAsync(
        IReadOnlyCollection<Point> points,
        double radiusMeters,
        AccessContext ctx,
        CancellationToken ct = default)
    {
        if (points.Count == 0 || radiusMeters <= 0)
        {
            return [];
        }

        // Each candidate is ranked from itself, not from the middle of the page: the points of one
        // file or one trip can lie at both ends of a county, and a pool ordered around the middle
        // and cut once would spend its ceiling on what lies between them while the entrance a
        // metre from one end — the duplicate this exists to find — was cut. Silence is this
        // answer's failure mode, which is why the order is not an optimisation.
        //
        // Who may be in the pool is decided inside the same statement, before each candidate's
        // order and cut, with the caller's own access fragments: a protected entrance the caller
        // may read but not place, dropped only after the cut, would have taken a place and moved
        // which placeable row survived as its true position did.
        var candidates = points.Select(p => p.Coordinate).Distinct().ToList();
        var perCandidate = Math.Max(MinNearbyPerCandidate, MaxNearbyFeatures / candidates.Count);
        var nearestIds = await ProximitySql.NearestPlaceableIdsAsync(
            db, ctx, candidates, radiusMeters, ComparableKinds, perCandidate, ct);
        var rank = nearestIds.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        var nearby = (await db.Features.AsNoTracking()
                .Where(f => nearestIds.Contains(f.Id))
                .Select(f => new { f.Id, f.Name, f.Kind, f.Geom })
                .ToListAsync(ct))
            .OrderBy(f => rank[f.Id])
            .ToList();
        var comparable = nearby.Where(f => f.Geom is Point).ToList();
        if (comparable.Count == 0)
        {
            return [];
        }

        // An entrance answers with its cave as well: recognising a candidate as something
        // already there is one answer, and offering to add it to the cave it belongs to is the
        // other, and both need the cave.
        var comparableIds = comparable.Select(f => f.Id).ToList();
        var cavesByEntrance = await db.CaveEntrances.AsNoTracking()
            .Where(e => comparableIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.CaveFeatureId, ct);
        var caveNames = await CaveNamesAsync(cavesByEntrance.Values, ct);

        return
        [
            .. comparable.Select(f =>
            {
                var caveId = cavesByEntrance.TryGetValue(f.Id, out var owner) ? owner : (Guid?)null;
                return new ProximityHit(
                    f.Id,
                    f.Name,
                    f.Kind,
                    (Point)f.Geom!,
                    caveId,
                    caveId is { } id ? caveNames.GetValueOrDefault(id) : null);
            })
        ];
    }

    private async Task<Dictionary<Guid, string?>> CaveNamesAsync(
        IEnumerable<Guid> caveIds, CancellationToken ct)
    {
        var ids = caveIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Features.AsNoTracking()
            .Where(f => ids.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.Name, ct);
    }
}
