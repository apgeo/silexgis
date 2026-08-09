// SPDX-License-Identifier: AGPL-3.0-or-later
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
/// What an installation refuses, what it says before it refuses, and how a large upload
/// survives a bad connection.
///
/// <para>
/// The duplicate assertions are the ones worth reading twice. Two copies of the same bytes get
/// two quite different answers depending on whether the uploader may see the existing one, and
/// that asymmetry is the whole rule: told "this already exists", somebody learns it exists —
/// which for a document whose very presence is the sensitive part is the disclosure. So the
/// visible case warns and the invisible case stores a second copy silently, and the pair is
/// left for an administrator who can see both.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UploadLimitsAndResumeTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string connectionString;

    private HttpClient owner = null!;
    private HttpClient other = null!;
    private Guid ownerId;

    public UploadLimitsAndResumeTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-uplimits-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            ["Files:MaxUploadBytes"] = "4096",
            ["Files:RefusedExtensions:0"] = ".exe",
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"lim-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"lim-own-{suffix}@t.local");

        // An ordinary member rather than a second Editor: an Editor reads every document in
        // the installation, so "may not see the existing copy" — the whole point of the
        // duplicate rule below — could never be true of one.
        var otherId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"lim-oth-{suffix}@t.local");
        other = await AuthHelper.BearerClientAsync(factory, $"lim-oth-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = otherId,
            Domain = AccessDomain.Documents,
            Actions = AccessAction.Create,
            Effect = AccessEffect.Allow,
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        other?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    [Fact]
    public async Task The_limits_are_stated_before_an_upload_rather_than_after_it()
    {
        var config = await owner.GetFromJsonAsync<JsonElement>("/api/v1/files/config");

        config.GetProperty("maxUploadBytes").GetInt64().ShouldBe(4096);
        config.GetProperty("refusedExtensions").EnumerateArray().Select(e => e.GetString()).ShouldContain(".exe");
        config.GetProperty("chunkBytes").GetInt32().ShouldBeGreaterThan(0);
        config.GetProperty("archiveExtensions").EnumerateArray().Select(e => e.GetString()).ShouldContain(".zip");

        // No quota configured here, so there is no number to report — which is a different
        // answer from "you have nothing left" and has to read differently.
        config.GetProperty("remainingBytes").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_file_past_the_size_limit_is_refused_with_the_limit_named()
    {
        var refused = await PostUploadAsync(owner, "big.txt", RandomNumberGenerator.GetBytes(5000));
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(refused)).ShouldBe("file.too_large");

        // Just inside the limit is accepted, so the refusal is the limit rather than the route.
        (await PostUploadAsync(owner, "ok.txt", RandomNumberGenerator.GetBytes(4096))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_refused_type_is_refused_and_everything_else_is_not()
    {
        var refused = await PostUploadAsync(owner, "setup.exe", Encoding.UTF8.GetBytes("MZ"));
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(refused)).ShouldBe("file.type_not_accepted");

        (await PostUploadAsync(owner, "survey.txt", Encoding.UTF8.GetBytes("notes")))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task An_empty_file_is_refused_as_empty_rather_than_as_a_limit()
    {
        var refused = await PostUploadAsync(owner, "nothing.txt", []);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(refused)).ShouldBe("file.empty");
    }

    [Fact]
    public async Task Uploading_the_same_bytes_twice_warns_before_it_stores_them_again()
    {
        var bytes = Encoding.UTF8.GetBytes($"the same survey {Guid.NewGuid()}");

        (await PostUploadAsync(owner, "survey.txt", bytes)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await PostUploadAsync(owner, "survey-copy.txt", bytes);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(second)).ShouldBe("file.duplicate");

        // The refusal comes from the server rather than from the client's own check, so a
        // client that never asked is warned all the same. Saying yes stores it.
        (await PostUploadAsync(owner, "survey-copy.txt", bytes, "?allowDuplicate=true"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Asking_by_hash_finds_what_the_caller_may_see_and_nothing_else()
    {
        var bytes = Encoding.UTF8.GetBytes($"a report {Guid.NewGuid()}");
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));

        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/files/duplicate-check?sha256={hash}"))
            .GetProperty("duplicate").GetBoolean().ShouldBeFalse();

        (await PostUploadAsync(owner, "report.txt", bytes)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var found = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/files/duplicate-check?sha256={hash}");
        found.GetProperty("duplicate").GetBoolean().ShouldBeTrue();
        found.GetProperty("title").GetString().ShouldBe("report.txt");

        // Somebody who may not read that document is told there is no duplicate — telling them
        // otherwise would disclose that it exists.
        (await other.GetFromJsonAsync<JsonElement>($"/api/v1/files/duplicate-check?sha256={hash}"))
            .GetProperty("duplicate").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_duplicate_the_uploader_may_not_see_is_stored_again_and_recorded_for_an_administrator()
    {
        var bytes = Encoding.UTF8.GetBytes($"a private survey {Guid.NewGuid()}");
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));

        (await PostUploadAsync(owner, "survey.txt", bytes)).StatusCode.ShouldBe(HttpStatusCode.Created);

        // No warning, no refusal: as far as this caller is concerned the content is new.
        var second = await PostUploadAsync(other, "survey.txt", bytes);
        second.StatusCode.ShouldBe(HttpStatusCode.Created, await second.Content.ReadAsStringAsync());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // Both copies exist, and the pair is on the record for somebody who can see both.
        (await db.StoredFiles.AsNoTracking().CountAsync(f => f.Sha256 == hash)).ShouldBe(2);
        var record = await db.DuplicateUploadRecords.AsNoTracking().SingleAsync(r => r.Sha256 == hash);
        record.UploadedByUserId.ShouldNotBe(ownerId);
    }

    [Fact]
    public async Task A_quota_is_reported_up_front_and_binds_when_the_bytes_arrive()
    {
        var quotaRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-quota-{Guid.NewGuid():N}");
        using var quotaFactory = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = quotaRoot,
                ["Keys:Path"] = Path.Combine(quotaRoot, "keys"),
                ["Files:DefaultUserQuotaBytes"] = "600",
            });

        var email = $"quota-{Guid.NewGuid():N}"[..16] + "@t.local";
        await AuthHelper.CreateUserAsync(quotaFactory, GlobalRoles.Editor, email);
        using var client = await AuthHelper.BearerClientAsync(quotaFactory, email);

        try
        {
            var before = await client.GetFromJsonAsync<JsonElement>("/api/v1/files/config");
            before.GetProperty("remainingBytes").GetInt64().ShouldBe(600);

            (await PostUploadAsync(client, "first.txt", RandomNumberGenerator.GetBytes(500))).StatusCode.ShouldBe(HttpStatusCode.Created);

            // The number moves with what was stored, which is what makes it advice worth acting on.
            var after = await client.GetFromJsonAsync<JsonElement>("/api/v1/files/config");
            after.GetProperty("remainingBytes").GetInt64().ShouldBe(100);

            var refused = await PostUploadAsync(client, "second.txt", RandomNumberGenerator.GetBytes(200));
            refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await ReadCodeAsync(refused)).ShouldBe("file.quota_exceeded");

            // What fits still fits: the quota refuses the excess rather than the account.
            (await PostUploadAsync(client, "small.txt", RandomNumberGenerator.GetBytes(100))).StatusCode.ShouldBe(HttpStatusCode.Created);
        }
        finally
        {
            if (Directory.Exists(quotaRoot))
            {
                Directory.Delete(quotaRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task A_large_upload_arrives_in_pieces_and_becomes_one_document()
    {
        var bytes = RandomNumberGenerator.GetBytes(3000);
        var session = await OpenSessionAsync(owner, "panorama.bin", bytes.Length);

        await AppendAsync(owner, session, 0, bytes[..1000]);
        await AppendAsync(owner, session, 1000, bytes[1000..2000]);
        await AppendAsync(owner, session, 2000, bytes[2000..]);

        var completed = await owner.PostAsync($"/api/v1/files/uploads/{session}/complete", null);
        var payload = await completed.Content.ReadAsStringAsync();
        completed.StatusCode.ShouldBe(HttpStatusCode.Created, payload);

        var fileId = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var stored = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);

        stored.SizeBytes.ShouldBe(bytes.Length);
        stored.Sha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(bytes)));

        // The session is gone, so the expiry sweep cannot come along and delete the bytes of a
        // perfectly good document.
        (await db.UploadSessions.AsNoTracking().AnyAsync(s => s.Id == session)).ShouldBeFalse();
    }

    [Fact]
    public async Task An_interrupted_upload_resumes_from_where_it_stopped()
    {
        var bytes = RandomNumberGenerator.GetBytes(2000);
        var session = await OpenSessionAsync(owner, "scan.bin", bytes.Length);

        await AppendAsync(owner, session, 0, bytes[..800]);

        // What a client asks after losing its connection: how far did we actually get?
        var status = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/files/uploads/{session}");
        status.GetProperty("receivedBytes").GetInt64().ShouldBe(800);

        await AppendAsync(owner, session, 800, bytes[800..]);
        (await owner.PostAsync($"/api/v1/files/uploads/{session}/complete", null))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_piece_sent_twice_is_a_no_op_and_a_gap_is_refused_with_the_offset_to_use()
    {
        var bytes = RandomNumberGenerator.GetBytes(1500);
        var session = await OpenSessionAsync(owner, "scan.bin", bytes.Length);

        await AppendAsync(owner, session, 0, bytes[..500]);

        // A client that never saw the first answer sends the same piece again. Refusing it
        // would end the upload every time a response is lost.
        var replay = await SendChunkAsync(owner, session, 0, bytes[..500]);
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadJsonAsync(replay)).GetProperty("receivedBytes").GetInt64().ShouldBe(500);

        // A piece that does not continue the file is refused, and the answer says where to
        // resume so the next attempt is right rather than another guess.
        var gap = await SendChunkAsync(owner, session, 900, bytes[900..1000]);
        gap.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(gap)).ShouldBe("upload.chunk_out_of_order");
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/files/uploads/{session}"))
            .GetProperty("receivedBytes").GetInt64().ShouldBe(500);
    }

    [Fact]
    public async Task A_transfer_that_is_short_cannot_be_completed()
    {
        var bytes = RandomNumberGenerator.GetBytes(1000);
        var session = await OpenSessionAsync(owner, "scan.bin", bytes.Length);
        await AppendAsync(owner, session, 0, bytes[..400]);

        var early = await owner.PostAsync($"/api/v1/files/uploads/{session}/complete", null);
        early.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(early)).ShouldBe("upload.incomplete");
    }

    [Fact]
    public async Task A_resumable_upload_is_refused_up_front_when_it_could_never_be_stored()
    {
        // The point of the mechanism is not transferring what will be refused, so everything is
        // decided before a byte moves.
        var tooBig = await owner.PostAsJsonAsync(
            "/api/v1/files/uploads", new { fileName = "huge.bin", sizeBytes = 999_999 });
        tooBig.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(tooBig)).ShouldBe("file.too_large");

        var refusedType = await owner.PostAsJsonAsync(
            "/api/v1/files/uploads", new { fileName = "setup.exe", sizeBytes = 100 });
        refusedType.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(refusedType)).ShouldBe("file.type_not_accepted");
    }

    [Fact]
    public async Task A_session_belongs_to_the_person_who_opened_it()
    {
        var session = await OpenSessionAsync(owner, "scan.bin", 100);

        // Answered as absent rather than forbidden: a session is a transfer in progress, and
        // confirming an id belongs to someone would say what they are uploading and when.
        (await other.GetAsync($"/api/v1/files/uploads/{session}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendChunkAsync(other, session, 0, RandomNumberGenerator.GetBytes(10))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"/api/v1/files/uploads/{session}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await owner.GetAsync($"/api/v1/files/uploads/{session}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Abandoning_an_upload_takes_its_partial_bytes_with_it()
    {
        var session = await OpenSessionAsync(owner, "scan.bin", 1000);
        await AppendAsync(owner, session, 0, RandomNumberGenerator.GetBytes(400));

        string storagePath;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            storagePath = await db.UploadSessions.AsNoTracking()
                .Where(s => s.Id == session).Select(s => s.StoragePath).FirstAsync();
        }

        var absolute = Path.Combine(filesRoot, storagePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(absolute).ShouldBeTrue("the partial content is there before it is abandoned");

        (await owner.DeleteAsync($"/api/v1/files/uploads/{session}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        File.Exists(absolute).ShouldBeFalse();
    }

    [Fact]
    public async Task A_resumable_upload_lands_where_it_was_aimed()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/cabinets", new
        {
            name = $"Archive {Guid.NewGuid():N}"[..24],
            description = (string?)null,
            parentId = (Guid?)null,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var shelf = (await ReadJsonAsync(response)).GetProperty("id").GetGuid();

        var bytes = RandomNumberGenerator.GetBytes(600);
        var session = await OpenSessionAsync(owner, "march.pdf", bytes.Length, new
        {
            cabinetId = shelf,
            relativePath = "1987/march.pdf",
        });
        await AppendAsync(owner, session, 0, bytes);

        var completed = await owner.PostAsync($"/api/v1/files/uploads/{session}/complete", null);
        completed.StatusCode.ShouldBe(HttpStatusCode.Created, await completed.Content.ReadAsStringAsync());
        var fileId = (await ReadJsonAsync(completed)).GetProperty("id").GetGuid();

        // The destination survived the transfer, which is the point of carrying it on the
        // session rather than sending it with the last piece.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var documentId = await db.DocumentVersions.AsNoTracking()
            .Where(v => db.StoredFiles.Any(f => f.Id == fileId && f.DocumentVersionId == v.Id))
            .Select(v => v.DocumentId)
            .FirstAsync();

        var landedOn = await db.CabinetDocuments.AsNoTracking()
            .Where(m => m.DocumentId == documentId).Select(m => m.CabinetId).SingleAsync();
        var landed = await db.Cabinets.AsNoTracking().FirstAsync(c => c.Id == landedOn);
        landed.Name.ShouldBe("1987");
        landed.ParentId.ShouldBe(shelf);
    }

    private async Task<Guid> OpenSessionAsync(
        HttpClient client, string fileName, int sizeBytes, object? destination = null)
    {
        var body = new Dictionary<string, object?> { ["fileName"] = fileName, ["sizeBytes"] = sizeBytes };
        if (destination is not null)
        {
            foreach (var property in JsonSerializer.SerializeToElement(destination).EnumerateObject())
            {
                body[property.Name] = property.Value;
            }
        }

        var response = await client.PostAsJsonAsync("/api/v1/files/uploads", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task AppendAsync(HttpClient client, Guid session, long offset, byte[] chunk)
    {
        var response = await SendChunkAsync(client, session, offset, chunk);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static Task<HttpResponseMessage> SendChunkAsync(
        HttpClient client, Guid session, long offset, byte[] chunk)
    {
        var content = new ByteArrayContent(chunk);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return client.PutAsync($"/api/v1/files/uploads/{session}?offset={offset}", content);
    }

    private static async Task<HttpResponseMessage> PostUploadAsync(
        HttpClient client, string fileName, byte[] bytes, string query = "")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/octet-stream");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };

        // Awaited inside the using: returning the task would dispose the form before the
        // request body had been read off it.
        return await client.PostAsync($"/api/v1/files/{query}", form);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var body = await ReadJsonAsync(response);
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
