// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Features.SurveyUploads;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Scans;

namespace SilexGis.Api.Tests;

/// <summary>
/// A point cloud's run once this installation has a worker: the thinning estimated, the cloud
/// tiled, each product stored as an artefact, the facts and the advice on the scan; and the ways
/// it ends when the worker is absent, silent, failing or slow. The worker is a canned one on the
/// far side of the hand-off directory, answering with what the test puts there.
/// </summary>
/// <remarks>
/// Every cloud is a synthetic header with zero points and every product is bytes the test wrote;
/// nothing here is a real scan.
/// </remarks>
public sealed class SurveyUploadPointCloudWorkerTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const long WarnPoints = 100;
    private const long AdvisePoints = 1000;
    private const long WarnTileBytes = 10_000;

    private readonly PostgresFixture postgres;
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string handoffRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];
    private CannedScanWorker worker = null!;

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private Guid readerId;
    private long caveTypeId;

    public SurveyUploadPointCloudWorkerTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-supcw-{Guid.NewGuid():N}");
        handoffRoot = Path.Combine(filesRoot, "handoff");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            ["Scans:ProcessingEnabled"] = "true",
            ["Scans:HandoffRoot"] = handoffRoot,
            ["Scans:PickupSeconds"] = "1",
            ["Scans:JobTimeoutSeconds"] = "5",
            ["Scans:DisplayWarnPoints"] = WarnPoints.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Scans:DisplayAdvisePoints"] = AdvisePoints.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Scans:WarnTileBytes"] = WarnTileBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }, JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"supcw-own-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"supcw-read-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"supcw-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"supcw-read-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();

        // The application looks at the directory every second in production; here every 50 ms.
        var handoff = factory.Services.GetRequiredService<ScanWorkerHandoff>();
        handoff.PollInterval = TimeSpan.FromMilliseconds(50);
        handoff.WithdrawalWait = TimeSpan.FromSeconds(1);

        CannedScanWorker.Announce(handoffRoot, PointCloudEstimateStep.StepId, PointCloudDownsampleStep.StepId, Py3dtilesTileStep.StepId);
        worker = new CannedScanWorker(handoffRoot);
    }

    public async ValueTask DisposeAsync()
    {
        await worker.DisposeAsync();
        Dispose();
    }

    public void Dispose()
    {
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            try
            {
                Directory.Delete(filesRoot, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task The_worker_estimates_and_tiles_the_cloud_and_the_products_become_artefacts()
    {
        worker.Answer = Tiling(fullPoints: 30, voxelPoints: [(1, 20), (5, 5)]);
        var caveId = await CreateCaveAsync();
        var id = await SendAsync(owner, "Tiled scan", caveId, ("scan.laz", SyntheticLas.Las(points: 3, compressed: true)));
        await RunPipelineAsync(id);

        worker.Steps.ShouldBe([PointCloudEstimateStep.StepId, Py3dtilesTileStep.StepId]);
        var shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        shown.GetProperty("status").GetString().ShouldBe("succeeded");
        shown.GetProperty("stage").GetString().ShouldBe("store");
        shown.GetProperty("message").GetString()!.ShouldStartWith("Tiled: 3 of 3 points");
        shown.GetProperty("error").ValueKind.ShouldBe(JsonValueKind.Null);

        var cloud = shown.GetProperty("pointCloud");
        cloud.GetProperty("tileCount").GetInt32().ShouldBe(4);
        cloud.GetProperty("tileBytes").GetInt64().ShouldBe(TilesZip.LongLength);
        cloud.GetProperty("tilerVersion").GetString().ShouldBe("test-1.0");
        cloud.GetProperty("hasIntensity").GetBoolean().ShouldBeTrue();
        cloud.GetProperty("displayVoxelM").ValueKind.ShouldBe(JsonValueKind.Null);
        cloud.GetProperty("originShift").EnumerateArray().Select(v => v.GetDouble()).ToArray().ShouldBe([0.5, 0.5, 0.5]);
        var estimate = cloud.GetProperty("estimate");
        estimate.GetProperty("full").GetProperty("points").GetInt64().ShouldBe(30);
        estimate.GetProperty("voxels").EnumerateArray().Select(r => r.GetProperty("voxelCm").GetDouble()).ToArray().ShouldBe([1, 5]);
        var advice = cloud.GetProperty("advice");
        advice.GetProperty("level").GetString().ShouldBe("none");
        advice.GetProperty("suggestedVoxelCm").ValueKind.ShouldBe(JsonValueKind.Null);
        advice.GetProperty("warnPoints").GetInt64().ShouldBe(WarnPoints);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var artefacts = await db.ScanArtifacts.AsNoTracking().Where(a => a.SurveyUploadId == id).ToListAsync();
        artefacts.Count.ShouldBe(3);
        var original = artefacts.Single(a => a.StepId == ScanSteps.UploadStepId);
        var report = artefacts.Single(a => a.StepId == PointCloudEstimateStep.StepId);
        var tiles = artefacts.Single(a => a.StepId == Py3dtilesTileStep.StepId);
        report.Kind.ShouldBe(ScanArtifactKind.Report);
        report.InputArtifactIds.ShouldBe([original.Id]);
        tiles.Kind.ShouldBe(ScanArtifactKind.PointCloudTiles);
        tiles.Format.ShouldBe("zip");
        tiles.ToolVersion.ShouldBe("test-1.0");
        tiles.InputArtifactIds.ShouldBe([original.Id]);
        tiles.SizeBytes.ShouldBe(TilesZip.LongLength);

        var model = await db.SurveyModels.AsNoTracking().SingleAsync(m => m.SurveyUploadId == id);
        model.ConvertedFileId.ShouldBe(tiles.FileId);
        var stored = await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == tiles.FileId);
        // The lineage is on the artefact rows, not on the files' "converted from" link, which
        // means "the one rendition of this upload" and is unique per upload: the report and the
        // tiles both derive from the original, and both are stored.
        stored.ConvertedFromFileId.ShouldBeNull();
        stored.MimeType.ShouldBe("application/zip");
        (await db.StoredFiles.AsNoTracking().Where(f => f.Id == report.FileId).Select(f => f.ConvertedFromFileId).SingleAsync()).ShouldBeNull();

        // The job directories are the application's to remove, and were.
        Directory.GetDirectories(Path.Combine(handoffRoot, "jobs")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Above_the_thresholds_the_run_stops_at_the_estimate_and_suggests_the_smallest_voxel_that_fits()
    {
        worker.Answer = Tiling(fullPoints: 500, voxelPoints: [(1, 400), (2, 150), (5, 60), (10, 20)]);
        var caveId = await CreateCaveAsync();
        var id = await SendAsync(owner, "Big scan", caveId, ("scan.laz", SyntheticLas.Las(points: 3, compressed: true)));
        await RunPipelineAsync(id);

        // Nobody has chosen how much to show, so nothing is tiled unasked: the run ends after
        // the estimate, succeeded, with the table and the advice on the scan and a note saying
        // what to do next. No tiling job reached the worker.
        worker.Steps.ShouldBe([PointCloudEstimateStep.StepId]);
        var shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        shown.GetProperty("status").GetString().ShouldBe("succeeded", shown.ToString());
        shown.GetProperty("stage").GetString().ShouldBe("downsample");
        shown.GetProperty("progress").GetInt32().ShouldBe(100);
        shown.GetProperty("message").GetString().ShouldBe(SurveyPipelineNotes.ChoiceNeeded(3));
        var cloud = shown.GetProperty("pointCloud");
        cloud.GetProperty("tileBytes").ValueKind.ShouldBe(JsonValueKind.Null);
        cloud.GetProperty("estimate").GetProperty("full").GetProperty("points").GetInt64().ShouldBe(500);
        var advice = cloud.GetProperty("advice");
        advice.GetProperty("level").GetString().ShouldBe("warn");
        advice.GetProperty("suggestedVoxelCm").GetDouble().ShouldBe(5);

        // A plain rerun stops at the same place, reusing the estimate; a rerun that chooses
        // goes on from it — the estimate is not repeated, the thinning and the tiling run.
        (await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", new { })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await RunPipelineAsync(id);
        worker.Steps.ShouldBe([PointCloudEstimateStep.StepId]);
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}")).GetProperty("message").GetString()
            .ShouldBe(SurveyPipelineNotes.ChoiceNeeded(3));

        var chosen = await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", new { display = new { method = "voxel", voxelCm = 5 } });
        chosen.StatusCode.ShouldBe(HttpStatusCode.Accepted, await chosen.Content.ReadAsStringAsync());
        await RunPipelineAsync(id);
        worker.Steps.ShouldBe([PointCloudEstimateStep.StepId, PointCloudDownsampleStep.StepId, Py3dtilesTileStep.StepId]);
        shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        shown.GetProperty("status").GetString().ShouldBe("succeeded", shown.ToString());
        shown.GetProperty("stage").GetString().ShouldBe("store");
        shown.GetProperty("message").GetString()!.ShouldStartWith("Tiled: 60 of 3 points");
        shown.GetProperty("pointCloud").GetProperty("displayVoxelM").GetDouble().ShouldBe(0.05);

        // Strongly advised past the second threshold, and the coarsest voxel offered when none
        // fits. Choosing the whole cloud all the same is a choice, and the run tiles it.
        worker.Answer = Tiling(fullPoints: 5000, voxelPoints: [(1, 4000), (10, 2000)]);
        var bigger = await SendAsync(owner, "Bigger scan", caveId, ("scan2.laz", SyntheticLas.Las(points: 3, compressed: true, size: (2, 2, 2))));
        await RunPipelineAsync(bigger);
        advice = (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{bigger}")).GetProperty("pointCloud").GetProperty("advice");
        advice.GetProperty("level").GetString().ShouldBe("advise");
        advice.GetProperty("suggestedVoxelCm").GetDouble().ShouldBe(10);
        worker.Steps.Skip(3).ShouldBe([PointCloudEstimateStep.StepId]);

        var whole = await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{bigger}/rerun", new { display = new { method = "full" } });
        whole.StatusCode.ShouldBe(HttpStatusCode.Accepted, await whole.Content.ReadAsStringAsync());
        await RunPipelineAsync(bigger);
        worker.Steps.Skip(4).ShouldBe([Py3dtilesTileStep.StepId]);
        shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{bigger}");
        shown.GetProperty("status").GetString().ShouldBe("succeeded", shown.ToString());
        shown.GetProperty("message").GetString()!.ShouldStartWith("Tiled: 3 of 3 points");
        shown.GetProperty("pointCloud").GetProperty("displayVoxelM").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_choice_sent_with_the_upload_is_applied_without_stopping_at_the_estimate()
    {
        worker.Answer = Tiling(fullPoints: 500, voxelPoints: [(1, 400), (2, 150), (5, 60), (10, 20)]);
        var caveId = await CreateCaveAsync();
        var id = await SendAsync(owner, "Decided scan", caveId, ("scan.laz", SyntheticLas.Las(points: 3, compressed: true)),
            display: new { method = "voxel", voxelCm = 10 });
        await RunPipelineAsync(id);

        worker.Steps.ShouldBe([PointCloudEstimateStep.StepId, PointCloudDownsampleStep.StepId, Py3dtilesTileStep.StepId]);
        var shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        shown.GetProperty("status").GetString().ShouldBe("succeeded", shown.ToString());
        shown.GetProperty("message").GetString()!.ShouldStartWith("Tiled: 20 of 3 points");
        shown.GetProperty("pointCloud").GetProperty("displayVoxelM").GetDouble().ShouldBe(0.1);

        // A choice the thinning step's schema refuses is refused when the upload is opened.
        var refused = await owner.PostAsJsonAsync("/api/v1/survey-uploads", new
        {
            source = "pointcloud",
            cave = new { id = caveId },
            client = new { name = "silexgis tests", version = "1" },
            surveyTitle = "Refused scan",
            files = new[] { File_("scan.laz", 100) },
            display = new { method = "voxel", voxelCm = 5000 },
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_rerun_reuses_what_still_matches_and_repeats_only_what_changed()
    {
        worker.Answer = Tiling(fullPoints: 30, voxelPoints: [(1, 20), (5, 5)]);
        var caveId = await CreateCaveAsync();
        var id = await SendAsync(owner, "Rerun scan", caveId, ("scan.laz", SyntheticLas.Las(points: 3, compressed: true)));
        await RunPipelineAsync(id);
        var first = await ArtefactIdsAsync(id);

        // Nothing changed: no job reaches the worker, and the note says the tiles were kept.
        (await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", new { })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await RunPipelineAsync(id);
        worker.Taken.ShouldBe(2);
        var shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        shown.GetProperty("status").GetString().ShouldBe("succeeded");
        shown.GetProperty("message").GetString()!.ShouldContain("kept");
        (await ArtefactIdsAsync(id)).ShouldBe(first);

        // A thinned copy chosen: the estimate is kept, the thinning and the tiling run, and the
        // tiles now derive from the thinned copy.
        var rerun = await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", new { display = new { method = "voxel", voxelCm = 5 } });
        rerun.StatusCode.ShouldBe(HttpStatusCode.Accepted, await rerun.Content.ReadAsStringAsync());
        await RunPipelineAsync(id);
        worker.Steps.Skip(2).ShouldBe([PointCloudDownsampleStep.StepId, Py3dtilesTileStep.StepId]);
        shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        shown.GetProperty("status").GetString().ShouldBe("succeeded", shown.ToString());
        shown.GetProperty("message").GetString()!.ShouldStartWith("Tiled: 5 of 3 points");
        var cloud = shown.GetProperty("pointCloud");
        cloud.GetProperty("displayVoxelM").GetDouble().ShouldBe(0.05);
        cloud.GetProperty("displayPointCount").GetInt64().ShouldBe(5);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var artefacts = await db.ScanArtifacts.AsNoTracking().Where(a => a.SurveyUploadId == id).ToListAsync();
            artefacts.Count.ShouldBe(4);
            var display = artefacts.Single(a => a.Kind == ScanArtifactKind.PointCloudLazDisplay);
            var tiles = artefacts.Single(a => a.Kind == ScanArtifactKind.PointCloudTiles);
            tiles.InputArtifactIds.ShouldBe([display.Id]);
            tiles.Id.ShouldNotBe(first.Single(a => a.Step == Py3dtilesTileStep.StepId).Id);
            artefacts.Single(a => a.StepId == PointCloudEstimateStep.StepId).Id.ShouldBe(first.Single(a => a.Step == PointCloudEstimateStep.StepId).Id);
            // The earlier tiles' file row went with its artefact.
            var earlierTilesFile = first.Single(a => a.Step == Py3dtilesTileStep.StepId).FileId;
            (await db.StoredFiles.AsNoTracking().AnyAsync(f => f.Id == earlierTilesFile)).ShouldBeFalse();
            (await db.SurveyModels.AsNoTracking().SingleAsync(m => m.SurveyUploadId == id)).ConvertedFileId.ShouldBe(tiles.FileId);
        }

        // Another voxel size: the thinning and the tiling are replaced again, and the earlier
        // copy's file row and the earlier tiles' go with their artefacts — nothing is orphaned,
        // and nothing cascades.
        var finer = await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", new { display = new { method = "voxel", voxelCm = 1 } });
        finer.StatusCode.ShouldBe(HttpStatusCode.Accepted, await finer.Content.ReadAsStringAsync());
        var before = await ArtefactIdsAsync(id);
        await RunPipelineAsync(id);
        worker.Steps.Skip(4).ShouldBe([PointCloudDownsampleStep.StepId, Py3dtilesTileStep.StepId]);
        shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        shown.GetProperty("status").GetString().ShouldBe("succeeded", shown.ToString());
        shown.GetProperty("pointCloud").GetProperty("displayVoxelM").GetDouble().ShouldBe(0.01);
        shown.GetProperty("pointCloud").GetProperty("displayPointCount").GetInt64().ShouldBe(20);
        Guid displayId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var artefacts = await db.ScanArtifacts.AsNoTracking().Where(a => a.SurveyUploadId == id).ToListAsync();
            artefacts.Count.ShouldBe(4);
            var display = artefacts.Single(a => a.Kind == ScanArtifactKind.PointCloudLazDisplay);
            var tiles = artefacts.Single(a => a.Kind == ScanArtifactKind.PointCloudTiles);
            displayId = display.Id;
            tiles.InputArtifactIds.ShouldBe([display.Id]);
            var replaced = before.Where(a => a.Step is PointCloudDownsampleStep.StepId or Py3dtilesTileStep.StepId).Select(a => a.FileId).ToList();
            replaced.Count.ShouldBe(2);
            (await db.StoredFiles.AsNoTracking().CountAsync(f => replaced.Contains(f.Id))).ShouldBe(0);
            (await db.SurveyModels.AsNoTracking().SingleAsync(m => m.SurveyUploadId == id)).ConvertedFileId.ShouldBe(tiles.FileId);
        }

        // Asked to tile again, and nothing else: only the tiling runs, from the same thinned
        // copy — the choice the upload was running with is kept, not reset to the whole cloud.
        var again = await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", new { fromStep = Py3dtilesTileStep.StepId });
        again.StatusCode.ShouldBe(HttpStatusCode.Accepted, await again.Content.ReadAsStringAsync());
        await RunPipelineAsync(id);
        worker.Steps.Skip(6).ShouldBe([Py3dtilesTileStep.StepId]);
        shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        shown.GetProperty("status").GetString().ShouldBe("succeeded", shown.ToString());
        shown.GetProperty("pointCloud").GetProperty("displayVoxelM").GetDouble().ShouldBe(0.01);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var artefacts = await db.ScanArtifacts.AsNoTracking().Where(a => a.SurveyUploadId == id).ToListAsync();
            artefacts.Single(a => a.Kind == ScanArtifactKind.PointCloudLazDisplay).Id.ShouldBe(displayId);
            artefacts.Single(a => a.Kind == ScanArtifactKind.PointCloudTiles).InputArtifactIds.ShouldBe([displayId]);
        }

        // The step to do again applied to that rerun only: the next plain rerun reuses everything.
        (await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", new { })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await RunPipelineAsync(id);
        worker.Taken.ShouldBe(7);
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}")).GetProperty("message").GetString()!.ShouldContain("kept");

        // A choice the step's schema refuses is refused here, before anything is queued.
        var bad = await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", new { display = new { method = "voxel", voxelCm = 0 } });
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var unknown = await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", new { display = new { method = "bogus" } });
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_worker_that_fails_a_step_fails_the_run_at_that_stage_with_its_words()
    {
        worker.Answer = async (directory, request) =>
        {
            if (request.GetProperty("step").GetString() == PointCloudEstimateStep.StepId)
            {
                await Tiling(fullPoints: 30, voxelPoints: [(1, 20)])(directory, request);
                return;
            }

            CannedScanWorker.Finish(directory, "failed", [], reason: "py3dtiles exited 1; its own account is in worker.log", tool: "py3dtiles");
        };
        var caveId = await CreateCaveAsync();
        var id = await SendAsync(owner, "Failing scan", caveId, ("scan.laz", SyntheticLas.Las(points: 3, compressed: true)));
        await Should.ThrowAsync<InvalidOperationException>(RunPipelineAsync(id));

        var shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        shown.GetProperty("status").GetString().ShouldBe("failed");
        shown.GetProperty("stage").GetString().ShouldBe("tile");
        shown.GetProperty("error").GetProperty("code").GetString().ShouldBe(SurveyPipelineFailures.WorkerFailed);
        shown.GetProperty("error").GetProperty("message").GetString()!.ShouldContain("exited 1");
        // The estimate the step before made stands, and the log tail is kept for the cave's editors.
        shown.GetProperty("pointCloud").GetProperty("estimate").ValueKind.ShouldBe(JsonValueKind.Object);
        shown.GetProperty("logTail").GetString()!.ShouldContain("line two");
        Directory.GetDirectories(Path.Combine(handoffRoot, "jobs")).ShouldBeEmpty();
    }

    [Fact]
    public async Task With_no_worker_announced_the_run_fails_saying_so_before_leaving_any_job()
    {
        File.Delete(Path.Combine(handoffRoot, "capabilities.json"));
        var caveId = await CreateCaveAsync();
        var id = await SendAsync(owner, "Unannounced", caveId, ("scan.laz", SyntheticLas.Las(points: 3, compressed: true)));
        await Should.ThrowAsync<InvalidOperationException>(RunPipelineAsync(id));

        var shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        shown.GetProperty("status").GetString().ShouldBe("failed");
        shown.GetProperty("error").GetProperty("code").GetString().ShouldBe(SurveyPipelineFailures.WorkerUnavailable);
        // The header was read all the same; the scan is there, untiled.
        shown.GetProperty("pointCloud").GetProperty("pointCount").GetInt64().ShouldBe(3);
        worker.Taken.ShouldBe(0);
    }

    [Fact]
    public async Task A_job_nothing_takes_fails_the_run_once_the_pickup_wait_has_passed()
    {
        // Announced, but nothing answers: the canned worker has no answer set.
        var caveId = await CreateCaveAsync();
        var id = await SendAsync(owner, "Silent", caveId, ("scan.laz", SyntheticLas.Las(points: 3, compressed: true)));
        await Should.ThrowAsync<InvalidOperationException>(RunPipelineAsync(id));

        var shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        shown.GetProperty("status").GetString().ShouldBe("failed");
        shown.GetProperty("stage").GetString().ShouldBe("downsample");
        shown.GetProperty("error").GetProperty("code").GetString().ShouldBe(SurveyPipelineFailures.WorkerSilent);
        Directory.GetDirectories(Path.Combine(handoffRoot, "jobs")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_step_that_runs_past_the_limit_is_called_off_and_fails_the_run()
    {
        var calledOff = new TaskCompletionSource();
        worker.Answer = async (directory, _) =>
        {
            // Takes the job and works until told to stop, as the real worker does.
            while (!File.Exists(Path.Combine(directory, "cancel")))
            {
                await Task.Delay(50);
            }

            CannedScanWorker.Finish(directory, "cancelled", [], reason: "the job was called off and the tool was stopped");
            calledOff.TrySetResult();
        };
        var caveId = await CreateCaveAsync();
        var id = await SendAsync(owner, "Slow", caveId, ("scan.laz", SyntheticLas.Las(points: 3, compressed: true)));
        await Should.ThrowAsync<InvalidOperationException>(RunPipelineAsync(id));

        await calledOff.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        shown.GetProperty("status").GetString().ShouldBe("failed");
        shown.GetProperty("error").GetProperty("code").GetString().ShouldBe(SurveyPipelineFailures.WorkerTimeout);
        Directory.GetDirectories(Path.Combine(handoffRoot, "jobs")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_point_cloud_is_queued_on_the_long_work_lane()
    {
        var caveId = await CreateCaveAsync();
        var id = await SendAsync(owner, "Lane", caveId, ("scan.laz", SyntheticLas.Las(points: 3, compressed: true)));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        // The payload is a jsonb column, matched in memory: the job kinds a run of this upload
        // is queued under.
        var queued = await db.ProcessingJobs.AsNoTracking()
            .Where(j => j.Status == ProcessingJobStatus.Queued)
            .Select(j => new { j.Kind, j.Payload }).ToListAsync();
        var kinds = queued
            .Where(j => JsonSerializer.Deserialize<SurveyUploadPipelinePayload>(j.Payload, JsonSerializerOptions.Web)?.SurveyUploadId == id)
            .Select(j => j.Kind).ToList();
        kinds.ShouldBe([ProcessingJobKinds.PointCloudPipeline]);
        ProcessingJobKinds.TerrainLane.ShouldContain(ProcessingJobKinds.PointCloudPipeline);
    }

    [Fact]
    public async Task A_georeferenced_cloud_and_its_thinned_copy_are_served_by_the_file_route_only_with_the_exact_location()
    {
        // A protected cave the reader may read, write and place exactly; they send a cloud whose
        // header names a coordinate system and choose a thinned copy of it.
        worker.Answer = Tiling(fullPoints: 30, voxelPoints: [(1, 20), (5, 5)]);
        var caveId = await CreateCaveAsync(locationProtected: true);
        await GrantAsync(caveId, "read, write, viewExactLocation");
        var placed = SyntheticLas.Las(points: 3, compressed: true, epsg: 3844);
        var id = await SendAsync(reader, "Placed scan", caveId, ("placed.laz", placed));
        await RunPipelineAsync(id);
        var thinned = await reader.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", new { display = new { method = "voxel", voxelCm = 5 } });
        thinned.StatusCode.ShouldBe(HttpStatusCode.Accepted, await thinned.Content.ReadAsStringAsync());
        await RunPipelineAsync(id);

        Guid originalFile, displayFile, tilesFile;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var artefacts = await db.ScanArtifacts.AsNoTracking().Where(a => a.SurveyUploadId == id).ToListAsync();
            originalFile = artefacts.Single(a => a.Kind == ScanArtifactKind.PointCloudLaz).FileId;
            displayFile = artefacts.Single(a => a.Kind == ScanArtifactKind.PointCloudLazDisplay).FileId;
            tilesFile = artefacts.Single(a => a.Kind == ScanArtifactKind.PointCloudTiles).FileId;
        }

        // The exact position taken away: the original and the copy — the position itself, in
        // every point — are not served by the file route either, whoever learns their ids. The
        // tiles are in the cloud's own frame and stay readable.
        await GrantAsync(caveId, "read, write");
        (await reader.GetAsync($"/api/v1/files/{originalFile}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/files/{displayFile}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/files/{tilesFile}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // With it back, both are served, with the bytes behind the address.
        await GrantAsync(caveId, "read, write, viewExactLocation");
        var original = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/files/{originalFile}");
        (await reader.GetByteArrayAsync(original.GetProperty("contentUrl").GetString())).ShouldBe(placed);
        (await reader.GetAsync($"/api/v1/files/{displayFile}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- helpers ----

    private static readonly byte[] TilesZip = MakeZip();

    private static byte[] MakeZip()
    {
        using var stream = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in new[] { "tileset.json", "r.pnts", "r0.pnts", "r1.pnts" })
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write("{}"u8);
            }
        }

        return stream.ToArray();
    }

    /// <summary>A worker that answers the estimate with these counts, thins to the chosen voxel's count, and tiles.</summary>
    private static Func<string, JsonElement, Task> Tiling(long fullPoints, (double VoxelCm, long Points)[] voxelPoints) => (directory, request) =>
    {
        var step = request.GetProperty("step").GetString();
        switch (step)
        {
            case PointCloudEstimateStep.StepId:
            {
                request.GetProperty("inputs").GetProperty("pointCloudLaz").GetString().ShouldBe("pointCloudLaz.laz");
                File.Exists(Path.Combine(directory, "pointCloudLaz.laz")).ShouldBeTrue();
                var rows = voxelPoints.Select(v => new { voxelCm = v.VoxelCm, points = v.Points, estimatedTileBytes = v.Points * 17, estimatedSeconds = 17 }).ToArray();
                var data = new
                {
                    pointCount = fullPoints,
                    hasRgb = false,
                    hasIntensity = true,
                    full = new { points = fullPoints, estimatedTileBytes = fullPoints * 17, estimatedSeconds = 17 + fullPoints / 600_000 },
                    voxels = rows,
                };
                var output = CannedScanWorker.Output(directory, "estimate", "estimate.json", JsonSerializer.SerializeToUtf8Bytes(data));
                CannedScanWorker.Finish(directory, "ok", [output], data);
                break;
            }

            case PointCloudDownsampleStep.StepId:
            {
                var voxelCm = request.GetProperty("parameters").GetProperty("voxelCm").GetDouble();
                var kept = voxelPoints.Single(v => v.VoxelCm == voxelCm).Points;
                var output = CannedScanWorker.Output(directory, "pointCloudLazDisplay", "display.laz", SyntheticLas.Las(points: (int)kept, compressed: true));
                CannedScanWorker.Finish(directory, "ok", [output], new { method = "voxel", voxelCm, pointsIn = fullPoints, pointsOut = kept });
                break;
            }

            case Py3dtilesTileStep.StepId:
            {
                var inputs = request.GetProperty("inputs");
                var source = inputs.TryGetProperty("pointCloudLazDisplay", out var display) ? display.GetString()! : inputs.GetProperty("pointCloudLaz").GetString()!;
                File.Exists(Path.Combine(directory, source)).ShouldBeTrue();
                request.GetProperty("parameters").GetProperty("specVersion").GetString().ShouldBe("1.0");
                request.GetProperty("timeoutSeconds").GetInt32().ShouldBe(5);
                var output = CannedScanWorker.Output(directory, "pointCloudTiles", "tiles.zip", TilesZip);
                var tiled = inputs.TryGetProperty("pointCloudLazDisplay", out _) ? voxelPoints.Last().Points : fullPoints;
                CannedScanWorker.Finish(
                    directory, "ok", [output],
                    new { pointCount = tiled, originShift = new[] { 0.5, 0.5, 0.5 }, tileCount = 4, tileBytes = 8, archiveBytes = TilesZip.LongLength, intensity = true, rgb = false },
                    tool: "py3dtiles");
                break;
            }

            default:
                CannedScanWorker.Finish(directory, "refused", [], reason: $"step '{step}' is not one this worker runs");
                break;
        }

        return Task.CompletedTask;
    };

    private sealed record Made(string Step, Guid Id, Guid FileId);

    private async Task<List<Made>> ArtefactIdsAsync(Guid uploadId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ScanArtifacts.AsNoTracking().Where(a => a.SurveyUploadId == uploadId).OrderBy(a => a.StepId)
            .Select(a => new Made(a.StepId, a.Id, a.FileId)).ToListAsync();
    }

    private static object File_(string name, long size) => new { name, size, role = "archive" };

    private static async Task<Guid> SendAsync(HttpClient client, string title, Guid caveId, (string Name, byte[] Bytes) file, object? display = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/survey-uploads", new
        {
            source = "pointcloud",
            cave = new { id = caveId },
            client = new { name = "silexgis tests", version = "1" },
            surveyTitle = title,
            files = new[] { File_(file.Name, file.Bytes.LongLength) },
            display,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var content = new ByteArrayContent(file.Bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var put = await client.PutAsync($"/api/v1/survey-uploads/{id}/files/{Uri.EscapeDataString(file.Name)}", content);
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        var complete = await client.PostAsync($"/api/v1/survey-uploads/{id}/complete", null);
        complete.StatusCode.ShouldBe(HttpStatusCode.Accepted, await complete.Content.ReadAsStringAsync());
        return id;
    }

    /// <summary>Runs the processing job the upload's completion or rerun queued, exactly once.</summary>
    private async Task RunPipelineAsync(Guid uploadId)
    {
        long jobId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var queued = await db.ProcessingJobs.AsNoTracking()
                .Where(j => j.Kind == ProcessingJobKinds.PointCloudPipeline && j.Status == ProcessingJobStatus.Queued)
                .ToListAsync();
            jobId = queued.Single(j =>
                JsonSerializer.Deserialize<SurveyUploadPipelinePayload>(j.Payload, JsonSerializerOptions.Web)!.SurveyUploadId == uploadId).Id;
        }

        await QueuedJob.RunAsync(factory.Services, jobId);
    }

    private async Task<Guid> CreateCaveAsync(bool locationProtected = false)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Scan Cave {Guid.NewGuid():N}"[..28],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>The reader's entries on the cave, replacing whatever they had.</summary>
    private async Task GrantAsync(Guid featureId, string actions)
    {
        var grant = await owner.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/access", new
        {
            entries = new[]
            {
                new { subjectKind = "user", subjectId = readerId, effect = "allow", actions, scopeKind = "object" },
            },
        });
        grant.StatusCode.ShouldBe(HttpStatusCode.OK, await grant.Content.ReadAsStringAsync());
    }
}
