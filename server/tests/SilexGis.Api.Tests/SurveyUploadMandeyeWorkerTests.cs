// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
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
/// A raw recording's run once this installation has an HDMapping worker: odometry, export and the
/// export's check through the hand-off directory, then a point cloud's steps on the export; the
/// products stored as artefacts; the run's ends when the worker is absent or was stopped mid-step;
/// a rerun from the export; and the scanner's track withheld without the exact location.
/// </summary>
/// <remarks>
/// The worker is a canned one answering with bytes the test writes. Every recording is invented:
/// LAS headers with a handful of zero points, three-line IMU logs, invented serial numbers and an
/// invented GNSS sentence. Nothing here comes from a real scanner.
/// </remarks>
public sealed class SurveyUploadMandeyeWorkerTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string handoffRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];
    private CannedScanWorker worker = null!;

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private Guid readerId;
    private long caveTypeId;

    public SurveyUploadMandeyeWorkerTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-sumdw-{Guid.NewGuid():N}");
        handoffRoot = Path.Combine(filesRoot, "handoff");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            ["Scans:ProcessingEnabled"] = "true",
            ["Scans:HandoffRoot"] = handoffRoot,
            ["Scans:PickupSeconds"] = "1",
            ["Scans:JobTimeoutSeconds"] = "5",
        }, JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"sumdw-own-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"sumdw-read-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"sumdw-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"sumdw-read-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();

        var handoff = factory.Services.GetRequiredService<ScanWorkerHandoff>();
        handoff.PollInterval = TimeSpan.FromMilliseconds(50);
        handoff.WithdrawalWait = TimeSpan.FromSeconds(1);

        // Both workers announced, and the HDMapping one's dumped defaults beside them.
        CannedScanWorker.Announce(
            handoffRoot,
            HdmappingOdometryStep.StepId, HdmappingExportStep.StepId, PointCloudCheckStep.StepId,
            PointCloudEstimateStep.StepId, PointCloudDownsampleStep.StepId, Py3dtilesTileStep.StepId);
        Directory.CreateDirectory(Path.Combine(handoffRoot, "defaults"));
        File.WriteAllText(Path.Combine(handoffRoot, "defaults", "hdmapping.odometry.json"), JsonSerializer.Serialize(new
        {
            step = "hdmapping.odometry",
            values = new
            {
                version_info = new { software_version = "0.0.0-test" },
                lidar_odometry_control = new { decimation = 0.01, use_robust = true },
            },
        }));
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
    public async Task The_recording_is_run_through_odometry_export_and_check_and_tiled_from_the_export()
    {
        worker.Answer = Hdmapping(exportPoints: 7);
        var caveId = await CreateCaveAsync(locationProtected: false);
        var recording = Recording(gnss: false);
        var id = await SendAsync(owner, "Processed scan", caveId, recording);
        await RunPipelineAsync(id);

        worker.Steps.ShouldBe([
            HdmappingOdometryStep.StepId, HdmappingExportStep.StepId, PointCloudCheckStep.StepId,
            PointCloudEstimateStep.StepId, Py3dtilesTileStep.StepId,
        ]);
        var shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        shown.GetProperty("status").GetString().ShouldBe("succeeded", shown.ToString());
        shown.GetProperty("stage").GetString().ShouldBe("store");
        shown.GetProperty("progress").GetInt32().ShouldBe(100);
        // The scan is the export now: its header's count, not the recording's.
        shown.GetProperty("pointCloud").GetProperty("pointCount").GetInt64().ShouldBe(7);
        shown.GetProperty("pointCloud").GetProperty("tileCount").GetInt32().ShouldBe(4);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var artefacts = await db.ScanArtifacts.AsNoTracking().Where(a => a.SurveyUploadId == id).ToListAsync();
        artefacts.Count(a => a.Kind == ScanArtifactKind.RawRecording && a.StepId == ScanSteps.UploadStepId).ShouldBe(recording.Count);
        var session = artefacts.Single(a => a.Kind == ScanArtifactKind.HdmSession);
        session.StepId.ShouldBe(HdmappingOdometryStep.StepId);
        session.Tool.ShouldBe("hdmapping");
        session.InputArtifactIds.Length.ShouldBe(recording.Count);
        var exported = artefacts.Single(a => a.Kind == ScanArtifactKind.PointCloudLaz);
        exported.StepId.ShouldBe(HdmappingExportStep.StepId);
        exported.InputArtifactIds.ShouldBe([session.Id]);
        var trajectory = artefacts.Single(a => a.Kind == ScanArtifactKind.Trajectory);
        trajectory.StepId.ShouldBe(HdmappingExportStep.StepId);
        artefacts.Single(a => a.StepId == PointCloudCheckStep.StepId).InputArtifactIds.ShouldBe([exported.Id, trajectory.Id]);
        artefacts.Single(a => a.StepId == PointCloudEstimateStep.StepId).InputArtifactIds.ShouldBe([exported.Id]);
        artefacts.Single(a => a.Kind == ScanArtifactKind.PointCloudTiles).InputArtifactIds.ShouldBe([exported.Id]);

        // The model is shown from the export, and still says what it was made from.
        var model = await db.SurveyModels.AsNoTracking().SingleAsync(m => m.SurveyUploadId == id);
        model.FileId.ShouldBe(exported.FileId);
        model.ConvertedFileId.ShouldBe(artefacts.Single(a => a.Kind == ScanArtifactKind.PointCloudTiles).FileId);
        var facts = PointCloudFacts.FromJson(model.PointCloud).ShouldNotBeNull();
        facts.PointCount.ShouldBe(7);
        facts.Recording.ShouldNotBeNull().ChunkCount.ShouldBe(2);

        // The odometry ran with the dumped defaults, and the recipe now records them resolved.
        var upload = await db.SurveyUploads.AsNoTracking().SingleAsync(u => u.Id == id);
        var recipe = ScanRecipes.FromOptionsJson(upload.Options).ShouldNotBeNull();
        var odometry = recipe.Steps.Single(s => s.StepId == HdmappingOdometryStep.StepId);
        odometry.Parameters.GetProperty("values").GetProperty("lidar_odometry_control").GetProperty("decimation").GetDouble().ShouldBe(0.01);
        session.ParamsHash.ShouldBe(odometry.ParamsHash);
        PointCloudRecipes.OdometryUnresolved(recipe).ShouldBeFalse();
    }

    [Fact]
    public async Task A_rerun_from_the_export_keeps_the_odometry_and_replaces_the_cloud_of_record()
    {
        worker.Answer = Hdmapping(exportPoints: 7);
        var caveId = await CreateCaveAsync(locationProtected: false);
        var id = await SendAsync(owner, "Rerun scan", caveId, Recording(gnss: false));
        await RunPipelineAsync(id);
        var first = await ArtefactsAsync(id);
        var firstExport = first.Single(a => a.Kind == ScanArtifactKind.PointCloudLaz);

        lock (worker.Steps)
        {
            worker.Steps.Clear();
        }

        worker.Answer = Hdmapping(exportPoints: 9);
        var rerun = await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", new { fromStep = HdmappingExportStep.StepId });
        rerun.StatusCode.ShouldBe(HttpStatusCode.Accepted, await rerun.Content.ReadAsStringAsync());
        await RunPipelineAsync(id);

        // The odometry is kept; the export and everything built on it are made again.
        worker.Steps.ShouldBe([
            HdmappingExportStep.StepId, PointCloudCheckStep.StepId, PointCloudEstimateStep.StepId, Py3dtilesTileStep.StepId,
        ]);
        var second = await ArtefactsAsync(id);
        second.Single(a => a.Kind == ScanArtifactKind.HdmSession).Id.ShouldBe(first.Single(a => a.Kind == ScanArtifactKind.HdmSession).Id);
        var secondExport = second.Single(a => a.Kind == ScanArtifactKind.PointCloudLaz);
        secondExport.Id.ShouldNotBe(firstExport.Id);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.AsNoTracking().SingleAsync(m => m.SurveyUploadId == id);
        model.FileId.ShouldBe(secondExport.FileId);
        PointCloudFacts.FromJson(model.PointCloud).ShouldNotBeNull().PointCount.ShouldBe(9);
        (await db.StoredFiles.AsNoTracking().AnyAsync(f => f.Id == firstExport.FileId)).ShouldBeFalse();

        var shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        shown.GetProperty("status").GetString().ShouldBe("succeeded", shown.ToString());
        shown.GetProperty("pointCloud").GetProperty("pointCount").GetInt64().ShouldBe(9);
    }

    [Fact]
    public async Task A_rerun_from_the_tiling_keeps_the_export_as_the_scans_file_and_its_facts()
    {
        worker.Answer = Hdmapping(exportPoints: 7);
        var caveId = await CreateCaveAsync(locationProtected: false);
        var id = await SendAsync(owner, "Tile rerun scan", caveId, Recording(gnss: false));
        await RunPipelineAsync(id);
        var export = (await ArtefactsAsync(id)).Single(a => a.Kind == ScanArtifactKind.PointCloudLaz);

        var rerun = await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", new { fromStep = Py3dtilesTileStep.StepId });
        rerun.StatusCode.ShouldBe(HttpStatusCode.Accepted, await rerun.Content.ReadAsStringAsync());
        await RunPipelineAsync(id);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.AsNoTracking().SingleAsync(m => m.SurveyUploadId == id);
        model.FileId.ShouldBe(export.FileId);
        var facts = PointCloudFacts.FromJson(model.PointCloud).ShouldNotBeNull();
        facts.PointCount.ShouldBe(7);
        facts.Recording.ShouldNotBeNull();
    }

    [Fact]
    public async Task With_no_worker_announced_the_recording_is_inspected_and_the_run_fails_saying_so()
    {
        File.Delete(Path.Combine(handoffRoot, "capabilities.json"));
        var caveId = await CreateCaveAsync(locationProtected: false);
        var id = await SendAsync(owner, "Unannounced scan", caveId, Recording(gnss: false));
        await Should.ThrowAsync<InvalidOperationException>(RunPipelineAsync(id));

        var shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        shown.GetProperty("status").GetString().ShouldBe("failed");
        shown.GetProperty("error").GetProperty("code").GetString().ShouldBe(SurveyPipelineFailures.WorkerUnavailable);
        // The recording was inspected all the same; the scan is there, described, unprocessed.
        shown.GetProperty("pointCloud").GetProperty("pointCount").GetInt64().ShouldBe(5);
        worker.Taken.ShouldBe(0);
    }

    [Fact]
    public async Task A_worker_stopped_during_the_odometry_fails_the_run_as_interrupted_and_keeps_nothing_of_it()
    {
        worker.Answer = (directory, _) =>
        {
            CannedScanWorker.Finish(
                directory, "interrupted", [], new { stagesDone = new[] { "preflight", "parameters", "links" }, interruptedIn = "odometry" },
                reason: "the worker stopped during the odometry stage; its outputs are unfinished", tool: "hdmapping");
            return Task.CompletedTask;
        };
        var caveId = await CreateCaveAsync(locationProtected: false);
        var id = await SendAsync(owner, "Interrupted scan", caveId, Recording(gnss: false));
        await Should.ThrowAsync<InvalidOperationException>(RunPipelineAsync(id));

        var shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        shown.GetProperty("status").GetString().ShouldBe("failed");
        shown.GetProperty("stage").GetString().ShouldBe("odometry");
        shown.GetProperty("error").GetProperty("code").GetString().ShouldBe(SurveyPipelineFailures.WorkerInterrupted);
        shown.GetProperty("error").GetProperty("message").GetString()!.ShouldContain("odometry stage");
        worker.Steps.ShouldBe([HdmappingOdometryStep.StepId]);
        (await ArtefactsAsync(id)).ShouldNotContain(a => a.Kind == ScanArtifactKind.HdmSession);
        Directory.GetDirectories(Path.Combine(handoffRoot, "jobs")).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_export_that_fails_its_check_fails_the_run_at_the_check_stage()
    {
        worker.Answer = Hdmapping(exportPoints: 7, checkFails: true);
        var caveId = await CreateCaveAsync(locationProtected: false);
        var id = await SendAsync(owner, "Unchecked scan", caveId, Recording(gnss: false));
        await Should.ThrowAsync<InvalidOperationException>(RunPipelineAsync(id));

        var shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        shown.GetProperty("status").GetString().ShouldBe("failed");
        shown.GetProperty("stage").GetString().ShouldBe("check");
        shown.GetProperty("error").GetProperty("code").GetString().ShouldBe(SurveyPipelineFailures.ExportInvalid);
        worker.Steps.ShouldNotContain(Py3dtilesTileStep.StepId);
    }

    [Fact]
    public async Task Without_an_account_or_the_right_to_write_the_cave_a_recording_is_not_run_again()
    {
        worker.Answer = Hdmapping(exportPoints: 7);
        var caveId = await CreateCaveAsync(locationProtected: false);
        var id = await SendAsync(owner, "Guarded scan", caveId, Recording(gnss: false));
        await RunPipelineAsync(id);
        var body = new { fromStep = HdmappingOdometryStep.StepId };

        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", body)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The reader sees the cave and may not write it.
        (await reader.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", body)).StatusCode
            .ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);

        // The owner may, and a step nobody defined is refused.
        var unknown = await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", new { fromStep = "hdmapping.nothing" });
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await unknown.Content.ReadAsStringAsync());
        (await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", body)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    // An invented walk: eleven seconds, five metres a second, along the frame's +Y axis. The
    // receiver put the same walk due east, so the frame's +Y must come out facing east.
    private const double WalkLatitude = 45.5;
    private const double WalkLongitude = 25.0;
    private const double MetresPerDegreeLongitude = 111320.0 * 0.7009; // cos(45.5 degrees)

    private static string WalkTrajectory() => string.Concat(Enumerable.Range(0, 11).Select(k =>
        string.Create(CultureInfo.InvariantCulture, $"{(1 + k) * 1_000_000_000L},{(1 + k) * 1000},0,{5 * k},0,0,0,0,1\n")));

    // The scanner's own whitespace layout: lidar clock, latitude, longitude, altitude, HDOP,
    // satellites, geoid separation, correction age, UTC time, quality, computer clock.
    private static string WalkFixes() => string.Concat(Enumerable.Range(0, 11).Select(k =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{(1 + k) * 1_000_000_000L} {WalkLatitude} {WalkLongitude + (5.0 * k / MetresPerDegreeLongitude):F8} 1000.0 1.0 9 0.0 0.0 120000.00 1 {(1 + k) * 1000}\n")));

    [Fact]
    public async Task A_recording_with_good_satellite_fixes_is_placed_from_them_when_tiled_and_a_placement_by_hand_is_kept()
    {
        worker.Answer = Hdmapping(exportPoints: 7, trajectoryText: WalkTrajectory());
        var caveId = await CreateCaveAsync(locationProtected: false);
        var id = await SendAsync(owner, "Fixed scan", caveId, Recording(gnss: true, gnssText: WalkFixes()));
        await RunPipelineAsync(id);

        async Task<SurveyModel> ModelAsync()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            return await db.SurveyModels.AsNoTracking().SingleAsync(m => m.SurveyUploadId == id);
        }

        var model = await ModelAsync();
        model.PlacementSource.ShouldBe(ScanPlacementSource.Gnss);
        // The frame's +Y runs the way the receiver walked: east.
        model.PlacementHeadingDeg.ShouldNotBeNull().ShouldBe(90.0, 1.0);
        // The placement puts the last pose of the walk, in tile coordinates (the pose less the
        // 0.5 m the tiles are shifted by), on the last good fix.
        var placement = new ScanPlacement(
            model.Anchor.ShouldNotBeNull().X, model.Anchor.Y, model.AnchorHeightM.ShouldNotBeNull(),
            model.PlacementHeadingDeg.ShouldNotBeNull(), ScanPlacementSource.Gnss);
        var (lon, lat, _) = placement.PositionOf(-0.5, 49.5, -0.5);
        (Math.Abs(lon - (WalkLongitude + (50.0 / MetresPerDegreeLongitude))) * MetresPerDegreeLongitude).ShouldBeLessThan(1.0);
        (Math.Abs(lat - WalkLatitude) * 111320.0).ShouldBeLessThan(1.0);

        // A hand placement made afterwards is not taken back by the fixes when the tiles are made again.
        var byHand = await owner.PutAsJsonAsync($"/api/v1/survey-models/{model.Id}/placement",
            new { longitude = 25.5, latitude = 45.9, heightM = 700, headingDeg = 10 });
        byHand.StatusCode.ShouldBe(HttpStatusCode.OK, await byHand.Content.ReadAsStringAsync());
        var rerun = await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", new { fromStep = Py3dtilesTileStep.StepId });
        rerun.StatusCode.ShouldBe(HttpStatusCode.Accepted, await rerun.Content.ReadAsStringAsync());
        await RunPipelineAsync(id);

        var kept = await ModelAsync();
        kept.PlacementSource.ShouldBe(ScanPlacementSource.Manual);
        kept.PlacementHeadingDeg.ShouldBe(10.0);
        kept.Anchor!.X.ShouldBe(25.5, 1e-9);
    }

    [Fact]
    public async Task A_recording_whose_fixes_are_all_in_one_spot_is_left_unplaced()
    {
        worker.Answer = Hdmapping(exportPoints: 7, trajectoryText: WalkTrajectory());
        var caveId = await CreateCaveAsync(locationProtected: false);
        var standing = string.Concat(Enumerable.Range(0, 11).Select(k =>
            string.Create(CultureInfo.InvariantCulture, $"{(1 + k) * 1_000_000_000L} 45.5 25.0 1000.0 1.0 9 0.0 0.0 120000.00 1 {(1 + k) * 1000}\n")));
        var id = await SendAsync(owner, "Standing scan", caveId, Recording(gnss: true, gnssText: standing));
        await RunPipelineAsync(id);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.AsNoTracking().SingleAsync(m => m.SurveyUploadId == id);
        model.PlacementSource.ShouldBeNull();
    }

    [Fact]
    public async Task The_track_and_a_gnss_recordings_export_are_served_only_with_the_exact_location()
    {
        // A protected cave the reader may read, write and place exactly; they send a recording
        // whose GNSS log heard a fix.
        worker.Answer = Hdmapping(exportPoints: 7);
        var caveId = await CreateCaveAsync(locationProtected: true);
        await GrantAsync(caveId, "read, write, viewExactLocation");
        var id = await SendAsync(reader, "Entrance scan", caveId, Recording(gnss: true));
        await RunPipelineAsync(id);

        var artefacts = await ArtefactsAsync(id);
        var exportFile = artefacts.Single(a => a.Kind == ScanArtifactKind.PointCloudLaz).FileId;
        var trajectoryFile = artefacts.Single(a => a.Kind == ScanArtifactKind.Trajectory).FileId;
        var sessionFile = artefacts.Single(a => a.Kind == ScanArtifactKind.HdmSession).FileId;
        var tilesFile = artefacts.Single(a => a.Kind == ScanArtifactKind.PointCloudTiles).FileId;
        var rawFile = artefacts.First(a => a.Kind == ScanArtifactKind.RawRecording).FileId;

        // With the exact location: everything served, and the extent's place shown.
        (await reader.GetAsync($"/api/v1/files/{exportFile}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/files/{trajectoryFile}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/files/{sessionFile}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/files/{rawFile}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var granted = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        granted.GetProperty("pointCloud").GetProperty("georeferenced").GetBoolean().ShouldBeTrue();
        granted.GetProperty("pointCloud").GetProperty("min").GetArrayLength().ShouldBe(3);

        // Without it: the track, the session, the georeferenced export and the recording are
        // withheld whoever learns their ids; the tiles, in their own frame, stay readable.
        await GrantAsync(caveId, "read, write");
        (await reader.GetAsync($"/api/v1/files/{exportFile}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/files/{trajectoryFile}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/files/{sessionFile}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/files/{rawFile}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/files/{tilesFile}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var withheld = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        withheld.GetProperty("pointCloud").GetProperty("min").ValueKind.ShouldBe(JsonValueKind.Null);
        withheld.GetProperty("pointCloud").GetProperty("originShift").ValueKind.ShouldBe(JsonValueKind.Null);
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

    /// <summary>
    /// A canned HDMapping worker and tiler together: the odometry checks it was handed the whole
    /// recording and the resolved defaults, the export writes a cloud of <paramref name="exportPoints"/>
    /// and a trajectory, the check passes (or fails), and the estimate and tiling answer as the tiler does.
    /// </summary>
    private static Func<string, JsonElement, Task> Hdmapping(int exportPoints, bool checkFails = false, string? trajectoryText = null) => (directory, request) =>
    {
        var step = request.GetProperty("step").GetString();
        var inputs = request.GetProperty("inputs");
        switch (step)
        {
            case HdmappingOdometryStep.StepId:
            {
                var manifest = inputs.GetProperty("rawRecording").GetString()!;
                using var listed = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, manifest)));
                var names = listed.RootElement.GetProperty("files").EnumerateArray().Select(n => n.GetString()!).ToList();
                names.ShouldContain("lidar0000.laz");
                names.ShouldContain("imu0001.csv");
                names.ShouldAllBe(n => File.Exists(Path.Combine(directory, n)));
                request.GetProperty("parameters").GetProperty("values").GetProperty("lidar_odometry_control")
                    .GetProperty("use_robust").GetBoolean().ShouldBeTrue();
                var output = CannedScanWorker.Output(directory, "hdmSession", "session.zip", MakeZip());
                CannedScanWorker.Finish(
                    directory, "ok", [output], new { odometry = new { scans = 2 } }, tool: "hdmapping", toolVersion: "0.0.0-test@abc");
                break;
            }

            case HdmappingExportStep.StepId:
            {
                File.Exists(Path.Combine(directory, inputs.GetProperty("hdmSession").GetString()!)).ShouldBeTrue();
                var cloud = CannedScanWorker.Output(directory, "pointCloudLaz", "export.laz", SyntheticLas.Las(exportPoints, compressed: true));
                var track = CannedScanWorker.Output(
                    directory, "trajectory", "trajectory.csv", Encoding.ASCII.GetBytes(trajectoryText ?? "1 0 0 0 0 0 0 0 1\n2 0 1 0 0 0 0 0 1\n"));
                CannedScanWorker.Finish(directory, "ok", [cloud, track], new { export = new { pointsWritten = exportPoints } }, tool: "hdmapping");
                break;
            }

            case PointCloudCheckStep.StepId:
            {
                File.Exists(Path.Combine(directory, inputs.GetProperty("pointCloudLaz").GetString()!)).ShouldBeTrue();
                File.Exists(Path.Combine(directory, inputs.GetProperty("trajectory").GetString()!)).ShouldBeTrue();
                var report = CannedScanWorker.Output(directory, "report", "check.json", "{}"u8.ToArray());
                if (checkFails)
                {
                    CannedScanWorker.Finish(directory, "failed", [report], reason: "the cloud failed its check: the cloud holds no points");
                }
                else
                {
                    CannedScanWorker.Finish(directory, "ok", [report], new { pointCount = exportPoints, problems = Array.Empty<string>() });
                }

                break;
            }

            case PointCloudEstimateStep.StepId:
            {
                var data = new
                {
                    pointCount = exportPoints,
                    hasRgb = false,
                    hasIntensity = true,
                    full = new { points = exportPoints, estimatedTileBytes = exportPoints * 17, estimatedSeconds = 17 },
                    voxels = Array.Empty<object>(),
                };
                var output = CannedScanWorker.Output(directory, "estimate", "estimate.json", JsonSerializer.SerializeToUtf8Bytes(data));
                CannedScanWorker.Finish(directory, "ok", [output], data);
                break;
            }

            case Py3dtilesTileStep.StepId:
            {
                File.Exists(Path.Combine(directory, inputs.GetProperty("pointCloudLaz").GetString()!)).ShouldBeTrue();
                var output = CannedScanWorker.Output(directory, "pointCloudTiles", "tiles.zip", TilesZip);
                CannedScanWorker.Finish(
                    directory, "ok", [output],
                    new { pointCount = exportPoints, originShift = new[] { 0.5, 0.5, 0.5 }, tileCount = 4, archiveBytes = TilesZip.LongLength, intensity = true },
                    tool: "py3dtiles");
                break;
            }

            default:
                CannedScanWorker.Finish(directory, "refused", [], reason: $"step '{step}' is not one this worker runs");
                break;
        }

        return Task.CompletedTask;
    };

    /// <summary>The invented two-chunk recording: 2 and 3 points, IMU logs over 2.5 seconds, two invented sensors, and an invented GNSS sentence when asked for.</summary>
    private static List<(string Name, byte[] Bytes)> Recording(bool gnss, string? gnssText = null)
    {
        const string header = "timestamp gyroX gyroY gyroZ accX accY accZ imuId timestampUnix\n";
        List<(string Name, byte[] Bytes)> files =
        [
            ("lidar0000.laz", SyntheticLas.Las(2, compressed: true)),
            ("imu0000.csv", Encoding.ASCII.GetBytes(header + "1000000000 0.1 0.2 0.3 0 0 1 0 0\n1500000000 0.1 0.2 0.3 0 0 1 0 0\n")),
            ("lidar0000.sn", Encoding.ASCII.GetBytes("0 TESTSENSOR0001\n1 TESTSENSOR0002\n")),
            ("lidar0001.laz", SyntheticLas.Las(3, compressed: true)),
            ("imu0001.csv", Encoding.ASCII.GetBytes(header + "2000000000 0.1 0.2 0.3 0 0 1 0 0\n3500000000 0.1 0.2 0.3 0 0 1 0 0\n")),
            ("lidar0001.sn", Encoding.ASCII.GetBytes("0 TESTSENSOR0001\n1 TESTSENSOR0002\n")),
        ];
        if (gnss)
        {
            files.Add(("gnss0000.gnss", Encoding.ASCII.GetBytes(gnssText ?? "1000000000 $GPGGA,000000.00,0000.0000,N,00000.0000,E,1,08,1.0,0.0,M,0.0,M,,*00\n")));
        }

        return files;
    }

    private async Task<List<ScanArtifact>> ArtefactsAsync(Guid uploadId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ScanArtifacts.AsNoTracking().Where(a => a.SurveyUploadId == uploadId).ToListAsync();
    }

    private static object File_(string name, long size) => new { name, size, role = "archive" };

    private static async Task<Guid> SendAsync(HttpClient client, string title, Guid caveId, IReadOnlyList<(string Name, byte[] Bytes)> recording)
    {
        var response = await client.PostAsJsonAsync("/api/v1/survey-uploads", new
        {
            source = "mandeye",
            cave = new { id = caveId },
            client = new { name = "silexgis tests", version = "1" },
            surveyTitle = title,
            files = recording.Select(f => File_(f.Name, f.Bytes.LongLength)).ToArray(),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        foreach (var (name, bytes) in recording)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            var put = await client.PutAsync($"/api/v1/survey-uploads/{id}/files/{Uri.EscapeDataString(name)}", content);
            put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        }

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

    private async Task<Guid> CreateCaveAsync(bool locationProtected)
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
