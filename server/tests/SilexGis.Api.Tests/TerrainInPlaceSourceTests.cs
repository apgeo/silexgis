// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OSGeo.GDAL;
using OSGeo.OSR;
using Shouldly;
using SilexGis.Api.Features.Terrain;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Tests;

/// <summary>
/// A directory on the server is read where it lies: it is never copied into a build, and nothing
/// the application does to a build — running it, calling it off, running it again, deleting it, or
/// sweeping uploads — ever writes, renames or deletes anything in it.
///
/// <para>
/// The chain is the real one from end to end — obtaining, preparing with the raster library,
/// meshing, checking and publishing — with only the program on the far side of the meshing step's
/// directory played by the test. The rasters are real ones, so the library really opens and closes
/// them, which is the moment it would write a sidecar beside them if anything let it.
/// </para>
/// <para>
/// The application's own terrain directories are deliberately placed <i>inside</i> the directory
/// the operator listed as readable. That is a layout an operator can arrive at, and it is the one
/// in which reading a directory in place could reach this application's own work, or this
/// application's deletions could reach the operator's files.
/// </para>
/// </summary>
public sealed class TerrainInPlaceSourceTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly string scratch;
    private readonly string importRoot;
    private readonly string lidar;
    private readonly string buildRoot;
    private readonly string spoolRoot;
    private readonly string publishRoot;
    private readonly SilexGisApiFactory factory;

    private HttpClient administrator = null!;
    private Guid administratorId;

    public TerrainInPlaceSourceTests(PostgresFixture postgres)
    {
        scratch = Path.Combine(TestScratch.Root, $"silexgis-tinplace-{Guid.NewGuid():N}");
        importRoot = Path.Combine(scratch, "import");
        lidar = Path.Combine(importRoot, "lidar");
        buildRoot = Path.Combine(importRoot, "app", "builds");
        spoolRoot = Path.Combine(importRoot, "app", "spool");
        publishRoot = Path.Combine(importRoot, "app", "published");
        Directory.CreateDirectory(lidar);
        Directory.CreateDirectory(buildRoot);

        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Terrain:BuildRoot"] = buildRoot,
                ["Terrain:SpoolRoot"] = spoolRoot,
                ["Terrain:PublishRoot"] = publishRoot,
                ["Terrain:BakeEnabled"] = "true",
                ["Terrain:BakePickupSeconds"] = "5",
                ["Files:ImportRoots:0"] = importRoot,
            },
            services => JobWorkers.RemoveFrom(services),
            ownHost: true);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        administratorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"tip-adm-{suffix}@t.local");
        administrator = await AuthHelper.BearerClientAsync(factory, $"tip-adm-{suffix}@t.local");

        GdalRuntime.Configure();

        // A regional raster reaching far beyond the rectangle, which is what makes the size of the
        // build tell its own disk apart from the operator's, and a second in another format.
        GeoTiff(Path.Combine(lidar, "region.tif"), west: 24.8, north: 46.3, pixel: 0.0005, size: 1000);
        Directory.CreateDirectory(Path.Combine(lidar, "sub"));
        AsciiGrid(Path.Combine(lidar, "sub", "island.asc"), west: 25.01, north: 46.04, pixel: 0.0002, size: 50);
    }

    /// <summary>
    /// The whole life of a build over an operator's directory, with the directory compared, file by
    /// file — name, length and time — after every act: nothing appears in it, nothing leaves it and
    /// nothing in it changes. The build's own disk, meanwhile, holds no copy of it and does not count
    /// it.
    /// </summary>
    [Fact]
    public async Task A_server_directory_is_read_in_place_and_comes_back_untouched_through_build_cancel_retry_sweep_and_delete()
    {
        var before = Snapshot(lidar);

        var id = await SubmitAsync(lidar, 25.00, 46.00);
        var spool = Path.Combine(spoolRoot, id.ToString("N"));

        // Called off while the tile maker is working.
        await using (new FakeBakeWorker(spoolRoot, (directory, request) =>
        {
            Pyramid(request["output"]);
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (!File.Exists(Path.Combine(directory, "cancel")) && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }

            Answer(directory, "cancelled", exit: 143);
        }))
        {
            var run = Task.Run(() => RunAsync(id));
            await UntilAsync(() => File.Exists(Path.Combine(spool, "started")), "the bake to be taken");

            var cancelled = await administrator.PostAsync($"/api/v1/terrain/builds/{id}/cancel", null);
            cancelled.StatusCode.ShouldBe(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
            await run.WaitAsync(TimeSpan.FromSeconds(90));
        }

        var stopped = await ReadAsync(id);
        stopped.ErrorCode.ShouldBe(TerrainBuildFailures.Cancelled, stopped.Message);
        Snapshot(lidar).ShouldBe(before);

        // Nothing was copied: the build's input holds no raster, its record names the operator's
        // files where they are, and what it prepared is its own.
        var root = Path.Combine(buildRoot, id.ToString("N"));
        Directory.GetFiles(Path.Combine(root, "input")).ShouldBeEmpty();
        File.Exists(Path.Combine(root, TerrainInPlaceSources.RecordFileName)).ShouldBeTrue();
        Directory.GetFiles(Path.Combine(root, "prepared"), "*.tif").Length.ShouldBe(2);

        // Run again, to the end.
        var retried = await administrator.PostAsync($"/api/v1/terrain/builds/{id}/retry", null);
        retried.StatusCode.ShouldBe(HttpStatusCode.OK, await retried.Content.ReadAsStringAsync());

        await using (new FakeBakeWorker(spoolRoot, (directory, request) =>
        {
            Pyramid(request["output"]);
            Answer(directory, "succeeded", exit: 0);
        }))
        {
            await RunAsync(id);
        }

        var finished = await ReadAsync(id);
        finished.Status.ShouldBe(TerrainBuildStatus.Succeeded, finished.ErrorCode + " " + finished.Message);
        Snapshot(lidar).ShouldBe(before);

        // What the build is responsible for on disk is its own: well under the one regional raster
        // it read, which is four megabytes on its own and was cut down to the rectangle.
        var regional = new FileInfo(Path.Combine(lidar, "region.tif")).Length;
        finished.SizeBytes.ShouldNotBeNull().ShouldBeGreaterThan(0);
        finished.SizeBytes.Value.ShouldBeLessThan(regional / 4);

        // The sweep of uploads nobody names, at its most eager, takes nothing of the operator's.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<TerrainUploads>()
                .SweepAbandoned(DateTimeOffset.UtcNow.AddYears(1), new HashSet<string>());
        }

        Snapshot(lidar).ShouldBe(before);

        // Deleted — which takes everything the build owns, and only that.
        (await administrator.DeleteAsync($"/api/v1/terrain/builds/{id}/active")).Dispose();
        var deleted = await administrator.DeleteAsync($"/api/v1/terrain/builds/{id}");
        deleted.IsSuccessStatusCode.ShouldBeTrue(await deleted.Content.ReadAsStringAsync());
        Directory.Exists(root).ShouldBeFalse();
        Snapshot(lidar).ShouldBe(before);
    }

    /// <summary>
    /// A directory inside the ones this application deletes in is refused, and a directory that
    /// contains them is read without them.
    /// </summary>
    [Fact]
    public async Task The_applications_own_terrain_directories_are_never_read_as_the_operators()
    {
        // Inside the build root: refused with the answer any unreadable directory gets.
        var inside = Path.Combine(buildRoot, "uploads");
        Directory.CreateDirectory(inside);
        File.WriteAllBytes(Path.Combine(inside, "stray.tif"), [0x49, 0x49, 0x2a, 0x00]);

        var refused = await administrator.PostAsJsonAsync("/api/v1/terrain/builds", Request(inside, 25.10, 46.10));
        var refusedBody = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, refusedBody);
        JsonDocument.Parse(refusedBody).RootElement.GetProperty("code").GetString()
            .ShouldBe(TerrainBuildEndpoints.DirectoryUnavailableCode);

        // The whole import root, which holds the operator's survey and this application's own
        // directories: only the survey is read.
        var before = Snapshot(lidar);
        var id = await SubmitAsync(importRoot, 25.00, 46.10);
        await RunAsync(id);

        var record = JsonSerializer.Deserialize<TerrainInPlaceRecord>(
            await File.ReadAllTextAsync(Path.Combine(buildRoot, id.ToString("N"), TerrainInPlaceSources.RecordFileName)),
            JsonSerializerOptions.Web)!;
        record.Rasters.Select(r => Path.GetRelativePath(importRoot, r.Path)).Order(StringComparer.Ordinal)
            .ShouldBe([Path.Combine("lidar", "region.tif"), Path.Combine("lidar", "sub", "island.asc")]);
        Snapshot(lidar).ShouldBe(before);
    }

    private static TerrainBuildSubmitRequest Request(string directory, double west, double south) =>
        new(west, south, west + 0.05, south + 0.05, 13, null, null, false,
            [new(TerrainBuildSourceKind.ServerDirectory, directory, "The operator's own survey", null)]);

    private async Task<Guid> SubmitAsync(string directory, double west, double south)
    {
        var response = await administrator.PostAsJsonAsync("/api/v1/terrain/builds", Request(directory, west, south));
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    private async Task RunAsync(Guid buildId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.TerrainBuild);

        try
        {
            await handler.ExecuteAsync(
                new ProcessingJob
                {
                    Kind = ProcessingJobKinds.TerrainBuild,
                    Attempts = 1,
                    RequestedBy = administratorId,
                    Payload = JsonSerializer.Serialize(new TerrainBuildPayload(buildId), JsonSerializerOptions.Web),
                },
                CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // A run that stops throws so the queue row goes red; the build row is what is read here.
        }
    }

    private async Task<TerrainBuild> ReadAsync(Guid buildId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TerrainBuilds.AsNoTracking().FirstAsync(b => b.Id == buildId);
    }

    /// <summary>Every entry under a directory, with its length and time, hidden ones included.</summary>
    private static List<string> Snapshot(string directory) =>
        [.. Directory.EnumerateFileSystemEntries(directory, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = 0,
            })
            .Select(entry =>
            {
                var info = new FileInfo(entry);
                var length = info.Exists ? info.Length : -1;
                return $"{Path.GetRelativePath(directory, entry)}|{length}|{File.GetLastWriteTimeUtc(entry).Ticks}";
            })
            .Order(StringComparer.Ordinal)];

    private static async Task UntilAsync(Func<bool> check, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (check())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Timed out waiting for {what}.");
    }

    private static void Answer(string directory, string status, int? exit = null)
    {
        var partial = Path.Combine(directory, "result.part");
        File.WriteAllText(partial, $"status={status}\nexit={exit}\n");
        File.Move(partial, Path.Combine(directory, "result"));
    }

    /// <summary>
    /// A pyramid the check accepts: tiles one past the advertised rectangle on the far sides, as a
    /// real bake writes them, each with a header describing ground.
    /// </summary>
    private static void Pyramid(string tiles)
    {
        const int columns = 2;
        const int rows = 1;

        var manifest = new JsonObject
        {
            ["tilejson"] = "2.1.0",
            ["format"] = "quantized-mesh-1.0",
            ["scheme"] = "tms",
            ["tiles"] = new JsonArray("{z}/{x}/{y}.terrain?v={version}"),
            ["available"] = new JsonArray(new JsonArray(new JsonObject
            {
                ["startX"] = 0,
                ["startY"] = 0,
                ["endX"] = columns - 1,
                ["endY"] = rows - 1,
            })),
        };

        Directory.CreateDirectory(tiles);
        File.WriteAllText(Path.Combine(tiles, "layer.json"), manifest.ToJsonString());

        for (var x = 0; x <= columns; x++)
        {
            for (var y = 0; y <= rows; y++)
            {
                const uint vertices = 12;
                var tile = new byte[92 + (vertices * 6)];
                BinaryPrimitives.WriteDoubleLittleEndian(tile.AsSpan(0), 4_200_000.5);
                BinaryPrimitives.WriteDoubleLittleEndian(tile.AsSpan(8), 1_700_000.25);
                BinaryPrimitives.WriteDoubleLittleEndian(tile.AsSpan(16), 4_600_000.75);
                BinaryPrimitives.WriteSingleLittleEndian(tile.AsSpan(24), 210.5f);
                BinaryPrimitives.WriteSingleLittleEndian(tile.AsSpan(28), 1840.25f);
                BinaryPrimitives.WriteUInt32LittleEndian(tile.AsSpan(88), vertices);

                var path = Path.Combine(tiles, "0", x.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    y.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".terrain");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, tile);
            }
        }
    }

    private static void GeoTiff(string path, double west, double north, double pixel, int size)
    {
        using var dataset = Gdal.GetDriverByName("GTiff").Create(path, size, size, 1, DataType.GDT_Float32, null);
        Fill(dataset, west, north, pixel, size);
    }

    private static void AsciiGrid(string path, double west, double north, double pixel, int size)
    {
        // The grid format can only be written as a copy of another dataset.
        using var memory = Gdal.GetDriverByName("MEM").Create("", size, size, 1, DataType.GDT_Float32, null);
        Fill(memory, west, north, pixel, size);
        using var written = Gdal.GetDriverByName("AAIGrid").CreateCopy(path, memory, 0, null, null, null);
        written.FlushCache();
    }

    private static void Fill(Dataset dataset, double west, double north, double pixel, int size)
    {
        dataset.SetGeoTransform([west, pixel, 0, north, 0, -pixel]);
        using (var reference = new SpatialReference(""))
        {
            reference.ImportFromEPSG(4326);
            reference.ExportToWkt(out var wkt, null);
            dataset.SetProjection(wkt);
        }

        var pixels = new float[size * size];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = 400f + (i % size) + (i / size * 0.5f);
        }

        dataset.GetRasterBand(1).WriteRaster(0, 0, size, size, pixels, size, size, 0, 0);
        dataset.FlushCache();
    }

    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.ProcessingJobs.Where(j => j.Kind == ProcessingJobKinds.TerrainBuild).ExecuteDeleteAsync();
        await db.TerrainBuilds.ExecuteDeleteAsync();
    }

    public void Dispose()
    {
        administrator?.Dispose();
        factory.Dispose();

        try
        {
            if (Directory.Exists(scratch))
            {
                Directory.Delete(scratch, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary directory is not worth failing a test run over.
        }
    }
}
