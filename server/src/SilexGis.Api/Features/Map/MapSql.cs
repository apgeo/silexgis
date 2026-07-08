// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Map;

/// <summary>
/// Dapper spatial SQL for the map slice (raw SQL lives only in *Sql.cs files).
/// Clustering: grid aggregation over coordinates. Protected cave coordinates enter the
/// aggregation already obfuscated with the SAME grid as LocationProtection.Snap (round to
/// nearest multiple of the cell) so cluster centroids can never leak an exact location.
/// </summary>
public static class MapSql
{
    public static async Task<FeatureCollection> ClustersAsync(
        SilexGisDbContext db,
        UserContext user,
        Bbox box,
        int zoom,
        double protectionGridMeters,
        string? tag,
        CancellationToken ct)
    {
        // ~64 px cluster cells on a 256 px tile pyramid.
        var clusterCellDegrees = 360d / Math.Pow(2, zoom) / 4d;
        var protectionCellDegrees = LocationProtection.CellDegrees(protectionGridMeters);

        var (visibilitySql, parameters) = PermissionSql.VisibleToFragment(user, "c");
        parameters.Add("west", box.West);
        parameters.Add("south", box.South);
        parameters.Add("east", box.East);
        parameters.Add("north", box.North);
        parameters.Add("cluster_cell", clusterCellDegrees);
        parameters.Add("protection_cell", protectionCellDegrees);

        // Optional tag filter — entity_type 0 = cave (schema-contract value, tested).
        var tagSql = string.Empty;
        if (!string.IsNullOrWhiteSpace(tag))
        {
            tagSql = """
                  AND EXISTS (SELECT 1 FROM taggings tg
                              JOIN tags t ON t.id = tg.tag_id
                              WHERE tg.entity_type = 0 AND tg.entity_id = c.id AND t.slug = @tag)
                """;
            parameters.Add("tag", tag);
        }

        // Cluster centroid of points == arithmetic mean of coordinates; avg(x)/avg(y)
        // avoids materializing ST_Collect geometry collections (which dominated cost at
        // 50k rows). SQL round() rounds half away from zero, exactly matching
        // LocationProtection.Snap (MidpointRounding.AwayFromZero) — keep them identical.
        var sql = $"""
            SELECT avg(g.gx) AS lon, avg(g.gy) AS lat, COUNT(*)::int AS count
            FROM (
                SELECT
                    CASE WHEN (NOT c.location_protected
                               OR @vis_is_admin
                               OR c.owner_user_id = @vis_user_id
                               OR (c.team_id IS NOT NULL AND c.team_id = ANY(@vis_team_ids)))
                        THEN ST_X(e.geom)
                        ELSE round(ST_X(e.geom) / @protection_cell) * @protection_cell
                    END AS gx,
                    CASE WHEN (NOT c.location_protected
                               OR @vis_is_admin
                               OR c.owner_user_id = @vis_user_id
                               OR (c.team_id IS NOT NULL AND c.team_id = ANY(@vis_team_ids)))
                        THEN ST_Y(e.geom)
                        ELSE round(ST_Y(e.geom) / @protection_cell) * @protection_cell
                    END AS gy
                FROM cave_entrances e
                JOIN caves c ON c.id = e.cave_id
                WHERE c.deleted_at IS NULL
                  AND e.geom && ST_MakeEnvelope(@west, @south, @east, @north, 4326)
                  AND {visibilitySql}{tagSql}
            ) g
            GROUP BY round(g.gx / @cluster_cell), round(g.gy / @cluster_cell)
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
