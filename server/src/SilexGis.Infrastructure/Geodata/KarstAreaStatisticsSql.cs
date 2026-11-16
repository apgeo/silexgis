// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>What one karst area adds up to, over the caves declared to be in it.</summary>
/// <param name="AreaM2">The outline's ground area, measured on the spheroid. Null when the area row
/// carries no outline — a named grouping with no geometry has no denominator.</param>
/// <param name="CaveCount">Caves under the area, at any depth of the containment chain.</param>
/// <param name="EntranceCount">Entrances under it, which is not the same number: caves have several.</param>
/// <param name="SurveyedLengthM">Surveyed passage summed over the caves that record one.</param>
/// <param name="SurveyedCaveCount">How many of the caves contributed a surveyed length, so a reader
/// can tell a short total from a mostly-unrecorded one.</param>
/// <param name="DepressionCount">Mapped depressions with an outline; the point-only ones enclose no
/// ground and are not counted here.</param>
/// <param name="DepressionAreaM2">Ground under those outlines, summed. Overlapping outlines are
/// summed twice — they are a mapping error rather than a case to handle.</param>
public sealed record KarstAreaTotalsRow(
    double? AreaM2,
    int CaveCount,
    int EntranceCount,
    double? SurveyedLengthM,
    int SurveyedCaveCount,
    int DepressionCount,
    double? DepressionAreaM2);

/// <summary>One cave standing at an end of a range.</summary>
public sealed record KarstAreaExtremeRow(Guid FeatureId, string? Name, double Value);

/// <summary>How many caves in the area carry each rock type.</summary>
public sealed record KarstAreaRockTypeRow(long? RockTypeId, string? Code, string? Name, int CaveCount);

/// <summary>
/// Ground of one lithology inside an area outline, or — with <see cref="Unmapped"/> set — the
/// outline ground no unit covers, with the entrances found on it.
/// </summary>
public sealed record KarstAreaLithologyRow(bool Unmapped, string? Lithology, double AreaM2, int EntranceCount);

/// <summary>
/// The per-area half of the registry statistics: what the caves declared to be inside one karst
/// area add up to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Membership is declared, never spatial.</b> A cave is in an area because somebody put it
/// there — the containment chain the feature carries — and not because its coordinate falls inside
/// the outline. Two reasons, and both matter enough that the answer says so on the wire. The first
/// is meaning: a registry's areas are edited, argued over and corrected, and a count that quietly
/// disagreed with the hierarchy the same screen displays would be a second, invisible opinion about
/// what is in the area. The second is disclosure: a spatial test evaluated against a protected
/// cave's real coordinate would make every area's count a probe, and a caller could bisect an
/// outline until the count moved and read a position out of where it moved.
/// </para>
/// <para>
/// <b>The area is its own ancestor.</b> The containment closure holds a row for the area itself, so
/// every count excludes it explicitly; without that a karst area would count as one of the features
/// inside itself.
/// </para>
/// <para>
/// <b>Only what the caller may read is counted.</b> The visibility filter rides inside the walk
/// rather than being applied to its result, so a cave this caller may not read never enters the
/// join and cannot be inferred from a total that is one larger than the list beneath it.
/// </para>
/// </remarks>
public static class KarstAreaStatisticsSql
{
    /// <summary>
    /// The declared-containment predicate every statement here shares: under the area, not the area
    /// itself, and not deleted. The containment half comes from the shared fragment, which is where
    /// the reason for its exact spelling lives.
    /// </summary>
    private static readonly string InArea =
        "f.deleted_at IS NULL\n          AND " + ContainmentSql.StrictlyUnder("ka_area_id");

    /// <summary>
    /// The area's totals, read through the karst index's own totals statement run over this one
    /// area — one SQL text for what an area adds up to, so the figures an area's statistics show
    /// and the index the map paints for it cannot drift apart.
    /// </summary>
    public static async Task<KarstAreaTotalsRow> TotalsAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        Guid areaFeatureId,
        long depressionTypeId,
        CancellationToken ct)
    {
        // The outline is drawn by the map, not here; the tolerance is irrelevant to every figure,
        // which is measured on the stored outline.
        var rows = await KarstIndexWindowSql.TotalsAsync(
            db, ctx, [areaFeatureId], depressionTypeId, toleranceDegrees: 0d, ct, countEntrances: true);

        // No row only when the area row is deleted, which the route has refused before asking;
        // answered as adding up to nothing rather than faulting.
        return rows.Count == 0
            ? new KarstAreaTotalsRow(null, 0, 0, null, 0, 0, null)
            : ToAreaTotals(rows[0]);
    }

    private static KarstAreaTotalsRow ToAreaTotals(KarstIndexTotalsRow row) => new(
        row.AreaM2,
        row.CaveCount,
        row.EntranceCount,
        row.SurveyedLengthM,
        row.SurveyedCaveCount,
        row.DepressionCount,
        row.DepressionAreaM2);

    /// <summary>
    /// The caves at one end of a range — the deepest, or the longest — as a short ordered list.
    /// </summary>
    /// <param name="column">
    /// Which cave column to rank on. Not a caller-supplied value: it is chosen from the two
    /// constants below, because a column name cannot be a parameter and the only safe way to
    /// interpolate one is for it never to have come from outside.
    /// </param>
    public static async Task<IReadOnlyList<KarstAreaExtremeRow>> ExtremesAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        Guid areaFeatureId,
        string column,
        int limit,
        CancellationToken ct)
    {
        var (sql, parameters) = BuildExtremes(ctx, areaFeatureId, column, limit);
        var rows = await db.Database.GetDbConnection().QueryAsync<KarstAreaExtremeRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        return [.. rows];
    }

    /// <summary>Surveyed passage length, the column the longest-cave list ranks on.</summary>
    public const string SurveyedLengthColumn = "surveyed_length";

    /// <summary>Vertical range, the column the deepest-cave list ranks on.</summary>
    public const string DepthColumn = "depth";

    public static (string Sql, DynamicParameters Parameters) BuildExtremes(
        AccessContext ctx, Guid areaFeatureId, string column, int limit)
    {
        if (column is not (SurveyedLengthColumn or DepthColumn))
        {
            throw new ArgumentOutOfRangeException(
                nameof(column), column, "only the two ranked cave columns may be interpolated.");
        }

        var (visibleSql, _, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("ka_area_id", areaFeatureId);
        parameters.Add("ka_limit", limit);

        // The name is the feature's, and it is readable exactly when the feature is: a cave whose
        // position is closed to this caller still has a name they may read, and a top-five list
        // that dropped it would say the area has fewer big caves than it has.
        var sql = $"""
            SELECT f.id AS "FeatureId",
                   f.name AS "Name",
                   c.{column}::double precision AS "Value"
            FROM features f
            JOIN caves c ON c.id = f.id
            WHERE f.kind = {(short)FeatureKind.Cave}
              AND c.{column} IS NOT NULL
              AND {InArea}
              AND {visibleSql}
            ORDER BY c.{column} DESC, f.id
            LIMIT @ka_limit
            """;

        return (sql, parameters);
    }

    public static async Task<IReadOnlyList<KarstAreaRockTypeRow>> RockTypesAsync(
        SilexGisDbContext db, AccessContext ctx, Guid areaFeatureId, CancellationToken ct)
    {
        var (sql, parameters) = BuildRockTypes(ctx, areaFeatureId);
        var rows = await db.Database.GetDbConnection().QueryAsync<KarstAreaRockTypeRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        return [.. rows];
    }

    public static (string Sql, DynamicParameters Parameters) BuildRockTypes(
        AccessContext ctx, Guid areaFeatureId)
    {
        var (visibleSql, _, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("ka_area_id", areaFeatureId);

        // Caves with no rock type recorded group into a row of their own rather than being dropped:
        // the breakdown is also a statement about how much of the area's geology is unrecorded.
        var sql = $"""
            SELECT c.rock_type_id AS "RockTypeId",
                   rt.code AS "Code",
                   rt.name AS "Name",
                   COUNT(*)::int AS "CaveCount"
            FROM features f
            JOIN caves c ON c.id = f.id
            LEFT JOIN rock_types rt ON rt.id = c.rock_type_id
            WHERE f.kind = {(short)FeatureKind.Cave}
              AND {InArea}
              AND {visibleSql}
            GROUP BY c.rock_type_id, rt.code, rt.name
            ORDER BY COUNT(*) DESC, rt.name NULLS LAST
            """;

        return (sql, parameters);
    }

    /// <summary>
    /// The area outline split by the geological units mapped over it: each lithology's outcrop
    /// inside the outline, the ground no unit covers, and how many entrances stand on each.
    ///
    /// <para>
    /// Unlike every other figure here this one is spatial — a geology map is not a hierarchy, and a
    /// unit is in an area because its outline overlaps it. A spatial test is a probe when it runs
    /// against a position the caller may not see, so <b>both sides run over the placeable set
    /// only</b>: a unit whose outline this caller may not see exactly does not shape the split, and
    /// an entrance they may not place is not counted on any ground. Withholding a row before it is
    /// counted is what keeps a protected entrance from being located by bisecting the units it
    /// would be counted in.
    /// </para>
    /// <para>
    /// Units of one class that overlap are merged before measuring, so a class's ground is not
    /// counted twice; units of different classes that overlap are a mapping error and are each
    /// counted, as overlapping depressions are. The class is read as stored; deciding what a stored
    /// code means — and which are karstifiable — is left to the vocabulary in code.
    /// </para>
    /// </summary>
    public static async Task<IReadOnlyList<KarstAreaLithologyRow>> LithologyAsync(
        SilexGisDbContext db, AccessContext ctx, Guid areaFeatureId, long unitTypeId, CancellationToken ct)
    {
        var (sql, parameters) = BuildLithology(ctx, areaFeatureId, unitTypeId);
        var rows = await db.Database.GetDbConnection().QueryAsync<KarstAreaLithologyRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        return [.. rows];
    }

    public static (string Sql, DynamicParameters Parameters) BuildLithology(
        AccessContext ctx, Guid areaFeatureId, long unitTypeId)
    {
        // One fragment set, spelled over the alias "f" in two separate scopes (units and
        // entrances): the two fragments share the caller's parameters and cannot be built twice.
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("ka_area_id", areaFeatureId);
        parameters.Add("ka_unit_type_id", unitTypeId);

        // ST_CollectionExtract keeps the polygonal part of an intersection: two outlines that share
        // an edge intersect in a line, which has no ground and would otherwise poison the union.
        var sql = $"""
            WITH area AS (
                SELECT a.geom
                FROM features a
                WHERE a.id = @ka_area_id AND a.deleted_at IS NULL AND a.geom IS NOT NULL
                  AND ST_Dimension(a.geom) = 2
            ),
            units AS (
                SELECT NULLIF(f.properties ->> '{Lithology.PropertyKey}', '') AS lithology,
                       ST_CollectionExtract(ST_Intersection(f.geom, area.geom), 3) AS g
                FROM features f, area
                WHERE f.kind = {(short)FeatureKind.Generic}
                  AND f.feature_type_id = @ka_unit_type_id
                  AND f.deleted_at IS NULL
                  AND f.geom IS NOT NULL
                  AND ST_Dimension(f.geom) = 2
                  AND f.geom && area.geom
                  AND ST_Intersects(f.geom, area.geom)
                  AND {visibleSql}
                  AND {exactSql}
            ),
            classes AS (
                SELECT lithology, ST_Union(g) AS g
                FROM units
                WHERE NOT ST_IsEmpty(g)
                GROUP BY lithology
            ),
            unmapped AS (
                SELECT COALESCE(ST_Difference(area.geom, (SELECT ST_Union(g) FROM classes)), area.geom) AS g
                FROM area
            ),
            entrances AS (
                SELECT f.geom
                FROM features f, area
                WHERE f.kind = {(short)FeatureKind.CaveEntrance}
                  AND f.deleted_at IS NULL
                  AND f.geom IS NOT NULL
                  AND f.geom && area.geom
                  AND ST_Intersects(f.geom, area.geom)
                  AND {visibleSql}
                  AND {exactSql}
            )
            SELECT false AS "Unmapped",
                   c.lithology AS "Lithology",
                   ST_Area(c.g::geography) AS "AreaM2",
                   (SELECT COUNT(*)::int FROM entrances e
                    WHERE e.geom && c.g AND ST_Intersects(e.geom, c.g)) AS "EntranceCount"
            FROM classes c
            UNION ALL
            SELECT true,
                   NULL,
                   CASE WHEN ST_IsEmpty(u.g) THEN 0 ELSE ST_Area(u.g::geography) END,
                   (SELECT COUNT(*)::int FROM entrances e
                    WHERE NOT ST_IsEmpty(u.g) AND e.geom && u.g AND ST_Intersects(e.geom, u.g))
            FROM unmapped u
            """;

        return (sql, parameters);
    }

    /// <summary>
    /// How many caves fall inside the outline on the map but are not declared to be in the area.
    ///
    /// <para>
    /// It is a data-quality hint and nothing else: the number the statistics are computed over is
    /// the declared one, and this says how far the hierarchy has drifted from the map. It is
    /// counted <b>only over caves this caller may place exactly</b>, because the test is a spatial
    /// one and running it against a coordinate the caller may not see — snapped or otherwise —
    /// would turn the hint into the probe that declared containment exists to avoid. A caller who
    /// may not place a cave simply does not see it in this count, and the payload says the count is
    /// over the placeable set.
    /// </para>
    /// </summary>
    public static async Task<int> UnparentedInsideCountAsync(
        SilexGisDbContext db, AccessContext ctx, Guid areaFeatureId, CancellationToken ct)
    {
        var (sql, parameters) = BuildUnparentedInsideCount(ctx, areaFeatureId);

        return await db.Database.GetDbConnection().ExecuteScalarAsync<int>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
    }

    public static (string Sql, DynamicParameters Parameters) BuildUnparentedInsideCount(
        AccessContext ctx, Guid areaFeatureId)
    {
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("ka_area_id", areaFeatureId);

        var sql = $"""
            WITH area AS (
                SELECT a.geom
                FROM features a
                WHERE a.id = @ka_area_id AND a.deleted_at IS NULL AND a.geom IS NOT NULL
            )
            SELECT COUNT(*)::int
            FROM features f, area
            WHERE f.kind = {(short)FeatureKind.Cave}
              AND f.deleted_at IS NULL
              AND f.geom IS NOT NULL
              AND NOT ({ContainmentSql.Under("ka_area_id")})
              AND f.geom && area.geom
              AND ST_Intersects(f.geom, area.geom)
              AND {visibleSql}
              AND {exactSql}
            """;

        return (sql, parameters);
    }

    /// <summary>
    /// How many of the area's caves this caller may place exactly — the calibration the drift hint
    /// has to be read against. The hint is a spatial test and can only be run over placeable
    /// positions, so a caller who may place a tenth of the area's caves should read a hint of zero
    /// as "nothing found in the tenth I can see", not as "the hierarchy is clean".
    /// </summary>
    public static async Task<int> PlaceableCaveCountAsync(
        SilexGisDbContext db, AccessContext ctx, Guid areaFeatureId, CancellationToken ct)
    {
        var (sql, parameters) = BuildPlaceableCaveCount(ctx, areaFeatureId);

        return await db.Database.GetDbConnection().ExecuteScalarAsync<int>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
    }

    public static (string Sql, DynamicParameters Parameters) BuildPlaceableCaveCount(
        AccessContext ctx, Guid areaFeatureId)
    {
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("ka_area_id", areaFeatureId);

        var sql = $"""
            SELECT COUNT(*)::int
            FROM features f
            WHERE f.kind = {(short)FeatureKind.Cave}
              AND f.geom IS NOT NULL
              AND {InArea}
              AND {visibleSql}
              AND {exactSql}
            """;

        return (sql, parameters);
    }
}
