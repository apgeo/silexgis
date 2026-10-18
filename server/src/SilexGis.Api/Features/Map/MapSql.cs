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
    /// <summary>
    /// The planner settings the cluster statement is always sent under. It splices the access
    /// checks into every entrance in the window — a country-wide window is every entrance there
    /// is — so it is the statement the planner's estimate of those checks misleads most: it would
    /// compile it (costing more than running it) and probe each row's ancestors through a bitmap
    /// (costing twice the direct index walk).
    /// </summary>
    public static readonly IReadOnlyList<string> ClusterSettings =
        [PlannerSql.WithoutJit, PlannerSql.WithoutBitmapScans];

    public static Task<FeatureCollection> ClustersAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        Bbox box,
        int zoom,
        double protectionGridMeters,
        string? tag,
        IReadOnlyCollection<Guid>? onlyIds,
        CancellationToken ct) =>
        ClustersAsync(db, FeatureAudienceSql.Caller(ctx, "f"), box, zoom, protectionGridMeters, tag, onlyIds, ct);

    /// <summary>
    /// The clusters an audience is shown — a signed-in caller, or a shared view's link. A non-null
    /// <paramref name="onlyIds"/> keeps only those entrances (a filter, resolved to the entrances it
    /// selects); null keeps every entrance the audience may see.
    /// </summary>
    public static async Task<FeatureCollection> ClustersAsync(
        SilexGisDbContext db,
        FeatureAudience audience,
        Bbox box,
        int zoom,
        double protectionGridMeters,
        string? tag,
        IReadOnlyCollection<Guid>? onlyIds,
        CancellationToken ct)
    {
        if (onlyIds is { Count: 0 })
        {
            return FeatureCollection.Of([]);
        }

        var (sql, parameters, clusterCellDegrees) = BuildClusters(audience, box, zoom, protectionGridMeters, tag, onlyIds);
        var connection = db.Database.GetDbConnection();

        var rows = await PlannerSql.QueryAsync<(double Lon, double Lat, int Count, double CellX, double CellY)>(
            connection, sql, parameters, ClusterSettings, ct);

        // Each cluster names the lattice cell it summed over. The lattice is global and the
        // same for every caller, so its bounds say nothing about any entrance; a client that
        // lists a cluster's members asks for exactly this cell instead of re-deriving the
        // lattice arithmetic and drifting from it.
        var features = rows
            .Select(r => GeoFeature.Of(
                new Point(r.Lon, r.Lat) { SRID = 4326 },
                new Dictionary<string, object?>
                {
                    ["cluster"] = true,
                    ["count"] = r.Count,
                    ["cell"] = new[]
                    {
                        (r.CellX - 0.5) * clusterCellDegrees,
                        (r.CellY - 0.5) * clusterCellDegrees,
                        (r.CellX + 0.5) * clusterCellDegrees,
                        (r.CellY + 0.5) * clusterCellDegrees,
                    },
                }))
            .ToList();

        return FeatureCollection.Of(features);
    }

    /// <summary>
    /// The cluster statement, its parameters and the lattice cell size it sums over — exposed so
    /// the plan pins explain the statement this route sends rather than a copy of it.
    /// </summary>
    public static (string Sql, DynamicParameters Parameters, double ClusterCellDegrees) BuildClusters(
        AccessContext ctx,
        Bbox box,
        int zoom,
        double protectionGridMeters,
        string? tag,
        IReadOnlyCollection<Guid>? onlyIds = null) =>
        BuildClusters(FeatureAudienceSql.Caller(ctx, "f"), box, zoom, protectionGridMeters, tag, onlyIds);

    /// <summary>The cluster statement for an audience over the alias <c>f</c>.</summary>
    public static (string Sql, DynamicParameters Parameters, double ClusterCellDegrees) BuildClusters(
        FeatureAudience audience,
        Bbox box,
        int zoom,
        double protectionGridMeters,
        string? tag,
        IReadOnlyCollection<Guid>? onlyIds = null)
    {
        // ~64 px cluster cells on a 256 px tile pyramid.
        var clusterCellDegrees = 360d / Math.Pow(2, zoom) / 4d;
        var protectionCellDegrees = LocationProtection.CellDegrees(protectionGridMeters);

        var (visibilitySql, exactSql, parameters) = (audience.VisibleSql, audience.ExactSql, audience.Parameters);
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

        // A filter arrives as the ids of the entrances it selects: its conditions are an EF
        // predicate this statement cannot embed. The ids narrow the candidates, before the snap
        // and the window test, so a cluster counts the filtered set exactly as the window decides
        // it for everyone else — a protected entrance is in or out by its shown point whether a
        // filter is on or not.
        var onlySql = string.Empty;
        if (onlyIds is not null)
        {
            onlySql = """

                  AND f.id = ANY(@only_ids)
                """;
            parameters.Add("only_ids", AccessSql.UuidArray(onlyIds as Guid[] ?? [.. onlyIds]));
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
        //
        // The exact-view walk is asked once per row, in a materialised step: the shown point is
        // read by the window test, the averages and the grouping, and an inlined expression would
        // run the walk's subplans at every one of those references.
        //
        // And it is asked only of the rows it can refuse. A row no protection root stands above
        // — itself included, soft-deleted ones still counting — is placed exactly by every
        // reader, which is precisely what the row's own effective-protection flag records; every
        // write maintains it and the integrity verifier walks the whole table against it. So the
        // flag decides the ordinary row and the walk decides the rest, instead of an index probe
        // per candidate proving over and over that nothing above the row is protected. Measured
        // over a hundred thousand entrances with a quarter of their caves protected: 1,086 ms
        // down to 545 ms for a reader who owns none of them, and no change at all for the owner,
        // whose own arm answers before either. The proximity pool reads this way already, and a
        // shared view's link places rows by this flag alone; the plan pins on this statement go
        // on checking that the walk, for the rows that still need it, is reached by an index.
        var sql = $"""
            WITH candidates AS MATERIALIZED (
                SELECT ST_X(f.geom) AS x, ST_Y(f.geom) AS y,
                       (NOT f.is_protected_effective OR {exactSql}) AS exact
                FROM features f
                WHERE f.kind = {(short)FeatureKind.CaveEntrance}
                  AND f.deleted_at IS NULL
                  AND f.geom && ST_Expand(
                        ST_MakeEnvelope(@west, @south, @east, @north, 4326), @protection_cell)
                  AND {visibilitySql}{tagSql}{onlySql}
            ),
            placed AS (
                SELECT
                    CASE WHEN c.exact THEN c.x ELSE {SpatialSql.SnapToGrid("c.x", "protection_cell")} END AS gx,
                    CASE WHEN c.exact THEN c.y ELSE {SpatialSql.SnapToGrid("c.y", "protection_cell")} END AS gy
                FROM candidates c
            )
            SELECT avg(g.gx) AS lon, avg(g.gy) AS lat, COUNT(*)::int AS count,
                   g.cx AS cellX, g.cy AS cellY
            FROM (
                SELECT p.gx, p.gy,
                       round(p.gx / @cluster_cell) AS cx,
                       round(p.gy / @cluster_cell) AS cy
                FROM placed p
                WHERE p.gx BETWEEN @west AND @east
                  AND p.gy BETWEEN @south AND @north
            ) g
            GROUP BY g.cx, g.cy
            """;

        return (sql, parameters, clusterCellDegrees);
    }
}
