// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The two ways a club's existing archive arrives without four hundred drag-and-drops: an
/// uploaded archive expanded on the server, and a directory the server itself can reach.
///
/// <para>
/// Both are hostile input in the same way — the structure is written by somebody else and read
/// by the server onto its own disk — so the assertions here are mostly about what is refused.
/// Every one of them is paired with the near-identical shape that must be allowed, because the
/// failure mode of a path guard is not "an attack got through" but "somebody tightened it until
/// ordinary archives stopped importing and nobody noticed until a club handed one over".
/// </para>
/// </summary>
public sealed class BulkImportTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string importRoot;
    private readonly string outsideRoot;
    private readonly string connectionString;

    private HttpClient admin = null!;   // full administrator — the only one who may read the disk
    private HttpClient editor = null!;  // Editor — uploads and files, but not from the disk

    public BulkImportTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        var suffix = Guid.NewGuid().ToString("N");
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-bulk-{suffix}");
        importRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-import-{suffix}");
        outsideRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-outside-{suffix}");
        Directory.CreateDirectory(importRoot);
        Directory.CreateDirectory(outsideRoot);

        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            ["Files:ImportRoots:0"] = importRoot,
            // These tests queue a job and then read it back to run its handler themselves. The
            // background worker polls the same table every couple of seconds, so with it running
            // this is a race: when the worker claims the row first, the read finds no queued job
            // and the test fails with "sequence contains no elements". It needs the poll to land
            // inside that gap, so it passes on a quiet machine and fails under load — which is why
            // it has been rediscovered and re-diagnosed several times rather than fixed.
            //
            // Switched off here rather than for every test, because most classes rely on the
            // worker doing its job: nine of them read back what it produced, and turning it off
            // globally fails 52 tests.
            ["Jobs:PollSeconds"] = "0",
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"bulk-adm-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"bulk-adm-{suffix}@t.local");

        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"bulk-ed-{suffix}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"bulk-ed-{suffix}@t.local");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        admin?.Dispose();
        editor?.Dispose();
        factory.Dispose();
        foreach (var directory in new[] { filesRoot, importRoot, outsideRoot })
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task An_uploaded_archive_becomes_filed_documents_under_the_folders_it_names()
    {
        var root = await CreateCabinetAsync($"Club {Guid.NewGuid():N}"[..24]);
        var run = Guid.NewGuid();
        var archive = Zip(
            ("1987/bulletins/march.txt", $"the march bulletin {run}"),
            ("1987/bulletins/april.txt", $"the april bulletin {run}"),
            ("1988/notes.txt", $"some notes {run}"));

        var batchId = await ExpandAsync(archive, root);

        var batch = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/upload-batches/{batchId}");
        batch.GetProperty("status").GetString().ShouldBe("completed");
        batch.GetProperty("storedCount").GetInt32().ShouldBe(3);
        batch.GetProperty("failedCount").GetInt32().ShouldBe(0);

        // The folder scheme somebody spent years maintaining survives as the filing tree.
        var tree = await TreeAsync();
        var year = tree.Single(c => c.ParentId == root && c.Name == "1987");
        var bulletins = tree.Single(c => c.ParentId == year.Id && c.Name == "bulletins");
        (await FiledOnAsync(bulletins.Id)).Count.ShouldBe(2);
        tree.ShouldContain(c => c.ParentId == root && c.Name == "1988");
    }

    [Fact]
    public async Task An_archives_report_names_every_file_so_a_failure_is_something_to_act_on()
    {
        // Bodies are unique per run: these suites share one database, so identical content in
        // two tests would be recognised as a duplicate — correctly — and skipped.
        var archive = Zip(("survey.txt", $"a survey {Guid.NewGuid()}"), ("empty.txt", string.Empty));
        var batchId = await ExpandAsync(archive, cabinetId: null);

        var items = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/upload-batches/{batchId}/items");
        var lines = items.GetProperty("items").EnumerateArray()
            .ToDictionary(i => i.GetProperty("sourcePath").GetString()!, i => i);

        lines["survey.txt"].GetProperty("outcome").GetString().ShouldBe("stored");

        // "12 of 500 failed" is useless; a named entry with a reason is a thing somebody can
        // go and look at.
        lines["empty.txt"].GetProperty("outcome").GetString().ShouldBe("skipped");
        lines["empty.txt"].GetProperty("reason").GetString().ShouldBe(UploadItemReasons.Empty);
    }

    [Fact]
    public async Task An_archive_entry_naming_somewhere_outside_itself_is_refused_and_the_rest_still_land()
    {
        var archive = Zip(
            ("../../etc/passwd", $"root:x:0:0 {Guid.NewGuid()}"),
            ("survey.txt", $"a survey {Guid.NewGuid()}"));
        var batchId = await ExpandAsync(archive, cabinetId: null);

        var items = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/upload-batches/{batchId}/items");
        var lines = items.GetProperty("items").EnumerateArray()
            .ToDictionary(i => i.GetProperty("sourcePath").GetString()!, i => i);

        lines["../../etc/passwd"].GetProperty("outcome").GetString().ShouldBe("skipped");
        lines["../../etc/passwd"].GetProperty("reason").GetString().ShouldBe(UploadItemReasons.PathRefused);

        // One refused entry does not abandon the archive: an import that stopped dead on the
        // first bad name would have to be restarted by hand, re-importing everything that
        // already worked.
        lines["survey.txt"].GetProperty("outcome").GetString().ShouldBe("stored");
    }

    [Fact]
    public async Task A_second_copy_of_the_same_content_in_one_archive_is_skipped_and_named()
    {
        var body = $"the same survey {Guid.NewGuid()}";
        var archive = Zip(("1987/survey.txt", body), ("1988/survey-copy.txt", body));

        var batchId = await ExpandAsync(archive, cabinetId: null);

        var batch = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/upload-batches/{batchId}");
        batch.GetProperty("storedCount").GetInt32().ShouldBe(1);
        batch.GetProperty("skippedCount").GetInt32().ShouldBe(1);

        var items = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/upload-batches/{batchId}/items");
        var skipped = items.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("outcome").GetString() == "skipped");
        skipped.GetProperty("reason").GetString().ShouldBe(UploadItemReasons.Duplicate);

        // A bulk source cannot answer a warning — there is nobody at the other end — so the
        // second copy is named in the report rather than stored twice.
        skipped.GetProperty("duplicateOfDocumentId").ValueKind.ShouldNotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task An_archive_expanding_past_its_ceiling_is_abandoned_with_a_reason()
    {
        var tinyRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-tiny-{Guid.NewGuid():N}");
        using var tinyFactory = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = tinyRoot,
                ["Keys:Path"] = Path.Combine(tinyRoot, "keys"),
                ["Files:MaxArchiveExpandedBytes"] = "40",
                // Same reason as the shared factory above: this test drives the handler itself.
                ["Jobs:PollSeconds"] = "0",
            });

        var email = $"tiny-{Guid.NewGuid():N}"[..14] + "@t.local";
        await AuthHelper.CreateUserAsync(tinyFactory, GlobalRoles.Admin, email);
        using var client = await AuthHelper.BearerClientAsync(tinyFactory, email);

        try
        {
            var archive = Zip(("a.txt", new string('a', 100)));
            var batchId = await ExpandAsync(archive, cabinetId: null, client: client, api: tinyFactory);

            var batch = await client.GetFromJsonAsync<JsonElement>($"/api/v1/upload-batches/{batchId}");

            // Abandoned rather than quietly truncated: an expansion that dropped the hostile
            // part and reported success would leave somebody believing their archive was filed.
            batch.GetProperty("status").GetString().ShouldBe("failed");
            batch.GetProperty("error").GetString().ShouldBeOneOf(
                ArchiveExpansionRules.TooLargeCode, ArchiveExpansionRules.BombCode);
        }
        finally
        {
            if (Directory.Exists(tinyRoot))
            {
                Directory.Delete(tinyRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task A_directory_the_operator_allowed_is_imported_with_its_folders_mirrored()
    {
        Directory.CreateDirectory(Path.Combine(importRoot, "1987", "bulletins"));
        var run = Guid.NewGuid();
        await File.WriteAllTextAsync(
            Path.Combine(importRoot, "1987", "bulletins", "march.txt"), $"march {run}");
        await File.WriteAllTextAsync(Path.Combine(importRoot, "notes.txt"), $"notes {run}");

        var root = await CreateCabinetAsync($"Club {Guid.NewGuid():N}"[..24]);
        var batchId = await ImportDirectoryAsync(importRoot, root);

        var batch = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/upload-batches/{batchId}");
        batch.GetProperty("status").GetString().ShouldBe("completed");
        batch.GetProperty("storedCount").GetInt32().ShouldBe(2);

        var tree = await TreeAsync();
        var year = tree.Single(c => c.ParentId == root && c.Name == "1987");
        tree.ShouldContain(c => c.ParentId == year.Id && c.Name == "bulletins");

        // The source is never touched: an import that goes wrong is undone by deleting what it
        // created, not by hoping the original is still there.
        File.Exists(Path.Combine(importRoot, "notes.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task A_directory_outside_the_configured_roots_is_refused_however_privileged_the_caller()
    {
        await File.WriteAllTextAsync(Path.Combine(outsideRoot, "secret.txt"), "secret");

        var refused = await admin.PostAsJsonAsync(
            "/api/v1/upload-batches/import-directory", new { path = outsideRoot });
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(refused)).ShouldBe(ServerImportPaths.OutsideRootsCode);

        // The allowed root is accepted by the same caller, so the refusal is the allow-list
        // rather than the route being closed.
        (await admin.PostAsJsonAsync("/api/v1/upload-batches/import-directory", new { path = importRoot }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_traversal_out_of_an_allowed_root_is_refused()
    {
        var escape = Path.Combine(importRoot, "..", Path.GetFileName(outsideRoot));

        var refused = await admin.PostAsJsonAsync(
            "/api/v1/upload-batches/import-directory", new { path = escape });
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(refused)).ShouldBe(ServerImportPaths.OutsideRootsCode);
    }

    [Fact]
    public async Task Reading_the_servers_own_disk_takes_more_than_being_able_to_upload()
    {
        // An Editor uploads and files all day and still may not make the server read its own
        // disk: this is a question about the machine, not about any content on it.
        (await editor.PostAsJsonAsync("/api/v1/upload-batches/import-directory", new { path = importRoot }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await editor.GetAsync("/api/v1/upload-batches/import-roots"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await admin.GetAsync("/api/v1/upload-batches/import-roots")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_configured_roots_are_published_so_a_page_can_offer_them()
    {
        var config = await admin.GetFromJsonAsync<JsonElement>("/api/v1/upload-batches/import-roots");
        config.GetProperty("roots").EnumerateArray()
            .Select(r => r.GetString())
            .ShouldContain(Path.GetFullPath(importRoot));
    }

    private sealed record CabinetNode(Guid Id, Guid? ParentId, string Name);

    private async Task<List<CabinetNode>> TreeAsync()
    {
        var tree = await admin.GetFromJsonAsync<JsonElement>("/api/v1/cabinets");
        return
        [
            .. tree.EnumerateArray().Select(c => new CabinetNode(
                c.GetProperty("id").GetGuid(),
                c.GetProperty("parentId").ValueKind == JsonValueKind.Null
                    ? null
                    : c.GetProperty("parentId").GetGuid(),
                c.GetProperty("name").GetString()!)),
        ];
    }

    private async Task<List<Guid>> FiledOnAsync(Guid cabinetId)
    {
        var page = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/cabinets/{cabinetId}/documents");
        return [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())];
    }

    private async Task<Guid> CreateCabinetAsync(string name)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/cabinets", new
        {
            name,
            description = (string?)null,
            parentId = (Guid?)null,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Uploads an archive asking for it to be expanded, then runs the queued walk to
    /// completion. The queue is deliberately not started in tests, so the handler is invoked
    /// directly — the same object the worker would resolve.
    /// </summary>
    private async Task<Guid> ExpandAsync(
        byte[] archive, Guid? cabinetId, HttpClient? client = null, SilexGisApiFactory? api = null)
    {
        client ??= admin;
        api ??= factory;

        var query = cabinetId is { } id ? $"?expandArchive=true&cabinetId={id}" : "?expandArchive=true";

        HttpResponseMessage response;
        var content = new ByteArrayContent(archive);
        content.Headers.ContentType = new("application/zip");
        using (var form = new MultipartFormDataContent { { content, "file", "club-archive.zip" } })
        {
            // Awaited inside the using: the form must outlive the request body being read.
            response = await client.PostAsync($"/api/v1/files/{query}", form);
        }

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await RunQueuedAsync(api, ProcessingJobKinds.ArchiveExpansion);
    }

    private async Task<Guid> ImportDirectoryAsync(string path, Guid? cabinetId)
    {
        var response = await admin.PostAsJsonAsync(
            "/api/v1/upload-batches/import-directory", new { path, cabinetId });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);

        var batchId = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
        await RunQueuedAsync(factory, ProcessingJobKinds.DirectoryImport);
        return batchId;
    }

    /// <summary>Runs the newest queued job of a kind and answers the batch it wrote into.</summary>
    private static async Task<Guid> RunQueuedAsync(SilexGisApiFactory api, string kind)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var job = await db.ProcessingJobs
            .Where(j => j.Kind == kind && j.Status == ProcessingJobStatus.Queued)
            .OrderByDescending(j => j.Id)
            .FirstAsync();

        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>().Single(h => h.Kind == kind);
        await handler.ExecuteAsync(job, CancellationToken.None);

        job.Status = ProcessingJobStatus.Succeeded;
        await db.SaveChangesAsync();

        return JsonDocument.Parse(job.Payload).RootElement.GetProperty("batchId").GetGuid();
    }

    /// <summary>An in-memory archive with the given entries.</summary>
    private static byte[] Zip(params (string Name, string Body)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, body) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
                using var stream = entry.Open();
                stream.Write(Encoding.UTF8.GetBytes(body));
            }
        }

        return buffer.ToArray();
    }

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
