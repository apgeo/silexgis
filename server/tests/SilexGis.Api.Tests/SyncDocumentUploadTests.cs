// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The upload's <c>documents</c> section: a phone's photo or document created under the phone's own
/// id from bytes it staged beforehand, edited by revision, given new bytes as a new revision that
/// keeps the old one, and removed only by somebody who may remove it.
/// </summary>
/// <remarks>
/// Every picture here is generated, with an invented position, so the camera facts the server
/// reads out of a file can be asserted against values the test chose.
/// </remarks>
public sealed class SyncDocumentUploadTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient phone = null!;
    private HttpClient colleague = null!;
    private Guid phoneUserId;
    private Guid set;
    private Guid colleagueSet;

    public SyncDocumentUploadTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-syncdocs-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async ValueTask InitializeAsync()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        phoneUserId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"sdu-ph-{marker}@t.local");
        phone = await AuthHelper.BearerClientAsync(factory, $"sdu-ph-{marker}@t.local");

        // Another editor: the seeded Editors group reads and edits every document, and removes none
        // — the account that may change a document somebody else owns and may not take it away.
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"sdu-co-{marker}@t.local");
        colleague = await AuthHelper.BearerClientAsync(factory, $"sdu-co-{marker}@t.local");

        set = await CreateSetAsync(phone, marker);
        colleagueSet = await CreateSetAsync(colleague, marker);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        phone?.Dispose();
        colleague?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Staged_bytes_named_by_a_row_become_a_document_under_the_phones_id_with_the_camera_facts_read()
    {
        var bytes = Corpus.GeotaggedJpeg(45.25, 25.5);
        var staged = await StageAsync(phone, "IMG_0042.jpg", bytes);
        var documentId = Guid.CreateVersion7();
        var capturedAt = new DateTimeOffset(2026, 7, 14, 10, 30, 0, TimeSpan.Zero);

        var batchId = Guid.CreateVersion7();
        var body = Upload(batchId, new
        {
            id = documentId,
            title = "Shaft entrance",
            description = "Looking down the shaft",
            fileName = "IMG_0042.jpg",
            sha256 = Hash(bytes),
            sizeBytes = bytes.Length,
            stagedUploadId = staged,
            capturedAt,
        });
        var answer = await UploadAsync(phone, set, body);

        var result = answer.GetProperty("documents").EnumerateArray().Single();
        result.GetProperty("id").GetGuid().ShouldBe(documentId);
        result.GetProperty("status").GetString().ShouldBe("created");
        var revision = result.GetProperty("revision").GetDateTimeOffset();
        answer.GetProperty("written").GetInt32().ShouldBe(1);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var document = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == documentId);
            document.Title.ShouldBe("Shaft entrance");
            document.OwnerUserId.ShouldBe(phoneUserId);
            (document.UpdatedAt.UtcTicks / 10).ShouldBe(revision.UtcTicks / 10);

            var version = await db.DocumentVersions.AsNoTracking().SingleAsync(v => v.DocumentId == documentId);
            version.IsCurrent.ShouldBeTrue();
            version.UploadedBy.ShouldBe(phoneUserId);

            // Read out of the file, as every upload's are: its type from the content, its position
            // from the camera's tags. The file states no capture time, so the phone's one stands.
            var file = await db.StoredFiles.AsNoTracking().SingleAsync(f => f.DocumentVersionId == version.Id);
            file.Sha256.ShouldBe(Hash(bytes));
            file.MimeType.ShouldBe("image/jpeg");
            file.Kind.ShouldBe(Domain.Entities.FileKind.Image);
            file.Geom.ShouldNotBeNull();
            file.Geom!.Y.ShouldBe(45.25, 0.001);
            file.Geom.X.ShouldBe(25.5, 0.001);
            file.ContentCreatedAt.ShouldBe(capturedAt);
            File.Exists(scope.ServiceProvider.GetRequiredService<IFileStore>().GetAbsolutePath(file.StoragePath)).ShouldBeTrue();

            // The phone's description is the photo's caption.
            (await db.PhotoDetails.AsNoTracking().SingleAsync(p => p.DocumentId == documentId))
                .Caption.ShouldBe("Looking down the shaft");

            // The staged row is consumed — its bytes are the document's now, out of the sweep's reach.
            (await db.UploadSessions.AsNoTracking().AnyAsync(s => s.Id == staged)).ShouldBeFalse();

            // The line an undo will reach for names the document and the revision it made.
            var line = await db.ImportBatchItems.AsNoTracking().SingleAsync(i => i.DocumentId == documentId);
            line.DocumentVersionId.ShouldBe(version.Id);
        }

        // Sent again, the batch is answered from the record and writes nothing.
        var replay = await UploadAsync(phone, set, body);
        replay.GetProperty("replayed").GetBoolean().ShouldBeTrue();
        replay.GetProperty("documents").EnumerateArray().Single().GetProperty("status").GetString().ShouldBe("created");
        (await CountAsync(db => db.DocumentVersions.CountAsync(v => v.DocumentId == documentId))).ShouldBe(1);
        (await CountAsync(db => db.ImportBatchItems.CountAsync(i => i.DocumentId == documentId))).ShouldBe(1);

        // The same create in a new batch — a phone that never heard the answer — changes nothing.
        var resent = await UploadAsync(phone, set, Upload(Guid.CreateVersion7(), new
        {
            id = documentId,
            title = "Renamed by a resend",
            sha256 = Hash(bytes),
        }));
        var again = resent.GetProperty("documents").EnumerateArray().Single();
        again.GetProperty("status").GetString().ShouldBe("unchanged");
        (await CountAsync(db => db.Documents.Where(d => d.Id == documentId).Select(d => d.Title).SingleAsync()))
            .ShouldBe("Shaft entrance");
    }

    [Fact]
    public async Task A_row_naming_missing_or_different_bytes_is_refused_and_creates_nothing()
    {
        var bytes = Corpus.GeotaggedJpeg(45.1, 25.1);
        var staged = await StageAsync(phone, "IMG_0100.jpg", bytes);
        var other = Corpus.Jpeg();
        var colleaguesStaged = await StageAsync(colleague, "IMG_0101.jpg", other);

        var wrongHash = Guid.CreateVersion7();
        var noBytes = Guid.CreateVersion7();
        var wrongSize = Guid.CreateVersion7();
        var notMine = Guid.CreateVersion7();
        var answer = await UploadAsync(phone, set, Upload(
            Guid.CreateVersion7(),
            new { id = wrongHash, sha256 = Hash(other), stagedUploadId = staged },
            new { id = noBytes, sha256 = Hash(bytes) },
            new { id = wrongSize, sha256 = Hash(bytes), sizeBytes = bytes.Length + 1, stagedUploadId = staged },
            // Another account's staged bytes are no staged bytes at all, whatever their hash.
            new { id = notMine, sha256 = Hash(other), stagedUploadId = colleaguesStaged }));

        var results = answer.GetProperty("documents").EnumerateArray().ToDictionary(r => r.GetProperty("id").GetGuid());
        Code(results[wrongHash]).ShouldBe("sync.file_hash_mismatch");
        Code(results[noBytes]).ShouldBe("sync.file_bytes_missing");
        Code(results[wrongSize]).ShouldBe("sync.file_hash_mismatch");
        Code(results[notMine]).ShouldBe("sync.file_bytes_missing");
        results.Values.ShouldAllBe(r => r.GetProperty("status").GetString() == "rejected");
        answer.GetProperty("refused").GetInt32().ShouldBe(4);

        // Recorded on its own, one row naming bytes that were never staged. It is filed with the
        // refusals although the exchange is a 200: the batch is accepted and the row inside it is
        // refused, which is exactly the difference a client has to read the row's status for.
        var unstaged = Guid.CreateVersion7();
        var unstagedBatch = Guid.CreateVersion7();
        var recorded = await new ContractFixture()
            .Literal(Hash(bytes), "sha256").Name(set, "set").Name(unstagedBatch, "batch").Name(unstaged, "document")
            .RecordPostAsync(
                Path.Combine("16-errors", "file-bytes-missing"),
                phone,
                $"/api/v1/sync/sets/{set}/upload",
                Upload(unstagedBatch, new { id = unstaged, title = "Never staged", fileName = "IMG_0102.jpg", sha256 = Hash(bytes) }));
        var unstagedRow = recorded.GetProperty("documents").EnumerateArray().Single();
        unstagedRow.GetProperty("status").GetString().ShouldBe("rejected");
        Code(unstagedRow).ShouldBe("sync.file_bytes_missing");
        (await CountAsync(db => db.Documents.IgnoreQueryFilters().CountAsync(d => d.Id == unstaged))).ShouldBe(0);

        var ids = new[] { wrongHash, noBytes, wrongSize, notMine };
        (await CountAsync(db => db.Documents.IgnoreQueryFilters().CountAsync(d => ids.Contains(d.Id)))).ShouldBe(0);

        // Refused rows leave the bytes waiting for the corrected row, which then adopts them.
        (await CountAsync(db => db.UploadSessions.CountAsync(s => s.Id == staged || s.Id == colleaguesStaged))).ShouldBe(2);
        var corrected = await UploadAsync(phone, set, Upload(
            Guid.CreateVersion7(), new { id = wrongHash, sha256 = Hash(bytes), stagedUploadId = staged }));
        corrected.GetProperty("documents").EnumerateArray().Single().GetProperty("status").GetString().ShouldBe("created");
    }

    [Fact]
    public async Task The_same_hash_changes_only_what_is_said_and_a_new_hash_makes_a_revision_that_keeps_the_old()
    {
        var first = Corpus.GeotaggedJpeg(45.3, 25.3);
        var documentId = Guid.CreateVersion7();
        var created = await UploadAsync(phone, set, Upload(Guid.CreateVersion7(), new
        {
            id = documentId,
            title = "Sump",
            sha256 = Hash(first),
            stagedUploadId = await StageAsync(phone, "sump.jpg", first),
        }));
        var revision = Revision(created);

        // Nothing different: answered unchanged, the revision where it was.
        var same = await UploadAsync(phone, set, Upload(Guid.CreateVersion7(), new
        {
            id = documentId, baseRevision = revision, title = "Sump", sha256 = Hash(first),
        }));
        var unchanged = same.GetProperty("documents").EnumerateArray().Single();
        unchanged.GetProperty("status").GetString().ShouldBe("unchanged");

        // The same hash with a new title and caption: what is said changes, the bytes do not.
        var described = await UploadAsync(phone, set, Upload(Guid.CreateVersion7(), new
        {
            id = documentId, baseRevision = revision, title = "Terminal sump", description = "Flooded", sha256 = Hash(first),
        }));
        described.GetProperty("documents").EnumerateArray().Single().GetProperty("status").GetString().ShouldBe("updated");
        var afterDescribe = Revision(described);
        afterDescribe.ShouldNotBe(revision);
        (await CountAsync(db => db.DocumentVersions.CountAsync(v => v.DocumentId == documentId))).ShouldBe(1);
        (await CountAsync(db => db.Documents.Where(d => d.Id == documentId).Select(d => d.Title).SingleAsync()))
            .ShouldBe("Terminal sump");
        (await CountAsync(db => db.PhotoDetails.Where(p => p.DocumentId == documentId).Select(p => p.Caption).SingleAsync()))
            .ShouldBe("Flooded");

        // A stale revision is a conflict, answered with the revision that won.
        var stale = await UploadAsync(phone, set, Upload(Guid.CreateVersion7(), new
        {
            id = documentId, baseRevision = revision, title = "Late edit", sha256 = Hash(first),
        }));
        var conflict = stale.GetProperty("documents").EnumerateArray().Single();
        conflict.GetProperty("status").GetString().ShouldBe("conflict");
        conflict.GetProperty("code").GetString().ShouldBe("sync.conflict");
        (conflict.GetProperty("revision").GetDateTimeOffset().UtcTicks / 10).ShouldBe(afterDescribe.UtcTicks / 10);

        // A new hash needs new bytes.
        var second = Corpus.Jpeg();
        var unsent = await UploadAsync(phone, set, Upload(Guid.CreateVersion7(), new
        {
            id = documentId, baseRevision = afterDescribe, sha256 = Hash(second),
        }));
        Code(unsent.GetProperty("documents").EnumerateArray().Single()).ShouldBe("sync.file_bytes_missing");

        // With them, a new revision becomes current and the first stays in the history.
        var replaced = await UploadAsync(phone, set, Upload(Guid.CreateVersion7(), new
        {
            id = documentId,
            baseRevision = afterDescribe,
            sha256 = Hash(second),
            stagedUploadId = await StageAsync(phone, "sump-2.jpg", second),
        }));
        replaced.GetProperty("documents").EnumerateArray().Single().GetProperty("status").GetString().ShouldBe("updated");
        Revision(replaced).ShouldNotBe(afterDescribe);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var versions = await db.DocumentVersions.AsNoTracking()
            .Where(v => v.DocumentId == documentId)
            .OrderBy(v => v.VersionNumber)
            .ToListAsync();
        versions.Count.ShouldBe(2);
        versions[0].IsCurrent.ShouldBeFalse();
        versions[1].IsCurrent.ShouldBeTrue();
        (await db.StoredFiles.AsNoTracking().SingleAsync(f => f.DocumentVersionId == versions[0].Id)).Sha256.ShouldBe(Hash(first));
        (await db.StoredFiles.AsNoTracking().SingleAsync(f => f.DocumentVersionId == versions[1].Id)).Sha256.ShouldBe(Hash(second));
        (await db.ImportBatchItems.AsNoTracking().CountAsync(i => i.DocumentVersionId == versions[1].Id)).ShouldBe(1);
    }

    [Fact]
    public async Task A_caption_or_a_revision_written_in_the_browser_moves_the_document_so_a_stale_phone_row_is_a_conflict()
    {
        var first = Corpus.GeotaggedJpeg(45.31, 25.31);
        var documentId = Guid.CreateVersion7();
        var revision = Revision(await UploadAsync(phone, set, Upload(Guid.CreateVersion7(), new
        {
            id = documentId, title = "Gallery", description = "From the phone", sha256 = Hash(first),
            stagedUploadId = await StageAsync(phone, "gallery.jpg", first),
        })));

        // A caption written in the browser lives beside the document row, and still moves it.
        var credit = await phone.PutAsJsonAsync($"/api/v1/photos/{documentId}/credit", new { caption = "Written on the web" });
        credit.StatusCode.ShouldBe(HttpStatusCode.OK, await credit.Content.ReadAsStringAsync());

        var afterCaption = await UploadAsync(phone, set, Upload(Guid.CreateVersion7(), new
        {
            id = documentId, baseRevision = revision, description = "Written on the phone", sha256 = Hash(first),
        }));
        var captionConflict = afterCaption.GetProperty("documents").EnumerateArray().Single();
        captionConflict.GetProperty("status").GetString().ShouldBe("conflict");
        (await CountAsync(db => db.PhotoDetails.Where(p => p.DocumentId == documentId).Select(p => p.Caption).SingleAsync()))
            .ShouldBe("Written on the web");
        var revisionAfterCaption = captionConflict.GetProperty("revision").GetDateTimeOffset();
        revisionAfterCaption.ShouldNotBe(revision);

        // New bytes uploaded in the browser move it too: a phone row read before them, carrying the
        // old hash, is a conflict rather than an invitation to put the old bytes back on top.
        var fileId = await CountAsync(db => (from v in db.DocumentVersions
                                             join f in db.StoredFiles on v.Id equals f.DocumentVersionId
                                             where v.DocumentId == documentId && v.IsCurrent
                                             select f.Id).SingleAsync());
        using (var form = new MultipartFormDataContent())
        {
            var part = new ByteArrayContent(Corpus.GeotaggedJpeg(45.311, 25.311));
            part.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(part, "file", "gallery-web.jpg");
            var version = await phone.PostAsync($"/api/v1/files/{fileId}/versions", form);
            version.StatusCode.ShouldBe(HttpStatusCode.Created, await version.Content.ReadAsStringAsync());
        }

        var afterVersion = await UploadAsync(phone, set, Upload(Guid.CreateVersion7(), new
        {
            id = documentId, baseRevision = revisionAfterCaption, title = "Gallery, renamed", sha256 = Hash(first),
        }));
        var versionConflict = afterVersion.GetProperty("documents").EnumerateArray().Single();
        versionConflict.GetProperty("status").GetString().ShouldBe("conflict");
        versionConflict.GetProperty("code").GetString().ShouldBe("sync.conflict");
        (await CountAsync(db => db.DocumentVersions.CountAsync(v => v.DocumentId == documentId))).ShouldBe(2);
        (await CountAsync(db => db.Documents.Where(d => d.Id == documentId).Select(d => d.Title).SingleAsync()))
            .ShouldBe("Gallery");
    }

    [Fact]
    public async Task A_phone_holding_a_copy_names_its_base_version_and_its_caption_edit_leaves_the_bytes_alone()
    {
        var original = Corpus.GeotaggedJpeg(45.32, 25.32);
        var documentId = Guid.CreateVersion7();
        var revision = Revision(await UploadAsync(phone, set, Upload(Guid.CreateVersion7(), new
        {
            id = documentId, title = "Chamber", sha256 = Hash(original),
            stagedUploadId = await StageAsync(phone, "chamber.jpg", original),
        })));
        var versionId = await CountAsync(db => db.DocumentVersions
            .Where(v => v.DocumentId == documentId && v.IsCurrent).Select(v => v.Id).SingleAsync());

        // The phone holds a copy without location data, whose hash is not the stored file's. With
        // its base version named, a caption edit is a caption edit — with the copy's hash, or none.
        var copyHash = Hash(Corpus.Jpeg());
        var described = await UploadAsync(phone, set, Upload(Guid.CreateVersion7(), new
        {
            id = documentId, baseRevision = revision, baseVersionId = versionId, description = "Calcite", sha256 = copyHash,
        }));
        var result = described.GetProperty("documents").EnumerateArray().Single();
        result.GetProperty("status").GetString().ShouldBe("updated");
        revision = result.GetProperty("revision").GetDateTimeOffset();

        var noHash = await UploadAsync(phone, set, Upload(Guid.CreateVersion7(), new
        {
            id = documentId, baseRevision = revision, baseVersionId = versionId, title = "Calcite chamber",
        }));
        result = noHash.GetProperty("documents").EnumerateArray().Single();
        result.GetProperty("status").GetString().ShouldBe("updated");
        revision = result.GetProperty("revision").GetDateTimeOffset();

        (await CountAsync(db => db.DocumentVersions.CountAsync(v => v.DocumentId == documentId))).ShouldBe(1);
        (await CountAsync(db => db.PhotoDetails.Where(p => p.DocumentId == documentId).Select(p => p.Caption).SingleAsync()))
            .ShouldBe("Calcite");
        (await CountAsync(db => db.Documents.Where(d => d.Id == documentId).Select(d => d.Title).SingleAsync()))
            .ShouldBe("Calcite chamber");

        // A base version that is no longer current is a conflict, whatever the revision says.
        var stale = await UploadAsync(phone, set, Upload(Guid.CreateVersion7(), new
        {
            id = documentId, baseRevision = revision, baseVersionId = Guid.CreateVersion7(), description = "Late",
        }));
        stale.GetProperty("documents").EnumerateArray().Single().GetProperty("status").GetString().ShouldBe("conflict");

        // Staged bytes named with the base version are a new revision, which keeps the old one.
        var second = Corpus.GeotaggedJpeg(45.33, 25.33);
        var replaced = await UploadAsync(phone, set, Upload(Guid.CreateVersion7(), new
        {
            id = documentId, baseRevision = revision, baseVersionId = versionId, sha256 = Hash(second),
            stagedUploadId = await StageAsync(phone, "chamber-2.jpg", second),
        }));
        replaced.GetProperty("documents").EnumerateArray().Single().GetProperty("status").GetString().ShouldBe("updated");
        (await CountAsync(db => db.DocumentVersions.CountAsync(v => v.DocumentId == documentId))).ShouldBe(2);
    }

    [Fact]
    public async Task Removing_a_document_needs_the_right_to_remove_it_not_only_to_edit_it()
    {
        var bytes = Corpus.GeotaggedJpeg(45.4, 25.4);
        var documentId = Guid.CreateVersion7();
        var revision = Revision(await UploadAsync(phone, set, Upload(Guid.CreateVersion7(), new
        {
            id = documentId, title = "Survey sketch", sha256 = Hash(bytes),
            stagedUploadId = await StageAsync(phone, "sketch.jpg", bytes),
        })));

        // The colleague may edit it, and may not remove it.
        var edited = await UploadAsync(colleague, colleagueSet, Upload(Guid.CreateVersion7(), new
        {
            id = documentId, baseRevision = revision, title = "Survey sketch, corrected", sha256 = Hash(bytes),
        }));
        edited.GetProperty("documents").EnumerateArray().Single().GetProperty("status").GetString().ShouldBe("updated");
        revision = Revision(edited);

        var refused = await UploadAsync(colleague, colleagueSet, Upload(Guid.CreateVersion7(), new
        {
            id = documentId, baseRevision = revision, deleted = true,
        }));
        Code(refused.GetProperty("documents").EnumerateArray().Single()).ShouldBe("sync.row_delete_forbidden");
        (await CountAsync(db => db.Documents.AnyAsync(d => d.Id == documentId))).ShouldBeTrue();

        // Its owner may.
        var removed = await UploadAsync(phone, set, Upload(Guid.CreateVersion7(), new
        {
            id = documentId, baseRevision = revision, deleted = true,
        }));
        removed.GetProperty("documents").EnumerateArray().Single().GetProperty("status").GetString().ShouldBe("deleted");
        (await CountAsync(db => db.Documents.AnyAsync(d => d.Id == documentId))).ShouldBeFalse();
        (await CountAsync(db => db.Documents.IgnoreQueryFilters().Where(d => d.Id == documentId)
            .Select(d => d.DeletedByUserId).SingleAsync())).ShouldBe(phoneUserId);

        // Gone is gone: removing it again is nothing, editing it is told so.
        var again = await UploadAsync(phone, set, Upload(Guid.CreateVersion7(),
            new { id = documentId, baseRevision = revision, deleted = true }));
        again.GetProperty("documents").EnumerateArray().Single().GetProperty("status").GetString().ShouldBe("unchanged");
        var edit = await UploadAsync(phone, set, Upload(Guid.CreateVersion7(),
            new { id = documentId, baseRevision = revision, title = "Back", sha256 = Hash(bytes) }));
        Code(edit.GetProperty("documents").EnumerateArray().Single()).ShouldBe("sync.row_deleted");
    }

    [Fact]
    public async Task A_document_row_is_validated_and_needs_an_account()
    {
        using var anonymous = factory.CreateClient();
        var body = Upload(Guid.CreateVersion7(), new { id = Guid.CreateVersion7(), sha256 = new string('a', 64) });
        (await anonymous.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", body)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);

        // A row that is not a removal states its hash, and the hash is a SHA-256.
        foreach (var row in new object[]
        {
            new { id = Guid.CreateVersion7() },
            new { id = Guid.CreateVersion7(), sha256 = "not-a-hash" },
            new { id = Guid.CreateVersion7(), sha256 = new string('a', 64), title = new string('t', 301) },
            // Wider than the column a stored file's name is kept in.
            new { id = Guid.CreateVersion7(), sha256 = new string('a', 64), fileName = new string('f', 296) + ".jpg" },
            // Naming a base version excuses the hash only of a row that brings no bytes.
            new { id = Guid.CreateVersion7(), baseVersionId = Guid.CreateVersion7(), stagedUploadId = Guid.CreateVersion7() },
        })
        {
            var response = await phone.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", Upload(Guid.CreateVersion7(), row));
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        }

        // Bytes staged under a name a stored file could not keep are refused before any arrive.
        var longName = await phone.PostAsJsonAsync("/api/v1/files/uploads", new
        {
            fileName = new string('f', 296) + ".jpg",
            sizeBytes = 100,
            stageForSync = true,
        });
        longName.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await longName.Content.ReadAsStringAsync());

        var twice = Guid.CreateVersion7();
        var duplicated = await phone.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", Upload(
            Guid.CreateVersion7(),
            new { id = twice, deleted = true },
            new { id = twice, deleted = true }));
        duplicated.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // A removal of a document that is not here needs no hash and is nothing.
        var nothing = await UploadAsync(phone, set, Upload(Guid.CreateVersion7(), new { id = Guid.CreateVersion7(), deleted = true }));
        nothing.GetProperty("documents").EnumerateArray().Single().GetProperty("status").GetString().ShouldBe("unchanged");

        // An upload of feature rows alone is answered without a documents member at all.
        var rowsOnly = await phone.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            rows = new[] { new { id = Guid.CreateVersion7(), kind = "cave", deleted = true } },
        });
        var rowsAnswer = await rowsOnly.Content.ReadFromJsonAsync<JsonElement>();
        rowsOnly.StatusCode.ShouldBe(HttpStatusCode.OK, rowsAnswer.ToString());
        rowsAnswer.TryGetProperty("documents", out _).ShouldBeFalse();
    }

    private static object Upload(Guid batchId, params object[] documents) =>
        new { batchId, contractVersion = 1, documents };

    private static async Task<JsonElement> UploadAsync(HttpClient client, Guid setId, object body)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/sync/sets/{setId}/upload", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static DateTimeOffset Revision(JsonElement answer) =>
        answer.GetProperty("documents").EnumerateArray().Single().GetProperty("revision").GetDateTimeOffset();

    private static string? Code(JsonElement result) =>
        result.TryGetProperty("code", out var code) ? code.GetString() : null;

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private async Task<T> CountAsync<T>(Func<SilexGisDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<SilexGisDbContext>());
    }

    private static async Task<Guid> CreateSetAsync(HttpClient client, string marker)
    {
        var response = await client.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker} {Guid.NewGuid():N}"[..30],
            uploadVisibility = "private",
            rootFeatureIds = Array.Empty<Guid>(),
            settings = new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> StageAsync(HttpClient client, string fileName, byte[] bytes)
    {
        var opened = await client.PostAsJsonAsync("/api/v1/files/uploads", new
        {
            fileName,
            sizeBytes = bytes.Length,
            stageForSync = true,
        });
        var payload = await opened.Content.ReadAsStringAsync();
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var session = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var appended = await client.PutAsync($"/api/v1/files/uploads/{session}?offset=0", content);
        appended.StatusCode.ShouldBe(HttpStatusCode.OK, await appended.Content.ReadAsStringAsync());

        var completed = await client.PostAsync($"/api/v1/files/uploads/{session}/complete", null);
        completed.StatusCode.ShouldBe(HttpStatusCode.OK, await completed.Content.ReadAsStringAsync());
        return session;
    }
}
