// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Api.Common.Graph;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// A cave's survey models, as the cave's model list answers them: read on the cave, and none
/// at all — not even how many — for a caller who may not place the cave exactly, because a
/// cave's 3D models are its location. Seen from a model, its cave is named under the same
/// rule, which the model's own read already requires.
/// </summary>
internal sealed class SurveyModelGraphSource(
    SilexGisDbContext db, IAccessService access, FeatureProtection protection) : IGraphEdgeSource
{
    private const string Model = "model";

    public GraphCategory Category => GraphCategory.Survey;

    public bool AcceptsSeed(GraphNodeType seedType) =>
        seedType is GraphNodeType.Feature or GraphNodeType.SurveyModel;

    public async Task<GraphSourceAnswer> NeighboursAsync(
        GraphSeed seed, AccessContext ctx, GraphPage page, CancellationToken ct)
    {
        var id = seed.Guid;
        var caveId = seed.Type == GraphNodeType.Feature
            ? id
            : await db.SurveyModels.AsNoTracking().Where(m => m.Id == id).Select(m => (Guid?)m.CaveFeatureId)
                .FirstOrDefaultAsync(ct);
        if (caveId is not { } cid)
        {
            return GraphSourceAnswer.Empty;
        }

        var cave = await db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == cid && f.Kind == FeatureKind.Cave, ct);
        if (cave is null || !await SurveyModelAccess.VisibleAsync(access, protection, ctx, cave, ct))
        {
            return GraphSourceAnswer.Empty;
        }

        var caveNode = GraphPaging.Node(GraphNodeType.Feature, cid);
        if (seed.Type == GraphNodeType.SurveyModel)
        {
            var seedNode = GraphPaging.Node(GraphNodeType.SurveyModel, id);
            return GraphPaging.Page(
                seedNode,
                [new GraphItem(cave.Name, caveNode, GraphPaging.Edge(Category, caveNode, seedNode, Model))],
                page);
        }

        var models = await db.SurveyModels.AsNoTracking()
            .Where(m => m.CaveFeatureId == cid)
            .Select(m => new { m.Id, m.Name })
            .ToListAsync(ct);
        return GraphPaging.Page(
            caveNode,
            models.Select(m =>
            {
                var far = GraphPaging.Node(GraphNodeType.SurveyModel, m.Id);
                return new GraphItem(m.Name, far, GraphPaging.Edge(Category, caveNode, far, Model));
            }),
            page);
    }
}
