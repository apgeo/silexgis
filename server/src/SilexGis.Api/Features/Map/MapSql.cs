// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Map;

/// <summary>
/// Dapper spatial SQL for the map slice (raw SQL lives only in *Sql.cs files).
/// Clustering: grid aggregation over coordinates. Protected entrance coordinates enter the
/// aggregation already obfuscated with the SAME grid as LocationProtection.Snap (round to
/// nearest multiple of the cell) so cluster centroids can never leak an exact location.
/// </summary>
public static class MapSql
{
    public static async Task<FeatureCollection> ClustersAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        Bbox box,
        int zoom,
        double protectionGridMeters,
        string? tag,
        CancellationToken ct)
    {
        // ~64 px cluster cells on a 256 px tile pyramid.
        var clusterCellDegrees = 360d / Math.Pow(2, zoom) / 4d;
        var protectionCellDegrees = LocationProtection.CellDegrees(protectionGridMeters);

        var (visibilitySql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("west", box.West);
        parameters.Add("south", box.South);
        parameters.Add("east", box.East);
        parameters.Add("north", box.North);
        parameters.Add("cluster_cell", clusterCellDegrees);
        parameters.Add("protection_cell", protectionCellDegrees);

        // Optional tag filter over the entrance feature's own taggings.
        var tagSql = string.Empty;
        if (!string.IsNullOrWhiteSpace(tag))
        {
            tagSql = """

                  AND EXISTS (SELECT 1 FROM taggings tg
                              JOIN tags t ON t.id = tg.tag_id
                              WHERE tg.feature_id = f.id AND t.slug = @tag)
                """;
            parameters.Add("tag", tag);
        }

        // Entrance features carry their access trio row-locally, so visibility needs no
        // join; the exact-view rule resolves protection roots from the row's ancestor
        // array. Cluster centroid of points == arithmetic mean of coordinates; avg(x)/
        // avg(y) avoids materializing ST_Collect geometry collections (which dominated
        // cost at 50k rows). The snap comes from the shared fragment, whose numeric cast
        // is what makes SQL round half away from zero as LocationProtection.Snap does —
        // round(double precision) rounds half to EVEN, and this statement carried that
        // wrong form for a while, disagreeing with every other snap at halfway values.
        //
        // Which rows are in the window is decided on the point that is counted (p.gx/p.gy),
        // never on the stored one. Tested on the stored point, the count is a probe: moving
        // one bbox edge in small steps changes it exactly when the edge crosses a protected
        // entrance's true position, which recovers that position far below the protection
        // grid without a coordinate ever being returned. The index prefilter on f.geom is
        // widened by one protection cell so a row whose snapped point lands inside the
        // window is still fetched; a snap moves a coordinate by at most half a cell on each
        // axis, in the same degrees on both, so one cell is a safe margin anywhere.
        var sql = $"""
            SELECT avg(g.gx) AS lon, avg(g.gy) AS lat, COUNT(*)::int AS count
            FROM (
                SELECT p.gx, p.gy,
                       round(p.gx / @cluster_cell) AS cx,
                       round(p.gy / @cluster_cell) AS cy
                FROM (
                    SELECT
                        CASE WHEN {exactSql}
                            THEN ST_X(f.geom)
                            ELSE {SpatialSql.SnapToGrid("ST_X(f.geom)", "protection_cell")}
                        END AS gx,
                        CASE WHEN {exactSql}
                            THEN ST_Y(f.geom)
                            ELSE {SpatialSql.SnapToGrid("ST_Y(f.geom)", "protection_cell")}
                        END AS gy
                    FROM features f
                    WHERE f.kind = {(short)FeatureKind.CaveEntrance}
                      AND f.deleted_at IS NULL
                      AND f.geom && ST_Expand(
                            ST_MakeEnvelope(@west, @south, @east, @north, 4326), @protection_cell)
                      AND {visibilitySql}{tagSql}
                ) p
                WHERE p.gx BETWEEN @west AND @east
                  AND p.gy BETWEEN @south AND @north
            ) g
            GROUP BY g.cx, g.cy
            """;

        var connection = db.Database.GetDbConnection();
        var rows = await connection.QueryAsync<(double Lon, double Lat, int Count)>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        var features = rows
            .Select(r => GeoFeature.Of(
                new Point(r.Lon, r.Lat) { SRID = 4326 },
                new Dictionary<string, object?> { ["cluster"] = true, ["count"] = r.Count }))
            .ToList();

        return FeatureCollection.Of(features);
    }
}
