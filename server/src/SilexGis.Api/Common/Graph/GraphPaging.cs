// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Api.Common.Targets;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Permissions;

namespace SilexGis.Api.Common.Graph;

/// <summary>One top-level item of a category (one edge and its far end), before paging.</summary>
public sealed record GraphItem(string? FarTitle, GraphNodeRef Far, GraphEdgeRef Edge);

/// <summary>
/// The one ordering and windowing rule every edge-per-item category pages by, and the helpers
/// every source builds its keys with — so two sources cannot spell the same edge differently
/// and meet as two edges, nor order the same items two ways.
/// </summary>
public static class GraphPaging
{
    /// <summary>
    /// Far-end title, case-insensitively, then edge key; then the window. Items are
    /// de-duplicated by edge key first, so a relation stored twice (one document attached to
    /// one item through two of its files under the same role) is one edge and counts once.
    /// </summary>
    public static GraphSourceAnswer Page(GraphNodeRef seedNode, IEnumerable<GraphItem> items, GraphPage page)
    {
        var unique = items
            .GroupBy(i => i.Edge.Key, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();
        var window = unique
            .OrderBy(i => i.FarTitle, Comparer<string?>.Create(GraphWire.CompareTitles))
            .ThenBy(i => i.Edge.Key, StringComparer.Ordinal)
            .Skip(page.Offset)
            .Take(page.Limit)
            .ToList();
        return new GraphSourceAnswer(
            [seedNode, .. window.Select(i => i.Far)],
            [.. window.Select(i => i.Edge)],
            unique.Count);
    }

    public static string EdgeKey(GraphCategory category, string source, string target, string relation) =>
        $"{GraphWire.Name(category)}:{source}|{target}|{relation}";

    public static GraphNodeRef Node(GraphNodeType type, Guid id) => new(GraphWire.Key(type, id), type, id.ToString());

    public static GraphEdgeRef Edge(GraphCategory category, GraphNodeRef source, GraphNodeRef target, string relation) =>
        new(EdgeKey(category, source.Key, target.Key, relation), category, relation, source.Key, target.Key, Directed: true);

    /// <summary>
    /// Fills in the far-end title of every item that has none, from the display the link
    /// target directory gives this viewer, and drops the items it gives none for. Used where a
    /// source's own route names its far ends by a rule the source does not repeat (a person's
    /// label, an expedition's name): ordering then follows exactly what the node will be drawn
    /// with, and the count never includes an item the drawing would leave out — which is why a
    /// protected centerline the viewer may not place exactly is dropped here too, by the rule
    /// the node resolution applies to it.
    /// </summary>
    public static async Task<IReadOnlyList<GraphItem>> TitledAsync(
        ResLinkTargetDirectory targets,
        FeatureProtection protection,
        AccessContext ctx,
        IReadOnlyList<GraphItem> items,
        CancellationToken ct)
    {
        var result = items.Where(i => i.FarTitle is not null).ToList();
        foreach (var group in items.Where(i => i.FarTitle is null).GroupBy(i => i.Far.Type))
        {
            if (!GraphWire.TryTargetWorld(group.Key, out var world))
            {
                continue;
            }

            var ids = group.Select(i => Guid.Parse(i.Far.Id!)).Distinct().ToList();
            var displays = await targets.Of(world).ResolveAsync(ctx, ids, ct);
            var withheld = group.Key == GraphNodeType.Feature
                ? await protection.WithheldCenterlineIdsAsync(ctx, [.. displays.Keys], ct)
                : [];
            foreach (var item in group)
            {
                var farId = Guid.Parse(item.Far.Id!);
                if (!withheld.Contains(farId) && displays.TryGetValue(farId, out var display))
                {
                    result.Add(item with { FarTitle = display.Title });
                }
            }
        }

        return result;
    }
}
