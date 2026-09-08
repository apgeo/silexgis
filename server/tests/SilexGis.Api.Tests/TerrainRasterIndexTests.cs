// SPDX-License-Identifier: AGPL-3.0-or-later
using MaxRev.Gdal.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using OSGeo.GDAL;
using OSGeo.OSR;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Tests;

/// <summary>
/// What ground a build's prepared rasters cover, as it is stored and as it is read back.
/// </summary>
/// <remarks>
/// The point of storing it at all is that reading a height must not open every file to find out
/// which of them holds the point — one height would pay that, a profile along a cave pays it
/// hundreds of times. So what is asserted here is that the description is written down, that it is
/// read back rather than derived again, and that it says the same thing either way: a stored
/// footprint that disagreed with the file would put a height in the wrong valley with nothing to
/// show for it.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class TerrainRasterIndexTests : IAsyncLifetime, IDisposable
{
    private const int LongitudeLatitude = 4326;

    private const double West = 25.0;
    private const double North = 46.1;
    private const double Pixel = 0.001;

    /// <summary>The geoid undulation measured over Piatra Craiului, used as a real correction.</summary>
    private const double Undulation = 39.39;

    private readonly SilexGisApiFactory factory;
    private readonly string buildRoot;

    public TerrainRasterIndexTests(PostgresFixture postgres)
    {
        buildRoot = Path.Combine(Path.GetTempPath(), $"silexgis-tindex-{Guid.NewGuid():N}");

        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Terrain:BuildRoot"] = buildRoot },
            // This class queues nothing, and the shared container's queue is drained by whoever is
            // running one.
            JobWorkers.RemoveFrom);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.TerrainBuilds.Where(b => b.Id == seeded).ExecuteDeleteAsync();
        await factory.DisposeAsync();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(buildRoot))
            {
                Directory.Delete(buildRoot, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Litter, not a failing test.
        }
    }

    private Guid seeded;

    [Fact]
    public async Task A_build_with_no_stored_description_is_described_from_disk_and_written_down()
    {
        var buildId = await SeedAsync();
        Raster(buildId, "ground.tif");

        var coverage = await CoverageAsync(buildId);

        coverage.Rasters.Count.ShouldBe(1);
        coverage.Rasters[0].Width.ShouldBe(16);
        coverage.Rasters[0].West.ShouldBe(West, 1e-9);
        coverage.Rasters[0].North.ShouldBe(North, 1e-9);
        coverage.Rasters[0].VoidValue.ShouldBe(TerrainRasterPreparation.VoidValue);

        (await StoredCountAsync(buildId)).ShouldBe(1);
    }

    [Fact]
    public async Task The_stored_description_is_read_back_rather_than_derived_a_second_time()
    {
        var buildId = await SeedAsync();
        Raster(buildId, "ground.tif");

        var first = await CoverageAsync(buildId);

        // The file goes away. Anything still opening the disk to answer would now report nothing;
        // the stored description is what makes the second answer the same as the first.
        File.Delete(Path.Combine(PreparedDirectory(buildId), "ground.tif"));

        var second = await CoverageAsync(buildId);

        second.Rasters.Count.ShouldBe(first.Rasters.Count);
        second.Rasters[0].Path.ShouldBe(first.Rasters[0].Path);
        second.Rasters[0].East.ShouldBe(first.Rasters[0].East, 1e-9);
        second.Rasters[0].PixelSizeDegrees.ShouldBe(first.Rasters[0].PixelSizeDegrees, 1e-12);

        // And describing again is still a replacement, not an addition.
        (await StoredCountAsync(buildId)).ShouldBe(1);
    }

    [Fact]
    public async Task Describing_a_build_again_replaces_what_it_said_rather_than_saying_it_twice()
    {
        var buildId = await SeedAsync();
        Raster(buildId, "ground.tif");
        Raster(buildId, "fill.tif");

        (await CoverageAsync(buildId)).Rasters.Count.ShouldBe(2);
        (await StoredCountAsync(buildId)).ShouldBe(2);

        File.Delete(Path.Combine(PreparedDirectory(buildId), "fill.tif"));
        await RefreshAsync(buildId);

        (await StoredCountAsync(buildId)).ShouldBe(1);
        (await CoverageAsync(buildId)).Rasters.Count.ShouldBe(1);
    }

    /// <summary>
    /// Several readers arriving at an undescribed build at once all get the description, and one
    /// description is what is left behind.
    /// </summary>
    /// <remarks>
    /// Writing the description is a replacement — the rows are deleted and written again — and the
    /// read path performs it whenever it finds nothing stored. So the ordinary case of a build
    /// prepared and then asked about by whoever happens to arrive first is several callers each
    /// beginning that replacement: without something ordering them, two delete an empty set, two
    /// write the same rows, and the second meets the rule that one path appears once per build. The
    /// caller sees that as a failed request, over an answer neither of them disagreed about.
    /// </remarks>
    [Fact]
    public async Task Readers_arriving_at_an_undescribed_build_together_all_get_its_coverage()
    {
        var buildId = await SeedAsync();
        Raster(buildId, "ground.tif");
        Raster(buildId, "fill.tif");

        const int readers = 8;

        var coverages = await Task.WhenAll(
            Enumerable.Range(0, readers).Select(_ => Task.Run(() => CoverageAsync(buildId))));

        foreach (var coverage in coverages)
        {
            coverage.Rasters.Count.ShouldBe(2);
        }

        // And the table holds one description of this build, not one per reader that raced for it.
        (await StoredCountAsync(buildId)).ShouldBe(2);
    }

    [Fact]
    public async Task A_build_with_no_prepared_rasters_covers_no_ground_at_all()
    {
        var buildId = await SeedAsync();

        var coverage = await CoverageAsync(buildId);

        coverage.Rasters.ShouldBeEmpty();
        (await StoredCountAsync(buildId)).ShouldBe(0);
    }

    [Fact]
    public async Task A_build_nobody_ever_made_covers_no_ground_and_carries_no_correction()
    {
        var coverage = await CoverageAsync(Guid.CreateVersion7());

        coverage.Rasters.ShouldBeEmpty();
        coverage.SampleToSurveyOffsetM.ShouldBe(0);
    }

    [Fact]
    public async Task The_datum_recorded_against_the_build_travels_with_its_rasters()
    {
        var buildId = await SeedAsync(TerrainHeightDatum.Ellipsoidal, 39.39);
        Raster(buildId, "ground.tif");

        var coverage = await CoverageAsync(buildId);

        coverage.HeightDatum.ShouldBe(TerrainHeightDatum.Ellipsoidal);
        coverage.GeoidHeightM.ShouldBe(39.39);

        // Resolved by the one function that owns the sign of that arithmetic, so that a height read
        // out of these rasters is corrected exactly once on its way to meeting a survey.
        coverage.SampleToSurveyOffsetM.ShouldBe(
            GeoidOffset.SampleToSurveyOffsetM(TerrainHeightDatum.Ellipsoidal, 39.39));
    }

    [Fact]
    public async Task The_build_the_installation_serves_is_the_one_whose_ground_is_read()
    {
        var buildId = await SeedAsync(active: true);
        Raster(buildId, "ground.tif");

        await using var scope = factory.Services.CreateAsyncScope();
        var index = scope.ServiceProvider.GetRequiredService<TerrainRasterIndex>();

        var coverage = await index.ActiveCoverageAsync(CancellationToken.None);

        // The active build, and not the narrower "active and already baked into tiles": this build
        // has prepared rasters and no pyramid, which is exactly the state a build is in between its
        // second step and its last one.
        coverage.Rasters.Count.ShouldBe(1);
    }

    /// <summary>
    /// A height read out of an ellipsoidal build's rasters, all the way from the row that records
    /// what those heights are measured from to the number a caller would be handed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The correction is worth about forty metres over Romanian karst, and that is exactly what
    /// makes it dangerous: a missing correction and a doubled one both come back as a perfectly
    /// plausible altitude for a hillside, and nothing in the number itself says which it is. An
    /// assertion that the answer is merely "corrected" would pass on all three.
    /// </para>
    /// <para>
    /// So the same ground is read twice out of the same file, differing only in what the build
    /// records its heights as being measured from, and the gap between the two answers is asserted
    /// to be one undulation exactly. A correction applied a second time anywhere between the stored
    /// row and the answer — in resolving the coverage, in the reader, or in a helper either of them
    /// calls — makes that gap two, and a correction lost makes it none.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Ground_recorded_as_ellipsoidal_meets_a_survey_exactly_one_undulation_lower()
    {
        var buildId = await SeedAsync(TerrainHeightDatum.Ellipsoidal, Undulation);
        Raster(buildId, "ground.tif");

        var recorded = await CoverageAsync(buildId);

        // The same rasters and the same undulation, differing only in what the heights in them are
        // said to be measured from. That isolates the correction from everything else about the
        // build, so the difference between the two answers can only be the correction itself.
        var asSurveyed = recorded with { HeightDatum = TerrainHeightDatum.Orthometric };

        await using var scope = factory.Services.CreateAsyncScope();
        var sampler = scope.ServiceProvider.GetRequiredService<IDemSampleService>();

        var point = new DemSamplePoint(West + (3.5 * Pixel), North - (5.5 * Pixel));
        var ellipsoidal = (await sampler.SampleAsync(recorded, [point], CancellationToken.None))
            .Single();
        var orthometric = (await sampler.SampleAsync(asSurveyed, [point], CancellationToken.None))
            .Single();

        // The height this class writes into pixel (3, 5): its rasters count upwards across the grid,
        // so the value there is known by construction rather than read back out of the file.
        const double written = 100 + (5 * 16) + 3;

        orthometric.Outcome.ShouldBe(DemSampleOutcome.Sampled);
        orthometric.ElevationM!.Value.ShouldBe(written, 0.001);

        ellipsoidal.Outcome.ShouldBe(DemSampleOutcome.Sampled);
        ellipsoidal.ElevationM!.Value.ShouldBe(written - Undulation, 0.001);

        (orthometric.ElevationM!.Value - ellipsoidal.ElevationM!.Value)
            .ShouldBe(Undulation, 0.001);
    }

    private string PreparedDirectory(Guid buildId) =>
        Path.Combine(buildRoot, buildId.ToString("N"), "prepared");

    private async Task<TerrainCoverage> CoverageAsync(Guid buildId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var index = scope.ServiceProvider.GetRequiredService<TerrainRasterIndex>();
        return await index.CoverageForAsync(buildId, CancellationToken.None);
    }

    private async Task RefreshAsync(Guid buildId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var index = scope.ServiceProvider.GetRequiredService<TerrainRasterIndex>();
        await index.RefreshAsync(buildId, CancellationToken.None);
    }

    private async Task<int> StoredCountAsync(Guid buildId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TerrainBuildRasters.CountAsync(r => r.TerrainBuildId == buildId);
    }

    private async Task<Guid> SeedAsync(
        TerrainHeightDatum datum = TerrainHeightDatum.Orthometric,
        double geoidHeightM = 0,
        bool active = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // At most one build in the whole installation is the active one, held by a unique index
        // rather than by a handler, and the container is shared.
        if (active)
        {
            await db.TerrainBuilds.Where(b => b.IsActive)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsActive, false));
        }

        var build = new TerrainBuild
        {
            Extent = new GeometryFactory(new PrecisionModel(), LongitudeLatitude).CreatePolygon(
            [
                new Coordinate(25.0, 46.0),
                new Coordinate(25.1, 46.0),
                new Coordinate(25.1, 46.1),
                new Coordinate(25.0, 46.1),
                new Coordinate(25.0, 46.0),
            ]),
            RequestedMaxDepth = 13,
            Status = TerrainBuildStatus.Queued,
            Phase = TerrainBuildPhase.Pending,
            HeightDatum = datum,
            GeoidHeightM = geoidHeightM,
            IsActive = active,
        };

        db.TerrainBuilds.Add(build);
        await db.SaveChangesAsync();
        seeded = build.Id;
        return build.Id;
    }

    /// <summary>
    /// A prepared raster in this build's prepared directory: the form the preparation step leaves
    /// behind, so what the index reads is what it reads in production.
    /// </summary>
    private void Raster(Guid buildId, string name)
    {
        GdalBase.ConfigureAll();
        var directory = PreparedDirectory(buildId);
        Directory.CreateDirectory(directory);

        using var dataset = Gdal.GetDriverByName("GTiff")
            .Create(Path.Combine(directory, name), 16, 16, 1, DataType.GDT_Float32, null);

        dataset.SetGeoTransform([West, Pixel, 0, North, 0, -Pixel]);

        using (var reference = new SpatialReference(""))
        {
            reference.ImportFromEPSG(LongitudeLatitude);
            reference.ExportToWkt(out var wkt, null);
            dataset.SetProjection(wkt);
        }

        var band = dataset.GetRasterBand(1);
        band.SetNoDataValue(TerrainRasterPreparation.VoidValue);

        var pixels = new float[16 * 16];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = 100f + i;
        }

        band.WriteRaster(0, 0, 16, 16, pixels, 16, 16, 0, 0);
        dataset.FlushCache();
    }
}
