// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
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
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Scans;

namespace SilexGis.Api.Tests;

/// <summary>
/// "Make a surface": a processed, placed scan asked for a surface, the worker's STL saved as a wall
/// mesh of the chosen cave standing where the scan stands, and the requests refused for a caller
/// who may not write or place a cave, for a scan with nowhere to stand, and for a profile of
/// another step.
/// </summary>
/// <remarks>
/// Every cloud is a synthetic header and every surface one triangle the test wrote; nothing here is
/// a real scan.
/// </remarks>
public sealed class SurveyUploadSurfaceTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private static readonly double[] Shift = [0.5, 0.5, 0.5];

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string handoffRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];
    private CannedScanWorker worker = null!;

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private Guid readerId;
    private long caveTypeId;

    public SurveyUploadSurfaceTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-surf-{Guid.NewGuid():N}");
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
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"surf-own-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"surf-read-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"surf-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"surf-read-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();

        var handoff = factory.Services.GetRequiredService<ScanWorkerHandoff>();
        handoff.PollInterval = TimeSpan.FromMilliseconds(50);
        handoff.WithdrawalWait = TimeSpan.FromSeconds(1);

        CannedScanWorker.Announce(handoffRoot, PointCloudEstimateStep.StepId, Py3dtilesTileStep.StepId, MeshSurfaceStep.StepId);
        worker = new CannedScanWorker(handoffRoot) { Answer = Worker };
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
    public async Task A_surface_is_saved_as_the_scans_cave_wall_mesh_turned_and_set_down_by_the_scans_placement()
    {
        var caveId = await CreateCaveAsync();
        var (uploadId, scanId) = await TiledScanAsync(caveId);
        await PlaceAsync(owner, scanId, headingDeg: 90);

        var asked = await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{uploadId}/surface", new { });
        asked.StatusCode.ShouldBe(HttpStatusCode.Accepted, await asked.Content.ReadAsStringAsync());
        await RunPipelineAsync(uploadId);

        // Only the surface was made: the estimate and the tiles were the earlier run's.
        worker.Steps.ShouldBe([PointCloudEstimateStep.StepId, Py3dtilesTileStep.StepId, MeshSurfaceStep.StepId]);
        var parameters = worker.Requests.Single(r => r.GetProperty("step").GetString() == MeshSurfaceStep.StepId).GetProperty("parameters");
        parameters.GetProperty("voxelCm").GetDouble().ShouldBe(2);
        parameters.GetProperty("targetTriangles").GetInt32().ShouldBe(500_000);
        // The surface is made in the tiles' own frame.
        parameters.GetProperty("originShift").EnumerateArray().Select(v => v.GetDouble()).ToArray().ShouldBe(Shift);

        var shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{uploadId}");
        shown.GetProperty("status").GetString().ShouldBe("succeeded", shown.ToString());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var mesh = await db.SurveyModels.AsNoTracking().SingleAsync(m => m.CaveFeatureId == caveId && m.Format == SurveyModelFormat.Stl);
            mesh.Anchor!.X.ShouldBe(25.5, 1e-9);
            mesh.Anchor.Y.ShouldBe(45.25, 1e-9);
            mesh.AnchorHeightM.ShouldBe(320);
            mesh.SourceEpsg.ShouldBeNull();
            mesh.SurveyUploadId.ShouldBeNull();
            mesh.IsCurrent.ShouldBeTrue();
            mesh.Status.ShouldBe(SurveyModelStatus.Pending);

            // The wall mesh holds its own copy, turned into east-north-up: the tiles' +x is south
            // when their +y points east.
            var file = await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == mesh.FileId);
            file.MimeType.ShouldBe(ScanSurfaceSaving.StlMediaType);
            var placed = await File.ReadAllBytesAsync(scope.ServiceProvider.GetRequiredService<IFileStore>().GetAbsolutePath(file.StoragePath));
            Corner(placed, 1).ShouldBe((0f, 0f, 0f));
            Corner(placed, 2).ShouldBe((0f, -1f, 0f));
            Corner(placed, 3).ShouldBe((2f, 0f, 0f));

            // The scan keeps the surface as its own artefact, and its recipe no longer asks for one.
            var artefact = await db.ScanArtifacts.AsNoTracking().SingleAsync(a => a.SurveyUploadId == uploadId && a.StepId == MeshSurfaceStep.StepId);
            artefact.Kind.ShouldBe(ScanArtifactKind.MeshStl);
            artefact.FileId.ShouldNotBe(mesh.FileId);
            var upload = await db.SurveyUploads.AsNoTracking().SingleAsync(u => u.Id == uploadId);
            var recipe = ScanRecipes.FromOptionsJson(upload.Options).ShouldNotBeNull();
            recipe.Steps.ShouldNotContain(s => s.StepId == MeshSurfaceStep.StepId);
            recipe.Surface.ShouldBeNull();

            // The scan itself is unchanged: still shown from its tiles.
            var scan = await db.SurveyModels.AsNoTracking().SingleAsync(m => m.Id == scanId);
            scan.ConvertedFileId.ShouldNotBeNull();
            scan.Format.ShouldBe(SurveyModelFormat.Laz);
        }

        // From here it is an ordinary wall mesh: the mesh path reads it and makes the drawable file.
        var meshId = await MeshIdAsync(caveId);
        await RunMeshJobAsync(meshId);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var mesh = await db.SurveyModels.AsNoTracking().SingleAsync(m => m.Id == meshId);
            mesh.Status.ShouldBe(SurveyModelStatus.Ready, mesh.ProcessingError);
            mesh.ConvertedFileId.ShouldNotBeNull();
            mesh.TriangleCount.ShouldBe(1);
            mesh.Anchor!.X.ShouldBe(25.5, 1e-6);
            mesh.AnchorHeightM.ShouldBe(320);
        }

        // A plain rerun afterwards makes no second wall mesh.
        var rerun = await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{uploadId}/rerun", new { fromStep = Py3dtilesTileStep.StepId });
        rerun.StatusCode.ShouldBe(HttpStatusCode.Accepted, await rerun.Content.ReadAsStringAsync());
        await RunPipelineAsync(uploadId);
        worker.Steps.Last().ShouldBe(Py3dtilesTileStep.StepId);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.SurveyModels.CountAsync(m => m.CaveFeatureId == caveId && m.Format == SurveyModelFormat.Stl)).ShouldBe(1);
        }
    }

    [Fact]
    public async Task A_surface_request_left_by_a_run_that_did_not_save_is_not_made_by_a_later_plain_rerun()
    {
        var caveId = await CreateCaveAsync();
        var (uploadId, scanId) = await TiledScanAsync(caveId);
        await PlaceAsync(owner, scanId, headingDeg: 0);

        var asked = await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{uploadId}/surface", new { });
        asked.StatusCode.ShouldBe(HttpStatusCode.Accepted, await asked.Content.ReadAsStringAsync());

        // The run ended without saving: failed, its stored recipe still holding the request.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.SurveyUploads.Where(u => u.Id == uploadId)
                .ExecuteUpdateAsync(x => x.SetProperty(u => u.Status, SurveyUploadStatus.Failed));
            await db.ProcessingJobs.Where(j => j.Kind == ProcessingJobKinds.PointCloudPipeline && j.Status == ProcessingJobStatus.Queued)
                .ExecuteUpdateAsync(x => x.SetProperty(j => j.Status, ProcessingJobStatus.Failed));
            var held = ScanRecipes.FromOptionsJson((await db.SurveyUploads.AsNoTracking().SingleAsync(u => u.Id == uploadId)).Options).ShouldNotBeNull();
            held.Surface.ShouldNotBeNull();
        }

        var rerun = await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{uploadId}/rerun", new { });
        rerun.StatusCode.ShouldBe(HttpStatusCode.Accepted, await rerun.Content.ReadAsStringAsync());
        await RunPipelineAsync(uploadId);

        worker.Steps.ShouldNotContain(MeshSurfaceStep.StepId);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.SurveyModels.CountAsync(m => m.CaveFeatureId == caveId && m.Format == SurveyModelFormat.Stl)).ShouldBe(0);
            var recipe = ScanRecipes.FromOptionsJson((await db.SurveyUploads.AsNoTracking().SingleAsync(u => u.Id == uploadId)).Options).ShouldNotBeNull();
            recipe.Surface.ShouldBeNull();
            recipe.Steps.ShouldNotContain(s => s.StepId == MeshSurfaceStep.StepId);
        }
    }

    [Fact]
    public async Task A_profile_chooses_the_values_and_another_cave_may_be_chosen_while_a_profile_of_another_step_is_refused()
    {
        var caveId = await CreateCaveAsync();
        var otherCaveId = await CreateCaveAsync();
        var (uploadId, scanId) = await TiledScanAsync(caveId);
        await PlaceAsync(owner, scanId, headingDeg: 0);

        await ShouldBeProblemAsync(
            await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{uploadId}/surface", new { profileId = BuiltInProcessingProfiles.HdmappingDefaultsId }),
            HttpStatusCode.BadRequest, "survey_upload.profile_step_not_in_recipe");
        await ShouldBeProblemAsync(
            await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{uploadId}/surface", new { profileId = Guid.NewGuid() }),
            HttpStatusCode.BadRequest, ProcessingProfileResolver.UnknownCode);
        await ShouldBeProblemAsync(
            await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{uploadId}/surface", new { caveId = Guid.Empty }),
            HttpStatusCode.BadRequest, null);

        var asked = await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{uploadId}/surface", new
        {
            caveId = otherCaveId,
            profileId = BuiltInProcessingProfiles.SurfaceQuickLookId,
        });
        asked.StatusCode.ShouldBe(HttpStatusCode.Accepted, await asked.Content.ReadAsStringAsync());
        await RunPipelineAsync(uploadId);

        var parameters = worker.Requests.Last(r => r.GetProperty("step").GetString() == MeshSurfaceStep.StepId).GetProperty("parameters");
        parameters.GetProperty("voxelCm").GetDouble().ShouldBe(5);
        parameters.GetProperty("poissonDepth").GetInt32().ShouldBe(8);
        // A key the profile does not name keeps the step's default.
        parameters.GetProperty("densityTrimPercent").GetDouble().ShouldBe(2);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.SurveyModels.CountAsync(m => m.CaveFeatureId == otherCaveId && m.Format == SurveyModelFormat.Stl)).ShouldBe(1);
        (await db.SurveyModels.CountAsync(m => m.CaveFeatureId == caveId && m.Format == SurveyModelFormat.Stl)).ShouldBe(0);
        // The profile used is remembered for the next surface.
        (await db.ScanProfileChoices.AsNoTracking().Where(c => c.StepId == MeshSurfaceStep.StepId).Select(c => c.ProfileId).ToListAsync())
            .ShouldContain(BuiltInProcessingProfiles.SurfaceQuickLookId);
    }

    [Fact]
    public async Task Asking_needs_a_signed_in_writer_who_may_place_both_caves_and_a_placed_tiled_scan()
    {
        var caveId = await CreateCaveAsync();
        var (uploadId, scanId) = await TiledScanAsync(caveId);

        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync($"/api/v1/survey-uploads/{uploadId}/surface", new { })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Not placed yet: the surface would have nowhere to stand.
        await ShouldBeProblemAsync(
            await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{uploadId}/surface", new { }),
            HttpStatusCode.Conflict, SurveyUploadEndpoints.SurfaceNeedsPlacementCode);
        await PlaceAsync(owner, scanId, headingDeg: 0);

        // A reader who sees the scan and its place but may not write the cave.
        await GrantAsync(caveId, "read, viewExactLocation");
        await ShouldBeProblemAsync(
            await reader.PostAsJsonAsync($"/api/v1/survey-uploads/{uploadId}/surface", new { }),
            HttpStatusCode.Forbidden, null);

        // A writer of the scan's cave asking for a protected cave they may write but not place: told
        // it is not there, exactly as for a missing cave. The same request once they may place it is taken.
        await GrantAsync(caveId, "read, write, viewExactLocation");
        var guarded = await CreateCaveAsync(locationProtected: true);
        await GrantAsync(guarded, "read, write");
        await ShouldBeProblemAsync(
            await reader.PostAsJsonAsync($"/api/v1/survey-uploads/{uploadId}/surface", new { caveId = guarded }),
            HttpStatusCode.NotFound, SurveyUploadEndpoints.CaveNotFoundCode);
        await ShouldBeProblemAsync(
            await reader.PostAsJsonAsync($"/api/v1/survey-uploads/{uploadId}/surface", new { caveId = Guid.NewGuid() }),
            HttpStatusCode.NotFound, SurveyUploadEndpoints.CaveNotFoundCode);
        await GrantAsync(guarded, "read, write, viewExactLocation");
        var taken = await reader.PostAsJsonAsync($"/api/v1/survey-uploads/{uploadId}/surface", new { caveId = guarded });
        taken.StatusCode.ShouldBe(HttpStatusCode.Accepted, await taken.Content.ReadAsStringAsync());

        // Queued now: a second request waits for the first.
        await ShouldBeProblemAsync(
            await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{uploadId}/surface", new { }),
            HttpStatusCode.Conflict, SurveyUploadEndpoints.RunInProgressCode);
    }

    [Fact]
    public async Task A_scan_whose_cave_location_the_caller_may_not_see_is_answered_as_missing()
    {
        var caveId = await CreateCaveAsync(locationProtected: true);
        var (uploadId, scanId) = await TiledScanAsync(caveId);
        await PlaceAsync(owner, scanId, headingDeg: 0);

        // Read and write on the protected cave, but not its exact location.
        await GrantAsync(caveId, "read, write");
        await ShouldBeProblemAsync(
            await reader.PostAsJsonAsync($"/api/v1/survey-uploads/{uploadId}/surface", new { }),
            HttpStatusCode.NotFound, SurveyUploadEndpoints.NotFoundCode);

        await GrantAsync(caveId, "read, write, viewExactLocation");
        var taken = await reader.PostAsJsonAsync($"/api/v1/survey-uploads/{uploadId}/surface", new { });
        taken.StatusCode.ShouldBe(HttpStatusCode.Accepted, await taken.Content.ReadAsStringAsync());
    }

    // ---- helpers ----

    /// <summary>One triangle in the tile frame: at the zero point, one metre along +x, two along +y.</summary>
    private static readonly byte[] SurfaceStl = MakeStl();

    private static byte[] MakeStl()
    {
        var bytes = new byte[84 + 50];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(80), 1);
        float[] values = [0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 2, 0];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(84 + (i * 4)), values[i]);
        }

        return bytes;
    }

    private static (float X, float Y, float Z) Corner(byte[] stl, int corner)
    {
        var at = stl.AsSpan(84 + 12 + ((corner - 1) * 12));
        return (
            MathF.Round(BinaryPrimitives.ReadSingleLittleEndian(at), 5),
            MathF.Round(BinaryPrimitives.ReadSingleLittleEndian(at[4..]), 5),
            MathF.Round(BinaryPrimitives.ReadSingleLittleEndian(at[8..]), 5));
    }

    private static readonly byte[] TilesZip = MakeZip();

    private static byte[] MakeZip()
    {
        using var stream = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in new[] { "tileset.json", "r.pnts" })
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write("{}"u8);
            }
        }

        return stream.ToArray();
    }

    private static Task Worker(string directory, JsonElement request)
    {
        var step = request.GetProperty("step").GetString();
        switch (step)
        {
            case PointCloudEstimateStep.StepId:
            {
                var data = new
                {
                    pointCount = 3,
                    hasRgb = false,
                    hasIntensity = false,
                    full = new { points = 3, estimatedTileBytes = 51, estimatedSeconds = 17 },
                    voxels = Array.Empty<object>(),
                };
                var output = CannedScanWorker.Output(directory, "estimate", "estimate.json", JsonSerializer.SerializeToUtf8Bytes(data));
                CannedScanWorker.Finish(directory, "ok", [output], data);
                break;
            }

            case Py3dtilesTileStep.StepId:
            {
                var output = CannedScanWorker.Output(directory, "pointCloudTiles", "tiles.zip", TilesZip);
                CannedScanWorker.Finish(
                    directory, "ok", [output],
                    new { pointCount = 3, originShift = Shift, tileCount = 2, tileBytes = 4, archiveBytes = TilesZip.LongLength, intensity = false, rgb = false },
                    tool: "py3dtiles");
                break;
            }

            case MeshSurfaceStep.StepId:
            {
                File.Exists(Path.Combine(directory, request.GetProperty("inputs").GetProperty("pointCloudLaz").GetString()!)).ShouldBeTrue();
                var output = CannedScanWorker.Output(directory, "meshStl", "surface.stl", SurfaceStl);
                CannedScanWorker.Finish(directory, "ok", [output], new { triangles = 1, vertices = 3 }, tool: "open3d", toolVersion: "0.19.0");
                break;
            }

            default:
                CannedScanWorker.Finish(directory, "refused", [], reason: $"step '{step}' is not one this worker runs");
                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>A scan uploaded to the cave and run through estimate and tiling; its upload and model.</summary>
    private async Task<(Guid UploadId, Guid ScanId)> TiledScanAsync(Guid caveId)
    {
        var bytes = SyntheticLas.Las(points: 3, compressed: true);
        var response = await owner.PostAsJsonAsync("/api/v1/survey-uploads", new
        {
            source = "pointcloud",
            cave = new { id = caveId },
            client = new { name = "silexgis tests", version = "1" },
            surveyTitle = "Surface scan",
            files = new[] { new { name = "scan.laz", size = bytes.LongLength, role = "archive" } },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        (await owner.PutAsync($"/api/v1/survey-uploads/{id}/files/scan.laz", content)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.PostAsync($"/api/v1/survey-uploads/{id}/complete", null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await RunPipelineAsync(id);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var scan = await db.SurveyModels.AsNoTracking().SingleAsync(m => m.SurveyUploadId == id);
        scan.ConvertedFileId.ShouldNotBeNull();
        return (id, scan.Id);
    }

    private static async Task PlaceAsync(HttpClient client, Guid scanId, double headingDeg)
    {
        var placed = await client.PutAsJsonAsync($"/api/v1/survey-models/{scanId}/scan-placement", new
        {
            longitude = 25.5,
            latitude = 45.25,
            heightM = 320.0,
            headingDeg,
        });
        placed.StatusCode.ShouldBe(HttpStatusCode.OK, await placed.Content.ReadAsStringAsync());
    }

    private async Task<Guid> MeshIdAsync(Guid caveId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.SurveyModels.AsNoTracking()
            .Where(m => m.CaveFeatureId == caveId && m.Format == SurveyModelFormat.Stl).Select(m => m.Id).SingleAsync();
    }

    private async Task RunMeshJobAsync(Guid modelId)
    {
        var mine = (await QueuedJob.OfKindAsync(factory.Services, ProcessingJobKinds.SurveyMesh))
            .Where(j => JsonSerializer.Deserialize<SurveyMeshPayload>(j.Payload, JsonSerializerOptions.Web)?.SurveyModelId == modelId)
            .ToList();
        mine.ShouldNotBeEmpty();
        foreach (var queued in mine)
        {
            await QueuedJob.RunAsync(factory.Services, queued.Id);
        }
    }

    /// <summary>Runs the processing job the upload's completion or request queued, exactly once.</summary>
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
            name = $"Surface Cave {Guid.NewGuid():N}"[..28],
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

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string? code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, body);
        if (code is not null)
        {
            JsonDocument.Parse(body).RootElement.GetProperty("code").GetString().ShouldBe(code, body);
        }
    }
}
