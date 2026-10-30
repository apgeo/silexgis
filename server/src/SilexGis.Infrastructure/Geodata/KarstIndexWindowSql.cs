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

/// <summary>What one area adds up to, with its outline to draw.</summary>
/// <param name="AreaId">The area.</param>
/// <param name="AreaM2">The outline's ground area on the spheroid, from the stored outline; null
/// when the area row carries no outline.</param>
/// <param name="OutlineGeoJson">The outline simplified for the zoom, as a GeoJSON geometry.</param>
/// <param name="CaveCount">Caves declared under the area, at any depth of the containment chain.</param>
/// <param name="EntranceCount">Entrances declared under it — not the same number: caves have several.
/// Counted only when the statement is asked to; zero otherwise.</param>
/// <param name="SurveyedLengthM">Surveyed passage summed over the caves that record one.</param>
/// <param name="SurveyedCaveCount">How many of the caves contributed a surveyed length.</param>
/// <param name="DepressionCount">Depression outlines declared under it.</param>
/// <param name="DepressionAreaM2">Ground under those outlines, summed; null when there are none.</param>
public sealed record KarstIndexTotalsRow(
    Guid AreaId,
    double? AreaM2,
    string? OutlineGeoJson,
    int CaveCount,
    int EntranceCount,
    double? SurveyedLengthM,
    int SurveyedCaveCount,
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
/// caller may read, and the area itself excluded. It is in fact the same statement: one area's
/// statistics run it with that one id (adding only the entrance count, which the index does not
/// use), so the two answers cannot drift apart however either is later changed.
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
    /// <param name="countEntrances">Whether to count the entrances under each area as well; when
    /// false <see cref="KarstIndexTotalsRow.EntranceCount"/> is zero.</param>
    public static async Task<IReadOnlyList<KarstIndexTotalsRow>> TotalsAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        IReadOnlyCollection<Guid> gatedAreaIds,
        long depressionTypeId,
        double toleranceDegrees,
        CancellationToken ct,
        bool countEntrances = false)
    {
        if (gatedAreaIds.Count == 0)
        {
            return [];
        }

        var (sql, parameters) = BuildTotals(
            ctx, gatedAreaIds, depressionTypeId, toleranceDegrees, countEntrances);
        var rows = await db.Database.GetDbConnection().QueryAsync<KarstIndexTotalsRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return [.. rows];
    }

    /// <summary>
    /// The totals statement and its parameters, exposed so a test can plan it. The single text for
    /// what a karst area adds up to: the map's index runs it over every area in the window, and one
    /// area's statistics run it over that area alone.
    /// </summary>
    /// <remarks>
    /// The entrance count is the one figure the index does not use, and the only one whose cost
    /// grows with the entrance register rather than with the caves: a window over a large region
    /// would walk every entrance under every area for a number it throws away. So it is asked for
    /// only by one area's statistics; every other part of the text is the same for both.
    /// </remarks>
    public static (string Sql, DynamicParameters Parameters) BuildTotals(
        AccessContext ctx,
        IReadOnlyCollection<Guid> gatedAreaIds,
        long depressionTypeId,
        double toleranceDegrees,
        bool countEntrances = false)
    {
        var (visibleSql, _, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("kw_area_ids", gatedAreaIds.ToArray());
        parameters.Add("kw_depression_type_id", depressionTypeId);
        parameters.Add("kw_tolerance", toleranceDegrees);

        var under = "f.deleted_at IS NULL\n          AND " + ContainmentSql.StrictlyUnderColumn("area.id");

        // Ends with a line break and the indentation of the CTE that follows it.
        var entrancesCte = countEntrances
            ? $"""
                entrances AS (
                    SELECT area.id AS area_id, COUNT(*)::int AS n
                    FROM area
                    JOIN features f ON {under}
                    WHERE f.kind = {((short)FeatureKind.CaveEntrance).ToString(CultureInfo.InvariantCulture)}
                      AND {visibleSql}
                    GROUP BY area.id
                ),
                """ + "\n            "
            : "";
        var entranceCount = countEntrances ? "COALESCE(entrances.n, 0)" : "0";
        var entrancesJoin = countEntrances
            ? "LEFT JOIN entrances ON entrances.area_id = area.id\n            "
            : "";

        var sql = $"""
            WITH area AS (
                SELECT a.id,
                       ST_Area(a.geom::geography) AS area_m2,
                       ST_AsGeoJSON(ST_SimplifyPreserveTopology(a.geom, @kw_tolerance), 7) AS outline
                FROM features a
                WHERE a.id = ANY(@kw_area_ids) AND a.deleted_at IS NULL
            ),
            caves AS (
                SELECT area.id AS area_id,
                       COUNT(*)::int AS n,
                       SUM(c.surveyed_length)::double precision AS surveyed_m,
                       COUNT(c.surveyed_length)::int AS surveyed_n
                FROM area
                JOIN features f ON {under}
                JOIN caves c ON c.id = f.id
                WHERE f.kind = {((short)FeatureKind.Cave).ToString(CultureInfo.InvariantCulture)}
                  AND {visibleSql}
                GROUP BY area.id
            ),
            {entrancesCte}depressions AS (
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
                   {entranceCount} AS "EntranceCount",
                   caves.surveyed_m AS "SurveyedLengthM",
                   COALESCE(caves.surveyed_n, 0) AS "SurveyedCaveCount",
                   COALESCE(depressions.n, 0) AS "DepressionCount",
                   depressions.area_m2 AS "DepressionAreaM2"
            FROM area
            LEFT JOIN caves ON caves.area_id = area.id
            {entrancesJoin}LEFT JOIN depressions ON depressions.area_id = area.id
            ORDER BY area.id
            """;

        return (sql, parameters);
    }
}
