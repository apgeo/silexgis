// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using MaxRev.Gdal.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using OSGeo.GDAL;
using OSGeo.OSR;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Tests;

/// <summary>
/// The register of computed pictures of the ground: what a picture records about where it came
/// from, and what happens to it when that ground is replaced.
/// </summary>
/// <remarks>
/// <para>
/// The register exists because the catalogue of uploaded overlays cannot answer either question.
/// That one records an owner, an audience and a file; a picture the server drew has none of those
/// and needs five things it has no column for — the elevation it came from, the arithmetic, the
/// settings, the run, and how many times it has been recomputed. Without them a shaded relief drawn
/// from elevation that has since been replaced goes on being served with nothing saying so, and it
/// disagrees with the heights beneath it in a way that reads as a fault in the cave data.
/// </para>
/// <para>
/// So the assertions here are about provenance and honesty rather than about pixels: that a
/// finished picture names the build and the raster it was drawn from, and that replacing that build
/// changes what the register says about the picture without taking the picture away.
/// </para>
/// </remarks>
/// <remarks>
/// In the collection that takes turns with the raster library rather than beside the other
/// database tests: these drive real writes of cloud-optimised rasters, and the setting that says
/// where the library may work is one value for the whole process, moved for the length of a test
/// elsewhere in that collection. Running beside those, a write here fails on a directory nobody
/// here chose, and it is reported as the picture having failed to compute.
/// </remarks>
[Collection(RasterScratchCollection.Name)]
public sealed class TerrainDerivativeRegistryTests : IAsyncLifetime, IDisposable
{
    private const int LongitudeLatitude = 4326;

    private const double West = 25.0;
    private const double North = 46.1;
    private const double Pixel = 0.001;

    private readonly SilexGisApiFactory factory;
    private readonly string buildRoot;
    private readonly List<Guid> seeded = [];

    public TerrainDerivativeRegistryTests(PostgresFixture postgres)
    {
        buildRoot = Path.Combine(Path.GetTempPath(), $"silexgis-tderiv-{Guid.NewGuid():N}");

        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Terrain:BuildRoot"] = buildRoot },
            // The handler is driven here directly. A worker running beside it would claim the job
            // row first and this class would be asserting against work it did not do.
            JobWorkers.RemoveFrom);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // The pictures and their rasters go with the build they were drawn from.
        await db.TerrainBuilds.Where(b => seeded.Contains(b.Id)).ExecuteDeleteAsync();
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

    /// <summary>
    /// A finished picture names the build it was drawn from, the raster it was drawn from, and the
    /// run that drew it.
    /// </summary>
    [Fact]
    public async Task A_computed_picture_records_the_ground_and_the_run_it_came_from()
    {
        var buildId = await SeedBuildAsync(active: true);
        var source = Raster(buildId, "ground.tif");
        var layerId = await AskForAsync(buildId, new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Hillshade,
        });

        var jobId = await ComputeAsync(layerId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var layer = await db.TerrainDerivativeLayers.AsNoTracking().SingleAsync(l => l.Id == layerId);
        layer.Status.ShouldBe(TerrainDerivativeStatus.Ready, layer.Message);
        layer.TerrainBuildId.ShouldBe(buildId);
        layer.ProcessingJobId.ShouldBe(jobId);

        // Counting from one, so that "never computed" and "computed once" are different answers.
        layer.Version.ShouldBe(1);
        layer.ComputedAt.ShouldNotBeNull();
        layer.SizeBytes.ShouldBeGreaterThan(0);

        var rasters = await db.TerrainDerivativeRasters.AsNoTracking()
            .Where(r => r.TerrainDerivativeLayerId == layerId).ToListAsync();

        // One output per elevation raster: a build's rasters are separate files at separate pixel
        // sizes and are deliberately never merged onto one grid.
        rasters.Count.ShouldBe(1);
        rasters[0].SourcePath.ShouldBe(source);
        File.Exists(rasters[0].Path).ShouldBeTrue(rasters[0].Path);

        // Beside the elevation and never among it: everything that reads a build's heights
        // enumerates the prepared directory and describes whatever raster it finds there.
        rasters[0].Path.ShouldContain(Path.Combine(buildId.ToString("N"), "derivatives"));
        rasters[0].Path.ShouldNotContain(Path.Combine(buildId.ToString("N"), "prepared"));

        rasters[0].Width.ShouldBeGreaterThan(0);
        rasters[0].Footprint.EnvelopeInternal.MinX.ShouldBe(West, 1e-9);
        rasters[0].Footprint.EnvelopeInternal.MaxY.ShouldBe(North, 1e-9);
    }

    /// <summary>
    /// Replacing the elevation an installation serves marks every picture drawn from the old one as
    /// out of date — and goes on serving them, labelled.
    /// </summary>
    /// <remarks>
    /// Both halves are asserted here, in this order, over the same picture. The fresh half is what
    /// makes the stale half mean anything: a reader that answered "stale" to everything would pass
    /// an assertion about the superseded state alone, and would then label a perfectly current
    /// shaded relief as out of date, which teaches everybody to ignore the label.
    /// </remarks>
    [Fact]
    public async Task Replacing_the_ground_marks_a_picture_stale_without_taking_it_away()
    {
        var buildId = await SeedBuildAsync(active: true);
        Raster(buildId, "ground.tif");
        var layerId = await AskForAsync(buildId, new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Slope,
        });

        await ComputeAsync(layerId);

        var current = await ViewAsync(layerId);
        current.ShouldNotBeNull();
        current.Status.ShouldBe(TerrainDerivativeStatus.Ready, current.Message);
        current.Stale.ShouldBeFalse();

        // The ground is replaced: a second build becomes the one the installation serves. At most
        // one build is active at a time, held by a unique index rather than by a handler.
        await SeedBuildAsync(active: true);

        var superseded = await ViewAsync(layerId);
        superseded.ShouldNotBeNull();

        // Said, rather than assumed. Withdrawing the picture would leave a reader with nothing over
        // ground that has probably barely changed; showing it as current is what makes a shaded
        // relief that disagrees with the heights beneath it look like a fault in the cave data.
        superseded.Stale.ShouldBeTrue();
        superseded.Status.ShouldBe(TerrainDerivativeStatus.Ready);
        superseded.Version.ShouldBe(1);

        // And it is still there to serve: the row, and the file it names.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var raster = await db.TerrainDerivativeRasters.AsNoTracking()
            .SingleAsync(r => r.TerrainDerivativeLayerId == layerId);
        File.Exists(raster.Path).ShouldBeTrue(raster.Path);
    }

    /// <summary>
    /// The same picture of the same ground is asked for once, and the database is what says so.
    /// </summary>
    [Fact]
    public async Task The_same_picture_of_the_same_ground_cannot_be_registered_twice()
    {
        var buildId = await SeedBuildAsync();
        var settings = new TerrainDerivativeSettings { Derivative = TerrainDerivative.Aspect };

        await AskForAsync(buildId, settings);

        // Different in a setting a facing map never reads, so it is the same request. Refused by the
        // database rather than by whatever accepts it: two administrators asking at the same instant
        // would each find nothing stored and each queue a run.
        await Should.ThrowAsync<DbUpdateException>(
            AskForAsync(buildId, settings with { SlopeUnit = TerrainSlopeUnit.Percent }));
    }

    /// <summary>
    /// A picture asked for over a build with no elevation prepared fails, saying what went wrong.
    /// </summary>
    [Fact]
    public async Task A_picture_of_ground_with_no_elevation_fails_with_a_reason_on_the_row()
    {
        var buildId = await SeedBuildAsync();
        var layerId = await AskForAsync(buildId, new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Hillshade,
        });

        await Should.ThrowAsync<TerrainBuildException>(ComputeAsync(layerId));

        var view = await ViewAsync(layerId);
        view.ShouldNotBeNull();

        // Not left reading "computing", which is indistinguishable from a run still in progress.
        view.Status.ShouldBe(TerrainDerivativeStatus.Failed);
        view.ErrorCode.ShouldBe(TerrainBuildFailures.NoRasters);
        view.Message.ShouldNotBeNullOrWhiteSpace();
        view.Version.ShouldBe(0);
    }

    private async Task<TerrainDerivativeLayerView?> ViewAsync(Guid layerId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var catalogue = scope.ServiceProvider.GetRequiredService<TerrainDerivativeCatalogue>();
        return await catalogue.FindAsync(layerId, CancellationToken.None);
    }

    /// <summary>Registers a request for one picture, the way an endpoint would.</summary>
    private async Task<Guid> AskForAsync(Guid buildId, TerrainDerivativeSettings settings)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var layer = new TerrainDerivativeLayer
        {
            TerrainBuildId = buildId,
            Derivative = settings.Derivative,
            Settings = TerrainDerivativeRegistry.Describe(settings),
            SettingsHash = TerrainDerivativeRegistry.Fingerprint(settings),
            Name = settings.Derivative.ToString(),
        };

        db.TerrainDerivativeLayers.Add(layer);
        await db.SaveChangesAsync();
        return layer.Id;
    }

    /// <summary>
    /// Runs the job that draws the picture, and answers the id of the run that drew it.
    /// </summary>
    /// <remarks>
    /// Driven directly rather than by queueing and waiting: the database this suite runs against is
    /// shared, so a queued row is claimed by whichever worker in whichever run reaches it first.
    /// </remarks>
    private async Task<long> ComputeAsync(Guid layerId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var job = new ProcessingJob
        {
            Kind = ProcessingJobKinds.TerrainDerivative,
            Payload = JsonSerializer.Serialize(
                new TerrainDerivativePayload(layerId), JsonSerializerOptions.Web),
            Status = ProcessingJobStatus.Running,
        };

        db.ProcessingJobs.Add(job);
        await db.SaveChangesAsync();

        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.TerrainDerivative);

        await handler.ExecuteAsync(job, CancellationToken.None);
        return job.Id;
    }

    private async Task<Guid> SeedBuildAsync(bool active = false)
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
            Status = TerrainBuildStatus.Succeeded,
            Phase = TerrainBuildPhase.Publish,
            IsActive = active,
        };

        db.TerrainBuilds.Add(build);
        await db.SaveChangesAsync();
        seeded.Add(build.Id);
        return build.Id;
    }

    /// <summary>
    /// A prepared elevation raster in this build's prepared directory: the form the preparation
    /// step leaves behind, so a picture is drawn from what it would be drawn from in production.
    /// </summary>
    private string Raster(Guid buildId, string name)
    {
        GdalBase.ConfigureAll();
        var directory = Path.Combine(buildRoot, buildId.ToString("N"), "prepared");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);

        using (var dataset = Gdal.GetDriverByName("GTiff")
            .Create(path, 16, 16, 1, DataType.GDT_Float32, null))
        {
            dataset.SetGeoTransform([West, Pixel, 0, North, 0, -Pixel]);

            using (var reference = new SpatialReference(""))
            {
                reference.ImportFromEPSG(LongitudeLatitude);
                reference.ExportToWkt(out var wkt, null);
                dataset.SetProjection(wkt);
            }

            var band = dataset.GetRasterBand(1);
            band.SetNoDataValue(TerrainRasterPreparation.VoidValue);

            // A slope, so that every picture drawn from it has something to say.
            var pixels = new float[16 * 16];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = 100f + i;
            }

            band.WriteRaster(0, 0, 16, 16, pixels, 16, 16, 0, 0);
            dataset.FlushCache();
        }

        return path;
    }
}
