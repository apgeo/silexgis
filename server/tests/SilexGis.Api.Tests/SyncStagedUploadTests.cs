// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Bytes a phone sends ahead of the sync upload that names them: the transfer opened to stage,
/// the completion that describes the content and makes no document, the retention that sweeps
/// what no upload adopted, and the quota that counts it all meanwhile.
/// </summary>
/// <remarks>
/// Adoption itself — an upload's document row naming the staged bytes — belongs to the upload's
/// documents section and is proved beside it. What is proved here is what the phone can rely on
/// before it sends that row: the hash it will be asked to state, how long the bytes wait, and that
/// nobody else can see or take them.
/// </remarks>
public sealed class SyncStagedUploadTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string connectionString;

    private HttpClient phone = null!;
    private HttpClient admin = null!;
    private HttpClient viewer = null!;

    public SyncStagedUploadTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-staged-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async ValueTask InitializeAsync()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"stg-ph-{marker}@t.local");
        phone = await AuthHelper.BearerClientAsync(factory, $"stg-ph-{marker}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"stg-adm-{marker}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"stg-adm-{marker}@t.local");

        // A Viewer holds no right to create documents anywhere, so it is the account that may not
        // stage — and, since it is not the phone's account, the one that must not see its bytes.
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"stg-vw-{marker}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"stg-vw-{marker}@t.local");
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        phone?.Dispose();
        admin?.Dispose();
        viewer?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Staged_bytes_are_described_and_wait_without_becoming_a_document()
    {
        var bytes = Corpus.GeotaggedJpeg(45.123, 25.456);
        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var documentsBefore = await CountAsync(db => db.Documents.IgnoreQueryFilters().CountAsync());

        var session = await OpenStagedAsync(phone, "IMG_0001.jpg", bytes.Length);

        // While the bytes are still arriving the transfer is an ordinary one.
        var arriving = await phone.GetFromJsonAsync<JsonElement>($"/api/v1/files/uploads/{session}");
        arriving.GetProperty("stagedForSync").GetBoolean().ShouldBeTrue();
        arriving.GetProperty("stagedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        arriving.GetProperty("sha256").ValueKind.ShouldBe(JsonValueKind.Null);

        await AppendAsync(phone, session, 0, bytes);
        var completed = await phone.PostAsync($"/api/v1/files/uploads/{session}/complete", null);
        var payload = await completed.Content.ReadAsStringAsync();
        completed.StatusCode.ShouldBe(HttpStatusCode.OK, payload);

        var staged = JsonDocument.Parse(payload).RootElement;
        staged.GetProperty("id").GetGuid().ShouldBe(session);
        staged.GetProperty("fileName").GetString().ShouldBe("IMG_0001.jpg");
        staged.GetProperty("sizeBytes").GetInt64().ShouldBe(bytes.Length);
        staged.GetProperty("sha256").GetString().ShouldBe(expectedHash);

        // Typed by what the bytes are, as every upload is.
        staged.GetProperty("mimeType").GetString().ShouldBe("image/jpeg");
        staged.GetProperty("kind").GetString().ShouldBe("image");

        // The default retention, counted from the moment of staging.
        var stagedAt = staged.GetProperty("stagedAt").GetDateTimeOffset();
        staged.GetProperty("expiresAt").GetDateTimeOffset().ShouldBe(stagedAt + TimeSpan.FromDays(7), TimeSpan.FromSeconds(1));

        // No document, no stored file: the bytes are nobody's document until an upload adopts them.
        (await CountAsync(db => db.Documents.IgnoreQueryFilters().CountAsync())).ShouldBe(documentsBefore);
        (await CountAsync(db => db.StoredFiles.CountAsync(f => f.Sha256 == expectedHash))).ShouldBe(0);

        // The row stays, describing the bytes, and the bytes stay where they landed.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var row = await db.UploadSessions.AsNoTracking().SingleAsync(s => s.Id == session);
            row.StagedAt.ShouldNotBeNull();
            row.Sha256.ShouldBe(expectedHash);
            row.Kind.ShouldBe(FileKind.Image);
            var stored = scope.ServiceProvider.GetRequiredService<IFileStore>().GetAbsolutePath(row.StoragePath);
            (await File.ReadAllBytesAsync(stored)).ShouldBe(bytes);
        }

        // The status answers the same hash, so a phone can check its bytes are still waiting.
        var status = await phone.GetFromJsonAsync<JsonElement>($"/api/v1/files/uploads/{session}");
        status.GetProperty("sha256").GetString().ShouldBe(expectedHash);
        status.GetProperty("stagedAt").GetDateTimeOffset().ShouldBe(stagedAt, TimeSpan.FromMilliseconds(1));

        // A phone that lost the answer asks again and is told the same thing; the wait does not move.
        var again = await phone.PostAsync($"/api/v1/files/uploads/{session}/complete", null);
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        var repeated = await ReadJsonAsync(again);
        repeated.GetProperty("sha256").GetString().ShouldBe(expectedHash);
        repeated.GetProperty("expiresAt").GetDateTimeOffset()
            .ShouldBe(staged.GetProperty("expiresAt").GetDateTimeOffset(), TimeSpan.FromMilliseconds(1));

        // A further piece is no way to change bytes that were already described.
        var extra = await SendChunkAsync(phone, session, bytes.Length, [1, 2, 3]);
        extra.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(extra)).ShouldBe("upload.size_mismatch");
    }

    [Fact]
    public async Task The_retention_is_the_sync_setting_in_force_when_the_bytes_were_staged()
    {
        (await admin.PutAsJsonAsync("/api/v1/admin/settings/sync", SyncSettings(stagedDays: 2)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        try
        {
            var staged = await StageAsync(phone, "IMG_0002.bin", RandomNumberGenerator.GetBytes(300));
            staged.GetProperty("expiresAt").GetDateTimeOffset()
                .ShouldBe(staged.GetProperty("stagedAt").GetDateTimeOffset() + TimeSpan.FromDays(2), TimeSpan.FromSeconds(1));
        }
        finally
        {
            (await admin.PutAsJsonAsync("/api/v1/admin/settings/sync", SyncSettings(stagedDays: 7)))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Staging_needs_an_account_and_a_right_to_create_documents_and_names_no_destination()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/v1/files/uploads", StagedOpen("a.jpg", 10)))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The Viewer may not create a document anywhere, so it may not park bytes for one either;
        // the Editor beside it, asking the same thing, may.
        var refused = await viewer.PostAsJsonAsync("/api/v1/files/uploads", StagedOpen("a.jpg", 10));
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadCodeAsync(refused)).ShouldBe("access.create_forbidden");
        (await phone.PostAsJsonAsync("/api/v1/files/uploads", StagedOpen("a.jpg", 10)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // A destination on staged bytes is refused rather than silently dropped.
        foreach (var (name, value) in new (string, object)[]
                 {
                     ("cabinetId", Guid.NewGuid()),
                     ("relativePath", "Expedition/day1"),
                     ("attachEntityType", "feature"),
                     ("batchId", Guid.NewGuid()),
                     ("cavingGroupId", Guid.NewGuid()),
                 })
        {
            var body = StagedOpen("b.jpg", 10);
            body[name] = value;
            if (name == "attachEntityType")
            {
                body["attachEntityId"] = Guid.NewGuid();
            }

            var invalid = await phone.PostAsJsonAsync("/api/v1/files/uploads", body);
            invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest, name);
        }

        // The quota and the type rules hold for staging as for any upload: an empty file is not one.
        (await phone.PostAsJsonAsync("/api/v1/files/uploads", StagedOpen("c.jpg", 0)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Somebody_elses_staged_bytes_are_answered_as_absent()
    {
        var staged = await StageAsync(phone, "IMG_0003.bin", RandomNumberGenerator.GetBytes(200));
        var id = staged.GetProperty("id").GetGuid();

        // Not forbidden: confirming the id exists would say that somebody is sending a file.
        foreach (var response in new[]
                 {
                     await admin.GetAsync($"/api/v1/files/uploads/{id}"),
                     await admin.PostAsync($"/api/v1/files/uploads/{id}/complete", null),
                     await admin.DeleteAsync($"/api/v1/files/uploads/{id}"),
                 })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await ReadCodeAsync(response)).ShouldBe("upload.session_not_found");
        }

        // Still the phone's, untouched by the attempts.
        (await phone.GetAsync($"/api/v1/files/uploads/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The phone may withdraw its own, and the bytes go with the row.
        string absolutePath;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var row = await db.UploadSessions.AsNoTracking().SingleAsync(s => s.Id == id);
            absolutePath = scope.ServiceProvider.GetRequiredService<IFileStore>().GetAbsolutePath(row.StoragePath);
        }

        File.Exists(absolutePath).ShouldBeTrue();
        (await phone.DeleteAsync($"/api/v1/files/uploads/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        File.Exists(absolutePath).ShouldBeFalse();
    }

    [Fact]
    public async Task Unadopted_staged_bytes_are_swept_after_their_retention_and_waiting_ones_are_kept()
    {
        var expired = (await StageAsync(phone, "IMG_0004.bin", RandomNumberGenerator.GetBytes(250))).GetProperty("id").GetGuid();
        var waiting = (await StageAsync(phone, "IMG_0005.bin", RandomNumberGenerator.GetBytes(260))).GetProperty("id").GetGuid();

        string expiredPath;
        string waitingPath;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var store = scope.ServiceProvider.GetRequiredService<IFileStore>();

            // The retention ran out for one of them; standing in for a week passing.
            await db.UploadSessions.Where(s => s.Id == expired)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));

            var rows = await db.UploadSessions.AsNoTracking().Where(s => s.Id == expired || s.Id == waiting).ToListAsync();
            expiredPath = store.GetAbsolutePath(rows.Single(r => r.Id == expired).StoragePath);
            waitingPath = store.GetAbsolutePath(rows.Single(r => r.Id == waiting).StoragePath);
        }

        // Past its retention the upload is answered as gone even before the sweep collects it,
        // so a phone is never allowed to build on bytes that are about to vanish.
        (await phone.GetAsync($"/api/v1/files/uploads/{expired}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await RunSweepAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.UploadSessions.AnyAsync(s => s.Id == expired)).ShouldBeFalse();
            (await db.UploadSessions.AnyAsync(s => s.Id == waiting)).ShouldBeTrue();
        }

        File.Exists(expiredPath).ShouldBeFalse();
        File.Exists(waitingPath).ShouldBeTrue();
        (await phone.GetAsync($"/api/v1/files/uploads/{waiting}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Staged_bytes_count_against_the_quota_from_the_moment_the_transfer_opens()
    {
        var quotaRoot = Path.Combine(TestScratch.Root, $"silexgis-test-stagequota-{Guid.NewGuid():N}");
        using var quotaFactory = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = quotaRoot,
                ["Keys:Path"] = Path.Combine(quotaRoot, "keys"),
                ["Files:DefaultUserQuotaBytes"] = "600",
            });

        var email = $"stq-{Guid.NewGuid():N}"[..16] + "@t.local";
        await AuthHelper.CreateUserAsync(quotaFactory, GlobalRoles.Editor, email);
        using var client = await AuthHelper.BearerClientAsync(quotaFactory, email);

        try
        {
            (await RemainingAsync(client)).ShouldBe(600);

            // Announced, not yet sent: the room is already spoken for.
            var opened = await OpenStagedAsync(client, "IMG_0006.bin", 400);
            (await RemainingAsync(client)).ShouldBe(200);

            var tooLarge = await client.PostAsJsonAsync("/api/v1/files/uploads", StagedOpen("IMG_0007.bin", 300));
            tooLarge.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await ReadCodeAsync(tooLarge)).ShouldBe("file.quota_exceeded");

            // A browser upload is measured against the same room.
            var browser = await PostUploadAsync(client, "notes.txt", RandomNumberGenerator.GetBytes(300));
            browser.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await ReadCodeAsync(browser)).ShouldBe("file.quota_exceeded");

            // Completed and waiting, it still counts.
            await AppendAsync(client, opened, 0, RandomNumberGenerator.GetBytes(400));
            (await client.PostAsync($"/api/v1/files/uploads/{opened}/complete", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await RemainingAsync(client)).ShouldBe(200);

            // Withdrawn, the room comes back.
            (await client.DeleteAsync($"/api/v1/files/uploads/{opened}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await RemainingAsync(client)).ShouldBe(600);
        }
        finally
        {
            if (Directory.Exists(quotaRoot))
            {
                Directory.Delete(quotaRoot, recursive: true);
            }
        }
    }

    private async Task RunSweepAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = new UploadSessionSweepHandler(
            scope.ServiceProvider.GetRequiredService<SilexGisDbContext>(),
            scope.ServiceProvider.GetRequiredService<IFileStore>(),
            NullLogger<UploadSessionSweepHandler>.Instance);
        await handler.ExecuteAsync(new ProcessingJob { Kind = ProcessingJobKinds.UploadSessionSweep }, CancellationToken.None);
    }

    private async Task<T> CountAsync<T>(Func<SilexGisDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<SilexGisDbContext>());
    }

    private static object SyncSettings(int stagedDays) => new
    {
        peopleDirectoryEnabled = true,
        peopleDirectoryScope = "myGroups",
        convertTripsWithProblems = true,
        stagedUploadRetentionDays = stagedDays,
        rejectedHeldBytesRetentionDays = 30,
    };

    private static Dictionary<string, object?> StagedOpen(string fileName, long sizeBytes) => new()
    {
        ["fileName"] = fileName,
        ["sizeBytes"] = sizeBytes,
        ["stageForSync"] = true,
    };

    private static async Task<Guid> OpenStagedAsync(HttpClient client, string fileName, long sizeBytes)
    {
        var response = await client.PostAsJsonAsync("/api/v1/files/uploads", StagedOpen(fileName, sizeBytes));
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> StageAsync(HttpClient client, string fileName, byte[] bytes)
    {
        var session = await OpenStagedAsync(client, fileName, bytes.Length);
        await AppendAsync(client, session, 0, bytes);
        var response = await client.PostAsync($"/api/v1/files/uploads/{session}/complete", null);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static async Task<long> RemainingAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/files/config")).GetProperty("remainingBytes").GetInt64();

    private static async Task AppendAsync(HttpClient client, Guid session, long offset, byte[] chunk)
    {
        var response = await SendChunkAsync(client, session, offset, chunk);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static Task<HttpResponseMessage> SendChunkAsync(HttpClient client, Guid session, long offset, byte[] chunk)
    {
        var content = new ByteArrayContent(chunk);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return client.PutAsync($"/api/v1/files/uploads/{session}?offset={offset}", content);
    }

    private static async Task<HttpResponseMessage> PostUploadAsync(HttpClient client, string fileName, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/octet-stream");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        return await client.PostAsync("/api/v1/files/", form);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var body = await ReadJsonAsync(response);
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
