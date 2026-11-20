// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.PlaceStations;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.ResLinks;

/// <summary>
/// The browser's side of the places phones mapped to survey stations: shown in the linked-items
/// lists like any link, and removable there through the ordinary link delete. Nothing in the
/// browser makes or edits one; the phones do.
/// </summary>
/// <remarks>
/// A mapping is curated — removed, and offered for removal — by whoever may change the place it
/// maps, the right a phone needs to make it, and by nobody else.
/// </remarks>
public static class PlaceStationEndpoints
{
    /// <summary>
    /// A mapping removed from the browser: stamped, so the phones are told it went, by whoever may
    /// change its place. The caller has already been shown the mapping.
    /// </summary>
    internal static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> RemoveAsync(
        SilexGisDbContext db, PlaceStationStore store, IAccessService access, AccessContext ctx, Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // The place's lock comes before the mapping is read, as for every write to a mapping: a phone
        // that removed the same mapping meanwhile has committed by then, and the read below sees it.
        var placeId = await PlaceStationQueries.Rows(db).AsNoTracking()
            .Where(r => r.Id == id).Select(r => (Guid?)r.PlaceId).FirstOrDefaultAsync(ct);
        if (placeId is null)
        {
            return ApiProblems.NotFound(ResLinkEndpoints.NotFoundCode);
        }

        await store.LockAsync(placeId.Value, ct);
        var mapping = await store.FindAsync(id, ct);
        if (mapping is null)
        {
            return ApiProblems.NotFound(ResLinkEndpoints.NotFoundCode);
        }

        // The link may already be tracked from the read that showed it to the caller, and a tracked
        // row is handed back as it was then, not as it is now.
        await db.Entry(mapping.Link).ReloadAsync(ct);

        if (!(await WritablePlacesAsync(db, access, ctx, [mapping.PlaceId], ct)).Contains(mapping.PlaceId))
        {
            return ApiProblems.Forbidden(detail: "Removing a place's station takes the right to change the place.");
        }

        store.Remove(mapping, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>Of these mappings, the ones this caller may remove: those of places it may change.</summary>
    internal static async Task<HashSet<Guid>> WritableAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var places = await PlaceStationQueries.Rows(db).AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .Select(r => new { r.Id, r.PlaceId })
            .ToListAsync(ct);
        // One read of the places and one decision over all of them: a well-mapped survey model holds a
        // mapping per place, and asking place by place cost two queries each on every listing.
        var writable = await WritablePlacesAsync(db, access, ctx, [.. places.Select(p => p.PlaceId).Distinct()], ct);
        return [.. places.Where(p => writable.Contains(p.PlaceId)).Select(p => p.Id)];
    }

    /// <summary>Of these places, the ones this caller may change — the right a mapping is curated by.</summary>
    private static async Task<HashSet<Guid>> WritablePlacesAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, IReadOnlyCollection<Guid> placeIds, CancellationToken ct)
    {
        var places = await db.Features.AsNoTracking().Where(f => placeIds.Contains(f.Id)).ToListAsync(ct);
        return await ProtectedWrites.WritableAsync(access, ctx, places, ct);
    }
}
