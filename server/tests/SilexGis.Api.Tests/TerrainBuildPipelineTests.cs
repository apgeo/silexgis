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
/// Starting a terrain build, and walking one through the chain.
///
/// <para>
/// Every permission assertion here is a pair, and the pairing is the point: a full administrator is
/// short-circuited before any terrain rule is read, so an endpoint whose permission check had been
/// deleted outright would still pass every assertion driven by one. The account that succeeds holds
/// exactly the right being tested and nothing else, and the account that is refused holds nothing —
/// the pass is what proves the refusal was about the right rather than about the fixture.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TerrainBuildPipelineTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string buildRoot;
    private readonly string publishRoot;
    private readonly RecordingPhase fetchPhase = new();

    private HttpClient starter = null!;   // an ordinary account granted Execute on the terrain domain
    private HttpClient outsider = null!;  // an ordinary account granted nothing at all
    private HttpClient anonymous = null!;
    private Guid starterId;

    public TerrainBuildPipelineTests(PostgresFixture postgres)
    {
        buildRoot = Path.Combine(Path.GetTempPath(), $"silexgis-terrain-{Guid.NewGuid():N}");

        // Named here rather than left at its default, which resolves against the test host's own
        // directory: a published pyramid is what tells a run that a build has already finished, so
        // one left in a shared directory would be a fact about the machine rather than the test.
        publishRoot = buildRoot + "-published";

        // This class queues terrain work, so it runs none of the drains itself: every test class
        // shares one PostGIS container and the queue lives in it, so a drain started here would
        // claim work another class queued and fail it against storage this class does not have.
        // What this class queues is deleted again when it finishes, for the mirror-image reason.
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Terrain:BuildRoot"] = buildRoot,
                ["Terrain:PublishRoot"] = publishRoot,
            },
            services =>
            {
                JobWorkers.RemoveFrom(services);

                // One step of the chain, standing in for the real first one. The walk is what is
                // under test — which steps run, in what order, what is skipped on a second pass,
                // and what a failure leaves behind — and the real one would make every one of those
                // assertions depend on a network. It replaces rather than joins the registered
                // steps: the walk takes the first implementation claiming to be the step it wants,
                // so leaving the real one in place would leave this one never asked.
                foreach (var step in services.Where(s => s.ServiceType == typeof(ITerrainPhase)).ToList())
                {
                    services.Remove(step);
                }

                services.AddSingleton<ITerrainPhase>(fetchPhase);
            });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        starterId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tbp-run-{suffix}@t.local");
        starter = await AuthHelper.BearerClientAsync(factory, $"tbp-run-{suffix}@t.local");
        // Execute and nothing else — not even the right to read a build back — so that what the
        // submit route is measured against is exactly the right it names.
        await GrantAsync(starterId, AccessAction.Execute);

        // Granted nothing. The ruleset every account joins opens map layers, tags, taxonomies and
        // the caver directory and says nothing about terrain, so this account genuinely holds no
        // terrain right rather than being assumed to hold none.
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tbp-out-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"tbp-out-{suffix}@t.local");

        anonymous = factory.CreateClient();
    }

    /// <summary>
    /// The whole permission table for starting a build, in one test, and what a caller gets back:
    /// the build, not the queue row that will run it.
    /// </summary>
    [Fact]
    public async Task Starting_a_build_takes_the_terrain_execute_right_and_nothing_less()
    {
        var request = SomeRequest(22.10, 46.10);

        (await anonymous.PostAsJsonAsync("/api/v1/terrain/builds", request)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);

        var refused = await outsider.PostAsJsonAsync("/api/v1/terrain/builds", request);
        var refusedBody = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, refusedBody);
        CodeOf(refusedBody).ShouldBe("access.forbidden");

        // The pass that makes the refusal above mean something.
        var accepted = await starter.PostAsJsonAsync("/api/v1/terrain/builds", request);
        var body = await accepted.Content.ReadAsStringAsync();
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created, body);

        var build = JsonDocument.Parse(body).RootElement;
        var id = build.GetProperty("id").GetGuid();
        build.GetProperty("status").GetString().ShouldBe("queued");
        build.GetProperty("phase").GetString().ShouldBe("pending");
        build.GetProperty("progress").GetInt32().ShouldBe(0);
        build.GetProperty("isActive").GetBoolean().ShouldBeFalse();
        build.GetProperty("requestedMaxDepth").GetInt32().ShouldBe(request.MaxDepth);
        build.GetProperty("extent").GetProperty("type").GetString().ShouldBe("Polygon");
        accepted.Headers.Location!.ToString().ShouldEndWith(id.ToString());

        // The row and the queue entry are written together, so there is no build nothing will run
        // and no job pointing at a build that is not there.
        var job = await QueuedJobAsync(id);
        job.ShouldNotBeNull();
        job.RequestedBy.ShouldBe(starterId);
    }

    /// <summary>
    /// One rectangle at a time. Nothing reports back while a build runs except the build's own row,
    /// so an administrator who has waited without seeing much cannot tell a slow start from a lost
    /// request — and pressing the button again would put five copies of hours of work in the queue.
    /// </summary>
    [Fact]
    public async Task A_second_build_of_the_same_area_is_refused_while_the_first_is_unfinished()
    {
        var request = SomeRequest(22.20, 46.20);

        var first = await starter.PostAsJsonAsync("/api/v1/terrain/builds", request);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var firstId = JsonDocument.Parse(await first.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();

        var second = await starter.PostAsJsonAsync("/api/v1/terrain/builds", request);
        var secondBody = await second.Content.ReadAsStringAsync();
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict, secondBody);
        CodeOf(secondBody).ShouldBe("terrain_build.already_building");

        // A neighbouring rectangle is a different rectangle: the guard is about repeating one
        // request, not about building terrain twice in a day.
        var elsewhere = await starter.PostAsJsonAsync("/api/v1/terrain/builds", SomeRequest(23.20, 47.20));
        elsewhere.StatusCode.ShouldBe(
            HttpStatusCode.Created, await elsewhere.Content.ReadAsStringAsync());

        // And once the first has finished, the same area may be built again — otherwise this would
        // be a guard that permanently forbids rebuilding anywhere terrain has ever been built.
        await FinishAsync(firstId);
        var again = await starter.PostAsJsonAsync("/api/v1/terrain/builds", request);
        again.StatusCode.ShouldBe(HttpStatusCode.Created, await again.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// An area dragged out by a factor of a hundred is refused before anything is queued, with the
    /// limit in the sentence — a refusal that does not say how large is one somebody can only
    /// respond to by guessing.
    /// </summary>
    [Fact]
    public async Task An_area_larger_than_this_installation_builds_is_refused_with_its_own_code()
    {
        var absurd = new TerrainBuildSubmitRequest(0d, 0d, 30d, 30d, 13, null, null);

        var refused = await starter.PostAsJsonAsync("/api/v1/terrain/builds", absurd);
        var body = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        CodeOf(body).ShouldBe(TerrainBuildRequestRules.ExtentTooLargeCode);
        body.ShouldContain(TerrainBuildRequestRules.MaxAreaSquareDegrees.ToString("0.##"));

        // Nothing was written: a refused request must not leave a row behind for somebody to
        // wonder about.
        (await QueuedJobCountAsync()).ShouldBe(0);

        // A rectangle inside the limit goes through, so the guard is a limit and not a wall.
        var accepted = await starter.PostAsJsonAsync("/api/v1/terrain/builds", SomeRequest(22.30, 46.30));
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created, await accepted.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A depth outside what can be built is refused with its own code rather than as a shapeless
    /// validation failure, because a screen has to be able to say which number was wrong.
    /// </summary>
    [Fact]
    public async Task A_depth_that_cannot_be_built_is_refused_with_its_own_code()
    {
        var tooDeep = SomeRequest(22.40, 46.40) with { MaxDepth = TerrainBuildRequestRules.MaxDepth + 1 };

        var refused = await starter.PostAsJsonAsync("/api/v1/terrain/builds", tooDeep);
        var body = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        CodeOf(body).ShouldBe(TerrainBuildRequestRules.DepthInvalidCode);

        var accepted = await starter.PostAsJsonAsync(
            "/api/v1/terrain/builds", tooDeep with { MaxDepth = TerrainBuildRequestRules.MaxDepth });
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created, await accepted.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The walk runs the steps that exist, in order, and stops at the first one nothing implements
    /// yet — which is a run that <b>succeeded</b>, at the phase it reached, and is not the terrain
    /// the scene draws. Then it is handed the same build again, the way a restart hands one back,
    /// and the step whose output is already there is skipped rather than redone.
    /// </summary>
    [Fact]
    public async Task A_build_stops_where_the_chain_stops_and_a_resumed_one_skips_what_is_already_done()
    {
        var id = await SubmitAsync(SomeRequest(22.50, 46.50));

        await RunAsync(id);

        fetchPhase.Runs.ShouldBe(1);
        var after = await ReadAsync(id);

        // Three separate questions, three separate answers. The run finished, it got as far as
        // obtaining rasters, and it is not what the scene draws.
        after.Status.ShouldBe(TerrainBuildStatus.Succeeded);
        after.Phase.ShouldBe(TerrainBuildPhase.Fetch);
        after.IsActive.ShouldBeFalse();
        after.Progress.ShouldBe(TerrainPhases.Overall(TerrainBuildPhase.Fetch, 100));
        after.ErrorCode.ShouldBeNull();
        after.StartedAt.ShouldNotBeNull();
        after.FinishedAt.ShouldNotBeNull();

        // The step wrote where it was told to, and the run's own working space did not survive it.
        Directory.Exists(Path.Combine(buildRoot, id.ToString("N"), "input")).ShouldBeTrue();
        Directory.Exists(Path.Combine(buildRoot, id.ToString("N"), "scratch")).ShouldBeFalse();

        // The machine died in the middle: the row is left running and the queue hands it back.
        await InterruptAsync(id);
        await RunAsync(id, attempts: 2);

        // Fetched once, not twice. Anything else means a restart halfway through a long build
        // starts the whole chain from the beginning, every time.
        fetchPhase.Runs.ShouldBe(1);
        fetchPhase.Skips.ShouldBe(1);

        var resumed = await ReadAsync(id);
        resumed.Status.ShouldBe(TerrainBuildStatus.Succeeded);
        resumed.Phase.ShouldBe(TerrainBuildPhase.Fetch);

        // The clock still says when the build first started, not when the machine came back —
        // otherwise "how long has this been going" hides exactly the thing it is asked about.
        resumed.StartedAt.ShouldBe(after.StartedAt);
    }

    /// <summary>
    /// A build handed back after it had already published is recorded as finished rather than
    /// walked again.
    /// </summary>
    /// <remarks>
    /// Publishing moves the pyramid out of the build's own folder rather than copying it, so a
    /// process that died between putting it at its address and writing down that it had leaves a
    /// build whose folder holds no tiles at all. Walked again, the meshing step asks that folder
    /// whether the work is done, is told no, and either repeats hours of it — leaving the second
    /// pyramid stranded, since the address already answers — or, on an installation with no tile
    /// maker of its own, fails a build that had actually succeeded and may be the terrain on
    /// screen.
    /// </remarks>
    [Fact]
    public async Task A_build_that_had_already_published_is_finished_rather_than_started_again()
    {
        var id = await SubmitAsync(SomeRequest(23.40, 47.40));

        // The machine died between the last rename and the row being written: the pyramid is at its
        // address, the build's own folder has none, and the row still says running.
        var published = Path.Combine(publishRoot, id.ToString("N"));
        Directory.CreateDirectory(published);
        await File.WriteAllTextAsync(
            Path.Combine(published, TerrainPyramid.ManifestFileName),
            """{"format":"quantized-mesh-1.0","version":"1.1.0-abcdef123456"}""");
        await InterruptAsync(id);

        await RunAsync(id, attempts: 2);

        // Nothing was walked at all — not run, and not even asked whether it was already done.
        fetchPhase.Runs.ShouldBe(0);
        fetchPhase.Skips.ShouldBe(0);

        var after = await ReadAsync(id);
        after.Status.ShouldBe(TerrainBuildStatus.Succeeded);
        after.Phase.ShouldBe(TerrainBuildPhase.Publish);
        after.Progress.ShouldBe(100);
        after.ErrorCode.ShouldBeNull();

        // And the pyramid is still where terrain is served from: finishing the row must not have
        // touched it.
        File.Exists(Path.Combine(published, TerrainPyramid.ManifestFileName)).ShouldBeTrue();
    }

    /// <summary>
    /// Recording a failure must not itself fail. The tools this pipeline drives fail with kilobytes
    /// of native text, and both columns that could hold it are bounded — so the reason kept on the
    /// build is a short code of our own, the tool's words are truncated into the log tail, and what
    /// reaches the queue row is shorter still.
    /// </summary>
    [Fact]
    public async Task A_failure_is_recorded_as_a_short_reason_however_much_the_tool_had_to_say()
    {
        var id = await SubmitAsync(SomeRequest(22.60, 46.60));

        // Twenty thousand characters: five times what the queue's own column holds, and more than
        // twice the build's log tail.
        fetchPhase.FailWith = new TerrainBuildException(
            TerrainBuildFailures.FetchFailed,
            "Could not obtain elevation rasters for that area.",
            new string('x', 20_000));

        var thrown = await Should.ThrowAsync<Exception>(() => RunAsync(id));

        // Short enough for the queue's own column, and carrying the code rather than the tool's
        // output: an over-long message would be refused by the database while the failure was being
        // recorded, which leaves the queue row reading as running for ever.
        thrown.Message.Length.ShouldBeLessThan(200);
        thrown.Message.ShouldContain(TerrainBuildFailures.FetchFailed);

        var failed = await ReadAsync(id);
        failed.Status.ShouldBe(TerrainBuildStatus.Failed);
        failed.Phase.ShouldBe(TerrainBuildPhase.Fetch);
        failed.ErrorCode.ShouldBe(TerrainBuildFailures.FetchFailed);
        failed.LogTail!.Length.ShouldBe(8000);
        failed.FinishedAt.ShouldNotBeNull();
        Directory.Exists(Path.Combine(buildRoot, id.ToString("N"), "scratch")).ShouldBeFalse();
    }

    /// <summary>
    /// A build that has already ended badly and is handed over again is stopped, because the only
    /// thing that hands a finished row back is a process dying and dying twice over one build is a
    /// fact about the build. Paired, in the same test, with the interrupted build that must still
    /// be resumed — the two arrive identically and telling them apart is the whole rule.
    /// </summary>
    [Fact]
    public async Task A_build_that_already_ended_badly_is_not_started_again_but_an_interrupted_one_is()
    {
        var interrupted = await SubmitAsync(SomeRequest(22.70, 46.70));
        await InterruptAsync(interrupted);
        await RunAsync(interrupted, attempts: 2);
        (await ReadAsync(interrupted)).Status.ShouldBe(TerrainBuildStatus.Succeeded);
        fetchPhase.Runs.ShouldBe(1);

        var repeating = await SubmitAsync(SomeRequest(22.80, 46.80));
        await InterruptAsync(
            repeating,
            previousFailure: TerrainBuildFailures.FetchFailed,
            previousLog: "what the tool said the first time");
        await RunAsync(repeating, attempts: 2);

        // Nothing ran a second time, and the row says why it will not be tried again.
        fetchPhase.Runs.ShouldBe(1);
        var stopped = await ReadAsync(repeating);
        stopped.Status.ShouldBe(TerrainBuildStatus.Failed);
        stopped.ErrorCode.ShouldBe(TerrainBuildFailures.RepeatedFailure);

        // And the earlier failure's own words survive being stopped for having them. This build is
        // the one somebody most needs to diagnose, and what it said cannot be produced again.
        stopped.LogTail.ShouldBe("what the tool said the first time");
    }

    /// <summary>
    /// What a step says is kept while the build is still running, and what a failure says is added
    /// to it rather than written over it.
    /// </summary>
    /// <remarks>
    /// The progress message is one sentence about now and is overwritten every few seconds, so
    /// without a tail that accumulates, a build in flight can show nothing of what its steps have
    /// said — and the words of the outside tools this pipeline will drive are exactly where a tool
    /// that has quietly degraded says so.
    /// </remarks>
    [Fact]
    public async Task What_a_step_says_while_it_runs_is_kept_and_a_failure_is_added_to_it()
    {
        var id = await SubmitAsync(SomeRequest(22.90, 46.90));
        fetchPhase.Says = "gathered 4 cells";

        await RunAsync(id);

        // Written while the step was running, by the step, and still there after it finished: a
        // tail only written at the end would leave an hours-long run with nothing to show.
        var succeeded = await ReadAsync(id);
        succeeded.Status.ShouldBe(TerrainBuildStatus.Succeeded);
        succeeded.LogTail.ShouldNotBeNull();
        succeeded.LogTail.ShouldContain("gathered 4 cells");

        // Another build says the same thing and then breaks. What the tool says at the end is
        // added to what was already there, so the run reads in order rather than as its last
        // sentence alone — the earlier lines are usually where the reason is.
        var broken = await SubmitAsync(SomeRequest(22.92, 46.92));
        fetchPhase.FailWith = new TerrainBuildException(
            TerrainBuildFailures.FetchFailed, "Could not obtain elevation rasters.", "the tool's last words");
        await Should.ThrowAsync<Exception>(() => RunAsync(broken));

        var failed = await ReadAsync(broken);
        var tail = failed.LogTail.ShouldNotBeNull();
        tail.ShouldContain("gathered 4 cells");
        tail.ShouldContain("the tool's last words");
    }

    /// <summary>
    /// A defect of our own is logged in full for the operator and summarised on the row, because
    /// the row is served to anybody holding the right to read a build.
    /// </summary>
    /// <remarks>
    /// A .NET stack trace names types, source files and line numbers, and an exception from a
    /// driver names hosts, accounts and directory layouts. None of that is the business of an
    /// account that was granted nothing but the right to look at terrain builds — and the log tail
    /// is described to its readers as what the tools said, which a stack trace is not.
    /// </remarks>
    [Fact]
    public async Task A_defect_of_our_own_is_summarised_on_the_row_and_left_in_full_only_in_the_log()
    {
        var id = await SubmitAsync(SomeRequest(22.95, 46.95));
        fetchPhase.BreakWith = new InvalidOperationException(
            @"Npgsql: connection to host 10.0.0.7 as silexgis failed, C:\srv\silexgis\secret.cfg");

        await Should.ThrowAsync<Exception>(() => RunAsync(id));

        var failed = await ReadAsync(id);
        failed.Status.ShouldBe(TerrainBuildStatus.Failed);
        failed.ErrorCode.ShouldBe(TerrainBuildFailures.Unexpected);

        // Neither the exception's own words nor its stack reach the row.
        var published = (failed.LogTail ?? "") + (failed.Message ?? "");
        published.ShouldNotContain("10.0.0.7");
        published.ShouldNotContain("secret.cfg");
        published.ShouldNotContain("at SilexGis.");
    }

    /// <summary>
    /// A build whose working directory cannot be made says so on its own row, rather than sitting
    /// at queued for ever with nothing on it to read.
    /// </summary>
    /// <remarks>
    /// Making the directory is the first thing a run does with the disk, and it is the step most
    /// likely to fail on a machine rather than in this code: a volume that was never mounted, a
    /// path the service account does not own, a disk with nothing left on it. Done before the row
    /// is marked as running, or outside the guard that records failures, the throw would leave a
    /// row that still says queued — indistinguishable from one the worker has not reached, so
    /// whoever is waiting for it waits for ever and the queue looks healthy.
    /// </remarks>
    [Fact]
    public async Task A_build_whose_directory_cannot_be_made_records_the_failure_on_its_row()
    {
        var id = await SubmitAsync(SomeRequest(23.30, 47.30));

        // A file where the build's own folder has to go. Creating a directory beneath a file fails
        // on every operating system this runs on, and needs no permissions the test may not have.
        Directory.CreateDirectory(buildRoot);
        await File.WriteAllTextAsync(Path.Combine(buildRoot, id.ToString("N")), "not a directory");

        await Should.ThrowAsync<Exception>(() => RunAsync(id));

        var failed = await ReadAsync(id);
        failed.Status.ShouldBe(TerrainBuildStatus.Failed);
        failed.ErrorCode.ShouldBe(TerrainBuildFailures.Unexpected);

        // And the path it could not make is not published on the row, which any holder of the
        // terrain read right can fetch: where this server keeps its data is not theirs to learn.
        ((failed.LogTail ?? "") + (failed.Message ?? "")).ShouldNotContain(buildRoot);
    }

    /// <summary>
    /// Two submissions of one rectangle arriving together: one starts a build and the other is
    /// refused, which is the case the guard exists for and the one a read followed by a write
    /// cannot answer.
    /// </summary>
    /// <remarks>
    /// The guard is there because pressing the button again is the obvious response to a build that
    /// has not visibly started, and pressing it again sends a second request while the first is
    /// still in flight. Checked and then written without anything holding the two together, both
    /// requests see an empty queue and both queue hours of work over the same ground.
    /// </remarks>
    [Fact]
    public async Task Two_submissions_of_one_rectangle_at_once_start_exactly_one_build()
    {
        var request = SomeRequest(23.10, 47.10);

        var both = await Task.WhenAll(
            starter.PostAsJsonAsync("/api/v1/terrain/builds", request),
            starter.PostAsJsonAsync("/api/v1/terrain/builds", request));

        var codes = both.Select(r => r.StatusCode).Order().ToList();
        codes.ShouldBe(
            [HttpStatusCode.Created, HttpStatusCode.Conflict],
            $"got {string.Join(", ", codes)}");

        var refused = both.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        CodeOf(await refused.Content.ReadAsStringAsync()).ShouldBe("terrain_build.already_building");

        // And only one row exists, which is the thing the status codes stand for.
        (await QueuedJobCountAsync()).ShouldBe(1);
    }

    private static TerrainBuildSubmitRequest SomeRequest(double west, double south) =>
        new(west, south, west + 0.05, south + 0.05, 13, null, null);

    private async Task<Guid> SubmitAsync(TerrainBuildSubmitRequest request)
    {
        var response = await starter.PostAsJsonAsync("/api/v1/terrain/builds", request);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Runs the handler directly rather than waiting for a worker, so the assertions are about what
    /// the handler did and not about how quickly something got to it.
    /// </summary>
    private async Task RunAsync(Guid buildId, int attempts = 1)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.TerrainBuild);

        await handler.ExecuteAsync(
            new ProcessingJob
            {
                Kind = ProcessingJobKinds.TerrainBuild,
                Attempts = attempts,
                Payload = JsonSerializer.Serialize(
                    new TerrainBuildPayload(buildId), JsonSerializerOptions.Web),
            },
            CancellationToken.None);
    }

    /// <summary>Puts a build back the way a process that died halfway through would leave it.</summary>
    private async Task InterruptAsync(
        Guid buildId, string? previousFailure = null, string? previousLog = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.TerrainBuilds.Where(b => b.Id == buildId).ExecuteUpdateAsync(s => s
            .SetProperty(b => b.Status, TerrainBuildStatus.Running)
            .SetProperty(b => b.ErrorCode, previousFailure)
            .SetProperty(b => b.LogTail, b => previousLog ?? b.LogTail)
            .SetProperty(b => b.FinishedAt, (DateTimeOffset?)null));
    }

    private async Task FinishAsync(Guid buildId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.TerrainBuilds.Where(b => b.Id == buildId).ExecuteUpdateAsync(
            s => s.SetProperty(b => b.Status, TerrainBuildStatus.Succeeded));
    }

    private async Task<TerrainBuild> ReadAsync(Guid buildId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TerrainBuilds.AsNoTracking().FirstAsync(b => b.Id == buildId);
    }

    private async Task<ProcessingJob?> QueuedJobAsync(Guid buildId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var jobs = await db.ProcessingJobs.AsNoTracking()
            .Where(j => j.Kind == ProcessingJobKinds.TerrainBuild)
            .ToListAsync();

        // The payload is a jsonb column, so it is matched here rather than in the query.
        return jobs.FirstOrDefault(j => j.Payload.Contains(buildId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private async Task<int> QueuedJobCountAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ProcessingJobs.CountAsync(j => j.Kind == ProcessingJobKinds.TerrainBuild);
    }

    private static string? CodeOf(string problemBody) =>
        JsonDocument.Parse(problemBody).RootElement.GetProperty("code").GetString();

    /// <summary>
    /// A rule naming one person directly, written straight into storage: the authoring surface
    /// refuses rules handing out more than their author holds, which is exactly what a fixture
    /// needs to do.
    /// </summary>
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

    /// <summary>
    /// Everything this class queued, taken out of the shared queue again. Left there, the next test
    /// class's terrain worker would claim these and run them against a host with no steps and no
    /// directories of theirs — work belonging to a class that has already gone.
    /// </summary>
    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.ProcessingJobs.Where(j => j.Kind == ProcessingJobKinds.TerrainBuild).ExecuteDeleteAsync();
        await db.TerrainBuilds.ExecuteDeleteAsync();
    }

    public void Dispose()
    {
        starter?.Dispose();
        outsider?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();

        if (Directory.Exists(buildRoot))
        {
            Directory.Delete(buildRoot, recursive: true);
        }

        if (Directory.Exists(publishRoot))
        {
            Directory.Delete(publishRoot, recursive: true);
        }
    }

    /// <summary>
    /// A step of the chain that records what it was asked to do, and reports itself already done
    /// once it has left its mark on disk.
    /// </summary>
    /// <remarks>
    /// It answers "am I already done" from the input directory rather than from a field, because
    /// that is the question the real steps have to answer and answering it from memory would prove
    /// nothing about a process that has been restarted.
    /// </remarks>
    private sealed class RecordingPhase : ITerrainPhase
    {
        public TerrainBuildPhase Phase => TerrainBuildPhase.Fetch;

        public int Runs { get; private set; }

        public int Skips { get; private set; }

        public TerrainBuildException? FailWith { get; set; }

        /// <summary>A defect rather than something a tool said: a failure of nobody's design.</summary>
        public Exception? BreakWith { get; set; }

        /// <summary>What this step says while it runs, added to the build's log tail.</summary>
        public string? Says { get; set; }

        public Task<bool> IsAlreadyDoneAsync(TerrainBuildContext context, CancellationToken ct)
        {
            var done = File.Exists(MarkerOf(context));
            if (done)
            {
                Skips++;
            }

            return Task.FromResult(done);
        }

        public async Task RunAsync(TerrainBuildContext context, CancellationToken ct)
        {
            await context.ReportAsync(50, "obtaining rasters", ct);

            if (Says is { } line)
            {
                await context.LogAsync(line, ct);
            }

            if (FailWith is { } failure)
            {
                throw failure;
            }

            if (BreakWith is { } defect)
            {
                throw defect;
            }

            await File.WriteAllTextAsync(MarkerOf(context), "one raster's worth", ct);
            Runs++;
        }

        private static string MarkerOf(TerrainBuildContext context) =>
            Path.Combine(context.Directories.Input, "fetched.txt");
    }
}
