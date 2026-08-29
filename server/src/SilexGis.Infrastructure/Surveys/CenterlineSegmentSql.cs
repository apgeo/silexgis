// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SilexGis.Domain.Access;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// The survey statistics substrate for a cave whose line work never went through a survey
/// program's own file format — the documented approximation (raw SQL lives only in *Sql.cs
/// files).
///
/// <para>
/// A centerline may arrive as a drawing: a track, a line layer, a shape somebody digitised.
/// Those formats carry no per-leg flags, so there is no record of which line is passage and
/// which is a shot at a wall, and the only thing left to go on is the shape of the network. The
/// reduction this reads applies exactly that: a station carrying several loose lines is a wall
/// fan and they go, a station carrying one is a passage that ends there and it stays. Measured
/// against real exports it retains 92–95% of the surveyed length, so the numbers it produces are
/// close to the flag-based ones and are not the same numbers. That is the whole reason every row
/// says which of the two it is.
/// </para>
///
/// <para>
/// The reduction is recomputed here rather than read from the stored one, because the stored
/// skeleton is flattened — it exists to be drawn on a surface map — and a statistic about how
/// steep a passage is, how deep it goes, or how its bearings distribute with height needs the
/// altitudes the flat copy threw away.
/// </para>
/// </summary>
public static class CenterlineSegmentSql
{
    /// <summary>
    /// The cave's current centerline, reduced and measured segment by segment, or an empty list
    /// when the cave has no centerline the caller may see.
    ///
    /// <para>
    /// Two statements, and they are two on purpose: the reduction is a graph walk that belongs
    /// in the domain rules and not in SQL, so the geometry comes out, is reduced, and goes back
    /// in to be measured on the spheroid. Whether there are altitudes to measure is decided on
    /// the way out, from the stored line work — the reduction fills a missing altitude with zero
    /// so that what it hands back can be stored and drawn, so a consumer that asked the reduction
    /// instead would read a plan drawing as a perfectly flat cave at sea level.
    /// </para>
    /// </summary>
    public static async Task<IReadOnlyList<SurveySegmentRow>> ForCaveAsync(
        SilexGisDbContext db, AccessContext ctx, Guid caveFeatureId, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();

        var (sourceSql, parameters) = BuildSourceQuery(ctx, caveFeatureId);

        var source = await connection.QuerySingleOrDefaultAsync<CenterlineSourceRow>(
            new CommandDefinition(sourceSql, parameters, cancellationToken: ct));
        if (source?.Ewkb is null)
        {
            return [];
        }

        if (new WKBReader().Read(source.Ewkb) is not MultiLineString lines || lines.IsEmpty)
        {
            return [];
        }

        // Whether there are altitudes at all — and the stored dimension does not answer it.
        //
        // A centerline uploaded as a plan drawing is legal and carries no third coordinate. The
        // upload path gives it one anyway, writing zero where the file said nothing, because a
        // geometry with a dimension half full of "not a number" is not something the database or
        // the map can work with. That is the right choice for storage and it destroys the
        // distinction this needs: by the time the row is read, a drawing with no altitudes and a
        // cave lying flat at exactly sea level look identical.
        //
        // So the question is asked of the values: line work with a third dimension in which not
        // one coordinate is anything other than zero has no altitudes in it. This is a reading of
        // the evidence rather than a record of what arrived, and it errs the safe way — a genuine
        // cave whose every station sits at exactly zero elevation is refused its vertical
        // statistics, which for such a cave are zero in any case, whereas the other mistake would
        // report a plan drawing as a flat cave at sea level with nothing to say it was a guess.
        var hasZ = source.HasZ && lines.Coordinates.Any(c => !double.IsNaN(c.Z) && c.Z != 0);

        var skeleton = CenterlineSkeleton.Build3D(lines);
        if (skeleton.IsEmpty)
        {
            return [];
        }

        var wkb = new WKBWriter(ByteOrder.LittleEndian, handleSRID: false, emitZ: true).Write(skeleton);

        // ST_DumpSegments hands back one two-point piece per consecutive vertex pair, with a path
        // of {polyline, segment} — the ordering the sinuosity of a single passage is computed
        // along. Both are one-based in the database and zero-based on the row.
        var measureSql = $"""
            WITH piece AS (
                SELECT (ds.path)[1]::int - 1 AS path_index,
                       (ds.path)[2]::int - 1 AS segment_index,
                       ST_StartPoint(ds.geom) AS a,
                       ST_EndPoint(ds.geom) AS b
                FROM ST_DumpSegments(ST_SetSRID(ST_GeomFromWKB(@seg_wkb), 4326)) ds
            ),
            measured AS (
                SELECT piece.*,
                       ST_Distance(piece.a::geography, piece.b::geography) AS plan_m,
                       ST_Azimuth(piece.a::geography, piece.b::geography) AS az_rad,
                       CASE WHEN @seg_has_z::boolean THEN ST_Z(piece.b) - ST_Z(piece.a) END AS dz
                FROM piece
            )
            SELECT
                {(short)SurveySegmentBasis.SkeletonHeuristic}::smallint AS "Basis",
                NULL::uuid AS "SurveyModelId",
                NULL::bigint AS "ShotId",
                NULL::text AS "FromStationName",
                NULL::text AS "ToStationName",
                path_index AS "PathIndex",
                segment_index AS "SegmentIndex",
                false AS "IsDuplicate",
                @seg_has_z::boolean AS "HasZ",
                ST_X(a) AS "FromLongitude",
                ST_Y(a) AS "FromLatitude",
                ST_X(b) AS "ToLongitude",
                ST_Y(b) AS "ToLatitude",
                plan_m AS "PlanLengthM",
                NULL::double precision AS "FileLengthM",
                degrees(az_rad) AS "AzimuthDegrees",
                dz AS "DeltaZM",
                sqrt(plan_m * plan_m + dz * dz) AS "SlopeLengthM",
                CASE WHEN plan_m = 0 AND dz = 0 THEN NULL
                     ELSE degrees(atan2(dz, plan_m)) END AS "DipDegrees",
                CASE WHEN @seg_has_z::boolean THEN (ST_Z(a) + ST_Z(b)) / 2 END AS "MidZM"
            FROM measured
            ORDER BY path_index, segment_index
            """;

        var rows = await connection.QueryAsync<SurveySegmentRow>(new CommandDefinition(
            measureSql,
            new { seg_wkb = wkb, seg_has_z = hasZ },
            cancellationToken: ct));
        return [.. rows];
    }

    /// <summary>
    /// The cave-scoped lookup that fetches the line work, and the parameters it runs with, built
    /// without a database. Exposed for the same reason its counterpart over the survey legs is:
    /// this is the half that carries the access walk over the feature table, so it is the half
    /// whose access path is worth pinning against a planner that decides to scan instead.
    /// </summary>
    public static (string Sql, DynamicParameters Parameters) BuildSourceQuery(
        AccessContext ctx, Guid caveFeatureId)
    {
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("seg_cave_id", caveFeatureId);

        // The centerline is a feature in its own right, so the access walk runs over its row and
        // not the cave's: a caller barred from the cave is barred from everything beneath it by
        // the same walk, while a centerline that is itself withheld stays withheld even for a
        // caller who may read the cave.
        var sql = $"""
            SELECT ST_NDims(f.geom) = 3 AS "HasZ",
                   ST_AsEWKB(f.geom) AS "Ewkb"
            FROM centerlines c
            JOIN features f ON f.id = c.id
            WHERE c.cave_feature_id = @seg_cave_id
              AND c.is_default
              AND f.deleted_at IS NULL
              AND {visibleSql}
              AND {exactSql}
            LIMIT 1
            """;

        return (sql, parameters);
    }

    /// <summary>The stored line work and whether it was ever given a third dimension.</summary>
    private sealed record CenterlineSourceRow(bool HasZ, byte[]? Ewkb);
}
