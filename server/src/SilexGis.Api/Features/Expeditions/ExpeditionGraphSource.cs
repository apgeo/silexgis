// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Api.Common.Graph;
using SilexGis.Domain.Access;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Expeditions;

/// <summary>
/// A camp's trips and its roster. The trips are the camp's own trips this caller may read —
/// what the trip listing answers when narrowed to the camp. The roster is one edge per person,
/// whatever roles they held, and is absent altogether for a caller the roster route refuses
/// people to (no names, and no count either, since how many were there is what that refusal
/// withholds). Seen from a trip, its camp is named only when the camp is readable, as the
/// trip's own page names it; seen from a person, the camps whose roster names them, under the
/// camp's read and the same people rule.
/// </summary>
internal sealed class ExpeditionGraphSource(SilexGisDbContext db, IUserContextAccessor users) : IGraphEdgeSource
{
    private const string Trip = "trip";
    private const string Roster = "roster";

    public GraphCategory Category => GraphCategory.Expedition;

    public bool AcceptsSeed(GraphNodeType seedType) =>
        seedType is GraphNodeType.Expedition or GraphNodeType.TripLog or GraphNodeType.Caver;

    public async Task<GraphSourceAnswer> NeighboursAsync(
        GraphSeed seed, AccessContext ctx, GraphPage page, CancellationToken ct)
    {
        var id = seed.Guid;
        var seedNode = GraphPaging.Node(seed.Type, id);
        var readableCamps = db.Expeditions.AsNoTracking().VisibleTo(ctx, AccessDomain.Expeditions);

        switch (seed.Type)
        {
            case GraphNodeType.TripLog:
            {
                var camps = await db.ExpeditionTrips.AsNoTracking()
                    .Where(m => m.TripLogId == id)
                    .Join(readableCamps, m => m.ExpeditionId, e => e.Id, (m, e) => new { e.Id, e.Name })
                    .ToListAsync(ct);
                return GraphPaging.Page(seedNode, camps.Select(c =>
                {
                    var far = GraphPaging.Node(GraphNodeType.Expedition, c.Id);
                    return new GraphItem(c.Name, far, GraphPaging.Edge(Category, far, seedNode, Trip));
                }), page);
            }

            case GraphNodeType.Caver:
            {
                if (!CaverDirectory.MayReadPeople(ctx))
                {
                    return GraphSourceAnswer.Empty;
                }

                var camps = await readableCamps
                    .Where(e => db.ExpeditionRoster.Any(r => r.ExpeditionId == e.Id && r.CaverId == id))
                    .Select(e => new { e.Id, e.Name })
                    .ToListAsync(ct);
                return GraphPaging.Page(seedNode, camps.Select(c =>
                {
                    var far = GraphPaging.Node(GraphNodeType.Expedition, c.Id);
                    return new GraphItem(c.Name, far, GraphPaging.Edge(Category, far, seedNode, Roster));
                }), page);
            }
        }

        var items = new List<GraphItem>();
        var trips = await db.ExpeditionTrips.AsNoTracking()
            .Where(m => m.ExpeditionId == id)
            .Join(db.TripLogs.AsNoTracking().VisibleTo(ctx, AccessDomain.TripLogs), m => m.TripLogId, t => t.Id,
                (m, t) => new { t.Id, t.Title })
            .ToListAsync(ct);
        foreach (var trip in trips)
        {
            var far = GraphPaging.Node(GraphNodeType.TripLog, trip.Id);
            items.Add(new GraphItem(trip.Title, far, GraphPaging.Edge(Category, seedNode, far, Trip)));
        }

        if (CaverDirectory.MayReadPeople(ctx))
        {
            var user = await users.GetAsync(ct);
            var people = await db.ExpeditionRoster.AsNoTracking()
                .Where(r => r.ExpeditionId == id)
                .Select(r => r.CaverId)
                .Distinct()
                .ToListAsync(ct);
            var labels = await CaverDirectory.ResolveLabelsAsync(db, user, people, ct);
            foreach (var caverId in people)
            {
                var far = GraphPaging.Node(GraphNodeType.Caver, caverId);
                items.Add(new GraphItem(
                    labels.GetValueOrDefault(caverId) ?? string.Empty, far, GraphPaging.Edge(Category, seedNode, far, Roster)));
            }
        }

        return GraphPaging.Page(seedNode, items, page);
    }
}
