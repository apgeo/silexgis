// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Api.Common.Graph;
using SilexGis.Domain.Access;

namespace SilexGis.Api.Features.Graph;

/// <summary>
/// Assembles one neighbourhood from the edge sources. It decides nothing about who may see
/// what — each source answers exactly what its relation's own route answers — except one
/// backstop: every node other than a placeholder is resolved for display again here, once per
/// type, and a node that resolution does not admit is not drawn, with its edges. So a source
/// that let something through by mistake still cannot put it on the screen.
/// </summary>
internal sealed class GraphNeighbourhoodService(
    IEnumerable<IGraphEdgeSource> sources,
    IEnumerable<IGraphNodeResolver> resolvers,
    ILogger<GraphNeighbourhoodService> logger)
{
    private readonly IReadOnlyList<IGraphEdgeSource> sources = [.. sources.OrderBy(s => s.Category)];
    private readonly Dictionary<GraphNodeType, IGraphNodeResolver> resolverOf = resolvers
        .SelectMany(r => r.Types.Select(t => (t, r)))
        .ToDictionary(p => p.t, p => p.r);

    /// <summary>Whether the viewer may ask about this item: the same question its node's display answers.</summary>
    public async Task<GraphNodeDisplay?> SeedAsync(GraphSeed seed, AccessContext ctx, CancellationToken ct)
    {
        if (!resolverOf.TryGetValue(seed.Type, out var resolver))
        {
            return null;
        }

        var resolved = await resolver.ResolveAsync(ctx, seed.Type, [seed.Id], ct);
        return resolved.GetValueOrDefault(seed.Id);
    }

    /// <param name="paged">The one category being paged on, when the request names one; only it is answered.</param>
    public async Task<GraphNeighbourhoodDto> NeighbourhoodAsync(
        GraphSeed seed,
        GraphNodeDisplay seedDisplay,
        AccessContext ctx,
        IReadOnlyCollection<GraphCategory> categories,
        GraphCategory? paged,
        int offset,
        int limit,
        CancellationToken ct)
    {
        var nodes = new Dictionary<string, GraphNodeRef>(StringComparer.Ordinal);
        var edges = new Dictionary<string, GraphEdgeRef>(StringComparer.Ordinal);
        var totals = new List<(GraphCategory Category, int Total)>();

        // Sequential on purpose: every source reads through the one request-scoped context,
        // which does not admit concurrent queries.
        foreach (var source in sources)
        {
            var asked = paged is { } only ? source.Category == only : categories.Contains(source.Category);
            if (!asked || !source.AcceptsSeed(seed.Type))
            {
                continue;
            }

            var answer = await source.NeighboursAsync(
                seed, ctx, new GraphPage(paged is null ? 0 : offset, limit), ct);
            totals.Add((source.Category, answer.Total));
            foreach (var node in answer.Nodes)
            {
                nodes.TryAdd(node.Key, node);
            }

            foreach (var edge in answer.Edges)
            {
                edges.TryAdd(edge.Key, edge);
            }
        }

        // A source may report the seed itself (a link hub asked about directly carries its
        // short code that way); the seed is drawn from that report when there is one.
        var seedRef = nodes.GetValueOrDefault(seed.Key) ?? new GraphNodeRef(seed.Key, seed.Type, seed.Id);
        nodes.Remove(seed.Key);
        var displays = new Dictionary<string, GraphNodeDisplay>(StringComparer.Ordinal) { [seed.Key] = seedDisplay };
        foreach (var group in nodes.Values.Where(n => !n.Restricted).GroupBy(n => n.Type))
        {
            if (!resolverOf.TryGetValue(group.Key, out var resolver))
            {
                continue;
            }

            var resolved = await resolver.ResolveAsync(ctx, group.Key, [.. group.Select(n => n.Id!).Distinct()], ct);
            foreach (var node in group)
            {
                if (resolved.TryGetValue(node.Id!, out var display))
                {
                    displays[node.Key] = display;
                }
            }
        }

        var dropped = nodes.Values.Where(n => !n.Restricted && !displays.ContainsKey(n.Key)).Select(n => n.Key).ToHashSet();
        if (dropped.Count > 0)
        {
            // Every source filters by the rule its route applies, so reaching this means one of
            // them disagrees with the display rules about some item. The node stays hidden; the
            // warning is there so the disagreement is found and settled, not silently absorbed.
            logger.LogWarning(
                "Graph backstop withheld {Count} node(s) a source returned around a {SeedType} seed.",
                dropped.Count, GraphWire.Name(seed.Type));
        }

        var keptEdges = edges.Values
            .Where(e => !dropped.Contains(e.Source) && !dropped.Contains(e.Target))
            .ToList();
        var connected = keptEdges.SelectMany(e => new[] { e.Source, e.Target }).ToHashSet(StringComparer.Ordinal);
        var keptNodes = nodes.Values
            .Where(n => !dropped.Contains(n.Key) && connected.Contains(n.Key))
            .OrderBy(n => n.Key, StringComparer.Ordinal)
            .ToList();

        var seedDto = ToDto(seedRef, seedDisplay);
        var counts = totals
            .Select(t => new GraphCategoryCountDto(GraphWire.Name(t.Category), Shown(t.Category, keptEdges), t.Total))
            .ToList();
        return new GraphNeighbourhoodDto(
            seedDto,
            [seedDto, .. keptNodes.Select(n => ToDto(n, displays.GetValueOrDefault(n.Key)))],
            [.. keptEdges.OrderBy(e => e.Category).ThenBy(e => e.Key, StringComparer.Ordinal).Select(ToDto)],
            counts);
    }

    /// <summary>A resource link's top-level item is its hub; every other category's is an edge.</summary>
    private static int Shown(GraphCategory category, IEnumerable<GraphEdgeRef> edges)
    {
        var mine = edges.Where(e => e.Category == category);
        return category == GraphCategory.ResLink
            ? mine.Select(e => e.Source).Distinct().Count()
            : mine.Count();
    }

    private static GraphNodeDto ToDto(GraphNodeRef node, GraphNodeDisplay? display) => new(
        node.Key,
        GraphWire.Name(node.Type),
        node.Restricted ? null : node.Id,
        node.Restricted ? null : display?.Title,
        node.Restricted ? null : display?.Subtitle,
        node.Restricted ? null : display?.Kind,
        node.Restricted,
        !node.Restricted && node.Type is not (GraphNodeType.ResLink or GraphNodeType.File),
        node.Restricted ? null : display?.Route,
        node.ShortCode,
        node.MemberCount);

    private static GraphEdgeDto ToDto(GraphEdgeRef edge) => new(
        edge.Key,
        GraphWire.Name(edge.Category),
        edge.Relation,
        edge.RelationName,
        edge.InverseName,
        edge.Directed,
        edge.IsMain,
        edge.Source,
        edge.Target,
        edge.AnchorKind);
}
