// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Features.Terrain;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Tests;

/// <summary>
/// Calling a terrain build off, and running a stopped one again.
///
/// <para>
/// The walk is driven for real, with the meshing step the real one and the program on the far side
/// of its directory played by the test — so "the bake stopped" is the far side answering that it
/// did, not a flag the test set. The steps before it are stand-ins that count how often they ran,
/// which is what tells a run that carried on from one that started again.
/// </para>
/// <para>
/// Every permission assertion is paired, as elsewhere in this area: an account holding exactly the
/// right being tested passes, and one holding only the right to read builds is refused — the pass
/// is what proves the refusal was about the right and not about the fixture.
/// </para>
/// </summary>
public sealed class TerrainBuildCancelRetryTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture postgres;
    private readonly string scratch;
    private readonly string buildRoot;
    private readonly string spoolRoot;
    private readonly string publishRoot;
    private readonly CountingFetchPhase fetch = new();
    private readonly CountingPreparePhase prepare = new();
    private readonly CountingPublishPhase publish = new();
    private readonly MovableClock clock = new();
    private readonly SilexGisApiFactory factory;

    private HttpClient runner = null!;    // Execute and Read on the terrain domain
    private HttpClient reader = null!;    // Read on the terrain domain and nothing else
    private HttpClient anonymous = null!;

    public TerrainBuildCancelRetryTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
        scratch = Path.Combine(TestScratch.Root, $"silexgis-tcancel-{Guid.NewGuid():N}");
        buildRoot = Path.Combine(scratch, "builds");
        spoolRoot = Path.Combine(scratch, "spool");
        publishRoot = Path.Combine(scratch, "published");
        factory = Installation(bakes: true);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var runnerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tcr-run-{suffix}@t.local");
        runner = await AuthHelper.BearerClientAsync(factory, $"tcr-run-{suffix}@t.local");
        await GrantAsync(runnerId, AccessAction.Execute | AccessAction.Read);

        var readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tcr-read-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"tcr-read-{suffix}@t.local");
        await GrantAsync(readerId, AccessAction.Read);

        anonymous = factory.CreateClient();
    }

    /// <summary>
    /// The whole permission table for calling a build off, and what calling off a queued one does:
    /// it stops at once, its queue row is closed, and a worker that claimed it anyway starts nothing.
    /// </summary>
    [Fact]
    public async Task A_queued_build_is_called_off_at_once_by_the_right_that_starts_it()
    {
        var id = await SubmitAsync(24.10, 46.10);

        (await anonymous.PostAsync($"/api/v1/terrain/builds/{id}/cancel", null)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);

        var refused = await reader.PostAsync($"/api/v1/terrain/builds/{id}/cancel", null);
        var refusedBody = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, refusedBody);
        CodeOf(refusedBody).ShouldBe("access.forbidden");

        var cancelled = await runner.PostAsync($"/api/v1/terrain/builds/{id}/cancel", null);
        var body = await cancelled.Content.ReadAsStringAsync();
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var dto = JsonDocument.Parse(body).RootElement;
        dto.GetProperty("status").GetString().ShouldBe("failed");
        dto.GetProperty("errorCode").GetString().ShouldBe(TerrainBuildFailures.Cancelled);
        dto.GetProperty("cancelRequestedAt").ValueKind.ShouldBe(JsonValueKind.String);

        // The queue row will never be claimed, and says why.
        var job = await JobOfAsync(id);
        job.Status.ShouldBe(ProcessingJobStatus.Failed);
        job.Error.ShouldNotBeNull().ShouldContain("called off");

        // A worker that had claimed it in that same moment finds the build stopped and starts
        // nothing — not the first step, and not the row back to running.
        await RunAsync(id);
        fetch.Runs.ShouldBe(0);
        fetch.Asked.ShouldBe(0);
        (await ReadAsync(id)).Status.ShouldBe(TerrainBuildStatus.Failed);

        // Asked again, the answer is the same build rather than a refusal.
        var again = await runner.PostAsync($"/api/v1/terrain/builds/{id}/cancel", null);
        again.StatusCode.ShouldBe(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A build that has already stopped another way has nothing left to call off, and a build that
    /// is not there is answered as missing.
    /// </summary>
    [Fact]
    public async Task A_build_that_already_stopped_cannot_be_called_off()
    {
        var id = await SubmitAsync(24.20, 46.20);
        await SetStatusAsync(id, TerrainBuildStatus.Succeeded);

        var refused = await runner.PostAsync($"/api/v1/terrain/builds/{id}/cancel", null);
        var body = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, body);
        CodeOf(body).ShouldBe(TerrainBuildEndpoints.NotCancellableCode);

        var missing = await runner.PostAsync($"/api/v1/terrain/builds/{Guid.NewGuid()}/cancel", null);
        var missingBody = await missing.Content.ReadAsStringAsync();
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound, missingBody);
        CodeOf(missingBody).ShouldBe(TerrainBuildEndpoints.NotFoundCode);
    }

    /// <summary>
    /// A build called off while a step inside this process is working stops at the next point that
    /// step looks, and is recorded at that step.
    /// </summary>
    [Fact]
    public async Task A_build_called_off_while_obtaining_rasters_stops_there()
    {
        var id = await SubmitAsync(24.30, 46.30);
        fetch.BlockUntilCancelled = true;

        var run = Task.Run(() => RunAsync(id));
        await UntilAsync(async () => (await ReadAsync(id)).Phase == TerrainBuildPhase.Fetch, "the fetch to start");

        var asked = await runner.PostAsync($"/api/v1/terrain/builds/{id}/cancel", null);
        var askedBody = await asked.Content.ReadAsStringAsync();
        asked.StatusCode.ShouldBe(HttpStatusCode.OK, askedBody);

        // Still running: the request is on the row and the run has not yet reached a point where it
        // can stop. This is the state a screen says "being cancelled" for.
        var midway = JsonDocument.Parse(askedBody).RootElement;
        midway.GetProperty("status").GetString().ShouldBe("running");
        midway.GetProperty("cancelRequestedAt").ValueKind.ShouldBe(JsonValueKind.String);

        await run.WaitAsync(TimeSpan.FromSeconds(60));

        var stopped = await ReadAsync(id);
        stopped.Status.ShouldBe(TerrainBuildStatus.Failed);
        stopped.ErrorCode.ShouldBe(TerrainBuildFailures.Cancelled);
        stopped.Phase.ShouldBe(TerrainBuildPhase.Fetch);
        prepare.Runs.ShouldBe(0);
        publish.Runs.ShouldBe(0);
    }

    /// <summary>
    /// The case that needs the far side's word. Called off during the bake, the tile maker is asked
    /// to stop and the run waits for it to say it has; only then is what it wrote cleared, so that no
    /// half of a pyramid is left for a later attempt to find and believe. Nothing is published.
    /// Then the same build is run again, and carries on at the bake.
    /// </summary>
    [Fact]
    public async Task A_build_called_off_during_its_bake_withdraws_it_leaves_no_pyramid_and_carries_on_when_run_again()
    {
        var id = await SubmitAsync(24.40, 46.40);
        var tiles = TilesOf(id);
        var spool = Path.Combine(spoolRoot, id.ToString("N"));

        await using (new FakeBakeWorker(spoolRoot, (directory, request) =>
        {
            // Half a pyramid, then nothing until somebody withdraws the request.
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
            await UntilAsync(
                () => Task.FromResult(File.Exists(Path.Combine(spool, "started"))), "the bake to be taken");

            var asked = await runner.PostAsync($"/api/v1/terrain/builds/{id}/cancel", null);
            asked.StatusCode.ShouldBe(HttpStatusCode.OK, await asked.Content.ReadAsStringAsync());

            await run.WaitAsync(TimeSpan.FromSeconds(60));
        }

        var stopped = await ReadAsync(id);
        stopped.Status.ShouldBe(TerrainBuildStatus.Failed);
        stopped.ErrorCode.ShouldBe(TerrainBuildFailures.Cancelled);
        stopped.Phase.ShouldBe(TerrainBuildPhase.Bake);

        // The far side was asked through the file it watches, answered, and what it wrote is gone.
        File.Exists(Path.Combine(spool, "cancel")).ShouldBeTrue();
        File.ReadAllText(Path.Combine(spool, "result")).ShouldContain("status=cancelled");
        Directory.EnumerateFileSystemEntries(tiles).ShouldBeEmpty();
        publish.Runs.ShouldBe(0);

        // Run again: everything before the bake is already there, so only the bake and what
        // follows it run.
        var retried = await runner.PostAsync($"/api/v1/terrain/builds/{id}/retry", null);
        var retriedBody = await retried.Content.ReadAsStringAsync();
        retried.StatusCode.ShouldBe(HttpStatusCode.OK, retriedBody);
        var queued = JsonDocument.Parse(retriedBody).RootElement;
        queued.GetProperty("status").GetString().ShouldBe("queued");
        queued.GetProperty("errorCode").ValueKind.ShouldBe(JsonValueKind.Null);
        queued.GetProperty("cancelRequestedAt").ValueKind.ShouldBe(JsonValueKind.Null);

        await using (new FakeBakeWorker(spoolRoot, (directory, request) =>
        {
            Pyramid(request["output"]);
            Answer(directory, "succeeded", exit: 0);
        }))
        {
            await RunAsync(id);
        }

        var finished = await ReadAsync(id);
        finished.Status.ShouldBe(TerrainBuildStatus.Succeeded, finished.Message ?? "");
        fetch.Runs.ShouldBe(1);
        prepare.Runs.ShouldBe(1);
        publish.Runs.ShouldBe(1);
    }

    /// <summary>
    /// A bake that reaches the time limit is stopped as a called-off one is: withdrawn from the tile
    /// maker through the same file, its tiles discarded once the tile maker has said it stopped,
    /// and the build recorded with its own reason and a message naming the time it ran, the limit
    /// and the setting. Run again, it carries on at the bake.
    /// </summary>
    /// <remarks>
    /// The limit cannot be set below a minute, so the clock it is measured on is moved instead: the
    /// bake is taken, then thirteen hours pass on that clock, against the twelve-hour default.
    /// </remarks>
    [Fact]
    public async Task A_bake_that_reaches_the_time_limit_is_withdrawn_reported_and_can_be_run_again()
    {
        var id = await SubmitAsync(24.80, 46.80);
        var tiles = TilesOf(id);
        var spool = Path.Combine(spoolRoot, id.ToString("N"));

        await using (new FakeBakeWorker(spoolRoot, (directory, request) =>
        {
            // Half a pyramid, then nothing until somebody withdraws the request — a bake that has
            // stopped making progress looks exactly like this from outside.
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
            await UntilAsync(
                () => Task.FromResult(File.Exists(Path.Combine(spool, "started"))), "the bake to be taken");

            clock.Offset = TimeSpan.FromHours(13);
            try
            {
                await run.WaitAsync(TimeSpan.FromSeconds(60));
            }
            finally
            {
                clock.Offset = TimeSpan.Zero;
            }
        }

        var stopped = await ReadAsync(id);
        stopped.Status.ShouldBe(TerrainBuildStatus.Failed);
        stopped.ErrorCode.ShouldBe(TerrainBuildFailures.BakeTimedOut);
        stopped.Phase.ShouldBe(TerrainBuildPhase.Bake);
        stopped.Message.ShouldNotBeNull().ShouldStartWith("Time limit reached");
        stopped.Message.ShouldContain("SILEXGIS__Terrain__BakeTimeoutSeconds=43200");
        stopped.Message.ShouldContain("The tile maker stopped");

        // Withdrawn through the file a person calling the build off writes, answered, and what it
        // wrote is gone — so nothing is left for a later attempt to find and believe.
        File.Exists(Path.Combine(spool, "cancel")).ShouldBeTrue();
        File.ReadAllText(Path.Combine(spool, "result")).ShouldContain("status=cancelled");
        Directory.EnumerateFileSystemEntries(tiles).ShouldBeEmpty();
        publish.Runs.ShouldBe(0);

        // The screen is handed the two numbers, so it can say them in its reader's language.
        var detail = await runner.GetAsync($"/api/v1/terrain/builds/{id}");
        var detailBody = await detail.Content.ReadAsStringAsync();
        detail.StatusCode.ShouldBe(HttpStatusCode.OK, detailBody);
        var limit = JsonDocument.Parse(detailBody).RootElement.GetProperty("build").GetProperty("bakeTimeLimit");
        limit.GetProperty("limitSeconds").GetInt64().ShouldBe(43_200);
        limit.GetProperty("elapsedSeconds").GetInt64().ShouldBeGreaterThanOrEqualTo(43_200);

        var retried = await runner.PostAsync($"/api/v1/terrain/builds/{id}/retry", null);
        var retriedBody = await retried.Content.ReadAsStringAsync();
        retried.StatusCode.ShouldBe(HttpStatusCode.OK, retriedBody);
        var queued = JsonDocument.Parse(retriedBody).RootElement;
        queued.GetProperty("errorCode").ValueKind.ShouldBe(JsonValueKind.Null);
        queued.GetProperty("bakeTimeLimit").ValueKind.ShouldBe(JsonValueKind.Null);

        await using (new FakeBakeWorker(spoolRoot, (directory, request) =>
        {
            Pyramid(request["output"]);
            Answer(directory, "succeeded", exit: 0);
        }))
        {
            await RunAsync(id);
        }

        var finished = await ReadAsync(id);
        finished.Status.ShouldBe(TerrainBuildStatus.Succeeded, finished.Message ?? "");
        fetch.Runs.ShouldBe(1);
        prepare.Runs.ShouldBe(1);
        publish.Runs.ShouldBe(1);
    }

    /// <summary>
    /// The case running a build again exists for: an installation with nothing to make tiles with
    /// stops a build at the bake, keeping what it obtained and prepared, and once a tile maker is
    /// there the same build carries on from the bake — without the count of hand-overs that stops a
    /// looping build ending it at once.
    /// </summary>
    [Fact]
    public async Task A_build_stopped_for_want_of_a_tile_maker_resumes_at_the_bake_when_run_again()
    {
        var id = await SubmitAsync(24.50, 46.50);

        await using (var bare = Installation(bakes: false))
        {
            await RunAsync(id, installation: bare);
        }

        var stopped = await ReadAsync(id);
        stopped.ErrorCode.ShouldBe(TerrainBuildFailures.BakeUnavailable);
        fetch.Runs.ShouldBe(1);
        prepare.Runs.ShouldBe(1);

        var retried = await runner.PostAsync($"/api/v1/terrain/builds/{id}/retry", null);
        retried.StatusCode.ShouldBe(HttpStatusCode.OK, await retried.Content.ReadAsStringAsync());

        // A new queue row for the new run rather than the old one revived, so the count of
        // hand-overs starts again. (The first row is still queued here only because this test ran
        // the handler itself instead of letting a worker claim it.)
        (await JobsOfAsync(id)).Count.ShouldBe(2);

        await using (new FakeBakeWorker(spoolRoot, (directory, request) =>
        {
            Pyramid(request["output"]);
            Answer(directory, "succeeded", exit: 0);
        }))
        {
            // Handed over a second time, the way a restart in the middle of the new run hands it
            // back. The old reason was cleared when it was queued again, so this is a resumption,
            // not a build that failed twice.
            await RunAsync(id, attempts: 2);
        }

        var finished = await ReadAsync(id);
        finished.Status.ShouldBe(TerrainBuildStatus.Succeeded, finished.ErrorCode ?? finished.Message ?? "");
        fetch.Runs.ShouldBe(1);
        prepare.Runs.ShouldBe(1);
    }

    /// <summary>
    /// Everything running a build again refuses, each with its own code, and the permission table.
    /// </summary>
    [Fact]
    public async Task Running_a_build_again_is_refused_unless_it_stopped_and_nothing_else_is_building_its_area()
    {
        var id = await SubmitAsync(24.60, 46.60);

        // Still queued: there is nothing to run again.
        var queued = await runner.PostAsync($"/api/v1/terrain/builds/{id}/retry", null);
        var queuedBody = await queued.Content.ReadAsStringAsync();
        queued.StatusCode.ShouldBe(HttpStatusCode.Conflict, queuedBody);
        CodeOf(queuedBody).ShouldBe(TerrainBuildEndpoints.NotRetryableCode);

        // Finished: likewise.
        await SetStatusAsync(id, TerrainBuildStatus.Succeeded);
        var finished = await runner.PostAsync($"/api/v1/terrain/builds/{id}/retry", null);
        CodeOf(await finished.Content.ReadAsStringAsync()).ShouldBe(TerrainBuildEndpoints.NotRetryableCode);

        // Stopped, while a second build of the same rectangle is unfinished: refused for the
        // reason starting a second one is.
        await SetStatusAsync(id, TerrainBuildStatus.Failed, TerrainBuildFailures.FetchFailed);
        var other = await SubmitAsync(24.60, 46.60);
        var busy = await runner.PostAsync($"/api/v1/terrain/builds/{id}/retry", null);
        var busyBody = await busy.Content.ReadAsStringAsync();
        busy.StatusCode.ShouldBe(HttpStatusCode.Conflict, busyBody);
        CodeOf(busyBody).ShouldBe(TerrainBuildEndpoints.AlreadyBuildingCode);
        await SetStatusAsync(other, TerrainBuildStatus.Failed, TerrainBuildFailures.FetchFailed);

        // Called off during a bake the tile maker has not yet said it stopped: running it again now
        // could put a second bake into the directory the first is still writing.
        var spool = Path.Combine(spoolRoot, id.ToString("N"));
        Directory.CreateDirectory(spool);
        await File.WriteAllTextAsync(Path.Combine(spool, "request"), "input=x\noutput=y\nmaxDepth=13\n");
        await File.WriteAllTextAsync(Path.Combine(spool, "cancel"), "now\n");
        var stopping = await runner.PostAsync($"/api/v1/terrain/builds/{id}/retry", null);
        var stoppingBody = await stopping.Content.ReadAsStringAsync();
        stopping.StatusCode.ShouldBe(HttpStatusCode.Conflict, stoppingBody);
        CodeOf(stoppingBody).ShouldBe(TerrainBuildEndpoints.BakeStoppingCode);

        // Once the far side has answered, it may.
        Answer(spool, "cancelled", exit: 143);

        (await anonymous.PostAsync($"/api/v1/terrain/builds/{id}/retry", null)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        var refused = await reader.PostAsync($"/api/v1/terrain/builds/{id}/retry", null);
        var refusedBody = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, refusedBody);
        CodeOf(refusedBody).ShouldBe("access.forbidden");

        var allowed = await runner.PostAsync($"/api/v1/terrain/builds/{id}/retry", null);
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());

        // The answered handover went with the run that is over; the new run starts its own.
        Directory.Exists(spool).ShouldBeFalse();

        var missing = await runner.PostAsync($"/api/v1/terrain/builds/{Guid.NewGuid()}/retry", null);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A process that restarts after somebody called its build off finishes calling it off rather
    /// than resuming it — including withdrawing a bake still outstanding from before the restart.
    /// </summary>
    [Fact]
    public async Task A_build_handed_back_after_a_restart_with_a_request_to_stop_is_stopped_not_resumed()
    {
        var id = await SubmitAsync(24.70, 46.70);
        var tiles = TilesOf(id);
        Pyramid(tiles);

        var spool = Path.Combine(spoolRoot, id.ToString("N"));
        Directory.CreateDirectory(spool);
        await File.WriteAllTextAsync(
            Path.Combine(spool, "request"), $"input=x\noutput={tiles}\nmaxDepth=13\n");
        await File.WriteAllTextAsync(Path.Combine(spool, "started"), "before the restart");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.TerrainBuilds.Where(b => b.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(b => b.Status, TerrainBuildStatus.Running)
            .SetProperty(b => b.Phase, TerrainBuildPhase.Bake)
            .SetProperty(b => b.CancelRequestedAt, DateTimeOffset.UtcNow));

        // The far side answers the withdrawal the moment it sees it.
        using var answering = new CancellationTokenSource();
        var farSide = Task.Run(async () =>
        {
            while (!answering.IsCancellationRequested)
            {
                if (File.Exists(Path.Combine(spool, "cancel")) && !File.Exists(Path.Combine(spool, "result")))
                {
                    Answer(spool, "cancelled", exit: 143);
                    return;
                }

                await Task.Delay(25);
            }
        });

        await RunAsync(id, attempts: 2);
        await answering.CancelAsync();
        await farSide;

        var stopped = await ReadAsync(id);
        stopped.Status.ShouldBe(TerrainBuildStatus.Failed);
        stopped.ErrorCode.ShouldBe(TerrainBuildFailures.Cancelled);
        stopped.Phase.ShouldBe(TerrainBuildPhase.Bake);
        fetch.Asked.ShouldBe(0);
        Directory.EnumerateFileSystemEntries(tiles).ShouldBeEmpty();
    }

    private SilexGisApiFactory Installation(bool bakes)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Terrain:BuildRoot"] = buildRoot,
            ["Terrain:SpoolRoot"] = spoolRoot,
            ["Terrain:PublishRoot"] = publishRoot,
            ["Terrain:BakeEnabled"] = bakes ? "true" : "false",
            ["Terrain:BakePickupSeconds"] = "5",
        };

        return new SilexGisApiFactory(postgres.ConnectionString, settings, services =>
        {
            JobWorkers.RemoveFrom(services);

            foreach (var step in services.Where(s => s.ServiceType == typeof(ITerrainPhase)).ToList())
            {
                services.Remove(step);
            }

            services.AddSingleton<ITerrainPhase>(fetch);
            services.AddSingleton<ITerrainPhase>(prepare);
            services.AddScoped<ITerrainPhase, TerrainBakePhase>();
            services.AddSingleton<ITerrainPhase, PassingValidatePhase>();
            services.AddSingleton<ITerrainPhase>(publish);

            foreach (var registered in services
                .Where(s => s.ServiceType == typeof(ITerrainRasterPreparer)).ToList())
            {
                services.Remove(registered);
            }

            services.AddSingleton<ITerrainRasterPreparer, DirectoryPreparer>();

            // The clock the bake's time limit is measured on, so a test can let half a day pass
            // without waiting for it. Read as the wall clock until a test moves it.
            services.AddSingleton<TimeProvider>(clock);
        },
        ownHost: true);
    }

    private string TilesOf(Guid buildId) => Path.Combine(buildRoot, buildId.ToString("N"), "tiles");

    private static void Pyramid(string tiles)
    {
        Directory.CreateDirectory(Path.Combine(tiles, "13", "4521"));
        File.WriteAllText(
            Path.Combine(tiles, "layer.json"),
            """{"tilejson":"2.1.0","format":"quantized-mesh-1.0","available":[[{"startX":0,"startY":0,"endX":1,"endY":0}]]}""");
        File.WriteAllBytes(Path.Combine(tiles, "13", "4521", "3011.terrain"), new byte[128]);
    }

    private static void Answer(string directory, string status, int? exit = null)
    {
        var partial = Path.Combine(directory, "result.part");
        File.WriteAllText(partial, $"status={status}\nexit={exit}\n");
        File.Move(partial, Path.Combine(directory, "result"));
    }

    private async Task<Guid> SubmitAsync(double west, double south)
    {
        var response = await runner.PostAsJsonAsync(
            "/api/v1/terrain/builds",
            new TerrainBuildSubmitRequest(west, south, west + 0.05, south + 0.05, 13, null, null));
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    private async Task RunAsync(Guid buildId, int attempts = 1, SilexGisApiFactory? installation = null)
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
                    Attempts = attempts,
                    Payload = JsonSerializer.Serialize(new TerrainBuildPayload(buildId), JsonSerializerOptions.Web),
                },
                CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // A run that stops throws a bounded stand-in so the queue sees a failed job; the build
            // row, which is what every test here reads, was written before it was thrown.
        }
    }

    private static async Task UntilAsync(Func<Task<bool>> check, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (await check())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Timed out waiting for {what}.");
    }

    private async Task SetStatusAsync(Guid buildId, TerrainBuildStatus status, string? errorCode = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.TerrainBuilds.Where(b => b.Id == buildId).ExecuteUpdateAsync(s => s
            .SetProperty(b => b.Status, status)
            .SetProperty(b => b.ErrorCode, errorCode));
    }

    private async Task<TerrainBuild> ReadAsync(Guid buildId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TerrainBuilds.AsNoTracking().FirstAsync(b => b.Id == buildId);
    }

    private async Task<List<ProcessingJob>> JobsOfAsync(Guid buildId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var jobs = await db.ProcessingJobs.AsNoTracking()
            .Where(j => j.Kind == ProcessingJobKinds.TerrainBuild)
            .ToListAsync();

        // The payload is a jsonb column, so it is matched here rather than in the query.
        return [.. jobs.Where(j => j.Payload.Contains(buildId.ToString(), StringComparison.OrdinalIgnoreCase))];
    }

    private async Task<ProcessingJob> JobOfAsync(Guid buildId) => (await JobsOfAsync(buildId)).Single();

    private static string? CodeOf(string problemBody) =>
        JsonDocument.Parse(problemBody).RootElement.GetProperty("code").GetString();

    private async Task GrantAsync(Guid userId, AccessAction actions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Domain = AccessDomain.Terrain,
            Actions = actions,
            Effect = AccessEffect.Allow,
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();
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
        runner?.Dispose();
        reader?.Dispose();
        anonymous?.Dispose();
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

    /// <summary>The wall clock, or the wall clock moved on by however much a test says.</summary>
    private sealed class MovableClock : TimeProvider
    {
        private long offsetTicks;

        public TimeSpan Offset
        {
            get => TimeSpan.FromTicks(Interlocked.Read(ref offsetTicks));
            set => Interlocked.Exchange(ref offsetTicks, value.Ticks);
        }

        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Offset;
    }

    /// <summary>
    /// Stands in for the step that obtains rasters, leaving a mark it reads back — or, when told
    /// to, waiting until the run is called off, the way a long download is cut short.
    /// </summary>
    private sealed class CountingFetchPhase : ITerrainPhase
    {
        private int runs;
        private int asked;

        public TerrainBuildPhase Phase => TerrainBuildPhase.Fetch;

        public int Runs => Volatile.Read(ref runs);

        public int Asked => Volatile.Read(ref asked);

        public bool BlockUntilCancelled { get; set; }

        public Task<bool> IsAlreadyDoneAsync(TerrainBuildContext context, CancellationToken ct)
        {
            Interlocked.Increment(ref asked);
            return Task.FromResult(File.Exists(Path.Combine(context.Directories.Input, "fetched.txt")));
        }

        public async Task RunAsync(TerrainBuildContext context, CancellationToken ct)
        {
            if (BlockUntilCancelled)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }

            await File.WriteAllTextAsync(Path.Combine(context.Directories.Input, "fetched.txt"), "x", ct);
            Interlocked.Increment(ref runs);
        }
    }

    /// <summary>Leaves one prepared raster, which is what the meshing step looks for.</summary>
    private sealed class CountingPreparePhase : ITerrainPhase
    {
        private int runs;

        public TerrainBuildPhase Phase => TerrainBuildPhase.Prepare;

        public int Runs => Volatile.Read(ref runs);

        public Task<bool> IsAlreadyDoneAsync(TerrainBuildContext context, CancellationToken ct) =>
            Task.FromResult(File.Exists(OneOf(context)));

        public async Task RunAsync(TerrainBuildContext context, CancellationToken ct)
        {
            await File.WriteAllTextAsync(OneOf(context), "not really a raster", ct);
            Interlocked.Increment(ref runs);
        }

        private static string OneOf(TerrainBuildContext context) =>
            Path.Combine(context.Directories.Prepared, "blanket.tif");
    }

    private sealed class PassingValidatePhase : ITerrainPhase
    {
        public TerrainBuildPhase Phase => TerrainBuildPhase.Validate;

        public Task<bool> IsAlreadyDoneAsync(TerrainBuildContext context, CancellationToken ct) =>
            Task.FromResult(false);

        public Task RunAsync(TerrainBuildContext context, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>Counts how often anything was published, which for a called-off run must be never.</summary>
    private sealed class CountingPublishPhase : ITerrainPhase
    {
        private int runs;

        public TerrainBuildPhase Phase => TerrainBuildPhase.Publish;

        public int Runs => Volatile.Read(ref runs);

        public Task<bool> IsAlreadyDoneAsync(TerrainBuildContext context, CancellationToken ct) =>
            Task.FromResult(false);

        public Task RunAsync(TerrainBuildContext context, CancellationToken ct)
        {
            Interlocked.Increment(ref runs);
            return Task.CompletedTask;
        }
    }

    private sealed class DirectoryPreparer : ITerrainRasterPreparer
    {
        public IReadOnlyList<PreparedTerrainRaster> Prepare(
            TerrainRasterPrepareRequest request, CancellationToken ct) =>
            DescribePrepared(request) ?? [];

        public IReadOnlyList<PreparedTerrainRaster>? DescribePrepared(TerrainRasterPrepareRequest request)
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
}
