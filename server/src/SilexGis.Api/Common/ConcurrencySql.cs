// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Common;

/// <summary>Allow-listed tables for the concurrency-version lookup (never user input).</summary>
public enum VersionedTable
{
    Caves,
    CaveEntrances,
    SurfaceFeatures,
    Geofiles,
    GeoreferencedMaps,
    TripLogs,
    SurveyModels,
}

/// <summary>
/// Raw SQL for optimistic concurrency (raw SQL lives only in *Sql.cs files). PostgreSQL's
/// xmin system column is the version token — it changes on every row update without any
/// schema change; reading it explicitly avoids provider-specific model mapping.
/// </summary>
public static class ConcurrencySql
{
    private static readonly IReadOnlyDictionary<VersionedTable, string> Tables =
        new Dictionary<VersionedTable, string>
        {
            [VersionedTable.Caves] = "caves",
            [VersionedTable.CaveEntrances] = "cave_entrances",
            [VersionedTable.SurfaceFeatures] = "surface_features",
            [VersionedTable.Geofiles] = "geofiles",
            [VersionedTable.GeoreferencedMaps] = "georeferenced_maps",
            [VersionedTable.TripLogs] = "trip_logs",
            [VersionedTable.SurveyModels] = "survey_models",
        };

    /// <summary>Current row version, or null when the row does not exist.</summary>
    public static async Task<long?> VersionAsync(
        SilexGisDbContext db, VersionedTable table, Guid id, CancellationToken ct)
    {
        // xid has no direct integer cast; text round-trip is the documented conversion.
        var sql = $"SELECT xmin::text::bigint FROM {Tables[table]} WHERE id = @id";
        return await db.Database.GetDbConnection().QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }
}
