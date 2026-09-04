// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>
/// Which areas a set of trips reached, and which features an area filter reaches into.
/// </summary>
/// <remarks>
/// <para>
/// One home for a question three surfaces ask — the narrowing, the count beside an area option and
/// the slice an area grouping cuts. They must agree: an option saying twelve over a page of four
/// tells the reader they are being shown less than they may see, and for a caller who genuinely
/// may not see everything that is the one sentence this application must not say by accident.
/// Written out three times the walk drifted, and did: an area was counted over the trips naming it
/// directly while selecting it also matched every trip that named something inside it.
/// </para>
/// <para>
/// The walk is the containment hierarchy in both directions. A trip that named a sub-area reached
/// the massif above it, so the massif's count includes it and the massif's filter returns it;
/// a massif nobody ever named directly is still offered, because trips reached it.
/// </para>
/// <para>
/// Two gates sit on every feature a naming passes through, in the order the trip's cave list
/// applies them and for the same reasons. The first is readability: naming a feature is a read of
/// it, and a trip's audience is not the feature's. The second is placement: a trip carries its own
/// exact geometry, so "this trip reached that place" positions a guarded place by proximity even
/// when the place itself is perfectly readable. Both gates apply to the named feature and again to
/// every area it reaches, so a guarded massif is neither counted, named, nor usable as a
/// narrowing — and a cave inside an area cannot be asked about through the area door with the
/// position check missing.
/// </para>
/// </remarks>
internal sealed class TripAreaReach
{
    public static readonly TripAreaReach Empty = new(
        new Dictionary<Guid, string?>(), new Dictionary<Guid, IReadOnlyList<Guid>>());

    private TripAreaReach(
        IReadOnlyDictionary<Guid, string?> names,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> areasOfTrip)
    {
        Names = names;
        AreasOfTrip = areasOfTrip;
    }

    /// <summary>Every area the trips reached that this caller may be told about, by its name.</summary>
    public IReadOnlyDictionary<Guid, string?> Names { get; }

    /// <summary>The areas each trip reached, deduplicated — a trip counts into each of them once.</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> AreasOfTrip { get; }

    /// <summary>How many of the trips reached each area, longest first.</summary>
    public IReadOnlyList<(Guid AreaId, int Count)> Counts() =>
        [.. AreasOfTrip
            .SelectMany(entry => entry.Value)
            .GroupBy(areaId => areaId)
            .Select(group => (AreaId: group.Key, Count: group.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.AreaId)];

    /// <summary>
    /// The areas a set of trips reached. The trips arrive as a query rather than as a list of ids
    /// so the caller's audience walk and every narrowing already in it stay inside the statement.
    /// </summary>
    public static async Task<TripAreaReach> BuildAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        IQueryable<Guid> tripIds,
        CancellationToken ct)
    {
        // Reduced by the database: a role names a feature once per link and per role, so a trip
        // that named one place under two roles would otherwise be two trips that went there.
        var pairs = await TripRoleLinks.PairsIn(db, tripIds).Distinct().ToListAsync(ct);
        if (pairs.Count == 0)
        {
            return Empty;
        }

        var namedIds = pairs.Select(pair => pair.FeatureId).Distinct().ToList();
        var reachOf = await GatedAncestorsAsync(db, protection, ctx, namedIds, ct);

        // Every area those namings sit inside, itself gated: the hierarchy is a cache of ids and
        // says nothing about who may read the rows it names.
        var names = await GatedAreaNamesAsync(
            db, protection, ctx, [.. reachOf.Values.SelectMany(ancestors => ancestors).Distinct()], ct);

        var areasOfTrip = pairs
            .Where(pair => reachOf.ContainsKey(pair.FeatureId))
            .SelectMany(pair => reachOf[pair.FeatureId]
                .Where(names.ContainsKey)
                .Select(areaId => (pair.TripId, AreaId: areaId)))
            .Distinct()
            .GroupBy(x => x.TripId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Guid>)[.. group.Select(x => x.AreaId)]);

        return new TripAreaReach(names, areasOfTrip);
    }

    /// <summary>
    /// The features a narrowing by one area matches on: the area and everything inside it that
    /// this caller may be told a trip reached. Null when the area itself is not one of them, which
    /// the listing answers as an empty page rather than as a refusal.
    /// </summary>
    public static async Task<IReadOnlyCollection<Guid>?> WithinAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        Guid areaId,
        CancellationToken ct)
    {
        // The kind is part of the gate rather than a hint: a cave asked about here would be the
        // cave question with the position check missing.
        var readableArea = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .AnyAsync(f => f.Id == areaId && f.Kind == FeatureKind.Generic && f.DeletedAt == null, ct);
        if (!readableArea)
        {
            return null;
        }

        // Asked over what survived the audience walk, never before it: the placement rule cannot
        // be expressed in the same statement, and asking it first would let an unreadable place
        // through on the strength of having no position to guard.
        if ((await protection.RedactedLinkTargetIdsAsync(ctx, [areaId], ct)).Count > 0)
        {
            return null;
        }

        // Narrowed to what something is linked to at all, so a massif holding thousands of caves
        // is not walked in full to answer a question only its named members can affect. Wider than
        // the trip roles on purpose — the role check is applied by the caller that uses this set,
        // and a narrower pre-cut here would be a second place for the two to disagree.
        var linked = db.ResLinkMembers.AsNoTracking()
            .Where(m => m.FeatureId != null)
            .Select(m => m.FeatureId!.Value);
        var inside = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => f.AncestorIds.Contains(areaId) && f.DeletedAt == null && linked.Contains(f.Id))
            .Select(f => f.Id)
            .ToListAsync(ct);

        var redacted = await protection.RedactedLinkTargetIdsAsync(ctx, inside, ct);
        // The area itself belongs to the set whether or not anything is linked to it, so a trip
        // naming the massif directly is matched by a filter on the massif.
        return [.. inside.Where(id => !redacted.Contains(id)).Append(areaId).Distinct()];
    }

    /// <summary>
    /// Of the given named features, the ones this caller may be told a trip reached, each with the
    /// containment chain it sits in — its own id included, because the hierarchy cache holds it.
    /// </summary>
    private static async Task<Dictionary<Guid, Guid[]>> GatedAncestorsAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        IReadOnlyCollection<Guid> featureIds,
        CancellationToken ct)
    {
        var readable = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => featureIds.Contains(f.Id) && f.DeletedAt == null)
            .Select(f => new { f.Id, f.AncestorIds })
            .ToListAsync(ct);

        var redacted = await protection.RedactedLinkTargetIdsAsync(ctx, [.. readable.Select(f => f.Id)], ct);
        return readable
            .Where(f => !redacted.Contains(f.Id))
            .ToDictionary(f => f.Id, f => f.AncestorIds.Append(f.Id).Distinct().ToArray());
    }

    /// <summary>Of the given candidates, the areas this caller may be told about, by name.</summary>
    private static async Task<Dictionary<Guid, string?>> GatedAreaNamesAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        IReadOnlyCollection<Guid> candidateIds,
        CancellationToken ct)
    {
        if (candidateIds.Count == 0)
        {
            return [];
        }

        var readable = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => candidateIds.Contains(f.Id) && f.Kind == FeatureKind.Generic && f.DeletedAt == null)
            .Select(f => new { f.Id, f.Name })
            .ToListAsync(ct);

        var redacted = await protection.RedactedLinkTargetIdsAsync(ctx, [.. readable.Select(f => f.Id)], ct);
        return readable
            .Where(f => !redacted.Contains(f.Id))
            .ToDictionary(f => f.Id, f => f.Name);
    }
}
