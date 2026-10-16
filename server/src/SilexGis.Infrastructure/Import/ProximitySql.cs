// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Dapper;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Import;

/// <summary>
/// The proximity pool's one statement: the rows of some kinds inside a search envelope that a
/// caller may both read and place, nearest a centre first, cut at a ceiling.
/// </summary>
/// <remarks>
/// <para>
/// <b>Decided in the statement, before the order and the cut.</b> Who may be in the pool has to be
/// settled before it is ordered and cut — a row the caller may read but not place, left in until
/// after the cut, takes a place that a placeable row then loses, and which row loses it moves as
/// the hidden one's distance from the centre does. Deciding it in the database rather than by
/// first fetching every protected readable id in reach and sending them back as a list keeps the
/// cost to one indexed read, however many protected rows a reach holds.
/// </para>
/// <para>
/// The two decisions are the caller's own access fragments — the same visibility walk and the same
/// exact-view rule every map statement splices in — so this cannot come to disagree with them.
/// </para>
/// </remarks>
public static class ProximitySql
{
    /// <summary>Ids of the nearest readable and placeable rows, nearest first.</summary>
    public static async Task<List<Guid>> NearestPlaceableIdsAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        Envelope search,
        Coordinate centre,
        IReadOnlyCollection<FeatureKind> kinds,
        int limit,
        CancellationToken ct)
    {
        var (sql, parameters) = Build(ctx, search, centre, kinds, limit);
        var ids = await db.Database.GetDbConnection().QueryAsync<Guid>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return [.. ids];
    }

    /// <summary>The statement and its parameters, exposed so a test can pin its plan.</summary>
    public static (string Sql, DynamicParameters Parameters) Build(
        AccessContext ctx,
        Envelope search,
        Coordinate centre,
        IReadOnlyCollection<FeatureKind> kinds,
        int limit)
    {
        var (visibleSql, exactSql, parameters) = FeatureAudienceSql.Caller(ctx, "f");
        parameters.Add("px_west", search.MinX);
        parameters.Add("px_south", search.MinY);
        parameters.Add("px_east", search.MaxX);
        parameters.Add("px_north", search.MaxY);
        parameters.Add("px_cx", centre.X);
        parameters.Add("px_cy", centre.Y);
        parameters.Add("px_limit", limit);

        // Enum constants, never caller text, written in so the planner sees literal kinds.
        var kindSql = kinds.Count == 0
            ? "false"
            : $"f.kind IN ({string.Join(", ", kinds.Select(k => ((short)k).ToString(CultureInfo.InvariantCulture)))})";

        // Ordered in degrees, as the pool always was: over a reach this small the two orders agree
        // on which rows are nearest, and the true metric distance is the caller's to compute.
        var sql = $"""
            SELECT f.id
            FROM features f
            WHERE f.deleted_at IS NULL
              AND f.geom IS NOT NULL
              AND {kindSql}
              AND ST_Intersects(f.geom, ST_MakeEnvelope(@px_west, @px_south, @px_east, @px_north, 4326))
              AND {visibleSql}
              AND (NOT f.is_protected_effective OR {exactSql})
            ORDER BY ST_Distance(f.geom, ST_SetSRID(ST_MakePoint(@px_cx, @px_cy), 4326)), f.id
            LIMIT @px_limit
            """;
        return (sql, parameters);
    }
}
