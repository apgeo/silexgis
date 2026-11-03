// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Jobs;
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
public sealed class UploadLimitsAndResumeTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string connectionString;

    private HttpClient owner = null!;
    private HttpClient other = null!;
    private Guid ownerId;
    private string ownerEmail = null!;

    public UploadLimitsAndResumeTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-uplimits-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, Settings(filesRoot));
    }

    private static Dictionary<string, string?> Settings(string root) => new()
    {
        ["Files:Root"] = root,
        ["Keys:Path"] = Path.Combine(root, "keys"),
        ["Files:MaxUploadBytes"] = "4096",
        ["Files:RefusedExtensions:0"] = ".exe",
    };

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerEmail = $"lim-own-{suffix}@t.local";
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, ownerEmail);
        owner = await AuthHelper.BearerClientAsync(factory, ownerEmail);

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

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

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
        var quotaRoot = Path.Combine(TestScratch.Root, $"silexgis-test-quota-{Guid.NewGuid():N}");
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
        var refusal = JsonDocument.Parse(await gap.Content.ReadAsStringAsync()).RootElement;
        refusal.GetProperty("code").GetString().ShouldBe("upload.chunk_out_of_order");

        // Where to resume is a member a client reads, not only the sentence a person does.
        refusal.GetProperty("expectedOffset").GetInt64().ShouldBe(500);
        refusal.GetProperty("detail").GetString().ShouldBe("The next piece starts at 500.");
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/files/uploads/{session}"))
            .GetProperty("receivedBytes").GetInt64().ShouldBe(500);
    }

    [Fact]
    public async Task A_piece_cut_off_mid_body_keeps_none_of_its_bytes_and_the_resumed_file_is_the_original()
    {
        var bytes = RandomNumberGenerator.GetBytes(3000);
        var session = await OpenSessionAsync(owner, "scan.bin", bytes.Length);
        await AppendAsync(owner, session, 0, bytes[..1000]);

        // The connection drops 400 bytes into the second piece: the body announces 1000 bytes
        // and fails after 400 have reached the server.
        await SendCutOffAsync(owner, session, 1000, bytes[1000..2000], deliver: 400, fail: true);
        (await ReceivedAsync(owner, session)).ShouldBe(1000);
        (await BlobLengthAsync(session)).ShouldBe(1000, "the partial piece is not left in the file");

        // A body that simply ends early, without an error, is no more of a piece.
        await SendCutOffAsync(owner, session, 1000, bytes[1000..2000], deliver: 300, fail: false);
        (await ReceivedAsync(owner, session)).ShouldBe(1000);
        (await BlobLengthAsync(session)).ShouldBe(1000);

        // The client resumes from what the status says, and the finished file is its own.
        await AppendAsync(owner, session, 1000, bytes[1000..2000]);
        await AppendAsync(owner, session, 2000, bytes[2000..]);
        var fileId = await CompleteAsync(owner, session);
        (await StoredHashAsync(fileId)).ShouldBe(Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    [Fact]
    public async Task Every_piece_sent_twice_at_once_still_assembles_the_original_file()
    {
        var bytes = RandomNumberGenerator.GetBytes(4000);
        var session = await OpenSessionAsync(owner, "scan.bin", bytes.Length);

        // Each piece goes out twice at the same moment, as a client whose timeout fired just as
        // the first copy was being written would send it. Each copy is either taken, answered
        // as already held, or refused with where to resume; none may write the piece twice.
        for (var offset = 0; offset < bytes.Length; offset += 200)
        {
            var piece = bytes[offset..(offset + 200)];
            var answers = await Task.WhenAll(
                SendChunkAsync(owner, session, offset, piece), SendChunkAsync(owner, session, offset, piece));
            foreach (var answer in answers)
            {
                answer.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.Conflict);
            }

            var received = await ReceivedAsync(owner, session);
            if (received == offset)
            {
                // Both copies lost to each other: the client resends, as the 409 told it to.
                await AppendAsync(owner, session, offset, piece);
                received = await ReceivedAsync(owner, session);
            }

            received.ShouldBe(offset + 200);
            (await BlobLengthAsync(session)).ShouldBe(received);
        }

        var fileId = await CompleteAsync(owner, session);
        (await StoredHashAsync(fileId)).ShouldBe(Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    [Fact]
    public async Task A_piece_that_does_not_say_how_long_it_is_is_refused()
    {
        var bytes = RandomNumberGenerator.GetBytes(1000);
        var session = await OpenSessionAsync(owner, "scan.bin", bytes.Length);

        // Sent chunked, with no Content-Length: the server cannot tell a whole piece from one the
        // connection cut short, so it takes none of it rather than guess.
        var content = new StreamContent(new MemoryStream(bytes[..400]));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/files/uploads/{session}?offset=0")
        {
            Content = content,
        };
        request.Headers.TransferEncodingChunked = true;
        var refused = await owner.SendAsync(request);

        refused.StatusCode.ShouldBe(HttpStatusCode.LengthRequired);
        (await ReadCodeAsync(refused)).ShouldBe("upload.length_required");
        (await ReceivedAsync(owner, session)).ShouldBe(0);
        (await BlobLengthAsync(session)).ShouldBe(0);

        // The same bytes with their length are a piece like any other.
        await AppendAsync(owner, session, 0, bytes[..400]);
        (await ReceivedAsync(owner, session)).ShouldBe(400);
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

    [Fact]
    public async Task A_resumable_upload_keeps_the_club_and_the_attachment_role_it_was_opened_with()
    {
        Guid club;
        long caveTypeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            club = await NewClubAsync(db);
            await RosterHelper.AddMemberAsync(db, club, ownerId);
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        var created = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Resumed Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var caveId = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var bytes = RandomNumberGenerator.GetBytes(700);
        var session = await OpenSessionAsync(owner, "entrance.bin", bytes.Length, new
        {
            cavingGroupId = club,
            attachEntityType = "feature",
            attachEntityId = caveId,
            attachRole = "photoEntrance",
        });
        await AppendAsync(owner, session, 0, bytes);
        var fileId = await CompleteAsync(owner, session);

        await using var check = factory.Services.CreateAsyncScope();
        var checkDb = check.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await DocumentClubAsync(checkDb, fileId)).ShouldBe(club);
        (await checkDb.Attachments.AsNoTracking().Where(a => a.FeatureId == caveId).Select(a => a.Role).SingleAsync())
            .ShouldBe(AttachmentRole.PhotoEntrance);
    }

    [Fact]
    public async Task Completion_asks_again_whether_the_caller_may_create_in_the_club_they_named()
    {
        // A member whose only right to add documents is one scoped to a single club: with the
        // club named they may, without it they may not, so the binding is what admits them.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var memberId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"lim-club-{suffix}@t.local");
        using var member = await AuthHelper.BearerClientAsync(factory, $"lim-club-{suffix}@t.local");

        Guid club;
        long grant;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            club = await NewClubAsync(db);
            var entry = new AccessEntry
            {
                SubjectKind = AccessSubjectKind.User,
                SubjectId = memberId,
                Domain = AccessDomain.Documents,
                Actions = AccessAction.Create,
                Effect = AccessEffect.Allow,
                ScopeKind = AccessScopeKind.CavingGroup,
                ScopeId = club,
            };
            db.AccessEntries.Add(entry);
            await db.SaveChangesAsync();
            grant = entry.Id;
        }

        var first = RandomNumberGenerator.GetBytes(300);
        var second = RandomNumberGenerator.GetBytes(310);
        var allowed = await OpenSessionAsync(member, "allowed.bin", first.Length, new { cavingGroupId = club });
        var refused = await OpenSessionAsync(member, "refused.bin", second.Length, new { cavingGroupId = club });
        await AppendAsync(member, allowed, 0, first);
        await AppendAsync(member, refused, 0, second);

        // While the grant stands, the finished document belongs to the club it was aimed at.
        var fileId = await CompleteAsync(member, allowed);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await DocumentClubAsync(db, fileId)).ShouldBe(club);

            // The grant goes while the second transfer waits to be finished.
            await db.AccessEntries.Where(e => e.Id == grant).ExecuteDeleteAsync();
        }

        var completed = await member.PostAsync($"/api/v1/files/uploads/{refused}/complete", null);
        completed.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await completed.Content.ReadAsStringAsync());
        (await ReadCodeAsync(completed)).ShouldBe(CavingGroupBindingRules.ForbiddenCode);

        // Refused, not ended: the bytes are still there should the right come back.
        (await ReceivedAsync(member, refused)).ShouldBe(second.Length);
    }

    [Fact]
    public async Task Completion_into_a_batch_closed_meanwhile_is_refused()
    {
        var opened = await owner.PostAsJsonAsync("/api/v1/upload-batches", new { label = "Resumed drop" });
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync());
        var batch = (await ReadJsonAsync(opened)).GetProperty("id").GetGuid();

        var first = RandomNumberGenerator.GetBytes(320);
        var second = RandomNumberGenerator.GetBytes(330);
        var early = await OpenSessionAsync(owner, "early.bin", first.Length, new { batchId = batch });
        var late = await OpenSessionAsync(owner, "late.bin", second.Length, new { batchId = batch });
        await AppendAsync(owner, early, 0, first);
        await AppendAsync(owner, late, 0, second);

        await CompleteAsync(owner, early);
        (await owner.PostAsync($"/api/v1/upload-batches/{batch}/close", null)).IsSuccessStatusCode.ShouldBeTrue();

        var completed = await owner.PostAsync($"/api/v1/files/uploads/{late}/complete", null);
        completed.StatusCode.ShouldBe(HttpStatusCode.Conflict, await completed.Content.ReadAsStringAsync());
        (await ReadCodeAsync(completed)).ShouldBe("upload_batch.closed");
    }

    [Fact]
    public async Task Completion_refuses_a_club_deleted_while_the_upload_arrived()
    {
        Guid club;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            club = await NewClubAsync(db);
            await RosterHelper.AddMemberAsync(db, club, ownerId);
        }

        var kept = RandomNumberGenerator.GetBytes(340);
        var orphaned = RandomNumberGenerator.GetBytes(350);
        var before = await OpenSessionAsync(owner, "before.bin", kept.Length, new { cavingGroupId = club });
        var after = await OpenSessionAsync(owner, "after.bin", orphaned.Length, new { cavingGroupId = club });
        await AppendAsync(owner, before, 0, kept);
        await AppendAsync(owner, after, 0, orphaned);

        // While the club stands, the transfer completes into it.
        var fileId = await CompleteAsync(owner, before);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await DocumentClubAsync(db, fileId)).ShouldBe(club);
            await db.CavingGroups.Where(g => g.Id == club).ExecuteDeleteAsync();
        }

        // The club the request named is gone: refused as the single-request route refuses it,
        // rather than filed with no club at all.
        var completed = await owner.PostAsync($"/api/v1/files/uploads/{after}/complete", null);
        completed.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await completed.Content.ReadAsStringAsync());
        (await ReadCodeAsync(completed)).ShouldBe("document.caving_group_not_found");
        (await ReceivedAsync(owner, after)).ShouldBe(orphaned.Length);
    }

    [Fact]
    public async Task A_store_that_cannot_write_a_piece_is_a_server_fault_rather_than_a_piece_to_resend()
    {
        using var host = new SilexGisApiFactory(connectionString, Settings(filesRoot), services =>
        {
            var real = services.Single(s => s.ServiceType == typeof(IFileStore));
            services.Remove(real);
            services.AddSingleton<IFileStore>(provider => new WriteRefusingFileStore(
                (IFileStore)ActivatorUtilities.CreateInstance(provider, real.ImplementationType!)));
        });
        var client = await AuthHelper.BearerClientAsync(host, ownerEmail);

        var bytes = RandomNumberGenerator.GetBytes(600);
        var session = await OpenSessionAsync(client, "full-disk.bin", bytes.Length);

        // A full disk is not the client's to fix: a 409 naming the same offset would have it send
        // the same piece for ever, with nothing logged.
        using var refused = await SendChunkAsync(client, session, 0, bytes);
        refused.StatusCode.ShouldBe(HttpStatusCode.InternalServerError, await refused.Content.ReadAsStringAsync());
        (await ReceivedAsync(client, session)).ShouldBe(0);
    }

    private static async Task<Guid> NewClubAsync(SilexGisDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var club = new CavingGroup { Name = $"Resumable club {suffix}", Slug = $"res-club-{suffix}" };
        db.CavingGroups.Add(club);
        await db.SaveChangesAsync();
        return club.Id;
    }

    private static Task<Guid?> DocumentClubAsync(SilexGisDbContext db, Guid fileId) =>
        db.Documents.AsNoTracking()
            .Where(d => db.DocumentVersions.Any(v =>
                v.DocumentId == d.Id && db.StoredFiles.Any(f => f.Id == fileId && f.DocumentVersionId == v.Id)))
            .Select(d => d.CavingGroupId)
            .FirstAsync();

    [Fact]
    public async Task A_completion_that_fails_after_the_document_leaves_nothing_a_sweep_could_take_from_it()
    {
        var bytes = RandomNumberGenerator.GetBytes(1500);
        var failing = new FailingCommand("DELETE FROM upload_sessions");
        using var host = new SilexGisApiFactory(connectionString, Settings(filesRoot), services =>
            services.ConfigureDbContext<SilexGisDbContext>(options => options.AddInterceptors(failing)));
        var client = await AuthHelper.BearerClientAsync(host, ownerEmail);

        var session = await OpenSessionAsync(client, "cut-off-answer.bin", bytes.Length);
        await AppendAsync(client, session, 0, bytes);

        failing.Armed = true;
        using (var refused = await client.PostAsync($"/api/v1/files/uploads/{session}/complete", null))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        }

        failing.Armed = false;

        // The failure landed between the document and the end of the session. What must not be
        // left is a committed document over bytes a session still claims: the sweep deletes a
        // session's bytes when it expires, whatever else points at them.
        string storagePath;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            storagePath = await db.UploadSessions.AsNoTracking()
                .Where(s => s.Id == session).Select(s => s.StoragePath).SingleAsync();
            (await db.StoredFiles.AsNoTracking().CountAsync(f => f.StoragePath == storagePath)).ShouldBe(0);
        }

        // And so asking again simply completes it, into one document holding the original bytes.
        var fileId = await CompleteAsync(client, session);
        (await StoredHashAsync(fileId)).ShouldBe(Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    [Fact]
    public async Task Two_completions_of_one_session_at_once_file_one_document()
    {
        var bytes = RandomNumberGenerator.GetBytes(1200);
        var hold = new HoldUntilBothStoreAFile();
        using var host = new SilexGisApiFactory(connectionString, Settings(filesRoot), services =>
            services.ConfigureDbContext<SilexGisDbContext>(options => options.AddInterceptors(hold)));
        var first = await AuthHelper.BearerClientAsync(host, ownerEmail);
        var second = await AuthHelper.BearerClientAsync(host, ownerEmail);

        var session = await OpenSessionAsync(first, "asked-twice.bin", bytes.Length);
        await AppendAsync(first, session, 0, bytes);
        string storagePath;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            storagePath = await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().UploadSessions
                .AsNoTracking().Where(s => s.Id == session).Select(s => s.StoragePath).SingleAsync();
        }

        hold.Armed = true;
        var answers = await Task.WhenAll(
            first.PostAsync($"/api/v1/files/uploads/{session}/complete", null),
            second.PostAsync($"/api/v1/files/uploads/{session}/complete", null));
        hold.Armed = false;

        // One of them files it; the other finds the session already finished.
        answers.Count(a => a.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        answers.Count(a => a.StatusCode == HttpStatusCode.NotFound).ShouldBe(1);

        await using var check = host.Services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.StoredFiles.AsNoTracking().CountAsync(f => f.StoragePath == storagePath)).ShouldBe(1);
        (await db.UploadSessions.AsNoTracking().AnyAsync(s => s.Id == session)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_name_longer_than_a_stored_file_keeps_is_shortened_rather_than_failing_the_upload()
    {
        var name = new string('n', 300) + ".pdf";

        // The single-request route.
        var bytes = RandomNumberGenerator.GetBytes(64);
        var response = await PostUploadAsync(owner, name, bytes);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var multipart = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        // And the resumable one, whose opening accepts the longer name.
        var pieces = RandomNumberGenerator.GetBytes(900);
        var session = await OpenSessionAsync(owner, name, pieces.Length);
        await AppendAsync(owner, session, 0, pieces);
        var resumable = await CompleteAsync(owner, session);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        foreach (var fileId in new[] { multipart, resumable })
        {
            var stored = await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == fileId);
            stored.OriginalName.Length.ShouldBe(FileFormats.MaxFileNameLength);
            stored.OriginalName.ShouldEndWith(".pdf");
        }
    }

    [Fact]
    public async Task A_single_request_upload_whose_recording_fails_takes_its_bytes_back_out()
    {
        var root = Path.Combine(filesRoot, "failing-record");
        var failing = new FailingCommand("INSERT INTO documents");
        using var host = new SilexGisApiFactory(connectionString, Settings(root), services =>
            services.ConfigureDbContext<SilexGisDbContext>(options => options.AddInterceptors(failing)));
        var client = await AuthHelper.BearerClientAsync(host, ownerEmail);

        failing.Armed = true;
        using (var refused = await PostUploadAsync(client, "never-recorded.txt", Encoding.UTF8.GetBytes("bytes with no row")))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        }

        failing.Armed = false;
        HoldsBytes(root, "bytes with no row").ShouldBeFalse();

        // The same host stores normally once nothing is failing, so the absence above is the
        // clean-up and not a host that never wrote anything.
        using var stored = await PostUploadAsync(client, "recorded.txt", Encoding.UTF8.GetBytes("bytes with a row"));
        stored.StatusCode.ShouldBe(HttpStatusCode.Created);
        HoldsBytes(root, "bytes with a row").ShouldBeTrue();
    }

    [Fact]
    public async Task A_new_revision_whose_recording_fails_takes_its_bytes_back_out()
    {
        var root = Path.Combine(filesRoot, "failing-revision");
        var failing = new FailingCommand("INSERT INTO files ");
        using var host = new SilexGisApiFactory(connectionString, Settings(root), services =>
            services.ConfigureDbContext<SilexGisDbContext>(options => options.AddInterceptors(failing)));
        var client = await AuthHelper.BearerClientAsync(host, ownerEmail);

        using var first = await PostUploadAsync(client, "to-revise.txt", Encoding.UTF8.GetBytes("revision one of it"));
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var fileId = (await ReadJsonAsync(first)).GetProperty("id").GetGuid();
        failing.Armed = true;
        using (var refused = await PostUploadAsync(
            client, "to-revise.txt", Encoding.UTF8.GetBytes("revision two, never recorded"), $"{fileId}/versions"))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        }

        failing.Armed = false;
        HoldsBytes(root, "revision two, never recorded").ShouldBeFalse();
        HoldsBytes(root, "revision one of it").ShouldBeTrue();
    }

    [Fact]
    public async Task Deleting_an_old_version_whose_bytes_will_not_delete_still_answers_that_it_is_gone()
    {
        var refusing = new HashSet<string>();
        using var host = new SilexGisApiFactory(connectionString, Settings(filesRoot), services =>
        {
            var real = services.Single(s => s.ServiceType == typeof(IFileStore));
            services.Remove(real);
            services.AddSingleton<IFileStore>(provider => new DeleteRefusingFileStore(
                (IFileStore)ActivatorUtilities.CreateInstance(provider, real.ImplementationType!), refusing));
        });
        var client = await AuthHelper.BearerClientAsync(host, ownerEmail);

        var first = await PostUploadAsync(client, "revised.txt", Encoding.UTF8.GetBytes("the first revision"));
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var oldId = (await ReadJsonAsync(first)).GetProperty("id").GetGuid();
        var second = await PostUploadAsync(client, "revised.txt", Encoding.UTF8.GetBytes("the second revision"), $"{oldId}/versions");
        second.StatusCode.ShouldBe(HttpStatusCode.Created, await second.Content.ReadAsStringAsync());

        await using (var scope = host.Services.CreateAsyncScope())
        {
            refusing.Add(await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().StoredFiles
                .AsNoTracking().Where(f => f.Id == oldId).Select(f => f.StoragePath).SingleAsync());
        }

        // Renderings drawn from the old revision, as a viewer asking for them would have left.
        var pageCache = Path.Combine(filesRoot, "pages", $"{oldId:N}-p1.webp");
        var locationFreeCopy = Path.Combine(filesRoot, "nolocation", $"{oldId:N}-copy.jpg");
        foreach (var rendering in new[] { pageCache, locationFreeCopy })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(rendering)!);
            await File.WriteAllBytesAsync(rendering, [1, 2, 3]);
        }

        // The rows go first and the bytes after, best effort: a file the store will not delete
        // is left behind, and the delete that did happen is not reported as a failure.
        using var deleted = await client.DeleteAsync($"/api/v1/files/{oldId}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());

        await using var check = host.Services.CreateAsyncScope();
        (await check.ServiceProvider.GetRequiredService<SilexGisDbContext>().StoredFiles
            .AsNoTracking().AnyAsync(f => f.Id == oldId)).ShouldBeFalse();

        // The bytes that would not delete do not keep the renderings drawn from them.
        File.Exists(pageCache).ShouldBeFalse();
        File.Exists(locationFreeCopy).ShouldBeFalse();
    }

    [Fact]
    public async Task An_expired_upload_is_gone_to_its_owner_and_the_sweep_takes_its_bytes_but_not_a_finished_one()
    {
        var partial = RandomNumberGenerator.GetBytes(1000);
        var expiring = await OpenSessionAsync(owner, "left-half-way.bin", 2000);
        await AppendAsync(owner, expiring, 0, partial);
        var expiringBlob = await BlobPathAsync(expiring);
        File.Exists(expiringBlob).ShouldBeTrue();

        // A piece pushes the expiry out: a session about to lapse is a day from lapsing again
        // once another piece arrives.
        var finishedBytes = RandomNumberGenerator.GetBytes(800);
        var finishing = await OpenSessionAsync(owner, "finished.bin", finishedBytes.Length);
        await SetExpiryAsync(finishing, DateTimeOffset.UtcNow.AddMinutes(10));
        await AppendAsync(owner, finishing, 0, finishedBytes);
        var finishedBlob = await BlobPathAsync(finishing);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().UploadSessions.AsNoTracking()
                .SingleAsync(s => s.Id == finishing)).ExpiresAt
                .ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddHours(23));
        }

        // A session left alone past its expiry is no longer the owner's to resume, answered
        // exactly as a session that never existed.
        await SetExpiryAsync(expiring, DateTimeOffset.UtcNow.AddMinutes(-1));

        using (var status = await owner.GetAsync($"/api/v1/files/uploads/{expiring}"))
        {
            status.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using (var piece = await SendChunkAsync(owner, expiring, 1000, partial))
        {
            piece.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using (var complete = await owner.PostAsync($"/api/v1/files/uploads/{expiring}/complete", null))
        {
            complete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        var finishedId = await CompleteAsync(owner, finishing);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var handler = new UploadSessionSweepHandler(
                scope.ServiceProvider.GetRequiredService<SilexGisDbContext>(),
                scope.ServiceProvider.GetRequiredService<IFileStore>(),
                scope.ServiceProvider.GetRequiredService<ThumbnailService>(),
                NullLogger<UploadSessionSweepHandler>.Instance);
            await handler.ExecuteAsync(
                new ProcessingJob { Kind = ProcessingJobKinds.UploadSessionSweep }, CancellationToken.None);

            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.UploadSessions.AsNoTracking().AnyAsync(s => s.Id == expiring)).ShouldBeFalse();
        }

        File.Exists(expiringBlob).ShouldBeFalse();

        // The finished upload's bytes now belong to its document, and the sweep leaves them.
        File.Exists(finishedBlob).ShouldBeTrue();
        (await StoredHashAsync(finishedId)).ShouldBe(Convert.ToHexStringLower(SHA256.HashData(finishedBytes)));
    }

    private async Task SetExpiryAsync(Guid session, DateTimeOffset expiresAt)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().UploadSessions
            .Where(s => s.Id == session)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, expiresAt));
    }

    private async Task<string> BlobPathAsync(Guid session)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var storagePath = await db.UploadSessions.AsNoTracking()
            .Where(s => s.Id == session).Select(s => s.StoragePath).SingleAsync();
        return scope.ServiceProvider.GetRequiredService<IFileStore>().GetAbsolutePath(storagePath);
    }

    /// <summary>Whether any file under a files root holds exactly these bytes.</summary>
    private static bool HoldsBytes(string root, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return Directory.Exists(root)
            && Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Any(f => new FileInfo(f).Length == bytes.Length && File.ReadAllBytes(f).AsSpan().SequenceEqual(bytes));
    }

    /// <summary>
    /// Fails, while armed, every command whose text starts a statement of the given kind: a
    /// stand-in for a save that breaks, or a request cut off, at exactly that point.
    /// </summary>
    private sealed class FailingCommand(string statement) : DbCommandInterceptor
    {
        private volatile bool armed;

        public bool Armed
        {
            get => armed;
            set => armed = value;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Refuse(command);
            return new(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Refuse(command);
            return new(result);
        }

        private void Refuse(DbCommand command)
        {
            if (armed && command.CommandText.Replace("\"", "", StringComparison.Ordinal)
                    .Contains(statement, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Refused for the test: {statement}.");
            }
        }
    }

    /// <summary>
    /// Holds a save that stores a file until a second request reaches the same point, so two
    /// completions both get as far as recording before either commits — if the code lets them.
    /// Bounded, so a request that is (correctly) kept waiting elsewhere lets the first go on.
    /// </summary>
    private sealed class HoldUntilBothStoreAFile : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrived;
        private volatile bool armed;

        public bool Armed
        {
            get => armed;
            set => armed = value;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (armed
                && eventData.Context is { } context
                && context.ChangeTracker.Entries<StoredFile>().Any(e => e.State == EntityState.Added))
            {
                if (Interlocked.Increment(ref arrived) >= 2)
                {
                    both.TrySetResult();
                }

                await Task.WhenAny(both.Task, Task.Delay(TimeSpan.FromSeconds(3), cancellationToken));
            }

            return result;
        }
    }

    /// <summary>The real store, except that no piece can be written: the device is full.</summary>
    private sealed class WriteRefusingFileStore(IFileStore inner) : IFileStore
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default) =>
            inner.SaveAsync(content, extension, ct);

        public async Task<IExclusiveContentWriter> OpenExclusiveAsync(string storagePath, CancellationToken ct = default) =>
            new FullWriter(await inner.OpenExclusiveAsync(storagePath, ct));

        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            inner.OpenReadAsync(storagePath, ct);

        public Task DeleteAsync(string storagePath, CancellationToken ct = default) =>
            inner.DeleteAsync(storagePath, ct);

        public string GetAbsolutePath(string storagePath) => inner.GetAbsolutePath(storagePath);

        private sealed class FullWriter(IExclusiveContentWriter held) : IExclusiveContentWriter
        {
            public long Length => held.Length;

            public Task<bool> WriteAtAsync(long offset, Stream content, long length, CancellationToken ct = default) =>
                throw new IOException("No space left on device.");

            public ValueTask DisposeAsync() => held.DisposeAsync();
        }
    }

    /// <summary>The real store, except that the paths it is given will not delete.</summary>
    private sealed class DeleteRefusingFileStore(IFileStore inner, IReadOnlySet<string> refusing) : IFileStore
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default) =>
            inner.SaveAsync(content, extension, ct);

        public Task<IExclusiveContentWriter> OpenExclusiveAsync(string storagePath, CancellationToken ct = default) =>
            inner.OpenExclusiveAsync(storagePath, ct);

        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            inner.OpenReadAsync(storagePath, ct);

        public Task DeleteAsync(string storagePath, CancellationToken ct = default) =>
            refusing.Contains(storagePath)
                ? throw new IOException("The file is in use.")
                : inner.DeleteAsync(storagePath, ct);

        public string GetAbsolutePath(string storagePath) => inner.GetAbsolutePath(storagePath);
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

    private static async Task SendCutOffAsync(
        HttpClient client, Guid session, long offset, byte[] piece, int deliver, bool fail)
    {
        var content = new CutOffContent(piece, deliver, fail);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        try
        {
            using var response = await client.PutAsync($"/api/v1/files/uploads/{session}?offset={offset}", content);

            // When the server gets to answer at all, it must not claim to have taken the piece.
            response.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        }
        catch (HttpRequestException)
        {
            // The client side of a dropped connection: there is no answer to read.
        }
        catch (IOException)
        {
        }
    }

    private static async Task<long> ReceivedAsync(HttpClient client, Guid session) =>
        (await client.GetFromJsonAsync<JsonElement>($"/api/v1/files/uploads/{session}"))
            .GetProperty("receivedBytes").GetInt64();

    private async Task<long> BlobLengthAsync(Guid session)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var storagePath = await db.UploadSessions.AsNoTracking()
            .Where(s => s.Id == session).Select(s => s.StoragePath).FirstAsync();
        var absolute = scope.ServiceProvider.GetRequiredService<IFileStore>().GetAbsolutePath(storagePath);
        return File.Exists(absolute) ? new FileInfo(absolute).Length : 0;
    }

    private static async Task<Guid> CompleteAsync(HttpClient client, Guid session)
    {
        var completed = await client.PostAsync($"/api/v1/files/uploads/{session}/complete", null);
        var payload = await completed.Content.ReadAsStringAsync();
        completed.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<string> StoredHashAsync(Guid fileId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var stored = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);

        // Hashed from the blob itself as well as read from the row: the row's hash is computed
        // from the same bytes at completion, and the claim is about what is on disk.
        var absolute = scope.ServiceProvider.GetRequiredService<IFileStore>().GetAbsolutePath(stored.StoragePath);
        var onDisk = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(absolute)));
        onDisk.ShouldBe(stored.Sha256);
        return onDisk;
    }

    /// <summary>
    /// A request body that announces the whole piece and delivers only part of it — then either
    /// fails, as a dropped connection does, or just ends.
    /// </summary>
    private sealed class CutOffContent(byte[] piece, int deliver, bool fail) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            await stream.WriteAsync(piece.AsMemory(0, deliver));
            await stream.FlushAsync();
            if (fail)
            {
                throw new IOException("The connection dropped part way through the piece.");
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = piece.Length;
            return true;
        }
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
