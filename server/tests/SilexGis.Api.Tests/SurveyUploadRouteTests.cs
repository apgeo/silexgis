// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The survey-upload routes end to end, as a field device drives them with its access key: open,
/// send each file, complete, watch — and who may do each, on the cave's survey terms with the
/// exact location included.
/// </summary>
/// <remarks>
/// Every survey here is invented: a two-entry archive written by the test, a three-line Therion
/// source, an SVG of nothing.
/// </remarks>
public sealed class SurveyUploadRouteTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture postgres;
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private Guid ownerId;
    private Guid readerId;
    private long caveTypeId;

    public SurveyUploadRouteTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-supl-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, Settings(filesRoot), JobWorkers.RemoveFrom);
    }

    private static Dictionary<string, string?> Settings(string root, bool caveCreation = true) => new()
    {
        ["Files:Root"] = root,
        ["Keys:Path"] = Path.Combine(root, "keys"),
        ["SurveyUploads:CaveCreation"] = caveCreation ? "true" : "false",
    };

    public async ValueTask InitializeAsync()
    {
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"supl-own-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"supl-read-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"supl-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"supl-read-{suffix}@t.local");

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
    public async Task A_device_opens_an_upload_sends_its_files_and_completes_it_with_its_access_key()
    {
        using var device = AuthHelper.AccessKeyClient(factory, await AuthHelper.CreateAccessKeyAsync(owner));
        device.DefaultRequestHeaders.Add("X-SilexGis-Contract", "1");

        var capabilities = await device.GetFromJsonAsync<JsonElement>("/api/v1/survey-uploads/capabilities");
        capabilities.GetProperty("contractVersion").GetInt32().ShouldBe(1);
        capabilities.GetProperty("maxFileBytes").GetInt64().ShouldBe(262_144_000);
        capabilities.GetProperty("compiler").GetProperty("available").GetBoolean().ShouldBeFalse();
        capabilities.GetProperty("caveCreation").GetProperty("allowed").GetBoolean().ShouldBeTrue();
        capabilities.GetProperty("caveCreation").GetProperty("defaultCaveTypeId").ValueKind.ShouldBe(JsonValueKind.Number);
        capabilities.GetProperty("acceptedExtensions").EnumerateArray().Select(e => e.GetString()).ShouldContain(".th2");

        // No cave named: one is made from the survey's title, on the uploader's own right.
        var title = $"Pestera Inventata {suffix}";
        var archive = Archive();
        var source = TherionSource();
        var drawing = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        var opened = await device.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody(
            title, $"inventata-{suffix}",
            files: [File("inventata.zip", archive, "archive"), File("inventata.th", source, "therionSource"), File("plan.svg", drawing, "other")]));
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync());
        var upload = await opened.Content.ReadFromJsonAsync<JsonElement>();
        var id = upload.GetProperty("id").GetGuid();
        var caveId = upload.GetProperty("cave").GetProperty("id").GetGuid();
        var surveyId = upload.GetProperty("caveSurvey").GetProperty("id").GetGuid();
        upload.GetProperty("status").GetString().ShouldBe("receiving");
        upload.GetProperty("caveCreated").GetBoolean().ShouldBeTrue();
        upload.GetProperty("cave").GetProperty("name").GetString().ShouldBe(title);
        upload.GetProperty("caveSurvey").GetProperty("title").GetString().ShouldBe(title);
        upload.GetProperty("statusPageUrl").GetString().ShouldBe($"/caves/{caveId}/surveys/{surveyId}/uploads/{id}");
        upload.GetProperty("publicLink").ValueKind.ShouldBe(JsonValueKind.Null);
        upload.GetProperty("outputs").EnumerateArray().Select(e => e.GetString())
            .ShouldBe(["lox", "3d", "plan-pdf", "extended-pdf"]);
        upload.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("status").GetString())
            .ShouldAllBe(s => s == "declared");

        // Completing before the files are in names every one still missing.
        var early = await device.PostAsync($"/api/v1/survey-uploads/{id}/complete", null);
        await ShouldBeProblemAsync(early, HttpStatusCode.BadRequest, "survey_upload.files_missing");

        var put = await PutAsync(device, id, "inventata.zip", archive);
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        var received = await put.Content.ReadFromJsonAsync<JsonElement>();
        received.GetProperty("status").GetString().ShouldBe("stored");
        received.GetProperty("sha256").GetString().ShouldBe(Convert.ToHexStringLower(SHA256.HashData(archive)));
        var archiveSourceId = received.GetProperty("sourceId").GetGuid();

        // A retry after a lost answer gets the first answer, and archives nothing twice.
        var repeat = await (await PutAsync(device, id, "inventata.zip", archive)).Content.ReadFromJsonAsync<JsonElement>();
        repeat.GetProperty("sourceId").GetGuid().ShouldBe(archiveSourceId);

        (await PutAsync(device, id, "inventata.th", source)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var attached = await (await PutAsync(device, id, "plan.svg", drawing)).Content.ReadFromJsonAsync<JsonElement>();
        attached.GetProperty("sourceId").ValueKind.ShouldBe(JsonValueKind.Null);
        var drawingDocumentId = attached.GetProperty("documentId").GetGuid();

        var completed = await device.PostAsync($"/api/v1/survey-uploads/{id}/complete", null);
        completed.StatusCode.ShouldBe(HttpStatusCode.Accepted, await completed.Content.ReadAsStringAsync());
        (await completed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().ShouldBe("queued");

        // A repeated completion is answered with where the upload stands.
        (await device.PostAsync($"/api/v1/survey-uploads/{id}/complete", null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var status = await device.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        status.GetProperty("files").EnumerateArray().ShouldAllBe(f => f.GetProperty("status").GetString() == "stored");
        var archiveUrl = status.GetProperty("files").EnumerateArray()
            .Single(f => f.GetProperty("name").GetString() == "inventata.zip").GetProperty("contentUrl").GetString()!;
        using (var anonymous = factory.CreateClient())
        {
            (await anonymous.GetByteArrayAsync(archiveUrl)).ShouldBe(archive);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var row = await db.SurveyUploads.AsNoTracking().SingleAsync(u => u.Id == id);
            row.RequestedBy.ShouldBe(ownerId);

            // The two survey files are sources of the cave, of this survey and this upload; the
            // drawing is a document attached to the cave. All three carry the upload's batch.
            var sources = await db.SurveySources.AsNoTracking().Where(s => s.CaveFeatureId == caveId).ToListAsync();
            sources.Select(s => s.Kind).Order().ShouldBe([SurveySourceKind.TherionSource, SurveySourceKind.TopoDroidArchive]);
            sources.ShouldAllBe(s => s.CaveSurveyId == surveyId && s.SurveyUploadId == id);
            (await db.Documents.AsNoTracking().CountAsync(d => d.UploadBatchId == row.UploadBatchId)).ShouldBe(3);
            var drawingFile = await db.DocumentVersions.AsNoTracking()
                .Where(v => v.DocumentId == drawingDocumentId)
                .Join(db.StoredFiles, v => v.Id, f => f.DocumentVersionId, (v, f) => f.Id)
                .FirstAsync();
            (await db.Attachments.AsNoTracking().AnyAsync(a => a.FeatureId == caveId && a.FileId == drawingFile)).ShouldBeTrue();

            var batch = await db.UploadBatches.AsNoTracking().SingleAsync(b => b.Id == row.UploadBatchId);
            batch.Source.ShouldBe(UploadSource.SurveyUpload);
            batch.Status.ShouldBe(UploadBatchStatus.Completed);
            batch.StoredCount.ShouldBe(3);

            // One run queued, with no requester: the generic job notice has nothing to say about a survey.
            var jobs = (await db.ProcessingJobs.AsNoTracking()
                    .Where(j => j.Kind == ProcessingJobKinds.SurveyUploadPipeline)
                    .ToListAsync())
                .Where(j => JsonDocument.Parse(j.Payload).RootElement.GetProperty("surveyUploadId").GetGuid() == id)
                .ToList();
            jobs.Count.ShouldBe(1);
            jobs[0].RequestedBy.ShouldBeNull();
        }

        // Once complete it is no longer receiving, but an archived file's repeat still answers.
        (await PutAsync(device, id, "inventata.th", source)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ShouldBeProblemAsync(
            await device.DeleteAsync($"/api/v1/survey-uploads/{id}"), HttpStatusCode.Conflict, "survey_upload.not_receiving");
        await ShouldBeProblemAsync(
            await device.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", new { }), HttpStatusCode.Conflict,
            "survey_upload.run_in_progress");

        // The same survey sent again, to the cave by id, is its next version; by the survey's id, the one after.
        var again = await device.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody(
            title, $"inventata-{suffix}", cave: new { id = caveId }, files: [File("inventata.zip", archive, "archive")]));
        again.StatusCode.ShouldBe(HttpStatusCode.Created, await again.Content.ReadAsStringAsync());
        var second = await again.Content.ReadFromJsonAsync<JsonElement>();
        second.GetProperty("caveSurvey").GetProperty("id").GetGuid().ShouldBe(surveyId);
        second.GetProperty("caveCreated").GetBoolean().ShouldBeFalse();
        second.GetProperty("version").GetDateTimeOffset().ShouldBeGreaterThan(upload.GetProperty("version").GetDateTimeOffset());

        var third = await (await device.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody(
            "Another title", "another-name", caveSurvey: new { id = surveyId }, files: [File("inventata.zip", archive, "archive")])))
            .Content.ReadFromJsonAsync<JsonElement>();
        third.GetProperty("caveSurvey").GetProperty("id").GetGuid().ShouldBe(surveyId);

        var mine = await device.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads?caveId={caveId}");
        mine.GetProperty("total").GetInt32().ShouldBe(3);
        var listed = mine.GetProperty("items").EnumerateArray().Last();
        listed.GetProperty("id").GetGuid().ShouldBe(id);
        listed.GetProperty("archive").GetProperty("name").GetString().ShouldBe("inventata.zip");
        listed.GetProperty("therionSources").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Every_refusal_of_the_open_and_the_send_has_its_code()
    {
        var caveId = await CreateCaveAsync("authenticated", locationProtected: false);
        var archive = Archive();

        await ShouldBeProblemAsync(
            await owner.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody("T", "t", source: "compass", files: [File("a.zip", archive, "archive")])),
            HttpStatusCode.BadRequest, "survey_upload.source_unsupported");
        await ShouldBeProblemAsync(
            await owner.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody(
                "T", "t", cave: new { id = caveId }, newCave: new { name = "N" }, files: [File("a.zip", archive, "archive")])),
            HttpStatusCode.BadRequest, "survey_upload.cave_ambiguous");
        var twoArchives = await owner.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody(
            "T", "t", cave: new { id = caveId }, files: [File("a.zip", archive, "archive"), File("b.zip", archive, "archive")]));
        await ShouldBeProblemAsync(twoArchives, HttpStatusCode.BadRequest, "survey_upload.files_invalid");
        var badName = await owner.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody(
            "T", "t", cave: new { id = caveId }, files: [File("a.zip", archive, "archive"), File("../x.th", archive, "therionSource")]));
        (await ShouldBeProblemAsync(badName, HttpStatusCode.BadRequest, "survey_upload.files_invalid"))
            .GetProperty("file").GetString().ShouldBe("../x.th");
        await ShouldBeProblemAsync(
            await owner.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody(
                "T", "t", cave: new { id = caveId }, files: [File("a.zip", archive, "archive")], outputs: ["lox", "hologram"])),
            HttpStatusCode.BadRequest, "survey_upload.output_unknown");
        await ShouldBeProblemAsync(
            await owner.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody(
                "T", "t", cave: new { id = Guid.CreateVersion7() }, files: [File("a.zip", archive, "archive")])),
            HttpStatusCode.NotFound, "cave.not_found");
        await ShouldBeProblemAsync(
            await owner.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody(
                "T", "t", caveSurvey: new { id = Guid.CreateVersion7() }, files: [File("a.zip", archive, "archive")])),
            HttpStatusCode.NotFound, "survey_upload.survey_not_found");
        (await owner.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody(" ", " ", cave: new { id = caveId }, files: [File("a.zip", archive, "archive")])))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using (var future = new HttpRequestMessage(HttpMethod.Post, "/api/v1/survey-uploads"))
        {
            future.Headers.Add("X-SilexGis-Contract", "2");
            future.Content = JsonContent.Create(OpenBody("T", "t", cave: new { id = caveId }, files: [File("a.zip", archive, "archive")]));
            await ShouldBeProblemAsync(await owner.SendAsync(future), HttpStatusCode.UpgradeRequired, "survey_upload.contract_unsupported");
        }

        var id = await OpenAsync(owner, OpenBody(
            "T", $"refusals-{suffix}", cave: new { id = caveId },
            files: [File("a.zip", archive, "archive"), File("a.th", TherionSource(), "therionSource")]));

        await ShouldBeProblemAsync(await PutAsync(owner, id, "nope.th", TherionSource()), HttpStatusCode.NotFound, "survey_upload.file_unknown");
        await ShouldBeProblemAsync(await PutAsync(owner, id, "a.zip", archive[..^1]), HttpStatusCode.BadRequest, "survey_upload.size_mismatch");

        // The archive's name says ZIP; bytes of the same length that are text are refused, and nothing is left in the store.
        var blobs = BlobCount();
        var disguised = Enumerable.Repeat((byte)'a', archive.Length).ToArray();
        await ShouldBeProblemAsync(await PutAsync(owner, id, "a.zip", disguised), HttpStatusCode.BadRequest, "survey_upload.content_mismatch");
        BlobCount().ShouldBe(blobs);
        var refusedRow = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        refusedRow.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("name").GetString() == "a.zip")
            .GetProperty("refusalCode").GetString().ShouldBe("survey_upload.content_mismatch");

        // The honest bytes may still follow.
        (await PutAsync(owner, id, "a.zip", archive)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await ShouldBeProblemAsync(
            await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", new { }), HttpStatusCode.Conflict, "survey_upload.not_completed");

        // Abandoning keeps what arrived and ends the session.
        (await owner.DeleteAsync($"/api/v1/survey-uploads/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.DeleteAsync($"/api/v1/survey-uploads/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ShouldBeProblemAsync(await PutAsync(owner, id, "a.th", TherionSource()), HttpStatusCode.Conflict, "survey_upload.not_receiving");
        await ShouldBeProblemAsync(await owner.PostAsync($"/api/v1/survey-uploads/{id}/complete", null), HttpStatusCode.Conflict, "survey_upload.not_receiving");
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}")).GetProperty("status").GetString().ShouldBe("abandoned");

        // A session left waiting past its time answers that it is gone.
        var stale = await OpenAsync(owner, OpenBody("T", $"stale-{suffix}", cave: new { id = caveId }, files: [File("a.zip", archive, "archive")]));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.SurveyUploads.Where(u => u.Id == stale)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        await ShouldBeProblemAsync(await PutAsync(owner, stale, "a.zip", archive), HttpStatusCode.Gone, "survey_upload.expired");
        await ShouldBeProblemAsync(await owner.PostAsync($"/api/v1/survey-uploads/{stale}/complete", null), HttpStatusCode.Gone, "survey_upload.expired");
    }

    [Fact]
    public async Task Changing_an_upload_needs_write_on_the_cave_and_an_account()
    {
        var caveId = await CreateCaveAsync("authenticated", locationProtected: false);
        var archive = Archive();

        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/survey-uploads/capabilities")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody("T", "t", cave: new { id = caveId }, files: [File("a.zip", archive, "archive")])))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The viewer reads the cave and its survey, and may change neither.
        await ShouldBeProblemAsync(
            await reader.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody("T", "t", cave: new { id = caveId }, files: [File("a.zip", archive, "archive")])),
            HttpStatusCode.Forbidden, "acl.forbidden");
        var id = await OpenAsync(owner, OpenBody("T", $"write-{suffix}", cave: new { id = caveId }, files: [File("a.zip", archive, "archive")]));
        (await reader.GetAsync($"/api/v1/survey-uploads/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PutAsync(reader, id, "a.zip", archive)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.PostAsync($"/api/v1/survey-uploads/{id}/complete", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", new { })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.DeleteAsync($"/api/v1/survey-uploads/{id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await anonymous.GetAsync($"/api/v1/survey-uploads/{id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // A viewer has no right to make caves, so an upload naming none is refused at the creation rule.
        await ShouldBeProblemAsync(
            await reader.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody($"Viewer cave {suffix}", "v", files: [File("a.zip", archive, "archive")])),
            HttpStatusCode.Forbidden, "access.create_forbidden");

        // The log tail is the compiler's words over the caver's files: for the cave's writers only.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.SurveyUploads.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.LogTail, "invented log line"));
        }

        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}")).GetProperty("logTail").GetString().ShouldBe("invented log line");
        (await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}")).GetProperty("logTail").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task An_upload_of_a_protected_cave_is_withheld_without_the_exact_location_and_shown_with_it()
    {
        var caveId = await CreateCaveAsync("authenticated", locationProtected: true);
        var id = await OpenAsync(owner, OpenBody("T", $"protected-{suffix}", cave: new { id = caveId }, files: [File("a.zip", Archive(), "archive")]));

        // The reader reads the cave record and nothing of its survey: the status is "not found",
        // the listing has no row and no count, and the survey cannot be named to upload to.
        (await reader.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ShouldBeProblemAsync(await reader.GetAsync($"/api/v1/survey-uploads/{id}"), HttpStatusCode.NotFound, "survey_upload.not_found");
        var hidden = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads?caveId={caveId}");
        hidden.GetProperty("total").GetInt32().ShouldBe(0);
        hidden.GetProperty("items").GetArrayLength().ShouldBe(0);
        await ShouldBeProblemAsync(await PutAsync(reader, id, "a.zip", Archive()), HttpStatusCode.NotFound, "survey_upload.not_found");

        // The owner never lost sight of it; the same reader granted the exact location sees it.
        (await owner.GetAsync($"/api/v1/survey-uploads/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await GrantAsync(caveId, "read, viewExactLocation");
        (await reader.GetAsync($"/api/v1/survey-uploads/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var shown = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads?caveId={caveId}");
        shown.GetProperty("total").GetInt32().ShouldBe(1);
        shown.GetProperty("items")[0].GetProperty("id").GetGuid().ShouldBe(id);
    }

    [Fact]
    public async Task An_upload_of_a_private_cave_is_not_disclosed_to_someone_who_cannot_read_it()
    {
        var caveId = await CreateCaveAsync("private", locationProtected: false);
        var id = await OpenAsync(owner, OpenBody("T", $"private-{suffix}", cave: new { id = caveId }, files: [File("a.zip", Archive(), "archive")]));

        (await owner.GetAsync($"/api/v1/survey-uploads/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ShouldBeProblemAsync(await reader.GetAsync($"/api/v1/survey-uploads/{id}"), HttpStatusCode.NotFound, "survey_upload.not_found");
        await ShouldBeProblemAsync(await reader.DeleteAsync($"/api/v1/survey-uploads/{id}"), HttpStatusCode.NotFound, "survey_upload.not_found");
        await ShouldBeProblemAsync(
            await reader.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody("T", "t", cave: new { id = caveId }, files: [File("a.zip", Archive(), "archive")])),
            HttpStatusCode.NotFound, "cave.not_found");
        (await reader.GetFromJsonAsync<JsonElement>("/api/v1/survey-uploads?mine=false"))
            .GetProperty("items").EnumerateArray().ShouldNotContain(i => i.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task An_automatic_cave_takes_a_number_only_for_a_name_the_uploader_can_see()
    {
        // An uploader who may make caves and reads only what is theirs or open to them — unlike an
        // editor, whose seeded group reads every cave.
        var creatorEmail = $"supl-make-{suffix}@t.local";
        var creatorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, creatorEmail);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.AccessEntries.Add(new AccessEntry
            {
                SubjectKind = AccessSubjectKind.User,
                SubjectId = creatorId,
                Domain = AccessDomain.Features,
                Actions = AccessAction.Create,
                Effect = AccessEffect.Allow,
                ScopeKind = AccessScopeKind.All,
            });
            await db.SaveChangesAsync();
        }

        using var creator = await AuthHelper.BearerClientAsync(factory, creatorEmail);
        var title = $"Avenul Inventat {suffix}";

        // A cave of that name the creator cannot see costs nothing: the name is taken as it is.
        var hiddenId = await CreateCaveAsync("private", locationProtected: false, name: title);
        (await creator.GetAsync($"/api/v1/caves/{hiddenId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var first = await (await creator.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody(title, "first", files: [File("a.zip", Archive(), "archive")])))
            .Content.ReadFromJsonAsync<JsonElement>();
        first.GetProperty("cave").GetProperty("name").GetString().ShouldBe(title);
        first.GetProperty("cave").GetProperty("id").GetGuid().ShouldNotBe(hiddenId);

        // One the creator can see — their own, just made — does: the next is numbered.
        var second = await (await creator.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody(title, "second", files: [File("a.zip", Archive(), "archive")])))
            .Content.ReadFromJsonAsync<JsonElement>();
        second.GetProperty("cave").GetProperty("name").GetString().ShouldBe($"{title} (2)");

        // An account in no group gets a private cave when it asks for no visibility, and may ask for public.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var made = first.GetProperty("cave").GetProperty("id").GetGuid();
            (await db.Features.AsNoTracking().SingleAsync(f => f.Id == made)).Visibility.ShouldBe(Visibility.Private);
        }

        var asked = await (await creator.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody(
            title, "third", newCave: new { name = $"Named {suffix}", visibility = "public" }, files: [File("a.zip", Archive(), "archive")])))
            .Content.ReadFromJsonAsync<JsonElement>();
        asked.GetProperty("cave").GetProperty("name").GetString().ShouldBe($"Named {suffix}");
        await ShouldBeProblemAsync(
            await creator.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody(
                title, "fourth", newCave: new { visibility = "cavingGroup" }, files: [File("a.zip", Archive(), "archive")])),
            HttpStatusCode.BadRequest, "survey_upload.visibility_invalid");
    }

    [Fact]
    public async Task An_installation_that_makes_no_caves_from_uploads_refuses_an_upload_that_names_none()
    {
        var root = Path.Combine(TestScratch.Root, $"silexgis-test-supl-off-{Guid.NewGuid():N}");
        using var strict = new SilexGisApiFactory(postgres.ConnectionString, Settings(root, caveCreation: false), JobWorkers.RemoveFrom, ownHost: true);
        var email = $"supl-strict-{suffix}@t.local";
        _ = await AuthHelper.CreateUserAsync(strict, GlobalRoles.Editor, email);
        using var client = await AuthHelper.BearerClientAsync(strict, email);

        var capabilities = await client.GetFromJsonAsync<JsonElement>("/api/v1/survey-uploads/capabilities");
        capabilities.GetProperty("caveCreation").GetProperty("allowed").GetBoolean().ShouldBeFalse();
        await ShouldBeProblemAsync(
            await client.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody("T", "t", files: [File("a.zip", Archive(), "archive")])),
            HttpStatusCode.BadRequest, "survey_upload.cave_required");
        await ShouldBeProblemAsync(
            await client.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody("T", "t", newCave: new { name = "N" }, files: [File("a.zip", Archive(), "archive")])),
            HttpStatusCode.Forbidden, "survey_upload.cave_creation_disabled");

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task The_caves_timeline_does_not_hand_over_what_the_upload_routes_withhold()
    {
        // An upload and its survey are audited into the cave's timeline, a second surface for the
        // same rows. A reader refused them on the upload routes is refused their contents there too.
        var caveId = await CreateCaveAsync("authenticated", locationProtected: true);
        var archive = Archive();
        var id = await OpenAsync(owner, OpenBody("T", $"timeline-{suffix}", cave: new { id = caveId }, files: [File("a.zip", archive, "archive")]));
        (await PutAsync(owner, id, "a.zip", archive)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.PostAsync($"/api/v1/survey-uploads/{id}/complete", null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // A failed run with a log tail, then run again: the tail, told only to the cave's writers,
        // must not be written into the history as the value the rerun cleared.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.SurveyUploads.Where(u => u.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Status, SurveyUploadStatus.Failed)
                .SetProperty(u => u.ErrorCode, "survey_upload.invented_failure")
                .SetProperty(u => u.LogTail, "invented log line"));
        }

        (await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", new { })).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        string[] surveyTypes = [nameof(SurveyUpload), nameof(CaveSurvey)];
        var withheld = await reader.GetAsync($"/api/v1/history?entityType=feature&entityId={caveId}&pageSize=100");
        withheld.StatusCode.ShouldBe(HttpStatusCode.OK);
        var hiddenRows = (await withheld.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray()
            .Where(e => surveyTypes.Contains(e.GetProperty("entityType").GetString()))
            .ToList();
        hiddenRows.Select(e => e.GetProperty("entityType").GetString()).Distinct().Count().ShouldBe(2);
        foreach (var row in hiddenRows)
        {
            row.GetProperty("changes").ValueKind.ShouldBe(
                JsonValueKind.Null, "an upload's or a survey's history reached a caller who may not place the cave");
            row.GetProperty("redactedProperties").GetArrayLength().ShouldBeGreaterThan(0);
        }

        // The owner still sees both, and the log tail is not in the trail for anybody.
        var seen = await owner.GetAsync($"/api/v1/history?entityType=feature&entityId={caveId}&pageSize=100");
        var seenText = await seen.Content.ReadAsStringAsync();
        var seenRows = JsonDocument.Parse(seenText).RootElement.GetProperty("items").EnumerateArray()
            .Where(e => surveyTypes.Contains(e.GetProperty("entityType").GetString()))
            .ToList();
        seenRows.Count(e => e.GetProperty("entityType").GetString() == nameof(SurveyUpload)).ShouldBeGreaterThan(1);
        seenRows.ShouldContain(e => e.GetProperty("changes").ValueKind == JsonValueKind.Object);
        seenText.ShouldNotContain("invented log line");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var idText = id.ToString();
            var trail = await db.AuditEntries.AsNoTracking()
                .Where(a => a.EntityId == idText)
                .Select(a => a.Changes)
                .ToListAsync();
            trail.Count.ShouldBeGreaterThan(1);
            trail.ShouldAllBe(c => c == null || !c.Contains("invented log line"));
            (await db.SurveyUploads.AsNoTracking().SingleAsync(u => u.Id == id)).LogTail.ShouldBeNull();
        }
    }

    [Fact]
    public async Task An_export_without_a_survey_format_is_a_document_made_on_the_documents_own_terms()
    {
        // A club's cave, written to by an account outside the club whose right to write reaches it
        // installation-wide — and who holds no right to make documents.
        Guid clubId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var club = new CavingGroup { Name = $"Upload Club {suffix}", Slug = $"upload-club-{suffix}", Type = CavingGroupType.CavingClub };
            db.CavingGroups.Add(club);
            await db.SaveChangesAsync();
            clubId = club.Id;
        }

        var caveId = await CreateCaveAsync("authenticated", locationProtected: false);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.Features.Where(f => f.Id == caveId).ExecuteUpdateAsync(s => s.SetProperty(f => f.CavingGroupId, clubId));
        }

        var outsiderEmail = $"supl-out-{suffix}@t.local";
        var outsiderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, outsiderEmail);
        await AllowAsync(outsiderId, AccessDomain.Features, AccessAction.Read | AccessAction.Write | AccessAction.ViewExactLocation);
        using var outsider = await AuthHelper.BearerClientAsync(factory, outsiderEmail);

        var archive = Archive();
        var plan = Encoding.ASCII.GetBytes("%PDF-1.4\n% invented plan of nothing\n%%EOF\n");
        object[] files = [File("a.zip", archive, "archive"), File("plan.pdf", plan, "other")];

        // Write on the cave makes survey sources; a document needs the right to make documents.
        await ShouldBeProblemAsync(
            await outsider.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody("T", $"outsider-{suffix}", cave: new { id = caveId }, files: files)),
            HttpStatusCode.Forbidden, "access.create_forbidden");
        (await outsider.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody(
            "T", $"outsider-src-{suffix}", cave: new { id = caveId }, files: [File("a.zip", archive, "archive")])))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // Given that right, the document is made on the cave's terms — but not filed under a club
        // the account is not in, which would hand the club's rulesets over it.
        await AllowAsync(outsiderId, AccessDomain.Documents, AccessAction.Read | AccessAction.Write | AccessAction.Create);
        var id = await OpenAsync(outsider, OpenBody("T", $"outsider-doc-{suffix}", cave: new { id = caveId }, files: files));
        var sent = await PutAsync(outsider, id, "plan.pdf", plan);
        sent.StatusCode.ShouldBe(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());
        var documentId = (await sent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var document = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == documentId);
            document.CavingGroupId.ShouldBeNull();
            document.Visibility.ShouldBe(Visibility.Authenticated);
        }

        // Asking outright for the documents to be open to a group the account cannot file them
        // under is refused with the reason, before any byte is sent.
        await ShouldBeProblemAsync(
            await outsider.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody(
                "T", $"outsider-grp-{suffix}", cave: new { id = caveId }, files: files, documentsVisibility: "cavingGroup")),
            HttpStatusCode.BadRequest, "survey_upload.visibility_invalid");

        // And the same on a cave bound to no group at all, for the owner.
        var unbound = await CreateCaveAsync("authenticated", locationProtected: false);
        await ShouldBeProblemAsync(
            await owner.PostAsJsonAsync("/api/v1/survey-uploads", OpenBody(
                "T", $"unbound-grp-{suffix}", cave: new { id = unbound }, files: files, documentsVisibility: "cavingGroup")),
            HttpStatusCode.BadRequest, "survey_upload.visibility_invalid");
    }

    [Fact]
    public async Task An_open_request_with_a_visibility_out_of_range_or_an_empty_file_is_refused_as_invalid()
    {
        var caveId = await CreateCaveAsync("authenticated", locationProtected: false);
        var archive = Archive();
        var file = $$"""{ "name": "a.zip", "size": {{archive.LongLength}}, "role": "archive" }""";

        async Task ShouldBeInvalidAsync(string json)
        {
            var response = await owner.PostAsync(
                "/api/v1/survey-uploads", new StringContent(json, Encoding.UTF8, "application/json"));
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        }

        await ShouldBeInvalidAsync($$"""
            { "source": "topodroid", "surveyTitle": "T", "newCave": { "visibility": 7 }, "files": [{{file}}] }
            """);
        await ShouldBeInvalidAsync($$"""
            { "source": "topodroid", "surveyTitle": "T", "cave": { "id": "{{caveId}}" }, "documentsVisibility": 9, "files": [{{file}}] }
            """);
        await ShouldBeInvalidAsync($$"""
            { "source": "topodroid", "surveyTitle": "T", "cave": { "id": "{{caveId}}" }, "files": [{{file}}, null] }
            """);
    }

    [Fact]
    public async Task Run_again_by_another_writer_the_upload_keeps_what_its_uploader_always_asks_for()
    {
        (await owner.PutAsJsonAsync("/api/v1/me/survey-upload-preferences", new { alwaysOutputs = new[] { "kml" }, publicLinks = false }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        try
        {
            var caveId = await CreateCaveAsync("authenticated", locationProtected: false);
            var archive = Archive();
            var id = await OpenAsync(owner, OpenBody("T", $"rerun-{suffix}", cave: new { id = caveId }, files: [File("a.zip", archive, "archive")]));
            (await PutAsync(owner, id, "a.zip", archive)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await owner.PostAsync($"/api/v1/survey-uploads/{id}/complete", null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
                await db.SurveyUploads.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, SurveyUploadStatus.Succeeded));
            }

            var otherEmail = $"supl-rr-{suffix}@t.local";
            await AllowAsync(
                await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, otherEmail),
                AccessDomain.Features,
                AccessAction.Read | AccessAction.Write);
            using var other = await AuthHelper.BearerClientAsync(factory, otherEmail);
            var rerun = await other.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", new { pipeline = new { outputs = new[] { "lox" } } });
            var text = await rerun.Content.ReadAsStringAsync();
            rerun.StatusCode.ShouldBe(HttpStatusCode.Accepted, text);
            var outputs = JsonDocument.Parse(text).RootElement.GetProperty("outputs").EnumerateArray().Select(o => o.GetString()).ToList();
            outputs.ShouldContain("lox");
            outputs.ShouldContain("kml");
        }
        finally
        {
            (await owner.PutAsJsonAsync("/api/v1/me/survey-upload-preferences", new { alwaysOutputs = Array.Empty<string>(), publicLinks = false }))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    // ---- helpers ----

    private static object File(string name, byte[] bytes, string role) => new { name, size = bytes.LongLength, role };

    private static object OpenBody(
        string title,
        string surveyName,
        object[] files,
        string source = "topodroid",
        object? cave = null,
        object? caveSurvey = null,
        object? newCave = null,
        string[]? outputs = null,
        string? documentsVisibility = null) => new
    {
        source,
        client = new { name = "TopoDroid", version = "6.5.34" },
        cave,
        caveSurvey,
        newCave,
        surveyTitle = title,
        surveyName,
        files,
        pipeline = outputs is null ? null : new { outputs },
        documentsVisibility,
    };

    private static async Task<Guid> OpenAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/v1/survey-uploads", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, string name, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return client.PutAsync($"/api/v1/survey-uploads/{id}/files/{Uri.EscapeDataString(name)}", content);
    }

    private static async Task<JsonElement> ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, text);
        var body = JsonDocument.Parse(text).RootElement;
        body.GetProperty("code").GetString().ShouldBe(code, text);
        return body;
    }

    private int BlobCount() =>
        Directory.Exists(filesRoot)
            ? Directory.EnumerateFiles(filesRoot, "*", SearchOption.AllDirectories)
                .Count(f => !f.StartsWith(Path.Combine(filesRoot, "keys"), StringComparison.Ordinal))
            : 0;

    private async Task<Guid> CreateCaveAsync(string visibility, bool locationProtected, string? name = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = name ?? $"Upload Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>An installation-wide allow over one domain, for one account.</summary>
    private async Task AllowAsync(Guid userId, AccessDomain domain, AccessAction actions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Domain = domain,
            Actions = actions,
            Effect = AccessEffect.Allow,
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();
    }

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

    /// <summary>An invented survey archive in the shape a survey tool writes: a manifest first, then the database dump.</summary>
    private static byte[] Archive()
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("manifest").Open()))
            {
                writer.Write("6.5.34 650034\n0.0.0\ninventata\n");
            }

            using (var writer = new StreamWriter(zip.CreateEntry("survey.sql").Open()))
            {
                writer.Write("INSERT into surveys values ( 1, \"inventata\", \"2026.10.20\", \"\", 0.00, \"\", \"\" );\n");
            }
        }

        return memory.ToArray();
    }

    /// <summary>An invented Therion source of three stations and no fix.</summary>
    private static byte[] TherionSource() => Encoding.UTF8.GetBytes(
        "survey inventata\n  centreline\n    data normal from to length compass clino\n    1 2 5.00 120 -5\n    2 3 4.20 95 0\n  endcentreline\nendsurvey\n");
}
