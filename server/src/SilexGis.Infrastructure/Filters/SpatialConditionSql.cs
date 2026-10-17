// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SilexGis.Domain.Access;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Filters;

/// <summary>A spatial question about feature rows, with its anchor already resolved and gated.</summary>
public abstract record SpatialAsk
{
    /// <summary>The degrees box every answer lies in, before the one-cell widening for snapped points.</summary>
    public abstract Envelope Reach { get; }
}

/// <summary>Within a distance of one geometry — a point sent with the request, or an object's own.</summary>
public sealed record WithinGeometry(Geometry Anchor, double RadiusMeters) : SpatialAsk
{
    public override Envelope Reach => SpatialReach.Around(Anchor.EnvelopeInternal, RadiusMeters);
}

/// <summary>
/// Within a distance of any of a set of feature rows — another filter's result, or a cave's
/// entrances. Every id here has already been proved readable and exactly placeable by the caller.
/// </summary>
public sealed record WithinRows(IReadOnlyCollection<Guid> AnchorIds, Envelope AnchorExtent, double RadiusMeters)
    : SpatialAsk
{
    public override Envelope Reach => SpatialReach.Around(AnchorExtent, RadiusMeters);
}

/// <summary>
/// The degrees box a radius reaches around an envelope, never smaller than what the database's
/// distance test can admit.
/// </summary>
/// <remarks>
/// <para>
/// The prefilter runs on stored geometry and the test on shown points, and the guarantee that every
/// row whose shown point answers passes the prefilter holds only while this box is no smaller than
/// the true reach: a box that fell short by more than half a protection cell would keep or drop a
/// protected point by its true position, and stepping the anchor would then bisect it.
/// </para>
/// <para>
/// So the box is an upper bound rather than an estimate. The distance is measured on the WGS84
/// spheroid, where a degree of latitude is shortest at the equator — 110 574 m, against the mean
/// sphere's 111 195 m, which at the largest radius falls a hundredth of a degree short. The
/// shortest degree is used for longitude as well, where every degree is longer still, and over
/// the cosine of the latitude the reach can climb to rather than of the envelope's own edge,
/// since the reach of a radius bulges towards the pole.
/// </para>
/// </remarks>
public static class SpatialReach
{
    private const double ShortestDegreeMeters = 110_574;

    public static Envelope Around(Envelope envelope, double meters)
    {
        var latitudeMargin = meters / ShortestDegreeMeters;
        var poleward = Math.Min(89.5, Math.Max(Math.Abs(envelope.MinY), Math.Abs(envelope.MaxY)) + latitudeMargin);
        var longitudeMargin = latitudeMargin / Math.Max(0.01, Math.Cos(poleward * Math.PI / 180));

        var grown = envelope.Copy();
        grown.ExpandBy(longitudeMargin, latitudeMargin);
        return grown;
    }
}

/// <summary>Wholly inside an outline — one sent with the request, or an area's own.</summary>
public sealed record InsideOutline(Geometry Outline) : SpatialAsk
{
    public override Envelope Reach => Outline.EnvelopeInternal;
}

/// <summary>
/// Which feature rows answer a spatial question for a caller, decided on the position that caller
/// is shown.
/// </summary>
/// <remarks>
/// <para>
/// A row the caller may place exactly is tested by its stored geometry. A protected point they may
/// not place is tested by its snapped point, the one every other surface shows them. A protected
/// line or outline they may not place answers no spatial question at all. The rule is spliced in
/// from the one place it is written down, the window-membership statement, so a filter cannot
/// measure on a position the map would not draw.
/// </para>
/// <para>
/// That is what makes the answer safe to bisect and safe to negate. Narrowing a radius or moving
/// an edge changes whether a withheld row matches only when the edge crosses its lattice point, and
/// "not within" over a withheld row is decided on the same lattice point — never on the stored
/// position, which is therefore never measured, whichever way the question is put.
/// </para>
/// <para>
/// A cave has no geometry of its own; it is where its entrances are. A cave answers when one of its
/// entrances does, by that entrance's shown position, so asking about caves cannot measure an
/// entrance more finely than asking about the entrance itself.
/// </para>
/// <para>
/// The stored geometry is what the GIST index serves, so the statement prefilters on it over the
/// reach of the question widened by one protection cell: a snap moves a coordinate by at most half
/// a cell, so a row whose shown point answers always passes the prefilter.
/// </para>
/// </remarks>
public static class SpatialConditionSql
{
    /// <summary>The ids of the rows that answer, caves included through their entrances.</summary>
    public static async Task<List<Guid>> MatchingAsync(
        SilexGisDbContext db, AccessContext ctx, SpatialAsk ask, double protectionGridMeters, CancellationToken ct)
    {
        var (sql, parameters) = Build(ctx, ask, protectionGridMeters);
        var ids = await db.Database.GetDbConnection().QueryAsync<Guid>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return [.. ids];
    }

    /// <summary>The statement and its parameters, exposed so a test can pin its shape.</summary>
    public static (string Sql, DynamicParameters Parameters) Build(
        AccessContext ctx, SpatialAsk ask, double protectionGridMeters)
    {
        ArgumentNullException.ThrowIfNull(ask);
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        var reach = ask.Reach;
        parameters.Add("sc_west", reach.MinX);
        parameters.Add("sc_south", reach.MinY);
        parameters.Add("sc_east", reach.MaxX);
        parameters.Add("sc_north", reach.MaxY);
        parameters.Add("sc_cell", LocationProtection.CellDegrees(protectionGridMeters));

        Func<string, string> test;
        switch (ask)
        {
            case WithinGeometry within:
                parameters.Add("sc_anchor", Wkb(within.Anchor));
                parameters.Add("sc_radius", within.RadiusMeters);
                test = g => $"ST_DWithin(({g})::geography, ST_SetSRID(ST_GeomFromWKB(@sc_anchor), 4326)::geography, @sc_radius)";
                break;

            case WithinRows rows:
                parameters.Add("sc_anchor_ids", rows.AnchorIds.ToArray());
                parameters.Add("sc_radius", rows.RadiusMeters);
                test = g => $"""
                    EXISTS (SELECT 1 FROM features a
                            WHERE a.id = ANY(@sc_anchor_ids)
                              AND a.geom IS NOT NULL
                              AND ST_DWithin(({g})::geography, a.geom::geography, @sc_radius))
                    """;
                break;

            case InsideOutline inside:
                parameters.Add("sc_outline", Wkb(inside.Outline));
                test = g => $"ST_CoveredBy(({g}), ST_SetSRID(ST_GeomFromWKB(@sc_outline), 4326))";
                break;

            default:
                throw new InvalidOperationException($"No statement for {ask.GetType().Name}.");
        }

        var membership = WindowMembershipSql.ShownPositionTest("f", exactSql, "sc_cell", test);
        var sql = $"""
            WITH hit AS (
                SELECT f.id
                FROM features f
                WHERE f.deleted_at IS NULL
                  AND f.geom IS NOT NULL
                  AND f.geom && ST_Expand(
                        ST_MakeEnvelope(@sc_west, @sc_south, @sc_east, @sc_north, 4326), @sc_cell)
                  AND {visibleSql}
                  AND {membership}
            )
            SELECT hit.id FROM hit
            UNION
            SELECT ce.cave_feature_id FROM cave_entrances ce JOIN hit ON hit.id = ce.id
            """;
        return (sql, parameters);
    }

    // Two-dimensional on purpose: the distance and containment tests are about where on the
    // ground, and a height carried along would only make the outline a different shape to PostGIS.
    private static byte[] Wkb(Geometry geometry) => new WKBWriter(ByteOrder.LittleEndian, false, false, false).Write(geometry);
}
