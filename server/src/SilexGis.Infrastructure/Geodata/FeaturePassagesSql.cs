// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// The surface feature a passage question is asked about: a doline, a sinkhole, or any other
/// polygon on the map.
/// </summary>
/// <param name="Name">The feature's name.</param>
/// <param name="IsPolygon">True for an outline, false for a point.</param>
public sealed record FeaturePassagesSubjectRow(string? Name, bool IsPolygon);

/// <summary>
/// One cave whose passage lies within reach of the surface feature, and the nearest the
/// passage comes to it.
/// </summary>
/// <param name="CaveId">The cave.</param>
/// <param name="CaveName">Its name.</param>
/// <param name="LateralOffsetM">Plan distance in metres, in the working system, from the feature to
/// the nearest part of the cave's line work; zero where the line work runs under an outline.</param>
/// <param name="ClosestLongitude">Longitude, stored system, of the point of the line work that is
/// nearest the feature.</param>
/// <param name="ClosestLatitude">Latitude of the same point.</param>
public sealed record FeaturePassageRow(
    Guid CaveId,
    string? CaveName,
    double LateralOffsetM,
    double ClosestLongitude,
    double ClosestLatitude);

/// <summary>
/// Which caves have passage beneath, or beside, a surface feature — built without a database so
/// the access path can be pinned.
///
/// <para>
/// <b>Who is in the answer.</b> Two things are positions here, the feature's and each cave's, and
/// each is gated on exact placement. The feature must be one the caller may read and place: a
/// protected sinkhole is withheld whole rather than snapped, because a shape leaks its own
/// position and because "these caves lie beneath it" would place the caves from it and it from
/// the caves. Each cave must pass the same test on the cave row and on its line work, so the list
/// — its members, its order, its length and whether it is cut short — is built only from caves the
/// caller could already place one at a time. A cave failing either test is not a candidate and
/// leaves no trace, and the cap on the list is applied after that, so a hidden cave can never
/// occupy a slot. That is also why the distance may be a parameter: varying it can only widen or
/// narrow a set of caves the caller can already place exactly.
/// </para>
/// <para>
/// <b>Why the transform comes last.</b> Lengths are metres in the working system, which has no
/// index, so candidates are narrowed first by an index-only envelope overlap in the stored system
/// — the feature's envelope grown by the distance, converted to degrees with the longitude scale
/// of its own latitude and never smaller than the true reach — and only the survivors are
/// projected. Nothing here casts a whole survey to the geography type.
/// </para>
/// </summary>
public static class FeaturePassagesSql
{
    private const string SridParameter = "workingSrid";

    /// <summary>
    /// What kind of feature this is, or no row when it is not visible, not placeable, not a plain
    /// feature, or not a point or an outline. Those are deliberately one answer.
    /// </summary>
    public static (string Sql, DynamicParameters Parameters) BuildSubject(AccessContext ctx, Guid featureId)
    {
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("fp_feature_id", featureId);

        var sql = $"""
            SELECT f.name AS "Name",
                   ST_GeometryType(f.geom) IN ('ST_Polygon', 'ST_MultiPolygon') AS "IsPolygon"
            FROM features f
            WHERE f.id = @fp_feature_id
              {SubjectFilter(visibleSql, exactSql)}
            """;
        return (sql, parameters);
    }

    /// <summary>
    /// The caves with line work within <paramref name="distanceM"/> metres of the feature, nearest
    /// first, at most <paramref name="limit"/> plus whatever the caller added to detect a cut.
    /// </summary>
    /// <param name="candidates">How many caves may be projected and measured at all; the nearest in
    /// the stored system are kept.</param>
    public static (string Sql, DynamicParameters Parameters) BuildPassages(
        AccessContext ctx,
        Guid featureId,
        double distanceM,
        int limit,
        int candidates,
        int workingSrid)
    {
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        // The same two tests for the cave row, under its own alias: they must run before the
        // candidate cap so a cave the reader may not place never takes a slot.
        var (caveVisibleSql, caveExactSql, _) = AccessSql.FeatureLayerFragments(ctx, "cv");
        parameters.Add("fp_feature_id", featureId);
        parameters.Add("fp_distance", distanceM);
        parameters.Add("fp_limit", limit);
        parameters.Add("fp_candidates", candidates);
        parameters.Add(SridParameter, workingSrid);

        var projectedFeature = SpatialSql.ToWorking("f.geom", SridParameter);
        var projectedLine = SpatialSql.ToWorking("sh.geom", SridParameter);

        var sql = $"""
            WITH subject AS MATERIALIZED (
                SELECT f.geom AS geom,
                       {projectedFeature} AS g,
                       -- The reach in degrees, using the smaller longitude scale of the two
                       -- latitudes the envelope spans, so the box never under-reaches. It is only
                       -- a pre-filter: the metric test below is the real cut.
                       @fp_distance / (111000.0 * GREATEST(
                           LEAST(cos(radians(ST_YMin(f.geom))), cos(radians(ST_YMax(f.geom)))),
                           0.01)) AS reach
                FROM features f
                WHERE f.id = @fp_feature_id
                  {SubjectFilter(visibleSql, exactSql)}
            ),
            shape AS MATERIALIZED (
                SELECT c.cave_feature_id AS cave_id, f.geom AS geom
                FROM subject s
                JOIN centerlines c ON c.is_default
                JOIN features f ON f.id = c.id
                JOIN features cv ON cv.id = c.cave_feature_id AND cv.kind = {(short)FeatureKind.Cave}
                WHERE f.deleted_at IS NULL
                  AND cv.deleted_at IS NULL
                  AND f.geom IS NOT NULL
                  AND NOT ST_IsEmpty(f.geom)
                  AND f.geom && ST_Expand(s.geom, s.reach)
                  AND {visibleSql}
                  AND {exactSql}
                  AND {caveVisibleSql}
                  AND {caveExactSql}
                ORDER BY f.geom <-> s.geom, c.cave_feature_id
                LIMIT @fp_candidates
            ),
            placed AS MATERIALIZED (
                -- The cave itself, held to the same two tests as its line work. A cave failing
                -- either drops out with its shape.
                SELECT sh.cave_id, f.name, {projectedLine} AS g
                FROM shape sh
                JOIN features f ON f.id = sh.cave_id AND f.kind = {(short)FeatureKind.Cave}
                WHERE f.deleted_at IS NULL
                  AND {visibleSql}
                  AND {exactSql}
            ),
            near AS (
                SELECT p.cave_id, p.name, ST_Distance(p.g, s.g) AS dist,
                       ST_ClosestPoint(p.g, s.g) AS cp
                FROM placed p
                CROSS JOIN subject s
                WHERE ST_DWithin(p.g, s.g, @fp_distance)
                ORDER BY dist, p.cave_id
                LIMIT @fp_limit
            )
            SELECT n.cave_id AS "CaveId",
                   n.name AS "CaveName",
                   n.dist AS "LateralOffsetM",
                   ST_X(ST_Transform(n.cp, 4326)) AS "ClosestLongitude",
                   ST_Y(ST_Transform(n.cp, 4326)) AS "ClosestLatitude"
            FROM near n
            ORDER BY n.dist, n.cave_id
            """;
        return (sql, parameters);
    }

    private static string SubjectFilter(string visibleSql, string exactSql) =>
        $"""
        AND f.kind = {(short)FeatureKind.Generic}
              AND f.deleted_at IS NULL
              AND f.geom IS NOT NULL
              AND NOT ST_IsEmpty(f.geom)
              AND ST_GeometryType(f.geom) IN ('ST_Point', 'ST_Polygon', 'ST_MultiPolygon')
              AND {visibleSql}
              AND {exactSql}
        """;
}
