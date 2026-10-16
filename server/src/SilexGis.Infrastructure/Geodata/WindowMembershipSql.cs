// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Dapper;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// Which location-protected rows belong to a caller's window, decided on the position that caller
/// is shown rather than on the stored one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the stored position cannot decide.</b> A route that takes a bbox and answers with a list,
/// a count, a total or a page decides, row by row, whether the row is inside. Asked of the stored
/// geometry, that answer is a probe even when no coordinate is returned: move one edge of the
/// window in small steps and the row appears or disappears exactly when the edge crosses its true
/// position, so a bisection recovers the position far below the protection grid. The snapped point
/// handed out elsewhere would then protect nothing.
/// </para>
/// <para>
/// <b>The rule.</b> A protected row the caller may place exactly is in the window when its stored
/// geometry intersects it — the same as an unprotected row. A protected point the caller may NOT
/// place exactly is in the window when its snapped point is — the point the protection rule hands
/// out, by the same arithmetic. A protected line or polygon the caller may not place exactly is in
/// no window at all: its geometry is withheld, there is no shown position to test, and any test of
/// the stored one is the probe above. Every answer this produces changes only when an edge crosses
/// a lattice point, which says nothing finer than the lattice.
/// </para>
/// <para>
/// <b>Index use.</b> The stored geometry is still what GIST can serve, so the statement prefilters
/// on it with the window widened by one protection cell. A snap moves a coordinate by at most half
/// a cell on each axis, in the same degrees on both, so a row whose snapped point is inside the
/// window always passes the widened prefilter.
/// </para>
/// <para>
/// Unprotected rows are not this statement's business: the caller's own query keeps its plain
/// window test for them, and adds <c>id = ANY(these)</c> for the protected ones. Only protected
/// rows readable by the caller are returned, so the list stays as short as the window's protected
/// population.
/// </para>
/// </remarks>
public static class WindowMembershipSql
{
    /// <summary>
    /// Ids of the location-protected, readable rows of <paramref name="kinds"/> that are inside the
    /// window by the rule above.
    /// </summary>
    /// <param name="kinds">Feature kinds to consider, or null for every kind.</param>
    public static async Task<List<Guid>> ProtectedMembersAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        double west,
        double south,
        double east,
        double north,
        double protectionGridMeters,
        IReadOnlyCollection<FeatureKind>? kinds,
        CancellationToken ct)
    {
        var (sql, parameters) = Build(ctx, west, south, east, north, protectionGridMeters, kinds);
        var ids = await db.Database.GetDbConnection().QueryAsync<Guid>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return [.. ids];
    }

    /// <summary>The statement and its parameters, exposed so a test can pin its shape.</summary>
    public static (string Sql, DynamicParameters Parameters) Build(
        AccessContext ctx,
        double west,
        double south,
        double east,
        double north,
        double protectionGridMeters,
        IReadOnlyCollection<FeatureKind>? kinds)
    {
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("wm_west", west);
        parameters.Add("wm_south", south);
        parameters.Add("wm_east", east);
        parameters.Add("wm_north", north);
        parameters.Add("wm_cell", LocationProtection.CellDegrees(protectionGridMeters));

        // The kinds are enum constants, never caller text, and are written into the statement
        // rather than passed as an array: a literal "kind = 2" is what lets the planner use the
        // entrance layer's partial GIST index, which an array parameter would hide from it.
        var kindSql = kinds is null
            ? string.Empty
            : kinds.Count == 0
            ? "\n  AND false"
            : $"\n  AND f.kind IN ({string.Join(", ", kinds.Select(k => ((short)k).ToString(CultureInfo.InvariantCulture)))})";

        var snappedX = SpatialSql.SnapToGrid("ST_X(f.geom)", "wm_cell");
        var snappedY = SpatialSql.SnapToGrid("ST_Y(f.geom)", "wm_cell");
        var sql = $"""
            SELECT f.id
            FROM features f
            WHERE f.deleted_at IS NULL
              AND f.is_protected_effective
              AND f.geom IS NOT NULL{kindSql}
              AND f.geom && ST_Expand(
                    ST_MakeEnvelope(@wm_west, @wm_south, @wm_east, @wm_north, 4326), @wm_cell)
              AND {visibleSql}
              AND CASE
                    WHEN {exactSql}
                        THEN ST_Intersects(
                            f.geom, ST_MakeEnvelope(@wm_west, @wm_south, @wm_east, @wm_north, 4326))
                    WHEN ST_GeometryType(f.geom) = 'ST_Point'
                        THEN {snappedX} BETWEEN @wm_west AND @wm_east
                         AND {snappedY} BETWEEN @wm_south AND @wm_north
                    ELSE false
                  END
            """;
        return (sql, parameters);
    }
}
