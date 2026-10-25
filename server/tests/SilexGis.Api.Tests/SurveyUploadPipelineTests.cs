// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Tests;

/// <summary>
/// A survey upload's processing run through its stages — the archive check, the reading of the
/// survey archive, the Therion project, the compile, storing what it produced and queueing the
/// model's reading — as a device sees it on the status route; and the sweep that closes uploads
/// whose device stopped sending and prunes older versions' outputs.
/// </summary>
/// <remarks>
/// The survey archive is genuine TopoDroid output of an invented survey; the broken one is built
/// by the test. Nothing here is a real cave.
/// </remarks>
public sealed class SurveyUploadPipelineTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture postgres;
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;

    public SurveyUploadPipelineTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-supipe-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, Settings(filesRoot), JobWorkers.RemoveFrom);
    }

    private static Dictionary<string, string?> Settings(string root) => new()
    {
        ["Files:Root"] = root,
        ["Keys:Path"] = Path.Combine(root, "keys"),
    };

    public async ValueTask InitializeAsync()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"supipe-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"supipe-{suffix}@t.local");
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

    private static byte[] Fixture(string name) =>
        System.IO.File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "topodroid", name));

    /// <summary>A second host over the same database and file store, with this compiler.</summary>
    private SilexGisApiFactory WithCompiler(ISurveyCompiler compiler) => new(
        postgres.ConnectionString,
        Settings(filesRoot),
        services =>
        {
            JobWorkers.RemoveFrom(services);
            services.Replace(ServiceDescriptor.Singleton(compiler));
        });

    private static byte[] InventedArchive() =>
        System.IO.File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "topodroid", "pestera_inventata.zip"));

    [Fact]
    public async Task A_completed_upload_is_checked_and_read_and_succeeds_saying_it_was_not_compiled()
    {
        var archive = InventedArchive();
        var id = await SendAsync(owner, $"Pestera Inventata {suffix}", "pestera_inventata", ("pestera_inventata.zip", archive));

        await RunPipelineAsync(factory, id);

        var status = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        status.GetProperty("status").GetString().ShouldBe("succeeded");
        status.GetProperty("stage").GetString().ShouldBe("unpack");
        status.GetProperty("progress").GetInt32().ShouldBe(100);
        status.GetProperty("message").GetString().ShouldBe(SurveyPipelineNotes.NoCompiler);
        status.GetProperty("error").ValueKind.ShouldBe(JsonValueKind.Null);
        status.GetProperty("startedAt").ValueKind.ShouldBe(JsonValueKind.String);
        status.GetProperty("finishedAt").ValueKind.ShouldBe(JsonValueKind.String);

        // What the archive says about the survey, read onto the row.
        var survey = status.GetProperty("survey");
        survey.GetProperty("date").GetString().ShouldBe("2026-10-25");
        survey.GetProperty("team").GetString().ShouldBe("Test A.; Test B.");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.SurveyUploads.AsNoTracking().SingleAsync(u => u.Id == id)).Declination.ShouldBe(0.0);

        // The archived file was read, never changed.
        status.GetProperty("files").EnumerateArray().ShouldHaveSingleItem()
            .GetProperty("sha256").GetString().ShouldBe(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(archive)));
        status.GetProperty("artefacts").GetArrayLength().ShouldBe(0);

        // The upload made the cave, so the cave is placed at the survey's fixed station (invented
        // coordinates), as its main entrance, uncompiled as the survey is.
        var caveId = status.GetProperty("cave").GetProperty("id").GetGuid();
        status.GetProperty("caveCreated").GetBoolean().ShouldBeTrue();
        var entrance = await db.CaveEntrances.AsNoTracking().Include(e => e.Feature)
            .Where(e => e.CaveFeatureId == caveId).ToListAsync();
        entrance.ShouldHaveSingleItem().IsMain.ShouldBeTrue();
        var point = (NetTopologySuite.Geometries.Point)entrance[0].Feature.Geom!;
        point.X.ShouldBe(24.0, 1e-6);
        point.Y.ShouldBe(46.0, 1e-6);
        // Not asked to protect the cave, so neither it nor its entrance is.
        entrance[0].Feature.IsProtectedEffective.ShouldBeFalse();

        // A rerun starts over from the top and lands the same way, keeping the first start time.
        var started = status.GetProperty("startedAt").GetDateTimeOffset();
        (await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", new { pipeline = new { compile = false } }))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await RunPipelineAsync(factory, id);
        var again = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        again.GetProperty("status").GetString().ShouldBe("succeeded");
        again.GetProperty("message").GetString().ShouldBe(SurveyPipelineNotes.NotAsked);
        again.GetProperty("startedAt").GetDateTimeOffset().ShouldBe(started);

        // The entrance the first run wrote stands; a second is never added beside it.
        (await db.CaveEntrances.AsNoTracking().CountAsync(e => e.CaveFeatureId == caveId)).ShouldBe(1);
    }

    [Fact]
    public async Task A_cave_the_phone_asked_to_protect_is_protected_before_its_entrance_is_placed()
    {
        var archive = InventedArchive();
        var id = await OpenAsync(
            owner, $"Pestera Ocrotita {suffix}", "pestera_inventata", null, [("pestera_inventata.zip", archive)],
            newCave: new { locationProtected = true });
        (await PutAsync(owner, id, "pestera_inventata.zip", archive)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.PostAsync($"/api/v1/survey-uploads/{id}/complete", null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await RunPipelineAsync(factory, id);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var upload = await db.SurveyUploads.AsNoTracking().SingleAsync(u => u.Id == id);
        upload.Status.ShouldBe(SurveyUploadStatus.Succeeded);
        upload.CreatedCave.ShouldBeTrue();
        var cave = await db.Features.AsNoTracking().SingleAsync(f => f.Id == upload.CaveFeatureId);
        cave.LocationProtected.ShouldBeTrue();
        cave.IsProtectedEffective.ShouldBeTrue();

        // The entrance the survey's fixed station placed carries the cave's protection.
        var entrance = await db.CaveEntrances.AsNoTracking().Include(e => e.Feature)
            .SingleAsync(e => e.CaveFeatureId == cave.Id);
        entrance.IsMain.ShouldBeTrue();
        entrance.Feature.IsProtectedEffective.ShouldBeTrue();
    }

    [Fact]
    public async Task A_broken_archive_fails_the_run_with_a_code_and_stays_archived()
    {
        // A ZIP, so the route archives it; but not a survey archive — no manifest.
        var broken = TopoDroidArchiveReaderTests.Zip(("survey.sql", "INSERT into surveys values( 1, \"x\" );\n"));
        var id = await SendAsync(owner, $"Broken {suffix}", "broken", ("broken.zip", broken));

        await Should.ThrowAsync<InvalidOperationException>(RunPipelineAsync(factory, id));

        var status = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        status.GetProperty("status").GetString().ShouldBe("failed");
        status.GetProperty("stage").GetString().ShouldBe("unpack");
        var error = status.GetProperty("error");
        error.GetProperty("code").GetString().ShouldBe(SurveyPipelineFailures.ArchiveInvalid);
        error.GetProperty("message").GetString()!.ShouldContain("no manifest");
        status.GetProperty("finishedAt").ValueKind.ShouldBe(JsonValueKind.String);
        status.GetProperty("files").EnumerateArray().ShouldHaveSingleItem()
            .GetProperty("status").GetString().ShouldBe("stored");

        // And it can be run again once something has changed; the same archive fails the same way.
        (await owner.PostAsync($"/api/v1/survey-uploads/{id}/rerun", null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await Should.ThrowAsync<InvalidOperationException>(RunPipelineAsync(factory, id));
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}"))
            .GetProperty("error").GetProperty("code").GetString().ShouldBe(SurveyPipelineFailures.ArchiveInvalid);
    }

    [Fact]
    public async Task An_archived_file_gone_from_the_store_fails_the_archive_stage()
    {
        var id = await SendAsync(owner, $"Vanished {suffix}", "vanished", ("vanished.zip", InventedArchive()));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var store = scope.ServiceProvider.GetRequiredService<IFileStore>();
            var documentId = (await db.SurveyUploadFiles.AsNoTracking().SingleAsync(f => f.SurveyUploadId == id)).DocumentId;
            var path = await (
                from version in db.DocumentVersions
                join blob in db.StoredFiles on version.Id equals blob.DocumentVersionId
                where version.DocumentId == documentId
                select blob.StoragePath).SingleAsync();
            await store.DeleteAsync(path);
        }

        await Should.ThrowAsync<InvalidOperationException>(RunPipelineAsync(factory, id));

        var status = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        status.GetProperty("status").GetString().ShouldBe("failed");
        status.GetProperty("stage").GetString().ShouldBe("archive");
        status.GetProperty("error").GetProperty("code").GetString().ShouldBe(SurveyPipelineFailures.FilesMissing);
    }

    [Fact]
    public async Task With_a_compiler_the_survey_is_built_into_a_project_compiled_and_judged_by_its_log()
    {
        var compiler = new FakeSurveyCompiler();
        using var host = WithCompiler(compiler);
        await AuthHelper.CreateUserAsync(host, GlobalRoles.Editor, $"supipe-c-{suffix}@t.local");
        using var client = await AuthHelper.BearerClientAsync(host, $"supipe-c-{suffix}@t.local");

        var capabilities = await client.GetFromJsonAsync<JsonElement>("/api/v1/survey-uploads/capabilities");
        var advertised = capabilities.GetProperty("compiler");
        advertised.GetProperty("available").GetBoolean().ShouldBeTrue();
        advertised.GetProperty("version").GetString().ShouldBe(FakeSurveyCompiler.FakeVersion);
        advertised.GetProperty("outputs").EnumerateArray().Select(o => o.GetString()).ShouldBe(SurveyOutputs.Catalogue);

        var survey = Fixture("pestera_inventata.th");
        var id = await SendAsync(
            client, $"Compiled {suffix}", "pestera_inventata",
            ("pestera_inventata.zip", InventedArchive()), ("pestera_inventata.th", survey),
            ("pestera_inventata-1p.th2", Fixture("pestera_inventata-1p.th2")), ("pestera_inventata-1s.th2", Fixture("pestera_inventata-1s.th2")));
        await RunPipelineAsync(host, id);

        var status = await client.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        status.GetProperty("status").GetString().ShouldBe("succeeded", status.ToString());
        status.GetProperty("stage").GetString().ShouldBe("read");
        status.GetProperty("message").GetString().ShouldBe(SurveyPipelineNotes.Compiled(4, 4, sketchesDrawn: true));
        status.GetProperty("compiler").GetProperty("version").GetString().ShouldBe(FakeSurveyCompiler.FakeVersion);
        status.GetProperty("logTail").GetString().ShouldBe("invented tail");
        compiler.Calls.ShouldBe(1);

        // The compiler was handed the survey with TopoDroid's commented sketches and fix made
        // live, the sketches beside it, and a configuration naming the four default outputs.
        var project = compiler.LastProject;
        var compiled = project["th/pestera_inventata.th"].Split('\n').Select(l => l.Trim()).ToList();
        compiled.ShouldContain("input \"pestera_inventata-1p.th2\"");
        compiled.ShouldContain("map m1s -projection extended");
        compiled.ShouldContain(l => l.StartsWith("fix 0 ", StringComparison.Ordinal));
        project.ShouldContainKey("th/pestera_inventata-1p.th2");
        project.ShouldContainKey("th/pestera_inventata-1s.th2");
        project.ShouldContainKey("upload.th");
        var config = project["thconfig.thc"];
        config.ShouldContain("-fmt loch");
        config.ShouldContain("-fmt survex");
        config.ShouldContain("-projection plan -fmt pdf");
        config.ShouldContain("-projection extended -fmt pdf");

        // What was archived is what the device sent, byte for byte.
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var archived = await db.SurveyUploadFiles.AsNoTracking().SingleAsync(f => f.SurveyUploadId == id && f.Name == "pestera_inventata.th");
        archived.Sha256.ShouldBe(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(survey)), StringCompareShould.IgnoreCase);

        // The run's own directory is gone once the run ends.
        Directory.EnumerateDirectories(Path.Combine(filesRoot, "survey-work")).ShouldBeEmpty();

        await ShouldHaveStoredEveryOutputAsync(db, client, id, status);
    }

    /// <summary>
    /// Everything a compile of the invented survey produced, stored where it belongs and named for
    /// the upload's version: the two models, the two drawings, the log with its compilation
    /// record, and the configuration that was compiled.
    /// </summary>
    private static async Task ShouldHaveStoredEveryOutputAsync(SilexGisDbContext db, HttpClient client, Guid id, JsonElement status)
    {
        var upload = await db.SurveyUploads.AsNoTracking().SingleAsync(u => u.Id == id);
        var stamp = SurveyVersionStamp.Of(upload.VersionAt);

        var artefacts = status.GetProperty("artefacts").EnumerateArray().ToList();
        artefacts.Select(a => (a.GetProperty("kind").GetString(), a.GetProperty("name").GetString())).ShouldBe(
            [
                ("3d", $"pestera_inventata-{stamp}.3d"),
                ("lox", $"pestera_inventata-{stamp}.lox"),
                ("plan-pdf", $"pestera_inventata-{stamp}.plan.pdf"),
                ("extended-pdf", $"pestera_inventata-{stamp}.extended.pdf"),
                ("log", $"pestera_inventata-{stamp}.log"),
            ],
            ignoreOrder: true);
        foreach (var artefact in artefacts)
        {
            // Every artefact can be fetched by the address it is listed with.
            var bytes = await client.GetByteArrayAsync(artefact.GetProperty("contentUrl").GetString());
            bytes.LongLength.ShouldBe(artefact.GetProperty("sizeBytes").GetInt64());
        }

        // The inputs list keeps to what the device sent and what the run was compiled from.
        status.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("name").GetString()).ShouldBe(
            [
                "pestera_inventata.zip", "pestera_inventata.th", "pestera_inventata-1p.th2", "pestera_inventata-1s.th2",
                $"pestera_inventata-{stamp}.thconfig", $"pestera_inventata-{stamp}.wrapper.th",
            ],
            ignoreOrder: true);

        // The Therion model: the survey's and the upload's, never the cave's current one, placed
        // in the coordinate system the compiler wrote it in, and queued to be read.
        var models = await db.SurveyModels.AsNoTracking().Where(m => m.SurveyUploadId == id).ToListAsync();
        var lox = models.Where(m => m.Format == SurveyModelFormat.Lox).ShouldHaveSingleItem();
        lox.IsCurrent.ShouldBeFalse();
        lox.Status.ShouldBe(SurveyModelStatus.Pending);
        lox.SourceEpsg.ShouldBe(32635);
        lox.CaveSurveyId.ShouldBe(upload.CaveSurveyId);
        lox.CaveFeatureId.ShouldBe(upload.CaveFeatureId);
        lox.SurveyedAt.ShouldBe(upload.SurveyDate);
        artefacts.Single(a => a.GetProperty("kind").GetString() == "lox").GetProperty("modelStatus").GetString().ShouldBe("pending");

        // The Survex model is kept for download and the viewer, and not read.
        var survex = models.Where(m => m.Format == SurveyModelFormat.Survex3d).ShouldHaveSingleItem();
        survex.IsCurrent.ShouldBeFalse();
        survex.Status.ShouldBe(SurveyModelStatus.Ready);

        var readings = (await db.ProcessingJobs.AsNoTracking().Where(j => j.Kind == ProcessingJobKinds.SurveyGraph).ToListAsync())
            .Select(j => JsonSerializer.Deserialize<SurveyGraphPayload>(j.Payload, JsonSerializerOptions.Web)!.SurveyModelId)
            .ToList();
        readings.ShouldContain(lox.Id);
        readings.ShouldNotContain(survex.Id);

        // The log is an archived source with its compilation record, linked to the model the same
        // run produced, and the record's reading is queued.
        var sources = await db.SurveySources.AsNoTracking().Where(s => s.SurveyUploadId == id).ToListAsync();
        var log = sources.Where(s => s.Kind == SurveySourceKind.TherionLog).ShouldHaveSingleItem();
        var compilation = await db.SurveyCompilations.AsNoTracking().SingleAsync(c => c.SurveySourceId == log.Id);
        compilation.SurveyModelId.ShouldBe(lox.Id);
        (await db.ProcessingJobs.AsNoTracking().Where(j => j.Kind == ProcessingJobKinds.SurveyCompilation).ToListAsync())
            .ShouldContain(j => JsonSerializer.Deserialize<SurveyCompilationPayload>(j.Payload, JsonSerializerOptions.Web)!.SurveyCompilationId == compilation.Id);
        sources.ShouldContain(s => s.Kind == SurveySourceKind.TherionConfig && s.Name == $"pestera_inventata-{stamp}.thconfig");

        // The drawings are documents attached to the cave, tagged as survey outputs, in the
        // upload's batch, titled with the version's date.
        var drawings = artefacts.Where(a => a.GetProperty("kind").GetString()!.EndsWith("-pdf", StringComparison.Ordinal))
            .Select(a => a.GetProperty("documentId").GetGuid()).ToList();
        drawings.Count.ShouldBe(2);
        var tag = await db.Tags.AsNoTracking().SingleAsync(t => t.Slug == SurveyUploadArtefactStore.OutputTagName);
        foreach (var documentId in drawings)
        {
            var document = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == documentId);
            document.UploadBatchId.ShouldBe(upload.UploadBatchId);
            document.Title.ShouldContain(SurveyVersionStamp.DocumentTitle("", upload.VersionAt).Trim());
            var fileId = await (
                from version in db.DocumentVersions
                join blob in db.StoredFiles on version.Id equals blob.DocumentVersionId
                where version.DocumentId == documentId && version.IsCurrent
                select blob.Id).SingleAsync();
            (await db.Attachments.AsNoTracking().AnyAsync(a => a.FileId == fileId && a.FeatureId == upload.CaveFeatureId)).ShouldBeTrue();
            (await db.Taggings.AsNoTracking().AnyAsync(t => t.TagId == tag.Id && t.EntityType == AttachedEntityType.StoredFile && t.EntityId == fileId))
                .ShouldBeTrue();
        }

        // Every document the run stored is in the upload's batch, so undoing the upload reaches it.
        var stored = await db.SurveyUploadFiles.AsNoTracking().Where(f => f.SurveyUploadId == id && f.DocumentId != null)
            .Select(f => f.DocumentId!.Value).ToListAsync();
        (await db.Documents.AsNoTracking().CountAsync(d => stored.Contains(d.Id) && d.UploadBatchId == upload.UploadBatchId))
            .ShouldBe(stored.Count);
    }

    [Fact]
    public async Task A_rerun_does_not_put_back_an_entrance_somebody_deleted()
    {
        var id = await SendAsync(owner, $"Deleted Entrance {suffix}", "pestera_inventata", ("pestera_inventata.zip", InventedArchive()));
        await RunPipelineAsync(factory, id);
        var caveId = (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}"))
            .GetProperty("cave").GetProperty("id").GetGuid();

        // Somebody removes the entrance on the web, because the position must not be recorded —
        // through the same writes the entrance route makes.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var writes = scope.ServiceProvider.GetRequiredService<SilexGis.Infrastructure.Features.FeatureWriteService>();
            var entranceId = await db.CaveEntrances.AsNoTracking().Where(e => e.CaveFeatureId == caveId).Select(e => e.Id).SingleAsync();
            await writes.SoftDeleteAsync(entranceId);
            await writes.SyncCaveMirrorAsync(caveId);
            await db.SaveChangesAsync();
        }

        (await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", new { pipeline = new { compile = false } }))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await RunPipelineAsync(factory, id);

        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}")).GetProperty("status").GetString().ShouldBe("succeeded");
        await using var check = factory.Services.CreateAsyncScope();
        var read = check.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await read.CaveEntrances.AsNoTracking().CountAsync(e => e.CaveFeatureId == caveId)).ShouldBe(0);
        (await read.CaveEntrances.AsNoTracking().IgnoreQueryFilters().CountAsync(e => e.CaveFeatureId == caveId)).ShouldBe(1);
    }

    [Fact]
    public async Task A_rerun_revises_its_own_outputs_and_a_second_version_is_kept_beside_the_first_until_the_sweep_is_told_to_prune()
    {
        using var host = WithCompiler(new FakeSurveyCompiler());
        await AuthHelper.CreateUserAsync(host, GlobalRoles.Editor, $"supipe-v-{suffix}@t.local");
        using var client = await AuthHelper.BearerClientAsync(host, $"supipe-v-{suffix}@t.local");
        (string, byte[])[] files =
        [
            ("pestera_inventata.zip", InventedArchive()), ("pestera_inventata.th", Fixture("pestera_inventata.th")),
            ("pestera_inventata-1p.th2", Fixture("pestera_inventata-1p.th2")),
        ];

        var first = await SendAsync(client, $"Versions {suffix}", "pestera_inventata", files);
        await RunPipelineAsync(host, first);
        var firstStatus = await client.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{first}");
        firstStatus.GetProperty("status").GetString().ShouldBe("succeeded", firstStatus.ToString());
        var surveyId = firstStatus.GetProperty("caveSurvey").GetProperty("id").GetGuid();
        var caveId = firstStatus.GetProperty("cave").GetProperty("id").GetGuid();
        var firstNames = Names(firstStatus);
        firstNames.Count.ShouldBe(5);

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var lox = await db.SurveyModels.AsNoTracking().SingleAsync(m => m.SurveyUploadId == first && m.Format == SurveyModelFormat.Lox);
        var rows = await db.SurveyUploadFiles.AsNoTracking().CountAsync(f => f.SurveyUploadId == first);

        // Run again with a compiler that writes something else: the same files under the same
        // names get a new revision each, and the model reads its new file — nothing is stored twice.
        using var corrected = WithCompiler(new FakeSurveyCompiler { Word = "corrected" });
        (await client.PostAsync($"/api/v1/survey-uploads/{first}/rerun", null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await RunPipelineAsync(corrected, first);
        var rerun = await client.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{first}");
        rerun.GetProperty("status").GetString().ShouldBe("succeeded", rerun.ToString());
        Names(rerun).ShouldBe(firstNames, ignoreOrder: true);
        (await db.SurveyUploadFiles.AsNoTracking().CountAsync(f => f.SurveyUploadId == first)).ShouldBe(rows);
        var revised = await db.SurveyModels.AsNoTracking().Where(m => m.SurveyUploadId == first).ToListAsync();
        revised.Count.ShouldBe(2);
        var revisedLox = revised.Single(m => m.Id == lox.Id);
        revisedLox.FileId.ShouldNotBe(lox.FileId);
        revisedLox.Status.ShouldBe(SurveyModelStatus.Pending);
        var plan = rerun.GetProperty("artefacts").EnumerateArray().Single(a => a.GetProperty("kind").GetString() == "plan-pdf");
        (await client.GetStringAsync(plan.GetProperty("contentUrl").GetString())).ShouldStartWith("corrected ");
        (await db.DocumentVersions.AsNoTracking().CountAsync(v => v.DocumentId == plan.GetProperty("documentId").GetGuid())).ShouldBe(2);
        (await db.SurveyCompilations.AsNoTracking().CountAsync(c => c.SurveyModelId == lox.Id)).ShouldBe(1);

        // A second version, sent to the survey by the id the server answered: same cave, same
        // survey, its own names, and the first version's outputs kept.
        var second = await SendToSurveyAsync(client, surveyId, files);
        await RunPipelineAsync(host, second);
        var secondStatus = await client.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{second}");
        secondStatus.GetProperty("status").GetString().ShouldBe("succeeded", secondStatus.ToString());
        secondStatus.GetProperty("cave").GetProperty("id").GetGuid().ShouldBe(caveId);
        secondStatus.GetProperty("caveCreated").GetBoolean().ShouldBeFalse();
        var secondNames = Names(secondStatus);
        secondNames.Count.ShouldBe(5);
        secondNames.Intersect(firstNames).ShouldBeEmpty();

        var listed = await client.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads?caveSurveyId={surveyId}");
        listed.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ShouldBe([second, first]);
        (await db.SurveyModels.AsNoTracking().CountAsync(m => m.CaveSurveyId == surveyId)).ShouldBe(4);
        (await db.SurveyModels.AsNoTracking().AnyAsync(m => m.CaveFeatureId == caveId && m.IsCurrent)).ShouldBeFalse();
        (await db.CaveEntrances.AsNoTracking().CountAsync(e => e.CaveFeatureId == caveId)).ShouldBe(1);

        // The default keeps every version's outputs: a sweep prunes nothing.
        await SweepAsync(host);
        Names(await client.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{first}")).Count.ShouldBe(5);

        // An installation keeping the newest version's outputs only prunes the first version's,
        // and keeps what it was compiled from, so it can be compiled again.
        using var keeping = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>(Settings(filesRoot)) { ["SurveyUploads:KeepArtefactVersions"] = "1" },
            JobWorkers.RemoveFrom);
        await SweepAsync(keeping);
        var pruned = await client.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{first}");
        pruned.GetProperty("artefacts").GetArrayLength().ShouldBe(0);
        var kept = pruned.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("name").GetString()!).ToList();
        kept.Count.ShouldBe(5);
        kept.ShouldContain("pestera_inventata.zip");
        kept.ShouldContain(n => n.EndsWith(".thconfig", StringComparison.Ordinal));
        (await db.SurveyModels.AsNoTracking().CountAsync(m => m.SurveyUploadId == first)).ShouldBe(0);
        (await db.SurveySources.AsNoTracking().CountAsync(s => s.SurveyUploadId == first && s.Kind == SurveySourceKind.TherionLog)).ShouldBe(0);
        (await db.SurveySources.AsNoTracking().CountAsync(s => s.SurveyUploadId == first && s.Kind == SurveySourceKind.TherionConfig)).ShouldBe(1);
        Names(await client.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{second}")).ShouldBe(secondNames, ignoreOrder: true);
    }

    [Fact]
    public async Task A_newer_version_that_failed_to_compile_does_not_get_an_older_ones_outputs_pruned()
    {
        using var host = WithCompiler(new FakeSurveyCompiler());
        await AuthHelper.CreateUserAsync(host, GlobalRoles.Editor, $"supipe-kf-{suffix}@t.local");
        using var client = await AuthHelper.BearerClientAsync(host, $"supipe-kf-{suffix}@t.local");
        (string, byte[])[] files =
        [
            ("pestera_inventata.zip", InventedArchive()), ("pestera_inventata.th", Fixture("pestera_inventata.th")),
        ];

        var first = await SendAsync(client, $"Kept {suffix}", "pestera_inventata", files);
        await RunPipelineAsync(host, first);
        var firstStatus = await client.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{first}");
        var stored = Names(firstStatus).Count;
        stored.ShouldBeGreaterThan(1);

        // The next version fails to compile: it keeps its log, and nothing it produced.
        using var failing = WithCompiler(new FakeSurveyCompiler(logSaysFailed: true));
        var second = await SendToSurveyAsync(client, firstStatus.GetProperty("caveSurvey").GetProperty("id").GetGuid(), files);
        await Should.ThrowAsync<InvalidOperationException>(RunPipelineAsync(failing, second));
        Names(await client.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{second}")).Count.ShouldBe(1);

        using var keeping = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>(Settings(filesRoot)) { ["SurveyUploads:KeepArtefactVersions"] = "1" },
            JobWorkers.RemoveFrom);
        await SweepAsync(keeping);
        Names(await client.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{first}")).Count.ShouldBe(stored);
    }

    [Fact]
    public async Task Every_output_of_the_catalogue_is_stored_as_what_it_is_under_its_versioned_name()
    {
        using var host = WithCompiler(new FakeSurveyCompiler());
        await AuthHelper.CreateUserAsync(host, GlobalRoles.Editor, $"supipe-all-{suffix}@t.local");
        using var client = await AuthHelper.BearerClientAsync(host, $"supipe-all-{suffix}@t.local");
        (string, byte[])[] files =
        [
            ("pestera_inventata.zip", InventedArchive()), ("pestera_inventata.th", Fixture("pestera_inventata.th")),
            ("pestera_inventata-1p.th2", Fixture("pestera_inventata-1p.th2")),
        ];

        var id = await OpenAsync(
            client, $"Toate {suffix}", "pestera_inventata", null, files, pipeline: new { outputs = SurveyOutputs.Catalogue });
        foreach (var (name, bytes) in files)
        {
            (await PutAsync(client, id, name, bytes)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await client.PostAsync($"/api/v1/survey-uploads/{id}/complete", null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await RunPipelineAsync(host, id);

        var status = await client.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        status.GetProperty("status").GetString().ShouldBe("succeeded", status.ToString());
        var version = status.GetProperty("version").GetDateTimeOffset();
        var artefacts = status.GetProperty("artefacts").EnumerateArray().ToList();
        artefacts.Select(a => a.GetProperty("kind").GetString()).ShouldBe(
            [.. SurveyOutputs.Catalogue, SurveyUploadArtefacts.LogKind], ignoreOrder: true);

        foreach (var artefact in artefacts)
        {
            var output = artefact.GetProperty("kind").GetString()!;
            var name = artefact.GetProperty("name").GetString()!;
            var (model, document, source) = (
                artefact.GetProperty("surveyModelId").ValueKind == JsonValueKind.String,
                artefact.GetProperty("documentId").ValueKind == JsonValueKind.String,
                artefact.GetProperty("sourceId").ValueKind == JsonValueKind.String);
            if (output == SurveyUploadArtefacts.LogKind)
            {
                name.ShouldBe(SurveyUploadArtefacts.LogFileName("pestera_inventata", version));
                (model, document, source).ShouldBe((false, false, true), output);
            }
            else
            {
                var kind = SurveyUploadArtefacts.KindOf(output).ShouldNotBeNull();
                name.ShouldBe(SurveyUploadArtefacts.FileName("pestera_inventata", version, kind));
                (model, document, source).ShouldBe(
                    kind.Storage == SurveyArtefactStorage.Document ? (false, true, false) : (true, false, false), output);
            }

            var bytes = await client.GetByteArrayAsync(artefact.GetProperty("contentUrl").GetString());
            bytes.LongLength.ShouldBe(artefact.GetProperty("sizeBytes").GetInt64(), output);
        }

        // Two models, eleven documents — every one attached to the cave and tagged — and none of
        // the models the cave's current one.
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.SurveyModels.AsNoTracking().Where(m => m.SurveyUploadId == id).Select(m => m.IsCurrent).ToListAsync())
            .ShouldBe([false, false]);
        var tag = await db.Tags.AsNoTracking().SingleAsync(t => t.Slug == SurveyUploadArtefactStore.OutputTagName);
        var documentIds = artefacts.Where(a => a.GetProperty("documentId").ValueKind == JsonValueKind.String)
            .Select(a => a.GetProperty("documentId").GetGuid()).ToList();
        documentIds.Count.ShouldBe(SurveyOutputs.Catalogue.Count - 2);
        var fileIds = await (
            from v in db.DocumentVersions
            join blob in db.StoredFiles on v.Id equals blob.DocumentVersionId
            where documentIds.Contains(v.DocumentId) && v.IsCurrent
            select blob.Id).ToListAsync();
        (await db.Attachments.AsNoTracking().CountAsync(a => fileIds.Contains(a.FileId))).ShouldBe(documentIds.Count);
        (await db.Taggings.AsNoTracking().CountAsync(t => t.TagId == tag.Id && t.EntityId != null && fileIds.Contains(t.EntityId.Value)))
            .ShouldBe(documentIds.Count);
    }

    private static List<string> Names(JsonElement status) =>
        [.. status.GetProperty("artefacts").EnumerateArray().Select(a => a.GetProperty("name").GetString()!)];

    private static async Task SweepAsync(SilexGisApiFactory host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sweep = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.SurveyUploadSweep);
        await sweep.ExecuteAsync(new ProcessingJob { Kind = ProcessingJobKinds.SurveyUploadSweep }, CancellationToken.None);
    }

    [Fact]
    public async Task An_output_the_compiler_did_not_write_is_counted_missing_and_none_written_fails_the_run()
    {
        using var partial = WithCompiler(new FakeSurveyCompiler(false, "rez/upload.3d"));
        await AuthHelper.CreateUserAsync(partial, GlobalRoles.Editor, $"supipe-p-{suffix}@t.local");
        using var client = await AuthHelper.BearerClientAsync(partial, $"supipe-p-{suffix}@t.local");

        var id = await SendAsync(client, $"Partial {suffix}", "pestera_inventata",
            ("pestera_inventata.zip", InventedArchive()), ("pestera_inventata.th", Fixture("pestera_inventata.th")));
        await RunPipelineAsync(partial, id);
        var status = await client.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        status.GetProperty("status").GetString().ShouldBe("succeeded", status.ToString());
        status.GetProperty("message").GetString().ShouldBe(SurveyPipelineNotes.Compiled(3, 4, sketchesDrawn: false));

        // The output it did not write is on the artefact list by itself, with its code and no file.
        var gap = status.GetProperty("artefacts").EnumerateArray().Single(a => a.GetProperty("kind").GetString() == "3d");
        gap.GetProperty("error").GetProperty("code").GetString().ShouldBe(SurveyPipelineFailures.OutputMissing);
        gap.GetProperty("contentUrl").ValueKind.ShouldBe(JsonValueKind.Null);
        gap.GetProperty("sizeBytes").ValueKind.ShouldBe(JsonValueKind.Null);
        gap.GetProperty("surveyModelId").ValueKind.ShouldBe(JsonValueKind.Null);
        status.GetProperty("artefacts").EnumerateArray()
            .Where(a => a.GetProperty("kind").GetString() != "3d")
            .ShouldAllBe(a => a.GetProperty("error").ValueKind == JsonValueKind.Null);

        // Run again with a compiler that writes it: the gap becomes the stored model.
        using var whole = WithCompiler(new FakeSurveyCompiler());
        (await client.PostAsync($"/api/v1/survey-uploads/{id}/rerun", null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await RunPipelineAsync(whole, id);
        var filled = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}"))
            .GetProperty("artefacts").EnumerateArray().Single(a => a.GetProperty("kind").GetString() == "3d");
        filled.GetProperty("error").ValueKind.ShouldBe(JsonValueKind.Null);
        filled.GetProperty("surveyModelId").ValueKind.ShouldBe(JsonValueKind.String);

        using var nothing = WithCompiler(new FakeSurveyCompiler(
            false, "rez/upload.lox", "rez/upload.3d", "rez/upload-plan.pdf", "rez/upload-extended.pdf"));
        var none = await SendAsync(client, $"Nothing {suffix}", "pestera_inventata",
            ("pestera_inventata.zip", InventedArchive()), ("pestera_inventata.th", Fixture("pestera_inventata.th")));
        await Should.ThrowAsync<InvalidOperationException>(RunPipelineAsync(nothing, none));
        var failed = await client.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{none}");
        failed.GetProperty("status").GetString().ShouldBe("failed");
        failed.GetProperty("stage").GetString().ShouldBe("compile");
        failed.GetProperty("error").GetProperty("code").GetString().ShouldBe(SurveyPipelineFailures.OutputMissing);
    }

    [Fact]
    public async Task A_compile_the_log_says_failed_fails_the_run_without_quoting_the_compiler()
    {
        using var host = WithCompiler(new FakeSurveyCompiler(logSaysFailed: true));
        await AuthHelper.CreateUserAsync(host, GlobalRoles.Editor, $"supipe-f-{suffix}@t.local");
        using var client = await AuthHelper.BearerClientAsync(host, $"supipe-f-{suffix}@t.local");

        var id = await SendAsync(client, $"Broken {suffix}", "pestera_inventata",
            ("pestera_inventata.zip", InventedArchive()), ("pestera_inventata.th", Fixture("pestera_inventata.th")));
        await Should.ThrowAsync<InvalidOperationException>(RunPipelineAsync(host, id));

        var status = await client.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        status.GetProperty("status").GetString().ShouldBe("failed");
        status.GetProperty("stage").GetString().ShouldBe("compile");
        var error = status.GetProperty("error");
        error.GetProperty("code").GetString().ShouldBe(SurveyPipelineFailures.CompileFailed);
        error.GetProperty("message").GetString()!.ShouldNotContain("invented reason");
        status.GetProperty("logTail").GetString().ShouldBe("invented tail");

        // A version that did not compile keeps what was compiled — the generated configuration
        // and wrapper, to carry on in Therion — and the whole log, with its compilation record.
        var generated = status.GetProperty("files").EnumerateArray()
            .Where(f => f.GetProperty("role").GetString() == "generatedConfig").Select(f => f.GetProperty("name").GetString()!).ToList();
        generated.Count.ShouldBe(2);
        generated.ShouldContain(n => n.EndsWith(".thconfig", StringComparison.Ordinal));
        var log = status.GetProperty("artefacts").EnumerateArray().ShouldHaveSingleItem();
        log.GetProperty("kind").GetString().ShouldBe(SurveyUploadArtefacts.LogKind);
        (await client.GetStringAsync(log.GetProperty("contentUrl").GetString())).ShouldContain("invented reason");
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.SurveySources.AsNoTracking().CountAsync(s => s.SurveyUploadId == id && s.Kind == SurveySourceKind.TherionConfig)).ShouldBe(1);
        var logSource = await db.SurveySources.AsNoTracking().SingleAsync(s => s.SurveyUploadId == id && s.Kind == SurveySourceKind.TherionLog);
        (await db.SurveyCompilations.AsNoTracking().CountAsync(c => c.SurveySourceId == logSource.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task A_survey_fixed_in_a_coordinate_system_with_no_known_code_is_stored_unread_as_crs_unknown()
    {
        using var host = WithCompiler(new FakeSurveyCompiler { OutputCoordinateSystem = "iJTSK" });
        await AuthHelper.CreateUserAsync(host, GlobalRoles.Editor, $"supipe-crs-{suffix}@t.local");
        using var client = await AuthHelper.BearerClientAsync(host, $"supipe-crs-{suffix}@t.local");

        var id = await SendAsync(client, $"Krovak {suffix}", "pestera_inventata",
            ("pestera_inventata.zip", InventedArchive()), ("pestera_inventata.th", Fixture("pestera_inventata.th")));
        await RunPipelineAsync(host, id);

        var status = await client.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        status.GetProperty("status").GetString().ShouldBe("succeeded", status.ToString());
        var message = status.GetProperty("message").GetString()!;
        message.ShouldEndWith(SurveyPipelineNotes.ModelCrsUnknown);
        message.ShouldNotContain(SurveyPipelineNotes.ModelUnplaced.Trim());
        var lox = status.GetProperty("artefacts").EnumerateArray().Single(a => a.GetProperty("kind").GetString() == "lox");
        lox.GetProperty("error").GetProperty("code").GetString().ShouldBe(SurveyPipelineFailures.CrsUnknown);
        lox.GetProperty("modelStatus").GetString().ShouldBe("failed");
        lox.GetProperty("contentUrl").ValueKind.ShouldBe(JsonValueKind.String);

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.AsNoTracking().SingleAsync(m => m.Id == lox.GetProperty("surveyModelId").GetGuid());
        model.SourceEpsg.ShouldBeNull();
        model.ProcessingError!.ShouldContain("coordinate system");
        (await db.ProcessingJobs.AsNoTracking().Where(j => j.Kind == ProcessingJobKinds.SurveyGraph).ToListAsync())
            .Count(j => JsonSerializer.Deserialize<SurveyGraphPayload>(j.Payload, JsonSerializerOptions.Web)!.SurveyModelId == model.Id)
            .ShouldBe(0);
    }

    [Fact]
    public async Task A_compile_asked_for_without_a_survey_file_or_reading_outside_the_upload_is_refused_before_compiling()
    {
        var compiler = new FakeSurveyCompiler();
        using var host = WithCompiler(compiler);
        await AuthHelper.CreateUserAsync(host, GlobalRoles.Editor, $"supipe-r-{suffix}@t.local");
        using var client = await AuthHelper.BearerClientAsync(host, $"supipe-r-{suffix}@t.local");

        var bare = await SendAsync(client, $"Bare {suffix}", "pestera_inventata", ("pestera_inventata.zip", InventedArchive()));
        await Should.ThrowAsync<InvalidOperationException>(RunPipelineAsync(host, bare));
        var missing = await client.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{bare}");
        missing.GetProperty("status").GetString().ShouldBe("failed");
        missing.GetProperty("stage").GetString().ShouldBe("scaffold");
        missing.GetProperty("error").GetProperty("code").GetString().ShouldBe(SurveyPipelineFailures.SourceMissing);

        var escaping = System.Text.Encoding.UTF8.GetBytes(
            System.Text.Encoding.UTF8.GetString(Fixture("pestera_inventata.th"))
                .Replace("endsurvey", "  input \"/etc/hostname\"\nendsurvey", StringComparison.Ordinal));
        var crafted = await SendAsync(client, $"Crafted {suffix}", "pestera_inventata",
            ("pestera_inventata.zip", InventedArchive()), ("pestera_inventata.th", escaping));
        await Should.ThrowAsync<InvalidOperationException>(RunPipelineAsync(host, crafted));
        var refused = await client.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{crafted}");
        refused.GetProperty("status").GetString().ShouldBe("failed");
        refused.GetProperty("stage").GetString().ShouldBe("scaffold");
        var error = refused.GetProperty("error");
        error.GetProperty("code").GetString().ShouldBe(SurveyPipelineFailures.SourceInvalid);
        error.GetProperty("message").GetString()!.ShouldStartWith("pestera_inventata.th, line ");
        error.GetProperty("message").GetString()!.ShouldNotContain("/etc/hostname");

        compiler.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task The_sweep_closes_uploads_that_stopped_receiving_and_keeps_what_arrived()
    {
        var expired = await OpenAsync(owner, $"Expired {suffix}", "expired", ("expired.zip", InventedArchive()), ("expired.th", "survey expired\nendsurvey\n"u8.ToArray()));
        (await PutAsync(owner, expired, "expired.zip", InventedArchive())).StatusCode.ShouldBe(HttpStatusCode.OK);
        var waiting = await OpenAsync(owner, $"Waiting {suffix}", "waiting", ("waiting.zip", InventedArchive()));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.SurveyUploads.Where(u => u.Id == expired)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));

        var sweep = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.SurveyUploadSweep);
        await sweep.ExecuteAsync(new ProcessingJob { Kind = ProcessingJobKinds.SurveyUploadSweep }, CancellationToken.None);

        var closed = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{expired}");
        closed.GetProperty("status").GetString().ShouldBe("abandoned");
        closed.GetProperty("finishedAt").ValueKind.ShouldBe(JsonValueKind.String);
        closed.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("status").GetString())
            .ShouldBe(["stored", "declared"], ignoreOrder: true);
        var batchId = (await db.SurveyUploads.AsNoTracking().SingleAsync(u => u.Id == expired)).UploadBatchId;
        (await db.UploadBatches.AsNoTracking().SingleAsync(b => b.Id == batchId)).Status.ShouldBe(UploadBatchStatus.Completed);

        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{waiting}"))
            .GetProperty("status").GetString().ShouldBe("receiving");

        // Running it again closes nothing twice.
        await sweep.ExecuteAsync(new ProcessingJob { Kind = ProcessingJobKinds.SurveyUploadSweep }, CancellationToken.None);
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{waiting}"))
            .GetProperty("status").GetString().ShouldBe("receiving");
    }

    // ---- helpers ----

    /// <summary>Opens an upload with these files, sends them all, and completes it.</summary>
    private static async Task<Guid> SendAsync(HttpClient client, string title, string surveyName, params (string Name, byte[] Bytes)[] files)
    {
        var id = await OpenAsync(client, title, surveyName, files);
        foreach (var (name, bytes) in files)
        {
            var put = await PutAsync(client, id, name, bytes);
            put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        }

        var complete = await client.PostAsync($"/api/v1/survey-uploads/{id}/complete", null);
        complete.StatusCode.ShouldBe(HttpStatusCode.Accepted, await complete.Content.ReadAsStringAsync());
        return id;
    }

    /// <summary>Opens a further version of a survey, by the id the server answered for it, sends the files and completes it.</summary>
    private static async Task<Guid> SendToSurveyAsync(HttpClient client, Guid caveSurveyId, params (string Name, byte[] Bytes)[] files)
    {
        var id = await OpenAsync(client, "Pestera Inventata, again", "pestera_inventata", caveSurveyId, files);
        foreach (var (name, bytes) in files)
        {
            (await PutAsync(client, id, name, bytes)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await client.PostAsync($"/api/v1/survey-uploads/{id}/complete", null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        return id;
    }

    private static Task<Guid> OpenAsync(HttpClient client, string title, string surveyName, params (string Name, byte[] Bytes)[] files) =>
        OpenAsync(client, title, surveyName, null, files);

    private static async Task<Guid> OpenAsync(
        HttpClient client,
        string title,
        string surveyName,
        Guid? caveSurveyId,
        (string Name, byte[] Bytes)[] files,
        object? newCave = null,
        object? pipeline = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/survey-uploads", new
        {
            source = "topodroid",
            caveSurvey = caveSurveyId is { } survey ? new { id = survey } : null,
            newCave,
            pipeline,
            client = new { name = "TopoDroid", version = "6.5.34" },
            surveyTitle = title,
            surveyName,
            files = files.Select(f => new
            {
                name = f.Name,
                size = f.Bytes.LongLength,
                role = f.Name.EndsWith(".zip", StringComparison.Ordinal) ? "archive"
                    : f.Name.EndsWith(".th2", StringComparison.Ordinal) ? "therionSketch"
                    : "therionSource",
            }),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, string name, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return client.PutAsync($"/api/v1/survey-uploads/{id}/files/{Uri.EscapeDataString(name)}", content);
    }

    /// <summary>Runs the processing job the upload's completion or rerun queued, exactly once.</summary>
    private static async Task RunPipelineAsync(SilexGisApiFactory host, Guid uploadId)
    {
        long jobId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var queued = await db.ProcessingJobs.AsNoTracking()
                .Where(j => j.Kind == ProcessingJobKinds.SurveyUploadPipeline && j.Status == ProcessingJobStatus.Queued)
                .ToListAsync();
            jobId = queued.Single(j =>
                JsonSerializer.Deserialize<SurveyUploadPipelinePayload>(j.Payload, JsonSerializerOptions.Web)!.SurveyUploadId == uploadId).Id;
        }

        await QueuedJob.RunAsync(host.Services, jobId);
    }
}
