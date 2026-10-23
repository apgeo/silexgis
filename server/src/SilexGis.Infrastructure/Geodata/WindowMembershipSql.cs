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
    public static Task<List<Guid>> ProtectedMembersAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        double west,
        double south,
        double east,
        double north,
        double protectionGridMeters,
        IReadOnlyCollection<FeatureKind>? kinds,
        CancellationToken ct) =>
        ProtectedMembersAsync(
            db, FeatureAudienceSql.Caller(ctx, "f"), west, south, east, north, protectionGridMeters, kinds, ct);

    /// <summary>The same, for an audience other than a signed-in caller — a shared view's link.</summary>
    public static async Task<List<Guid>> ProtectedMembersAsync(
        SilexGisDbContext db,
        FeatureAudience audience,
        double west,
        double south,
        double east,
        double north,
        double protectionGridMeters,
        IReadOnlyCollection<FeatureKind>? kinds,
        CancellationToken ct)
    {
        var (sql, parameters) = Build(audience, west, south, east, north, protectionGridMeters, kinds);
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
        IReadOnlyCollection<FeatureKind>? kinds) =>
        Build(FeatureAudienceSql.Caller(ctx, "f"), west, south, east, north, protectionGridMeters, kinds);

    /// <summary>The statement for an audience over the alias <c>f</c>.</summary>
    public static (string Sql, DynamicParameters Parameters) Build(
        FeatureAudience audience,
        double west,
        double south,
        double east,
        double north,
        double protectionGridMeters,
        IReadOnlyCollection<FeatureKind>? kinds)
    {
        var (visibleSql, exactSql, parameters) = (audience.VisibleSql, audience.ExactSql, audience.Parameters);
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

        var membership = ShownPositionTest(
            "f",
            exactSql,
            "wm_cell",
            g => $"ST_Intersects({g}, ST_MakeEnvelope(@wm_west, @wm_south, @wm_east, @wm_north, 4326))");
        var sql = $"""
            SELECT f.id
            FROM features f
            WHERE f.deleted_at IS NULL
              AND f.is_protected_effective
              AND f.geom IS NOT NULL{kindSql}
              AND f.geom && ST_Expand(
                    ST_MakeEnvelope(@wm_west, @wm_south, @wm_east, @wm_north, 4326), @wm_cell)
              AND {visibleSql}
              AND {membership}
            """;
        return (sql, parameters);
    }

    /// <summary>
    /// Ids of the caves that have at least one entrance the caller may read inside the window by the
    /// rule above — every entrance, protected or not, tested where the caller is shown it.
    /// </summary>
    /// <remarks>
    /// One statement over the entrances, served by their GIST index on the window widened by one
    /// protection cell, rather than a look at the entrances per candidate cave: correlated per cave,
    /// a windowed cave list paid the visibility walk for every cave of the register, which on 100k
    /// entrances was over two seconds for a reader who owns none of them. The answer is one id per
    /// cave, so its length is bounded by the caves in the window, not by their entrances.
    /// <para>
    /// An unprotected cave whose own point is inside the window is in it already — the caller's
    /// query tests that point itself — so its entrances are passed over before the visibility walk,
    /// which is what keeps a country-wide window cheap. Only an unprotected cave is passed over: for
    /// a protected one the stored point is not the shown one, and skipping its entrances on it would
    /// let the stored point decide.
    /// </para>
    /// </remarks>
    public static async Task<List<Guid>> CavesWithShownEntrancesAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        double west,
        double south,
        double east,
        double north,
        double protectionGridMeters,
        CancellationToken ct)
    {
        var (sql, parameters) = BuildCavesWithShownEntrances(ctx, west, south, east, north, protectionGridMeters);
        var ids = await db.Database.GetDbConnection().QueryAsync<Guid>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return [.. ids];
    }

    /// <summary>The statement and its parameters, exposed so a test can pin its shape.</summary>
    public static (string Sql, DynamicParameters Parameters) BuildCavesWithShownEntrances(
        AccessContext ctx,
        double west,
        double south,
        double east,
        double north,
        double protectionGridMeters)
    {
        var (visibleSql, exactSql, parameters) = FeatureAudienceSql.Caller(ctx, "f");
        parameters.Add("wm_west", west);
        parameters.Add("wm_south", south);
        parameters.Add("wm_east", east);
        parameters.Add("wm_north", north);
        parameters.Add("wm_cell", LocationProtection.CellDegrees(protectionGridMeters));
        var membership = ShownPositionTest(
            "f",
            exactSql,
            "wm_cell",
            g => $"ST_Intersects({g}, ST_MakeEnvelope(@wm_west, @wm_south, @wm_east, @wm_north, 4326))");
        var entranceKind = ((short)FeatureKind.CaveEntrance).ToString(CultureInfo.InvariantCulture);
        var sql = $"""
            SELECT DISTINCT ce.cave_feature_id
            FROM features f
            JOIN cave_entrances ce ON ce.id = f.id
            JOIN features cave ON cave.id = ce.cave_feature_id
            WHERE f.deleted_at IS NULL
              AND f.kind = {entranceKind}
              AND NOT (NOT cave.is_protected_effective
                       AND cave.geom IS NOT NULL
                       AND ST_Intersects(cave.geom, ST_MakeEnvelope(@wm_west, @wm_south, @wm_east, @wm_north, 4326)))
              AND f.geom IS NOT NULL
              AND f.geom && ST_Expand(
                    ST_MakeEnvelope(@wm_west, @wm_south, @wm_east, @wm_north, 4326), @wm_cell)
              AND {visibleSql}
              AND {membership}
            """;
        return (sql, parameters);
    }

    /// <summary>
    /// The rule above as one SQL expression over a feature row, for any spatial test — inside a
    /// window, within a distance, inside an outline. The single statement of it: every caller that
    /// asks a spatial question of feature rows splices this in rather than writing its own.
    /// </summary>
    /// <param name="alias">The feature row's alias.</param>
    /// <param name="exactSql">The caller's exact-view fragment over that alias.</param>
    /// <param name="cellParameter">
    /// The parameter carrying one protection cell in degrees
    /// (<see cref="LocationProtection.CellDegrees"/>), without the marker.
    /// </param>
    /// <param name="test">
    /// The test, given an expression for the geometry to test: the stored geometry for a row the
    /// caller may place exactly, the snapped point for a protected point they may not. A protected
    /// line or polygon they may not place is never handed to it — it answers false.
    /// </param>
    public static string ShownPositionTest(
        string alias, string exactSql, string cellParameter, Func<string, string> test)
    {
        ArgumentNullException.ThrowIfNull(test);
        var snappedX = SpatialSql.SnapToGrid($"ST_X({alias}.geom)", cellParameter);
        var snappedY = SpatialSql.SnapToGrid($"ST_Y({alias}.geom)", cellParameter);
        return $"""
            CASE
                WHEN {exactSql}
                    THEN {test($"{alias}.geom")}
                WHEN ST_GeometryType({alias}.geom) = 'ST_Point'
                    THEN {test($"ST_SetSRID(ST_MakePoint({snappedX}, {snappedY}), 4326)")}
                ELSE false
            END
            """;
    }
}
