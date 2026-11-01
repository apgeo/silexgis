// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Api.Common.Graph;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Cabinets;

/// <summary>
/// The cabinet tree and what is filed in it, read as the cabinet routes read them: every
/// signed-in account sees the shelves (the tree carries no per-shelf rule), and a shelf's
/// documents are only those its document listing would show this caller — the rules written
/// about each document and its filings, widened by reach through what the document hangs on.
/// Only direct filings make an edge; a document filed further down the tree is that shelf's.
/// </summary>
internal sealed class CabinetGraphSource(SilexGisDbContext db) : IGraphEdgeSource
{
    private const string Filed = "filed";
    private const string Parent = "parent";

    public GraphCategory Category => GraphCategory.Cabinet;

    public bool AcceptsSeed(GraphNodeType seedType) =>
        seedType is GraphNodeType.Cabinet or GraphNodeType.Document;

    public async Task<GraphSourceAnswer> NeighboursAsync(
        GraphSeed seed, AccessContext ctx, GraphPage page, CancellationToken ct)
    {
        var id = seed.Guid;
        var seedNode = GraphPaging.Node(seed.Type, id);
        var items = new List<GraphItem>();

        if (seed.Type == GraphNodeType.Document)
        {
            // The seed is already known to be readable, which is the same question the shelf
            // listing asks of it; every shelf it is filed on would list it.
            var shelves = await db.CabinetDocuments.AsNoTracking()
                .Where(m => m.DocumentId == id)
                .Join(db.Cabinets.AsNoTracking(), m => m.CabinetId, c => c.Id, (m, c) => new { c.Id, c.Name })
                .ToListAsync(ct);
            foreach (var shelf in shelves)
            {
                var far = GraphPaging.Node(GraphNodeType.Cabinet, shelf.Id);
                items.Add(new GraphItem(shelf.Name, far, GraphPaging.Edge(Category, far, seedNode, Filed)));
            }

            return GraphPaging.Page(seedNode, items, page);
        }

        var cabinet = await db.Cabinets.AsNoTracking().Where(c => c.Id == id)
            .Select(c => new { c.ParentId }).FirstOrDefaultAsync(ct);
        if (cabinet is null)
        {
            return GraphSourceAnswer.Empty;
        }

        if (cabinet.ParentId is { } parentId)
        {
            var parent = await db.Cabinets.AsNoTracking().Where(c => c.Id == parentId)
                .Select(c => new { c.Id, c.Name }).FirstOrDefaultAsync(ct);
            if (parent is not null)
            {
                var far = GraphPaging.Node(GraphNodeType.Cabinet, parent.Id);
                items.Add(new GraphItem(parent.Name, far, GraphPaging.Edge(Category, far, seedNode, Parent)));
            }
        }

        foreach (var child in await db.Cabinets.AsNoTracking().Where(c => c.ParentId == id)
                     .Select(c => new { c.Id, c.Name }).ToListAsync(ct))
        {
            var far = GraphPaging.Node(GraphNodeType.Cabinet, child.Id);
            items.Add(new GraphItem(child.Name, far, GraphPaging.Edge(Category, seedNode, far, Parent)));
        }

        var filed = db.Documents.AsNoTracking()
            .Where(d => db.CabinetDocuments.Any(m => m.DocumentId == d.Id && m.CabinetId == id));
        var readable = await DocumentReach.ReadableAsync(db, ctx, filed, ct);
        foreach (var document in await readable.Select(d => new { d.Id, d.Title }).ToListAsync(ct))
        {
            var far = GraphPaging.Node(GraphNodeType.Document, document.Id);
            items.Add(new GraphItem(document.Title, far, GraphPaging.Edge(Category, seedNode, far, Filed)));
        }

        return GraphPaging.Page(seedNode, items, page);
    }
}
