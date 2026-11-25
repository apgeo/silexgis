// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MaxRev.Gdal.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using OSGeo.GDAL;
using OSGeo.OSR;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Potential;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Analysis;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The evidence layers of a cave-potential run, made on the run's grid: terrain surfaces read cell
/// by cell from invented rasters, and surfaces made from recorded features — every one of them
/// from the features the account the run is for may place exactly, and from no others.
/// </summary>
public sealed class CavePotentialEvidenceTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const double West = 75.0;
    private const double North = -48.9;
    private const double Pixel = 0.001;
    private const double East = West + (16 * Pixel);
    private const double South = North - (16 * Pixel);
    private const double Latitude = North - 0.010;

    private readonly SilexGisApiFactory factory;
    private readonly string buildRoot;

    private HttpClient owner = null!;
    private Guid ownerId;
    private Guid viewerId;
    private Guid buildId;

    private long caveTypeId;
    private long entranceTypeId;
    private long sinkholeTypeId;
    private long fractureTypeId;
    private long readingTypeId;
    private long unitTypeId;

    public CavePotentialEvidenceTests(PostgresFixture postgres)
    {
        buildRoot = Path.Combine(TestScratch.Root, $"silexgis-potev-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Terrain:BuildRoot"] = buildRoot },
            JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"pe-own-{suffix}@t.local");
        // A viewer reads what is visible to everyone signed in and holds no grant to place a
        // protected position: the reader a protected feature has to stay out of reach of.
        viewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"pe-read-{suffix}@t.local");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
            sinkholeTypeId = await TypeAsync(db, FeatureTypeSeeds.Sinkhole);
            fractureTypeId = await TypeAsync(db, FeatureTypeSeeds.FractureLine);
            readingTypeId = await TypeAsync(db, FeatureTypeSeeds.StructuralMeasurement);
            unitTypeId = await TypeAsync(db, FeatureTypeSeeds.GeologicalUnit);

            // The tests share one database and one box of open ocean. Each test that records
            // features records a kind no other test here reads, so none sees another's.
            await db.TerrainBuilds.Where(b => b.IsActive)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsActive, false));
            var build = new TerrainBuild
            {
                Extent = Rectangle(),
                RequestedMaxDepth = 13,
                Status = TerrainBuildStatus.Queued,
                Phase = TerrainBuildPhase.Pending,
                HeightDatum = TerrainHeightDatum.Orthometric,
                GeoidHeightM = 0,
                IsActive = true,
            };
            db.TerrainBuilds.Add(build);
            await db.SaveChangesAsync();
            buildId = build.Id;
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"pe-own-{suffix}@t.local");
    }

    [Fact]
    public async Task Terrain_layers_are_read_at_every_cell_from_the_current_surfaces()
    {
        await SeedLayerAsync(TerrainDerivative.Slope, c => c < 8 ? 10f : c < 14 ? 40f : 0f);
        // Landform class numbers: 6 slope, 3 ridge, 9 valley, and a nought that is no class.
        await SeedLayerAsync(TerrainDerivative.Geomorphons, c => c < 8 ? 6f : c < 14 ? 3f : c == 14 ? 9f : 0f);
        await SeedElevationAsync(300f);
        var grid = Grid();

        var built = await BuildAsync(
            viewerId, grid, EvidenceLayers.Slope, EvidenceLayers.Geomorphons, EvidenceLayers.Elevation);

        var slope = built[0];
        var landforms = built[1];
        var heights = built[2].Values!.Where(v => !float.IsNaN(v)).ToList();
        heights.Count.ShouldBeGreaterThan(grid.CellCount / 2);
        heights.ShouldAllBe(v => Math.Abs(v - 300f) < 0.01f);
        slope.Available.ShouldBeTrue(slope.UnavailableReason);
        landforms.Available.ShouldBeTrue(landforms.UnavailableReason);
        slope.Provenance.ShouldBe(EvidenceProvenance.None);
        slope.Provenance.UsedProtectedPositions.ShouldBeFalse();

        var classes = landforms.Layer.Categories;
        var read = 0;
        for (var row = 0; row < grid.Rows; row++)
        {
            for (var column = 0; column < grid.Columns; column++)
            {
                var index = grid.IndexOf(column, row);
                var pixel = (int)Math.Floor((grid.LongitudeOf(column) - West) / Pixel);
                if (pixel > 15 || grid.LatitudeOf(row) < South)
                {
                    // The grid runs whole cells past the box, and past the raster with it.
                    float.IsNaN(slope.Values![index]).ShouldBeTrue();
                    float.IsNaN(landforms.Values![index]).ShouldBeTrue();
                    continue;
                }

                read++;
                slope.Values![index].ShouldBe(pixel < 8 ? 10f : pixel < 14 ? 40f : 0f);
                var landform = pixel < 8 ? "slope" : pixel < 14 ? "ridge" : pixel == 14 ? "valley" : null;
                if (landform is null)
                {
                    float.IsNaN(landforms.Values![index]).ShouldBeTrue();
                }
                else
                {
                    classes[(int)landforms.Values![index]].ShouldBe(landform);
                }
            }
        }

        // The fixture is only proving something if most of the grid really lies on the raster.
        read.ShouldBeGreaterThan(grid.CellCount / 2);
    }

    [Fact]
    public async Task A_surface_that_cannot_be_read_is_reported_with_its_reason_and_never_computed()
    {
        await SeedLayerAsync(TerrainDerivative.Slope, _ => 12f, TerrainSlopeUnit.Percent);
        var grid = Grid();

        var built = await BuildAsync(ownerId, grid, EvidenceLayers.Slope, EvidenceLayers.Aspect);

        built[0].Available.ShouldBeFalse();
        built[0].UnavailableReason.ShouldBe(EvidenceUnavailable.SlopeUnit);
        built[1].UnavailableReason.ShouldBe(EvidenceUnavailable.NoLayer);

        // With steepness in degrees beside it, the same question is answered.
        await SeedLayerAsync(TerrainDerivative.Slope, _ => 7f);
        (await BuildAsync(ownerId, grid, EvidenceLayers.Slope))[0].Values!
            .Count(v => v == 7f).ShouldBeGreaterThan(grid.CellCount / 2);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.TerrainBuilds.Where(b => b.Id == buildId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsActive, false));
        }

        var groundless = await BuildAsync(ownerId, grid, EvidenceLayers.Elevation, EvidenceLayers.Slope);
        groundless.ShouldAllBe(b => b.UnavailableReason == EvidenceUnavailable.NoTerrain);

        await Should.ThrowAsync<ArgumentException>(() => BuildAsync(ownerId, grid, "no_such_layer"));
    }

    [Fact]
    public async Task Entrance_density_is_made_only_from_entrances_the_account_may_place_exactly()
    {
        const double PublicLongitude = West + 0.004;
        const double ProtectedLongitude = West + 0.012;
        await CaveAtAsync(PublicLongitude, locationProtected: false);
        await CaveAtAsync(ProtectedLongitude, locationProtected: true);
        var grid = Grid();

        var forOwner = (await BuildAsync(ownerId, grid, EvidenceLayers.EntranceDensity))[0];
        var forViewer = (await BuildAsync(viewerId, grid, EvidenceLayers.EntranceDensity))[0];

        // The owner places both, and the layer says a protected position went into it.
        forOwner.Provenance.FeatureIds.Count.ShouldBe(2);
        forOwner.Provenance.ProtectedFeatures.ShouldBe(1);
        forOwner.Provenance.UsedProtectedPositions.ShouldBeTrue();

        // The viewer's layer is the layer of a register that never held the protected cave: the
        // same numbers, cell for cell, as a density of the one public entrance alone.
        forViewer.Provenance.FeatureIds.Count.ShouldBe(1);
        forViewer.Provenance.UsedProtectedPositions.ShouldBeFalse();
        forOwner.Provenance.FeatureIds.ShouldContain(forViewer.Provenance.FeatureIds[0]);
        var alone = EvidenceSurfaces.KernelDensityPerKm2(
            grid,
            [new Coordinate(PublicLongitude, Latitude)],
            EvidenceSurfaces.DensityBandwidthMeters(grid.CellMeters));
        for (var i = 0; i < alone.Length; i++)
        {
            forViewer.Values![i].ShouldBe(alone[i], 1e-6f);
        }

        var atProtected = CellAt(grid, ProtectedLongitude, Latitude);
        forOwner.Values![atProtected].ShouldBeGreaterThan(forViewer.Values![atProtected] * 1.5f);
    }

    [Fact]
    public async Task Distance_to_sinkholes_has_no_zero_on_a_sinkhole_the_account_may_not_place()
    {
        const double PublicLongitude = West + 0.003;
        const double ProtectedLongitude = West + 0.013;
        await FeatureAsync(sinkholeTypeId, Point(PublicLongitude, Latitude), locationProtected: false);
        var hidden = await FeatureAsync(sinkholeTypeId, Point(ProtectedLongitude, Latitude), locationProtected: true);
        var grid = Grid();

        var forOwner = (await BuildAsync(ownerId, grid, EvidenceLayers.SinkholeDistance))[0];
        var forViewer = (await BuildAsync(viewerId, grid, EvidenceLayers.SinkholeDistance))[0];

        var atProtected = CellAt(grid, ProtectedLongitude, Latitude);
        forOwner.Provenance.FeatureIds.ShouldContain(hidden);
        forOwner.Provenance.ProtectedFeatures.ShouldBe(1);
        forOwner.Values![atProtected].ShouldBeLessThan((float)grid.CellMeters);

        // For the viewer the cell over the protected sinkhole is simply three quarters of a
        // kilometre from the public one, like any other cell out there.
        forViewer.Provenance.FeatureIds.ShouldNotContain(hidden);
        forViewer.Provenance.FeatureIds.Count.ShouldBe(1);
        forViewer.Provenance.UsedProtectedPositions.ShouldBeFalse();
        forViewer.Values![atProtected].ShouldBeInRange(700f, 850f);
        forViewer.Values![CellAt(grid, PublicLongitude, Latitude)].ShouldBeLessThan((float)grid.CellMeters);
    }

    [Fact]
    public async Task Distance_to_faults_reads_fault_lines_and_fault_readings_and_no_other_reading()
    {
        var line = await FeatureAsync(
            fractureTypeId,
            new
            {
                type = "LineString",
                coordinates = new[] { new[] { West + 0.002, North - 0.014 }, new[] { West + 0.002, North - 0.002 } },
            },
            locationProtected: false);
        var fault = await ReadingAsync(West + 0.014, StructuralMeasurement.FaultType);
        var bedding = await ReadingAsync(West + 0.008, "bedding");
        var grid = Grid();

        var built = (await BuildAsync(viewerId, grid, EvidenceLayers.FaultDistance))[0];

        built.Provenance.FeatureIds.ShouldBe(new[] { line, fault }.Order().ToArray());
        built.Provenance.FeatureIds.ShouldNotContain(bedding);
        built.Values![CellAt(grid, West + 0.014, Latitude)].ShouldBeLessThan((float)grid.CellMeters);
        built.Values![CellAt(grid, West + 0.002, North - 0.005)].ShouldBeLessThan((float)grid.CellMeters);
        // The bedding reading is 0.006 degrees from either fault, some 440 m at this latitude, and
        // is not itself one.
        built.Values![CellAt(grid, West + 0.008, Latitude)].ShouldBeInRange(400f, 520f);

        // A protected fault line and a protected fault reading are drawn for an account that may
        // place them and are no part of anybody else's layer: no zero over either.
        var hiddenLine = await FeatureAsync(
            fractureTypeId,
            new
            {
                type = "LineString",
                coordinates = new[] { new[] { West + 0.010, North - 0.014 }, new[] { West + 0.010, North - 0.002 } },
            },
            locationProtected: true);
        var hiddenReading = await ReadingAsync(West + 0.0055, StructuralMeasurement.FaultType, locationProtected: true);
        var onHiddenLine = CellAt(grid, West + 0.010, North - 0.004);
        var onHiddenReading = CellAt(grid, West + 0.0055, Latitude);

        var forViewer = (await BuildAsync(viewerId, grid, EvidenceLayers.FaultDistance))[0];
        forViewer.Provenance.FeatureIds.ShouldBe(new[] { line, fault }.Order().ToArray());
        forViewer.Provenance.UsedProtectedPositions.ShouldBeFalse();
        forViewer.Values![onHiddenLine].ShouldBeGreaterThan(200f);
        forViewer.Values![onHiddenReading].ShouldBeGreaterThan(200f);

        var forOwner = (await BuildAsync(ownerId, grid, EvidenceLayers.FaultDistance))[0];
        forOwner.Provenance.FeatureIds.ShouldContain(hiddenLine);
        forOwner.Provenance.FeatureIds.ShouldContain(hiddenReading);
        forOwner.Provenance.ProtectedFeatures.ShouldBe(2);
        forOwner.Values![onHiddenLine].ShouldBeLessThan((float)grid.CellMeters);
        forOwner.Values![onHiddenReading].ShouldBeLessThan((float)grid.CellMeters);
    }

    [Fact]
    public async Task Every_source_of_recorded_features_is_read_through_both_the_visible_and_the_exactly_placeable_fragments()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var ctx = await AccessContextResolver.ResolveAsync(db, viewerId);
        var (visible, exact, _) = AccessSql.FeatureLayerFragments(ctx, "f");

        foreach (var source in Enum.GetValues<CavePotentialEvidenceSource>())
        {
            var (sql, _) = CavePotentialEvidenceSql.Build(ctx, source, [1L, 2L], West, South, East, North, 10);
            sql.ShouldContain($"AND {visible}", Case.Sensitive, $"{source} must be filtered by what the account may read");
            sql.ShouldContain($"AND {exact}", Case.Sensitive, $"{source} must be filtered by what the account may place exactly");
        }
    }

    [Fact]
    public async Task Lithology_gives_each_cell_the_class_of_the_unit_it_falls_in_the_smaller_unit_over_the_larger()
    {
        await UnitAsync("limestone", West, North - 0.016, West + 0.008, North, locationProtected: false);
        await UnitAsync("sandstone", West + 0.002, North - 0.012, West + 0.004, North - 0.008, locationProtected: false);
        await UnitAsync(null, West + 0.01, North - 0.016, West + 0.016, North, locationProtected: false);
        var hidden = await UnitAsync(
            "granite", West + 0.012, North - 0.006, West + 0.015, North - 0.001, locationProtected: true);
        var grid = Grid();

        var forViewer = (await BuildAsync(viewerId, grid, EvidenceLayers.Lithology))[0];
        var forOwner = (await BuildAsync(ownerId, grid, EvidenceLayers.Lithology))[0];

        var classes = forViewer.Layer.Categories;
        classes[(int)forViewer.Values![CellAt(grid, West + 0.006, North - 0.005)]].ShouldBe("limestone");
        classes[(int)forViewer.Values![CellAt(grid, West + 0.003, North - 0.01)]].ShouldBe("sandstone");
        // A unit nobody gave a rock class to is read and classes nothing; so does ground with no unit.
        float.IsNaN(forViewer.Values![CellAt(grid, West + 0.011, North - 0.01)]).ShouldBeTrue();
        float.IsNaN(forViewer.Values![CellAt(grid, West + 0.009, North - 0.01)]).ShouldBeTrue();

        // The protected outline is drawn for its owner and is no part of the viewer's layer.
        forViewer.Provenance.FeatureIds.Count.ShouldBe(3);
        forViewer.Provenance.FeatureIds.ShouldNotContain(hidden);
        float.IsNaN(forViewer.Values![CellAt(grid, West + 0.0135, North - 0.0035)]).ShouldBeTrue();
        forOwner.Provenance.FeatureIds.ShouldContain(hidden);
        forOwner.Provenance.ProtectedFeatures.ShouldBe(1);
        classes[(int)forOwner.Values![CellAt(grid, West + 0.0135, North - 0.0035)]].ShouldBe("granite");
    }

    public async ValueTask DisposeAsync()
    {
        await factory.DisposeAsync();
        Dispose();
    }

    public void Dispose() => owner?.Dispose();

    private static EvidenceGrid Grid() => EvidenceGrid.Over(West, South, East, North, 50)!;

    private static int CellAt(EvidenceGrid grid, double longitude, double latitude) =>
        grid.IndexOf(
            (int)Math.Floor((longitude - grid.West) / grid.CellWidthDegrees),
            (int)Math.Floor((grid.North - latitude) / grid.CellHeightDegrees));

    private static object Point(double longitude, double latitude) =>
        new { type = "Point", coordinates = new[] { longitude, latitude } };

    private static Task<long> TypeAsync(SilexGisDbContext db, string code) =>
        db.FeatureTypes.Where(t => t.Code == code).Select(t => t.Id).SingleAsync();

    /// <summary>
    /// Builds the layers for an account whose rights are resolved from its rows, the way a queued
    /// run resolves the account it is for.
    /// </summary>
    private async Task<IReadOnlyList<BuiltEvidence>> BuildAsync(Guid userId, EvidenceGrid grid, params string[] layers)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var ctx = await AccessContextResolver.ResolveAsync(db, userId);
        var builder = scope.ServiceProvider.GetRequiredService<CavePotentialEvidenceBuilder>();
        return await builder.BuildAsync(ctx, grid, layers, CancellationToken.None);
    }

    private async Task<Guid> FeatureAsync(
        long featureTypeId, object geometry, bool locationProtected, object? properties = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"PotEv {Guid.NewGuid():N}"[..24],
            featureTypeId,
            geometry,
            properties,
            locationProtected,
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private Task<Guid> ReadingAsync(double longitude, string type, bool locationProtected = false) =>
        FeatureAsync(
            readingTypeId,
            Point(longitude, Latitude),
            locationProtected,
            new Dictionary<string, object>
            {
                [StructuralMeasurement.TypeKey] = type,
                [StructuralMeasurement.DipDirectionKey] = 120,
                [StructuralMeasurement.DipKey] = 60,
            });

    private Task<Guid> UnitAsync(
        string? lithology, double west, double south, double east, double north, bool locationProtected)
    {
        var properties = new Dictionary<string, string> { ["formation"] = "Invented Formation" };
        if (lithology is not null)
        {
            properties[Lithology.PropertyKey] = lithology;
        }

        return FeatureAsync(
            unitTypeId,
            new
            {
                type = "Polygon",
                coordinates = new[]
                {
                    new[]
                    {
                        new[] { west, south }, new[] { east, south }, new[] { east, north },
                        new[] { west, north }, new[] { west, south },
                    },
                },
            },
            locationProtected,
            properties);
    }

    private async Task CaveAtAsync(double longitude, bool locationProtected)
    {
        var caveResponse = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"PotEv {Guid.NewGuid():N}"[..20],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await caveResponse.Content.ReadAsStringAsync();
        caveResponse.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var caveId = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var entrance = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { longitude, Latitude } },
            positionQuality = "Gps",
        });
        entrance.StatusCode.ShouldBe(HttpStatusCode.Created, await entrance.Content.ReadAsStringAsync());
    }

    private static Polygon Rectangle() =>
        new GeometryFactory(new PrecisionModel(), 4326).CreatePolygon(
        [
            new Coordinate(West, South),
            new Coordinate(East, South),
            new Coordinate(East, North),
            new Coordinate(West, North),
            new Coordinate(West, South),
        ]);

    /// <summary>
    /// A computed surface of the active build, held in a real raster whose value is a function of
    /// the pixel column, registered as ready and current the way the job that draws it would.
    /// </summary>
    private async Task SeedLayerAsync(
        TerrainDerivative derivative, Func<int, float> byColumn, TerrainSlopeUnit unit = TerrainSlopeUnit.Degrees)
    {
        var settings = TerrainDerivativeRegistry.Normalise(
            new TerrainDerivativeSettings { Derivative = derivative, SlopeUnit = unit });

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var layer = new TerrainDerivativeLayer
        {
            TerrainBuildId = buildId,
            Derivative = derivative,
            Settings = TerrainDerivativeRegistry.Describe(settings),
            SettingsHash = TerrainDerivativeRegistry.Fingerprint(settings),
            Name = derivative.ToString(),
            Status = TerrainDerivativeStatus.Ready,
            Version = 1,
            MethodRevision = TerrainDerivativeRegistry.MethodRevision(derivative),
            ComputedAt = DateTimeOffset.UtcNow,
        };
        db.TerrainDerivativeLayers.Add(layer);

        var directory = Path.Combine(buildRoot, buildId.ToString("N"), "derivatives", layer.Id.ToString("N"), "v1");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "tile.tif");
        WriteColumns(file, byColumn);

        db.TerrainDerivativeRasters.Add(new TerrainDerivativeRaster
        {
            TerrainDerivativeLayerId = layer.Id,
            SourcePath = Path.Combine(buildRoot, buildId.ToString("N"), "prepared", "ground.tif"),
            Path = file,
            Width = 16,
            Height = 16,
            PixelSizeDegrees = Pixel,
            Footprint = Rectangle(),
            SizeBytes = new FileInfo(file).Length,
            ComputedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>The build's own elevation: one prepared raster of a single height, indexed as a build indexes it.</summary>
    private async Task SeedElevationAsync(float metres)
    {
        var directory = Path.Combine(buildRoot, buildId.ToString("N"), "prepared");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "ground.tif");
        WriteColumns(file, _ => metres);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.TerrainBuildRasters.Add(new TerrainBuildRaster
        {
            TerrainBuildId = buildId,
            Path = file,
            Width = 16,
            Height = 16,
            PixelSizeDegrees = Pixel,
            Footprint = Rectangle(),
            VoidValue = -9999,
            SizeBytes = new FileInfo(file).Length,
            DescribedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static void WriteColumns(string path, Func<int, float> byColumn)
    {
        GdalBase.ConfigureAll();
        using var dataset = Gdal.GetDriverByName("GTiff").Create(path, 16, 16, 1, DataType.GDT_Float32, null);
        dataset.SetGeoTransform([West, Pixel, 0, North, 0, -Pixel]);
        using (var reference = new SpatialReference(""))
        {
            reference.ImportFromEPSG(4326);
            reference.ExportToWkt(out var wkt, null);
            dataset.SetProjection(wkt);
        }

        var band = dataset.GetRasterBand(1);
        band.SetNoDataValue(-9999);
        var pixels = new float[16 * 16];
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                pixels[(y * 16) + x] = byColumn(x);
            }
        }

        band.WriteRaster(0, 0, 16, 16, pixels, 16, 16, 0, 0);
        dataset.FlushCache();
    }
}
