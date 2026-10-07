// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Common;

/// <summary>Allow-listed tables for the concurrency-version lookup (never user input).</summary>
public enum VersionedTable
{
    /// <summary>
    /// The feature supertype row. Every feature kind — caves, entrances, centerlines and
    /// generic features — versions here: a subtype edit also touches its feature row, so one
    /// token covers the whole aggregate and a concurrent edit of any part is detected.
    /// </summary>
    Features,
    TripLogs,
    Geofiles,
    GeoreferencedMaps,
    MapViews,
    SurveyModels,
    Files,
    Expeditions,
    Events,
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
            [VersionedTable.Features] = "features",
            [VersionedTable.Geofiles] = "geofiles",
            [VersionedTable.GeoreferencedMaps] = "georeferenced_maps",
            [VersionedTable.MapViews] = "map_views",
            [VersionedTable.SurveyModels] = "survey_models",
            [VersionedTable.Files] = "files",
            [VersionedTable.Expeditions] = "expeditions",
            [VersionedTable.Events] = "events",
        };

    /// <summary>
    /// A trip's version, as a statement of its own rather than a row in the table of names: a
    /// deleted trip is kept in this table, hidden from every read the model makes, and a
    /// statement written by hand passes through none of that. So it says so itself — a deleted
    /// trip has no version to hand out and none to compare a precondition against, which is the
    /// same answer a trip that never existed gives.
    /// </summary>
    private const string LiveTripLogVersion =
        "SELECT xmin::text::bigint FROM trip_logs WHERE id = @id AND deleted_at IS NULL";

    /// <summary>Current row version, or null when the row does not exist.</summary>
    public static async Task<long?> VersionAsync(
        SilexGisDbContext db, VersionedTable table, Guid id, CancellationToken ct)
    {
        // xid has no direct integer cast; text round-trip is the documented conversion.
        var sql = table == VersionedTable.TripLogs
            ? LiveTripLogVersion
            : $"SELECT xmin::text::bigint FROM {Tables[table]} WHERE id = @id";
        return await db.Database.GetDbConnection().QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }
}
