// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
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
    /// itself, and not deleted.
    ///
    /// <para>
    /// Written as array containment — <c>ancestor_ids @&gt; ARRAY[id]</c> — and not as
    /// <c>id = ANY(ancestor_ids)</c>, which reads identically and plans nothing like it. The GIN
    /// index on that column serves the containment operators with the column on the left; a scalar
    /// compared against <c>ANY</c> of it is not rewritten into an indexable form, so the same
    /// predicate spelled the other way is a sequential scan of every feature in the installation,
    /// five times over per request. There is a test that asserts the plan, because the difference
    /// is invisible in the result.
    /// </para>
    /// </summary>
    private const string InArea = """
        f.deleted_at IS NULL
          AND f.ancestor_ids @> ARRAY[@ka_area_id]::uuid[]
          AND f.id <> @ka_area_id
        """;

    public static async Task<KarstAreaTotalsRow> TotalsAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        Guid areaFeatureId,
        long depressionTypeId,
        CancellationToken ct)
    {
        var (sql, parameters) = BuildTotals(ctx, areaFeatureId, depressionTypeId);
        var row = await db.Database.GetDbConnection().QuerySingleAsync<KarstAreaTotalsRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        return row;
    }

    /// <summary>
    /// The totals statement and its parameters, exposed so the access path and the declared-
    /// containment walk can be pinned by a test rather than assumed.
    /// </summary>
    public static (string Sql, DynamicParameters Parameters) BuildTotals(
        AccessContext ctx, Guid areaFeatureId, long depressionTypeId)
    {
        var (visibleSql, _, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("ka_area_id", areaFeatureId);
        parameters.Add("ka_depression_type_id", depressionTypeId);

        // The outline's own area is read without a visibility fragment: the caller was already
        // checked able to read the area before this ran, and a second fragment cannot share the
        // first one's parameter set.
        var sql = $"""
            WITH area AS (
                SELECT ST_Area(a.geom::geography) AS area_m2
                FROM features a
                WHERE a.id = @ka_area_id AND a.deleted_at IS NULL AND a.geom IS NOT NULL
            ),
            caves AS (
                SELECT f.id, c.surveyed_length
                FROM features f
                JOIN caves c ON c.id = f.id
                WHERE f.kind = {(short)FeatureKind.Cave}
                  AND {InArea}
                  AND {visibleSql}
            ),
            entrances AS (
                SELECT COUNT(*)::int AS n
                FROM features f
                WHERE f.kind = {(short)FeatureKind.CaveEntrance}
                  AND {InArea}
                  AND {visibleSql}
            ),
            depressions AS (
                SELECT COUNT(*)::int AS n,
                       SUM(ST_Area(f.geom::geography)) AS area_m2
                FROM features f
                WHERE f.feature_type_id = @ka_depression_type_id
                  AND f.geom IS NOT NULL
                  AND ST_Dimension(f.geom) = 2
                  AND {InArea}
                  AND {visibleSql}
            )
            SELECT (SELECT area_m2 FROM area) AS "AreaM2",
                   (SELECT COUNT(*)::int FROM caves) AS "CaveCount",
                   (SELECT n FROM entrances) AS "EntranceCount",
                   (SELECT SUM(surveyed_length)::double precision FROM caves) AS "SurveyedLengthM",
                   (SELECT COUNT(*)::int FROM caves WHERE surveyed_length IS NOT NULL)
                       AS "SurveyedCaveCount",
                   (SELECT n FROM depressions) AS "DepressionCount",
                   (SELECT area_m2 FROM depressions) AS "DepressionAreaM2"
            """;

        return (sql, parameters);
    }

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
              AND NOT (f.ancestor_ids @> ARRAY[@ka_area_id]::uuid[])
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
