// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data;
using Dapper;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Permissions;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// One polygon's measured shape: the classical closed-depression parameter set — area,
/// perimeter, circularity, the two axes of the smallest rectangle that contains it, how
/// elongated that makes it, which way the long axis points, and where the middle is.
/// </summary>
/// <remarks>
/// Every length is metres and every area is square metres, measured in the installation's
/// working system. The centroid is handed back in the stored system because that is what a
/// map and an export want; it is the projected centroid converted back, not the centroid of
/// the stored degrees, so it is the middle by area rather than the middle by longitude.
/// </remarks>
/// <param name="FeatureId">The feature measured.</param>
/// <param name="Name">Its name, so a bulk table needs no second query.</param>
/// <param name="GeometryValid">
/// False when the ring crosses itself. Such a polygon still has an area function that
/// answers — it answers zero — so every derived figure is withheld instead: a doline
/// reported as having no area and no shape is a defect in the drawing, and saying so is the
/// only honest answer.
/// </param>
/// <param name="AreaM2">Plan area.</param>
/// <param name="PerimeterM">Plan perimeter.</param>
/// <param name="Circularity">
/// 4·π·A / P². One for a circle, falling towards zero as the outline lengthens or frets.
/// Null when the perimeter is zero, which is the degenerate polygon rather than a shape.
/// </param>
/// <param name="LongAxisM">The longer side of the smallest-area rectangle containing the polygon.</param>
/// <param name="ShortAxisM">The shorter side of that rectangle.</param>
/// <param name="Elongation">
/// Long axis over short axis, so one is equidimensional and larger is longer. Stated this way
/// round because a doline is described as "three times as long as it is wide", not as "a third
/// as wide as it is long".
/// </param>
/// <param name="LongAxisAzimuthDegrees">
/// Where the long axis lies, in degrees clockwise from grid north of the working system,
/// folded into [0, 180). An axis has no direction — a rectangle's long side is equally well
/// described by either of its two opposite bearings — so a value of 190 would be the same
/// alignment as 10 and reporting it as a bearing would split one population of dolines into
/// two.
/// </param>
/// <param name="CentroidLongitude">Centroid, stored system.</param>
/// <param name="CentroidLatitude">Centroid, stored system.</param>
public sealed record PolygonMorphometryRow(
    Guid FeatureId,
    string? Name,
    bool GeometryValid,
    double? AreaM2,
    double? PerimeterM,
    double? Circularity,
    double? LongAxisM,
    double? ShortAxisM,
    double? Elongation,
    double? LongAxisAzimuthDegrees,
    double? CentroidLongitude,
    double? CentroidLatitude);

/// <summary>
/// The morphometry queries, built without a database so their access path can be pinned.
///
/// <para>
/// <b>Who is in the answer.</b> A polygon's own outline is not obviously a position, but its
/// centroid is one exactly, and its long-axis bearing places it as surely as a coordinate does
/// once the outline is known. So the whole answer is gated on exact placement, not only the
/// centroid: the alternative is a caller who may not be told where a protected doline is
/// reading its shape, its size and which way it lies, and then finding it. This matches the
/// floor the display rules already set — a protected feature carrying anything other than a
/// plain point is withheld entirely rather than snapped, because a shape leaks its own
/// position — so there is one rule here and not a second, weaker one.
/// </para>
///
/// <para>
/// <b>Why the transform comes last.</b> There is no index over the projected geometry and
/// deliberately so, the working system being configuration rather than something a migration
/// could name. Both queries therefore narrow in the stored system first — by primary key for
/// one feature, by an index-only envelope overlap for an area — and transform only the rows
/// that survived.
/// </para>
/// </summary>
public static class PolygonMorphometrySql
{
    /// <summary>
    /// The measured shape of one feature, or no row at all when it is not a polygon, is not
    /// visible, or is one the caller may read but not place. Those are deliberately the same
    /// answer: distinguishing them would say that a protected doline exists here.
    /// </summary>
    public static (string Sql, DynamicParameters Parameters) BuildForFeature(
        AccessContext ctx, Guid featureId, int workingSrid)
    {
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("mm_feature_id", featureId);
        parameters.Add(SridParameter, workingSrid);

        return (Build("AND f.id = @mm_feature_id", string.Empty, visibleSql, exactSql), parameters);
    }

    /// <summary>
    /// Every polygon of one kind inside a bounding box, largest first — the bulk table.
    /// </summary>
    /// <param name="featureTypeId">
    /// The kind to measure, or null for every polygon in the box. Null is not the useful case
    /// and is offered because the query is the same one: a box holds karst areas and cave
    /// sectors as well as dolines, and a table mixing them measures nothing in particular.
    /// </param>
    public static (string Sql, DynamicParameters Parameters) BuildForArea(
        AccessContext ctx,
        double west,
        double south,
        double east,
        double north,
        long? featureTypeId,
        int limit,
        int workingSrid)
    {
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("mm_west", west);
        parameters.Add("mm_south", south);
        parameters.Add("mm_east", east);
        parameters.Add("mm_north", north);
        // Typed explicitly: the null case is the "every kind" case, and an untyped null leaves
        // the driver nothing to infer the parameter's type from.
        parameters.Add("mm_feature_type_id", featureTypeId, DbType.Int64);
        parameters.Add("mm_limit", limit);
        parameters.Add(SridParameter, workingSrid);

        // The overlap operator is answered from the geometry index alone. The kind filter is
        // spelled so that a null means "every kind" rather than "no kind", which keeps one
        // statement instead of two that could drift apart.
        const string narrowing =
            """
            AND f.geom && ST_MakeEnvelope(@mm_west, @mm_south, @mm_east, @mm_north, 4326)
                  AND (@mm_feature_type_id IS NULL OR f.feature_type_id = @mm_feature_type_id)
            """;

        return (
            Build(narrowing, "ORDER BY \"AreaM2\" DESC NULLS LAST, s.id\nLIMIT @mm_limit", visibleSql, exactSql),
            parameters);
    }

    private const string SridParameter = "workingSrid";

    private static string Build(string narrowing, string ordering, string visibleSql, string exactSql)
    {
        // Materialised on purpose. Inlined — which is the planner's default — the transform is
        // substituted into every column that reads it and the projection runs once per column
        // rather than once per row.
        var projected = SpatialSql.ToWorking("f.geom", SridParameter);

        return $"""
            WITH shape AS MATERIALIZED (
                SELECT f.id, f.name, {projected} AS g
                FROM features f
                WHERE f.deleted_at IS NULL
                  AND f.geom IS NOT NULL
                  AND ST_Dimension(f.geom) = 2
                  {narrowing}
                  AND {visibleSql}
                  AND {exactSql}
            ),
            envelope AS MATERIALIZED (
                SELECT s.id, s.name, s.g, ST_IsValid(s.g) AS valid,
                       CASE WHEN ST_IsValid(s.g) THEN ST_OrientedEnvelope(s.g) END AS env
                FROM shape s
            ),
            sides AS (
                -- The smallest-area rectangle around the polygon, walked side by side. Its four
                -- sides come back as five vertices; the last has no successor and drops out.
                -- A polygon whose points are collinear has no such rectangle — the function
                -- answers with a line or a point instead — and then there are no axes to report.
                SELECT e.id,
                       ST_Distance(p.geom, lead(p.geom) OVER (PARTITION BY e.id ORDER BY p.path[2])) AS len,
                       ST_Azimuth(p.geom, lead(p.geom) OVER (PARTITION BY e.id ORDER BY p.path[2])) AS az
                FROM envelope e
                CROSS JOIN LATERAL ST_DumpPoints(e.env) p
                WHERE e.env IS NOT NULL AND GeometryType(e.env) = 'POLYGON'
            ),
            axes AS (
                SELECT id,
                       max(len) AS long_axis,
                       min(len) AS short_axis,
                       (array_agg(az ORDER BY len DESC))[1] AS long_az
                FROM sides
                WHERE len IS NOT NULL
                GROUP BY id
            )
            SELECT s.id AS "FeatureId",
                   s.name AS "Name",
                   s.valid AS "GeometryValid",
                   CASE WHEN s.valid THEN ST_Area(s.g) END AS "AreaM2",
                   CASE WHEN s.valid THEN ST_Perimeter(s.g) END AS "PerimeterM",
                   CASE WHEN s.valid AND ST_Perimeter(s.g) > 0
                        THEN 4 * pi() * ST_Area(s.g) / (ST_Perimeter(s.g) ^ 2) END AS "Circularity",
                   a.long_axis AS "LongAxisM",
                   a.short_axis AS "ShortAxisM",
                   CASE WHEN a.short_axis > 0 THEN a.long_axis / a.short_axis END AS "Elongation",
                   CASE WHEN a.long_az IS NOT NULL
                        THEN mod(degrees(a.long_az)::numeric, 180)::double precision END AS "LongAxisAzimuthDegrees",
                   CASE WHEN s.valid THEN ST_X(ST_Transform(ST_Centroid(s.g), 4326)) END AS "CentroidLongitude",
                   CASE WHEN s.valid THEN ST_Y(ST_Transform(ST_Centroid(s.g), 4326)) END AS "CentroidLatitude"
            FROM envelope s
            LEFT JOIN axes a ON a.id = s.id
            {ordering}
            """;
    }
}
