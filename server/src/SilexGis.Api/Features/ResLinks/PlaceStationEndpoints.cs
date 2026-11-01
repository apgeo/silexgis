// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
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
        var mapping = await store.FindAsync(id, ct);
        if (mapping is null)
        {
            return ApiProblems.NotFound(ResLinkEndpoints.NotFoundCode);
        }

        await store.LockAsync(mapping.PlaceId, ct);
        if (!await MayWritePlaceAsync(db, access, ctx, mapping.PlaceId, ct))
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
        var writable = new HashSet<Guid>();
        foreach (var place in places.Select(p => p.PlaceId).Distinct())
        {
            if (await MayWritePlaceAsync(db, access, ctx, place, ct))
            {
                writable.Add(place);
            }
        }

        return [.. places.Where(p => writable.Contains(p.PlaceId)).Select(p => p.Id)];
    }

    private static async Task<bool> MayWritePlaceAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, Guid placeId, CancellationToken ct)
    {
        var place = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == placeId, ct);
        return place is not null && (await access.DecideAsync(ctx, AccessAction.Write, place, ct)).Allowed;
    }
}
