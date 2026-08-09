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
/// One rule seen from three sides. A caller who can read a cave can read the documents whose
/// files hang on it, with no rule naming those documents at all — and the surfaces that list
/// documents, count them and find them by their words must all agree with that, or the archive
/// answers different questions depending on which door was used.
/// <para>
/// Every negative is built rather than assumed. The caller withheld from is a Viewer, who holds
/// nothing over documents beyond the built-ins, or one facing an explicit deny written against
/// the very document in question; and every negative is paired in the same test with the caller
/// or the document that is admitted, so a fixture that quietly stopped producing anything could
/// not pass for a security assertion.
/// </para>
/// <para>
/// Uploads carry a nonsense word unique to the run and every search asks for it, because the
/// database is shared with the rest of the collection and a total counted over the whole archive
/// would say nothing about what this query decided.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AttachmentReachListingTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string nonce = Nonce();

    private HttpClient owner = null!;   // Editor — uploads, files, attaches
    private HttpClient reader = null!;  // Viewer — no entry over documents, reaches only through the cave
    private Guid readerId;

    public AttachmentReachListingTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-reach-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"reach-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"reach-own-{suffix}@t.local");

        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"reach-rd-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"reach-rd-{suffix}@t.local");
    }

    /// <summary>
    /// Filing something into a cabinet must put it on that shelf for everyone who may read it,
    /// however they may read it. The count on the shelf comes out of the same query as the rows,
    /// so it can neither overstate what was shown nor understate it.
    /// </summary>
    [Fact]
    public async Task A_document_reached_only_through_an_attachment_is_listed_on_its_shelf_and_counted_there()
    {
        var caveId = await CreateCaveAsync();
        var shelf = await CreateCabinetAsync();

        var attached = await UploadAsync("hanging.txt", "on a cave");
        await AttachAsync(attached, caveId);
        var attachedDocument = await DocumentIdOfAsync(attached);
        await FileAsync(shelf, attachedDocument);

        var alone = await UploadAsync("alone.txt", "on nothing");
        var aloneDocument = await DocumentIdOfAsync(alone);
        await FileAsync(shelf, aloneDocument);

        // The fixture proving the caller is the caller the rule is about: the cave is readable to
        // them, the second document is not, and no entry of theirs names either document.
        (await reader.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/documents/{aloneDocument}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/documents/{attachedDocument}")).StatusCode
            .ShouldBe(HttpStatusCode.OK);

        // The shelf lists what the caller may read, which now includes the one they reach only
        // because it hangs off that cave — and withholds the one nothing reaches them for.
        var listed = await ListingAsync(reader, shelf);
        listed.Ids.ShouldBe([attachedDocument]);
        listed.Total.ShouldBe(1);

        // The number on the cabinet is that same number, out of that same rule.
        (await CountOfAsync(reader, shelf)).ShouldBe(1);

        // And the uploader sees both, so the reader's single row is a decision about the second
        // document rather than a shelf that turned out to be nearly empty.
        var theirs = await ListingAsync(owner, shelf);
        theirs.Ids.Order().ShouldBe(new[] { attachedDocument, aloneDocument }.Order());
        theirs.Total.ShouldBe(2);
        (await CountOfAsync(owner, shelf)).ShouldBe(2);
    }

    /// <summary>
    /// Reach is the last band of the rule and the only one that never refuses. Widening a listing
    /// with it must therefore never talk past a rule that denied the document — a deny something
    /// else could overrule would not be a deny.
    /// </summary>
    [Fact]
    public async Task Reach_widens_a_shelf_but_never_talks_past_a_rule_denying_the_document()
    {
        var caveId = await CreateCaveAsync();
        var shelf = await CreateCabinetAsync();

        var admitted = await UploadAsync("admitted.txt", "on a cave");
        await AttachAsync(admitted, caveId);
        var admittedDocument = await DocumentIdOfAsync(admitted);
        await FileAsync(shelf, admittedDocument);

        var refused = await UploadAsync("refused.txt", "on the same cave");
        await AttachAsync(refused, caveId);
        var refusedDocument = await DocumentIdOfAsync(refused);
        await FileAsync(shelf, refusedDocument);

        // Both hang on the same readable cave; one of them is then denied by name. Without the
        // deny the two are the same case, which is what makes the difference below the rule.
        await DenyAsync(readerId, refusedDocument);

        var listed = await ListingAsync(reader, shelf);
        listed.Ids.ShouldBe([admittedDocument]);
        listed.Total.ShouldBe(1);
        (await CountOfAsync(reader, shelf)).ShouldBe(1);

        // Fetching it by name refuses in the same breath, so the listing and the document surface
        // agree about the deny as well as about the reach.
        (await reader.GetAsync($"/api/v1/documents/{refusedDocument}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/documents/{admittedDocument}")).StatusCode
            .ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// The same rule asked of the words instead of the shelf: a document opened from a cave page
    /// must also be found by a phrase in it, and the total beside the results must count exactly
    /// the documents the results were drawn from.
    /// </summary>
    [Fact]
    public async Task A_document_reached_only_through_an_attachment_is_found_by_its_text_and_counted()
    {
        var caveId = await CreateCaveAsync();

        var attached = await UploadAsync("raport.txt", $"Galerie noua masurata cu topofilul. {nonce}");
        await WaitForExtractionAsync(attached);
        await AttachAsync(attached, caveId);

        var alone = await UploadAsync("privat.txt", $"Alta galerie, nelegata de nimic. {nonce}");
        await WaitForExtractionAsync(alone);

        // The uploader finds both, so what the reader does not find is a decision about that
        // document rather than a search that matched nothing.
        var theirs = await SearchAsync(owner, nonce);
        theirs.Total.ShouldBe(2);

        // The reader holds no entry over documents and is in no club. They find the one their
        // reading of the cave reaches, and only that one — and the total says one, because it was
        // counted by the statement that produced the row.
        var found = await SearchAsync(reader, nonce);
        found.Ids.ShouldBe([await DocumentIdOfAsync(attached)]);
        found.Total.ShouldBe(1);
    }

    // ---- helpers -------------------------------------------------------------------------

    private sealed record Listing(IReadOnlyList<Guid> Ids, int Total);

    private async Task<Listing> ListingAsync(HttpClient client, Guid cabinetId)
    {
        var page = await ReadJsonAsync(await client.GetAsync($"/api/v1/cabinets/{cabinetId}/documents"));
        return new Listing(
            [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())],
            page.GetProperty("totalItems").GetInt32());
    }

    private static async Task<int> CountOfAsync(HttpClient client, Guid cabinetId) =>
        (await ReadJsonAsync(await client.GetAsync($"/api/v1/cabinets/{cabinetId}")))
            .GetProperty("documentCount").GetInt32();

    private async Task<Listing> SearchAsync(HttpClient client, string term)
    {
        var documents = (await ReadJsonAsync(
                await client.GetAsync($"/api/v1/search?q={Uri.EscapeDataString(term)}")))
            .GetProperty("documents");
        return new Listing(
            [.. documents.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())],
            documents.GetProperty("totalItems").GetInt32());
    }

    private async Task<Guid> CreateCabinetAsync()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/cabinets", new
        {
            name = $"Shelf {Guid.NewGuid():N}"[..24],
            description = (string?)null,
            parentId = (Guid?)null,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task FileAsync(Guid cabinetId, Guid documentId)
    {
        var response = await owner.PutAsync($"/api/v1/cabinets/{cabinetId}/documents/{documentId}", null);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> CreateCaveAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();

        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Reach {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task AttachAsync(Guid fileId, Guid caveId)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/attachments/", new
        {
            fileId,
            entityType = "feature",
            entityId = caveId,
            role = "document",
            sortOrder = 0,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> UploadAsync(string fileName, string text)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        content.Headers.ContentType = new("text/plain");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        // These fixtures upload byte-identical content more than once, which the store now
        // warns about. Saying yes up front is what a person would do; deduplication is
        // asserted in its own suite rather than incidentally here.
        var response = await owner.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> DocumentIdOfAsync(Guid fileId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);
        var version = await db.DocumentVersions.AsNoTracking().FirstAsync(v => v.Id == file.DocumentVersionId);
        return version.DocumentId;
    }

    /// <summary>
    /// A rule refusing one person one document, written straight into storage — the authoring
    /// surface refuses rules handing out more than their author holds, which is exactly what a
    /// fixture needs to be able to do.
    /// </summary>
    private async Task DenyAsync(Guid userId, Guid documentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Deny,
            Domain = AccessDomain.Documents,
            Actions = AccessAction.Read,
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

            DateTimeOffset.UtcNow.ShouldBeLessThan(deadline, "the file's text was never read");
            await Task.Delay(200);
        }
    }

    /// <summary>
    /// One term, all letters, unique to the run: a word only this suite's uploads contain, so a
    /// total computed over the whole archive is still a total over exactly these documents.
    /// </summary>
    private static string Nonce()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        return "zq" + new string([.. bytes.Take(10).Select(b => (char)('a' + (b % 26)))]);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        reader?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
