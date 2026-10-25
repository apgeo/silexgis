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
/// A survey upload's processing run through the stages this server has — the archive check and
/// the reading of the survey archive — as a device sees it on the status route; and the sweep
/// that closes uploads whose device stopped sending.
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

        // A rerun starts over from the top and lands the same way, keeping the first start time.
        var started = status.GetProperty("startedAt").GetDateTimeOffset();
        (await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{id}/rerun", new { pipeline = new { compile = false } }))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await RunPipelineAsync(factory, id);
        var again = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        again.GetProperty("status").GetString().ShouldBe("succeeded");
        again.GetProperty("message").GetString().ShouldBe(SurveyPipelineNotes.NotAsked);
        again.GetProperty("startedAt").GetDateTimeOffset().ShouldBe(started);
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
    public async Task With_a_compiler_the_server_says_so_and_this_run_still_stops_after_reading_the_archive()
    {
        var compiler = new FakeSurveyCompiler("rez/inventata.lox");
        using var withCompiler = new SilexGisApiFactory(
            postgres.ConnectionString,
            Settings(filesRoot),
            services =>
            {
                JobWorkers.RemoveFrom(services);
                services.Replace(ServiceDescriptor.Singleton<ISurveyCompiler>(compiler));
            });
        await AuthHelper.CreateUserAsync(withCompiler, GlobalRoles.Editor, $"supipe-c-{suffix}@t.local");
        using var client = await AuthHelper.BearerClientAsync(withCompiler, $"supipe-c-{suffix}@t.local");

        var capabilities = await client.GetFromJsonAsync<JsonElement>("/api/v1/survey-uploads/capabilities");
        var advertised = capabilities.GetProperty("compiler");
        advertised.GetProperty("available").GetBoolean().ShouldBeTrue();
        advertised.GetProperty("version").GetString().ShouldBe(FakeSurveyCompiler.FakeVersion);
        advertised.GetProperty("outputs").EnumerateArray().Select(o => o.GetString()).ShouldBe(SurveyOutputs.Catalogue);

        var id = await SendAsync(client, $"Compiled {suffix}", "compiled", ("compiled.zip", InventedArchive()));
        await RunPipelineAsync(withCompiler, id);

        var status = await client.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        status.GetProperty("status").GetString().ShouldBe("succeeded");
        status.GetProperty("message").GetString().ShouldBe(SurveyPipelineNotes.CompileNotBuilt);
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

    private static async Task<Guid> OpenAsync(HttpClient client, string title, string surveyName, params (string Name, byte[] Bytes)[] files)
    {
        var response = await client.PostAsJsonAsync("/api/v1/survey-uploads", new
        {
            source = "topodroid",
            client = new { name = "TopoDroid", version = "6.5.34" },
            surveyTitle = title,
            surveyName,
            files = files.Select(f => new
            {
                name = f.Name,
                size = f.Bytes.LongLength,
                role = f.Name.EndsWith(".zip", StringComparison.Ordinal) ? "archive" : "therionSource",
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
