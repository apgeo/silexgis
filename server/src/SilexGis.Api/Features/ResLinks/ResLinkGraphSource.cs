// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common.Graph;
using SilexGis.Api.Common.Targets;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.ResLinks;

/// <summary>
/// Resource links as graph edges: each link is a hub with an edge to every member the viewer
/// is shown. Read through the links panel's own answer for the same item, so the hubs, the
/// members, the placeholders and the total are exactly the panel's: members of dead trips,
/// hidden place pins, hidden places at survey stations and withheld associations are absent, and a member the viewer cannot
/// read is a placeholder of its own that names nothing. Links page in the panel's order,
/// newest first, so a window here is a window there.
/// </summary>
internal sealed class ResLinkGraphSource(
    SilexGisDbContext db,
    ResLinkTargetDirectory targets,
    AssociationDisclosure associations,
    FeatureProtection protection,
    PhotoPositionDisclosure photoPositions,
    PlacePointDisclosure placePoints,
    PlaceStationDisclosure placeStations,
    IAccessService access) : IGraphEdgeSource
{
    public GraphCategory Category => GraphCategory.ResLink;

    public bool AcceptsSeed(GraphNodeType seedType) =>
        seedType == GraphNodeType.ResLink || GraphWire.TryTargetWorld(seedType, out _);

    public async Task<GraphSourceAnswer> NeighboursAsync(
        GraphSeed seed, AccessContext ctx, GraphPage page, CancellationToken ct)
    {
        if (seed.Type == GraphNodeType.ResLink)
        {
            // A link asked about directly is its own single hub: the one-link read's answer.
            var link = await ResLinkStanding.Of(db).AsNoTracking().FirstOrDefaultAsync(l => l.Id == seed.Guid, ct);
            if (link is null || await ResLinkEndpoints.HiddenWholeAsync(placePoints, placeStations, ctx, link.Id, ct))
            {
                return GraphSourceAnswer.Empty;
            }

            var projected = await ResLinkEndpoints.ProjectAsync(
                db, targets, associations, protection, photoPositions, access, ctx, [link], ct, skipCuration: true);
            return await AnswerAsync([.. projected.Skip(page.Offset).Take(page.Limit)], 1, ctx, ct);
        }

        GraphWire.TryTargetWorld(seed.Type, out var world);
        var (links, total) = await ResLinkEndpoints.IncidentLinksAsync(
            db, targets, associations, protection, photoPositions, placePoints, placeStations, access, ctx,
            world, seed.Guid, null, page.Offset, page.Limit, ct, withCuration: false);
        return await AnswerAsync(links, total, ctx, ct);
    }

    private async Task<GraphSourceAnswer> AnswerAsync(
        IReadOnlyList<ResLinkDto> links, int total, AccessContext ctx, CancellationToken ct)
    {
        // A protected centerline is never named to a viewer who may not place it exactly — its
        // own address answers that viewer with a not-found — so a member naming one is drawn
        // as a bare placeholder of its own even where the chip would carry a title.
        var featureMembers = links.SelectMany(l => l.Members)
            .Where(m => m.Display is not null && m.TargetType == GraphWire.Name(GraphNodeType.Feature))
            .Select(m => m.TargetId)
            .ToList();
        var withheld = await protection.WithheldCenterlineIdsAsync(ctx, featureMembers, ct);

        var nodes = new List<GraphNodeRef>();
        var edges = new List<GraphEdgeRef>();
        foreach (var link in links)
        {
            var hubKey = GraphWire.Key(GraphNodeType.ResLink, link.Id);
            nodes.Add(new GraphNodeRef(
                hubKey, GraphNodeType.ResLink, link.Id.ToString(), ShortCode: link.ShortCode, MemberCount: link.Members.Count));

            var relation = link.RelationType;
            var custom = relation is { Seeded: false };
            foreach (var member in link.Members)
            {
                var type = GraphWire.TryParseType(member.TargetType, out var parsed)
                    ? parsed
                    : throw new InvalidOperationException($"Member target type '{member.TargetType}' has no graph node type.");
                string memberKey;
                if (member.Display is null || (type == GraphNodeType.Feature && withheld.Contains(member.TargetId)))
                {
                    memberKey = GraphWire.RestrictedKey(member.Id);
                    nodes.Add(new GraphNodeRef(memberKey, type, null, Restricted: true));
                }
                else
                {
                    memberKey = GraphWire.Key(type, member.TargetId);
                    nodes.Add(new GraphNodeRef(memberKey, type, member.TargetId.ToString()));
                }

                edges.Add(new GraphEdgeRef(
                    $"resLink:{link.Id}:{member.Id}",
                    GraphCategory.ResLink,
                    relation?.Code ?? "unspecified",
                    hubKey,
                    memberKey,
                    relation?.Directed ?? false,
                    custom ? relation!.Name : null,
                    custom ? relation!.InverseName : null,
                    member.IsMain,
                    member.AnchorKind == AnchorKind.Whole
                        ? null
                        : JsonNamingPolicy.CamelCase.ConvertName(member.AnchorKind.ToString())));
            }
        }

        return new GraphSourceAnswer(nodes, edges, total);
    }
}

/// <summary>
/// Link hubs. A standing link is readable by every signed-in account unless it is a place
/// pinned on phones' maps or a place mapped to a survey station that this viewer is not
/// shown — the rule of the one-link read.
/// A hub carries no title of its own; its relation travels on its edges.
/// </summary>
internal sealed class ResLinkGraphNodeResolver(
    SilexGisDbContext db, PlacePointDisclosure placePoints, PlaceStationDisclosure placeStations) : IGraphNodeResolver
{
    public IReadOnlyCollection<GraphNodeType> Types { get; } = [GraphNodeType.ResLink];

    public async Task<IReadOnlyDictionary<string, GraphNodeDisplay>> ResolveAsync(
        AccessContext ctx, GraphNodeType type, IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        var guids = ids.Select(i => Guid.TryParse(i, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty).Distinct().ToList();
        if (guids.Count == 0)
        {
            return new Dictionary<string, GraphNodeDisplay>();
        }

        var standing = ResLinkStanding.Of(db).AsNoTracking().Where(l => guids.Contains(l.Id));
        var hidden = await placePoints.HiddenLinkIdsAsync(ctx, standing, ct);
        hidden.UnionWith(await placeStations.HiddenLinkIdsAsync(ctx, standing, ct));
        var rows = await standing.Select(l => new { l.Id, l.Description }).ToListAsync(ct);
        return rows
            .Where(r => !hidden.Contains(r.Id))
            .ToDictionary(r => r.Id.ToString(), r => new GraphNodeDisplay(r.Description, null, null));
    }
}
