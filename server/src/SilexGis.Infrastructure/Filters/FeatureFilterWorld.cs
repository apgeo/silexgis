// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Filters;

/// <summary>
/// Caves, entrances, centrelines and everything else on the map, as a filterable world.
/// </summary>
public sealed class FeatureFilterWorld(
    SilexGisDbContext db, FeatureProtection protection, LocationProtectionGrid grid)
    : FilterWorld<Feature>
{
    public const string Key = "feature";

    public override string World => Key;

    /// <summary>
    /// What a feature can be asked about: the fields every installation has, plus the typed
    /// properties this installation's feature types declare — only for a caller who may read the
    /// feature types at all.
    /// </summary>
    /// <remarks>
    /// The schemas are what <c>GET /feature-types</c> already returns to the same readers, so a
    /// property field offered here names nothing that caller could not have read for themselves; a
    /// caller refused that list is offered no property, and a condition naming one reads to them
    /// exactly like a field that does not exist. No schema keyword marks a property as withheld
    /// today, and every feature answer that carries a row carries its whole property document, so
    /// there is nothing a filter could reveal about a visible row that reading it would not.
    /// </remarks>
    public override async ValueTask<WorldVocabulary> VocabularyAsync(
        AccessContext caller, CancellationToken ct)
    {
        if (!AccessEvaluator.Decide(caller, AccessDomain.Taxonomies, AccessAction.Read, null).Allowed)
        {
            return FeatureFilterFields.Vocabulary;
        }

        var types = await db.FeatureTypes.AsNoTracking()
            .Where(t => t.PropertiesSchema != null)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Code)
            .Select(t => new FeatureTypeSchema(t.Id, t.Code, t.PropertiesSchema))
            .ToListAsync(ct);

        return FeaturePropertyFields.Fold(FeatureFilterFields.Vocabulary, types);
    }

    /// <summary>
    /// What this caller may see: the visibility walk, less any protected centreline they may not
    /// place exactly.
    /// </summary>
    /// <remarks>
    /// The second half is not a refinement of the first. A centreline traces a cave's course
    /// underground, so for somebody without exact view the row is withheld altogether — not shown
    /// without its geometry, not counted, not hinted at by a total that moves. The feature list has
    /// always done this; leaving it out here would have made the filter a second way to ask the
    /// same question with a more generous answer, which is the exact failure this whole design is
    /// arranged to prevent.
    /// </remarks>
    protected override async ValueTask<IQueryable<Feature>> VisibleAsync(
        AccessContext caller, CancellationToken ct)
    {
        var visible = db.Features.AsNoTracking().VisibleTo(caller, db.Features, db.FeatureSetMembers);

        var protectedCenterlineIds = await visible
            .Where(f => f.Kind == FeatureKind.Centerline && f.IsProtectedEffective)
            .Select(f => f.Id)
            .ToListAsync(ct);
        if (protectedCenterlineIds.Count == 0)
        {
            return visible;
        }

        var exact = await protection.ExactViewIdsAsync(caller, protectedCenterlineIds, ct);
        var withheld = protectedCenterlineIds.Where(id => !exact.Contains(id)).ToArray();

        return withheld.Length == 0 ? visible : visible.Where(f => !withheld.Contains(f.Id));
    }

    /// <remarks>
    /// An unnarrowed question visits every row the caller may read, twice — once to count and once
    /// to take the page — and probes each row's ancestors for the visibility cascade; the planner
    /// prices that so high for a reader who owns nothing that it compiles both statements, which
    /// costs more than the page it saves time on.
    /// </remarks>
    protected override async ValueTask<IAsyncDisposable> ReadScopeAsync(CancellationToken ct) =>
        await PlannerSql.WithoutJitAsync(db.Database, ct);

    protected override Expression<Func<Feature, bool>> HasId(IReadOnlyCollection<Guid> ids) =>
        f => ids.Contains(f.Id);

    protected override async ValueTask<Expression<Func<Feature, bool>>> CompileAsync(
        WorldQuery query, CancellationToken ct) =>
        new FeatureFilterCompiler(db).Compile(
            query.Where, await ResolveSpatialAsync(query.Caller, query.Where, mayNest: true, ct));

    // ---------- spatial conditions ----------

    /// <summary>
    /// Every spatial condition in the tree, resolved to the ids of the rows that answer it for this
    /// caller. The anchor of each is gated here — readable and exactly placeable, or refused — and
    /// membership is decided on shown positions by <see cref="SpatialConditionSql"/>.
    /// </summary>
    /// <param name="mayNest">
    /// False inside another saved filter measured from: that one may not measure from a third, so a
    /// chain of filters cannot be made into a loop or a cost nobody can see the end of.
    /// </param>
    private async Task<Dictionary<ConditionNode, ResolvedSpatial>?> ResolveSpatialAsync(
        AccessContext caller, FilterNode? where, bool mayNest, CancellationToken ct)
    {
        Dictionary<ConditionNode, ResolvedSpatial>? resolved = null;
        foreach (var condition in SpatialConditionsOf(where))
        {
            resolved ??= [];
            if (resolved.ContainsKey(condition))
            {
                continue;
            }

            var ask = await AskAsync(caller, condition, mayNest, ct);
            resolved[condition] = new ResolvedSpatial(
                ask is null ? [] : await SpatialConditionSql.MatchingAsync(db, caller, ask, grid.Meters, ct));
        }

        return resolved;
    }

    private static IEnumerable<ConditionNode> SpatialConditionsOf(FilterNode? node)
    {
        if (node is ConditionNode { Field: FeatureFilterFields.Position } condition)
        {
            yield return condition;
            yield break;
        }

        if (node is null)
        {
            yield break;
        }

        foreach (var child in node.Children)
        {
            foreach (var found in SpatialConditionsOf(child))
            {
                yield return found;
            }
        }
    }

    /// <summary>The question with its anchor resolved, or null when it can match no row at all.</summary>
    private async Task<SpatialAsk?> AskAsync(
        AccessContext caller, ConditionNode condition, bool mayNest, CancellationToken ct)
    {
        switch (condition.Op, condition.Values)
        {
            case (FilterOp.Within, [NumberValue radius, PointValue point]):
                return new WithinGeometry(
                    new Point(point.Longitude, point.Latitude) { SRID = 4326 }, radius.Value);

            case (FilterOp.Within, [NumberValue radius, ObjectValue target]):
            {
                var scoped = await ScopedAnchorAsync(caller, target.Id, ct);
                if (scoped.Kind == FeatureKind.Cave)
                {
                    var entrances = await PlaceableEntrancesAsync(caller, [target.Id], ct);
                    return entrances.Count == 0 ? null : await WithinRowsAsync(entrances, radius.Value, ct);
                }

                return scoped.Geometry is { } anchor
                    ? new WithinGeometry(anchor, radius.Value)
                    : throw AnchorNotFound();
            }

            case (FilterOp.Within, [NumberValue radius, SavedFilterValue saved]):
            {
                if (!mayNest)
                {
                    throw new FilterRefusedException(
                        SpatialConditions.AnchorNestedCode,
                        "A saved filter measured from may not itself measure from a saved filter.");
                }

                var rows = await SavedFilterAnchorRowsAsync(caller, saved.Id, ct);
                return rows.Count == 0 ? null : await WithinRowsAsync(rows, radius.Value, ct);
            }

            case (FilterOp.Inside, [PolygonValue polygon]):
                // Validation has already proved the outline builds.
                return new InsideOutline(SpatialConditions.ToPolygon(polygon)!);

            case (FilterOp.Inside, [ObjectValue area]):
            {
                // The study-area ladder: placement first, in the words of a missing object, and
                // only then whether it encloses any ground.
                var scoped = await protection.ScopeOutlineAsync(caller, area.Id, ct) ?? throw AnchorNotFound();
                return scoped.Outline is { } outline
                    ? new InsideOutline(outline)
                    : throw new FilterRefusedException(
                        SpatialConditions.AnchorNotAnAreaCode,
                        "Only an outline encloses anything; that object is not one.");
            }

            default:
                throw new InvalidOperationException(
                    $"No spatial arm for {condition.Op} over {condition.Values.Count} value(s). A "
                    + "condition the validator admits must have one here.");
        }
    }

    // The placement gate for an object anchor is the one every place-scoped question uses: a
    // missing object, one the caller cannot read and one they cannot place exactly are one refusal.
    private async Task<ScopedFeature> ScopedAnchorAsync(AccessContext caller, Guid id, CancellationToken ct) =>
        await protection.ScopeFeatureAsync(caller, id, ct) ?? throw AnchorNotFound();

    private static FilterRefusedException AnchorNotFound() =>
        new(SpatialConditions.AnchorNotFoundCode, SpatialConditions.AnchorNotFoundDetail);

    /// <summary>
    /// The rows of another saved filter's result that a distance may be measured from: run under
    /// this caller's own access, as if they had run it themselves, and narrowed to the rows they may
    /// place exactly — a cave by its entrances. A filter they cannot see or cannot read is refused in
    /// the same words as a missing object.
    /// </summary>
    private async Task<IReadOnlyCollection<Guid>> SavedFilterAnchorRowsAsync(
        AccessContext caller, Guid savedFilterId, CancellationToken ct)
    {
        var row = await SavedFilterReader.VisibleRows(db, caller)
            .FirstOrDefaultAsync(f => f.Id == savedFilterId, ct);
        var scope = row is null ? null
            : FilterDocument.Deserialize(row.Document) is { } document ? SavedFilterRules.ScopeFor(document, Key)
            : null;
        if (scope is null)
        {
            throw AnchorNotFound();
        }

        var vocabulary = await VocabularyAsync(caller, ct);
        var inner = new FilterDocument { Scope = [scope] };
        if (!SavedFilterRules.IsReadableBy(inner, new Dictionary<string, WorldVocabulary> { [Key] = vocabulary }))
        {
            throw AnchorNotFound();
        }

        if (SpatialConditions.MeasuresFromSavedFilter(scope.Where))
        {
            throw new FilterRefusedException(
                SpatialConditions.AnchorNestedCode,
                "A saved filter measured from may not itself measure from a saved filter.");
        }

        var predicate = new FeatureFilterCompiler(db).Compile(
            scope.Where, await ResolveSpatialAsync(caller, scope.Where, mayNest: false, ct));
        var matching = (await VisibleAsync(caller, ct)).Where(predicate);

        var placed = await matching.Where(f => f.Geom != null).Select(f => f.Id).ToListAsync(ct);
        var caves = await matching.Where(f => f.Kind == FeatureKind.Cave).Select(f => f.Id).ToListAsync(ct);

        var anchors = new HashSet<Guid>(await protection.ExactViewIdsAsync(caller, placed, ct));
        if (caves.Count > 0)
        {
            anchors.UnionWith(await PlaceableEntrancesAsync(caller, caves, ct));
        }

        if (anchors.Count > SpatialConditions.MaxAnchorRows)
        {
            throw new FilterRefusedException(
                SpatialConditions.AnchorTooLargeCode,
                $"A distance can be measured from at most {SpatialConditions.MaxAnchorRows} objects.");
        }

        return anchors;
    }

    /// <summary>The live entrances of these caves that the caller may read and place exactly.</summary>
    private async Task<IReadOnlyCollection<Guid>> PlaceableEntrancesAsync(
        AccessContext caller, IReadOnlyCollection<Guid> caveIds, CancellationToken ct)
    {
        var visible = await VisibleAsync(caller, ct);
        var entrances = await db.CaveEntrances.AsNoTracking()
            .Where(e => caveIds.Contains(e.CaveFeatureId))
            .Select(e => e.Id)
            .Where(id => visible.Any(f => f.Id == id && f.Geom != null))
            .ToListAsync(ct);
        return entrances.Count == 0 ? [] : await protection.ExactViewIdsAsync(caller, entrances, ct);
    }

    private async Task<WithinRows> WithinRowsAsync(
        IReadOnlyCollection<Guid> anchors, double radiusMeters, CancellationToken ct)
    {
        var geometries = await db.Features.AsNoTracking()
            .Where(f => anchors.Contains(f.Id) && f.Geom != null)
            .Select(f => f.Geom!)
            .ToListAsync(ct);
        var extent = new Envelope();
        foreach (var geometry in geometries)
        {
            extent.ExpandToInclude(geometry.EnvelopeInternal);
        }

        return new WithinRows(anchors, extent, radiusMeters);
    }

    // ---------- the proximity sort ----------

    /// <summary>
    /// Nearest the anchor first, over the rows this caller may place exactly; every other row
    /// after them, newest first. Without an anchor — only an internal caller can ask that, since a
    /// request is refused first — the fallback order alone.
    /// </summary>
    /// <remarks>
    /// The anchor is resolved here, under the caller's own access, through the gate a spatial
    /// condition's object anchor goes through, so "nearest to that cave" and "within 500 m of that
    /// cave" cannot disagree about whether the caller may measure from it.
    /// </remarks>
    protected override async ValueTask<IQueryable<Feature>> OrderAsync(
        IQueryable<Feature> rows, WorldQuery query, CancellationToken ct)
    {
        if (query.Sort != SortKey.Proximity || query.Anchor is null)
        {
            return Order(rows, query);
        }

        var anchor = await ProximityAnchorAsync(query.Caller, query.Anchor, ct);

        // A page of the ranking needs only the rows nearest the anchor. When enough matching rows
        // lie within the reach of the page's nearest readable rows, the first rows of the ranking
        // are all among them, so the page is ranked from those alone, found through the geometry
        // index, instead of measuring every matching row and sorting them all. The count proves
        // it for this question's own rows; when it falls short, the whole ranking is taken.
        var wanted = query.Skip + query.Take;
        if (query.Take > 0
            && await ProximityOrderSql.ReachOfNearestAsync(db, query.Caller, anchor, wanted, ct) is { } reach)
        {
            var near = ProximityOrderSql.Ranks(db, query.Caller, anchor, reach);
            var inReach =
                from f in rows
                join r in near on f.Id equals r.Id
                select new { Feature = f, r.Distance };
            if (await inReach.Take(wanted).CountAsync(ct) == wanted)
            {
                return
                    from x in inReach
                    orderby x.Distance, x.Feature.UpdatedAt descending, x.Feature.Id descending
                    select x.Feature;
            }
        }

        var ranks = ProximityOrderSql.Ranks(db, query.Caller, anchor);

        // Ranked rows by distance, the rest after them in the fallback order; ties broken by the
        // fallback and then the id in every arm, so a page boundary never splits an equal pair
        // differently on two requests.
        return
            from f in rows
            join r in ranks on f.Id equals r.Id into ranked
            from r in ranked.DefaultIfEmpty()
            let distance = (double?)r.Distance
            orderby distance == null, distance, f.UpdatedAt descending, f.Id descending
            select f;
    }

    private async Task<Geometry> ProximityAnchorAsync(AccessContext caller, FilterAnchor anchor, CancellationToken ct)
    {
        switch (anchor)
        {
            case PointAnchor point:
                return new Point(point.Longitude, point.Latitude) { SRID = 4326 };

            case ObjectAnchor { World: Key } target:
            {
                var scoped = await ScopedAnchorAsync(caller, target.Id, ct);
                if (scoped.Kind != FeatureKind.Cave)
                {
                    return scoped.Geometry ?? throw AnchorNotFound();
                }

                // A cave is measured from by its entrances the caller may read and place exactly;
                // one with none is nothing to measure from, in the same words as a missing one.
                var entrances = await PlaceableEntrancesAsync(caller, [target.Id], ct);
                var points = (await db.Features.AsNoTracking()
                        .Where(f => entrances.Contains(f.Id) && f.Geom != null)
                        .Select(f => f.Geom!)
                        .ToListAsync(ct))
                    .OfType<Point>()
                    .ToArray();
                return points.Length == 0
                    ? throw AnchorNotFound()
                    : new MultiPoint(points) { SRID = 4326 };
            }

            default:
                throw AnchorNotFound();
        }
    }

    protected override IQueryable<Feature> Order(IQueryable<Feature> rows, WorldQuery query)
    {
        // Ties are broken by id in every arm. Without it a row can appear on two consecutive pages
        // and another on neither, because the database is free to return equal rows in any order
        // and does — which reads to somebody scrolling as results appearing and vanishing.
        var descending = query.Descending;
        return query.Sort switch
        {
            SortKey.Created => descending
                ? rows.OrderByDescending(f => f.CreatedAt).ThenByDescending(f => f.Id)
                : rows.OrderBy(f => f.CreatedAt).ThenBy(f => f.Id),
            SortKey.Title => descending
                ? rows.OrderByDescending(f => f.Name).ThenByDescending(f => f.Id)
                : rows.OrderBy(f => f.Name).ThenBy(f => f.Id),
            SortKey.Owner => descending
                ? rows.OrderByDescending(f => db.Users
                        .Where(u => u.Id == f.OwnerUserId).Select(u => u.DisplayName).FirstOrDefault())
                    .ThenByDescending(f => f.Id)
                : rows.OrderBy(f => db.Users
                        .Where(u => u.Id == f.OwnerUserId).Select(u => u.DisplayName).FirstOrDefault())
                    .ThenBy(f => f.Id),
            _ => descending
                ? NewestFirst(rows)
                : rows.OrderBy(f => f.UpdatedAt).ThenBy(f => f.Id),
        };
    }

    /// <summary>
    /// Most recently changed first, the id breaking ties — the default order of this world and of
    /// the feature list, and the one the feature table's index over those two columns serves.
    /// </summary>
    /// <remarks>
    /// Read off that index, a page is the first rows along it the caller may read, rather than
    /// every readable row visited, its ancestors probed for the visibility cascade, and all of them
    /// sorted so that a page of them can be kept.
    /// </remarks>
    public static IOrderedQueryable<Feature> NewestFirst(IQueryable<Feature> rows) =>
        rows.OrderByDescending(f => f.UpdatedAt).ThenByDescending(f => f.Id);

    protected override async ValueTask<IReadOnlyList<FilterHit>> ProjectAsync(
        IReadOnlyList<Feature> rows, AccessContext caller, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var typeIds = rows.Where(f => f.FeatureTypeId is not null)
            .Select(f => f.FeatureTypeId!.Value).Distinct().ToArray();
        var types = typeIds.Length == 0
            ? []
            : await db.FeatureTypes.AsNoTracking()
                .Where(t => typeIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t, ct);

        var exact = await protection.ExactViewIdsAsync(caller, [.. rows.Select(f => f.Id)], ct);
        var context = await FeatureContextLines.ForAsync(
            db,
            caller,
            [.. rows.Select(f => new FeatureContextRow(
                f.Id,
                f.Kind,
                f.FeatureTypeId is not null && types.TryGetValue(f.FeatureTypeId.Value, out var type) ? type.Name : null,
                f.AncestorIds))],
            ct);

        return
        [
            .. rows.Select(f =>
            {
                var type = f.FeatureTypeId is not null && types.TryGetValue(f.FeatureTypeId.Value, out var t)
                    ? t
                    : null;
                return new FilterHit
                {
                    World = Key,
                    Id = f.Id,
                    // A feature with no name still has to be pickable from a list, so it is named
                    // after what it is rather than shown as a blank row somebody cannot tell apart
                    // from the blank row above it.
                    Title = string.IsNullOrWhiteSpace(f.Name) ? UnnamedTitle(f, type) : f.Name,
                    Subtitle = context.GetValueOrDefault(f.Id),
                    Symbol = SymbolOf(f, type),
                    Placeable = exact.Contains(f.Id),
                };
            }),
        ];
    }

    /// <summary>
    /// The symbol key for a row: the kind for the built-in ones, the type's own code otherwise.
    /// </summary>
    /// <remarks>
    /// A key rather than anything drawable. The client owns what a key looks like, which is what
    /// lets the same answer be a list row, a map pin and a legend entry without the server having
    /// an opinion about any of the three.
    /// </remarks>
    private static string SymbolOf(Feature feature, FeatureType? type) => feature.Kind switch
    {
        FeatureKind.Cave => "cave",
        FeatureKind.CaveEntrance => "caveEntrance",
        FeatureKind.Centerline => "centerline",
        _ => type?.Code ?? "generic",
    };

    private static string UnnamedTitle(Feature feature, FeatureType? type) =>
        type?.Name ?? feature.Kind.ToString();
}
