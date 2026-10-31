// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
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
/// Deleting a document and putting it back, with the retention window left at its default — which
/// keeps everything.
/// <para>
/// A deleted document is marked, not removed, so the interesting assertions are about readers:
/// every surface that lists, finds, counts or opens a document has to treat a deleted one as
/// absent, and treat the restored one as it was. Each surface is asked three times — before,
/// after the delete, after the restore — so a reader that always answered empty could not pass.
/// </para>
/// <para>
/// Every refusal is built rather than assumed: the caller who is refused is a Viewer holding
/// nothing (or exactly Read), never a second Editor, because the seeded Editors read and write
/// every document.
/// </para>
/// </summary>
public sealed class DocumentDeletionTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string nonce = Nonce();

    private HttpClient owner = null!;      // Editor — uploads and edits everything
    private HttpClient stranger = null!;   // Viewer — no entries, no reach
    private HttpClient reader = null!;     // Viewer — Read on one document, and only Read
    private HttpClient writer = null!;     // Viewer — Read and Write on that same document
    private Guid readerId;
    private Guid writerId;

    public DocumentDeletionTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-docdelete-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"dd-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"dd-own-{suffix}@t.local");
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"dd-str-{suffix}@t.local");
        stranger = await AuthHelper.BearerClientAsync(factory, $"dd-str-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"dd-rd-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"dd-rd-{suffix}@t.local");
        writerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"dd-wr-{suffix}@t.local");
        writer = await AuthHelper.BearerClientAsync(factory, $"dd-wr-{suffix}@t.local");
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        owner?.Dispose();
        stranger?.Dispose();
        reader?.Dispose();
        writer?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    [Fact]
    public async Task A_deleted_document_is_absent_from_every_reader_and_comes_back_whole()
    {
        var (fileId, documentId) = await UploadAsync($"Galerie cartată. {nonce}");
        await WaitForExtractionAsync(fileId);
        var cabinetId = await CreateCabinetAsync();
        (await owner.PutAsync($"/api/v1/cabinets/{cabinetId}/documents/{documentId}", null))
            .StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        var caveId = await CreateCaveAsync();
        var attachmentId = await AttachAsync(fileId, caveId);
        var linkId = await LinkAsync(documentId, caveId);
        (await owner.PostAsJsonAsync(
            $"/api/v1/documents/{documentId}/comments/", new { body = "A remark.", anchorKind = "whole" }))
            .StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        // Before: every surface sees it, so what follows measures the delete.
        await AssertPresenceAsync(documentId, fileId, cabinetId, caveId, attachmentId, linkId, present: true);

        (await owner.DeleteAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await AssertPresenceAsync(documentId, fileId, cabinetId, caveId, attachmentId, linkId, present: false);

        // Nothing was removed: the rows are all still there, which is what makes the restore exact.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var row = await db.Documents.AsNoTracking().IgnoreQueryFilters().SingleAsync(d => d.Id == documentId);
            row.DeletedAt.ShouldNotBeNull();
            row.DeletedByUserId.ShouldNotBeNull();
            (await db.StoredFiles.AnyAsync(f => f.Id == fileId)).ShouldBeTrue();
            (await db.DocumentComments.AnyAsync(c => c.DocumentId == documentId)).ShouldBeTrue();
        }

        (await owner.PostAsync($"/api/v1/documents/{documentId}/restore", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await AssertPresenceAsync(documentId, fileId, cabinetId, caveId, attachmentId, linkId, present: true);
        var comments = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}/comments/");
        comments.GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task A_document_a_survey_archive_entry_is_built_on_is_refused_and_a_hidden_one_leaves_the_archive()
    {
        var caveId = await CreateCaveAsync();
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes("*begin x\n1 2 3.5 100 5\n*end x\n")), "file", "cave.svx" },
        };
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-sources", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var documentId = (await ReadJsonAsync(created)).GetProperty("documentId").GetGuid();

        // Refused while the entry stands: it would otherwise keep serving the deleted bytes.
        var refused = await owner.DeleteAsync($"/api/v1/documents/{documentId}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadJsonAsync(refused)).GetProperty("code").GetString().ShouldBe("document.in_use");
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-sources"))
            .GetArrayLength().ShouldBe(1);

        // Should a document be hidden by any other path, the archive treats it as absent too.
        await SetDeletedAsync(documentId, deleted: true);
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-sources"))
            .GetArrayLength().ShouldBe(0);
        await SetDeletedAsync(documentId, deleted: false);
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-sources"))
            .GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Reading_what_a_deleted_document_hangs_on_is_not_the_right_to_see_or_restore_it()
    {
        var (fileId, documentId) = await UploadAsync($"Attached. {nonce}");
        var caveId = await CreateCaveAsync();
        await AttachAsync(fileId, caveId);
        (await owner.DeleteAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The stranger reads the cave (visible to every account) and cannot edit it.
        (await DeletedAsync(stranger)).ShouldNotContain(i => i.GetProperty("id").GetGuid() == documentId);
        (await stranger.PostAsync($"/api/v1/documents/{documentId}/restore", null))
            .StatusCode.ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        (await DeletedAsync(owner)).ShouldContain(i => i.GetProperty("id").GetGuid() == documentId);
    }

    [Fact]
    public async Task A_cabinet_holding_only_deleted_documents_says_so()
    {
        var (_, documentId) = await UploadAsync($"Filed. {nonce}");
        var cabinetId = await CreateCabinetAsync();
        (await owner.PutAsync($"/api/v1/cabinets/{cabinetId}/documents/{documentId}", null))
            .StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        var live = await owner.DeleteAsync($"/api/v1/cabinets/{cabinetId}");
        live.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadJsonAsync(live)).GetProperty("code").GetString().ShouldBe("cabinet.not_empty");

        (await owner.DeleteAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var hidden = await owner.DeleteAsync($"/api/v1/cabinets/{cabinetId}");
        hidden.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadJsonAsync(hidden)).GetProperty("code").GetString().ShouldBe("cabinet.holds_deleted_documents");
    }

    [Fact]
    public async Task An_unfiled_document_leaves_the_inbox_and_the_deleted_list_offers_it_back()
    {
        var (_, documentId) = await UploadAsync($"Loose sheet. {nonce}");
        var (_, kept) = await UploadAsync($"Kept sheet. {nonce}");
        (await UnfiledIdsAsync(owner)).ShouldContain(documentId);

        (await owner.DeleteAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var inbox = await UnfiledIdsAsync(owner);
        inbox.ShouldNotContain(documentId);
        inbox.ShouldContain(kept);

        // Offered back, with nothing to count down to: this installation keeps what is deleted.
        var row = (await DeletedAsync(owner)).Single(i => i.GetProperty("id").GetGuid() == documentId);
        row.GetProperty("restorableUntil").ValueKind.ShouldBe(JsonValueKind.Null);
        row.GetProperty("deletedByUserId").ValueKind.ShouldNotBe(JsonValueKind.Null);
        (await DeletedAsync(owner)).ShouldNotContain(i => i.GetProperty("id").GetGuid() == kept);
        (await owner.GetFromJsonAsync<JsonElement>("/api/v1/documents/config"))
            .GetProperty("deletedRetentionDays").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Deleting_and_restoring_need_the_right_to_edit_and_say_nothing_to_one_who_cannot_read()
    {
        var (_, documentId) = await UploadAsync($"Guarded. {nonce}");
        await GrantAsync(readerId, AccessAction.Read, documentId);
        await GrantAsync(writerId, AccessAction.Read | AccessAction.Write, documentId);

        // Signed out: nothing at all.
        using var anonymous = factory.CreateClient();
        (await anonymous.DeleteAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync($"/api/v1/documents/{documentId}/restore", null))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/v1/documents/deleted")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/v1/documents/config")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Reads but may not edit: told so. Cannot read at all: told it is not there.
        var forbidden = await reader.DeleteAsync($"/api/v1/documents/{documentId}");
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadJsonAsync(forbidden)).GetProperty("code").GetString().ShouldBe("document.write_forbidden");
        var hidden = await stranger.DeleteAsync($"/api/v1/documents/{documentId}");
        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadJsonAsync(hidden)).GetProperty("code").GetString().ShouldBe("document.not_found");
        (await owner.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The one holding Read and Write may.
        (await writer.DeleteAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Deleted already: not there to delete again.
        (await owner.DeleteAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The list and the restore ask the same question, of a deleted document: the writer, the
        // owner (an Editor) see and restore it; the reader and the stranger neither see nor can.
        (await DeletedAsync(writer)).ShouldContain(i => i.GetProperty("id").GetGuid() == documentId);
        (await DeletedAsync(owner)).ShouldContain(i => i.GetProperty("id").GetGuid() == documentId);
        (await DeletedAsync(reader)).ShouldNotContain(i => i.GetProperty("id").GetGuid() == documentId);
        (await DeletedAsync(stranger)).ShouldNotContain(i => i.GetProperty("id").GetGuid() == documentId);

        (await reader.PostAsync($"/api/v1/documents/{documentId}/restore", null))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await stranger.PostAsync($"/api/v1/documents/{documentId}/restore", null))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await writer.PostAsync($"/api/v1/documents/{documentId}/restore", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Back, and restoring what is not deleted is a conflict rather than a silent success.
        (await owner.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var again = await owner.PostAsync($"/api/v1/documents/{documentId}/restore", null);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadJsonAsync(again)).GetProperty("code").GetString().ShouldBe("document.not_deleted");
        (await owner.PostAsync($"/api/v1/documents/{Guid.NewGuid()}/restore", null))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_deleted_document_is_kept_however_long_ago_unless_a_window_is_set()
    {
        var (_, documentId) = await UploadAsync($"Old news. {nonce}");
        (await owner.DeleteAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await BackdateDeletionAsync(documentId, TimeSpan.FromDays(400));

        // The default window is none, so a year-old deletion is neither hidden from the list nor
        // refused a restore.
        (await DeletedAsync(owner)).ShouldContain(i => i.GetProperty("id").GetGuid() == documentId);
        (await owner.PostAsync($"/api/v1/documents/{documentId}/restore", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task The_deletion_and_the_restoration_are_in_the_documents_history()
    {
        var (_, documentId) = await UploadAsync($"Audited. {nonce}");
        (await owner.DeleteAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.PostAsync($"/api/v1/documents/{documentId}/restore", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Read straight from the audit table: the stamp is an ordinary change of the document, so
        // the trail carries who and when without a second mechanism.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var entries = await db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityType == nameof(Document) && a.EntityId == documentId.ToString()
                && a.Action == AuditActions.Updated)
            .OrderBy(a => a.At)
            .ToListAsync();
        var stamps = entries.Where(e => e.Changes != null && e.Changes.Contains("DeletedAt")).ToList();
        stamps.Count.ShouldBe(2);
        stamps.ShouldAllBe(e => e.UserId != null);
    }

    // ---- surfaces ----

    private async Task AssertPresenceAsync(
        Guid documentId, Guid fileId, Guid cabinetId, Guid caveId, Guid attachmentId, Guid linkId, bool present)
    {
        var expected = present ? HttpStatusCode.OK : HttpStatusCode.NotFound;
        (await owner.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(expected);
        (await owner.GetAsync($"/api/v1/files/{fileId}")).StatusCode.ShouldBe(expected);
        (await owner.GetAsync($"/api/v1/documents/{documentId}/comments/")).StatusCode.ShouldBe(expected);

        var shelf = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/cabinets/{cabinetId}/documents");
        shelf.GetProperty("items").EnumerateArray().Any(i => i.GetProperty("id").GetGuid() == documentId)
            .ShouldBe(present, "the cabinet listing");

        var attachments = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/attachments/?entityType=feature&entityId={caveId}");
        attachments.EnumerateArray().Any(a => a.GetProperty("id").GetGuid() == attachmentId)
            .ShouldBe(present, "the attachments of the cave");

        var found = (await owner.GetFromJsonAsync<JsonElement>(
                $"/api/v1/search?q={Uri.EscapeDataString($"galerie {nonce}")}"))
            .GetProperty("documents").GetProperty("items").EnumerateArray()
            .Any(i => i.GetProperty("fileId").GetGuid() == fileId);
        found.ShouldBe(present, "the content search");

        // A link keeps its member row either way; what changes is whether the member resolves.
        var link = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reslinks/{linkId}");
        link.GetProperty("members").EnumerateArray()
            .Any(m => m.GetProperty("targetType").GetString() == "document"
                && m.GetProperty("targetId").GetGuid() == documentId
                && m.GetProperty("display").ValueKind != JsonValueKind.Null)
            .ShouldBe(present, "the document as a member of its link");
        (await owner.GetAsync($"/api/v1/reslinks/for-target?type=document&id={documentId}")).StatusCode
            .ShouldBe(expected, "the links of the document");
        var onCave = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reslinks/for-target?type=feature&id={caveId}");
        onCave.GetProperty("items").EnumerateArray()
            .Any(l => l.GetProperty("id").GetGuid() == linkId
                && l.GetProperty("members").EnumerateArray().Any(m =>
                    m.GetProperty("targetType").GetString() == "document"
                    && m.GetProperty("display").ValueKind != JsonValueKind.Null))
            .ShouldBe(present, "the document among the members of the cave's links");
    }

    // ---- helpers ----

    private static string Nonce()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        return "zq" + new string([.. bytes.Take(10).Select(b => (char)('a' + (b % 26)))]);
    }

    private async Task<(Guid FileId, Guid DocumentId)> UploadAsync(string text)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        content.Headers.ContentType = new("text/plain");
        using var form = new MultipartFormDataContent { { content, "file", $"{Guid.NewGuid():N}.txt" } };
        var response = await owner.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var fileId = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);
        var version = await db.DocumentVersions.AsNoTracking().FirstAsync(v => v.Id == file.DocumentVersionId);
        return (fileId, version.DocumentId);
    }

    private async Task<Guid> CreateCabinetAsync()
    {
        var response = await owner.PostAsJsonAsync(
            "/api/v1/cabinets/", new { name = $"Shelf {Guid.NewGuid():N}"[..14], description = (string?)null, parentId = (Guid?)null });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCaveAsync()
    {
        long caveTypeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Cave {Guid.NewGuid():N}"[..20],
            caveTypeId,
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> AttachAsync(Guid fileId, Guid caveId)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/attachments/", new
        {
            fileId,
            entityType = "feature",
            entityId = caveId,
            role = "document",
            sortOrder = 0,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> LinkAsync(Guid documentId, Guid caveId)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            members = new object[]
            {
                new { targetType = "document", targetId = documentId, isMain = false },
                new { targetType = "feature", targetId = caveId, isMain = false },
            },
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task SetDeletedAsync(Guid documentId, bool deleted)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var document = await db.Documents.IgnoreQueryFilters().FirstAsync(d => d.Id == documentId);
        document.DeletedAt = deleted ? DateTimeOffset.UtcNow : null;
        await db.SaveChangesAsync();
    }

    private static async Task<List<Guid>> UnfiledIdsAsync(HttpClient client)
    {
        var page = await client.GetFromJsonAsync<JsonElement>("/api/v1/documents/unfiled?pageSize=100");
        return [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())];
    }

    private static async Task<List<JsonElement>> DeletedAsync(HttpClient client)
    {
        var page = await client.GetFromJsonAsync<JsonElement>("/api/v1/documents/deleted?pageSize=100");
        return [.. page.GetProperty("items").EnumerateArray()];
    }

    private async Task BackdateDeletionAsync(Guid documentId, TimeSpan age)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var document = await db.Documents.IgnoreQueryFilters().FirstAsync(d => d.Id == documentId);
        document.DeletedAt = DateTimeOffset.UtcNow - age;
        await db.SaveChangesAsync();
    }

    private async Task GrantAsync(Guid userId, AccessAction actions, Guid documentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Documents,
            Actions = actions,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = documentId,
        });
        await db.SaveChangesAsync();
    }

    private async Task WaitForExtractionAsync(Guid fileId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (true)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var file = await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == fileId);
            if (file.TextExtraction is not TextExtractionState.Pending)
            {
                file.TextExtraction.ShouldBe(TextExtractionState.Extracted);
                return;
            }

            DateTimeOffset.UtcNow.ShouldBeLessThan(deadline, "text extraction did not settle");
            await Task.Delay(250);
        }
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
