// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Dapper;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>An area in a map window, before the caller's placement of it has been checked.</summary>
public sealed record KarstIndexAreaRow(Guid Id, string? Name, bool IsProtected);

/// <summary>What one area adds up to for its karstification index, with its outline to draw.</summary>
/// <param name="AreaId">The area.</param>
/// <param name="AreaM2">The outline's ground area on the spheroid, from the stored outline.</param>
/// <param name="OutlineGeoJson">The outline simplified for the zoom, as a GeoJSON geometry.</param>
/// <param name="CaveCount">Caves declared under the area, as the area's own statistics count them.</param>
/// <param name="DepressionCount">Depression outlines declared under it.</param>
/// <param name="DepressionAreaM2">Ground under those outlines, summed; null when there are none.</param>
public sealed record KarstIndexTotalsRow(
    Guid AreaId,
    double? AreaM2,
    string? OutlineGeoJson,
    int CaveCount,
    int DepressionCount,
    double? DepressionAreaM2);

/// <summary>
/// The karstification index over many areas at once: which areas a map window holds, and what each
/// adds up to — two statements, however many areas the window holds.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which areas.</b> An outline places itself, so an area is in a window only when the caller may
/// place it exactly, and then by its stored outline. An unprotected area is in by its outline; a
/// protected one only through the shared window-membership rule, which puts a protected outline
/// the caller may not place in no window at all — its shape never decides an answer, and nothing
/// says it was left out. The outline test is spelled as the outline rule's SQL twin, so a
/// collection that the rule would not call an outline is not one here either.
/// </para>
/// <para>
/// <b>What each area adds up to</b> is counted exactly as one area's own statistics count it: caves
/// and depressions under the area by the declared containment chain — never by being inside the
/// outline, which would make every protected cave's true position a probe — over only what the
/// caller may read, and the area itself excluded. Only the half of those statistics the index is
/// built from is computed here.
/// </para>
/// <para>
/// <b>The ids passed to the totals statement must already be gated.</b> The area rows are read there
/// without a visibility fragment, because a second fragment cannot share the first one's parameter
/// set; the caller has checked every id readable and exactly placeable before it gets here.
/// </para>
/// </remarks>
public static class KarstIndexWindowSql
{
    /// <summary>
    /// The readable outlines of the given types in the window by the membership rule, in id order,
    /// at most <paramref name="limit"/> of them.
    /// </summary>
    /// <param name="protectedMembers">
    /// The protected rows the shared window-membership rule puts in this window for this caller.
    /// </param>
    public static async Task<IReadOnlyList<KarstIndexAreaRow>> AreasAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        double west,
        double south,
        double east,
        double north,
        IReadOnlyCollection<long> areaTypeIds,
        IReadOnlyCollection<Guid> protectedMembers,
        int limit,
        CancellationToken ct)
    {
        var (sql, parameters) = BuildAreas(ctx, west, south, east, north, areaTypeIds, protectedMembers, limit);
        var rows = await db.Database.GetDbConnection().QueryAsync<KarstIndexAreaRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return [.. rows];
    }

    /// <summary>The area statement and its parameters, exposed so a test can plan it.</summary>
    public static (string Sql, DynamicParameters Parameters) BuildAreas(
        AccessContext ctx,
        double west,
        double south,
        double east,
        double north,
        IReadOnlyCollection<long> areaTypeIds,
        IReadOnlyCollection<Guid> protectedMembers,
        int limit)
    {
        var (visibleSql, _, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("kw_west", west);
        parameters.Add("kw_south", south);
        parameters.Add("kw_east", east);
        parameters.Add("kw_north", north);
        parameters.Add("kw_type_ids", areaTypeIds.ToArray());
        parameters.Add("kw_protected_members", protectedMembers.ToArray());
        parameters.Add("kw_limit", limit);

        var sql = $"""
            SELECT f.id AS "Id", f.name AS "Name", f.is_protected_effective AS "IsProtected"
            FROM features f
            WHERE f.deleted_at IS NULL
              AND f.kind = {((short)FeatureKind.Generic).ToString(CultureInfo.InvariantCulture)}
              AND f.feature_type_id = ANY(@kw_type_ids)
              AND f.geom IS NOT NULL
              AND ST_GeometryType(f.geom) IN ('ST_Polygon', 'ST_MultiPolygon')
              AND ((NOT f.is_protected_effective
                    AND f.geom && ST_MakeEnvelope(@kw_west, @kw_south, @kw_east, @kw_north, 4326)
                    AND ST_Intersects(f.geom, ST_MakeEnvelope(@kw_west, @kw_south, @kw_east, @kw_north, 4326)))
                   OR f.id = ANY(@kw_protected_members))
              AND {visibleSql}
            ORDER BY f.id
            LIMIT @kw_limit
            """;

        return (sql, parameters);
    }

    /// <summary>
    /// The index totals and the drawable outline of each of the given — already gated — areas.
    /// </summary>
    /// <param name="toleranceDegrees">How far the drawn outline may be simplified; the ground area
    /// is always measured on the stored outline.</param>
    public static async Task<IReadOnlyList<KarstIndexTotalsRow>> TotalsAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        IReadOnlyCollection<Guid> gatedAreaIds,
        long depressionTypeId,
        double toleranceDegrees,
        CancellationToken ct)
    {
        if (gatedAreaIds.Count == 0)
        {
            return [];
        }

        var (sql, parameters) = BuildTotals(ctx, gatedAreaIds, depressionTypeId, toleranceDegrees);
        var rows = await db.Database.GetDbConnection().QueryAsync<KarstIndexTotalsRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return [.. rows];
    }

    /// <summary>The totals statement and its parameters, exposed so a test can plan it.</summary>
    public static (string Sql, DynamicParameters Parameters) BuildTotals(
        AccessContext ctx,
        IReadOnlyCollection<Guid> gatedAreaIds,
        long depressionTypeId,
        double toleranceDegrees)
    {
        var (visibleSql, _, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("kw_area_ids", gatedAreaIds.ToArray());
        parameters.Add("kw_depression_type_id", depressionTypeId);
        parameters.Add("kw_tolerance", toleranceDegrees);

        var under = "f.deleted_at IS NULL\n          AND " + ContainmentSql.StrictlyUnderColumn("area.id");

        var sql = $"""
            WITH area AS (
                SELECT a.id,
                       ST_Area(a.geom::geography) AS area_m2,
                       ST_AsGeoJSON(ST_SimplifyPreserveTopology(a.geom, @kw_tolerance), 7) AS outline
                FROM features a
                WHERE a.id = ANY(@kw_area_ids) AND a.deleted_at IS NULL AND a.geom IS NOT NULL
            ),
            caves AS (
                SELECT area.id AS area_id, COUNT(*)::int AS n
                FROM area
                JOIN features f ON {under}
                JOIN caves c ON c.id = f.id
                WHERE f.kind = {((short)FeatureKind.Cave).ToString(CultureInfo.InvariantCulture)}
                  AND {visibleSql}
                GROUP BY area.id
            ),
            depressions AS (
                SELECT area.id AS area_id,
                       COUNT(*)::int AS n,
                       SUM(ST_Area(f.geom::geography)) AS area_m2
                FROM area
                JOIN features f ON {under}
                WHERE f.feature_type_id = @kw_depression_type_id
                  AND f.geom IS NOT NULL
                  AND ST_Dimension(f.geom) = 2
                  AND {visibleSql}
                GROUP BY area.id
            )
            SELECT area.id AS "AreaId",
                   area.area_m2 AS "AreaM2",
                   area.outline AS "OutlineGeoJson",
                   COALESCE(caves.n, 0) AS "CaveCount",
                   COALESCE(depressions.n, 0) AS "DepressionCount",
                   depressions.area_m2 AS "DepressionAreaM2"
            FROM area
            LEFT JOIN caves ON caves.area_id = area.id
            LEFT JOIN depressions ON depressions.area_id = area.id
            ORDER BY area.id
            """;

        return (sql, parameters);
    }
}
