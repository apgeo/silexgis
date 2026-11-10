// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
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
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Scans;

namespace SilexGis.Api.Tests;

/// <summary>
/// A scan's step done on another computer: the run stops before it, waiting; the download holds
/// the step's inputs, its parameters in the tool's own format and a README; a product sent back
/// under the wrong name or with the wrong content is refused; the right one queues the run again,
/// which reuses it and carries on. Both routes are for writers who see the exact location.
/// </summary>
/// <remarks>
/// The worker is a canned one; every recording and session is invented (a handful of zero points,
/// three-line IMU logs, an empty session file). Nothing here comes from a real scanner.
/// </remarks>
public sealed class SurveyUploadExternalStepTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
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

    public SurveyUploadExternalStepTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-suext-{Guid.NewGuid():N}");
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
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"suext-own-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"suext-read-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"suext-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"suext-read-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();

        var handoff = factory.Services.GetRequiredService<ScanWorkerHandoff>();
        handoff.PollInterval = TimeSpan.FromMilliseconds(50);
        handoff.WithdrawalWait = TimeSpan.FromSeconds(1);

        CannedScanWorker.Announce(
            handoffRoot,
            HdmappingOdometryStep.StepId, HdmappingExportStep.StepId, PointCloudCheckStep.StepId, PointCloudRasterStep.StepId,
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
    public async Task Odometry_done_elsewhere_stops_the_run_offers_its_inputs_and_carries_on_when_the_session_arrives()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        var recording = Recording();
        var id = await SendAsync(owner, "Laptop scan", caveId, recording, externalSteps: [HdmappingOdometryStep.StepId]);
        ScanRecipes.FromOptionsJson(await OptionsAsync(id)).ShouldNotBeNull().External.ShouldBe([HdmappingOdometryStep.StepId]);
        await RunPipelineAsync(id);

        // Stopped before the odometry: the worker was not asked for anything.
        worker.Steps.ShouldBeEmpty();
        var waiting = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        waiting.GetProperty("status").GetString().ShouldBe("succeeded", waiting.ToString());
        waiting.GetProperty("stage").GetString().ShouldBe("awaitingExternal");
        waiting.GetProperty("message").GetString().ShouldNotBeNull().ShouldContain("another computer");
        // What the page offers the writer: the step, and each product's names and whether it is in.
        var external = waiting.GetProperty("external");
        external.GetProperty("stepId").GetString().ShouldBe(HdmappingOdometryStep.StepId);
        external.GetProperty("tool").GetString().ShouldBe("hdmapping");
        var output = external.GetProperty("outputs").EnumerateArray().ShouldHaveSingleItem();
        output.GetProperty("names").EnumerateArray().Select(n => n.GetString()).ShouldBe(["session.zip"]);
        output.GetProperty("received").GetBoolean().ShouldBeFalse();

        // The download: the README, HDMapping's own parameter file with the resolved values, and
        // every file of the recording under its own name.
        var bundle = await owner.GetAsync($"/api/v1/survey-uploads/{id}/external/bundle");
        bundle.StatusCode.ShouldBe(HttpStatusCode.OK, await bundle.Content.ReadAsStringAsync());
        bundle.Content.Headers.ContentType!.MediaType.ShouldBe("application/zip");
        using (var zip = new ZipArchive(new MemoryStream(await bundle.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read))
        {
            var names = zip.Entries.Select(e => e.FullName).ToList();
            names.ShouldContain("README.txt");
            names.ShouldContain("parameters.toml");
            foreach (var (name, _) in recording)
            {
                names.ShouldContain($"inputs/{name}");
            }

            names.Count.ShouldBe(recording.Count + 2);
            using var toml = new StreamReader(zip.GetEntry("parameters.toml")!.Open());
            var parameters = await toml.ReadToEndAsync();
            parameters.ShouldContain("[lidar_odometry_control]");
            parameters.ShouldContain("decimation");
            using var readme = new StreamReader(zip.GetEntry("README.txt")!.Open());
            (await readme.ReadToEndAsync()).ShouldContain("session.zip");
        }

        // A name the step makes no product by, and a session that would not unpack where the
        // export looks for it, are refused and nothing is kept.
        var unknown = await PutAsync(owner, id, "cloud.laz", SyntheticLas.Las(2, compressed: true));
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("survey_upload.external_output_unknown");
        var notAZip = await PutAsync(owner, id, "session.zip", "not a zip"u8.ToArray());
        (await notAZip.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("survey_upload.external_output_invalid");
        var folderZipped = await PutAsync(owner, id, "session.zip", Zip(("lio/session.mjs", "{}")));
        folderZipped.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await folderZipped.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("survey_upload.external_output_invalid");
        var escaping = await PutAsync(owner, id, "session.zip", Zip(("session.mjs", "{}"), ("../outside.laz", "x")));
        (await escaping.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("survey_upload.external_output_invalid");
        (await ArtefactsAsync(id)).ShouldNotContain(a => a.Kind == ScanArtifactKind.HdmSession);

        // The session: the run is queued again, and goes on from the export.
        var sent = await PutAsync(owner, id, "Session.ZIP", Zip(("session.mjs", "{}"), ("scan_lio_0.laz", "x")));
        sent.StatusCode.ShouldBe(HttpStatusCode.Accepted, await sent.Content.ReadAsStringAsync());
        (await sent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().ShouldBe("queued");
        await RunPipelineAsync(id);

        worker.Steps.ShouldBe([
            HdmappingExportStep.StepId, PointCloudCheckStep.StepId, PointCloudRasterStep.StepId, PointCloudEstimateStep.StepId, Py3dtilesTileStep.StepId,
        ]);
        var done = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        done.GetProperty("status").GetString().ShouldBe("succeeded", done.ToString());
        done.GetProperty("stage").GetString().ShouldBe("store");
        IsAbsentOrNull(done, "external").ShouldBeTrue();

        var artefacts = await ArtefactsAsync(id);
        var session = artefacts.Single(a => a.Kind == ScanArtifactKind.HdmSession);
        session.StepId.ShouldBe(HdmappingOdometryStep.StepId);
        session.ToolVersion.ShouldBeNull();
        session.InputArtifactIds.ShouldBe(
            artefacts.Where(a => a.Kind == ScanArtifactKind.RawRecording).OrderBy(a => a.Id).Select(a => a.Id).ToArray());
        artefacts.Single(a => a.Kind == ScanArtifactKind.PointCloudLaz).InputArtifactIds.ShouldBe([session.Id]);

        // No longer waiting: neither route answers.
        var late = await owner.GetAsync($"/api/v1/survey-uploads/{id}/external/bundle");
        late.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await late.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("survey_upload.not_awaiting_external");
        (await PutAsync(owner, id, "session.zip", Zip(("session.mjs", "{}")))).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // A plain rerun keeps the step done elsewhere and reuses the session sent for it.
        lock (worker.Steps)
        {
            worker.Steps.Clear();
        }

        (await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", new { })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await RunPipelineAsync(id);
        worker.Steps.ShouldBeEmpty();
        (await ArtefactsAsync(id)).Single(a => a.Kind == ScanArtifactKind.HdmSession).Id.ShouldBe(session.Id);
    }

    [Fact]
    public async Task Only_a_writer_who_sees_the_exact_location_takes_the_inputs_or_sends_a_product()
    {
        var caveId = await CreateCaveAsync(locationProtected: true);
        var id = await SendAsync(owner, "Guarded scan", caveId, Recording(), externalSteps: [HdmappingOdometryStep.StepId]);
        await RunPipelineAsync(id);

        // Nobody signed in: refused before anything is looked up.
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/survey-uploads/{id}/external/bundle")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Read and write but not the exact location: the upload does not exist for them.
        await GrantAsync(caveId, "read, write");
        (await reader.GetAsync($"/api/v1/survey-uploads/{id}/external/bundle")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PutAsync(reader, id, "session.zip", Zip(("session.mjs", "{}")))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The exact location but no write: seen, and refused.
        await GrantAsync(caveId, "read, viewExactLocation");
        (await reader.GetAsync($"/api/v1/survey-uploads/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        // Told that the run waits (its stage), but not what to send: that is for a writer.
        IsAbsentOrNull(await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}"), "external").ShouldBeTrue();
        (await reader.GetAsync($"/api/v1/survey-uploads/{id}/external/bundle")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await PutAsync(reader, id, "session.zip", Zip(("session.mjs", "{}")))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // All three: the download is served, and the session taken.
        await GrantAsync(caveId, "read, write, viewExactLocation");
        (await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}"))
            .GetProperty("external").GetProperty("stepId").GetString().ShouldBe(HdmappingOdometryStep.StepId);
        (await reader.GetAsync($"/api/v1/survey-uploads/{id}/external/bundle")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PutAsync(reader, id, "session.zip", Zip(("session.mjs", "{}")))).StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task An_upload_that_waits_for_nothing_offers_no_download()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        var id = await SendAsync(owner, "Server scan", caveId, Recording());

        // Queued, not yet run.
        var queued = await owner.GetAsync($"/api/v1/survey-uploads/{id}/external/bundle");
        queued.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await queued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("survey_upload.not_awaiting_external");

        // Run through on the server: still nothing to wait for.
        await RunPipelineAsync(id);
        worker.Steps.ShouldContain(HdmappingOdometryStep.StepId);
        (await owner.GetAsync($"/api/v1/survey-uploads/{id}/external/bundle")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await PutAsync(owner, id, "session.zip", Zip(("session.mjs", "{}")))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Only_a_step_a_worker_would_run_may_be_chosen_to_run_elsewhere()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        foreach (var refused in new[] { "no.such.step", PointCloudInspectStep.StepId, "py3dtiles.tile" })
        {
            var response = await OpenAsync(owner, new Dictionary<string, object?>
            {
                ["source"] = "mandeye",
                ["cave"] = new { id = caveId },
                ["surveyTitle"] = "Refused scan",
                ["files"] = Declared(Recording()),
                ["externalSteps"] = new[] { refused },
            });
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, refused);
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("survey_upload.external_step_invalid");
        }

        // A survey tool's upload has no steps to move.
        var survey = await OpenAsync(owner, new Dictionary<string, object?>
        {
            ["source"] = "topodroid",
            ["cave"] = new { id = caveId },
            ["surveyTitle"] = "Refused survey",
            ["files"] = new[] { new { name = "survey.zip", size = 10L, role = "archive" } },
            ["externalSteps"] = new[] { HdmappingOdometryStep.StepId },
        });
        survey.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await survey.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("survey_upload.external_step_invalid");

        // A step the recipe runs on a worker is taken, once however often it is named.
        var taken = await SendAsync(
            owner, "Taken scan", caveId, Recording(), externalSteps: [HdmappingExportStep.StepId, HdmappingExportStep.StepId]);
        ScanRecipes.FromOptionsJson(await OptionsAsync(taken)).ShouldNotBeNull().External.ShouldBe([HdmappingExportStep.StepId]);
    }

    [Fact]
    public async Task A_session_refined_on_a_desktop_is_the_scans_next_version_and_its_run_starts_at_the_export()
    {
        var caveId = await CreateCaveAsync(locationProtected: true);
        var first = await SendAsync(owner, "Desk scan", caveId, Recording());
        await RunPipelineAsync(first);
        var processed = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{first}");
        processed.GetProperty("stage").GetString().ShouldBe("store", processed.ToString());
        var surveyId = processed.GetProperty("caveSurvey").GetProperty("id").GetGuid();

        // The session the odometry made is offered for the desktop program.
        var sessionUrl = processed.GetProperty("pointCloud").GetProperty("sessionUrl").GetString().ShouldNotBeNull();
        var downloaded = await owner.GetAsync(sessionUrl);
        downloaded.StatusCode.ShouldBe(HttpStatusCode.OK, await downloaded.Content.ReadAsStringAsync());
        using (var zip = new ZipArchive(new MemoryStream(await downloaded.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read))
        {
            zip.GetEntry("session.mjs").ShouldNotBeNull();
        }

        lock (worker.Steps)
        {
            worker.Steps.Clear();
        }

        // The session is sent alone, as the next version of the scan's survey, by a writer who
        // sees the exact location, and the run starts at the export: no odometry is asked of the worker.
        await GrantAsync(caveId, "read, write, viewExactLocation");
        var session = Zip(("session.mjs", "{}"), ("scan_lio_0.laz", "refined"));
        var second = await SendAsync(reader, "Desk scan", caveId, [("session.zip", session)], caveSurveyId: surveyId);
        await RunPipelineAsync(second);
        worker.Steps.ShouldBe([
            HdmappingExportStep.StepId, PointCloudCheckStep.StepId, PointCloudRasterStep.StepId, PointCloudEstimateStep.StepId, Py3dtilesTileStep.StepId,
        ]);
        var refined = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{second}");
        refined.GetProperty("status").GetString().ShouldBe("succeeded", refined.ToString());
        refined.GetProperty("stage").GetString().ShouldBe("store");
        refined.GetProperty("caveSurvey").GetProperty("id").GetGuid().ShouldBe(surveyId);

        var artefacts = await ArtefactsAsync(second);
        var sent = artefacts.Single(a => a.Kind == ScanArtifactKind.HdmSession);
        sent.StepId.ShouldBe(ScanSteps.UploadStepId);
        artefacts.ShouldNotContain(a => a.Kind == ScanArtifactKind.RawRecording);
        artefacts.Single(a => a.Kind == ScanArtifactKind.PointCloudLaz).InputArtifactIds.ShouldBe([sent.Id]);
        ScanRecipes.FromOptionsJson(await OptionsAsync(second)).ShouldNotBeNull()
            .Steps.ShouldNotContain(s => s.StepId == HdmappingOdometryStep.StepId);

        // A rerun with a new choice of how much to show keeps starting at the export.
        lock (worker.Steps)
        {
            worker.Steps.Clear();
        }

        (await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{second}/rerun", new { display = new { method = "full" } }))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await RunPipelineAsync(second);
        worker.Steps.ShouldNotContain(HdmappingOdometryStep.StepId);
        ScanRecipes.FromOptionsJson(await OptionsAsync(second)).ShouldNotBeNull()
            .Steps.ShouldNotContain(s => s.StepId == HdmappingOdometryStep.StepId);

        // The session is the scanner's path. Its sender, while they see the exact location, is
        // linked to it and offered it for the desktop; once that right is gone they still see how
        // their upload went, but are linked to neither.
        static string? LinkOf(JsonElement upload, string name) =>
            upload.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("name").GetString() == name)
                .GetProperty("contentUrl").GetString();
        var withExact = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{second}");
        LinkOf(withExact, "session.zip").ShouldNotBeNull();
        withExact.GetProperty("pointCloud").GetProperty("sessionUrl").GetString().ShouldNotBeNull();

        await GrantAsync(caveId, "read, write");
        var withoutExact = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{second}");
        withoutExact.GetProperty("status").GetString().ShouldBe("succeeded");
        LinkOf(withoutExact, "session.zip").ShouldBeNull();
        withoutExact.GetProperty("pointCloud").GetProperty("sessionUrl").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_refined_session_is_refused_unless_it_is_alone_and_names_a_scan_processed_here()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        var session = new { name = "session.zip", size = 100L, role = "archive" };

        // No survey named.
        var unnamed = await OpenAsync(owner, new Dictionary<string, object?>
        {
            ["source"] = "mandeye",
            ["cave"] = new { id = caveId },
            ["surveyTitle"] = "Loose session",
            ["files"] = new[] { session },
        });
        unnamed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unnamed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("survey_upload.session_needs_scan");

        // A survey whose recording was never processed holds no session to refine.
        var unrun = await SendAsync(owner, "Unrun scan", caveId, Recording());
        var unrunSurvey = (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{unrun}"))
            .GetProperty("caveSurvey").GetProperty("id").GetGuid();
        var unprocessed = await OpenAsync(owner, new Dictionary<string, object?>
        {
            ["source"] = "mandeye",
            ["caveSurvey"] = new { id = unrunSurvey },
            ["surveyTitle"] = "Unrun scan",
            ["files"] = new[] { session },
        });
        unprocessed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unprocessed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("survey_upload.session_needs_scan");

        // A session sent with a recording beside it.
        var mixed = await OpenAsync(owner, new Dictionary<string, object?>
        {
            ["source"] = "mandeye",
            ["caveSurvey"] = new { id = unrunSurvey },
            ["surveyTitle"] = "Unrun scan",
            ["files"] = new object[] { session, new { name = "imu0000.csv", size = 10L, role = "archive" } },
        });
        mixed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await mixed.Content.ReadAsStringAsync()).ShouldContain("alone");
    }

    // ---- helpers ----

    private static object[] Declared(IReadOnlyList<(string Name, byte[] Bytes)> files) =>
        [.. files.Select(f => new { name = f.Name, size = f.Bytes.LongLength, role = "archive" })];

    private static Task<HttpResponseMessage> OpenAsync(HttpClient client, Dictionary<string, object?> body)
    {
        body.TryAdd("client", new { name = "silexgis tests", version = "1" });
        return client.PostAsJsonAsync("/api/v1/survey-uploads", body);
    }

    private async Task<string?> OptionsAsync(Guid uploadId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.SurveyUploads.AsNoTracking().Where(u => u.Id == uploadId).Select(u => u.Options).SingleAsync();
    }

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        return stream.ToArray();
    }

    private static readonly byte[] TilesZip = Zip(("tileset.json", "{}"), ("r.pnts", "{}"));

    /// <summary>A canned HDMapping worker and tiler: every step answers ok with invented products.</summary>
    private static Task Worker(string directory, JsonElement request)
    {
        switch (request.GetProperty("step").GetString())
        {
            case HdmappingOdometryStep.StepId:
                CannedScanWorker.Finish(
                    directory, "ok", [CannedScanWorker.Output(directory, "hdmSession", "session.zip", Zip(("session.mjs", "{}")))], tool: "hdmapping");
                break;
            case HdmappingExportStep.StepId:
                File.Exists(Path.Combine(directory, request.GetProperty("inputs").GetProperty("hdmSession").GetString()!)).ShouldBeTrue();
                CannedScanWorker.Finish(
                    directory,
                    "ok",
                    [
                        CannedScanWorker.Output(directory, "pointCloudLaz", "export.laz", SyntheticLas.Las(7, compressed: true)),
                        CannedScanWorker.Output(directory, "trajectory", "trajectory.csv", Encoding.ASCII.GetBytes("1 0 0 0 0 0 0 0 1\n")),
                    ],
                    tool: "hdmapping");
                break;
            case PointCloudRasterStep.StepId:
                CannedScanWorker.Pictures(directory, request);
                break;

            case PointCloudCheckStep.StepId:
                CannedScanWorker.Finish(directory, "ok", [CannedScanWorker.Output(directory, "report", "check.json", "{}"u8.ToArray())], new { pointCount = 7 });
                break;
            case PointCloudEstimateStep.StepId:
                var data = new
                {
                    pointCount = 7,
                    hasRgb = false,
                    hasIntensity = true,
                    full = new { points = 7, estimatedTileBytes = 119, estimatedSeconds = 1 },
                    voxels = Array.Empty<object>(),
                };
                CannedScanWorker.Finish(
                    directory, "ok", [CannedScanWorker.Output(directory, "estimate", "estimate.json", JsonSerializer.SerializeToUtf8Bytes(data))], data);
                break;
            case Py3dtilesTileStep.StepId:
                CannedScanWorker.Finish(
                    directory, "ok", [CannedScanWorker.Output(directory, "pointCloudTiles", "tiles.zip", TilesZip)],
                    new { pointCount = 7, originShift = new[] { 0.5, 0.5, 0.5 }, tileCount = 2, archiveBytes = TilesZip.LongLength },
                    tool: "py3dtiles");
                break;
            default:
                CannedScanWorker.Finish(directory, "refused", [], reason: "not a step this worker runs");
                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>An invented two-chunk recording with no satellite log.</summary>
    private static List<(string Name, byte[] Bytes)> Recording()
    {
        const string header = "timestamp gyroX gyroY gyroZ accX accY accZ imuId timestampUnix\n";
        return
        [
            ("lidar0000.laz", SyntheticLas.Las(2, compressed: true)),
            ("imu0000.csv", Encoding.ASCII.GetBytes(header + "1000000000 0.1 0.2 0.3 0 0 1 0 0\n1500000000 0.1 0.2 0.3 0 0 1 0 0\n")),
            ("lidar0001.laz", SyntheticLas.Las(3, compressed: true)),
            ("imu0001.csv", Encoding.ASCII.GetBytes(header + "2000000000 0.1 0.2 0.3 0 0 1 0 0\n3500000000 0.1 0.2 0.3 0 0 1 0 0\n")),
        ];
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, Guid uploadId, string name, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return client.PutAsync($"/api/v1/survey-uploads/{uploadId}/external/{Uri.EscapeDataString(name)}", content);
    }

    private async Task<List<ScanArtifact>> ArtefactsAsync(Guid uploadId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ScanArtifacts.AsNoTracking().Where(a => a.SurveyUploadId == uploadId).ToListAsync();
    }

    private static async Task<Guid> SendAsync(
        HttpClient client,
        string title,
        Guid caveId,
        IReadOnlyList<(string Name, byte[] Bytes)> recording,
        string[]? externalSteps = null,
        Guid? caveSurveyId = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["source"] = "mandeye",
            ["surveyTitle"] = title,
            ["files"] = Declared(recording),
            ["externalSteps"] = externalSteps,
        };
        if (caveSurveyId is { } surveyId)
        {
            body["caveSurvey"] = new { id = surveyId };
        }
        else
        {
            body["cave"] = new { id = caveId };
        }

        var response = await OpenAsync(client, body);
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

    /// <summary>Runs the processing job the upload's completion, rerun or last product queued, exactly once.</summary>
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
            name = $"Ext Cave {Guid.NewGuid():N}"[..27],
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

    private static bool IsAbsentOrNull(JsonElement dto, string member) =>
        !dto.TryGetProperty(member, out var value) || value.ValueKind == JsonValueKind.Null;
}
