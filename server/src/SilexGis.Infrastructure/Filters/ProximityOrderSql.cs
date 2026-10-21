// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Npgsql;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Filters;

/// <summary>One feature row's distance from a proximity anchor, in metres.</summary>
/// <remarks>
/// Mutable with a parameterless constructor because EF materialises it from raw SQL, reading its
/// columns by the snake-case names the model's naming convention gives these properties.
/// </remarks>
public sealed class ProximityRank
{
    public Guid Id { get; set; }

    public double Distance { get; set; }
}

/// <summary>
/// The distance of every feature row a caller may place exactly from an anchor — the ranking the
/// filter's proximity sort orders by.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only exactly placed rows are ranked.</b> A row the caller may not place exactly is absent
/// from the answer, not given a distance to its snapped point: a distance to a lattice point is
/// still a measurement, and the same rows ranked from two anchors would triangulate the snapping
/// away. The sort that reads this puts every absent row after the ranked ones, in its fallback
/// order, so how far an unplaceable row is from anything never moves it.
/// </para>
/// <para>
/// <b>A cave is ranked where every surface puts it.</b> Its row carries a representative point —
/// its main entrance's, kept in step by the write service — and it is ranked by that point, under
/// the cave's own exact-view decision. Ranking it by the nearest of all its entrances instead would
/// mean deciding read and exact view for every entrance in the installation on every request, to
/// refine an order by less than a cave's own extent.
/// </para>
/// <para>
/// <b>Composed, not run.</b> The answer is a query the sort joins against the caller's own
/// filtered rows, so the visibility walk, the filter and the ranking are one statement and the
/// page is cut after all three. A row's visibility is not re-decided here: only rows the walk
/// already admitted are ever joined to it. Kept a plain select — no grouping, no union — so the
/// planner can fold it into that join and rank only the rows the filter kept, rather than
/// every row in the installation.
/// </para>
/// <para>
/// <b>Exact view is decided once.</b> The test is the exact-view fragment the map layers splice,
/// short-circuited on the stored protection flag the way the batch evaluator's fast path is, so
/// the two cannot disagree about which rows a caller may place.
/// </para>
/// </remarks>
public static class ProximityOrderSql
{
    /// <summary>The ranked rows, as a query to compose with.</summary>
    public static IQueryable<ProximityRank> Ranks(
        SilexGisDbContext db, AccessContext ctx, Geometry anchor, double? withinMeters = null)
    {
        var (sql, parameters) = Build(ctx, anchor, withinMeters);
        return db.Database.SqlQueryRaw<ProximityRank>(sql, ToDbParameters(parameters));
    }

    /// <summary>
    /// How far the <paramref name="count"/>-th nearest row this caller may read and place exactly
    /// lies from the anchor, in metres — or null when there are fewer such rows than that.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A bound for a page, not a ranking: it is taken along the geometry index's nearest-first
    /// walk, which orders by planar distance in degrees, and only the largest true distance among
    /// the rows that walk finds is kept. Whatever those rows are, <paramref name="count"/> readable,
    /// placeable rows lie within it, so the first <paramref name="count"/> of the true ranking lie
    /// within it as well. The page is then ranked among the rows inside that reach only (see
    /// <see cref="Ranks"/>), which the geometry index finds without measuring every other row.
    /// </para>
    /// <para>
    /// Every row the walk keeps is one the caller may read and place exactly, so the reach is
    /// decided only on positions this caller is shown as they are. A protected row they may not
    /// place is passed over by the walk without a distance being taken, as the ranking itself does.
    /// </para>
    /// </remarks>
    public static async Task<double?> ReachOfNearestAsync(
        SilexGisDbContext db, AccessContext ctx, Geometry anchor, int count, CancellationToken ct)
    {
        var (sql, parameters) = BuildReach(ctx, anchor, count);
        var (found, reach) = await db.Database.GetDbConnection().QuerySingleAsync<(int, double?)>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return found < count ? null : reach;
    }

    /// <summary>The reach statement and its parameters, exposed so a test can pin its shape.</summary>
    public static (string Sql, DynamicParameters Parameters) BuildReach(AccessContext ctx, Geometry anchor, int count)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("px_anchor", Wkb(anchor));
        parameters.Add("px_count", count);

        var sql = $"""
            SELECT count(*)::int AS found,
                   max(ST_Distance(n.geom::geography, ST_SetSRID(ST_GeomFromWKB(@px_anchor), 4326)::geography)) AS reach
            FROM (SELECT f.geom
                  FROM features f
                  WHERE f.deleted_at IS NULL
                    AND f.geom IS NOT NULL
                    AND {visibleSql}
                    AND {exactSql}
                  ORDER BY f.geom <-> ST_SetSRID(ST_GeomFromWKB(@px_anchor), 4326)
                  LIMIT @px_count) n
            """;
        return (sql, parameters);
    }

    /// <summary>The statement and its parameters, exposed so a test can pin its shape.</summary>
    public static (string Sql, DynamicParameters Parameters) Build(
        AccessContext ctx, Geometry anchor, double? withinMeters = null)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        var (exactSql, parameters) = AccessSql.ExactViewFragment(ctx, "f");
        parameters.Add("px_anchor", Wkb(anchor));

        // Inside a reach: the rows no farther than it, found through the geometry index by a box
        // that is never smaller than the reach, and measured by the same expression the ranking
        // orders by, so a row the reach was taken from is never lost to a rounding difference.
        var within = "";
        if (withinMeters is { } meters)
        {
            var box = SpatialReach.Around(anchor.EnvelopeInternal, meters);
            parameters.Add("px_west", box.MinX);
            parameters.Add("px_south", box.MinY);
            parameters.Add("px_east", box.MaxX);
            parameters.Add("px_north", box.MaxY);
            parameters.Add("px_reach", meters);
            within = """

                  AND f.geom && ST_MakeEnvelope(@px_west, @px_south, @px_east, @px_north, 4326)
                  AND ST_Distance(f.geom::geography, ST_SetSRID(ST_GeomFromWKB(@px_anchor), 4326)::geography) <= @px_reach
                """;
        }

        var sql = $"""
            SELECT f.id AS id,
                   ST_Distance(f.geom::geography, ST_SetSRID(ST_GeomFromWKB(@px_anchor), 4326)::geography) AS distance
            FROM features f
            WHERE f.deleted_at IS NULL
              AND f.geom IS NOT NULL
              AND (NOT f.is_protected_effective OR {exactSql}){within}
            """;
        return (sql, parameters);
    }

    private static byte[] Wkb(Geometry anchor) =>
        new WKBWriter(ByteOrder.LittleEndian, false, false, false).Write(anchor);

    /// <summary>
    /// The access fragments' parameters as provider parameters, so EF can run a statement built
    /// for Dapper. An array the fragments pass as a custom parameter is asked to write itself, which
    /// keeps its native array type rather than one placeholder per element.
    /// </summary>
    private static object[] ToDbParameters(DynamicParameters parameters)
    {
        using var command = new NpgsqlCommand();
        foreach (var name in parameters.ParameterNames)
        {
            var value = parameters.Get<object?>(name);
            if (value is SqlMapper.ICustomQueryParameter custom)
            {
                custom.AddParameter(command, name);
            }
            else
            {
                command.Parameters.Add(new NpgsqlParameter(name, value ?? DBNull.Value));
            }
        }

        object[] converted = [.. command.Parameters];
        // Detached, so the statement EF builds may adopt them.
        command.Parameters.Clear();
        return converted;
    }
}
