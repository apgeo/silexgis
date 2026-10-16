// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Features;

/// <summary>
/// Raw SQL for centerline measurements (raw SQL lives only in *Sql.cs files). Length is
/// computed by PostGIS on the geography type — geodesic on the spheroid — instead of a
/// hand-rolled approximation, so the number matches what any GIS tool reports.
/// </summary>
/// <remarks>
/// Here rather than beside the upload endpoint because a centerline is now also written by a
/// background job, which cannot reach the web layer, and both must report the same length for the
/// same passage.
/// </remarks>
public static class CenterlineSql
{
    /// <summary>
    /// Geodesic length in meters of a 4326 geometry: along the line, rise included, when every
    /// position carries an altitude; in plan when any does not.
    /// </summary>
    public static async Task<decimal> GeodesicLengthMetersAsync(
        SilexGisDbContext db, Geometry geometry, CancellationToken ct)
    {
        // The geography length carries a line's heights — a 3D line 100.92 m long in plan with
        // 1000 m of rise measures 1005.08 — so a survey's length is its length along the passage,
        // and the heights are written whenever the line has them. A flat geometry has NaN
        // altitudes, and written with a Z flag those turn the whole length into NaN: it is
        // written flat, and measured in plan, because it has no heights to measure.
        var hasHeights = !geometry.IsEmpty && geometry.Coordinates.All(c => !double.IsNaN(c.Z));
        var wkb = new WKBWriter(ByteOrder.LittleEndian, handleSRID: false, emitZ: hasHeights).Write(geometry);
        const string sql = "SELECT ST_Length(ST_SetSRID(ST_GeomFromWKB(@wkb), 4326)::geography)::numeric(12,2)";
        return await db.Database.GetDbConnection().QuerySingleAsync<decimal>(
            new CommandDefinition(sql, new { wkb }, cancellationToken: ct));
    }
}
