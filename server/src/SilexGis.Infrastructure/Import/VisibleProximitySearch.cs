// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
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
public sealed class VisibleProximitySearch(SilexGisDbContext db, FeatureProtection protection)
{
    /// <summary>Kinds a candidate is ever compared against: the things that are places.</summary>
    private static readonly FeatureKind[] ComparableKinds = [FeatureKind.CaveEntrance, FeatureKind.Generic];

    /// <summary>
    /// A ceiling on the read. A page of candidates spread over a whole massif with a
    /// five-kilometre radius could otherwise sweep in every feature an installation has; the
    /// nearest neighbours of each candidate are still found among the rows nearest the page.
    /// </summary>
    public const int MaxNearbyFeatures = 2000;

    /// <summary>
    /// The objects near any of the given points that this caller may both read and place. One
    /// envelope over all of them, grown by the radius: the points come from one file or one
    /// trip, so they are near each other by construction, and this turns a proximity query per
    /// candidate into a single indexed read.
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

        var envelope = new Envelope();
        foreach (var point in points)
        {
            envelope.ExpandToInclude(point.EnvelopeInternal);
        }

        var search = new GeometryFactory(new PrecisionModel(), 4326)
            .ToGeometry(Geodesy.ExpandedBy(envelope, radiusMeters));

        var nearby = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => f.Geom != null && ComparableKinds.Contains(f.Kind) && f.Geom.Intersects(search))
            .Select(f => new { f.Id, f.Name, f.Kind, f.Geom })
            .Take(MaxNearbyFeatures)
            .ToListAsync(ct);
        if (nearby.Count == 0)
        {
            return [];
        }

        var exact = await protection.ExactViewIdsAsync(ctx, [.. nearby.Select(f => f.Id)], ct);
        var comparable = nearby
            .Where(f => exact.Contains(f.Id) && f.Geom is Point)
            .ToList();
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
