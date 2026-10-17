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
    public static IQueryable<ProximityRank> Ranks(SilexGisDbContext db, AccessContext ctx, Geometry anchor)
    {
        var (sql, parameters) = Build(ctx, anchor);
        return db.Database.SqlQueryRaw<ProximityRank>(sql, ToDbParameters(parameters));
    }

    /// <summary>The statement and its parameters, exposed so a test can pin its shape.</summary>
    public static (string Sql, DynamicParameters Parameters) Build(AccessContext ctx, Geometry anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        var (exactSql, parameters) = AccessSql.ExactViewFragment(ctx, "f");
        parameters.Add("px_anchor", new WKBWriter(ByteOrder.LittleEndian, false, false, false).Write(anchor));

        var sql = $"""
            SELECT f.id AS id,
                   ST_Distance(f.geom::geography, ST_SetSRID(ST_GeomFromWKB(@px_anchor), 4326)::geography) AS distance
            FROM features f
            WHERE f.deleted_at IS NULL
              AND f.geom IS NOT NULL
              AND (NOT f.is_protected_effective OR {exactSql})
            """;
        return (sql, parameters);
    }

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
