// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Documents;

/// <summary>A cabinet write the tree's own rules refused, carrying a stable code.</summary>
public sealed class CabinetWriteException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// The single mutator of the cabinet tree's derived state — the materialized path every
/// read matches against. Creating a cabinet stamps its path; moving one rewrites the paths
/// of its whole subtree, because each descendant's path names its ancestors. Both stage
/// the change onto the context and leave the commit to the caller, so a cabinet edit lands
/// in one transaction with whatever else the request writes, and one audit merge covers it.
/// <para>
/// Keeping this in one place is what stops a path from disagreeing with the parent column
/// it is derived from: nothing else assigns a path, and no read path traverses parents.
/// </para>
/// </summary>
public sealed class CabinetWriteService(SilexGisDbContext db)
{
    /// <summary>Name of the shadow column holding the materialized path.</summary>
    public const string PathProperty = "Path";

    /// <summary>Refusal: the cabinet named as the parent is not there.</summary>
    public const string ParentNotFoundCode = "cabinet.parent_not_found";

    /// <summary>
    /// Stages a new cabinet under an optional parent, with its path stamped. The caller
    /// has already established that the parent exists and that the caller may write here;
    /// name collisions among siblings are refused by the database's own unique index.
    /// </summary>
    public async Task<Cabinet> CreateAsync(
        string name, string? description, Guid? parentId, CancellationToken ct = default)
    {
        var parentPath = await ParentPathAsync(parentId, ct);
        Refuse(CabinetHierarchyRules.ValidatePlacement(null, parentPath));

        var cabinet = new Cabinet { Name = name, Description = description, ParentId = parentId };
        db.Cabinets.Add(cabinet);
        SetPath(cabinet, CabinetHierarchyRules.PathOf(cabinet.Id, parentPath));
        return cabinet;
    }

    /// <summary>
    /// Stages a re-parent of one cabinet and every cabinet below it. Refused when the new
    /// parent sits inside the moved subtree (that would detach the subtree from the tree
    /// entirely) or when the deepest descendant would land past the depth cap.
    /// </summary>
    public async Task MoveAsync(Cabinet cabinet, Guid? newParentId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cabinet);

        if (newParentId == cabinet.Id)
        {
            Refuse(CabinetHierarchyRules.CycleCode);
        }

        var movedPath = PathOf(cabinet);
        var newParentPath = await ParentPathAsync(newParentId, ct);

        // The whole subtree moves, so the cap has to hold for its deepest member.
        var subtree = await SubtreeAsync(movedPath, ct);
        var height = CabinetHierarchyRules.HeightOf(movedPath, subtree.Select(PathOf));
        Refuse(CabinetHierarchyRules.ValidatePlacement(movedPath, newParentPath, height));

        var newMovedPath = CabinetHierarchyRules.PathOf(cabinet.Id, newParentPath);
        cabinet.ParentId = newParentId;
        foreach (var member in subtree)
        {
            SetPath(member, CabinetHierarchyRules.Rebase(PathOf(member), movedPath, newMovedPath));
        }

        // The moved cabinet is part of its own subtree, but only when it was already
        // stored; a cabinet staged in this same unit of work is not returned by the query.
        if (!subtree.Any(c => c.Id == cabinet.Id))
        {
            SetPath(cabinet, newMovedPath);
        }
    }

    /// <summary>
    /// A cabinet and everything filed below it, matched by the indexed path prefix rather
    /// than by walking parents.
    /// </summary>
    public Task<List<Cabinet>> SubtreeAsync(string path, CancellationToken ct = default) =>
        db.Cabinets.Where(c => EF.Property<LTree>(c, PathProperty).IsDescendantOf(path)).ToListAsync(ct);

    /// <summary>The stored path of a tracked cabinet.</summary>
    public string PathOf(Cabinet cabinet) =>
        db.Entry(cabinet).Property<LTree>(PathProperty).CurrentValue;

    private void SetPath(Cabinet cabinet, string path)
    {
        db.Entry(cabinet).Property<LTree>(PathProperty).CurrentValue = new LTree(path);

        // The array says what the path says; both are stamped here so neither can drift.
        cabinet.AncestorIds = [.. CabinetHierarchyRules.IdsOf(path)];
    }

    private async Task<string?> ParentPathAsync(Guid? parentId, CancellationToken ct)
    {
        if (parentId is not { } id)
        {
            return null;
        }

        // Prefer the tracked instance: a parent created or moved earlier in this same unit
        // of work has a path the database does not carry yet.
        var tracked = db.Cabinets.Local.FirstOrDefault(c => c.Id == id);
        if (tracked is not null)
        {
            return PathOf(tracked);
        }

        var stored = await db.Cabinets
            .Where(c => c.Id == id)
            .Select(c => EF.Property<LTree>(c, PathProperty))
            .Take(1)
            .ToListAsync(ct);

        return stored.Count > 0
            ? stored[0]
            : throw new CabinetWriteException(ParentNotFoundCode, "The parent cabinet does not exist.");
    }

    private static void Refuse(string? code)
    {
        if (code is null)
        {
            return;
        }

        throw new CabinetWriteException(
            code,
            code == CabinetHierarchyRules.CycleCode
                ? "A cabinet cannot be moved inside its own subtree."
                : $"Cabinets may not nest deeper than {CabinetHierarchyRules.MaxDepth} levels.");
    }
}
