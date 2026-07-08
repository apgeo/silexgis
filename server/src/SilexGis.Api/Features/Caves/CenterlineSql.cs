// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// Raw SQL for centerline measurements (raw SQL lives only in *Sql.cs files). Length is
/// computed by PostGIS on the geography type — geodesic on the spheroid — instead of a
/// hand-rolled approximation, so the number matches what any GIS tool reports.
/// </summary>
public static class CenterlineSql
{
    /// <summary>Geodesic length in meters of a 4326 geometry.</summary>
    public static async Task<decimal> GeodesicLengthMetersAsync(
        SilexGisDbContext db, Geometry geometry, CancellationToken ct)
    {
        var wkb = new WKBWriter(ByteOrder.LittleEndian, handleSRID: false, emitZ: true).Write(geometry);
        const string sql = "SELECT ST_Length(ST_SetSRID(ST_GeomFromWKB(@wkb), 4326)::geography)::numeric(12,2)";
        return await db.Database.GetDbConnection().QuerySingleAsync<decimal>(
            new CommandDefinition(sql, new { wkb }, cancellationToken: ct));
    }
}
