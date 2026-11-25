// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>One structural reading as stored: which feature, and its property document.</summary>
/// <param name="FeatureId">The reading.</param>
/// <param name="PropertiesJson">
/// The reading's typed properties, verbatim. Read through the domain's reader rather than picked
/// apart here, so a stored document an administrator's loosened form let through is judged by one
/// rule wherever readings are read.
/// </param>
public sealed record StructuralReadingRow(Guid FeatureId, string PropertiesJson);

/// <summary>
/// The structural readings — bedding, joints, fault planes measured at a point — around a cave or
/// under an area.
/// </summary>
/// <remarks>
/// <para>
/// <b>Protection.</b> A reading is a point, and which readings answer a question about a place says
/// where they are as surely as their coordinates would; an underground one linked to a protected
/// cave inherits the cave's protection through the hierarchy. So each reading is admitted only for
/// a caller who may both read it and place it exactly — the rule mapped fracture traces follow —
/// and one closed to the caller is in no pole, no count and no test statistic. The cave end of the
/// buffer question is gated by the caller before this runs.
/// </para>
/// <para>
/// The reach around a cave is measured the way the trace comparison measures it — from every part
/// of the cave this caller may see, flattened to two dimensions — so the two answers on one page
/// gather from one neighbourhood.
/// </para>
/// </remarks>
public static class StructuralMeasurementSql
{
    private const double MetresPerDegreeLatitude = 110_540d;
    private const double MetresPerDegreeLongitudeAtEquator = 111_320d;

    // A reading of another kind is left out before the cap, so the cap counts only the readings
    // the question is about.
    private static readonly string TypeFilter =
        $"(CAST(@sm_measurement_type AS text) IS NULL OR f.properties ->> '{StructuralMeasurement.TypeKey}' = @sm_measurement_type)";

    /// <summary>The readings within <paramref name="metres"/> of a cave the caller may read and place.</summary>
    public static async Task<IReadOnlyList<StructuralReadingRow>> NearCaveAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        Guid caveFeatureId,
        long featureTypeId,
        string? measurementType,
        double metres,
        int maxReadings,
        CancellationToken ct)
    {
        var (sql, parameters) = BuildNearCave(ctx, caveFeatureId, featureTypeId, measurementType, metres, maxReadings);
        var rows = await db.Database.GetDbConnection().QueryAsync<StructuralReadingRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return [.. rows];
    }

    /// <summary>The readings under one area of the containment hierarchy the caller may read and place.</summary>
    public static async Task<IReadOnlyList<StructuralReadingRow>> InAreaAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        Guid areaFeatureId,
        long featureTypeId,
        string? measurementType,
        int maxReadings,
        CancellationToken ct)
    {
        var (sql, parameters) = BuildInArea(ctx, areaFeatureId, featureTypeId, measurementType, maxReadings);
        var rows = await db.Database.GetDbConnection().QueryAsync<StructuralReadingRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return [.. rows];
    }

    /// <summary>The area-scoped statement and its parameters, exposed so a test can pin it.</summary>
    public static (string Sql, DynamicParameters Parameters) BuildInArea(
        AccessContext ctx, Guid areaFeatureId, long featureTypeId, string? measurementType, int maxReadings)
    {
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("sm_area_id", areaFeatureId);
        AddCommon(parameters, featureTypeId, measurementType, maxReadings);

        var sql = $"""
            SELECT f.id AS "FeatureId", f.properties::text AS "PropertiesJson"
            FROM features f
            WHERE f.deleted_at IS NULL
              AND f.geom IS NOT NULL
              AND ST_Dimension(f.geom) = 0
              AND f.feature_type_id = @sm_type_id
              AND {TypeFilter}
              AND {ContainmentSql.Under("sm_area_id")}
              AND {visibleSql}
              AND {exactSql}
            ORDER BY f.id
            LIMIT @sm_max_readings
            """;

        return (sql, parameters);
    }

    /// <summary>The buffer-scoped statement and its parameters, exposed so a test can pin it.</summary>
    public static (string Sql, DynamicParameters Parameters) BuildNearCave(
        AccessContext ctx,
        Guid caveFeatureId,
        long featureTypeId,
        string? measurementType,
        double metres,
        int maxReadings)
    {
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("sm_cave_id", caveFeatureId);
        parameters.Add("sm_metres", metres);
        parameters.Add("sm_lat_metres", MetresPerDegreeLatitude);
        parameters.Add("sm_lon_metres", MetresPerDegreeLongitudeAtEquator);
        AddCommon(parameters, featureTypeId, measurementType, maxReadings);

        var withinReach = SpatialSql.WithinMetres("f.geom", "centre.g", "sm_metres");

        // The centre is gathered from the cave and the parts of it this caller may see; a hidden
        // child must not widen the reach and so let a reading near it be detected.
        var sql = $"""
            WITH centre AS (
                SELECT ST_Collect(ST_Force2D(f.geom)) AS g,
                       ST_Y(ST_Centroid(ST_Collect(ST_Force2D(f.geom)))) AS lat
                FROM features f
                WHERE {ContainmentSql.Under("sm_cave_id")}
                  AND f.deleted_at IS NULL
                  AND f.geom IS NOT NULL
                  AND (f.id = @sm_cave_id OR NOT f.is_protected_effective)
                  AND (f.id = @sm_cave_id OR {visibleSql})
            ),
            box AS (
                SELECT ST_Expand(
                           centre.g,
                           @sm_metres / (@sm_lon_metres * greatest(cos(radians(centre.lat)), 0.01)),
                           @sm_metres / @sm_lat_metres) AS b,
                       centre.g AS g
                FROM centre
            )
            SELECT f.id AS "FeatureId", f.properties::text AS "PropertiesJson"
            FROM features f, box AS centre
            WHERE f.deleted_at IS NULL
              AND f.geom IS NOT NULL
              AND ST_Dimension(f.geom) = 0
              AND f.feature_type_id = @sm_type_id
              AND {TypeFilter}
              AND f.geom && centre.b
              AND {withinReach}
              AND {visibleSql}
              AND {exactSql}
            ORDER BY f.id
            LIMIT @sm_max_readings
            """;

        return (sql, parameters);
    }

    private static void AddCommon(DynamicParameters parameters, long featureTypeId, string? measurementType, int maxReadings)
    {
        parameters.Add("sm_type_id", featureTypeId);
        parameters.Add("sm_measurement_type", measurementType);
        parameters.Add("sm_max_readings", maxReadings);
    }
}
