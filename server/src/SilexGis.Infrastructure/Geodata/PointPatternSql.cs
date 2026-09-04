// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>One entrance as a point-pattern statistic sees it: a position in metres, nothing else.</summary>
public sealed record PointPatternPointRow(double X, double Y);

/// <summary>
/// The ground a point pattern was measured over.
/// </summary>
/// <param name="WindowWkt">
/// The window as text in the projected working system, so the statistics can scatter random points
/// over the same shape the observed ones sit on and correct for its edges.
/// </param>
/// <param name="AreaM2">Its ground area, which is the denominator both statistics divide by.</param>
public sealed record PointPatternWindowRow(string? WindowWkt, double? AreaM2);

/// <summary>
/// The point set and the window that a nearest-neighbour index and a Ripley curve are computed
/// over.
///
/// <para>
/// <b>Only exactly-placeable entrances are read.</b> Unlike a density grid, where a protected
/// coordinate can be counted honestly after being rounded onto the protection lattice, a spacing
/// statistic is a statement about the distances between individual points — and a snapped point
/// carries no distance smaller than the lattice, so feeding it in would replace a measurement with
/// an artefact of the rounding, and repeatedly at that, since every snapped point in a cell sits on
/// the same intersection. Entrances whose position this caller may not see exactly are therefore
/// absent from the set rather than approximated into it, which makes the reported count smaller
/// than the number of entrances they can read, and deliberately so.
/// </para>
/// <para>
/// Coordinates are transformed into the installation's projected working system because both
/// statistics are about distances and areas in metres. The narrowing happens first, in the stored
/// geographic system, against the index that exists there; only the survivors are transformed.
/// </para>
/// </summary>
public static class PointPatternSql
{
    /// <summary>
    /// How finely the window's geographic edges are followed before they are projected. A window
    /// stated in degrees has curved edges in a projected system, and four transformed corners would
    /// cut the corners off it — a difference of a few parts in ten thousand over a window of a
    /// degree, which is small but is a bias in the denominator of every number computed from it.
    /// </summary>
    private const double WindowSegmentDegrees = 0.02d;

    /// <summary>Name of the parameter carrying the projected working system's code.</summary>
    private const string SridParameter = "workingSrid";

    /// <summary>The positions, in the projected working system, that the caller may place exactly.</summary>
    /// <param name="limit">
    /// At most this many rows are read. The caller asks for one more than it will accept, so that a
    /// set too large to compute over is refused rather than silently truncated into a statistic
    /// about an arbitrary subset.
    /// </param>
    public static async Task<IReadOnlyList<PointPatternPointRow>> PointsAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        double west,
        double south,
        double east,
        double north,
        int workingSrid,
        Guid? studyAreaFeatureId,
        int limit,
        CancellationToken ct)
    {
        var (sql, parameters) = BuildPoints(
            ctx, west, south, east, north, workingSrid, studyAreaFeatureId, limit);
        var rows = await db.Database.GetDbConnection().QueryAsync<PointPatternPointRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        return [.. rows];
    }

    /// <summary>The window itself, projected, with its ground area.</summary>
    public static async Task<PointPatternWindowRow> WindowAsync(
        SilexGisDbContext db,
        double west,
        double south,
        double east,
        double north,
        int workingSrid,
        Guid? studyAreaFeatureId,
        CancellationToken ct)
    {
        var (sql, parameters) = BuildWindow(west, south, east, north, workingSrid, studyAreaFeatureId);
        var row = await db.Database.GetDbConnection().QuerySingleOrDefaultAsync<PointPatternWindowRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        return row ?? new PointPatternWindowRow(null, null);
    }

    /// <summary>
    /// The point statement and its parameters, exposed so the access path — visible <i>and</i>
    /// exactly placeable — can be pinned by a test rather than assumed.
    /// </summary>
    public static (string Sql, DynamicParameters Parameters) BuildPoints(
        AccessContext ctx,
        double west,
        double south,
        double east,
        double north,
        int workingSrid,
        Guid? studyAreaFeatureId,
        int limit)
    {
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        AddWindowParameters(parameters, west, south, east, north, workingSrid, studyAreaFeatureId);
        parameters.Add("pp_limit", limit);

        var projected = SpatialSql.ToWorking("f.geom");
        var sql = $"""
            SELECT ST_X(p.g) AS "X", ST_Y(p.g) AS "Y"
            FROM (
                SELECT {projected} AS g
                FROM features f
                WHERE f.kind = {(short)FeatureKind.CaveEntrance}
                  AND f.deleted_at IS NULL
                  AND f.geom IS NOT NULL
                  AND f.geom && ST_MakeEnvelope(@pp_west, @pp_south, @pp_east, @pp_north, 4326)
                  AND (@pp_area_id IS NULL OR EXISTS (
                        SELECT 1 FROM features a
                        WHERE a.id = @pp_area_id AND a.deleted_at IS NULL
                          AND a.geom IS NOT NULL AND ST_Intersects(f.geom, a.geom)))
                  AND {visibleSql}
                  AND {exactSql}
                ORDER BY f.id
                LIMIT @pp_limit
            ) p
            """;

        return (sql, parameters);
    }

    /// <summary>The window statement and its parameters.</summary>
    /// <remarks>
    /// The study outline is not access-filtered here: it must already have been checked readable
    /// and exactly placeable by the caller, because a second access fragment cannot share the
    /// first one's parameter set, and because a window a caller may not place is a window they may
    /// not be shown the edges of.
    /// </remarks>
    public static (string Sql, DynamicParameters Parameters) BuildWindow(
        double west,
        double south,
        double east,
        double north,
        int workingSrid,
        Guid? studyAreaFeatureId)
    {
        var parameters = new DynamicParameters();
        AddWindowParameters(parameters, west, south, east, north, workingSrid, studyAreaFeatureId);

        var sql = $"""
            WITH box AS (
                SELECT ST_Segmentize(
                    ST_MakeEnvelope(@pp_west, @pp_south, @pp_east, @pp_north, 4326),
                    @pp_segment) AS g
            ),
            area AS (
                SELECT a.geom
                FROM features a
                WHERE @pp_area_id IS NOT NULL
                  AND a.id = @pp_area_id
                  AND a.deleted_at IS NULL
                  AND a.geom IS NOT NULL
            ),
            -- Named "win" rather than "window", which is a reserved word in SQL.
            win AS (
                SELECT CASE WHEN @pp_area_id IS NULL THEN b.g ELSE ST_Intersection(b.g, a.geom) END AS g
                FROM box b
                LEFT JOIN area a ON TRUE
            )
            SELECT ST_AsText({SpatialSql.ToWorking("w.g")}) AS "WindowWkt",
                   ST_Area({SpatialSql.ToWorking("w.g")}) AS "AreaM2"
            FROM win w
            """;

        return (sql, parameters);
    }

    private static void AddWindowParameters(
        DynamicParameters parameters,
        double west,
        double south,
        double east,
        double north,
        int workingSrid,
        Guid? studyAreaFeatureId)
    {
        parameters.Add("pp_west", west);
        parameters.Add("pp_south", south);
        parameters.Add("pp_east", east);
        parameters.Add("pp_north", north);
        parameters.Add("pp_segment", WindowSegmentDegrees);
        parameters.Add(SridParameter, workingSrid);
        // Typed explicitly: null here means "no study area", and an untyped null leaves the driver
        // nothing to infer the parameter's type from.
        parameters.Add("pp_area_id", studyAreaFeatureId, DbType.Guid);
    }
}
