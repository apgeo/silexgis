// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Import;

/// <summary>One parsed row of an uploaded file, as the classification scan reads it.</summary>
public sealed record SourceRow(long Id, string GeometryType, string Properties);

/// <summary>
/// Raw SQL for the staged import (raw SQL lives only in *Sql.cs files).
///
/// <para>
/// The classification scan reads every row of a file to answer "which rule claimed how many",
/// which is the dry run. It deliberately does not read the geometries: a file holding a few
/// hundred waypoints also holds the tracks walked between them, and those carry tens of
/// thousands of vertices each. What it needs is the shape's *class* — a point is a candidate,
/// a line is a track — which the database can say in one word without sending the shape.
/// </para>
/// </summary>
public static class ImportSql
{
    public static async Task<List<SourceRow>> ScanAsync(
        SilexGisDbContext db, Guid geofileId, int limit, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = new NpgsqlCommand(
            """
            select id, st_geometrytype(geom) as geometry_type, properties::text
            from geofile_features
            where geofile_id = @geofile_id
            order by id
            limit @limit
            """,
            connection);
        command.Parameters.AddWithValue("geofile_id", NpgsqlDbType.Uuid, geofileId);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);

        var rows = new List<SourceRow>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new SourceRow(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                reader.IsDBNull(2) ? "{}" : reader.GetString(2)));
        }

        return rows;
    }

    /// <summary>How many rows the file holds, whatever the scan's ceiling.</summary>
    public static Task<int> CountAsync(SilexGisDbContext db, Guid geofileId, CancellationToken ct) =>
        db.GeofileFeatures.CountAsync(f => f.GeofileId == geofileId, ct);
}
