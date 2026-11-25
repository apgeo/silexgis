// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Dapper;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Import;

/// <summary>
/// The proximity pool's one statement: for each candidate, the point rows of some kinds within its
/// reach that a caller may both read and place, nearest that candidate first, cut at a ceiling per
/// candidate.
/// </summary>
/// <remarks>
/// <para>
/// <b>Decided in the statement, before the order and the cut.</b> Who may be in the pool has to be
/// settled before it is ordered and cut — a row the caller may read but not place, left in until
/// after the cut, takes a place that a placeable row then loses, and which row loses it moves as
/// the hidden one's distance from the candidate does. Deciding it in the database rather than by
/// first fetching every protected readable id in reach and sending them back as a list keeps the
/// cost to one indexed read, however many protected rows a reach holds.
/// </para>
/// <para>
/// <b>Ranked per candidate, not around the page.</b> A page of candidates can lie at both ends of a
/// county. Ranked by distance from the middle of the page and cut once, the pool fills with whatever
/// lies between them, and the entrance a metre from the candidate at one end is the row cut — the
/// duplicate the search exists to find, reported as nothing. So every candidate takes its own
/// nearest rows from the geometry index, inside its own reach, and the pool is their union.
/// </para>
/// <para>
/// The two decisions are the caller's own access fragments — the same visibility walk and the same
/// exact-view rule every map statement splices in — so this cannot come to disagree with them.
/// </para>
/// </remarks>
public static class ProximitySql
{
    /// <summary>
    /// Ids of the readable and placeable rows nearest each candidate, the union over candidates,
    /// nearest to any candidate first.
    /// </summary>
    public static async Task<List<Guid>> NearestPlaceableIdsAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        IReadOnlyCollection<Coordinate> candidates,
        double radiusMeters,
        IReadOnlyCollection<FeatureKind> kinds,
        int perCandidate,
        CancellationToken ct)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var (sql, parameters) = Build(ctx, candidates, radiusMeters, kinds, perCandidate);
        var ids = await db.Database.GetDbConnection().QueryAsync<Guid>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return [.. ids];
    }

    /// <summary>The statement and its parameters, exposed so a test can pin its plan.</summary>
    public static (string Sql, DynamicParameters Parameters) Build(
        AccessContext ctx,
        IReadOnlyCollection<Coordinate> candidates,
        double radiusMeters,
        IReadOnlyCollection<FeatureKind> kinds,
        int perCandidate)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var (visibleSql, exactSql, parameters) = FeatureAudienceSql.Caller(ctx, "f");

        // One margin for every candidate, taken at the page's most poleward latitude, where a
        // metre is the most degrees of longitude: never smaller than any candidate's own reach.
        var envelope = new Envelope();
        foreach (var candidate in candidates)
        {
            envelope.ExpandToInclude(candidate);
        }

        var reach = Geodesy.ExpandedBy(envelope, radiusMeters);
        var factory = new GeometryFactory(new PrecisionModel(), 4326);
        var points = factory.CreateMultiPointFromCoords([.. candidates.Select(c => new Coordinate(c.X, c.Y))]);

        parameters.Add("px_points", new WKBWriter(ByteOrder.LittleEndian, false, false, false).Write(points));
        parameters.Add("px_dlon", envelope.IsNull ? 0 : envelope.MinX - reach.MinX);
        parameters.Add("px_dlat", envelope.IsNull ? 0 : envelope.MinY - reach.MinY);
        parameters.Add("px_per", perCandidate);

        // Enum constants, never caller text, written in so the planner sees literal kinds.
        var kindSql = kinds.Count == 0
            ? "false"
            : $"f.kind IN ({string.Join(", ", kinds.Select(k => ((short)k).ToString(CultureInfo.InvariantCulture)))})";

        // Ordered in degrees, as the pool always was: over a reach this small the two orders agree
        // on which rows are nearest, and the true metric distance is the caller's to compute.
        //
        // Points only, decided before the cut: a candidate is only ever compared with a place
        // that is a point, so an area or a line the order put first would take a place in the
        // pool and be thrown away after it, and the point it pushed past the ceiling — perhaps
        // the near duplicate the search exists to find — would go unreported.
        var sql = $"""
            WITH candidate AS (
                SELECT DISTINCT d.geom AS p
                FROM ST_Dump(ST_SetSRID(ST_GeomFromWKB(@px_points), 4326)) d
            )
            SELECT n.id
            FROM candidate c
            CROSS JOIN LATERAL (
                SELECT f.id, f.geom <-> c.p AS d
                FROM features f
                WHERE f.deleted_at IS NULL
                  AND f.geom IS NOT NULL
                  AND GeometryType(f.geom) = 'POINT'
                  AND {kindSql}
                  AND f.geom && ST_Expand(c.p, @px_dlon, @px_dlat)
                  AND {visibleSql}
                  AND (NOT f.is_protected_effective OR {exactSql})
                ORDER BY f.geom <-> c.p, f.id
                LIMIT @px_per
            ) n
            GROUP BY n.id
            ORDER BY min(n.d), n.id
            """;
        return (sql, parameters);
    }
}
