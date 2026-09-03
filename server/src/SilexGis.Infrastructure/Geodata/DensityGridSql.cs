// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>One cell of a density grid, with everything needed to normalise its count.</summary>
/// <param name="CellX">Column index on the absolute lattice anchored at the prime meridian.</param>
/// <param name="CellY">Row index on the same lattice, anchored at the equator.</param>
/// <param name="Count">How many entrances fell in the cell, after visibility filtering.</param>
/// <param name="CellAreaM2">
/// The cell's ground area, measured on the spheroid rather than assumed to be the cell size
/// squared. A cell one kilometre tall is narrower than a kilometre on the ground everywhere but
/// the equator, and by a fifth of its width at the latitude of the Carpathians.
/// </param>
/// <param name="StudyAreaM2">
/// How much of the cell lies inside the study-area outline, when one was given, so a cell half
/// outside the karst is divided by the half that is karst rather than by the whole square. Null
/// when no study area was named.
/// </param>
public sealed record DensityCellRow(
    long CellX,
    long CellY,
    double West,
    double South,
    double East,
    double North,
    int Count,
    double CellAreaM2,
    double? StudyAreaM2);

/// <summary>
/// How thickly cave entrances sit, counted into a grid of a chosen cell size.
/// </summary>
/// <remarks>
/// <para>
/// <b>Who is counted, and where they are counted.</b> Every entrance this caller may read is
/// counted, including the ones whose exact position is closed to them — leaving those out would
/// make the density itself a disclosure, since a district's number would fall by one for every
/// hidden cave and the fall could be located by comparing two windows. What changes for such a row
/// is not whether it counts but where: its coordinate enters the aggregate already rounded to the
/// installation's location-protection grid, by the identical arithmetic the protection rule uses
/// when it hands out a snapped point. Every rounding here goes through <c>numeric</c> rather than
/// straight over a double, because PostgreSQL's <c>round(double precision)</c> rounds half to
/// <i>even</i> while <c>round(numeric)</c> rounds half away from zero — and half away from zero is
/// what the protection rule itself does. Getting that wrong is not a rounding nicety: at a cell
/// exactly twice the protection grid every snapped coordinate lands on a cell boundary, so
/// half-to-even sends whole alternating rows of the lattice into the same cell and the published
/// surface comes out striped, several times over-dense in one cell and empty in its neighbour,
/// with nothing on the wire recording that it happened.
/// </para>
/// <para>
/// <b>Counted over the grid, not over the request.</b> The window that selects features is the
/// enumerated cells' own extent, not the bbox that produced them. A cell the bbox cuts in half
/// would otherwise report half its features against the whole cell's area — so the same cell would
/// answer differently depending on where the caller had panned — and, worse, a caller could shrink
/// the bbox inside a single cell and read the true position of a protected entrance out of whether
/// the count moved, which is exactly the disclosure the cell-size floor exists to prevent. The
/// extent is widened by one protection cell first, so an entrance just outside it that snaps onto
/// the lattice inside it is still counted where it belongs.
/// </para>
/// <para>
/// <b>The study area filters the count as well as the denominator.</b> When an outline is named it
/// restricts which features are counted, not only how much ground each cell is divided by. Without
/// that, a cell clipping the outline's corner would divide the entrances of the whole cell — most
/// of them outside the karst — by the sliver of it that is karst, and report a density many times
/// the true one.
/// </para>
/// <para>
/// <b>The cell size is not this file's decision.</b> A grid finer than the protection lattice would
/// undo the snap, so the caller's cell size is checked against that floor before this statement is
/// built, and a smaller one is refused rather than quietly widened. This body assumes the check has
/// already happened, which is why the only caller is the endpoint that performs it.
/// </para>
/// <para>
/// <b>Empty cells are returned.</b> A surface with holes where the count is zero is not a surface,
/// and a reader cannot tell a cell nobody has surveyed from a cell the request never covered. The
/// number of cells is bounded by the caller before the query runs, so the whole window is cheap to
/// enumerate.
/// </para>
/// </remarks>
public static class DensityGridSql
{
    /// <summary>
    /// The counted grid over one window, optionally clipped to a study-area outline.
    /// </summary>
    /// <param name="protectionGridMeters">
    /// The installation's location-protection grid. Coordinates the caller may not see exactly are
    /// rounded onto it before they are counted.
    /// </param>
    /// <param name="cellDegrees">
    /// The grid cell, already converted through the same metres-to-degrees conversion the
    /// protection grid uses, and already checked to be no finer than it.
    /// </param>
    /// <param name="studyAreaFeatureId">
    /// An outline to use as the denominator, or null. It must already have been checked readable
    /// and exactly placeable for this caller — this statement does not filter it, because a second
    /// access fragment cannot share the first one's parameter set.
    /// </param>
    public static async Task<IReadOnlyList<DensityCellRow>> CellsAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        double west,
        double south,
        double east,
        double north,
        double cellDegrees,
        double protectionGridMeters,
        Guid? studyAreaFeatureId,
        CancellationToken ct)
    {
        var (sql, parameters) = BuildCells(
            ctx, west, south, east, north, cellDegrees, protectionGridMeters, studyAreaFeatureId);
        var rows = await db.Database.GetDbConnection().QueryAsync<DensityCellRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        return [.. rows];
    }

    /// <summary>
    /// The statement and its parameters, exposed so the access path and the snapping can be pinned
    /// by a test rather than assumed.
    /// </summary>
    public static (string Sql, DynamicParameters Parameters) BuildCells(
        AccessContext ctx,
        double west,
        double south,
        double east,
        double north,
        double cellDegrees,
        double protectionGridMeters,
        Guid? studyAreaFeatureId)
    {
        var (firstX, lastX) = DensityGrid.CellRange(west, east, cellDegrees);
        var (firstY, lastY) = DensityGrid.CellRange(south, north, cellDegrees);

        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("dg_west", west);
        parameters.Add("dg_south", south);
        parameters.Add("dg_east", east);
        parameters.Add("dg_north", north);
        parameters.Add("dg_cell", cellDegrees);
        parameters.Add("dg_protection_cell", LocationProtection.CellDegrees(protectionGridMeters));
        parameters.Add("dg_first_x", firstX);
        parameters.Add("dg_last_x", lastX);
        parameters.Add("dg_first_y", firstY);
        parameters.Add("dg_last_y", lastY);
        // Typed explicitly: null here means "no study area", and an untyped null leaves the driver
        // nothing to infer the parameter's type from.
        parameters.Add("dg_area_id", studyAreaFeatureId, DbType.Guid);

        var sql = $"""
            WITH grid AS (
                SELECT cx, cy,
                       ST_MakeEnvelope((cx - 0.5) * @dg_cell, (cy - 0.5) * @dg_cell,
                                       (cx + 0.5) * @dg_cell, (cy + 0.5) * @dg_cell, 4326) AS env
                FROM generate_series(@dg_first_x, @dg_last_x) AS cx,
                     generate_series(@dg_first_y, @dg_last_y) AS cy
            ),
            study AS (
                SELECT a.geom
                FROM features a
                WHERE @dg_area_id IS NOT NULL
                  AND a.id = @dg_area_id
                  AND a.deleted_at IS NULL
                  AND a.geom IS NOT NULL
            ),
            located AS (
                SELECT
                    CASE WHEN {exactSql}
                        THEN ST_X(f.geom)
                        ELSE round((ST_X(f.geom) / @dg_protection_cell)::numeric)::float8
                             * @dg_protection_cell
                    END AS gx,
                    CASE WHEN {exactSql}
                        THEN ST_Y(f.geom)
                        ELSE round((ST_Y(f.geom) / @dg_protection_cell)::numeric)::float8
                             * @dg_protection_cell
                    END AS gy
                FROM features f
                WHERE f.kind = {(short)FeatureKind.CaveEntrance}
                  AND f.deleted_at IS NULL
                  AND f.geom IS NOT NULL
                  AND f.geom && ST_Expand(
                        ST_MakeEnvelope((@dg_first_x - 0.5) * @dg_cell, (@dg_first_y - 0.5) * @dg_cell,
                                        (@dg_last_x + 0.5) * @dg_cell, (@dg_last_y + 0.5) * @dg_cell,
                                        4326),
                        @dg_protection_cell)
                  AND (@dg_area_id IS NULL OR EXISTS (
                        SELECT 1 FROM study a WHERE ST_Intersects(f.geom, a.geom)))
                  AND {visibleSql}
            ),
            counted AS (
                SELECT round((gx / @dg_cell)::numeric)::bigint AS cx,
                       round((gy / @dg_cell)::numeric)::bigint AS cy,
                       COUNT(*)::int AS n
                FROM located
                GROUP BY 1, 2
            )
            SELECT g.cx AS "CellX",
                   g.cy AS "CellY",
                   ST_XMin(g.env) AS "West",
                   ST_YMin(g.env) AS "South",
                   ST_XMax(g.env) AS "East",
                   ST_YMax(g.env) AS "North",
                   COALESCE(c.n, 0) AS "Count",
                   ST_Area(g.env::geography) AS "CellAreaM2",
                   CASE WHEN s.geom IS NULL THEN NULL
                        ELSE ST_Area(ST_Intersection(g.env, s.geom)::geography)
                   END AS "StudyAreaM2"
            FROM grid g
            LEFT JOIN counted c ON c.cx = g.cx AND c.cy = g.cy
            LEFT JOIN study s ON TRUE
            WHERE @dg_area_id IS NULL
               OR (s.geom IS NOT NULL AND ST_Intersects(g.env, s.geom))
            ORDER BY g.cy, g.cx
            """;

        return (sql, parameters);
    }
}
