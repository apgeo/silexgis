// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Api.Common.Graph;
using SilexGis.Domain.Access;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>
/// Who was on a trip. From a trip: its whole roster as the trip's own page shows it —
/// participants and proposers both, each under their role's code and named by the people
/// label rule. From a person: the trips the trip listing answers when narrowed to that
/// person, so the roster entry's own read is asked first and a refused one answers as
/// though they had been on nothing.
/// </summary>
internal sealed class TripParticipantGraphSource(
    SilexGisDbContext db, FeatureProtection protection, IUserContextAccessor users) : IGraphEdgeSource
{
    public GraphCategory Category => GraphCategory.TripParticipant;

    public bool AcceptsSeed(GraphNodeType seedType) =>
        seedType is GraphNodeType.TripLog or GraphNodeType.Caver;

    public async Task<GraphSourceAnswer> NeighboursAsync(
        GraphSeed seed, AccessContext ctx, GraphPage page, CancellationToken ct)
    {
        var id = seed.Guid;
        var seedNode = GraphPaging.Node(seed.Type, id);
        var roleCodes = await db.TripParticipantRoles.AsNoTracking()
            .ToDictionaryAsync(r => r.Id, r => r.Code, ct);

        if (seed.Type == GraphNodeType.TripLog)
        {
            var user = await users.GetAsync(ct);
            var rows = await db.TripLogParticipants.AsNoTracking()
                .Where(p => p.TripLogId == id)
                .Join(db.Cavers.AsNoTracking(), p => p.CaverId, c => c.Id, (p, c) => new { p.CaverId, p.RoleId })
                .ToListAsync(ct);
            var labels = await CaverDirectory.ResolveLabelsAsync(db, user, rows.Select(r => r.CaverId), ct);
            return GraphPaging.Page(seedNode, rows.Select(r =>
            {
                var far = GraphPaging.Node(GraphNodeType.Caver, r.CaverId);
                return new GraphItem(
                    labels.GetValueOrDefault(r.CaverId) ?? string.Empty,
                    far,
                    GraphPaging.Edge(Category, seedNode, far, roleCodes.GetValueOrDefault(r.RoleId) ?? r.RoleId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }), page);
        }

        var listing = await TripLogListing.ResolveAsync(
            db,
            protection,
            ctx,
            new TripLogListFilter(null, null, null, null, [id], [], [], [], [], null, null, default),
            ct);
        if (listing.Blocked)
        {
            return GraphSourceAnswer.Empty;
        }

        var trips = await listing.Narrowed()
            .Join(db.TripLogParticipants.AsNoTracking().Where(p => p.CaverId == id),
                t => t.Id, p => p.TripLogId, (t, p) => new { t.Id, t.Title, p.RoleId })
            .ToListAsync(ct);
        return GraphPaging.Page(seedNode, trips.Select(t =>
        {
            var far = GraphPaging.Node(GraphNodeType.TripLog, t.Id);
            return new GraphItem(
                t.Title,
                far,
                GraphPaging.Edge(Category, far, seedNode, roleCodes.GetValueOrDefault(t.RoleId) ?? t.RoleId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }), page);
    }
}
