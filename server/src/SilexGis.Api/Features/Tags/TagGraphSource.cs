// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Api.Common.Graph;
using SilexGis.Api.Common.Targets;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Tags;

/// <summary>
/// Taggings. From an item: its tags, under the item's own read — the gate the taggings list
/// asks — and only for a caller the tag catalogue answers at all, since a tag the catalogue
/// would not name is not drawn either. From a tag: every tagged item the caller may read,
/// each decided by the same target read the taggings list asks of it.
/// </summary>
internal sealed class TagGraphSource(
    SilexGisDbContext db, IAccessService access, ResLinkTargetDirectory targets, FeatureProtection protection) : IGraphEdgeSource
{
    private const string Tagged = "tagged";

    public GraphCategory Category => GraphCategory.Tag;

    public bool AcceptsSeed(GraphNodeType seedType) =>
        seedType == GraphNodeType.Tag || AttachmentTargets.TryParse(GraphWire.Name(seedType), out _);

    public async Task<GraphSourceAnswer> NeighboursAsync(
        GraphSeed seed, AccessContext ctx, GraphPage page, CancellationToken ct)
    {
        if (!TagCatalogue.MayRead(ctx))
        {
            return GraphSourceAnswer.Empty;
        }

        return seed.Type == GraphNodeType.Tag
            ? await TaggedItemsAsync(seed, ctx, page, ct)
            : await TagsOfAsync(seed, ctx, page, ct);
    }

    private async Task<GraphSourceAnswer> TagsOfAsync(GraphSeed seed, AccessContext ctx, GraphPage page, CancellationToken ct)
    {
        if (!AttachmentTargets.TryParse(GraphWire.Name(seed.Type), out var world))
        {
            return GraphSourceAnswer.Empty;
        }

        var id = seed.Guid;
        if (!await FileAccessRules.CanReadTargetAsync(db, access, ctx, new AttachmentTarget(world, id), ct))
        {
            return GraphSourceAnswer.Empty;
        }

        var scoped = world is { } pairType
            ? db.Taggings.AsNoTracking().Where(x => x.EntityType == pairType && x.EntityId == id)
            : db.Taggings.AsNoTracking().Where(x => x.FeatureId == id);
        var tags = await scoped
            .Join(db.Tags.AsNoTracking(), x => x.TagId, t => t.Id, (x, t) => new { t.Id, t.Name })
            .ToListAsync(ct);

        var seedNode = GraphPaging.Node(seed.Type, id);
        return GraphPaging.Page(seedNode, tags.Select(t =>
        {
            var far = TagNode(t.Id);
            return new GraphItem(t.Name, far, GraphPaging.Edge(Category, seedNode, far, Tagged));
        }), page);
    }

    private async Task<GraphSourceAnswer> TaggedItemsAsync(GraphSeed seed, AccessContext ctx, GraphPage page, CancellationToken ct)
    {
        var tagId = long.Parse(seed.Id, CultureInfo.InvariantCulture);
        var seedNode = TagNode(tagId);
        var rows = await db.Taggings.AsNoTracking()
            .Where(x => x.TagId == tagId)
            .Select(x => new { x.FeatureId, x.EntityType, x.EntityId })
            .ToListAsync(ct);

        // Features in one query, through the same visibility filter the target read applies to
        // a feature; every other kind through that read itself, once per distinct item. A
        // protected centerline the caller may not place exactly is dropped where the far ends
        // are titled, before paging, so it is neither drawn nor counted.
        var featureIds = rows.Where(r => r.FeatureId is not null).Select(r => r.FeatureId!.Value).Distinct().ToList();
        var readable = featureIds.Count == 0
            ? []
            : (await db.Features.AsNoTracking()
                .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                .Where(f => featureIds.Contains(f.Id))
                .Select(f => f.Id)
                .ToListAsync(ct))
            .Select(f => (GraphNodeType.Feature, f))
            .ToList();
        foreach (var other in rows.Where(r => r.FeatureId is null && r.EntityId is not null)
                     .Select(r => (r.EntityType, Id: r.EntityId!.Value)).Distinct())
        {
            if (GraphWire.TryFromTargetWorld(other.EntityType, out var type)
                && await FileAccessRules.CanReadTargetAsync(db, access, ctx, new AttachmentTarget(other.EntityType, other.Id), ct))
            {
                readable.Add((type, other.Id));
            }
        }

        var items = await GraphPaging.TitledAsync(targets, protection, ctx, [.. readable.Select(r =>
        {
            var far = GraphPaging.Node(r.Item1, r.Item2);
            return new GraphItem(null, far, GraphPaging.Edge(Category, far, seedNode, Tagged));
        })], ct);
        return GraphPaging.Page(seedNode, items, page);
    }

    private static GraphNodeRef TagNode(long id)
    {
        var text = id.ToString(CultureInfo.InvariantCulture);
        return new GraphNodeRef(GraphWire.Key(GraphNodeType.Tag, text), GraphNodeType.Tag, text);
    }
}
