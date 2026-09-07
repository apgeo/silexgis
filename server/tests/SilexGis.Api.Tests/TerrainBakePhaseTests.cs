// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
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
/// The meshing step as the walk drives it: a build whose rasters are ready asks for a pyramid on a
/// directory, waits, and is told one of the several different things that can come back.
///
/// <para>
/// The program that makes tiles is stood in for, by writing the answer it would write. That is the
/// whole point of handing the work over through a directory rather than over a connection — the far
/// side is a file, so a test can be the far side, and every answer it can give including the ones
/// that never happen on a healthy machine can be tried without a container, an image or a minute of
/// waiting.
/// </para>
///
/// <para>
/// Nearly everything here fails invisibly if it is got wrong. A missing depth is a bake several
/// levels deeper than anyone asked for; a memory warning gone unread is a complete pyramid of ground
/// at the wrong resolution; and a half-written pyramid left on disk is found by the next attempt,
/// believed, and published.
/// </para>
/// </summary>
public sealed class TerrainBakePhaseTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture postgres;
    private readonly SilexGisApiFactory factory;
    private readonly string buildRoot;
    private readonly string spoolRoot;

    public TerrainBakePhaseTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
        var scratch = Path.Combine(Path.GetTempPath(), $"silexgis-tbake-{Guid.NewGuid():N}");
        buildRoot = Path.Combine(scratch, "builds");
        spoolRoot = Path.Combine(scratch, "spool");
        factory = Installation(bakes: true);
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
            var scratch = Path.GetDirectoryName(buildRoot);
            if (scratch is not null && Directory.Exists(scratch))
            {
                Directory.Delete(scratch, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary directory is not worth failing a test run over.
        }
    }

    /// <summary>
    /// The ordinary case: the request says exactly what to make and how deep, and what comes back
    /// is a pyramid.
    /// </summary>
    [Fact]
    public async Task A_build_whose_rasters_are_ready_is_meshed_to_the_depth_it_asked_for()
    {
        var buildId = await SeedAsync();
        var seen = new ConcurrentDictionary<string, string>();

        await using var worker = new FakeBakeWorker(spoolRoot, (directory, request) =>
        {
            foreach (var (key, value) in request)
            {
                seen[key] = value;
            }

            Pyramid(request["output"]);
            Say(directory, "Generating tiles for level 13", "Done");
            Answer(directory, "succeeded", exit: 0);
        });

        await RunAsync(buildId);

        var build = await ReadAsync(buildId);
        build.Status.ShouldBe(TerrainBuildStatus.Succeeded, build.ErrorCode ?? build.Message ?? "");
        build.Phase.ShouldBe(TerrainBuildPhase.Bake);

        // Always stated. Left to work it out for itself the tile maker takes its depth from the
        // finest raster it was given, and the price of a bake tracks the number of tiles.
        seen["maxDepth"].ShouldBe("13");
        seen["datum"].ShouldBe("orthometric");
        seen["input"].ShouldBe(Path.Combine(buildRoot, buildId.ToString("N"), "prepared"));
        seen["output"].ShouldBe(Path.Combine(buildRoot, buildId.ToString("N"), "tiles"));

        File.Exists(Path.Combine(TilesOf(buildId), "layer.json")).ShouldBeTrue();
    }

    /// <summary>
    /// An installation with nothing to make tiles with is an ordinary installation, and is told
    /// apart from every other reason a bake does not happen — the same build, on a machine that has
    /// one, is meshed.
    /// </summary>
    [Fact]
    public async Task An_installation_with_no_tile_maker_says_so_and_the_same_build_bakes_where_there_is_one()
    {
        var buildId = await SeedAsync();

        await using (var bare = Installation(bakes: false))
        {
            await RunAsync(buildId, bare);
        }

        var refused = await ReadAsync(buildId);
        refused.Status.ShouldBe(TerrainBuildStatus.Failed);
        refused.ErrorCode.ShouldBe(TerrainBuildFailures.BakeUnavailable);

        // Nothing was asked of anybody: a request left in the directory would be picked up whenever
        // a worker was eventually started, hours later, against a build that had already been told
        // it could not be built.
        Directory.Exists(Path.Combine(spoolRoot, buildId.ToString("N"))).ShouldBeFalse();

        await RewindAsync(buildId);
        await using var worker = new FakeBakeWorker(spoolRoot, (directory, request) =>
        {
            Pyramid(request["output"]);
            Answer(directory, "succeeded", exit: 0);
        });

        await RunAsync(buildId);

        var built = await ReadAsync(buildId);
        built.Status.ShouldBe(TerrainBuildStatus.Succeeded, built.ErrorCode ?? "");
        built.Phase.ShouldBe(TerrainBuildPhase.Bake);
    }

    /// <summary>
    /// A tile maker that is configured and does not answer is a third thing again: something is
    /// deployed and is not running, which is worth trying again, and is not the build's fault.
    /// </summary>
    [Fact]
    public async Task A_tile_maker_that_never_takes_the_work_is_not_the_same_as_having_none()
    {
        var buildId = await SeedAsync();

        // No worker at all this time. The request is written and nothing ever claims it.
        await RunAsync(buildId);

        var build = await ReadAsync(buildId);
        build.Status.ShouldBe(TerrainBuildStatus.Failed);
        build.ErrorCode.ShouldBe(TerrainBuildFailures.BakeWorkerSilent);
        build.ErrorCode.ShouldNotBe(TerrainBuildFailures.BakeUnavailable);

        File.Exists(Path.Combine(spoolRoot, buildId.ToString("N"), "request")).ShouldBeTrue();
    }

    /// <summary>
    /// A refusal is this application's own mistake — the two sides agree on a small vocabulary and
    /// something outside it was asked for — so it is recorded as its own thing rather than as a
    /// failure of the data.
    /// </summary>
    [Fact]
    public async Task A_request_the_tile_maker_will_not_accept_is_recorded_as_ours()
    {
        var buildId = await SeedAsync();

        await using var worker = new FakeBakeWorker(spoolRoot, (directory, _) =>
            Answer(directory, "refused", reason: "input is not a directory inside /data/terrain"));

        await RunAsync(buildId);

        var build = await ReadAsync(buildId);
        build.Status.ShouldBe(TerrainBuildStatus.Failed);
        build.ErrorCode.ShouldBe(TerrainBuildFailures.BakeRefused);
        build.Message.ShouldNotBeNull().ShouldContain("input is not a directory");
    }

    /// <summary>
    /// The one this step exists for. Short of memory the tile maker does not fail: it stops refining
    /// and finishes cleanly, having written a complete, valid pyramid of ground at the wrong
    /// resolution. Nothing downstream can tell that from the right one, so it is caught here — and
    /// what it wrote is cleared, or the next attempt would find it, believe it and publish it.
    /// </summary>
    [Fact]
    public async Task A_bake_that_quietly_gave_up_on_detail_fails_and_leaves_nothing_behind()
    {
        var buildId = await SeedAsync();

        await using var worker = new FakeBakeWorker(spoolRoot, (directory, request) =>
        {
            Pyramid(request["output"]);
            Say(
                directory,
                "Generating tiles for level 12",
                "2026-08-16 09:12:44 WARN CRITICAL memory pressure detected: 6% heap free",
                "Generating tiles for level 13",
                "Done");

            // Cleanly. That is the whole difficulty: the exit code says nothing went wrong.
            Answer(directory, "succeeded", exit: 0);
        });

        await RunAsync(buildId);

        var build = await ReadAsync(buildId);
        build.Status.ShouldBe(TerrainBuildStatus.Failed);
        build.ErrorCode.ShouldBe(TerrainBuildFailures.BakeDegraded);
        build.LogTail.ShouldNotBeNull().ShouldContain("CRITICAL memory pressure");

        Directory.EnumerateFileSystemEntries(TilesOf(buildId)).ShouldBeEmpty();
    }

    /// <summary>
    /// A build handed back to a worker after its host restarted does not mesh everything a second
    /// time — but only because there is a pyramid there, not because a column said so.
    /// </summary>
    [Fact]
    public async Task A_build_that_already_has_a_pyramid_is_not_meshed_again()
    {
        var buildId = await SeedAsync();
        var bakes = 0;

        await using var worker = new FakeBakeWorker(spoolRoot, (directory, request) =>
        {
            Interlocked.Increment(ref bakes);
            Pyramid(request["output"]);
            Answer(directory, "succeeded", exit: 0);
        });

        await RunAsync(buildId);
        await RewindAsync(buildId);
        await RunAsync(buildId);

        (await ReadAsync(buildId)).Status.ShouldBe(TerrainBuildStatus.Succeeded);
        Volatile.Read(ref bakes).ShouldBe(1);
    }

    /// <summary>
    /// The same failure, reached the way it actually reaches a machine: the thing doing the meshing
    /// is a separate program, so this application can be restarted mid-bake — a deployment, a host
    /// that rebooted — while the other side carries on, finishes, and leaves behind a complete
    /// pyramid and a clean answer, having said in its log and nowhere else that it ran short of
    /// memory and stopped refining. The run that comes back finds exactly what a good bake leaves.
    /// </summary>
    /// <remarks>
    /// Nothing later can catch this: coarser tiles are whole tiles, fully advertised and uniformly
    /// written, so every check downstream passes and the ground is published at the wrong
    /// resolution under a green tick.
    /// </remarks>
    [Fact]
    public async Task A_bake_that_finished_while_this_process_was_away_is_still_read_for_what_it_gave_up_on()
    {
        var buildId = await SeedAsync();
        var tiles = TilesOf(buildId);
        Pyramid(tiles);

        var spool = Path.Combine(spoolRoot, buildId.ToString("N"));
        Directory.CreateDirectory(spool);
        File.WriteAllText(
            Path.Combine(spool, "request"),
            $"input=x\noutput={tiles}\nmaxDepth=13\ndatum=orthometric\n");
        File.WriteAllText(Path.Combine(spool, "started"), "while this process was away");
        Say(
            spool,
            "Generating tiles for level 12",
            "2026-08-16 09:12:44 WARN CRITICAL memory pressure detected: 6% heap free",
            "Generating tiles for level 13",
            "Done");
        Answer(spool, "succeeded", exit: 0);

        var bakes = 0;
        await using var worker = new FakeBakeWorker(spoolRoot, (directory, request) =>
        {
            Interlocked.Increment(ref bakes);
            Pyramid(request["output"]);
            Answer(directory, "succeeded", exit: 0);
        });

        await RunAsync(buildId);

        var build = await ReadAsync(buildId);
        build.Status.ShouldBe(TerrainBuildStatus.Failed);
        build.ErrorCode.ShouldBe(TerrainBuildFailures.BakeDegraded);
        build.LogTail.ShouldNotBeNull().ShouldContain("CRITICAL memory pressure");

        Directory.EnumerateFileSystemEntries(tiles).ShouldBeEmpty();

        // Nothing was asked for again either: the answer that was already there is the one being
        // judged, and re-meshing it would have thrown away the log that carries the verdict.
        Volatile.Read(ref bakes).ShouldBe(0);
    }

    /// <summary>
    /// A bake abandoned without ever getting an answer — nothing took it, or it took too long —
    /// leaves nothing behind either.
    /// </summary>
    /// <remarks>
    /// The routes that read an answer all sweep the output before they fail, and this one has to as
    /// well, for the same reason: whatever had been written is a pyramid, a later attempt decides
    /// the meshing is done by finding one, and no run of this step ever stood behind that one.
    /// </remarks>
    [Fact]
    public async Task A_bake_abandoned_without_an_answer_leaves_no_pyramid_for_the_next_attempt()
    {
        var buildId = await SeedAsync();
        var tiles = TilesOf(buildId);
        Pyramid(tiles);

        // A request with no answer beside it, which is a bake that may be running into this very
        // directory right now. So it is waited for rather than taken as finished work — and here
        // nothing ever claims it.
        var spool = Path.Combine(spoolRoot, buildId.ToString("N"));
        Directory.CreateDirectory(spool);
        File.WriteAllText(
            Path.Combine(spool, "request"),
            $"input=x\noutput={tiles}\nmaxDepth=13\ndatum=orthometric\n");

        await RunAsync(buildId);

        var build = await ReadAsync(buildId);
        build.Status.ShouldBe(TerrainBuildStatus.Failed);
        build.ErrorCode.ShouldBe(TerrainBuildFailures.BakeWorkerSilent);

        Directory.EnumerateFileSystemEntries(tiles).ShouldBeEmpty();
    }

    /// <summary>
    /// An installation of this application, with or without something to make tiles with.
    /// </summary>
    /// <remarks>
    /// The steps before this one are stood in for. What they do is tested where they live; here they
    /// only have to leave a prepared set behind without a raster library, a network or a minute of
    /// waiting. They are replaced rather than joined, because the walk takes the first
    /// implementation claiming a step and a stand-in registered alongside a real one would never be
    /// asked.
    /// </remarks>
    private SilexGisApiFactory Installation(bool bakes)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Terrain:BuildRoot"] = buildRoot,
            ["Terrain:SpoolRoot"] = spoolRoot,
            ["Terrain:BakeEnabled"] = bakes ? "true" : "false",

            // The shortest wait this application will accept, so that "nothing ever took the work"
            // is a few seconds here instead of the several minutes a starting service is given.
            ["Terrain:BakePickupSeconds"] = "1",
        };

        return new SilexGisApiFactory(postgres.ConnectionString, settings, services =>
        {
            JobWorkers.RemoveFrom(services);

            foreach (var step in services.Where(s => s.ServiceType == typeof(ITerrainPhase)).ToList())
            {
                services.Remove(step);
            }

            services.AddSingleton<ITerrainPhase, StubFetchPhase>();
            services.AddSingleton<ITerrainPhase, StubPreparePhase>();
            services.AddScoped<ITerrainPhase, TerrainBakePhase>();

            foreach (var registered in services
                .Where(s => s.ServiceType == typeof(ITerrainRasterPreparer)).ToList())
            {
                services.Remove(registered);
            }

            services.AddSingleton<ITerrainRasterPreparer, DirectoryPreparer>();
        });
    }

    private string TilesOf(Guid buildId) =>
        Path.Combine(buildRoot, buildId.ToString("N"), "tiles");

    /// <summary>The smallest thing that reads as a pyramid: a manifest, and a tile under it.</summary>
    private static void Pyramid(string tiles)
    {
        Directory.CreateDirectory(Path.Combine(tiles, "13", "4521"));
        File.WriteAllText(
            Path.Combine(tiles, "layer.json"),
            """{"tilejson":"2.1.0","format":"quantized-mesh-1.0","available":[[{"startX":0,"startY":0,"endX":1,"endY":0}]]}""");
        File.WriteAllBytes(Path.Combine(tiles, "13", "4521", "3011.terrain"), new byte[128]);
    }

    /// <summary>What the tile maker said, as it would have said it.</summary>
    private static void Say(string directory, params string[] lines) =>
        File.WriteAllText(Path.Combine(directory, "bake.log"), string.Join('\n', lines) + "\n");

    /// <summary>
    /// The answer, written the way both sides must write everything they leave for each other:
    /// under another name, then renamed into place, so that no reader ever sees half of it.
    /// </summary>
    private static void Answer(string directory, string status, int? exit = null, string? reason = null)
    {
        var text = $"status={status}\nexit={exit}\n"
            + (reason is null ? "" : $"reason={reason}\n")
            + $"finished={DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\n";

        var partial = Path.Combine(directory, "result.part");
        File.WriteAllText(partial, text);
        File.Move(partial, Path.Combine(directory, "result"));
    }

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
    private async Task RunAsync(Guid buildId, SilexGisApiFactory? installation = null)
    {
        await using var scope = (installation ?? factory).Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.TerrainBuild);

        try
        {
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
        catch (InvalidOperationException)
        {
            // A step that stops throws a bounded stand-in, so that the queue sees a failed job. The
            // reason is recorded on the build row before it is thrown, and the row is what every
            // test in this class reads.
        }
    }

    /// <summary>Puts a build back the way a process that died after it would leave it.</summary>
    /// <remarks>
    /// The recorded reason is cleared with it: a row carrying one and handed over again is stopped
    /// rather than started, and what is being set up here is a run that was interrupted.
    /// </remarks>
    private async Task RewindAsync(Guid buildId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.TerrainBuilds.Where(b => b.Id == buildId).ExecuteUpdateAsync(s => s
            .SetProperty(b => b.Status, TerrainBuildStatus.Running)
            .SetProperty(b => b.ErrorCode, (string?)null)
            .SetProperty(b => b.Message, (string?)null)
            .SetProperty(b => b.FinishedAt, (DateTimeOffset?)null));
    }

    private async Task<TerrainBuild> ReadAsync(Guid buildId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TerrainBuilds.AsNoTracking().FirstAsync(b => b.Id == buildId);
    }

    /// <summary>Stands in for the step that obtains rasters. Nothing here needs one.</summary>
    private sealed class StubFetchPhase : ITerrainPhase
    {
        public TerrainBuildPhase Phase => TerrainBuildPhase.Fetch;

        public Task<bool> IsAlreadyDoneAsync(TerrainBuildContext context, CancellationToken ct) =>
            Task.FromResult(true);

        public Task RunAsync(TerrainBuildContext context, CancellationToken ct) =>
            Task.CompletedTask;
    }

    /// <summary>
    /// Stands in for the step that prepares them, leaving behind what a real one leaves: a file per
    /// source, in the directory the meshing step reads.
    /// </summary>
    private sealed class StubPreparePhase : ITerrainPhase
    {
        public TerrainBuildPhase Phase => TerrainBuildPhase.Prepare;

        public Task<bool> IsAlreadyDoneAsync(TerrainBuildContext context, CancellationToken ct) =>
            Task.FromResult(File.Exists(OneOf(context)));

        public async Task RunAsync(TerrainBuildContext context, CancellationToken ct) =>
            await File.WriteAllTextAsync(OneOf(context), "not really a raster", ct);

        private static string OneOf(TerrainBuildContext context) =>
            Path.Combine(context.Directories.Prepared, "blanket.tif");
    }

    /// <summary>
    /// A raster chain that answers only the one question this step asks it: whether the prepared set
    /// is there. It reads the directory, which is what the real one does — through a great deal more
    /// care about whether each file is whole.
    /// </summary>
    private sealed class DirectoryPreparer : ITerrainRasterPreparer
    {
        public IReadOnlyList<PreparedTerrainRaster> Prepare(
            TerrainRasterPrepareRequest request, CancellationToken ct) =>
            DescribePrepared(request) ?? [];

        public IReadOnlyList<PreparedTerrainRaster>? DescribePrepared(
            TerrainRasterPrepareRequest request)
        {
            if (!Directory.Exists(request.OutputDirectory))
            {
                return null;
            }

            var files = Directory.GetFiles(request.OutputDirectory, "*.tif");
            return files.Length == 0 ? null : [.. files.Select(f => Describe(f)!)];
        }

        public PreparedTerrainRaster? Describe(string path) =>
            File.Exists(path)
                ? new PreparedTerrainRaster(path, 8, 8, 0.0125, 25, 46, 25.1, 46.1, -9999, 64)
                : null;
    }

    /// <summary>
    /// Stands in for the service that makes tiles, by being the far side of the directory the two
    /// meet on: it claims a request the way that service does, and leaves whatever answer the test
    /// wants beside it.
    /// </summary>
    private sealed class FakeBakeWorker : IAsyncDisposable
    {
        private readonly CancellationTokenSource stopping = new();
        private readonly Task watching;

        public FakeBakeWorker(
            string spoolRoot, Action<string, IReadOnlyDictionary<string, string>> bake)
        {
            Directory.CreateDirectory(spoolRoot);
            watching = Task.Run(async () =>
            {
                while (!stopping.IsCancellationRequested)
                {
                    foreach (var directory in Directory.GetDirectories(spoolRoot))
                    {
                        var request = Path.Combine(directory, "request");
                        if (!File.Exists(request)
                            || File.Exists(Path.Combine(directory, "result"))
                            || File.Exists(Path.Combine(directory, "started")))
                        {
                            continue;
                        }

                        File.WriteAllText(Path.Combine(directory, "started"), "now");
                        bake(directory, Fields(File.ReadAllText(request)));
                    }

                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(25), stopping.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            });
        }

        public async ValueTask DisposeAsync()
        {
            await stopping.CancelAsync();
            try
            {
                await watching;
            }
            catch (Exception e) when (e is OperationCanceledException or IOException)
            {
                // Stopping while a directory is being removed underneath it is an ordinary end.
            }

            stopping.Dispose();
        }

        private static Dictionary<string, string> Fields(string text)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in text.Split('\n'))
            {
                var separator = line.IndexOf('=', StringComparison.Ordinal);
                if (separator > 0)
                {
                    fields[line[..separator]] = line[(separator + 1)..].TrimEnd('\r');
                }
            }

            return fields;
        }
    }
}
