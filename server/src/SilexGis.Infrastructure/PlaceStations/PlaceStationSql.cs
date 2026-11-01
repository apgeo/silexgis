// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.PlaceStations;

/// <summary>The raw statements of the place-at-station writer (raw SQL lives only in *Sql.cs files).</summary>
public static class PlaceStationSql
{
    /// <summary>
    /// Takes the place's row lock for the rest of the surrounding transaction. "One live mapping per
    /// place in a survey" spans two member rows, so no index can state it; every write that could
    /// break it takes this lock before it looks, so two writers of one place queue here and the
    /// second sees what the first wrote.
    /// </summary>
    public static Task LockPlaceAsync(SilexGisDbContext db, Guid placeId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM features WHERE id = {placeId} FOR UPDATE", ct);
    }
}
