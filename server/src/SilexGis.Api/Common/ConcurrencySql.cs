// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

    /// <summary>
    /// A live trip's version, taking the row's lock until the surrounding transaction ends. A
    /// writer that holds the lock and then finds the version it was shown is still the current
    /// one knows nobody else's write can land before its own commits; a writer that waited behind
    /// another reads the version that one left. Needs a transaction on the context.
    /// </summary>
    public static async Task<long?> LockLiveTripLogVersionAsync(
        SilexGisDbContext db, Guid id, CancellationToken ct) =>
        await db.Database.GetDbConnection().QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition(
                LiveTripLogVersion + " FOR UPDATE",
                new { id },
                transaction: db.Database.CurrentTransaction?.GetDbTransaction(),
                cancellationToken: ct));

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

    /// <summary>
    /// One version for a run of a series' occurrences: this day and every later one. It changes
    /// when any of them is updated, when one is added or removed, and when one is moved into or
    /// out of the run, which is exactly the set an edit of "this and the following" writes.
    /// </summary>
    /// <remarks>
    /// A digest of each member's own row version rather than a sum or a maximum, so that two
    /// changes cannot cancel one another and a row dropping out cannot leave the figure where it
    /// was. Hexadecimal, so it survives being carried as an entity tag. The series has at least
    /// the occurrence it was asked about; a series that has no rows answers null.
    /// </remarks>
    /// <param name="lockRows">
    /// Locks the run's rows until the surrounding transaction ends, so that the version returned
    /// stays true while the caller writes. Needs a transaction on the context; without one the
    /// lock is released as soon as the statement ends.
    /// </param>
    public static async Task<string?> EventSeriesFollowingVersionAsync(
        SilexGisDbContext db, Guid seriesId, DateOnly fromDay, CancellationToken ct,
        bool lockRows = false)
    {
        var sql = lockRows
            ? """
              SELECT md5(string_agg(id::text || ':' || xmin::text, ',' ORDER BY id))
              FROM (SELECT id, xmin FROM events
                    WHERE series_id = @seriesId AND start_date >= @fromDay
                    ORDER BY id FOR UPDATE) run
              """
            : """
              SELECT md5(string_agg(id::text || ':' || xmin::text, ',' ORDER BY id))
              FROM events
              WHERE series_id = @seriesId AND start_date >= @fromDay
              """;
        return await db.Database.GetDbConnection().QuerySingleOrDefaultAsync<string?>(
            new CommandDefinition(
                sql,
                new { seriesId, fromDay },
                transaction: db.Database.CurrentTransaction?.GetDbTransaction(),
                cancellationToken: ct));
    }
}
