// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// One shape for a feature row, wherever this slice hands one to a device.
/// </summary>
/// <remarks>
/// There is more than one place a row goes out from — a page of a download, and the server's own
/// version of a row an upload could not apply — and they are the same row to the device that
/// receives them. Shaped in two places they would drift: one would learn to carry a field, or to
/// name a parent the caller cannot read, and a device would then be told two different things
/// about the same cave depending on which request it happened to arrive on.
/// </remarks>
internal static class SyncFeatureShaping
{
    /// <summary>
    /// The server's own version of rows a device holds a different idea of — an edit the upload
    /// could not apply, or one a reviewer turned down — shaped as the feature stream would hand
    /// them to this caller.
    /// </summary>
    /// <remarks>
    /// A row this caller may not read, or may read but not be given the location of, is absent:
    /// not blurred, not stubbed. The device keeps what it has and learns of the row only when its
    /// own download would carry it, which is exactly what the download stream would have told it.
    /// </remarks>
    internal static async Task<List<SyncFeatureDto>> CurrentAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var visible = db.Features.AsNoTracking().VisibleTo(ctx, db.Features, db.FeatureSetMembers);
        var candidates = await visible.Where(f => ids.Contains(f.Id)).ToListAsync(ct);
        var withheld = await SyncWithhold.WithheldIdsAsync(
            protection, ctx, [.. candidates.Select(f => f.Id)], ct);

        return await ToDtosAsync(db, visible, [.. candidates.Where(f => !withheld.Contains(f.Id))], ct);
    }

    /// <summary>
    /// Fills in the two things a row cannot carry on its own: the stable code of its kind, and the
    /// containment edges above it. Parent edges are restricted to parents this caller may read, so
    /// the payload never names a row somebody cannot ask about.
    /// </summary>
    internal static async Task<List<SyncFeatureDto>> ToDtosAsync(
        SilexGisDbContext db, IQueryable<Feature> visible, List<Feature> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(f => f.Id).ToList();
        var typeIds = rows.Where(f => f.FeatureTypeId is not null).Select(f => f.FeatureTypeId!.Value).Distinct().ToList();
        var codes = typeIds.Count == 0
            ? []
            : await db.FeatureTypes.AsNoTracking()
                .Where(t => typeIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Code, ct);

        var edges = await db.FeatureHierarchyEdges.AsNoTracking()
            .Where(e => ids.Contains(e.ChildId) && visible.Any(v => v.Id == e.ParentId))
            .Select(e => new { e.ChildId, e.ParentId, e.IsPrimary })
            .ToListAsync(ct);

        return [.. rows.Select(f => new SyncFeatureDto(
            f.Id,
            f.Kind,
            f.FeatureTypeId is { } typeId && codes.TryGetValue(typeId, out var code) ? code : null,
            f.Category,
            f.Name,
            f.Description,
            f.Geom is null ? null : GeoJsonGeometry.From(f.Geom),
            JsonSerializer.Deserialize<JsonElement>(f.Properties),
            f.PropertiesSchemaVersion,
            f.LocationProtected,
            f.IsProtectedEffective,
            f.Visibility,
            [.. edges.Where(e => e.ChildId == f.Id)
                .OrderByDescending(e => e.IsPrimary).ThenBy(e => e.ParentId)
                .Select(e => new SyncParentDto(e.ParentId, e.IsPrimary))],
            f.CreatedAt,
            f.UpdatedAt,
            f.ClientUpdatedAt))];
    }
}
