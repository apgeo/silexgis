// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Api.Common.Graph;
using SilexGis.Api.Common.Targets;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Attachments;

/// <summary>
/// Files hung on items, as an item's attachment list answers them: read on the item, the
/// documents' own rules, and the pairing withheld wherever the caller may not place the
/// feature exactly. Seen from a document, the items it hangs on are exactly those whose
/// attachment list this caller could open and find the document in — each asked that same
/// list's question, so a pairing the item's side withholds is not told from the document's.
/// Every attachment serves a document (the stored file is reached through a document
/// version), so the far end is always a document node.
/// </summary>
internal sealed class AttachmentGraphSource(
    SilexGisDbContext db,
    IAccessService access,
    AssociationDisclosure associations,
    ResLinkTargetDirectory targets,
    FeatureProtection protection) : IGraphEdgeSource
{
    public GraphCategory Category => GraphCategory.Attachment;

    public bool AcceptsSeed(GraphNodeType seedType) =>
        seedType == GraphNodeType.Document || AttachmentTargets.TryParse(GraphWire.Name(seedType), out _);

    public async Task<GraphSourceAnswer> NeighboursAsync(
        GraphSeed seed, AccessContext ctx, GraphPage page, CancellationToken ct)
    {
        var id = seed.Guid;
        var seedNode = GraphPaging.Node(seed.Type, id);

        if (seed.Type != GraphNodeType.Document)
        {
            if (!AttachmentTargets.TryParse(GraphWire.Name(seed.Type), out var world)
                || !await FileAccessRules.CanReadTargetAsync(db, access, ctx, new AttachmentTarget(world, id), ct))
            {
                return GraphSourceAnswer.Empty;
            }

            var shown = await AttachmentEndpoints.VisibleRowsAsync(db, associations, ctx, world, id, ct);
            return GraphPaging.Page(seedNode, shown.Select(x =>
            {
                var far = GraphPaging.Node(GraphNodeType.Document, x.Document.Id);
                return new GraphItem(x.Document.Title, far, GraphPaging.Edge(Category, seedNode, far, RoleCode(x.Attachment.Role)));
            }), page);
        }

        var hangings = await (
                from attachment in db.Attachments.AsNoTracking()
                join file in db.StoredFiles.AsNoTracking() on attachment.FileId equals file.Id
                join version in db.DocumentVersions.AsNoTracking() on file.DocumentVersionId equals version.Id
                where version.DocumentId == id
                select new { attachment.FeatureId, attachment.EntityType, attachment.EntityId })
            .Distinct()
            .ToListAsync(ct);

        var items = new List<GraphItem>();
        foreach (var hanging in hangings)
        {
            var targetId = hanging.FeatureId ?? hanging.EntityId;
            var world = hanging.FeatureId is null ? hanging.EntityType : null;
            if (targetId is not { } tid
                || !GraphWire.TryFromTargetWorld(world, out var type)
                || !await FileAccessRules.CanReadTargetAsync(db, access, ctx, new AttachmentTarget(world, tid), ct))
            {
                continue;
            }

            var far = GraphPaging.Node(type, tid);
            foreach (var row in (await AttachmentEndpoints.VisibleRowsAsync(db, associations, ctx, world, tid, ct))
                         .Where(r => r.Document.Id == id))
            {
                items.Add(new GraphItem(null, far, GraphPaging.Edge(Category, far, seedNode, RoleCode(row.Attachment.Role))));
            }
        }

        return GraphPaging.Page(seedNode, await GraphPaging.TitledAsync(targets, protection, ctx, items, ct), page);
    }

    private static string RoleCode(AttachmentRole role) => JsonNamingPolicy.CamelCase.ConvertName(role.ToString());
}
