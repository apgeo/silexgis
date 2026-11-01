// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Api.Common.Graph;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Features;

internal static class FeatureGraphPaging
{
    public static GraphSourceAnswer Page(GraphNodeRef seedNode, IReadOnlyList<GraphItem> items, GraphPage page) =>
        GraphPaging.Page(seedNode, items, page);

    public static string EdgeKey(GraphCategory category, string source, string target, string relation) =>
        GraphPaging.EdgeKey(category, source, target, relation);

    public static GraphNodeRef FeatureNode(Guid id) => GraphPaging.Node(GraphNodeType.Feature, id);
}

/// <summary>
/// Typed links between features, in their stored direction, read through the feature links
/// route's own view: a link whose other end is unreadable is absent, and a locating link is
/// absent whenever either end is one the viewer may not place exactly.
/// </summary>
internal sealed class FeatureLinkGraphSource(SilexGisDbContext db, FeatureProtection protection) : IGraphEdgeSource
{
    public GraphCategory Category => GraphCategory.FeatureLink;

    public bool AcceptsSeed(GraphNodeType seedType) => seedType == GraphNodeType.Feature;

    public async Task<GraphSourceAnswer> NeighboursAsync(
        GraphSeed seed, AccessContext ctx, GraphPage page, CancellationToken ct)
    {
        var id = seed.Guid;
        var links = await FeatureLinkEndpoints.LinksViewAsync(db, protection, ctx, id, ct);
        var otherIds = links.Select(l => l.FromId == id ? l.ToId : l.FromId).Distinct().ToList();

        // A link of a kind that does not locate can still name a protected centerline the
        // viewer may not place exactly; the graph never names one (its own address answers
        // with a not-found), so such a link is left out before counting.
        var withheld = await protection.WithheldCenterlineIdsAsync(ctx, otherIds, ct);
        if (withheld.Count > 0)
        {
            links = [.. links.Where(l => !withheld.Contains(l.FromId == id ? l.ToId : l.FromId))];
            otherIds = [.. otherIds.Where(o => !withheld.Contains(o))];
        }
        var titles = otherIds.Count == 0
            ? []
            : await db.Features.AsNoTracking()
                .Where(f => otherIds.Contains(f.Id))
                .Select(f => new { f.Id, f.Name })
                .ToDictionaryAsync(f => f.Id, f => f.Name, ct);

        var items = links.Select(l =>
        {
            var other = l.FromId == id ? l.ToId : l.FromId;
            var source = GraphWire.Key(GraphNodeType.Feature, l.FromId);
            var target = GraphWire.Key(GraphNodeType.Feature, l.ToId);
            return new GraphItem(
                titles.GetValueOrDefault(other),
                FeatureGraphPaging.FeatureNode(other),
                new GraphEdgeRef(
                    FeatureGraphPaging.EdgeKey(Category, source, target, l.LinkKindCode),
                    Category, l.LinkKindCode, source, target, Directed: true));
        }).ToList();

        return FeatureGraphPaging.Page(FeatureGraphPaging.FeatureNode(id), items, page);
    }
}

/// <summary>
/// Containment, read through the routes that list it: the feature's readable parents, its
/// children as the children route answers them, and for a cave its entrances and centerlines
/// as the cave's own lists answer them. An entrance or centerline edge is told under its own
/// relation rather than as a plain child, and only the cave's list introduces one, so the
/// stricter centerline rule (no centerline at all without exact view of it) is the one that
/// decides — never the looser child listing beside it.
/// </summary>
internal sealed class HierarchyGraphSource(SilexGisDbContext db, FeatureProtection protection) : IGraphEdgeSource
{
    private const string Parent = "parent";
    private const string Entrance = "entrance";
    private const string Centerline = "centerline";

    public GraphCategory Category => GraphCategory.Hierarchy;

    public bool AcceptsSeed(GraphNodeType seedType) => seedType == GraphNodeType.Feature;

    public async Task<GraphSourceAnswer> NeighboursAsync(
        GraphSeed seed, AccessContext ctx, GraphPage page, CancellationToken ct)
    {
        var id = seed.Guid;
        var seedKey = GraphWire.Key(GraphNodeType.Feature, id);
        var kind = await db.Features.AsNoTracking().Where(f => f.Id == id).Select(f => (FeatureKind?)f.Kind)
            .FirstOrDefaultAsync(ct);
        if (kind is null)
        {
            return GraphSourceAnswer.Empty;
        }

        var items = new List<GraphItem>();
        var upward = kind switch
        {
            FeatureKind.CaveEntrance => Entrance,
            FeatureKind.Centerline => Centerline,
            _ => Parent,
        };
        foreach (var parent in await HierarchyEndpoints.ParentsViewAsync(db, ctx, id, ct))
        {
            items.Add(Item(parent.Name, parent.Id, GraphWire.Key(GraphNodeType.Feature, parent.Id), seedKey, upward));
        }

        var isCave = kind == FeatureKind.Cave;
        var children = await (await HierarchyEndpoints.ChildrenQueryAsync(db, protection, ctx, id, ct))
            .Where(c => !isCave || (c.Kind != FeatureKind.CaveEntrance && c.Kind != FeatureKind.Centerline))
            .ToListAsync(ct);
        foreach (var child in children)
        {
            items.Add(Item(child.Name, child.Id, seedKey, GraphWire.Key(GraphNodeType.Feature, child.Id), Parent));
        }

        if (isCave)
        {
            // The cave's entrance list decides entrances on the cave alone, so a caller who reads
            // a private cave through a grant on the cave row is listed entrances whose own rows
            // they cannot read. The graph draws a node only where its own row is readable (the
            // feature's address and the link chip answer by that row), so those entrances are
            // left out here, before counting, rather than counted and then not drawn.
            var entrances = await CavePartReads.EntrancesAsync(db, id, ct);
            var entranceIds = entrances.Select(e => e.Id).ToList();
            var drawable = entranceIds.Count == 0
                ? []
                : (await db.Features.AsNoTracking()
                    .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                    .Where(f => entranceIds.Contains(f.Id))
                    .Select(f => f.Id)
                    .ToListAsync(ct)).ToHashSet();
            foreach (var entrance in entrances.Where(e => drawable.Contains(e.Id)))
            {
                items.Add(Item(entrance.Feature.Name, entrance.Id, seedKey, GraphWire.Key(GraphNodeType.Feature, entrance.Id), Entrance));
            }

            foreach (var centerline in await CavePartReads.CenterlinesAsync(db, protection, ctx, id, ct))
            {
                items.Add(Item(centerline.Name, centerline.Id, seedKey, GraphWire.Key(GraphNodeType.Feature, centerline.Id), Centerline));
            }
        }

        return FeatureGraphPaging.Page(FeatureGraphPaging.FeatureNode(id), items, page);
    }

    private GraphItem Item(string? title, Guid farId, string source, string target, string relation) =>
        new(title,
            FeatureGraphPaging.FeatureNode(farId),
            new GraphEdgeRef(FeatureGraphPaging.EdgeKey(Category, source, target, relation), Category, relation, source, target, Directed: true));
}
