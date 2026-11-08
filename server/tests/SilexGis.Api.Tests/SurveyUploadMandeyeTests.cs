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

namespace SilexGis.Api.Tests;

/// <summary>
/// A raw MANDEYE recording sent as a survey upload: every file of the folder declared and
/// archived as the scan's original, the recording inspected by the run — chunks paired, headers
/// read, IMU columns checked, sensors counted, GNSS noted — and its facts recorded on the scan;
/// without a worker that runs HDMapping the run stops there and says so. A recording with GNSS is
/// a location, and its files are handed out only with the exact-location right.
/// </summary>
/// <remarks>
/// Every recording here is written by the test: LAS headers with a handful of zero points, IMU
/// logs of three invented samples, invented serial numbers and a one-line GNSS log. Nothing here
/// comes from a real scanner.
/// </remarks>
public sealed class SurveyUploadMandeyeTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const long ScanCap = 4000;
    private const long RawCap = 20_000;

    private readonly PostgresFixture postgres;
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private Guid readerId;
    private long caveTypeId;

    public SurveyUploadMandeyeTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-sumd-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            // Small enough for a test to go over them with a few kilobytes.
            ["Scans:MaxUploadBytes"] = ScanCap.ToString(CultureInfo.InvariantCulture),
            ["Scans:MaxRawUploadBytes"] = RawCap.ToString(CultureInfo.InvariantCulture),
            ["Scans:MaxRawFiles"] = "12",
        }, JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"sumd-own-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"sumd-read-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"sumd-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"sumd-read-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    [Fact]
    public async Task A_recording_is_archived_inspected_and_its_facts_recorded_and_it_waits_for_an_hdmapping_worker()
    {
        var caveId = await CreateCaveAsync("authenticated", locationProtected: false);
        var recording = Recording(gnss: false);
        var id = await SendAsync(owner, "Galeria scan", caveId, recording);
        await RunPipelineAsync(id);

        // The run inspected the recording and stopped where odometry would start, saying why.
        var shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        shown.GetProperty("status").GetString().ShouldBe("succeeded", shown.ToString());
        shown.GetProperty("stage").GetString().ShouldBe("odometry");
        shown.GetProperty("progress").GetInt32().ShouldBe(100);
        shown.GetProperty("message").GetString().ShouldBe(SurveyPipelineNotes.NoOdometry);

        var facts = shown.GetProperty("pointCloud");
        facts.GetProperty("pointCount").GetInt64().ShouldBe(5);
        facts.GetProperty("georeferenced").GetBoolean().ShouldBeFalse();

        // The recording's own facts reach the screen beside the cloud's.
        var read = facts.GetProperty("recording");
        read.GetProperty("chunkCount").GetInt32().ShouldBe(2);
        read.GetProperty("sensorCount").GetInt32().ShouldBe(2);
        read.GetProperty("hasGnss").GetBoolean().ShouldBeFalse();
        read.GetProperty("durationSeconds").GetDouble().ShouldBe(2.5);
        read.GetProperty("bytes").GetInt64().ShouldBe(recording.Sum(f => (long)f.Bytes.Length));

        // Every file of the folder is archived as the recording's original, none of them as a
        // document of the cave.
        var files = shown.GetProperty("files").EnumerateArray().ToList();
        files.Count.ShouldBe(recording.Count);
        files.ShouldAllBe(f => f.GetProperty("role").GetString() == "archive");
        files.ShouldAllBe(f => f.GetProperty("sourceId").ValueKind == JsonValueKind.String);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.SurveySources.AsNoTracking().CountAsync(s => s.SurveyUploadId == id && s.Kind == SurveySourceKind.LidarRawSession))
            .ShouldBe(recording.Count);
        (await db.Attachments.AsNoTracking().CountAsync(a => a.FeatureId == caveId)).ShouldBe(0);

        // What the inspection found is on the scan's model, under the recording's own facts.
        var model = await db.SurveyModels.AsNoTracking().SingleAsync(m => m.SurveyUploadId == id);
        model.Format.ShouldBe(SurveyModelFormat.Laz);
        var stored = PointCloudFacts.FromJson(model.PointCloud).ShouldNotBeNull();
        stored.Recording.ShouldBe(new MandeyeRecordingFacts(
            ChunkCount: 2, SensorCount: 2, HasCalibration: false, HasGnss: false, DurationSeconds: 2.5,
            PointCount: 5, ImuHasHeader: true, Bytes: recording.Sum(f => (long)f.Bytes.Length)));

        // The scan is listed on the cave as a recording; a rerun refreshes the one model.
        var listed = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads?caveId={caveId}");
        listed.GetProperty("items")[0].GetProperty("source").GetString().ShouldBe("mandeye");
        listed.GetProperty("items")[0].GetProperty("pointCloud").GetProperty("pointCount").GetInt64().ShouldBe(5);
        (await owner.PostAsync($"/api/v1/survey-uploads/{id}/rerun", null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await RunPipelineAsync(id);
        (await db.SurveyModels.AsNoTracking().CountAsync(m => m.SurveyUploadId == id)).ShouldBe(1);
    }

    [Fact]
    public async Task The_declared_folder_is_checked_against_the_recording_rules_and_caps()
    {
        var caveId = await CreateCaveAsync("authenticated", locationProtected: false);

        // The limits are published, so a screen can check a folder before it opens an upload.
        var capabilities = await owner.GetFromJsonAsync<JsonElement>("/api/v1/survey-uploads/capabilities");
        var limits = capabilities.GetProperty("pointCloud").GetProperty("recording");
        limits.GetProperty("maxFileBytes").GetInt64().ShouldBe(ScanCap);
        limits.GetProperty("maxTotalBytes").GetInt64().ShouldBe(RawCap);
        limits.GetProperty("maxFiles").GetInt32().ShouldBe(12);
        limits.GetProperty("acceptedExtensions").EnumerateArray().Select(e => e.GetString()).ShouldContain(".csv");
        limits.GetProperty("odometryWorkerFactor").GetInt32().ShouldBe(ScanStorageNeeds.OdometryWorkerFactor);

        await ShouldBeProblemAsync(await Open(owner, caveId, File("lidar0000.laz", 10)), HttpStatusCode.BadRequest, SurveyUploadFiles.InvalidCode);
        await ShouldBeProblemAsync(
            await Open(owner, caveId, File("lidar0000.laz", 10), File("imu0000.csv", 10), File("photo.jpg", 10)),
            HttpStatusCode.BadRequest, SurveyUploadFiles.InvalidCode);
        await ShouldBeProblemAsync(
            await Open(owner, caveId, File("lidar0000.laz", ScanCap + 1), File("imu0000.csv", 10)),
            HttpStatusCode.BadRequest, SurveyUploadFiles.InvalidCode);

        // Each file within the scan cap, together over the recording's.
        var heavy = Enumerable.Range(0, 6)
            .SelectMany(i => new[] { File($"lidar{i:D4}.laz", ScanCap), File($"imu{i:D4}.csv", 10) })
            .ToArray();
        await ShouldBeProblemAsync(await Open(owner, caveId, heavy), HttpStatusCode.BadRequest, SurveyUploadFiles.InvalidCode);

        // More files than the recording may declare.
        var many = Enumerable.Range(0, 7)
            .SelectMany(i => new[] { File($"lidar{i:D4}.laz", 10), File($"imu{i:D4}.csv", 10) })
            .ToArray();
        await ShouldBeProblemAsync(await Open(owner, caveId, many), HttpStatusCode.BadRequest, SurveyUploadFiles.InvalidCode);

        // A lidar chunk that is not a LAS file is refused when it arrives.
        var opened = await Open(owner, caveId, File("lidar0000.laz", 16), File("imu0000.csv", 16));
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync());
        var id = (await opened.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await ShouldBeProblemAsync(
            await PutAsync(owner, id, "lidar0000.laz", Encoding.ASCII.GetBytes("not a point cloud")[..16]),
            HttpStatusCode.BadRequest, "survey_upload.content_mismatch");
    }

    [Fact]
    public async Task Without_an_account_or_the_right_to_write_the_cave_a_recording_is_refused()
    {
        var caveId = await CreateCaveAsync("authenticated", locationProtected: false);
        var body = OpenBody("Scan", caveId, File("lidar0000.laz", 10), File("imu0000.csv", 10));

        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/v1/survey-uploads", body)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await reader.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.PostAsJsonAsync("/api/v1/survey-uploads", body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var privateId = await CreateCaveAsync("private", locationProtected: false);
        await ShouldBeProblemAsync(
            await reader.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody("Scan", privateId, File("lidar0000.laz", 10), File("imu0000.csv", 10))),
            HttpStatusCode.NotFound, "cave.not_found");
    }

    [Fact]
    public async Task A_recording_with_gnss_is_a_location_and_its_files_need_the_exact_location()
    {
        // A protected cave the reader may read, write and place exactly: they send a recording
        // whose GNSS log heard a fix.
        var caveId = await CreateCaveAsync("authenticated", locationProtected: true);
        await GrantAsync(caveId, "read, write, viewExactLocation");
        var id = await SendAsync(reader, "Entrance scan", caveId, Recording(gnss: true));
        await RunPipelineAsync(id);

        // With the exact position, the recording is described as georeferenced and its files offered.
        var granted = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        granted.GetProperty("status").GetString().ShouldBe("succeeded");
        granted.GetProperty("pointCloud").GetProperty("georeferenced").GetBoolean().ShouldBeTrue();
        granted.GetProperty("pointCloud").GetProperty("min").GetArrayLength().ShouldBe(3);
        granted.GetProperty("files").EnumerateArray()
            .ShouldAllBe(f => f.GetProperty("contentUrl").ValueKind == JsonValueKind.String);

        // Then the exact position is taken from them: the upload stays theirs to read, but no
        // file of the recording is offered, nor where its extent lies.
        await GrantAsync(caveId, "read, write");
        var withheld = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        withheld.GetProperty("status").GetString().ShouldBe("succeeded");
        withheld.GetProperty("pointCloud").GetProperty("georeferenced").GetBoolean().ShouldBeTrue();
        withheld.GetProperty("pointCloud").GetProperty("min").ValueKind.ShouldBe(JsonValueKind.Null);
        withheld.GetProperty("pointCloud").GetProperty("sizeM").GetArrayLength().ShouldBe(3);
        withheld.GetProperty("files").EnumerateArray()
            .ShouldAllBe(f => f.GetProperty("contentUrl").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task A_recording_without_gnss_offers_its_files_without_the_exact_location()
    {
        var caveId = await CreateCaveAsync("authenticated", locationProtected: true);
        await GrantAsync(caveId, "read, write, viewExactLocation");
        var id = await SendAsync(reader, "Interior scan", caveId, Recording(gnss: false));
        await RunPipelineAsync(id);
        await GrantAsync(caveId, "read, write");

        var shown = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        shown.GetProperty("pointCloud").GetProperty("georeferenced").GetBoolean().ShouldBeFalse();
        shown.GetProperty("files").EnumerateArray()
            .ShouldAllBe(f => f.GetProperty("contentUrl").ValueKind == JsonValueKind.String);
    }

    [Fact]
    public async Task An_imu_log_without_the_expected_columns_fails_the_run_at_the_inspect_stage()
    {
        var caveId = await CreateCaveAsync("authenticated", locationProtected: false);
        var recording = Recording(gnss: false)
            .Select(f => f.Name == "imu0001.csv" ? (f.Name, Encoding.ASCII.GetBytes("time a b c\n1 2 3 4\n")) : f)
            .ToList();
        var id = await SendAsync(owner, "Broken scan", caveId, recording);
        await Should.ThrowAsync<InvalidOperationException>(RunPipelineAsync(id));

        var shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        shown.GetProperty("status").GetString().ShouldBe("failed");
        shown.GetProperty("stage").GetString().ShouldBe("inspect");
        shown.GetProperty("error").GetProperty("code").GetString().ShouldBe(SurveyPipelineFailures.RecordingInvalid);
        shown.GetProperty("error").GetProperty("message").GetString()!.ShouldContain("IMU");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.SurveyModels.AsNoTracking().AnyAsync(m => m.SurveyUploadId == id)).ShouldBeFalse();
    }

    /// <summary>
    /// An invented two-chunk recording: two LAZ headers (2 and 3 points), IMU logs with a header
    /// spanning 2.5 seconds together, the serial numbers of two invented sensors, and a GNSS log
    /// of one invented sentence when asked for.
    /// </summary>
    private static List<(string Name, byte[] Bytes)> Recording(bool gnss)
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
            files.Add(("gnss0000.gnss", Encoding.ASCII.GetBytes("1000000000 $GPGGA,000000.00,0000.0000,N,00000.0000,E,1,08,1.0,0.0,M,0.0,M,,*00\n")));
        }

        return files;
    }

    private static object File(string name, long size, string role = "archive") => new { name, size, role };

    private static object OpenBody(string title, Guid caveId, params object[] files) => new
    {
        source = "mandeye",
        cave = new { id = caveId },
        client = new { name = "silexgis tests", version = "1" },
        surveyTitle = title,
        files,
    };

    private static Task<HttpResponseMessage> Open(HttpClient client, Guid caveId, params object[] files) =>
        client.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody("Scan", caveId, files));

    /// <summary>Opens a recording upload of these files, sends each, and completes it.</summary>
    private static async Task<Guid> SendAsync(HttpClient client, string title, Guid caveId, IReadOnlyList<(string Name, byte[] Bytes)> recording)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/survey-uploads", OpenBody(title, caveId, [.. recording.Select(f => File(f.Name, f.Bytes.LongLength))]));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        foreach (var (name, bytes) in recording)
        {
            var put = await PutAsync(client, id, name, bytes);
            put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        }

        var complete = await client.PostAsync($"/api/v1/survey-uploads/{id}/complete", null);
        complete.StatusCode.ShouldBe(HttpStatusCode.Accepted, await complete.Content.ReadAsStringAsync());
        return id;
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, string name, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return client.PutAsync($"/api/v1/survey-uploads/{id}/files/{Uri.EscapeDataString(name)}", content);
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

    private static async Task<JsonElement> ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, text);
        var body = JsonDocument.Parse(text).RootElement;
        body.GetProperty("code").GetString().ShouldBe(code, text);
        return body;
    }

    private async Task<Guid> CreateCaveAsync(string visibility, bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Scan Cave {Guid.NewGuid():N}"[..28],
            caveTypeId,
            visibility,
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
