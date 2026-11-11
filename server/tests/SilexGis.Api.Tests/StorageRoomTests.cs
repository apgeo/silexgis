// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
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
/// Room in storage, asked before anything is written: the totals and caps a screen shows before an
/// upload or a run starts, a point-cloud upload refused when its declared files do not fit under
/// the scans' cap — counting the room uploads still arriving have reserved — and a processing step
/// refused before the worker is asked, when what it is estimated to write does not fit.
/// </summary>
/// <remarks>
/// The scans' cap is set to a few mebibytes so a test can pass it with declared sizes, and every
/// expected figure is worked out from the figures the route answers at the start of the test, so
/// tests of this class can run in any order over the one database they share. Every cloud here is
/// written by the test.
/// </remarks>
public sealed class StorageRoomTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const long ScanCap = 8L * 1024 * 1024;

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string handoffRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];
    private CannedScanWorker worker = null!;

    private HttpClient owner = null!;
    private long caveTypeId;

    public StorageRoomTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-store-{Guid.NewGuid():N}");
        handoffRoot = Path.Combine(filesRoot, "handoff");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            ["Scans:MaxUploadBytes"] = (4 * ScanCap).ToString(CultureInfo.InvariantCulture),
            ["Scans:MaxTotalBytes"] = ScanCap.ToString(CultureInfo.InvariantCulture),
            ["Scans:ProcessingEnabled"] = "true",
            ["Scans:HandoffRoot"] = handoffRoot,
            ["Scans:PickupSeconds"] = "1",
            ["Scans:JobTimeoutSeconds"] = "5",
        }, JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"store-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"store-own-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();

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
    public async Task The_room_route_needs_an_account_and_a_sane_size()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/storage/room")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        await ShouldBeProblemAsync(await owner.GetAsync("/api/v1/storage/room?addingBytes=-1"), HttpStatusCode.BadRequest, "storage.adding_invalid");
    }

    [Fact]
    public async Task The_room_route_shows_the_totals_and_caps_and_what_an_addition_would_leave()
    {
        var now = await RoomAsync();
        now.GetProperty("scanCapBytes").GetInt64().ShouldBe(ScanCap);
        now.GetProperty("storeCapBytes").ValueKind.ShouldBe(JsonValueKind.Null);
        now.GetProperty("accountCapBytes").ValueKind.ShouldBe(JsonValueKind.Null);
        now.GetProperty("storeUsedBytes").GetInt64().ShouldBeGreaterThanOrEqualTo(now.GetProperty("scanUsedBytes").GetInt64());
        now.GetProperty("addingBytes").GetInt64().ShouldBe(0);
        now.GetProperty("fits").GetBoolean().ShouldBeTrue();
        now.GetProperty("level").GetString().ShouldNotBeNull();

        var room = ScanCap - now.GetProperty("scanUsedBytes").GetInt64();

        // Exactly the room left fits; a byte more, as a point cloud, is short by that byte — and
        // the same bytes as anything else are not measured against the scans' cap at all.
        (await RoomAsync($"addingBytes={room}&scan=true")).GetProperty("fits").GetBoolean().ShouldBeTrue();
        var over = await RoomAsync($"addingBytes={room + 1}&scan=true");
        over.GetProperty("fits").GetBoolean().ShouldBeFalse();
        var shortfall = over.GetProperty("shortfall");
        shortfall.GetProperty("limit").GetString().ShouldBe("scans");
        shortfall.GetProperty("neededBytes").GetInt64().ShouldBe(room + 1);
        shortfall.GetProperty("roomBytes").GetInt64().ShouldBe(room);
        shortfall.GetProperty("missingBytes").GetInt64().ShouldBe(1);
        (await RoomAsync($"addingBytes={room + 1}")).GetProperty("fits").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task A_point_cloud_that_does_not_fit_under_the_scans_cap_is_refused_before_a_byte_is_sent_saying_how_much_is_missing()
    {
        var caveId = await CreateCaveAsync();
        var room = ScanCap - (await RoomAsync()).GetProperty("scanUsedBytes").GetInt64();

        var refused = await ShouldBeProblemAsync(
            await OpenAsync(caveId, "big.laz", room + 1234), HttpStatusCode.Conflict, "storage.insufficient");
        refused.GetProperty("limit").GetString().ShouldBe("scans");
        refused.GetProperty("missingBytes").GetInt64().ShouldBe(1234);
        refused.GetProperty("neededBytes").GetInt64().ShouldBe(room + 1234);

        // Nothing of the refused upload was kept: the cave lists no upload, and the room is unchanged.
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads?caveId={caveId}")).GetProperty("total").GetInt32().ShouldBe(0);
        (ScanCap - (await RoomAsync()).GetProperty("scanUsedBytes").GetInt64()).ShouldBe(room);
    }

    [Fact]
    public async Task An_upload_still_arriving_reserves_its_room_and_its_own_files_are_not_refused_for_it()
    {
        var caveId = await CreateCaveAsync();
        var cloud = SyntheticLas.Las(points: 3, compressed: true);
        var usedBefore = (await RoomAsync()).GetProperty("scanUsedBytes").GetInt64();
        var room = ScanCap - usedBefore;

        // The first upload declares the real cloud; the second takes all the rest but 50 bytes.
        var first = await IdOfAsync(await OpenAsync(caveId, "first.laz", cloud.LongLength));
        var second = await IdOfAsync(await OpenAsync(caveId, "second.laz", room - cloud.LongLength - 50));
        (await RoomAsync()).GetProperty("scanUsedBytes").GetInt64().ShouldBe(usedBefore + room - 50);

        // A third that wants a hundred bytes is short by the fifty nobody has reserved yet.
        var refused = await ShouldBeProblemAsync(await OpenAsync(caveId, "third.laz", 100), HttpStatusCode.Conflict, "storage.insufficient");
        refused.GetProperty("missingBytes").GetInt64().ShouldBe(50);

        // The first upload's own file arrives into the room it reserved; once stored it counts as
        // stored rather than reserved, so the total does not move.
        var put = await PutAsync(first, "first.laz", cloud);
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        (await RoomAsync()).GetProperty("scanUsedBytes").GetInt64().ShouldBe(usedBefore + room - 50);

        // An abandoned upload reserves nothing any more.
        (await owner.DeleteAsync($"/api/v1/survey-uploads/{second}")).IsSuccessStatusCode.ShouldBeTrue();
        (await owner.DeleteAsync($"/api/v1/survey-uploads/{first}")).IsSuccessStatusCode.ShouldBeTrue();
        (await RoomAsync()).GetProperty("scanUsedBytes").GetInt64().ShouldBe(usedBefore + cloud.LongLength);
    }

    [Fact]
    public async Task A_tiling_whose_estimate_does_not_fit_is_refused_before_the_worker_is_asked()
    {
        // The estimate says the full cloud's tiles take twice the scans' whole cap.
        worker.Answer = (directory, request) =>
        {
            request.GetProperty("step").GetString().ShouldBe(PointCloudEstimateStep.StepId);
            var data = new
            {
                pointCount = 3,
                hasRgb = false,
                hasIntensity = false,
                full = new { points = 3, estimatedTileBytes = 2 * ScanCap, estimatedSeconds = 1 },
                voxels = Array.Empty<object>(),
            };
            var output = CannedScanWorker.Output(directory, "estimate", "estimate.json", JsonSerializer.SerializeToUtf8Bytes(data));
            CannedScanWorker.Finish(directory, "ok", [output], data);
            return Task.CompletedTask;
        };
        var caveId = await CreateCaveAsync();
        var id = await SendAsync(caveId, "scan.laz", SyntheticLas.Las(points: 3, compressed: true));
        await Should.ThrowAsync<InvalidOperationException>(RunPipelineAsync(id));

        worker.Steps.ShouldBe([PointCloudEstimateStep.StepId]);
        var shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        shown.GetProperty("status").GetString().ShouldBe("failed");
        shown.GetProperty("stage").GetString().ShouldBe("tile");
        var error = shown.GetProperty("error");
        error.GetProperty("code").GetString().ShouldBe(SurveyPipelineFailures.StorageInsufficient);
        error.GetProperty("message").GetString().ShouldNotBeNull().ShouldContain("missing");

        // No tileset was stored, and no job directory was left behind for the refused step.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.ScanArtifacts.AsNoTracking().CountAsync(a => a.SurveyUploadId == id && a.Kind == ScanArtifactKind.PointCloudTiles)).ShouldBe(0);
        Directory.Exists(Path.Combine(handoffRoot, "jobs", $"{id:N}-py3dtiles-tile")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_run_whose_thinning_fits_but_whose_tiling_then_would_not_is_refused_before_the_thinning()
    {
        // The thinning writes no more than the small cloud it reads, which fits easily; the tiles
        // of the voxel chosen would take twice the scans' whole cap.
        worker.Answer = (directory, request) =>
        {
            request.GetProperty("step").GetString().ShouldBe(PointCloudEstimateStep.StepId);
            var data = new
            {
                pointCount = 3,
                hasRgb = false,
                hasIntensity = false,
                full = new { points = 3, estimatedTileBytes = 3 * ScanCap, estimatedSeconds = 1 },
                voxels = new[] { new { voxelCm = 5.0, points = 2, estimatedTileBytes = 2 * ScanCap, estimatedSeconds = 1 } },
            };
            var output = CannedScanWorker.Output(directory, "estimate", "estimate.json", JsonSerializer.SerializeToUtf8Bytes(data));
            CannedScanWorker.Finish(directory, "ok", [output], data);
            return Task.CompletedTask;
        };
        var caveId = await CreateCaveAsync();
        var id = await SendAsync(caveId, "thinned.laz", SyntheticLas.Las(points: 3, compressed: true), display: new { method = "voxel", voxelCm = 5 });
        await Should.ThrowAsync<InvalidOperationException>(RunPipelineAsync(id));

        // The worker was asked for the estimate only: the thinning never started.
        worker.Steps.ShouldBe([PointCloudEstimateStep.StepId]);
        var shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        shown.GetProperty("status").GetString().ShouldBe("failed");
        shown.GetProperty("stage").GetString().ShouldBe("downsample");
        var error = shown.GetProperty("error");
        error.GetProperty("code").GetString().ShouldBe(SurveyPipelineFailures.StorageInsufficient);
        error.GetProperty("message").GetString().ShouldNotBeNull().ShouldContain("this run needs about");

        // Nothing of the run was stored: neither a thinned copy nor a tileset.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.ScanArtifacts.AsNoTracking().CountAsync(a => a.SurveyUploadId == id
            && (a.Kind == ScanArtifactKind.PointCloudLazDisplay || a.Kind == ScanArtifactKind.PointCloudTiles))).ShouldBe(0);
    }

    private async Task<JsonElement> RoomAsync(string query = "")
    {
        var response = await owner.GetAsync("/api/v1/storage/room" + (query.Length == 0 ? "" : "?" + query));
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement;
    }

    private Task<HttpResponseMessage> OpenAsync(Guid caveId, string name, long size, object? display = null) =>
        owner.PostAsJsonAsync("/api/v1/survey-uploads", new
        {
            source = "pointcloud",
            cave = new { id = caveId },
            client = new { name = "silexgis tests", version = "1" },
            surveyTitle = "Scan " + name,
            files = new[] { new { name, size, role = "archive" } },
            display,
        });

    private static async Task<Guid> IdOfAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, text);
        return JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> PutAsync(Guid id, string name, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return owner.PutAsync($"/api/v1/survey-uploads/{id}/files/{Uri.EscapeDataString(name)}", content);
    }

    private async Task<Guid> SendAsync(Guid caveId, string name, byte[] cloud, object? display = null)
    {
        var id = await IdOfAsync(await OpenAsync(caveId, name, cloud.LongLength, display));
        var put = await PutAsync(id, name, cloud);
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        var complete = await owner.PostAsync($"/api/v1/survey-uploads/{id}/complete", null);
        complete.StatusCode.ShouldBe(HttpStatusCode.Accepted, await complete.Content.ReadAsStringAsync());
        return id;
    }

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

    private static async Task<JsonElement> ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, text);
        var body = JsonDocument.Parse(text).RootElement;
        body.GetProperty("code").GetString().ShouldBe(code, text);
        return body;
    }

    private async Task<Guid> CreateCaveAsync()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Store Cave {Guid.NewGuid():N}"[..28],
            caveTypeId,
            visibility = "authenticated",
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
