// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// One closed depression's alignment: which way its long axis lies, and how much longer it is than
/// it is wide.
/// </summary>
/// <param name="FeatureId">The outline measured.</param>
/// <param name="AxisAzimuthDegrees">
/// Where the long axis lies, degrees clockwise from true north, folded into [0, 180). Null when the
/// outline has no rectangle around it — a ring whose points are collinear — or when the two ends of
/// its longest side coincide. An axis has no direction, so 190 would be the same alignment as 10
/// and reporting it as a bearing would split one population of depressions into two.
/// </param>
/// <param name="LongAxisM">The longer side of the smallest-area rectangle containing the outline.</param>
/// <param name="ShortAxisM">The shorter side of that rectangle.</param>
public sealed record DolineAxisRow(
    Guid FeatureId, double? AxisAzimuthDegrees, double? LongAxisM, double? ShortAxisM);

/// <summary>
/// The alignments of the closed depressions under one area, in a form that can be put in the same
/// rose as a set of fracture traces.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not the shape table's axis bearing.</b> The measured-shape table reports the long
/// axis in degrees clockwise from the working system's <i>grid</i> north, which is the right answer
/// for a table read next to a map drawn in that system. It is the wrong answer here: a fracture
/// trace's bearing is taken on the spheroid, and grid north and true north differ by the
/// convergence of the meridians — a few degrees through the middle latitudes, varying across a
/// sheet. Put in one comparison the two would read as a real, small disagreement between the
/// depressions and the rock, which is exactly the finding this view exists to report. So the
/// rectangle is still found in the projected system, where a smallest-area rectangle means
/// something, but the bearing of its longest side is taken on the spheroid from that side's two
/// ends — the same measurement, taken the same way, as every other bearing in the comparison.
/// </para>
/// <para>
/// <b>Who is in the answer.</b> An outline places itself and its alignment places it further, so
/// only outlines this caller may both read and place exactly are measured. A withheld one
/// contributes to no sector and to no count, which is what makes the same area, asked about with
/// and without a protected depression in it, give the same rose.
/// </para>
/// </remarks>
public static class DolineAxisSql
{
    /// <summary>
    /// The alignments of the polygons of one kind that sit under <paramref name="areaFeatureId"/>.
    /// </summary>
    public static async Task<IReadOnlyList<DolineAxisRow>> InAreaAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        Guid areaFeatureId,
        long? featureTypeId,
        int maxCandidates,
        int limit,
        int workingSrid,
        CancellationToken ct)
    {
        var (sql, parameters) =
            BuildInArea(ctx, areaFeatureId, featureTypeId, maxCandidates, limit, workingSrid);
        var rows = await db.Database.GetDbConnection().QueryAsync<DolineAxisRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        return [.. rows];
    }

    /// <summary>
    /// The statement and its parameters, exposed so the access path can be pinned by a test rather
    /// than assumed.
    /// </summary>
    public static (string Sql, DynamicParameters Parameters) BuildInArea(
        AccessContext ctx,
        Guid areaFeatureId,
        long? featureTypeId,
        int maxCandidates,
        int limit,
        int workingSrid)
    {
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("da_area_id", areaFeatureId);
        // Typed explicitly: null here means "every polygon kind", and an untyped null leaves the
        // driver nothing to infer the parameter's type from.
        parameters.Add("da_type_id", featureTypeId, DbType.Int64);
        parameters.Add("da_candidates", maxCandidates);
        parameters.Add("da_limit", limit);
        parameters.Add("workingSrid", workingSrid);

        var projected = SpatialSql.ToWorking("c.geom");

        var sql = $"""
            WITH shape AS MATERIALIZED (
                SELECT c.id, {projected} AS g
                FROM (
                    SELECT f.id, f.geom
                    FROM features f
                    WHERE f.deleted_at IS NULL
                      AND f.geom IS NOT NULL
                      AND ST_Dimension(f.geom) = 2
                      AND @da_area_id = ANY(f.ancestor_ids)
                      -- The containment closure holds a row for the area itself. An area is a
                      -- boundary somebody drew round a district; it is not one of the depressions
                      -- inside it, and measuring its own long axis would put the shape of the
                      -- study area into the rose it is the study area of.
                      AND f.id <> @da_area_id
                      AND (@da_type_id IS NULL OR f.feature_type_id = @da_type_id)
                      AND {visibleSql}
                      AND {exactSql}
                    ORDER BY ST_Area(ST_Envelope(f.geom)) DESC, f.id
                    LIMIT @da_candidates
                ) c
            ),
            envelope AS MATERIALIZED (
                SELECT s.id,
                       CASE WHEN ST_IsValid(s.g) THEN ST_OrientedEnvelope(s.g) END AS env
                FROM shape s
            ),
            sides AS (
                -- The four sides of the smallest-area rectangle, walked corner to corner. The
                -- function returns five vertices for four sides; the last has no successor and
                -- drops out. A ring whose points are collinear has no such rectangle — the answer
                -- is a line or a point — and then there is no alignment to report.
                SELECT e.id,
                       ST_Distance(p.geom, lead(p.geom) OVER w) AS len,
                       p.geom AS a,
                       lead(p.geom) OVER w AS b
                FROM envelope e
                CROSS JOIN LATERAL ST_DumpPoints(e.env) p
                WHERE e.env IS NOT NULL AND GeometryType(e.env) = 'POLYGON'
                WINDOW w AS (PARTITION BY e.id ORDER BY p.path[2])
            ),
            axes AS (
                SELECT id,
                       max(len) AS long_axis,
                       min(len) AS short_axis,
                       (array_agg(a ORDER BY len DESC))[1] AS long_a,
                       (array_agg(b ORDER BY len DESC))[1] AS long_b
                FROM sides
                WHERE len IS NOT NULL
                GROUP BY id
            )
            SELECT a.id AS "FeatureId",
                   mod(degrees(ST_Azimuth(
                       ST_Transform(a.long_a, 4326)::geography,
                       ST_Transform(a.long_b, 4326)::geography))::numeric,
                       180)::double precision AS "AxisAzimuthDegrees",
                   a.long_axis AS "LongAxisM",
                   a.short_axis AS "ShortAxisM"
            FROM axes a
            ORDER BY a.long_axis DESC, a.id
            LIMIT @da_limit
            """;

        return (sql, parameters);
    }
}
