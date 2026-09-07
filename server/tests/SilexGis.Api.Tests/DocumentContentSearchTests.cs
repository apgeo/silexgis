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
/// Searching documents by what they say, over the wire: the section the existing search grew
/// rather than a surface of its own, and — the part worth testing — who it refuses to find
/// things for.
/// <para>
/// Every negative here is built, never assumed. The caller it withholds from is a Viewer, who
/// holds nothing over documents beyond the built-ins, so a document they do not find is one no
/// grant of theirs reaches rather than one that happened not to match; and every negative is
/// paired in the same test with the caller who does find it, so a search that returned nothing
/// to anybody could not pass.
/// </para>
/// <para>
/// Every uploaded page carries a nonsense word unique to the run and every query asks for it.
/// The database is shared with the rest of the collection, so a result counted over the whole
/// archive would say nothing about what this query decided.
/// </para>
/// </summary>
public sealed class DocumentContentSearchTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string nonce = Nonce();

    private HttpClient owner = null!;     // Editor — uploads everything below
    private HttpClient stranger = null!;  // Viewer — no entries, no club, no reach
    private HttpClient reader = null!;    // Viewer — Read on one document, and only Read
    private HttpClient writer = null!;    // Viewer — Read and Write on that same document
    private Guid readerId;
    private Guid writerId;

    public DocumentContentSearchTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-search-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"cs-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"cs-own-{suffix}@t.local");

        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cs-str-{suffix}@t.local");
        stranger = await AuthHelper.BearerClientAsync(factory, $"cs-str-{suffix}@t.local");

        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cs-rd-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"cs-rd-{suffix}@t.local");

        writerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cs-wr-{suffix}@t.local");
        writer = await AuthHelper.BearerClientAsync(factory, $"cs-wr-{suffix}@t.local");
    }

    [Fact]
    public async Task Finds_a_document_by_words_in_it_and_quotes_them_back_as_they_were_written()
    {
        var fileId = await UploadAsync(
            "raport.txt", $"Peștera Ursilor are galerii lungi. {nonce}");
        await WaitForExtractionAsync(fileId);

        // Typed without diacritics, as somebody in a hurry types it.
        var hits = await SearchDocumentsAsync(owner, $"pestera {nonce}");
        var hit = hits.Items.ShouldHaveSingleItem();

        hit.GetProperty("title").GetString().ShouldBe("raport.txt");
        hit.GetProperty("fileId").GetGuid().ShouldBe(fileId);
        hit.GetProperty("isCurrentVersion").GetBoolean().ShouldBeTrue();

        // The archive is quoted as it was written rather than as it was searched for.
        hit.GetProperty("snippet").GetString()!.ShouldContain("[[Peștera]]");

        // A plain text file arrives as one row however long it is, so the result says its number
        // counts nothing a reader would recognise instead of announcing "page 1".
        hit.GetProperty("division").GetString().ShouldBe("whole");

        hits.Total.ShouldBe(1);
        hits.Page.ShouldBe(1);
    }

    /// <summary>
    /// The section carries a total because it is paged, and a total beside a filtered list is
    /// only honest if the same rules produced both — otherwise the number announces exactly what
    /// the list declined to show.
    /// </summary>
    [Fact]
    public async Task A_document_out_of_a_caller_reach_is_neither_matched_nor_counted_for_them()
    {
        var openId = await UploadAsync("deschis.txt", $"Galerie cartată în 1980. {nonce}");
        var closedId = await UploadAsync("inchis.txt", $"Galerie cartată în 1990. {nonce}");
        await WaitForExtractionAsync(openId);
        await WaitForExtractionAsync(closedId);
        await SetVisibilityAsync(await DocumentIdOfAsync(openId), Visibility.Authenticated);

        // The uploader sees both, so the difference below is about the documents rather than
        // about a search that finds nothing.
        var mine = await SearchDocumentsAsync(owner, $"galerie {nonce}");
        mine.Items.Count.ShouldBe(2);
        mine.Total.ShouldBe(2);

        // The stranger holds no entry over documents and is in no club: the second document is
        // genuinely out of reach rather than merely dropped late, and the first still is in
        // reach, which is what makes this a statement about that document.
        var theirs = await SearchDocumentsAsync(stranger, $"galerie {nonce}");
        theirs.Items.ShouldHaveSingleItem().GetProperty("fileId").GetGuid().ShouldBe(openId);
        theirs.Total.ShouldBe(1);
    }

    /// <summary>
    /// A revision is replaced precisely when something in it had to go, so finding the removed
    /// paragraph by searching for it would make a new upload a retraction that retracts nothing.
    /// Whoever may replace the revision may still read what it said.
    /// </summary>
    [Fact]
    public async Task Superseded_text_is_found_only_by_a_caller_who_could_have_replaced_it()
    {
        var firstId = await UploadAsync(
            "teren.txt", $"Intrarea este pe terenul lui Wilkinson. {nonce}");
        await WaitForExtractionAsync(firstId);
        var secondId = await UploadVersionAsync(firstId, "teren.txt", $"Intrarea este pe teren privat. {nonce}");
        await WaitForExtractionAsync(secondId);

        var documentId = await DocumentIdOfAsync(firstId);
        await GrantAsync(readerId, AccessAction.Read, documentId);
        await GrantAsync(writerId, AccessAction.Read | AccessAction.Write, documentId);

        // Both callers may read the document and both find what it says now.
        (await SearchDocumentsAsync(reader, $"privat {nonce}", superseded: true))
            .Items.ShouldHaveSingleItem();
        (await SearchDocumentsAsync(writer, $"privat {nonce}", superseded: true))
            .Items.ShouldHaveSingleItem();

        // The name was removed by the second upload. Read alone does not find it again, and the
        // total says the same thing the rows do.
        var refused = await SearchDocumentsAsync(reader, $"wilkinson {nonce}", superseded: true);
        refused.Items.ShouldBeEmpty();
        refused.Total.ShouldBe(0);

        var allowed = await SearchDocumentsAsync(writer, $"wilkinson {nonce}", superseded: true);
        var hit = allowed.Items.ShouldHaveSingleItem();
        hit.GetProperty("isCurrentVersion").GetBoolean().ShouldBeFalse();
        hit.GetProperty("versionNumber").GetInt32().ShouldBe(1);

        // And the old text is opt-in even then: asking the ordinary way searches what the
        // documents say now.
        (await SearchDocumentsAsync(writer, $"wilkinson {nonce}")).Items.ShouldBeEmpty();
    }

    /// <summary>
    /// Being attached to a cave whose position is guarded is not a reason to withhold a
    /// document, hide it from this list or strip its text. What must not be disclosed is the
    /// pairing — and a hit here names no feature at all, so there is none in it to disclose.
    /// </summary>
    [Fact]
    public async Task A_document_on_a_protected_cave_is_found_by_its_text_while_the_pairing_stays_withheld()
    {
        var caveId = await CreateProtectedCaveAsync();
        var fileId = await UploadAsync("cartare.txt", $"Cartarea sălii mari, cu topofil. {nonce}");
        await WaitForExtractionAsync(fileId);
        var attachmentId = await AttachAsync(fileId, caveId);
        await SetVisibilityAsync(await DocumentIdOfAsync(fileId), Visibility.Authenticated);

        // The caller the rule is about, and the fixture proving they are that caller: the cave
        // is readable to them and its position is not.
        var seenCave = await ReadJsonAsync(await reader.GetAsync($"/api/v1/caves/{caveId}"));
        seenCave.GetProperty("locationProtected").GetBoolean().ShouldBeTrue();
        seenCave.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();

        // The document is found by its words, and its words are quoted back — to a caller who
        // may read the document, which is the only question search asks about it.
        var hit = (await SearchDocumentsAsync(reader, $"topofil {nonce}")).Items.ShouldHaveSingleItem();
        hit.GetProperty("fileId").GetGuid().ShouldBe(fileId);
        hit.GetProperty("snippet").GetString()!.ShouldContain("[[topofil]]");

        // Nothing in the hit says which cave it is about, or where anything is. Checked over the
        // whole shape rather than field by field, so a field added later cannot quietly carry
        // one back in.
        foreach (var property in hit.EnumerateObject())
        {
            property.Name.ShouldNotContain("feature", Case.Insensitive);
            property.Name.ShouldNotContain("cave", Case.Insensitive);
            property.Name.ShouldNotContain("attach", Case.Insensitive);
            property.Name.ShouldNotContain("geom", Case.Insensitive);
        }

        // The pairing itself stays withheld from the same caller in the same breath, and is told
        // to the one who may place the cave exactly — so the absence above is the rule rather
        // than an empty fixture.
        (await AttachmentIdsAsync(reader, caveId)).ShouldBeEmpty();
        (await AttachmentIdsAsync(owner, caveId)).ShouldBe([attachmentId]);

        // And the uploader finds the document too, so the reader's hit is not the only one the
        // query can produce.
        (await SearchDocumentsAsync(owner, $"topofil {nonce}")).Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Paging_walks_the_same_ranking_and_repeats_the_same_total()
    {
        foreach (var index in Enumerable.Range(1, 3))
        {
            await WaitForExtractionAsync(
                await UploadAsync($"pagina-{index}.txt", $"Notă de teren {index}. {nonce}"));
        }

        var first = await SearchDocumentsAsync(owner, nonce, page: 1);
        var second = await SearchDocumentsAsync(owner, nonce, page: 2);

        // Ten to a page, so the second page of three hits is empty. The total is counted by the
        // same statement that chooses the rows and travels back on them, which is what keeps it
        // from ever announcing rows the list declined to show — and the price of that is stated
        // here rather than left to be discovered: a slice past the end carries no row, so it
        // carries no total either and reports nothing rather than a number counted separately.
        first.Items.Count.ShouldBe(3);
        first.Total.ShouldBe(3);
        second.Items.ShouldBeEmpty();
        second.PageSize.ShouldBe(first.PageSize);
        second.Total.ShouldBe(0);
    }

    [Fact]
    public async Task The_section_keeps_the_shape_the_rest_of_the_search_has()
    {
        var response = await owner.GetAsync($"/api/v1/search?q={nonce}");
        var payload = await ReadJsonAsync(response);

        // One surface, three sections: documents joined the search that already existed rather
        // than starting a second one beside it.
        payload.TryGetProperty("features", out _).ShouldBeTrue();
        payload.TryGetProperty("trips", out _).ShouldBeTrue();
        payload.TryGetProperty("documents", out _).ShouldBeTrue();

        // The same rules the whole endpoint has: signed in, and a query worth running.
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/search?q={nonce}")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);

        var tooShort = await owner.GetAsync("/api/v1/search?q=p");
        tooShort.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await tooShort.Content.ReadAsStringAsync()).RootElement
            .GetProperty("code").GetString().ShouldBe("search.query_too_short");
    }

    // ---- helpers ----

    private sealed record DocumentSection(IReadOnlyList<JsonElement> Items, int Page, int PageSize, int Total);

    /// <summary>
    /// One term, all letters, unique to the run: a word only this suite's uploads contain, so a
    /// total computed over the whole archive is still a total over exactly these documents.
    /// </summary>
    private static string Nonce()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        return "zq" + new string([.. bytes.Take(10).Select(b => (char)('a' + (b % 26)))]);
    }

    private async Task<DocumentSection> SearchDocumentsAsync(
        HttpClient client, string query, bool superseded = false, int? page = null)
    {
        var url = $"/api/v1/search?q={Uri.EscapeDataString(query)}";
        if (superseded)
        {
            url += "&includeSuperseded=true";
        }

        if (page is { } wanted)
        {
            url += $"&documentPage={wanted}";
        }

        var documents = (await ReadJsonAsync(await client.GetAsync(url))).GetProperty("documents");
        return new DocumentSection(
            [.. documents.GetProperty("items").EnumerateArray()],
            documents.GetProperty("page").GetInt32(),
            documents.GetProperty("pageSize").GetInt32(),
            documents.GetProperty("totalItems").GetInt32());
    }

    private async Task<Guid> UploadAsync(string fileName, string text)
    {
        using var form = BuildForm(fileName, Encoding.UTF8.GetBytes(text));
        // These fixtures upload byte-identical content more than once, which the store now
        // warns about. Saying yes up front is what a person would do; deduplication is
        // asserted in its own suite rather than incidentally here.
        var response = await owner.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> UploadVersionAsync(Guid fileId, string fileName, string text)
    {
        using var form = BuildForm(fileName, Encoding.UTF8.GetBytes(text));
        var response = await owner.PostAsync($"/api/v1/files/{fileId}/versions", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static MultipartFormDataContent BuildForm(string fileName, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("text/plain");
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    private async Task<Guid> CreateProtectedCaveAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();

        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Content {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected = true,
            explorationStatus = "Unknown",
            isShowCave = false,
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

    private static async Task<Guid[]> AttachmentIdsAsync(HttpClient client, Guid caveId)
    {
        var response = await client.GetAsync($"/api/v1/attachments/?entityType=feature&entityId={caveId}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return [.. JsonDocument.Parse(payload).RootElement.EnumerateArray()
            .Select(a => a.GetProperty("id").GetGuid())];
    }

    private async Task<Guid> DocumentIdOfAsync(Guid fileId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);
        var version = await db.DocumentVersions.AsNoTracking().FirstAsync(v => v.Id == file.DocumentVersionId);
        return version.DocumentId;
    }

    private async Task SetVisibilityAsync(Guid documentId, Visibility visibility)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Documents.Where(d => d.Id == documentId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Visibility, visibility));
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

    /// <summary>
    /// Waits for the queued reading of a file to settle. The worker polls, so a search run
    /// immediately would be racing it rather than testing it.
    /// </summary>
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
        stranger?.Dispose();
        reader?.Dispose();
        writer?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
