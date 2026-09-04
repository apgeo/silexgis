// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// One straight piece of a mapped structural line, with the trend and the ground length of that
/// piece.
/// </summary>
/// <remarks>
/// A fracture trace is drawn as a polyline and its vertices are where the person drawing it changed
/// direction, so a single feature carries several trends and summing it as one bearing would report
/// the direction from one end of the trace to the other — a number no part of the trace runs along
/// once it bends. Each straight piece is measured separately and weighted by its own length, which
/// is the same treatment a passage gets and the reason the two roses are comparable at all.
/// </remarks>
/// <param name="FeatureId">The trace this piece came from.</param>
/// <param name="AzimuthDegrees">
/// Bearing of the piece, 0–360, measured on the spheroid — the same measurement a survey leg's
/// bearing is, and not a grid bearing in any projected system. Null when the two ends coincide,
/// which a drawn line can carry if somebody clicked twice in one place. Folding it onto the
/// half-circle is the consumer's job, as it is for passage.
/// </param>
/// <param name="LengthM">Ground length of the piece, metres.</param>
public sealed record StructureLineSegmentRow(Guid FeatureId, double? AzimuthDegrees, double LengthM);

/// <summary>
/// The mapped structural lines near a cave, cut into straight pieces that can be put in a rose.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the reach is measured in metres without a projected system.</b> The question is "faults
/// within so many metres of this cave", and the geography type answers a distance in metres on the
/// spheroid without any working system being chosen. The stored index is on the 4326 geometry, so
/// the query narrows with a bounding-box overlap first — expanded by the reach converted to degrees
/// at the cave's own latitude, generously, so the box can only ever be too big — and applies the
/// exact spheroid distance to the handful of rows that survive. Nothing here transforms anything,
/// which is what lets this stand up without the projected ground.
/// </para>
/// <para>
/// <b>What the reach is measured from.</b> Everything the cave is: its own geometry together with
/// the geometry of its entrances and its drawn line work, gathered through the containment
/// hierarchy. So "within five hundred metres" is within five hundred metres of any part of the
/// cave, not of a point somebody picked to stand for the whole of it — which for a cave a kilometre
/// long is a different question with a different answer. A cave with nothing placed anywhere has
/// nothing to measure from and the answer is empty.
/// </para>
/// <para>
/// Anything under the cave that carries a protection of its own is left out of that gathering, and
/// so is anything the caller may not read at all: both would otherwise let the reach be varied to
/// bracket where a withheld feature is, since a trace entering the answer at one reach and not at a
/// shorter one places whatever pulled it in. Protection and read visibility are two separate axes —
/// a private child feature under a readable cave carries no location protection of its own — so
/// both are asked, not one. The cave's own row is always in, because the caller was already
/// established to be one who may place it: the route that asks this question refuses everybody else
/// outright.
/// </para>
/// <para>
/// <b>Why the gathered geometry is flattened.</b> A cave's stored geometries deliberately mix
/// dimensions — a reduced centerline always carries a height, an entrance recorded without an
/// altitude does not — and collecting a two-dimensional geometry together with a three-dimensional
/// one is an error rather than a wider collection. The reach is a ground distance on the spheroid
/// and wants no third ordinate anyway, so every gathered geometry is dropped to two dimensions
/// before it is collected.
/// </para>
/// <para>
/// <b>Protection.</b> A trace is withheld from a caller who may not place it exactly, on the same
/// footing as a doline outline: a line's bearing places it as surely as a coordinate does once its
/// shape is known, and there is no snapped form of a bearing. The cave end is gated by the caller
/// before this runs.
/// </para>
/// </remarks>
public static class StructureLineSql
{
    /// <summary>
    /// Degrees of latitude per metre, near enough for padding a bounding box. Latitude spacing
    /// varies by about a fifth of a percent between the equator and the pole and the pad is a
    /// generous over-estimate in any case.
    /// </summary>
    private const double MetresPerDegreeLatitude = 110_540d;

    /// <summary>
    /// Metres per degree of longitude at the equator. Divided by the cosine of the cave's latitude
    /// in the query, so the box widens where the meridians converge.
    /// </summary>
    private const double MetresPerDegreeLongitudeAtEquator = 111_320d;

    /// <summary>
    /// The structural line pieces within <paramref name="metres"/> of a cave that the caller may
    /// both read and place exactly.
    /// </summary>
    public static async Task<IReadOnlyList<StructureLineSegmentRow>> NearCaveAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        Guid caveFeatureId,
        long featureTypeId,
        double metres,
        int maxFeatures,
        CancellationToken ct)
    {
        var (sql, parameters) = BuildNearCave(ctx, caveFeatureId, featureTypeId, metres, maxFeatures);
        var connection = db.Database.GetDbConnection();
        var rows = await connection.QueryAsync<StructureLineSegmentRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        return [.. rows];
    }

    /// <summary>
    /// The structural line pieces that sit under one area of the containment hierarchy and that
    /// the caller may both read and place exactly.
    /// </summary>
    /// <remarks>
    /// The area end of the question, where <see cref="NearCaveAsync"/> is the buffer end. A karst
    /// area is a structural domain somebody drew a boundary around, and "the faults in this
    /// massif" is a different question from "the faults within a kilometre of this cave" whenever
    /// the massif is not roughly circular — a buffer centred in a long valley averages in traces
    /// belonging to the block on the other side of it. The subtree is resolved to rows through the
    /// containment closure inside the same visibility filter, so a trace kept from this caller
    /// never enters the join and cannot be detected by asking about the area that contains it.
    /// </remarks>
    public static async Task<IReadOnlyList<StructureLineSegmentRow>> InAreaAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        Guid areaFeatureId,
        long featureTypeId,
        int maxFeatures,
        CancellationToken ct)
    {
        var (sql, parameters) = BuildInArea(ctx, areaFeatureId, featureTypeId, maxFeatures);
        var connection = db.Database.GetDbConnection();
        var rows = await connection.QueryAsync<StructureLineSegmentRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        return [.. rows];
    }

    /// <summary>
    /// The area-scoped statement and its parameters, exposed for the same reason the buffer one is.
    /// </summary>
    public static (string Sql, DynamicParameters Parameters) BuildInArea(
        AccessContext ctx, Guid areaFeatureId, long featureTypeId, int maxFeatures)
    {
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("sl_area_id", areaFeatureId);
        parameters.Add("sl_type_id", featureTypeId);
        parameters.Add("sl_max_features", maxFeatures);

        var sql = $"""
            WITH nearby AS (
                SELECT f.id, f.geom
                FROM features f
                WHERE f.deleted_at IS NULL
                  AND f.geom IS NOT NULL
                  AND ST_Dimension(f.geom) = 1
                  AND f.feature_type_id = @sl_type_id
                  AND @sl_area_id = ANY(f.ancestor_ids)
                  AND {visibleSql}
                  AND {exactSql}
                ORDER BY f.id
                LIMIT @sl_max_features
            )
            {SegmentTail}
            """;

        return (sql, parameters);
    }

    /// <summary>
    /// The statement and its parameters, exposed so the access path and the index the planner
    /// chooses can be pinned by a test rather than assumed.
    /// </summary>
    public static (string Sql, DynamicParameters Parameters) BuildNearCave(
        AccessContext ctx,
        Guid caveFeatureId,
        long featureTypeId,
        double metres,
        int maxFeatures)
    {
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("sl_cave_id", caveFeatureId);
        parameters.Add("sl_type_id", featureTypeId);
        parameters.Add("sl_metres", metres);
        parameters.Add("sl_max_features", maxFeatures);
        parameters.Add("sl_lat_metres", MetresPerDegreeLatitude);
        parameters.Add("sl_lon_metres", MetresPerDegreeLongitudeAtEquator);

        var withinReach = SpatialSql.WithinMetres("f.geom", "centre.g", "sl_metres");

        var sql = $"""
            WITH centre AS (
                SELECT ST_Collect(ST_Force2D(f.geom)) AS g,
                       ST_Y(ST_Centroid(ST_Collect(ST_Force2D(f.geom)))) AS lat
                FROM features f
                WHERE @sl_cave_id = ANY(f.ancestor_ids)
                  AND f.deleted_at IS NULL
                  AND f.geom IS NOT NULL
                  AND (f.id = @sl_cave_id OR NOT f.is_protected_effective)
                  AND (f.id = @sl_cave_id OR {visibleSql})
            ),
            box AS (
                SELECT ST_Expand(
                           centre.g,
                           @sl_metres / (@sl_lon_metres * greatest(cos(radians(centre.lat)), 0.01)),
                           @sl_metres / @sl_lat_metres) AS b,
                       centre.g AS g
                FROM centre
            ),
            nearby AS (
                SELECT f.id, f.geom
                FROM features f, box AS centre
                WHERE f.deleted_at IS NULL
                  AND f.geom IS NOT NULL
                  AND ST_Dimension(f.geom) = 1
                  AND f.feature_type_id = @sl_type_id
                  AND f.geom && centre.b
                  AND {withinReach}
                  AND {visibleSql}
                  AND {exactSql}
                ORDER BY f.id
                LIMIT @sl_max_features
            )
            {SegmentTail}
            """;

        return (sql, parameters);
    }

    /// <summary>
    /// The part both statements share: each admitted trace cut into its straight pieces, with the
    /// spheroid bearing and ground length of each. Held in one place so the buffer-scoped and the
    /// area-scoped answers cannot come to mean two different measurements.
    /// </summary>
    private const string SegmentTail =
        """
        SELECT n.id AS "FeatureId",
               degrees(ST_Azimuth(
                   ST_StartPoint(ds.geom)::geography,
                   ST_EndPoint(ds.geom)::geography)) AS "AzimuthDegrees",
               ST_Distance(
                   ST_StartPoint(ds.geom)::geography,
                   ST_EndPoint(ds.geom)::geography) AS "LengthM"
        FROM nearby n
        CROSS JOIN LATERAL ST_DumpSegments(n.geom) ds
        ORDER BY n.id
        """;
}
