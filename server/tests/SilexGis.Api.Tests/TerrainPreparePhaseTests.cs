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
using SilexGis.Domain.Geo;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Tests;

/// <summary>
/// The preparation step as the walk actually drives it: a build whose rasters have arrived comes out
/// with its prepared rasters on disk, a build interrupted after that point does not do the work
/// again, and a build whose prepared raster was left half-written does.
///
/// <para>
/// Every one of those goes wrong silently. A step that skips itself because a field said so, or
/// because a file of the right name existed, produces a build that reports success and a mesh with a
/// hole in it drawn as smooth ground. So the rasters here are real ones, built in the test, and what
/// is asserted is what is in the file afterwards.
/// </para>
///
/// <para>
/// The step that obtains rasters is stood in for. What it does is tested where it lives; here it
/// only has to put real elevation data in the directory this step reads, without a network.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TerrainPreparePhaseTests : IAsyncLifetime, IDisposable
{
    /// <summary>The Romanian national projected grid, in metres.</summary>
    private const int Stereo70 = 31700;

    private const int LongitudeLatitude = 4326;

    private readonly SilexGisApiFactory factory;
    private readonly string buildRoot;
    private readonly StubFetchPhase fetch = new();
    private readonly CountingPreparer preparer = new(new GdalTerrainRasterPreparer());

    public TerrainPreparePhaseTests(PostgresFixture postgres)
    {
        buildRoot = Path.Combine(Path.GetTempPath(), $"silexgis-tprep-{Guid.NewGuid():N}");

        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Terrain:BuildRoot"] = buildRoot },
            services =>
            {
                // This class queues nothing, but the shared container's queue is drained by whoever
                // is running one, so it runs no drain of its own either.
                JobWorkers.RemoveFrom(services);

                // The real preparation step, and a stand-in for the one before it. Replaced rather
                // than joined: the walk takes the first implementation claiming a given step, so a
                // stand-in registered alongside the real first step would never be asked.
                foreach (var step in services.Where(s => s.ServiceType == typeof(ITerrainPhase)).ToList())
                {
                    services.Remove(step);
                }

                services.AddSingleton<ITerrainPhase>(fetch);
                services.AddScoped<ITerrainPhase, TerrainPreparePhase>();

                // The real chain, with a counter around it, so "this step was skipped" is a fact
                // about whether the work happened and not an inference from a timestamp.
                foreach (var registered in services
                    .Where(s => s.ServiceType == typeof(ITerrainRasterPreparer)).ToList())
                {
                    services.Remove(registered);
                }

                services.AddSingleton<ITerrainRasterPreparer>(preparer);
            });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.TerrainBuilds.ExecuteDeleteAsync();
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
            // A leftover temporary directory is not worth failing a test run over.
        }
    }

    /// <summary>
    /// The ordinary case, end to end: rasters in two coordinate systems and with a hole in one of
    /// them come out in longitude and latitude with one value for the hole — and stay two rasters,
    /// each keeping its own pixel size.
    /// </summary>
    [Fact]
    public async Task A_build_whose_rasters_have_arrived_comes_out_prepared()
    {
        const double ItsOwnVoidValue = -32768d;

        fetch.Lay = input =>
        {
            Raster(
                Path.Combine(input, "blanket.tif"), LongitudeLatitude,
                originX: 25.0, originY: 46.1, pixelSize: 0.004, width: 32, height: 32,
                sample: static (x, y) => x >= 20 && y >= 20 ? (float)ItsOwnVoidValue : 120f,
                noData: ItsOwnVoidValue);
            Raster(
                Path.Combine(input, "survey.tif"), Stereo70,
                originX: 500_000, originY: 506_000, pixelSize: 30, width: 32, height: 32,
                sample: static (_, _) => 800f);
        };

        var buildId = await SeedAsync();
        await RunAsync(buildId);

        var build = await ReadAsync(buildId);
        build.Status.ShouldBe(TerrainBuildStatus.Succeeded, build.ErrorCode ?? build.Message ?? "");
        build.Phase.ShouldBe(TerrainBuildPhase.Prepare);

        // The step's band runs to forty-five, and a build that got all the way through the last
        // step there is an implementation for is reported at the top of that band.
        build.Progress.ShouldBe(45);

        // Two rasters in, two rasters out. Merged into one they would share a single grid, and the
        // survey's third-of-a-thousandth-degree pixel would either be lost or be forced on the
        // blanket's ground, where there is no such detail to put in it.
        Directory.GetFiles(PreparedDirectory(buildId))
            .Select(Path.GetFileName)
            .ShouldBe(["blanket.tif", "survey.tif"], ignoreOrder: true);

        foreach (var raster in Directory.GetFiles(PreparedDirectory(buildId)))
        {
            AuthorityOf(raster).ShouldBe(LongitudeLatitude.ToString());
            DeclaredVoidValue(raster).ShouldBe(TerrainRasterPreparation.VoidValue);
        }

        // Near enough its own rather than exactly its own: the blanket reaches past the rectangle
        // and is cut down to it, and a whole number of pixels has to fit the piece that is left.
        // What matters is that it is nowhere near the survey's, which is more than ten times finer.
        PixelSizeOf(PreparedPath(buildId, "blanket.tif")).ShouldBe(0.004, 0.0005);
        PixelSizeOf(PreparedPath(buildId, "survey.tif")).ShouldBeLessThan(0.001);

        // Kept, not swept: the working directory a run is given goes away with the run and this
        // does not, because it is what makes meshing the same ground again cheap.
        Directory.Exists(Path.Combine(buildRoot, buildId.ToString("N"), "scratch")).ShouldBeFalse();

        // Beside the sources rather than among them, or the next run would read it back in as one.
        Directory.GetFiles(Path.Combine(buildRoot, buildId.ToString("N"), "input"))
            .Select(Path.GetFileName)
            .ShouldBe(["blanket.tif", "survey.tif"], ignoreOrder: true);

        // What the step did is in the build's own account of itself, in words rather than a code.
        build.LogTail.ShouldNotBeNull();
        build.LogTail.ShouldContain("Prepared blanket.tif");
        build.LogTail.ShouldContain("Prepared survey.tif");
    }

    /// <summary>
    /// A raster covering none of the rectangle the build was drawn over is left out of what is
    /// prepared, and the build says how many it left out.
    /// </summary>
    /// <remarks>
    /// The rectangle is the only thing bounding this work. A directory an operator lists can hold a
    /// country's rasters — the step that gathers them copies every one it finds — so without the
    /// rectangle reaching this step, a box over one hillside prepares a country, at whatever pixel
    /// size that country was surveyed at, and keeps it for the life of the build.
    /// </remarks>
    [Fact]
    public async Task Rasters_covering_none_of_the_rectangle_are_left_out()
    {
        fetch.Lay = input =>
        {
            Raster(
                Path.Combine(input, "here.tif"), LongitudeLatitude,
                originX: 25.0, originY: 46.1, pixelSize: 0.001, width: 48, height: 48,
                sample: static (_, _) => 700f);
            Raster(
                Path.Combine(input, "next-country.tif"), LongitudeLatitude,
                originX: 30.0, originY: 46.1, pixelSize: 0.001, width: 48, height: 48,
                sample: static (_, _) => 900f);
        };

        var buildId = await SeedAsync();
        await RunAsync(buildId);

        var build = await ReadAsync(buildId);
        build.Status.ShouldBe(TerrainBuildStatus.Succeeded, build.ErrorCode ?? build.Message ?? "");

        Directory.GetFiles(PreparedDirectory(buildId))
            .Select(Path.GetFileName)
            .ShouldBe(["here.tif"]);

        build.LogTail.ShouldNotBeNull();
        build.LogTail.ShouldContain("Prepared 1 of 2 raster");
    }

    /// <summary>
    /// A build whose host died after the rasters were prepared picks up where it was rather than
    /// preparing them again.
    /// </summary>
    /// <remarks>
    /// Every row a dead process left running is put back on the queue when the process starts again,
    /// so this is ordinary rather than exceptional, and the work being skipped is an hour of it.
    /// </remarks>
    [Fact]
    public async Task A_build_interrupted_after_preparing_does_not_prepare_again()
    {
        fetch.Lay = input => Raster(
            Path.Combine(input, "ground.tif"), LongitudeLatitude,
            originX: 25.0, originY: 46.1, pixelSize: 0.001, width: 48, height: 48,
            sample: static (_, _) => 640f);

        var buildId = await SeedAsync();
        await RunAsync(buildId);
        preparer.Calls.ShouldBe(1);

        var prepared = PreparedPath(buildId, "ground.tif");
        var written = File.GetLastWriteTimeUtc(prepared);
        var length = new FileInfo(prepared).Length;

        await InterruptAsync(buildId);
        await RunAsync(buildId);

        // Skipped, not redone. Asserted on whether the work happened rather than on the file's
        // timestamp, because a second run producing an identical file would look the same.
        preparer.Calls.ShouldBe(1);
        File.GetLastWriteTimeUtc(prepared).ShouldBe(written);
        new FileInfo(prepared).Length.ShouldBe(length);

        var build = await ReadAsync(buildId);
        build.Status.ShouldBe(TerrainBuildStatus.Succeeded, build.ErrorCode ?? build.Message ?? "");
        build.Phase.ShouldBe(TerrainBuildPhase.Prepare);
    }

    /// <summary>
    /// A prepared raster that was cut short is not taken for a finished one, and neither is one that
    /// is not a raster at all.
    /// </summary>
    /// <remarks>
    /// This is the failure the whole check exists for. A file of the right name in the right place
    /// with its tail missing opens, measures and describes itself correctly; the only thing wrong
    /// with it is that part of the ground it claims to cover is not in it, and that part is meshed
    /// as smooth terrain with nothing anywhere reporting a problem. The positive case is asserted in
    /// the same test, because a check tightened until it refuses everything would pass a test that
    /// only asked whether something was refused.
    /// </remarks>
    [Fact]
    public async Task A_prepared_raster_cut_short_is_not_taken_for_a_finished_one()
    {
        fetch.Lay = input => Raster(
            Path.Combine(input, "ground.tif"), LongitudeLatitude,
            originX: 25.0, originY: 46.1, pixelSize: 0.0004, width: 256, height: 256,
            sample: static (x, y) => 400f + x + y);

        var buildId = await SeedAsync();
        await RunAsync(buildId);
        preparer.Calls.ShouldBe(1);

        var prepared = PreparedPath(buildId, "ground.tif");
        var whole = new FileInfo(prepared).Length;

        // The positive case first, so what follows means something: run again untouched and the
        // work is skipped, because the file that is there is whole.
        await InterruptAsync(buildId);
        await RunAsync(buildId);
        preparer.Calls.ShouldBe(1);

        // Now the same file with its tail taken off — what a process killed mid-write leaves.
        using (var truncate = new FileStream(prepared, FileMode.Open, FileAccess.Write))
        {
            truncate.SetLength(whole / 3);
        }

        await InterruptAsync(buildId);
        await RunAsync(buildId);

        preparer.Calls.ShouldBe(2);
        new FileInfo(prepared).Length.ShouldBe(whole);
        AuthorityOf(prepared).ShouldBe(LongitudeLatitude.ToString());
        (await ReadAsync(buildId)).Status.ShouldBe(TerrainBuildStatus.Succeeded);

        // And bytes that are not a raster at all, which is what a disk that filled up leaves.
        await File.WriteAllBytesAsync(prepared, [0x49, 0x49, 0x2a, 0x00, 0x08, 0x00, 0x00, 0x00]);

        await InterruptAsync(buildId);
        await RunAsync(buildId);

        preparer.Calls.ShouldBe(3);
        new FileInfo(prepared).Length.ShouldBe(whole);
        (await ReadAsync(buildId)).Status.ShouldBe(TerrainBuildStatus.Succeeded);
    }

    /// <summary>
    /// A file the raster tools will not open stops the build with a reason that names it, the tools'
    /// own words kept apart from that reason, and nothing of unbounded length thrown at the queue.
    /// </summary>
    /// <remarks>
    /// The three places a failure lands are three different sizes. The build row's code is a short
    /// constant because its column is a hundred characters; the log tail is where a native library's
    /// account of itself goes, truncated to what fits; and what is thrown for the queue to store
    /// carries neither, because the queue would refuse an over-long message <i>while recording the
    /// failure</i> and leave the row reading as running for ever.
    /// </remarks>
    [Fact]
    public async Task A_file_the_raster_tools_refuse_stops_the_build_and_says_which_file()
    {
        fetch.Lay = input => File.WriteAllBytes(
            Path.Combine(input, "corrupt.tif"), [0x49, 0x49, 0x2a, 0x00, 0x08, 0x00, 0x00, 0x00]);

        var buildId = await SeedAsync();
        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => RunAsync(buildId));

        var build = await ReadAsync(buildId);
        build.Status.ShouldBe(TerrainBuildStatus.Failed);
        build.Phase.ShouldBe(TerrainBuildPhase.Prepare);
        build.ErrorCode.ShouldBe(TerrainBuildFailures.SourceUnreadable);

        // A sentence naming the file, of a length the row can hold.
        build.Message.ShouldNotBeNull();
        build.Message.ShouldContain("corrupt.tif");
        build.Message.Length.ShouldBeLessThanOrEqualTo(500);

        // What the library said, which is the part that is worth reading and the part that has no
        // length anybody controls.
        build.LogTail.ShouldNotBeNullOrWhiteSpace();
        build.LogTail.Length.ShouldBeLessThanOrEqualTo(8000);

        // The queue gets the build and the code and nothing else.
        thrown.Message.ShouldContain(buildId.ToString());
        thrown.Message.ShouldContain(TerrainBuildFailures.SourceUnreadable);
        thrown.Message.Length.ShouldBeLessThan(200);

        // The positive half: the same build, the same host, a real raster this time. Without it a
        // step that refused everything would satisfy everything above.
        fetch.Lay = input =>
        {
            File.Delete(Path.Combine(input, "corrupt.tif"));
            Raster(
                Path.Combine(input, "ground.tif"), LongitudeLatitude,
                originX: 25.0, originY: 46.1, pixelSize: 0.001, width: 32, height: 32,
                sample: static (_, _) => 500f);
        };

        var second = await SeedAsync();
        await RunAsync(second);

        var built = await ReadAsync(second);
        built.Status.ShouldBe(TerrainBuildStatus.Succeeded, built.ErrorCode ?? built.Message ?? "");
        built.Phase.ShouldBe(TerrainBuildPhase.Prepare);
        AuthorityOf(PreparedPath(second, "ground.tif")).ShouldBe(LongitudeLatitude.ToString());
    }

    /// <summary>
    /// A build whose rasters never arrived stops here rather than going on to mesh nothing.
    /// </summary>
    [Fact]
    public async Task A_build_with_nothing_in_its_input_directory_stops_at_this_step()
    {
        // Leaves behind only what an interrupted transfer does, which is not a raster and must not
        // be read as one.
        fetch.Lay = input => File.WriteAllText(
            Path.Combine(input, "ground.tif" + TerrainRasterFiles.PartialSuffix), "half a file");

        var buildId = await SeedAsync();
        await Should.ThrowAsync<InvalidOperationException>(() => RunAsync(buildId));

        var build = await ReadAsync(buildId);
        build.Status.ShouldBe(TerrainBuildStatus.Failed);
        build.Phase.ShouldBe(TerrainBuildPhase.Prepare);
        build.ErrorCode.ShouldBe(TerrainBuildFailures.NoRasters);
        Directory.GetFiles(PreparedDirectory(buildId)).ShouldBeEmpty();
    }

    private string PreparedDirectory(Guid buildId) =>
        Path.Combine(buildRoot, buildId.ToString("N"), "prepared");

    private string PreparedPath(Guid buildId, string name) =>
        Path.Combine(PreparedDirectory(buildId), name);

    private async Task<Guid> SeedAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var build = new TerrainBuild
        {
            Extent = new GeometryFactory(new PrecisionModel(), 4326).CreatePolygon(
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
            HeightDatum = TerrainHeightDatum.Orthometric,
        };

        db.TerrainBuilds.Add(build);
        await db.SaveChangesAsync();
        return build.Id;
    }

    /// <summary>
    /// Runs the handler directly rather than waiting for a worker, so what is asserted is what the
    /// handler did and not how quickly something got to it.
    /// </summary>
    private async Task RunAsync(Guid buildId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.TerrainBuild);

        await handler.ExecuteAsync(
            new ProcessingJob
            {
                Kind = ProcessingJobKinds.TerrainBuild,
                Attempts = 1,
                Payload = JsonSerializer.Serialize(
                    new TerrainBuildPayload(buildId), JsonSerializerOptions.Web),
            },
            CancellationToken.None);
    }

    /// <summary>Puts a build back the way a process that died halfway through would leave it.</summary>
    /// <remarks>
    /// The attempt count stays at one: a row carrying a recorded failure and handed over a second
    /// time is stopped rather than started again, and what is under test here is a run that was
    /// interrupted rather than one that failed.
    /// </remarks>
    private async Task InterruptAsync(Guid buildId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.TerrainBuilds.Where(b => b.Id == buildId).ExecuteUpdateAsync(s => s
            .SetProperty(b => b.Status, TerrainBuildStatus.Running)
            .SetProperty(b => b.ErrorCode, (string?)null)
            .SetProperty(b => b.FinishedAt, (DateTimeOffset?)null));
    }

    private async Task<TerrainBuild> ReadAsync(Guid buildId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TerrainBuilds.AsNoTracking().FirstAsync(b => b.Id == buildId);
    }

    /// <summary>A single-band elevation raster on disk, with an optional value meaning nothing-known.</summary>
    private static void Raster(
        string path,
        int epsg,
        double originX,
        double originY,
        double pixelSize,
        int width,
        int height,
        Func<int, int, float> sample,
        double? noData = null)
    {
        GdalBase.ConfigureAll();
        var driver = Gdal.GetDriverByName("GTiff");

        using var dataset = driver.Create(path, width, height, 1, DataType.GDT_Float32, null);
        dataset.SetGeoTransform([originX, pixelSize, 0, originY, 0, -pixelSize]);

        using (var reference = new SpatialReference(""))
        {
            reference.ImportFromEPSG(epsg);
            reference.ExportToWkt(out var wkt, null);
            dataset.SetProjection(wkt);
        }

        var band = dataset.GetRasterBand(1);
        if (noData is { } value)
        {
            band.SetNoDataValue(value);
        }

        var pixels = new float[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = sample(x, y);
            }
        }

        band.WriteRaster(0, 0, width, height, pixels, width, height, 0, 0);
        dataset.FlushCache();
    }

    private static string? AuthorityOf(string path)
    {
        GdalBase.ConfigureAll();
        using var dataset = Gdal.Open(path, Access.GA_ReadOnly);
        using var reference = new SpatialReference(dataset.GetProjection());
        return reference.GetAuthorityCode(null);
    }

    private static double DeclaredVoidValue(string path)
    {
        GdalBase.ConfigureAll();
        using var dataset = Gdal.Open(path, Access.GA_ReadOnly);
        dataset.GetRasterBand(1).GetNoDataValue(out var value, out var declared);
        declared.ShouldNotBe(0);
        return value;
    }

    private static double PixelSizeOf(string path)
    {
        GdalBase.ConfigureAll();
        using var dataset = Gdal.Open(path, Access.GA_ReadOnly);
        var geoTransform = new double[6];
        dataset.GetGeoTransform(geoTransform);
        return Math.Abs(geoTransform[1]);
    }

    /// <summary>
    /// Stands in for the step that obtains rasters, laying down whatever the test says arrived.
    /// </summary>
    /// <remarks>
    /// It answers "am I already done" from the disk, the way the real steps must, so that a build
    /// run twice does not get its input written twice — and so nothing in this class is proved by a
    /// stand-in behaving in a way no real step does.
    /// </remarks>
    private sealed class StubFetchPhase : ITerrainPhase
    {
        public TerrainBuildPhase Phase => TerrainBuildPhase.Fetch;

        /// <summary>What this build's rasters are, given the directory they belong in.</summary>
        public Action<string> Lay { get; set; } = _ => { };

        public Task<bool> IsAlreadyDoneAsync(TerrainBuildContext context, CancellationToken ct) =>
            Task.FromResult(File.Exists(MarkerOf(context)));

        public async Task RunAsync(TerrainBuildContext context, CancellationToken ct)
        {
            Lay(context.Directories.Input);
            await File.WriteAllTextAsync(MarkerOf(context), "laid", ct);
        }

        private static string MarkerOf(TerrainBuildContext context) =>
            Path.Combine(context.Directories.Root, "fetched.marker");
    }

    /// <summary>
    /// The real raster chain with a count of how many times it was actually asked to do the work.
    /// </summary>
    /// <remarks>
    /// Counting rather than replacing, because what these tests are about is whether a step decided
    /// to run — and a stand-in that did no raster work would leave nothing on disk for the decision
    /// to be made about.
    /// </remarks>
    private sealed class CountingPreparer(ITerrainRasterPreparer inner) : ITerrainRasterPreparer
    {
        public int Calls { get; private set; }

        public IReadOnlyList<PreparedTerrainRaster> Prepare(
            TerrainRasterPrepareRequest request, CancellationToken ct)
        {
            Calls++;
            return inner.Prepare(request, ct);
        }

        public IReadOnlyList<PreparedTerrainRaster>? DescribePrepared(
            TerrainRasterPrepareRequest request) => inner.DescribePrepared(request);

        public PreparedTerrainRaster? Describe(string path) => inner.Describe(path);
    }
}
