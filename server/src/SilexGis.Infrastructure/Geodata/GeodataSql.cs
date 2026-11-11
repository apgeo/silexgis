// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// Raw SQL for geodata bulk operations (raw SQL lives only in *Sql.cs files).
/// Imports go through binary COPY — geofiles can carry 100k+ rows, which would
/// take minutes through EF change tracking and seconds through COPY.
/// </summary>
public static class GeodataSql
{
    public static async Task BulkInsertFeaturesAsync(
        SilexGisDbContext db, Guid geofileId, IReadOnlyList<VectorFeature> features, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using (var importer = await connection.BeginBinaryImportAsync(
            "COPY geofile_features (geofile_id, geom, properties) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var feature in features)
            {
                await importer.StartRowAsync(ct);
                await importer.WriteAsync(geofileId, NpgsqlDbType.Uuid, ct);
                await importer.WriteAsync(feature.Geom, NpgsqlDbType.Geometry, ct);
                await importer.WriteAsync(JsonSerializer.Serialize(feature.Properties), NpgsqlDbType.Jsonb, ct);
            }

            await importer.CompleteAsync(ct);
        }

        // Freshly bulk-loaded tables have no planner statistics until autoanalyze runs,
        // and the bbox layer queries them immediately — analyze right away.
        await db.Database.ExecuteSqlRawAsync("ANALYZE geofile_features", ct);
    }

    /// <summary>
    /// Holds one geofile's row until the transaction ends, so that a decision read from it and the
    /// write that follows are made by one caller at a time.
    /// </summary>
    /// <remarks>
    /// Re-importing reads "is an import of this file still to come?" and then queues one. Two
    /// requests that ask together would both hear "no" and both queue, and the two imports would
    /// each replace the other's features. The second waits here instead, and asks once the first
    /// has committed its job.
    /// </remarks>
    public static Task LockGeofileAsync(SilexGisDbContext db, Guid geofileId, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM geofiles WHERE id = {0} FOR UPDATE", [geofileId], ct);

    /// <summary>
    /// Whether an import of this geofile is waiting to run or running.
    /// </summary>
    /// <remarks>
    /// Asked of the queue rather than of the geofile's own status, because the status can be left
    /// saying "uploaded" by a job that ended without reaching the line that moves it on — a stored
    /// file that had gone, a worker lost before its first save — and a status read as "queued" for
    /// good would refuse every re-import of the file for ever. Matched on the geofile's key inside
    /// the payload, the only place a queue row names it.
    /// </remarks>
    public static Task<bool> HasLiveImportAsync(
        SilexGisDbContext db, Guid geofileId, string kind, CancellationToken ct) =>
        db.Database.SqlQueryRaw<bool>(
            """
            SELECT EXISTS (
                SELECT 1 FROM processing_jobs
                WHERE kind = {0} AND status IN (0, 1) AND payload ->> 'geofileId' = {1}) AS "Value"
            """,
            kind, geofileId.ToString()).SingleAsync(ct);
}
