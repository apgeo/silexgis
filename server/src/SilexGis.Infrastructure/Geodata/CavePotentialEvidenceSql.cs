// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>One recorded feature read as evidence for a cave-potential run.</summary>
/// <param name="Wkb">The feature's geometry in two dimensions, as well-known binary.</param>
/// <param name="IsProtected">Whether the feature's location is protected, as the row says now.</param>
/// <param name="Lithology">The stored lithology code of a geological unit; null for anything else.</param>
public sealed record CavePotentialEvidenceRow(Guid Id, byte[] Wkb, bool IsProtected, string? Lithology);

/// <summary>Which recorded features an evidence layer is made from.</summary>
public enum CavePotentialEvidenceSource
{
    /// <summary>Fault traces, and structural readings taken on a fault plane.</summary>
    Faults,

    /// <summary>Sinkholes and dolines, as points or as outlines.</summary>
    Sinkholes,

    /// <summary>Cave entrances.</summary>
    Entrances,

    /// <summary>Geological unit outlines.</summary>
    GeologicalUnits,
}

/// <summary>
/// Reads the recorded features a cave-potential run turns into evidence surfaces.
/// </summary>
/// <remarks>
/// <para>
/// <b>Protection: withheld, never snapped.</b> A row is read only when the account the run is for
/// may both read it and place it exactly. A surface of distances has its zero on the feature and a
/// density has its peak there, so a snapped position would simply be drawn in the wrong place with
/// full confidence; a feature the runner may not place is left out before anything is computed, and
/// the surface is then exactly what that account could have worked out from its own map.
/// </para>
/// <para>
/// <b>No area in the statement.</b> The window is a plain box. The area a run is over has already
/// been checked readable and exactly placeable by whoever built the grid, and a second access
/// fragment could not share the first one's parameters. Features are found by where they are and
/// not by what they are filed under, because a fault just outside an area is as near to its edge
/// as one inside it.
/// </para>
/// </remarks>
public static class CavePotentialEvidenceSql
{
    public static async Task<IReadOnlyList<CavePotentialEvidenceRow>> FeaturesAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        CavePotentialEvidenceSource source,
        IReadOnlyList<long> featureTypeIds,
        double west,
        double south,
        double east,
        double north,
        int limit,
        CancellationToken ct)
    {
        var (sql, parameters) = Build(ctx, source, featureTypeIds, west, south, east, north, limit);
        var rows = await db.Database.GetDbConnection().QueryAsync<CavePotentialEvidenceRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        return [.. rows];
    }

    /// <summary>
    /// The statement and its parameters, exposed so the access path — visible <i>and</i> exactly
    /// placeable — can be pinned by a test rather than assumed.
    /// </summary>
    /// <param name="featureTypeIds">
    /// The kinds the source reads, in the source's own order: the fault-line kind then the
    /// structural-reading kind for faults, the one kind for sinkholes and for geological units,
    /// and none for entrances.
    /// </param>
    public static (string Sql, DynamicParameters Parameters) Build(
        AccessContext ctx,
        CavePotentialEvidenceSource source,
        IReadOnlyList<long> featureTypeIds,
        double west,
        double south,
        double east,
        double north,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(featureTypeIds);
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("pe_west", west);
        parameters.Add("pe_south", south);
        parameters.Add("pe_east", east);
        parameters.Add("pe_north", north);
        parameters.Add("pe_limit", limit);
        parameters.Add("pe_type_a", featureTypeIds.Count > 0 ? featureTypeIds[0] : -1L);
        parameters.Add("pe_type_b", featureTypeIds.Count > 1 ? featureTypeIds[1] : -1L);
        parameters.Add("pe_fault", StructuralMeasurement.FaultType);

        var which = source switch
        {
            CavePotentialEvidenceSource.Faults => $"""
                ((f.feature_type_id = @pe_type_a AND ST_Dimension(f.geom) = 1)
                 OR (f.feature_type_id = @pe_type_b AND ST_Dimension(f.geom) = 0
                     AND f.properties ->> '{StructuralMeasurement.TypeKey}' = @pe_fault))
                """,
            CavePotentialEvidenceSource.Sinkholes =>
                $"f.kind = {(short)FeatureKind.Generic} AND f.feature_type_id = @pe_type_a",
            CavePotentialEvidenceSource.Entrances =>
                $"f.kind = {(short)FeatureKind.CaveEntrance} AND ST_Dimension(f.geom) = 0",
            CavePotentialEvidenceSource.GeologicalUnits =>
                $"f.kind = {(short)FeatureKind.Generic} AND f.feature_type_id = @pe_type_a AND ST_Dimension(f.geom) = 2",
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };

        var lithology = source == CavePotentialEvidenceSource.GeologicalUnits
            ? $"NULLIF(f.properties ->> '{Lithology.PropertyKey}', '')"
            : "NULL::text";

        var sql = $"""
            SELECT f.id AS "Id",
                   ST_AsBinary(ST_Force2D(f.geom)) AS "Wkb",
                   f.is_protected_effective AS "IsProtected",
                   {lithology} AS "Lithology"
            FROM features f
            WHERE f.deleted_at IS NULL
              AND f.geom IS NOT NULL
              AND f.geom && ST_MakeEnvelope(@pe_west, @pe_south, @pe_east, @pe_north, 4326)
              AND {which}
              AND {visibleSql}
              AND {exactSql}
            ORDER BY f.id
            LIMIT @pe_limit
            """;

        return (sql, parameters);
    }
}
