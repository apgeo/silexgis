// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common.Graph;
using SilexGis.Domain.Access;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.CavingGroups;

/// <summary>
/// Caving-group membership, as a group's member list answers it: the members of a group the
/// caller holds Read on, named by the people label rule. Seen from a person, the groups are
/// exactly those whose member list the caller could open and find the person in.
/// </summary>
internal sealed class GroupMembershipGraphSource(SilexGisDbContext db, IUserContextAccessor users) : IGraphEdgeSource
{
    public GraphCategory Category => GraphCategory.GroupMembership;

    public bool AcceptsSeed(GraphNodeType seedType) =>
        seedType is GraphNodeType.CavingGroup or GraphNodeType.Caver;

    public async Task<GraphSourceAnswer> NeighboursAsync(
        GraphSeed seed, AccessContext ctx, GraphPage page, CancellationToken ct)
    {
        var id = seed.Guid;
        var seedNode = GraphPaging.Node(seed.Type, id);

        if (seed.Type == GraphNodeType.CavingGroup)
        {
            var user = await users.GetAsync(ct);
            if (user is null || !CavingGroupEndpoints.Holds(ctx, AccessAction.Read, id))
            {
                return GraphSourceAnswer.Empty;
            }

            var members = await CavingGroupEndpoints.MembersViewAsync(db, user, id, ct);
            return GraphPaging.Page(seedNode, members.Select(m =>
            {
                var far = GraphPaging.Node(GraphNodeType.Caver, m.CaverId);
                return new GraphItem(m.Name, far, GraphPaging.Edge(Category, seedNode, far, RoleCode(m.Role)));
            }), page);
        }

        var groups = await db.CavingGroupMemberships.AsNoTracking()
            .Where(m => m.CaverId == id)
            .Join(db.CavingGroups.AsNoTracking(), m => m.CavingGroupId, g => g.Id, (m, g) => new { g.Id, g.Name, m.Role })
            .ToListAsync(ct);
        return GraphPaging.Page(seedNode, groups
            .Where(g => CavingGroupEndpoints.Holds(ctx, AccessAction.Read, g.Id))
            .Select(g =>
            {
                var far = GraphPaging.Node(GraphNodeType.CavingGroup, g.Id);
                return new GraphItem(g.Name, far, GraphPaging.Edge(Category, far, seedNode, RoleCode(g.Role)));
            }), page);
    }

    private static string RoleCode(Domain.CavingGroupRole role) =>
        JsonNamingPolicy.CamelCase.ConvertName(role.ToString());
}
