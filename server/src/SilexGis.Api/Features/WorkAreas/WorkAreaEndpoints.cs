// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.WorkAreas;

/// <summary>
/// The stretches of country this club works — a massif, a karst zone, a valley — and the levels
/// beneath them, as one answer.
///
/// <para>
/// One request rather than one per level, because there are tens of these and not thousands: a
/// club works the ground it can reach. Answering the whole tree at once lets the board that lists
/// them, the overview that colours them and the map that zooms to one all read the same answer,
/// so they cannot disagree about which areas exist or where one of them is. Levelling is left to
/// the reader: an area's parent is stated, and which level a row sits at follows from that.
/// </para>
///
/// <para>
/// A work area is a feature of the work-area kind, so it is filtered by exactly the rule every
/// other feature is filtered by, and it carries a name, a description, a shape and links like any
/// other. Nothing here is a second access path.
/// </para>
///
/// <para>
/// The shape is emitted as drawn. A work area is a stretch of country somebody outlined on a plan
/// — no more a position than a permit boundary is — and it is not the coordinates of anything
/// inside it: the caves within one are answered, and protected, by the endpoints that answer
/// caves. An area whose own row is marked protected is the one case where that is not obviously
/// true, and it is left out entirely rather than drawn snapped, because a work area snapped to the
/// protection grid is a shape of the wrong size in the wrong place, which reads as fact.
/// </para>
/// </summary>
public static class WorkAreaEndpoints
{
    /// <summary>
    /// The kind a work area is declared by, resolved to an identity by code at request time and
    /// never carried as a number: the same row is numbered differently on a fresh installation
    /// than on one that grew into it. Taken from the shared constant the seeder writes the row
    /// under, because this board answers empty when it cannot find the kind — a second spelling
    /// of the code here would empty it silently and for ever.
    /// </summary>
    private const string WorkAreaTypeCode = FeatureTypeSeeds.WorkArea;

    /// <summary>
    /// Safety cap. Ordered by name and then id before it bites, so an installation over the cap
    /// answers with the same areas every time rather than an arbitrary subset that moves under
    /// the reader — and the answer says it was capped.
    /// </summary>
    private const int MaxAreas = 500;

    public static RouteGroupBuilder MapWorkAreaEndpoints(this RouteGroupBuilder api)
    {
        api.MapGroup("/work-areas").WithTags("WorkAreas")
            .MapGet("/", GetAsync)
            .WithSummary(
                "Every work area this caller may read, with its shape, its description, the area "
                + "it sits inside and how many sit inside it. Takes no filter.");

        return api;
    }

    private static async Task<Results<Ok<WorkAreaCollectionDto>, UnauthorizedHttpResult>> GetAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var typeId = await db.FeatureTypes.AsNoTracking()
            .Where(t => t.Code == WorkAreaTypeCode)
            .Select(t => (long?)t.Id)
            .FirstOrDefaultAsync(ct);
        if (typeId is null)
        {
            // The kind is seeded at startup, so this is the shape of an installation whose seeding
            // has not run rather than of one with no areas. Both are "nothing to show" to a
            // reader, and neither is this endpoint's to repair.
            return TypedResults.Ok(new WorkAreaCollectionDto([], false));
        }

        var visible = db.Features.AsNoTracking()
            .Where(f => f.DeletedAt == null && f.FeatureTypeId == typeId)
            .VisibleTo(ctx, db.Features.Where(f => f.DeletedAt == null), db.FeatureSetMembers);

        var rows = await visible
            // Protected areas are left out rather than snapped — see the note on the type.
            .Where(f => !f.IsProtectedEffective)
            .OrderBy(f => f.Name).ThenBy(f => f.Id)
            .Take(MaxAreas + 1)
            .Select(f => new { f.Id, f.Name, f.Description, f.Geom })
            .ToListAsync(ct);

        var truncated = rows.Count > MaxAreas;
        if (truncated)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var ids = rows.Select(r => r.Id).ToList();

        // The area each one sits inside, and only where the parent is itself a work area this
        // caller may read. A work area nested under a cave system the reader cannot see must not
        // report that parent — the name would be absent but the link would still say something
        // is there. Restricting to ids already in `rows` settles both at once.
        var parents = await db.FeatureHierarchyEdges.AsNoTracking()
            .Where(e => e.IsPrimary && ids.Contains(e.ChildId) && ids.Contains(e.ParentId))
            .Select(e => new { e.ChildId, e.ParentId })
            .ToListAsync(ct);
        var parentOf = parents.ToDictionary(x => x.ChildId, x => x.ParentId);

        var childCount = parents
            .GroupBy(x => x.ParentId)
            .ToDictionary(g => g.Key, g => g.Count());

        var items = rows.Select(r => new WorkAreaDto(
            r.Id,
            r.Name,
            r.Description,
            parentOf.TryGetValue(r.Id, out var parent) ? parent : null,
            childCount.TryGetValue(r.Id, out var children) ? children : 0,
            r.Geom is null ? null : GeoJsonGeometry.From(r.Geom))).ToList();

        return TypedResults.Ok(new WorkAreaCollectionDto(items, truncated));
    }
}
