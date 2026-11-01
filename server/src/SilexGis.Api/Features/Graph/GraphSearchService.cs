// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Api.Common.Graph;
using SilexGis.Api.Common.Targets;
using SilexGis.Domain.Access;
using SilexGis.Domain.Filters;
using SilexGis.Infrastructure.Filters;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Graph;

/// <summary>
/// Finds items to start exploring from. Each kind is searched exactly where the link dialog's
/// target picker searches it — people, clubs and camps through the link surface's own feed, the
/// filterable kinds through their filter world with one "name contains" condition, tags through
/// the tag catalogue's name search under the catalogue's own gate — so the search offers nobody
/// an item that the picker would not. Every hit is then put through the same display resolution
/// the neighbourhood route uses to decide whether an item may be a seed, and a hit it does not
/// admit is dropped: a hit is always an item the route will answer for.
/// </summary>
internal sealed class GraphSearchService(
    SilexGisDbContext db,
    ResLinkTargetDirectory targets,
    FilterWorldRegistry worlds,
    IEnumerable<IGraphNodeResolver> resolvers)
{
    /// <summary>The kinds the search answers for, in the order their hits are listed.</summary>
    public static readonly IReadOnlyList<GraphNodeType> Searchable =
    [
        GraphNodeType.Feature,
        GraphNodeType.Document,
        GraphNodeType.TripLog,
        GraphNodeType.Caver,
        GraphNodeType.CavingGroup,
        GraphNodeType.MapView,
        GraphNodeType.SurveyModel,
        GraphNodeType.Geofile,
        GraphNodeType.Cabinet,
        GraphNodeType.Expedition,
        GraphNodeType.Tag,
    ];

    /// <summary>
    /// The filter world each filterable kind is searched in, and the field typed text is matched
    /// against there — the same pair the link picker names for that kind.
    /// </summary>
    private static readonly Dictionary<GraphNodeType, (string World, string Field)> FilterWorlds = new()
    {
        [GraphNodeType.Feature] = (FeatureFilterWorld.Key, FeatureFilterFields.Name),
        [GraphNodeType.Document] = (DocumentFilterWorld.Key, DocumentFilterFields.Title),
        [GraphNodeType.TripLog] = (TripLogFilterWorld.Key, TripLogFilterFields.Title),
        [GraphNodeType.MapView] = (MapViewFilterWorld.Key, CommonFilterFields.Name),
        [GraphNodeType.Cabinet] = (CabinetFilterWorld.Key, CommonFilterFields.Name),
        [GraphNodeType.SurveyModel] = (SurveyModelFilterWorld.Key, CommonFilterFields.Name),
        [GraphNodeType.Geofile] = (GeofileFilterWorld.Key, CommonFilterFields.Name),
    };

    private readonly Dictionary<GraphNodeType, IGraphNodeResolver> resolverOf = resolvers
        .SelectMany(r => r.Types.Select(t => (t, r)))
        .ToDictionary(p => p.t, p => p.r);

    public async Task<IReadOnlyList<GraphSearchHitDto>> SearchAsync(
        AccessContext ctx, string query, IReadOnlyCollection<GraphNodeType> types, int limit, CancellationToken ct)
    {
        IReadOnlyDictionary<string, WorldVocabulary>? vocabularies = null;
        var hits = new List<GraphSearchHitDto>();

        // One kind after another on the one context: a DbContext answers one query at a time.
        foreach (var type in Searchable.Where(types.Contains))
        {
            IReadOnlyList<string> ids;
            if (type == GraphNodeType.Tag)
            {
                ids = await TagIdsAsync(ctx, query, limit, ct);
            }
            else if (FilterWorlds.TryGetValue(type, out var world))
            {
                vocabularies ??= (await worlds.VocabulariesAsync(ctx, ct)).ToDictionary(v => v.World, StringComparer.Ordinal);
                ids = await WorldIdsAsync(ctx, world.World, world.Field, query, limit, vocabularies, ct);
            }
            else
            {
                ids = await FeedIdsAsync(ctx, type, query, limit, ct);
            }

            if (ids.Count == 0 || !resolverOf.TryGetValue(type, out var resolver))
            {
                continue;
            }

            var displays = await resolver.ResolveAsync(ctx, type, ids, ct);
            foreach (var id in ids)
            {
                if (displays.TryGetValue(id, out var display))
                {
                    hits.Add(new GraphSearchHitDto(
                        GraphWire.Key(type, id),
                        GraphWire.Name(type),
                        id,
                        display.Title,
                        display.Subtitle,
                        display.Kind,
                        display.Route));
                }
            }
        }

        return hits;
    }

    /// <summary>
    /// The picker's question for a filterable kind, asked of its world exactly as the filter query
    /// would ask it. A world, or a field, this caller may not ask about answers nothing rather
    /// than being asked anyway — the filter query refuses such a document outright.
    /// </summary>
    private async Task<IReadOnlyList<string>> WorldIdsAsync(
        AccessContext ctx,
        string worldKey,
        string field,
        string query,
        int limit,
        IReadOnlyDictionary<string, WorldVocabulary> vocabularies,
        CancellationToken ct)
    {
        if (!vocabularies.TryGetValue(worldKey, out var vocabulary))
        {
            return [];
        }

        var where = new ConditionNode(field, FilterOp.Contains, [new TextValue(query)]);
        var sort = vocabulary.Supports(SortKey.Title) ? SortKey.Title : SortKey.Created;
        var document = new FilterDocument
        {
            Scope = [new WorldScope(worldKey, where)],
            Sort = sort,
            Descending = sort != SortKey.Title,
        };
        if (FilterValidation.Validate(document, vocabularies).Count > 0)
        {
            return [];
        }

        var world = worlds.Find(worldKey);
        if (world is null)
        {
            return [];
        }

        var page = await world.QueryAsync(
            new WorldQuery(ctx, where, sort, document.Descending, null, 0, limit, WantTotal: false),
            ct);
        return [.. page.Hits.Select(h => h.Id.ToString())];
    }

    /// <summary>People, clubs and camps: the link surface's own search feed.</summary>
    private async Task<IReadOnlyList<string>> FeedIdsAsync(
        AccessContext ctx, GraphNodeType type, string query, int limit, CancellationToken ct)
    {
        if (!GraphWire.TryTargetWorld(type, out var world) || targets.SearchOf(world) is not { } search)
        {
            return [];
        }

        var found = await search.SearchAsync(ctx, query, limit, ct);
        return [.. found.Select(h => h.Id.ToString())];
    }

    /// <summary>
    /// The tag catalogue's name search, behind the catalogue's own gate: without Read on tags no
    /// tag name is told, here as on the catalogue.
    /// </summary>
    private async Task<IReadOnlyList<string>> TagIdsAsync(
        AccessContext ctx, string query, int limit, CancellationToken ct)
    {
        if (!TagCatalogue.MayRead(ctx))
        {
            return [];
        }

        var pattern = $"%{query}%";
        var ids = await db.Tags.AsNoTracking()
            .Where(x => EF.Functions.ILike(EF.Functions.Unaccent(x.Name), EF.Functions.Unaccent(pattern)))
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Take(limit)
            .Select(x => x.Id)
            .ToListAsync(ct);
        return [.. ids.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture))];
    }
}
