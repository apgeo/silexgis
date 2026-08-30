// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// How close two caves come to each other: the shortest line between the two bodies of line
/// work, measured in three dimensions.
/// </summary>
/// <remarks>
/// Every length is metres, measured in the installation's working system. The two ends of the
/// line are handed back in the stored system, with their altitudes, because that is what a map
/// and a three-dimensional scene draw.
/// </remarks>
/// <param name="CaveAId">The lower of the two cave ids, so one pair is one row.</param>
/// <param name="CaveAName">Its name, so a table of pairs needs no second query.</param>
/// <param name="CaveBId">The higher of the two cave ids.</param>
/// <param name="CaveBName">Its name.</param>
/// <param name="HasAltitudes">
/// True when both bodies of line work carry real altitudes. A drawn centerline that never had
/// any is stored with zero in its third ordinate so it can be held and drawn at all, and
/// measuring between two of those would report a vertical separation of nothing and a distance
/// that is really a plan distance — a confident answer to a question the data cannot answer. So
/// when this is false every measurement below is null, and the caller says why.
/// </param>
/// <param name="DistanceM">The shortest distance in three dimensions.</param>
/// <param name="HorizontalDistanceM">
/// The plan separation of the two ends of that line — not the shortest plan distance between the
/// caves, which is a different pair of points and a smaller number.
/// </param>
/// <param name="VerticalDistanceM">The altitude difference between the same two ends.</param>
/// <param name="BearingDegrees">
/// True bearing from the first cave's end of the line to the second's, degrees clockwise from
/// north. Null when the two ends coincide, which is two caves that touch and have no direction
/// between them.
/// </param>
/// <param name="FromLongitude">The first cave's end of the line, stored system.</param>
/// <param name="FromLatitude">The first cave's end of the line, stored system.</param>
/// <param name="FromAltitude">The first cave's end of the line.</param>
/// <param name="ToLongitude">The second cave's end of the line, stored system.</param>
/// <param name="ToLatitude">The second cave's end of the line, stored system.</param>
/// <param name="ToAltitude">The second cave's end of the line.</param>
public sealed record ClosestApproachRow(
    Guid CaveAId,
    string? CaveAName,
    Guid CaveBId,
    string? CaveBName,
    bool HasAltitudes,
    double? DistanceM,
    double? HorizontalDistanceM,
    double? VerticalDistanceM,
    double? BearingDegrees,
    double? FromLongitude,
    double? FromLatitude,
    double? FromAltitude,
    double? ToLongitude,
    double? ToLatitude,
    double? ToAltitude);

/// <summary>
/// The closest-approach queries, built without a database so their access path can be pinned.
///
/// <para>
/// <b>Who is in the answer, and why this gate is the strictest one here.</b> The rule the
/// duplicate search already states — "there is something within twelve metres of this point" is
/// itself a position — applies here with more force, because this answers a distance and a
/// bearing between two <i>named</i> caves. Anyone holding one cave's position could then place
/// the other exactly. So both sides must be readable <i>and</i> placeable, and a caller who may
/// read a cave but not place it is given no distance at all: not a rounded one, not one snapped
/// to a grid, nothing. A snapped answer is still an answer, and two snapped answers about the
/// same pair from different vantage points would triangulate what the snapping was meant to
/// hide.
/// </para>
/// <para>
/// <b>Both rows are gated, the cave and its line work.</b> The line work is a feature in its own
/// right and carries the cave among its protected roots, so in ordinary data the two answers
/// agree; asking both means a route into this geometry through the cave and a route through the
/// centerline cannot come out differently. A cave that fails either test is simply not in the
/// pool, and a pair with one member missing produces no row — the same answer as a cave with no
/// line work at all, deliberately, because distinguishing them would say that a cave is being
/// kept from somebody.
/// </para>
/// <para>
/// <b>Why the transform comes last.</b> Three-dimensional distance is cartesian: it adds the
/// three ordinates as they are given, so run over stored degrees it returns a number that is
/// mostly the height difference and is not a distance at all. Every figure here therefore comes
/// from the working system. There is no index over the projected geometry and deliberately so,
/// the working system being configuration rather than something a migration could name, so both
/// queries narrow in the stored system first — by cave id for a pair, by an index-only envelope
/// overlap for an area — and transform only the survivors.
/// </para>
/// <para>
/// <b>What bounds the work, which is not the distance threshold.</b> The area query pairs every
/// admitted cave with every other one, so its cost grows with the square of how many caves are
/// admitted — and the threshold is the predicate being evaluated by that pairing, not a filter
/// ahead of it. What bounds it is a ceiling on how many caves enter the pool at all, applied
/// inside the narrowing CTE, in the same way the map layers cap the rows they will draw. Above
/// that ceiling the table is built from the first caves in id order rather than from the whole
/// box: a bounded answer to an unreasonable question, rather than a request that never returns.
/// </para>
/// <para>
/// <b>Nothing on the pairing path casts a whole survey to the geography type.</b> A survey is a
/// multi-line string with tens of thousands of components; a spheroid distance over two of them
/// costs a detoast and a full traversal each, and the pairing evaluates its predicate once per
/// pair with no index able to help it. So the pre-filter is a bounding-box overlap between the
/// two projected geometries, expanded by the threshold — a header read, no traversal — and the
/// metre-accurate test that follows it runs in the working system on the rows that survived.
/// A plan test can only over-include, the shortest plan distance between two bodies of line work
/// never being larger than the shortest distance in three dimensions, so nothing inside the
/// threshold is lost by pre-filtering in two.
/// </para>
/// </summary>
public static class ClosestApproachSql
{
    private const string SridParameter = "workingSrid";

    /// <summary>
    /// The closest approach of two named caves, or no row when either side is missing from the
    /// pool. The two ids may be given in either order; the row names them lowest first.
    /// </summary>
    public static (string Sql, DynamicParameters Parameters) BuildForPair(
        AccessContext ctx, Guid caveA, Guid caveB, int workingSrid)
    {
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("ca_cave_ids", AccessSql.UuidArray([caveA, caveB]));
        parameters.Add(SridParameter, workingSrid);

        return (
            Build(forArea: false, "AND c.cave_feature_id = ANY(@ca_cave_ids)", visibleSql, exactSql),
            parameters);
    }

    /// <summary>
    /// The closest pairs inside a bounding box, nearest first — the top-k table. Only pairs both
    /// of whose caves the caller may place are considered, so the table is not a way to ask the
    /// per-pair question about a cave the per-pair question would refuse.
    /// </summary>
    /// <param name="maxMetres">
    /// How far apart two caves may be and still be worth reporting, in the working system's
    /// metres — the same metres every figure on the answer is stated in.
    /// </param>
    /// <param name="maxCaves">
    /// How many caves may enter the pairing at all. This, and not the threshold, is what keeps
    /// the quadratic pairing bounded; see the note on this class.
    /// </param>
    public static (string Sql, DynamicParameters Parameters) BuildForArea(
        AccessContext ctx,
        double west,
        double south,
        double east,
        double north,
        double maxMetres,
        int limit,
        int maxCaves,
        int workingSrid)
    {
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("ca_west", west);
        parameters.Add("ca_south", south);
        parameters.Add("ca_east", east);
        parameters.Add("ca_north", north);
        parameters.Add("ca_max_metres", maxMetres);
        parameters.Add("ca_limit", limit);
        parameters.Add("ca_candidates", maxCaves);
        parameters.Add(SridParameter, workingSrid);

        return (
            Build(
                forArea: true,
                // The overlap operator is answered from the geometry index alone.
                "AND f.geom && ST_MakeEnvelope(@ca_west, @ca_south, @ca_east, @ca_north, 4326)",
                visibleSql,
                exactSql),
            parameters);
    }

    private static string Build(bool forArea, string shapeNarrowing, string visibleSql, string exactSql)
    {
        // Materialised on purpose: inlined — which is the planner's default for a CTE read once —
        // the projection is substituted into every column that reads it and runs once per column
        // rather than once per cave.
        var projected = SpatialSql.ToWorking("s.geom", SridParameter);
        var hasAltitudes = SpatialSql.HasAltitudes("f.geom");

        // The ceiling on the pairing's input. Ordered so the cut is the same cut on every run of
        // the same request rather than whatever the scan happened to reach first. The per-pair
        // question is already narrowed to two named caves and needs none.
        var candidateCap = forArea
            ? "\n      ORDER BY c.cave_feature_id\n      LIMIT @ca_candidates"
            : string.Empty;

        // The bounding boxes first, then the metric test on what survives — the class note says
        // why neither of them is a spheroid cast of a whole survey.
        var pairNarrowing = forArea
            ? "AND ST_Expand(a.g, @ca_max_metres) && b.g\n"
                + "                       AND ST_DWithin(a.g, b.g, @ca_max_metres)"
            : string.Empty;

        // A table of nearest pairs carries no room to say why a row is missing, so a pair
        // whose line work has no altitudes is left out of it rather than listed with nothing in
        // it. The per-pair question is asked about two named caves and can say so, which is why
        // it keeps such a pair and reports the reason instead.
        var altitudeFilter = forArea ? "AND a.has_z AND b.has_z" : string.Empty;

        var ordering = forArea
            ? "ORDER BY dist ASC NULLS LAST, a_id, b_id\n                LIMIT @ca_limit"
            : string.Empty;

        return $"""
            WITH shape AS MATERIALIZED (
                SELECT c.cave_feature_id AS cave_id,
                       f.geom AS geom,
                       {hasAltitudes} AS has_z
                FROM centerlines c
                JOIN features f ON f.id = c.id
                WHERE c.is_default
                  AND f.deleted_at IS NULL
                  AND f.geom IS NOT NULL
                  AND NOT ST_IsEmpty(f.geom)
                  {shapeNarrowing}
                  AND {visibleSql}
                  AND {exactSql}{candidateCap}
            ),
            placed AS MATERIALIZED (
                -- The cave itself, reached by primary key from the line work that was narrowed
                -- above, and held to the same two tests. A cave failing either drops out with
                -- its shape, so it is never one end of a measurement.
                SELECT s.cave_id, f.name, s.has_z, {projected} AS g
                FROM shape s
                JOIN features f ON f.id = s.cave_id AND f.kind = {(short)FeatureKind.Cave}
                WHERE f.deleted_at IS NULL
                  AND {visibleSql}
                  AND {exactSql}
            ),
            pair AS MATERIALIZED (
                SELECT a.cave_id AS a_id, a.name AS a_name, b.cave_id AS b_id, b.name AS b_name,
                       a.has_z AND b.has_z AS has_z,
                       CASE WHEN a.has_z AND b.has_z THEN ST_3DShortestLine(a.g, b.g) END AS ln
                FROM placed a
                JOIN placed b ON a.cave_id < b.cave_id
                       {pairNarrowing}
                       {altitudeFilter}
            ),
            ends AS (
                -- The shortest line is two points; everything reported is read off them. It is
                -- computed once here because a query that called for it in each output column
                -- would run the whole comparison once per column.
                SELECT p.*,
                       ST_StartPoint(p.ln) AS pa,
                       ST_EndPoint(p.ln) AS pb
                FROM pair p
            ),
            ranked AS (
                -- Where the table is cut down to the rows it will return. Everything below this
                -- — two transforms back to the stored system and a spheroid bearing, per row —
                -- is presentation, and running it over every pair in the box before discarding
                -- all but a handful would be paying for answers nobody is shown.
                SELECT e.*, ST_3DDistance(e.pa, e.pb) AS dist
                FROM ends e
                {ordering}
            )
            SELECT e.a_id AS "CaveAId",
                   e.a_name AS "CaveAName",
                   e.b_id AS "CaveBId",
                   e.b_name AS "CaveBName",
                   e.has_z AS "HasAltitudes",
                   e.dist AS "DistanceM",
                   ST_Distance(e.pa, e.pb) AS "HorizontalDistanceM",
                   abs(ST_Z(e.pb) - ST_Z(e.pa)) AS "VerticalDistanceM",
                   -- On the spheroid rather than off the working system's grid, so the bearing is
                   -- the true one every other bearing in the application is: grid north and true
                   -- north differ by up to a few degrees across a projected zone. Two points, and
                   -- only for the rows being returned.
                   degrees(ST_Azimuth(
                       ST_Transform(e.pa, 4326)::geography,
                       ST_Transform(e.pb, 4326)::geography)) AS "BearingDegrees",
                   ST_X(ST_Transform(e.pa, 4326)) AS "FromLongitude",
                   ST_Y(ST_Transform(e.pa, 4326)) AS "FromLatitude",
                   ST_Z(e.pa) AS "FromAltitude",
                   ST_X(ST_Transform(e.pb, 4326)) AS "ToLongitude",
                   ST_Y(ST_Transform(e.pb, 4326)) AS "ToLatitude",
                   ST_Z(e.pb) AS "ToAltitude"
            FROM ranked e
            """;
    }
}
