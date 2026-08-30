// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// The survey statistics substrate, read from the legs of a parsed survey file (raw SQL lives
/// only in *Sql.cs files).
///
/// <para>
/// <b>Why the legs and not the stored line geometry.</b> A cave's stored survey geometry is
/// everything the file contained, wall shots included — measured on real exports at around 98%
/// of the line components and, on one 15.7 km cave, 244 km of line work for 15.7 km of passage.
/// An orientation statistic computed over that measures how the surveyor pointed the instrument
/// at walls, not where the passage goes. The legs carry the surveyor's own per-leg flags, so
/// wall shots and surface legs are excluded here because the file says what they are.
/// </para>
///
/// <para>
/// <b>Why the measuring happens in the database.</b> The legs are stored in longitude and
/// latitude. A bearing taken from raw coordinate differences is wrong by the cosine of the
/// latitude — around ten degrees at the latitudes this application is used at — and a length
/// taken the same way is in degrees. Asking the geography type gives a true bearing and a
/// distance in metres without any projected system having to be chosen, which also means the
/// answer does not change when an installation changes its working system.
/// </para>
///
/// <para>
/// In Infrastructure rather than beside the endpoint because several unrelated read surfaces —
/// orientation, hypsometry, topology, cross-section morphometry — all reduce to this same set
/// of rows, and each of them computing its own would be several definitions of one cave's length.
/// </para>
/// </summary>
public static class SurveySegmentSql
{
    /// <summary>
    /// Legs the file marked as something other than passage the survey walked. Wall shots
    /// radiate from a station in every direction and surface legs are not underground at all;
    /// neither belongs in a statistic about where the cave goes. Duplicated legs are <i>not</i>
    /// here — they are real passage and the rows are kept, they are only excluded from length
    /// sums.
    /// </summary>
    private const SurveyShotFlags NotPassage = SurveyShotFlags.Splay | SurveyShotFlags.Surface;

    /// <summary>
    /// Every measured leg of the one survey model that answers for a cave, or an empty list when
    /// the cave has no parsed survey the caller may see.
    ///
    /// <para>
    /// <b>One model, not all of them.</b> Nothing stops a cave holding several uploaded survey
    /// files, and in practice one does: uploads are immutable, so a corrected re-export is a new
    /// model beside the old one, and the same cave is routinely held in both of the two formats
    /// that are read. Each of those leaves a complete set of legs against the same cave. Summing
    /// them would report a six-hundred-metre cave as twelve hundred metres, halve every ratio
    /// built from a length against an extent, and raise a disagreement against a record that is
    /// exactly right — the one comparison these statistics exist to make. So exactly one model
    /// answers: the one the cave's current shape was read out of, and failing that the most
    /// recently uploaded model whose reading finished.
    /// </para>
    ///
    /// <para>
    /// The access walk is spliced in rather than assumed from a caller-side check, so this
    /// cannot become the query that answers past a refusal. Both arms apply: the caller must be
    /// able to read the cave <i>and</i> to place it exactly, because a survey leg is a cave
    /// coordinate as much as the cave's own point is.
    /// </para>
    /// </summary>
    public static async Task<IReadOnlyList<SurveySegmentRow>> ForCaveAsync(
        SilexGisDbContext db, AccessContext ctx, Guid caveFeatureId, CancellationToken ct)
    {
        var (sql, parameters) = BuildForCave(ctx, caveFeatureId);
        var connection = db.Database.GetDbConnection();
        var rows = await connection.QueryAsync<SurveySegmentRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return [.. rows];
    }

    /// <summary>
    /// The statement <see cref="ForCaveAsync"/> runs and the parameters it runs it with, built
    /// without a database. Exposed so the access path this rides can be pinned by a test that
    /// asks the planner what it intends to do, rather than only by a test that checks the answer:
    /// the query splices the whole access walk over a hundred-thousand-row feature table, which is
    /// the shape that quietly becomes a sequential scan.
    /// </summary>
    public static (string Sql, DynamicParameters Parameters) BuildForCave(
        AccessContext ctx, Guid caveFeatureId)
    {
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("seg_cave_id", caveFeatureId);

        // The flag words are interpolated as the numbers they are so the planner sees constants;
        // they are compile-time enum values, never anything a caller supplies. The same goes for
        // the model status and the two line-plot formats below.
        var sql = $"""
            WITH chosen AS (
                SELECT COALESCE(
                    (SELECT c.survey_model_id
                     FROM centerlines c
                     WHERE c.cave_feature_id = @seg_cave_id
                       AND c.is_default
                       AND c.survey_model_id IS NOT NULL
                     LIMIT 1),
                    (SELECT m.id
                     FROM survey_models m
                     WHERE m.cave_feature_id = @seg_cave_id
                       AND m.status = {(short)SurveyModelStatus.Ready}
                       AND m.format IN ({(short)SurveyModelFormat.Lox}, {(short)SurveyModelFormat.Survex3d})
                     ORDER BY m.created_at DESC, m.id DESC
                     LIMIT 1)
                ) AS model_id
            ),
            leg AS (
                SELECT s.id,
                       s.survey_model_id,
                       s.from_station_name,
                       s.to_station_name,
                       s.length_m,
                       (s.flags & {(int)SurveyShotFlags.Duplicate}) <> 0 AS is_duplicate,
                       {SpatialSql.HasAltitudes("s.geom")} AS has_z,
                       ST_StartPoint(s.geom) AS a,
                       ST_EndPoint(s.geom) AS b
                FROM survey_shots s
                JOIN chosen ON chosen.model_id = s.survey_model_id
                JOIN survey_models m ON m.id = s.survey_model_id
                JOIN features f ON f.id = m.cave_feature_id
                WHERE m.cave_feature_id = @seg_cave_id
                  AND f.kind = {(short)FeatureKind.Cave}
                  AND f.deleted_at IS NULL
                  AND {visibleSql}
                  AND {exactSql}
                  AND (s.flags & {(int)NotPassage}) = 0
            ),
            measured AS (
                SELECT leg.*,
                       ST_Distance(leg.a::geography, leg.b::geography) AS plan_m,
                       ST_Azimuth(leg.a::geography, leg.b::geography) AS az_rad,
                       CASE WHEN leg.has_z THEN ST_Z(leg.b) - ST_Z(leg.a) END AS dz
                FROM leg
            )
            SELECT
                {(short)SurveySegmentBasis.SurveyFlags}::smallint AS "Basis",
                survey_model_id AS "SurveyModelId",
                id AS "ShotId",
                from_station_name AS "FromStationName",
                to_station_name AS "ToStationName",
                NULL::int AS "PathIndex",
                (row_number() OVER (ORDER BY id))::int - 1 AS "SegmentIndex",
                is_duplicate AS "IsDuplicate",
                has_z AS "HasZ",
                ST_X(a) AS "FromLongitude",
                ST_Y(a) AS "FromLatitude",
                ST_X(b) AS "ToLongitude",
                ST_Y(b) AS "ToLatitude",
                plan_m AS "PlanLengthM",
                length_m AS "FileLengthM",
                degrees(az_rad) AS "AzimuthDegrees",
                dz AS "DeltaZM",
                sqrt(plan_m * plan_m + dz * dz) AS "SlopeLengthM",
                CASE WHEN plan_m = 0 AND dz = 0 THEN NULL
                     ELSE degrees(atan2(dz, plan_m)) END AS "DipDegrees",
                CASE WHEN has_z THEN (ST_Z(a) + ST_Z(b)) / 2 END AS "MidZM"
            FROM measured
            ORDER BY id
            """;

        return (sql, parameters);
    }
}
