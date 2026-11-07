// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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

namespace SilexGis.Api.Tests;

/// <summary>
/// A point cloud sent as a survey upload: one LAS or LAZ file under the scans' own cap, archived
/// as it arrives, its header read by the run, the scan listed on the cave with what the header
/// said, and the original downloadable under the survey-file rule — plus the exact-location right
/// when the header names a coordinate system.
/// </summary>
/// <remarks>
/// Every cloud here is written by the test: a LAS header with a handful of zero points and, for
/// the georeferenced one, a GeoTIFF key naming a projected system. Nothing here is a real scan.
/// </remarks>
public sealed class SurveyUploadPointCloudTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const long ScanCap = 4000;

    private readonly PostgresFixture postgres;
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private Guid readerId;
    private long caveTypeId;

    public SurveyUploadPointCloudTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-supc-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            // Small enough for a test to go over it with a few kilobytes.
            ["Scans:MaxUploadBytes"] = ScanCap.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }, JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"supc-own-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"supc-read-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"supc-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"supc-read-{suffix}@t.local");

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
    public async Task A_point_cloud_is_archived_inspected_and_listed_with_its_facts_and_waits_for_a_tiler()
    {
        var caveId = await CreateCaveAsync("authenticated", locationProtected: false);
        var cloud = Las(points: 3, compressed: true, size: (12.5, 4, 2.25));
        var id = await SendAsync(owner, "Sala Mare scan", caveId, ("sala-mare.laz", cloud));
        await RunPipelineAsync(id);

        // The run read the header and stopped where tiling would start, saying so the way a
        // survey without a compiler says so; nothing failed.
        var shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        shown.GetProperty("status").GetString().ShouldBe("succeeded");
        shown.GetProperty("stage").GetString().ShouldBe("tile");
        shown.GetProperty("progress").GetInt32().ShouldBe(100);
        shown.GetProperty("message").GetString().ShouldBe(SurveyPipelineNotes.NoTiler);
        shown.GetProperty("error").ValueKind.ShouldBe(JsonValueKind.Null);
        shown.GetProperty("compile").GetBoolean().ShouldBeFalse();

        var facts = shown.GetProperty("pointCloud");
        facts.GetProperty("pointCount").GetInt64().ShouldBe(3);
        facts.GetProperty("lasVersion").GetString().ShouldBe("1.2");
        facts.GetProperty("compressed").GetBoolean().ShouldBeTrue();
        facts.GetProperty("georeferenced").GetBoolean().ShouldBeFalse();
        facts.GetProperty("crs").ValueKind.ShouldBe(JsonValueKind.Null);
        facts.GetProperty("sizeM").EnumerateArray().Select(v => v.GetDouble()).ToArray().ShouldBe(new[] { 12.5, 4, 2.25 });
        facts.GetProperty("min").EnumerateArray().Select(v => v.GetDouble()).ToArray().ShouldBe(new[] { 0d, 0, 0 });
        facts.GetProperty("tileBytes").ValueKind.ShouldBe(JsonValueKind.Null);
        var modelId = facts.GetProperty("modelId").GetGuid();

        // The file is archived as a survey source and the uploader may take it.
        var file = shown.GetProperty("files").EnumerateArray().Single();
        file.GetProperty("role").GetString().ShouldBe("archive");
        file.GetProperty("sourceId").ValueKind.ShouldBe(JsonValueKind.String);
        var bytes = await owner.GetByteArrayAsync(file.GetProperty("contentUrl").GetString());
        bytes.ShouldBe(cloud);

        // The cave lists the scan with its facts.
        var listed = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads?caveId={caveId}");
        listed.GetProperty("total").GetInt32().ShouldBe(1);
        var item = listed.GetProperty("items")[0];
        item.GetProperty("source").GetString().ShouldBe("pointcloud");
        item.GetProperty("pointCloud").GetProperty("pointCount").GetInt64().ShouldBe(3);
        item.GetProperty("archive").GetProperty("name").GetString().ShouldBe("sala-mare.laz");

        // Underneath: one model of the point-cloud kind, current, with the facts; one artefact row
        // for the original; and a rerun refreshes both rather than adding a second of each.
        await ShouldHoldOneModelAndArtefactAsync(id, modelId, SurveyModelFormat.Laz, cloud);
        (await owner.PostAsync($"/api/v1/survey-uploads/{id}/rerun", null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await RunPipelineAsync(id);
        await ShouldHoldOneModelAndArtefactAsync(id, modelId, SurveyModelFormat.Laz, cloud);
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}")).GetProperty("status").GetString().ShouldBe("succeeded");
    }

    [Fact]
    public async Task The_declared_files_are_checked_against_the_point_cloud_rules_and_the_scans_cap()
    {
        var caveId = await CreateCaveAsync("authenticated", locationProtected: false);

        await ShouldBeProblemAsync(await Open(owner, caveId, File("scan.zip", 10)), HttpStatusCode.BadRequest, SurveyUploadFiles.InvalidCode);
        await ShouldBeProblemAsync(await Open(owner, caveId, File("scan.laz", 10), File("notes.txt", 10)), HttpStatusCode.BadRequest, SurveyUploadFiles.InvalidCode);
        await ShouldBeProblemAsync(await Open(owner, caveId, File("scan.laz", ScanCap + 1)), HttpStatusCode.BadRequest, SurveyUploadFiles.InvalidCode);
        await ShouldBeProblemAsync(await Open(owner, caveId, File("scan.laz", 10, role: "other")), HttpStatusCode.BadRequest, SurveyUploadFiles.InvalidCode);
        await ShouldBeProblemAsync(await Open(owner, caveId, source: "lidar", File("scan.laz", 10)), HttpStatusCode.BadRequest, "survey_upload.source_unsupported");

        // The survey tool's cap is not the scan's: a cloud at the scan cap is accepted, and the
        // capabilities say so apart from the survey tool's own figures.
        (await Open(owner, caveId, File("scan.laz", ScanCap))).StatusCode.ShouldBe(HttpStatusCode.Created);
        var capabilities = await owner.GetFromJsonAsync<JsonElement>("/api/v1/survey-uploads/capabilities");
        capabilities.GetProperty("sources").EnumerateArray().Select(s => s.GetString()).ShouldContain("pointcloud");
        capabilities.GetProperty("maxFileBytes").GetInt64().ShouldNotBe(ScanCap);
        var scans = capabilities.GetProperty("pointCloud");
        scans.GetProperty("maxFileBytes").GetInt64().ShouldBe(ScanCap);
        scans.GetProperty("maxFilesPerUpload").GetInt32().ShouldBe(1);
        scans.GetProperty("acceptedExtensions").EnumerateArray().Select(e => e.GetString()).ToArray().ShouldBe([".laz", ".las"]);

        // And a file that claims to be a point cloud but is not one is refused when it arrives.
        var opened = await Open(owner, caveId, File("not-a-cloud.las", 16));
        var id = (await opened.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await ShouldBeProblemAsync(
            await PutAsync(owner, id, "not-a-cloud.las", Encoding.ASCII.GetBytes("survey 1 -title\n")),
            HttpStatusCode.BadRequest, "survey_upload.content_mismatch");
    }

    [Fact]
    public async Task Without_an_account_or_the_right_to_write_the_cave_a_point_cloud_is_refused()
    {
        var caveId = await CreateCaveAsync("authenticated", locationProtected: false);
        var body = OpenBody("Scan", caveId, "pointcloud", File("scan.laz", 10));

        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/v1/survey-uploads", body)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The reader sees the cave and may not write it: refused, and told so.
        (await reader.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.PostAsJsonAsync("/api/v1/survey-uploads", body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A cave the reader cannot see is not told apart from one that does not exist.
        var privateId = await CreateCaveAsync("private", locationProtected: false);
        await ShouldBeProblemAsync(
            await reader.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody("Scan", privateId, "pointcloud", File("scan.laz", 10))),
            HttpStatusCode.NotFound, "cave.not_found");
    }

    [Fact]
    public async Task A_scan_is_listed_only_to_those_who_may_see_the_cave()
    {
        var caveId = await CreateCaveAsync("private", locationProtected: false);
        var id = await SendAsync(owner, "Private scan", caveId, ("scan.las", Las(points: 2)));
        await RunPipelineAsync(id);

        // No grant: no row, no count, not found by id — the same as if there were nothing.
        var hidden = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads?caveId={caveId}");
        hidden.GetProperty("total").GetInt32().ShouldBe(0);
        hidden.GetProperty("items").GetArrayLength().ShouldBe(0);
        await ShouldBeProblemAsync(await reader.GetAsync($"/api/v1/survey-uploads/{id}"), HttpStatusCode.NotFound, "survey_upload.not_found");

        // Granted the cave, the same reader sees the scan and its facts.
        await GrantAsync(caveId, "read, viewExactLocation");
        var shown = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads?caveId={caveId}");
        shown.GetProperty("total").GetInt32().ShouldBe(1);
        shown.GetProperty("items")[0].GetProperty("pointCloud").GetProperty("pointCount").GetInt64().ShouldBe(2);
        (await reader.GetAsync($"/api/v1/survey-uploads/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_georeferenced_original_is_taken_only_with_the_exact_location_and_a_local_one_without_it()
    {
        // A protected cave the reader may read, write and place exactly: they send two clouds, one
        // whose header names a coordinate system and one whose header names none.
        var caveId = await CreateCaveAsync("authenticated", locationProtected: true);
        await GrantAsync(caveId, "read, write, viewExactLocation");
        var placed = Las(points: 2, epsg: 3844);
        var local = Las(points: 2);
        var placedId = await SendAsync(reader, "Placed scan", caveId, ("placed.las", placed));
        var localId = await SendAsync(reader, "Local scan", caveId, ("local.las", local));
        await RunPipelineAsync(placedId);
        await RunPipelineAsync(localId);

        // Then the exact position is taken from them. Their own uploads stay readable, but the
        // placed cloud's original — a position in itself — is no longer offered, nor is where
        // it lies or the system it lies in; the local cloud's original still is.
        await GrantAsync(caveId, "read, write");
        var withheld = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{placedId}");
        withheld.GetProperty("status").GetString().ShouldBe("succeeded");
        var facts = withheld.GetProperty("pointCloud");
        facts.GetProperty("georeferenced").GetBoolean().ShouldBeTrue();
        facts.GetProperty("crs").ValueKind.ShouldBe(JsonValueKind.Null);
        facts.GetProperty("min").ValueKind.ShouldBe(JsonValueKind.Null);
        facts.GetProperty("sizeM").GetArrayLength().ShouldBe(3);
        withheld.GetProperty("files")[0].GetProperty("contentUrl").ValueKind.ShouldBe(JsonValueKind.Null);

        var offered = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{localId}");
        offered.GetProperty("pointCloud").GetProperty("georeferenced").GetBoolean().ShouldBeFalse();
        var localUrl = offered.GetProperty("files")[0].GetProperty("contentUrl").GetString();
        localUrl.ShouldNotBeNull();
        (await reader.GetByteArrayAsync(localUrl)).ShouldBe(local);

        // With the exact position back, the placed original is offered, with its system.
        await GrantAsync(caveId, "read, write, viewExactLocation");
        var granted = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{placedId}");
        granted.GetProperty("pointCloud").GetProperty("crs").GetProperty("horizontalEpsg").GetInt32().ShouldBe(3844);
        granted.GetProperty("pointCloud").GetProperty("min").GetArrayLength().ShouldBe(3);
        var placedUrl = granted.GetProperty("files")[0].GetProperty("contentUrl").GetString();
        placedUrl.ShouldNotBeNull();
        (await reader.GetByteArrayAsync(placedUrl)).ShouldBe(placed);
    }

    [Fact]
    public async Task A_file_the_header_reader_refuses_fails_the_run_at_the_inspect_stage()
    {
        // Begins with the signature, so it is taken when it arrives; says it is LAS 2.2 (the
        // synthetic file's minor version under a major nobody has published), which the reader
        // does not read, so the run refuses it — at the stage that read it, naming the version.
        var caveId = await CreateCaveAsync("authenticated", locationProtected: false);
        var id = await SendAsync(owner, "Future scan", caveId, ("future.las", Las(points: 1, major: 2)));
        await Should.ThrowAsync<InvalidOperationException>(RunPipelineAsync(id));

        var shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        shown.GetProperty("status").GetString().ShouldBe("failed");
        shown.GetProperty("stage").GetString().ShouldBe("inspect");
        shown.GetProperty("error").GetProperty("code").GetString().ShouldBe(SurveyPipelineFailures.PointCloudInvalid);
        shown.GetProperty("error").GetProperty("message").GetString()!.ShouldContain("2.2");
        shown.GetProperty("pointCloud").ValueKind.ShouldBe(JsonValueKind.Null);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.SurveyModels.AsNoTracking().AnyAsync(m => m.SurveyUploadId == id)).ShouldBeFalse();
        (await db.ScanArtifacts.AsNoTracking().AnyAsync(a => a.SurveyUploadId == id)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_point_cloud_is_not_read_again_and_the_refusal_says_so()
    {
        var caveId = await CreateCaveAsync("authenticated", locationProtected: false);
        var id = await SendAsync(owner, "Scan", caveId, ("scan.laz", Las(points: 2, compressed: true)));
        await RunPipelineAsync(id);
        var shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        var modelId = shown.GetProperty("pointCloud").GetProperty("modelId").GetGuid();

        // The model is listed with the cave's others and is ready, so the request is well formed;
        // it is refused with a code rather than failing, and the model is left as it was.
        await ShouldBeProblemAsync(
            await owner.PostAsync($"/api/v1/survey-models/{modelId}/reading", null), HttpStatusCode.Conflict, "survey_model.no_reading");
        var model = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-models/{modelId}");
        model.GetProperty("status").GetString().ShouldBe("ready");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.ProcessingJobs.AsNoTracking().CountAsync(j =>
            j.Kind == ProcessingJobKinds.SurveyGraph || j.Kind == ProcessingJobKinds.SurveyMesh)).ShouldBe(0);
    }

    [Fact]
    public async Task The_archived_original_stays_in_use_while_its_artefact_row_stands()
    {
        var caveId = await CreateCaveAsync("authenticated", locationProtected: false);
        var cloud = Las(points: 2);
        var id = await SendAsync(owner, "Scan", caveId, ("scan.las", cloud));
        await RunPipelineAsync(id);
        var shown = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        var modelId = shown.GetProperty("pointCloud").GetProperty("modelId").GetGuid();
        var file = shown.GetProperty("files").EnumerateArray().Single();
        var documentId = file.GetProperty("documentId").GetGuid();
        var originalFileId = FileIdOf(file.GetProperty("contentUrl").GetString()!);

        // The model goes; the archived file and the artefact row describing it stay.
        (await owner.DeleteAsync($"/api/v1/survey-models/{modelId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var artefact = await db.ScanArtifacts.AsNoTracking().SingleAsync(a => a.SurveyUploadId == id);
            artefact.SurveyModelId.ShouldBeNull();
            artefact.FileId.ShouldBe(originalFileId);
        }

        // A corrected file is a new revision of the same document; the inspected revision is
        // then no longer current, and deleting it is refused as in use rather than failing.
        using var form = new MultipartFormDataContent { { new ByteArrayContent(Las(points: 3)), "file", "scan.las" } };
        var revised = await owner.PostAsync($"/api/v1/files/{originalFileId}/versions", form);
        revised.StatusCode.ShouldBe(HttpStatusCode.Created, await revised.Content.ReadAsStringAsync());
        await ShouldBeProblemAsync(
            await owner.DeleteAsync($"/api/v1/files/{originalFileId}"), HttpStatusCode.Conflict, "file.version_in_use");
        await ShouldBeProblemAsync(
            await owner.DeleteAsync($"/api/v1/documents/{documentId}"), HttpStatusCode.Conflict, "document.in_use");
        (await owner.GetAsync($"/api/v1/files/{originalFileId}/versions")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_revision_of_the_original_is_not_offered_without_the_exact_position()
    {
        // The reader may place the cave exactly while sending a local cloud; a georeferenced
        // revision of the same document is uploaded afterwards.
        var caveId = await CreateCaveAsync("authenticated", locationProtected: true);
        await GrantAsync(caveId, "read, write, viewExactLocation");
        var local = Las(points: 2);
        var id = await SendAsync(reader, "Local scan", caveId, ("local.las", local));
        await RunPipelineAsync(id);
        var before = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        var originalFileId = FileIdOf(before.GetProperty("files")[0].GetProperty("contentUrl").GetString()!);

        var placed = Las(points: 2, epsg: 3844);
        using var form = new MultipartFormDataContent { { new ByteArrayContent(placed), "file", "local.las" } };
        var revised = await reader.PostAsync($"/api/v1/files/{originalFileId}/versions", form);
        revised.StatusCode.ShouldBe(HttpStatusCode.Created, await revised.Content.ReadAsStringAsync());

        // The facts still describe the local file the run read, and without the exact position
        // the link is withheld: what the document now serves is not the file the facts vouch for.
        await GrantAsync(caveId, "read, write");
        var withheld = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        withheld.GetProperty("pointCloud").GetProperty("georeferenced").GetBoolean().ShouldBeFalse();
        withheld.GetProperty("files")[0].GetProperty("contentUrl").ValueKind.ShouldBe(JsonValueKind.Null);

        // With the exact position the current revision is offered, as every file of the upload is.
        await GrantAsync(caveId, "read, write, viewExactLocation");
        var offered = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        var url = offered.GetProperty("files")[0].GetProperty("contentUrl").GetString();
        url.ShouldNotBeNull();
        (await reader.GetByteArrayAsync(url)).ShouldBe(placed);

        // A rerun reads the bytes the upload received, by their digest, not whatever the document
        // currently serves: the facts still describe the local file, and without the exact
        // position the current revision stays withheld, since it is not the file they vouch for.
        (await reader.PostAsync($"/api/v1/survey-uploads/{id}/rerun", null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await RunPipelineAsync(id);
        var reread = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        reread.GetProperty("status").GetString().ShouldBe("succeeded");
        reread.GetProperty("pointCloud").GetProperty("georeferenced").GetBoolean().ShouldBeFalse();
        await GrantAsync(caveId, "read, write");
        var still = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        still.GetProperty("files")[0].GetProperty("contentUrl").ValueKind.ShouldBe(JsonValueKind.Null);
        // Nor do the file routes hand the revision out — neither by its own id nor in the
        // document's history, whose every row carries a delivery address: a file of a scan's
        // document the run has not read is taken to be a position until a run says otherwise.
        Guid revisionFileId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            revisionFileId = await (
                from version in db.DocumentVersions.AsNoTracking()
                join file in db.StoredFiles.AsNoTracking() on version.Id equals file.DocumentVersionId
                where version.DocumentId == before.GetProperty("files")[0].GetProperty("documentId").GetGuid() && version.IsCurrent
                select file.Id).SingleAsync();
        }

        revisionFileId.ShouldNotBe(originalFileId);
        (await reader.GetAsync($"/api/v1/files/{revisionFileId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/files/{originalFileId}/versions")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await GrantAsync(caveId, "read, write, viewExactLocation");
        (await reader.GetAsync($"/api/v1/files/{revisionFileId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/files/{originalFileId}/versions")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- helpers ----

    /// <summary>The stored file a signed content address serves.</summary>
    private static Guid FileIdOf(string contentUrl) =>
        Guid.Parse(contentUrl["/api/v1/files/".Length..contentUrl.IndexOf("/content", StringComparison.Ordinal)]);

    private async Task ShouldHoldOneModelAndArtefactAsync(Guid uploadId, Guid modelId, SurveyModelFormat format, byte[] cloud)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.AsNoTracking().SingleAsync(m => m.SurveyUploadId == uploadId);
        model.Id.ShouldBe(modelId);
        model.Format.ShouldBe(format);
        model.IsCurrent.ShouldBeTrue();
        model.Status.ShouldBe(SurveyModelStatus.Ready);
        PointCloudFacts.FromJson(model.PointCloud).ShouldNotBeNull().PointCount.ShouldBe(3);

        var artefact = await db.ScanArtifacts.AsNoTracking().SingleAsync(a => a.SurveyUploadId == uploadId);
        artefact.SurveyModelId.ShouldBe(modelId);
        artefact.Kind.ShouldBe(ScanArtifactKind.PointCloudLaz);
        artefact.Format.ShouldBe("laz");
        artefact.StepId.ShouldBe(ScanSteps.UploadStepId);
        artefact.SizeBytes.ShouldBe(cloud.LongLength);
        artefact.Sha256.ShouldBe(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(cloud)));
        artefact.FileId.ShouldBe(model.FileId);
        (await db.SurveyUploadFiles.AsNoTracking().SingleAsync(f => f.SurveyUploadId == uploadId)).SurveyModelId.ShouldBe(modelId);
    }

    private static object File(string name, long size, string role = "archive") => new { name, size, role };

    private static object OpenBody(string title, Guid caveId, string source, params object[] files) => new
    {
        source,
        cave = new { id = caveId },
        client = new { name = "silexgis tests", version = "1" },
        surveyTitle = title,
        files,
    };

    private static Task<HttpResponseMessage> Open(HttpClient client, Guid caveId, params object[] files) =>
        Open(client, caveId, "pointcloud", files);

    private static Task<HttpResponseMessage> Open(HttpClient client, Guid caveId, string source, params object[] files) =>
        client.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody("Scan", caveId, source, files));

    /// <summary>Opens a point-cloud upload of this one file, sends it, and completes it.</summary>
    private static async Task<Guid> SendAsync(HttpClient client, string title, Guid caveId, (string Name, byte[] Bytes) file)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/survey-uploads", OpenBody(title, caveId, "pointcloud", File(file.Name, file.Bytes.LongLength)));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var put = await PutAsync(client, id, file.Name, file.Bytes);
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
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

    private static byte[] Las(int points, bool compressed = false, (double X, double Y, double Z)? size = null, int? epsg = null, byte major = 1) =>
        SyntheticLas.Las(points, compressed, size, epsg, major);
}
