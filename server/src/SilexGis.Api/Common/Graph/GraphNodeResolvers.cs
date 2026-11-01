// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common.Targets;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Common.Graph;

/// <summary>
/// The ten resource-link target kinds, resolved through the link target directory: a graph
/// node of one of them is drawn with exactly what a link chip naming the same item shows,
/// and is not drawn wherever that chip would be bare. One rule is added for features: a
/// protected centerline the viewer may not place exactly is not resolved at all, as the
/// feature's own address answers it with a not-found — and since this resolution is both the
/// seed check and the backstop over every source, no category can draw one or open it.
/// </summary>
public sealed class GraphTargetNodeResolver(
    ResLinkTargetDirectory targets, SilexGisDbContext db, FeatureProtection protection) : IGraphNodeResolver
{
    private static readonly GraphNodeType[] Handled =
    [
        GraphNodeType.Feature, GraphNodeType.Document, GraphNodeType.TripLog, GraphNodeType.Caver,
        GraphNodeType.CavingGroup, GraphNodeType.MapView, GraphNodeType.SurveyModel, GraphNodeType.Geofile,
        GraphNodeType.Cabinet, GraphNodeType.Expedition,
    ];

    public IReadOnlyCollection<GraphNodeType> Types => Handled;

    public async Task<IReadOnlyDictionary<string, GraphNodeDisplay>> ResolveAsync(
        AccessContext ctx, GraphNodeType type, IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        if (!GraphWire.TryTargetWorld(type, out var world))
        {
            return new Dictionary<string, GraphNodeDisplay>();
        }

        var guids = ids.Select(i => Guid.TryParse(i, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty).Distinct().ToList();
        if (guids.Count == 0)
        {
            return new Dictionary<string, GraphNodeDisplay>();
        }

        if (type == GraphNodeType.Document)
        {
            return await DocumentsAsync(ctx, guids, ct);
        }

        var displays = await targets.Of(world).ResolveAsync(ctx, guids, ct);
        if (type == GraphNodeType.Feature && displays.Count > 0)
        {
            var withheld = await protection.WithheldCenterlineIdsAsync(ctx, [.. displays.Keys], ct);
            if (withheld.Count > 0)
            {
                displays = displays.Where(d => !withheld.Contains(d.Key)).ToDictionary(d => d.Key, d => d.Value);
            }
        }

        // A feature's kind styles its node (a cave is drawn unlike an entrance). Asked only for
        // the ids the resolver already admitted, so it decides nothing about readability.
        Dictionary<Guid, string> kinds = [];
        if (type == GraphNodeType.Feature && displays.Count > 0)
        {
            var admitted = displays.Keys.ToList();
            kinds = (await db.Features.AsNoTracking()
                    .Where(f => admitted.Contains(f.Id))
                    .Select(f => new { f.Id, f.Kind })
                    .ToListAsync(ct))
                .ToDictionary(f => f.Id, f => JsonNamingPolicy.CamelCase.ConvertName(f.Kind.ToString()));
        }

        return displays.ToDictionary(
            d => d.Key.ToString(),
            d => new GraphNodeDisplay(d.Value.Title, d.Value.Subtitle, d.Value.Route, kinds.GetValueOrDefault(d.Key)));
    }

    /// <summary>
    /// Documents in one pass, by the listing form of the document walk (the rules about each
    /// document and its filings, then reach through what its current file hangs on) — the
    /// form the cabinet and document listings answer with. The link chip decides documents
    /// one at a time, which a graph around a busy shelf or a busy item cannot afford. Drawn
    /// with what the chip shows: the title and the document type's name.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, GraphNodeDisplay>> DocumentsAsync(
        AccessContext ctx, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var readable = await DocumentReach.ReadableAsync(
            db, ctx, db.Documents.AsNoTracking().Where(d => ids.Contains(d.Id)), ct);
        var rows = await readable
            .Select(d => new
            {
                d.Id,
                d.Title,
                TypeName = db.DocumentTypes.Where(t => t.Id == d.DocumentTypeId).Select(t => t.Name).FirstOrDefault(),
            })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Id.ToString(), r => new GraphNodeDisplay(r.Title, r.TypeName, null));
    }
}

/// <summary>
/// Tags. Not resource-link targets, and deliberately not registered with the link target
/// directory, which would make them linkable. A tag's name is told to exactly the viewers the
/// tag catalogue answers: without Read on tags, no tag is resolved and none is drawn.
/// </summary>
public sealed class GraphTagNodeResolver(SilexGisDbContext db) : IGraphNodeResolver
{
    public IReadOnlyCollection<GraphNodeType> Types { get; } = [GraphNodeType.Tag];

    public async Task<IReadOnlyDictionary<string, GraphNodeDisplay>> ResolveAsync(
        AccessContext ctx, GraphNodeType type, IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        var tagIds = ids.Select(i => long.TryParse(i, out var v) ? v : 0).Where(v => v > 0).Distinct().ToList();
        if (tagIds.Count == 0 || !TagCatalogue.MayRead(ctx))
        {
            return new Dictionary<string, GraphNodeDisplay>();
        }

        return await db.Tags.AsNoTracking()
            .Where(t => tagIds.Contains(t.Id))
            .ToDictionaryAsync(
                t => t.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                t => new GraphNodeDisplay(t.Name, t.Slug, null),
                ct);
    }
}
