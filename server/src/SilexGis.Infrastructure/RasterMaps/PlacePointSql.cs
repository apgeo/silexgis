// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.RasterMaps;

/// <summary>The raw statements of the place-pin writer (raw SQL lives only in *Sql.cs files).</summary>
public static class PlacePointSql
{
    /// <summary>
    /// Takes the map's row lock for the rest of the surrounding transaction. "One live pin per
    /// place on a map" spans two member rows, so no index can state it; every write that could
    /// break it takes this lock before it looks, so two writers of one map queue here and the
    /// second sees what the first wrote.
    /// </summary>
    public static Task LockMapAsync(SilexGisDbContext db, Guid mapId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM raster_maps WHERE id = {mapId} FOR UPDATE", ct);
    }
}
