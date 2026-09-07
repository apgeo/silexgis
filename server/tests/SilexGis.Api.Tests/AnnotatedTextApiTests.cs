// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Link-annotated text: writing one, reading it back, replacing its words, and what happens to
/// the links over the passages when the words move.
///
/// The re-measuring is what most of this file is about, because it is the part with a failure
/// mode nobody can see. An anchor whose document was edited under it still holds offsets that
/// resolve, still lands inside the text, and names the wrong sentence — so a test that only
/// checked the link still existed after an edit would pass on exactly the behaviour these
/// routes exist to prevent. Every assertion below therefore reads the words the anchor now
/// names, not just its state.
/// </summary>
public sealed class AnnotatedTextApiTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;      // Editor — writes the texts below
    private HttpClient stranger = null!;   // Viewer — holds nothing over documents
    private HttpClient anonymous = null!;

    public AnnotatedTextApiTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-annotated-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"at-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"at-own-{suffix}@t.local");

        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"at-str-{suffix}@t.local");
        stranger = await AuthHelper.BearerClientAsync(factory, $"at-str-{suffix}@t.local");

        anonymous = factory.CreateClient();
    }

    // ---- writing and reading ------------------------------------------------------------

    [Fact]
    public async Task A_text_is_written_and_read_back_as_the_blocks_it_was_written_as()
    {
        var created = await CreateAsync(
            "Field notes",
            Block("h2", "Galeria Mare"),
            Block("p", "From the second sump the passage widens."));

        created.GetProperty("title").GetString().ShouldBe("Field notes");
        created.GetProperty("versionNumber").GetInt32().ShouldBe(1);
        created.GetProperty("mayWrite").GetBoolean().ShouldBeTrue();

        // The length of the stream anchors are measured against, stated so a client can refuse
        // an out-of-range anchor before the round trip.
        created.GetProperty("canonicalLength").GetInt32()
            .ShouldBe("Galeria Mare\n\nFrom the second sump the passage widens.".Length);

        var read = await ReadJsonAsync(
            await owner.GetAsync($"/api/v1/annotated-texts/{created.GetProperty("documentId").GetGuid()}"));
        var blocks = read.GetProperty("blocks").EnumerateArray().ToList();
        blocks[0].GetProperty("type").GetString().ShouldBe("h2");
        blocks[1].GetProperty("text").GetString().ShouldBe("From the second sump the passage widens.");
    }

    [Fact]
    public async Task The_words_are_read_into_the_search_index_as_prose_rather_than_as_their_storage()
    {
        // The body is stored as JSON. A reader that let the structure through would put every
        // key and brace into the index, so a member searching for a field name would be handed
        // every annotated document in the installation.
        var created = await CreateAsync("Indexed", Block("p", "The chamber beyond the second sump."));
        var fileId = created.GetProperty("fileId").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await WaitForTextAsync(db, fileId);

        var page = await db.DocumentPages.AsNoTracking().SingleAsync(p => p.FileId == fileId);
        page.Text.ShouldNotBeNull();
        page.Text.ShouldBe("The chamber beyond the second sump.");
        page.Text.ShouldNotContain("blocks");
    }

    [Fact]
    public async Task A_body_the_format_refuses_is_refused_with_its_reason()
    {
        // Each of these breaks the offset contract rather than merely being untidy: a blank
        // block and a stray newline both produce runs of blank lines that normalising collapses,
        // which would silently move every anchor below them.
        (await CreateResponseAsync("Empty block", Block("p", ""))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CreateResponseAsync("Two lines", Block("p", "one\ntwo"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CreateResponseAsync("Padded", Block("p", " padded "))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var refused = await CreateResponseAsync("Padded", Block("p", " padded "));
        (await ReadCodeAsync(refused)).ShouldBe(AnnotatedText.BodyInvalidCode);
    }

    // ---- who may do what ----------------------------------------------------------------

    [Fact]
    public async Task Anonymous_reaches_none_of_it()
    {
        var created = await CreateAsync("Private notes", Block("p", "Nothing anonymous may read."));
        var id = created.GetProperty("documentId").GetGuid();

        (await anonymous.GetAsync($"/api/v1/annotated-texts/{id}")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/api/v1/annotated-texts", new { title = "x", blocks = new[] { Block("p", "x") } }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PutAsJsonAsync($"/api/v1/annotated-texts/{id}", new { blocks = new[] { Block("p", "x") } }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_text_somebody_may_not_read_is_reported_as_absent_rather_than_as_refused()
    {
        // Existence is not disclosed: a refusal and a document that is not there read the same.
        var created = await CreateAsync("Private notes", Block("p", "The entrance is behind the barn."));
        var id = created.GetProperty("documentId").GetGuid();

        var refused = await stranger.GetAsync($"/api/v1/annotated-texts/{id}");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadCodeAsync(refused)).ShouldBe("document.not_found");

        // The words themselves are nowhere in the answer.
        (await refused.Content.ReadAsStringAsync()).ShouldNotContain("barn");

        // And the same document read by somebody who may is the ordinary case, asserted here so
        // a fixture that stopped working cannot pass as a passing security assertion.
        (await owner.GetAsync($"/api/v1/annotated-texts/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_reader_who_may_not_write_is_told_so_and_the_body_is_unchanged()
    {
        var created = await CreateAsync("Shared notes", Block("p", "The original wording."));
        var id = created.GetProperty("documentId").GetGuid();
        await MakePublicAsync(id);

        var read = await ReadJsonAsync(await stranger.GetAsync($"/api/v1/annotated-texts/{id}"));
        read.GetProperty("mayWrite").GetBoolean().ShouldBeFalse();

        var refused = await stranger.PutAsJsonAsync(
            $"/api/v1/annotated-texts/{id}", new { blocks = new[] { Block("p", "Rewritten by a stranger.") } });
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadCodeAsync(refused)).ShouldBe("document.write_forbidden");

        var after = await ReadJsonAsync(await owner.GetAsync($"/api/v1/annotated-texts/{id}"));
        after.GetProperty("blocks").EnumerateArray().Single()
            .GetProperty("text").GetString().ShouldBe("The original wording.");
    }

    [Fact]
    public async Task A_document_that_is_not_one_of_these_is_not_read_as_one()
    {
        var documentId = await UploadPlainDocumentAsync("notes.txt", "just a text file"u8.ToArray());

        var refused = await owner.GetAsync($"/api/v1/annotated-texts/{documentId}");
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(refused)).ShouldBe("annotatedtext.not_annotated_text");
    }

    // ---- replacing the body, and the links over it ----------------------------------------

    [Fact]
    public async Task Replacing_the_body_stacks_a_revision_rather_than_overwriting_one()
    {
        var created = await CreateAsync("Notes", Block("p", "First wording."));
        var id = created.GetProperty("documentId").GetGuid();

        var written = await ReadJsonAsync(await ReplaceAsync(id, Block("p", "Second wording.")));
        var text = written.GetProperty("text");
        text.GetProperty("versionNumber").GetInt32().ShouldBe(2);
        text.GetProperty("fileId").GetGuid().ShouldNotBe(created.GetProperty("fileId").GetGuid());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var versions = await db.DocumentVersions.AsNoTracking().Where(v => v.DocumentId == id).ToListAsync();
        versions.Count.ShouldBe(2);
        versions.Count(v => v.IsCurrent).ShouldBe(1);
    }

    [Fact]
    public async Task A_passage_that_survives_an_edit_above_it_is_re_measured_onto_its_own_words()
    {
        // The defect this whole design exists for. Text inserted above a passage shifts every
        // offset below it; without re-measuring the link goes on resolving, and highlights the
        // wrong sentence, with nothing anywhere reporting it.
        var created = await CreateAsync("Notes", Block("p", "The passage widens after the sump."));
        var id = created.GetProperty("documentId").GetGuid();
        var memberId = await LinkPassageAsync(id, created.GetProperty("fileId").GetGuid(), "widens after the sump");

        var report = (await ReadJsonAsync(await ReplaceAsync(
            id,
            Block("p", "A new opening paragraph."),
            Block("p", "The passage widens after the sump."))))
            .GetProperty("reanchoring");

        report.GetProperty("moved").GetInt32().ShouldBe(1);
        report.GetProperty("lost").GetInt32().ShouldBe(0);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var member = await db.ResLinkMembers.AsNoTracking().SingleAsync(m => m.Id == memberId);
        var anchor = JsonDocument.Parse(member.Anchor!).RootElement;

        // The words the offsets now name, read out of the new stream — not merely "the link is
        // still there", which is exactly what a wrong anchor also looks like.
        var stream = "A new opening paragraph.\n\nThe passage widens after the sump.";
        var start = anchor.GetProperty("start").GetInt32();
        var end = anchor.GetProperty("end").GetInt32();
        stream[start..end].ShouldBe("widens after the sump");

        // And it is pinned to the revision it now describes, so it reads as exact rather than
        // as measured against something superseded.
        member.AnchorFileId.ShouldBe((await CurrentFileIdAsync(db, id)));
    }

    [Fact]
    public async Task A_passage_whose_words_are_gone_keeps_what_its_author_wrote_and_reads_as_degraded()
    {
        var created = await CreateAsync("Notes", Block("p", "The passage widens after the sump."));
        var id = created.GetProperty("documentId").GetGuid();
        var originalFileId = created.GetProperty("fileId").GetGuid();
        var memberId = await LinkPassageAsync(id, originalFileId, "widens after the sump");

        var report = (await ReadJsonAsync(await ReplaceAsync(id, Block("p", "Something else entirely."))))
            .GetProperty("reanchoring");
        report.GetProperty("lost").GetInt32().ShouldBe(1);
        report.GetProperty("moved").GetInt32().ShouldBe(0);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var member = await db.ResLinkMembers.AsNoTracking().SingleAsync(m => m.Id == memberId);

        // Untouched, and still pinned to the revision it was measured against — which is what
        // makes a reader see "this may have moved" instead of a confident highlight on words
        // its author never chose.
        JsonDocument.Parse(member.Anchor!).RootElement.GetProperty("quote").GetString()
            .ShouldBe("widens after the sump");
        member.AnchorFileId.ShouldBe(originalFileId);
        member.AnchorFileId.ShouldNotBe(await CurrentFileIdAsync(db, id));
    }

    [Fact]
    public async Task A_passage_nothing_disturbed_is_left_exactly_where_it_was()
    {
        var created = await CreateAsync("Notes", Block("p", "The passage widens."), Block("p", "A second paragraph."));
        var id = created.GetProperty("documentId").GetGuid();
        var memberId = await LinkPassageAsync(id, created.GetProperty("fileId").GetGuid(), "The passage widens");
        var before = await AnchorOffsetAsync(memberId);

        // The edit is below the passage, so nothing above it moved.
        var report = (await ReadJsonAsync(await ReplaceAsync(
            id, Block("p", "The passage widens."), Block("p", "A rewritten second paragraph."))))
            .GetProperty("reanchoring");

        report.GetProperty("unmoved").GetInt32().ShouldBe(1);
        (await AnchorOffsetAsync(memberId)).ShouldBe(before);
    }

    [Fact]
    public async Task The_stored_stream_is_exactly_what_the_offsets_are_measured_against()
    {
        // The contract between this format and the browser, asserted end to end: a passage
        // located by its offsets in the text the server read back is the passage the anchor
        // quotes. A disagreement of one character here is every highlight slightly wrong.
        var created = await CreateAsync(
            "Notes",
            Block("h2", "Galeria Mare"),
            Block("p", "Șaua Mică lies above the second sump."),
            Block("ul", "Re-survey the connection."));
        var id = created.GetProperty("documentId").GetGuid();
        var memberId = await LinkPassageAsync(id, created.GetProperty("fileId").GetGuid(), "above the second sump");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await WaitForTextAsync(db, created.GetProperty("fileId").GetGuid());

        var stored = (await db.DocumentPages.AsNoTracking()
            .SingleAsync(p => p.FileId == created.GetProperty("fileId").GetGuid())).Text!;
        var member = await db.ResLinkMembers.AsNoTracking().SingleAsync(m => m.Id == memberId);
        var anchor = JsonDocument.Parse(member.Anchor!).RootElement;

        stored[anchor.GetProperty("start").GetInt32()..anchor.GetProperty("end").GetInt32()]
            .ShouldBe("above the second sump");
    }

    // ---- helpers --------------------------------------------------------------------------

    private static object Block(string type, string text) => new { type, text };

    private async Task<JsonElement> CreateAsync(string title, params object[] blocks) =>
        await ReadJsonAsync(await CreateResponseAsync(title, blocks));

    private Task<HttpResponseMessage> CreateResponseAsync(string title, params object[] blocks) =>
        owner.PostAsJsonAsync("/api/v1/annotated-texts", new { title, blocks, visibility = "private" });

    private Task<HttpResponseMessage> ReplaceAsync(Guid id, params object[] blocks) =>
        owner.PutAsJsonAsync($"/api/v1/annotated-texts/{id}", new { blocks });

    /// <summary>
    /// Marks a passage with a link, the way the reader does: offsets into the stream the client
    /// computed for itself, the quote beside them, and the revision they were measured against.
    /// </summary>
    private async Task<Guid> LinkPassageAsync(Guid documentId, Guid fileId, string quote)
    {
        var read = await ReadJsonAsync(await owner.GetAsync($"/api/v1/annotated-texts/{documentId}"));
        var stream = string.Join(
            "\n\n",
            read.GetProperty("blocks").EnumerateArray().Select(b => b.GetProperty("text").GetString()));
        var start = stream.IndexOf(quote, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);

        var created = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = (long?)null,
            description = (string?)null,
            members = new object[]
            {
                new
                {
                    targetType = "document",
                    targetId = documentId,
                    isMain = false,
                    sortOrder = 0,
                    note = (string?)null,
                    anchorKind = "textRange",
                    anchor = new
                    {
                        start,
                        end = start + quote.Length,
                        quote,
                        prefix = stream[Math.Max(0, start - 32)..start],
                        suffix = stream[(start + quote.Length)..Math.Min(stream.Length, start + quote.Length + 32)],
                    },
                    anchorFileId = fileId,
                },
            },
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        var link = await ReadJsonAsync(created);
        return link.GetProperty("members").EnumerateArray().Single().GetProperty("id").GetGuid();
    }

    private async Task<int> AnchorOffsetAsync(Guid memberId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var member = await db.ResLinkMembers.AsNoTracking().SingleAsync(m => m.Id == memberId);
        return JsonDocument.Parse(member.Anchor!).RootElement.GetProperty("start").GetInt32();
    }

    private static Task<Guid> CurrentFileIdAsync(SilexGisDbContext db, Guid documentId) =>
        db.StoredFiles.AsNoTracking()
            .Where(f => db.DocumentVersions
                .Any(v => v.Id == f.DocumentVersionId && v.DocumentId == documentId && v.IsCurrent))
            .Select(f => f.Id)
            .FirstAsync();

    /// <summary>
    /// Waits for the background reading of the file's text. It is queued by the write, so a test
    /// asserting on the stored stream has to let it happen; failing loudly here beats a flaky
    /// assertion about an empty page.
    /// </summary>
    private static async Task WaitForTextAsync(SilexGisDbContext db, Guid fileId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await db.DocumentPages.AsNoTracking().AnyAsync(p => p.FileId == fileId && p.Text != null))
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException($"The text of file {fileId} was never read.");
    }

    private async Task MakePublicAsync(Guid documentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var document = await db.Documents.SingleAsync(d => d.Id == documentId);
        document.Visibility = Visibility.Public;
        await db.SaveChangesAsync();
    }

    private async Task<Guid> UploadPlainDocumentAsync(string name, byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", name);
        var response = await owner.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("documentId").GetGuid();
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        stranger?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
