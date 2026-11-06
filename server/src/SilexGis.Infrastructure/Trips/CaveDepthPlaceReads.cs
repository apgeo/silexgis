// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Trips;

/// <summary>The depths a cave has declared the meaning of, read once for every path that places a depth.</summary>
public static class CaveDepthPlaceReads
{
    /// <summary>
    /// The depths a cave has declared the meaning of — station and name — ordered by depth.
    /// </summary>
    /// <remarks>
    /// Only the places that have a depth. A place the cave names without one says nothing about
    /// any depth, so it is never a candidate for placing a report, recording one, correcting one,
    /// importing a sheet of them, converting a phone's recording or previewing where a depth would
    /// land — every one of which reads through here — and the chooser of places a report can be
    /// made at, which reads here too, offers only places that a depth stands for.
    /// </remarks>
    public static async Task<IReadOnlyList<DeclaredDepthPlaces.Declared>> DeclaredOfAsync(
        SilexGisDbContext db, Guid caveFeatureId, CancellationToken ct)
    {
        var rows = await db.CaveDepthPlaces.AsNoTracking()
            .Where(p => p.CaveFeatureId == caveFeatureId)
            .WithDepth()
            .OrderBy(p => p.DepthM)
            .Select(p => new { DepthM = p.DepthM!.Value, p.ViewerStationName, p.PlaceLabel })
            .ToListAsync(ct);

        return [.. rows.Select(r =>
            new DeclaredDepthPlaces.Declared(r.DepthM, r.ViewerStationName, r.PlaceLabel))];
    }
}
