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
}
