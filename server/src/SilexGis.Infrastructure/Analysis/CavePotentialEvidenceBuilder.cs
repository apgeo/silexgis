// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Potential;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Infrastructure.Analysis;

/// <summary>
/// Makes the evidence layers of one cave-potential run, each on the run's grid.
/// </summary>
/// <remarks>
/// <para>
/// <b>Whose reading it is.</b> Every layer made from recorded features is made from the features
/// the given account may both read and place exactly, and from no others. The account is the one
/// the run is for — resolved afresh by the caller, never taken from a stored copy of somebody's
/// rights — and each such layer reports every feature that went into it, so that what is computed
/// from the layer can later be kept from anyone who may not place all of them.
/// </para>
/// <para>
/// <b>What it does not check.</b> Whether the account may read the terrain, and whether it may
/// place the area the grid was cut over, are decided before a grid exists. Nothing here looks at
/// the area at all: a grid is a box of cells, and which of them lie inside the area's outline is
/// the caller's mask.
/// </para>
/// <para>
/// <b>Terrain layers</b> are read from the surfaces already computed for the current elevation —
/// the newest that is ready and not out of date, the same rule the area statistics read by — at
/// the nearest cell to each grid cell's centre. A surface that has not been computed is reported
/// as unavailable with its reason; it is never computed here.
/// </para>
/// </remarks>
public sealed class CavePotentialEvidenceBuilder(
    SilexGisDbContext db,
    TerrainDerivativeCatalogue catalogue,
    TerrainRasterIndex rasters,
    IDemSampleService heights,
    IDerivativeSampleService pictures)
{
    /// <summary>
    /// The most features of one kind a run reads. One more than this is asked for, so that a
    /// window holding more is refused rather than silently computed over an arbitrary part of it.
    /// </summary>
    public const int MaxFeaturesPerSource = 50_000;

    /// <summary>
    /// The layers named, in the order named. A code outside the closed list is a programming
    /// error: a model is validated against that list before it is ever stored.
    /// </summary>
    public async Task<IReadOnlyList<BuiltEvidence>> BuildAsync(
        AccessContext ctx, EvidenceGrid grid, IReadOnlyList<string> layerCodes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(layerCodes);

        var layers = layerCodes
            .Select(code => EvidenceLayers.Find(code)
                ?? throw new ArgumentException($"No evidence layer is called '{code}'.", nameof(layerCodes)))
            .ToList();

        TerrainSurfaces? terrain = null;
        var built = new List<BuiltEvidence>(layers.Count);
        foreach (var layer in layers)
        {
            ct.ThrowIfCancellationRequested();
            if (layer.Source == EvidenceSource.Terrain)
            {
                terrain ??= await TerrainSurfacesAsync(ct);
                built.Add(await TerrainLayerAsync(layer, grid, terrain, ct));
            }
            else
            {
                built.Add(await FeatureLayerAsync(ctx, layer, grid, ct));
            }
        }

        return built;
    }

    private async Task<BuiltEvidence> FeatureLayerAsync(
        AccessContext ctx, EvidenceLayer layer, EvidenceGrid grid, CancellationToken ct)
    {
        var (source, typeCodes, reachMeters) = layer.Code switch
        {
            EvidenceLayers.FaultDistance => (
                CavePotentialEvidenceSource.Faults,
                new[] { FeatureTypeSeeds.FractureLine, FeatureTypeSeeds.StructuralMeasurement },
                EvidenceSurfaces.DistanceReachMeters),
            EvidenceLayers.SinkholeDistance => (
                CavePotentialEvidenceSource.Sinkholes,
                new[] { FeatureTypeSeeds.Sinkhole },
                EvidenceSurfaces.DistanceReachMeters),
            EvidenceLayers.EntranceDensity => (
                CavePotentialEvidenceSource.Entrances,
                Array.Empty<string>(),
                EvidenceSurfaces.DensityReachMeters(grid.CellMeters)),
            EvidenceLayers.Lithology => (
                CavePotentialEvidenceSource.GeologicalUnits,
                new[] { FeatureTypeSeeds.GeologicalUnit },
                0d),
            _ => throw new ArgumentException($"'{layer.Code}' is not made from recorded features.", nameof(layer)),
        };

        var typeIds = new List<long>(typeCodes.Length);
        foreach (var code in typeCodes)
        {
            // A kind missing from the taxonomy matches nothing rather than failing the run: the
            // layer is then honestly empty of features.
            var id = await db.FeatureTypes.AsNoTracking()
                .Where(t => t.Code == code)
                .Select(t => (long?)t.Id)
                .FirstOrDefaultAsync(ct);
            typeIds.Add(id ?? -1L);
        }

        var padLatitude = reachMeters / EvidenceGrid.MetersPerDegree;
        var padLongitude = reachMeters / grid.MetersPerDegreeLongitude;
        var rows = await CavePotentialEvidenceSql.FeaturesAsync(
            db,
            ctx,
            source,
            typeIds,
            grid.West - padLongitude,
            grid.South - padLatitude,
            grid.East + padLongitude,
            grid.North + padLatitude,
            MaxFeaturesPerSource + 1,
            ct);
        if (rows.Count > MaxFeaturesPerSource)
        {
            return new BuiltEvidence(layer, null, EvidenceUnavailable.TooManyFeatures, EvidenceProvenance.None);
        }

        var reader = new WKBReader();
        var read = rows.Select(r => (Row: r, Geometry: reader.Read(r.Wkb))).ToList();
        var provenance = new EvidenceProvenance(
            [.. rows.Select(r => r.Id).Order()],
            rows.Count(r => r.IsProtected));

        var values = layer.Code switch
        {
            EvidenceLayers.EntranceDensity => EvidenceSurfaces.KernelDensityPerKm2(
                grid,
                [.. read.Select(r => r.Geometry.Coordinate)],
                EvidenceSurfaces.DensityBandwidthMeters(grid.CellMeters)),
            EvidenceLayers.Lithology => EvidenceSurfaces.ClassesInside(grid, UnitsLargestFirst(layer, read)),
            _ => EvidenceSurfaces.DistanceMeters(grid, [.. read.Select(r => r.Geometry)]),
        };

        return new BuiltEvidence(layer, values, null, provenance);
    }

    /// <summary>
    /// The units with a recognised rock class, the largest outline first so that a unit mapped in
    /// detail is laid over the regional one it sits in; equal outlines in id order, so two runs
    /// over the same units paint the same cells.
    /// </summary>
    /// <remarks>
    /// A unit whose stored class is outside the vocabulary paints nothing — the cells under it are
    /// left unclassed rather than scored as some class nobody recorded. It is still counted in the
    /// layer's provenance, since it was read.
    /// </remarks>
    private static List<(Geometry Outline, int ClassIndex)> UnitsLargestFirst(
        EvidenceLayer layer, List<(CavePotentialEvidenceRow Row, Geometry Geometry)> read)
    {
        var units = new List<(Geometry Outline, int ClassIndex, Guid Id)>();
        foreach (var (row, geometry) in read)
        {
            var code = Lithology.Find(row.Lithology)?.Code;
            var index = code is null ? -1 : IndexOf(layer.Categories, code);
            if (index >= 0)
            {
                units.Add((geometry, index, row.Id));
            }
        }

        return
        [
            .. units
                .OrderByDescending(u => u.Outline.Area)
                .ThenBy(u => u.Id)
                .Select(u => (u.Outline, u.ClassIndex)),
        ];
    }

    private async Task<BuiltEvidence> TerrainLayerAsync(
        EvidenceLayer layer, EvidenceGrid grid, TerrainSurfaces terrain, CancellationToken ct)
    {
        if (!terrain.HasTerrain)
        {
            return Unavailable(layer, EvidenceUnavailable.NoTerrain);
        }

        var values = new float[grid.CellCount];
        Array.Fill(values, float.NaN);
        var points = new DemSamplePoint[grid.Columns];

        if (layer.Code == EvidenceLayers.Elevation)
        {
            // A row at a time, so the raster library's single gate is given up between rows and a
            // probe on the map is not kept waiting for a whole grid.
            for (var row = 0; row < grid.Rows; row++)
            {
                FillRow(grid, row, points);
                var samples = await heights.SampleAsync(terrain.Coverage, points, ct);
                for (var column = 0; column < grid.Columns; column++)
                {
                    if (samples[column] is { Outcome: DemSampleOutcome.Sampled, ElevationM: { } metres })
                    {
                        values[grid.IndexOf(column, row)] = (float)metres;
                    }
                }
            }

            return new BuiltEvidence(
                layer, values, null, EvidenceProvenance.None,
                new TerrainInput(terrain.BuildId, layer.Code, null, null));
        }

        var derivative = layer.Code switch
        {
            EvidenceLayers.Slope => TerrainDerivative.Slope,
            EvidenceLayers.Aspect => TerrainDerivative.Aspect,
            EvidenceLayers.PositionIndex => TerrainDerivative.PositionIndex,
            EvidenceLayers.Ruggedness => TerrainDerivative.RuggednessIndex,
            EvidenceLayers.Roughness => TerrainDerivative.Roughness,
            EvidenceLayers.Geomorphons => TerrainDerivative.Geomorphons,
            _ => throw new ArgumentException($"'{layer.Code}' is not read from the terrain.", nameof(layer)),
        };

        var candidates = terrain.Views.Where(v => v.Derivative == derivative).ToList();
        var chosen = derivative == TerrainDerivative.Slope
            ? candidates.FirstOrDefault(v => v.SlopeUnit == TerrainSlopeUnit.Degrees)
            : candidates.FirstOrDefault();
        if (chosen is null)
        {
            return Unavailable(
                layer, candidates.Count > 0 ? EvidenceUnavailable.SlopeUnit : EvidenceUnavailable.NoLayer);
        }

        var tiles = await TilesAsync(chosen.Id, ct);
        if (tiles.Count == 0)
        {
            return Unavailable(layer, EvidenceUnavailable.NoLayer);
        }

        var categorical = layer.ValueKind == EvidenceValueKind.Categorical;
        for (var row = 0; row < grid.Rows; row++)
        {
            FillRow(grid, row, points);
            var samples = await pictures.SampleLayerAsync(tiles, points, ct);
            for (var column = 0; column < grid.Columns; column++)
            {
                if (samples[column] is not { Outcome: DemSampleOutcome.Sampled, Value: { } value })
                {
                    continue;
                }

                if (!categorical)
                {
                    values[grid.IndexOf(column, row)] = (float)value;
                }
                else if (TerrainGeomorphons.ClassOf(value) is { } code && IndexOf(layer.Categories, code) is >= 0 and var index)
                {
                    values[grid.IndexOf(column, row)] = index;
                }
            }
        }

        return new BuiltEvidence(
            layer, values, null, EvidenceProvenance.None,
            new TerrainInput(terrain.BuildId, layer.Code, chosen.Id, chosen.Settings));
    }

    private async Task<TerrainSurfaces> TerrainSurfacesAsync(CancellationToken ct)
    {
        var buildId = await db.TerrainBuilds.AsNoTracking()
            .Where(b => b.IsActive)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(ct);
        if (buildId is not { } build)
        {
            return new TerrainSurfaces(false, Guid.Empty, TerrainCoverage.None, []);
        }

        var views = (await catalogue.ForBuildAsync(build, ct))
            .Where(v => v.Status == TerrainDerivativeStatus.Ready && !v.Stale)
            .OrderByDescending(v => v.ComputedAt)
            .ToList();

        return new TerrainSurfaces(true, build, await rasters.CoverageForAsync(build, ct), views);
    }

    private async Task<IReadOnlyList<PreparedTerrainRaster>> TilesAsync(Guid layerId, CancellationToken ct)
    {
        var stored = await db.TerrainDerivativeRasters.AsNoTracking()
            .Where(r => r.TerrainDerivativeLayerId == layerId)
            .ToListAsync(ct);

        return
        [
            .. stored.Select(r =>
            {
                var box = r.Footprint.EnvelopeInternal;
                return new PreparedTerrainRaster(
                    r.Path, r.Width, r.Height, r.PixelSizeDegrees,
                    box.MinX, box.MinY, box.MaxX, box.MaxY, 0, r.SizeBytes);
            }),
        ];
    }

    private static void FillRow(EvidenceGrid grid, int row, DemSamplePoint[] points)
    {
        var latitude = grid.LatitudeOf(row);
        for (var column = 0; column < points.Length; column++)
        {
            points[column] = new DemSamplePoint(grid.LongitudeOf(column), latitude);
        }
    }

    private static int IndexOf(IReadOnlyList<string> codes, string code)
    {
        for (var i = 0; i < codes.Count; i++)
        {
            if (string.Equals(codes[i], code, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static BuiltEvidence Unavailable(EvidenceLayer layer, string reason) =>
        new(layer, null, reason, EvidenceProvenance.None);

    private sealed record TerrainSurfaces(
        bool HasTerrain, Guid BuildId, TerrainCoverage Coverage, IReadOnlyList<TerrainDerivativeLayerView> Views);
}
