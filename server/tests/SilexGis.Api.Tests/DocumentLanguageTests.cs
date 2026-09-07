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
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The language a document is written in, end to end: detected when its text is read, corrected
/// by whoever may write the document, and — the part that makes any of it worth doing — acted
/// on by the search index the moment it changes.
/// <para>
/// The observable effect throughout is stemming. Accent folding happens for every document
/// whatever its language, so it proves nothing here; what only the Romanian configuration can do
/// is reach "peștera" from "peșteri", and every assertion below is written in those terms.
/// </para>
/// <para>
/// Each upload carries a nonsense word unique to the run and every query asks for it alongside
/// the real term, because the database is shared with the rest of the collection and a hit
/// counted over the whole archive would say nothing about what this query decided.
/// </para>
/// </summary>
public sealed class DocumentLanguageTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    /// <summary>
    /// Romanian written without diacritics and without any of the short words the detector
    /// recognises, so nothing detects a language from it. That is the point: it leaves the
    /// correction as the only thing that can put one there.
    /// </summary>
    private const string UndetectableRomanian = "Pestera Ursilor. Galerie, sala, sifon.";

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string nonce = Nonce();

    private HttpClient owner = null!;     // Editor — uploads everything below
    private HttpClient stranger = null!;  // Viewer — no entry, no club, no reach at all
    private HttpClient reader = null!;    // Viewer — Read on the document, and only Read
    private Guid readerId;

    public DocumentLanguageTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-lang-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"lang-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"lang-own-{suffix}@t.local");

        // Viewers, not Editors: the seeded editors group reads and writes every document in the
        // installation by design, so a refusal proved against an Editor would prove nothing.
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"lang-str-{suffix}@t.local");
        stranger = await AuthHelper.BearerClientAsync(factory, $"lang-str-{suffix}@t.local");

        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"lang-rd-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"lang-rd-{suffix}@t.local");
    }

    [Fact]
    public async Task Reads_the_language_out_of_the_text_when_the_file_is_read()
    {
        var romanian = await UploadAsync(
            "raport.txt",
            $"{nonce} Peștera se află în versantul nordic și a fost explorată prin galeria "
            + "principală. Sala mare este acoperită cu formațiuni, iar cursul de apă se pierde "
            + "sub peretele de calcar.");
        var english = await UploadAsync(
            "report.txt",
            $"{nonce} The cave is located on the northern slope of the massif and was explored "
            + "through the main gallery. The large chamber is covered with formations, and the "
            + "stream sinks under the limestone wall.");

        await WaitForExtractionAsync(romanian);
        await WaitForExtractionAsync(english);

        (await LanguageOfAsync(await DocumentIdOfAsync(romanian))).ShouldBe("ro");
        (await LanguageOfAsync(await DocumentIdOfAsync(english))).ShouldBe("en");
    }

    [Fact]
    public async Task Says_nothing_rather_than_guessing_when_the_text_does_not_say()
    {
        // A page of proper nouns is not a language, and calling it one would stem the words
        // into forms no query produces. Paired with the detected case above: the same reader
        // over the same pipeline does answer when the text is one-sided.
        var file = await UploadAsync("plan.txt", $"{nonce} {UndetectableRomanian}");
        await WaitForExtractionAsync(file);

        (await LanguageOfAsync(await DocumentIdOfAsync(file))).ShouldBeNull();
    }

    [Fact]
    public async Task Correcting_the_language_reindexes_the_document_and_stemming_starts_working()
    {
        var file = await UploadAsync("cartare.txt", $"{nonce} {UndetectableRomanian}");
        await WaitForExtractionAsync(file);
        var documentId = await DocumentIdOfAsync(file);

        // Before the correction the document is indexed language-neutrally: it is findable by
        // the word as written, and only by that. The plural is a different word.
        (await FindAsync(owner, "pestera")).ShouldContain(documentId);
        (await FindAsync(owner, "pesteri")).ShouldNotContain(documentId);

        (await CorrectLanguageAsync(documentId, "ro")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Nothing rewrote a page. The language changed, and every page under the document was
        // re-derived under the Romanian stemmer, which is what makes the plural reach the
        // singular; the word as written still matches, so nothing was traded away for it.
        (await FindAsync(owner, "pesteri")).ShouldContain(documentId);
        (await FindAsync(owner, "pestera")).ShouldContain(documentId);
        (await LanguageOfAsync(documentId)).ShouldBe("ro");
    }

    [Fact]
    public async Task Clearing_the_language_puts_the_document_back_on_neutral_indexing()
    {
        var file = await UploadAsync("anexa.txt", $"{nonce} {UndetectableRomanian}");
        await WaitForExtractionAsync(file);
        var documentId = await DocumentIdOfAsync(file);

        (await CorrectLanguageAsync(documentId, "ro")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await FindAsync(owner, "pesteri")).ShouldContain(documentId);

        // An empty string is not a language subtag, and asking for one is how a person says
        // "I do not know" after all. It has to be distinguishable from not asking at all.
        (await CorrectLanguageAsync(documentId, string.Empty)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await LanguageOfAsync(documentId)).ShouldBeNull();
        (await FindAsync(owner, "pesteri")).ShouldNotContain(documentId);
        (await FindAsync(owner, "pestera")).ShouldContain(documentId);
    }

    [Fact]
    public async Task A_write_that_does_not_mention_the_language_leaves_it_alone()
    {
        var file = await UploadAsync("titlu.txt", $"{nonce} {UndetectableRomanian}");
        await WaitForExtractionAsync(file);
        var documentId = await DocumentIdOfAsync(file);
        (await CorrectLanguageAsync(documentId, "ro")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A title correction is not a statement about the language. If it were, every such save
        // would silently undo the detection and re-index the whole document neutrally.
        var response = await owner.PutAsJsonAsync(
            $"/api/v1/documents/{documentId}",
            new { title = "Raport corectat", visibility = "private" });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        (await LanguageOfAsync(documentId)).ShouldBe("ro");
        (await FindAsync(owner, "pesteri")).ShouldContain(documentId);
    }

    [Fact]
    public async Task Only_a_caller_who_may_write_the_document_may_correct_its_language()
    {
        var file = await UploadAsync("acces.txt", $"{nonce} {UndetectableRomanian}");
        await WaitForExtractionAsync(file);
        var documentId = await DocumentIdOfAsync(file);
        await SetVisibilityAsync(documentId, Visibility.Private);

        // A Viewer holding nothing over this document is told only that there is no such
        // document: a refusal must not disclose that it exists.
        (await CorrectLanguageAsync(documentId, "ro", stranger)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        // A Viewer holding Read and only Read may see it and is refused the write, which is a
        // different refusal and says so.
        await GrantAsync(readerId, AccessAction.Read, documentId);
        (await CorrectLanguageAsync(documentId, "ro", reader)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        // And the owner, in the same test, so a route that refused everybody could not pass.
        (await CorrectLanguageAsync(documentId, "ro")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await LanguageOfAsync(documentId)).ShouldBe("ro");
    }

    [Fact]
    public async Task Signing_in_is_required_to_correct_a_language()
    {
        var file = await UploadAsync("anonim.txt", $"{nonce} {UndetectableRomanian}");
        await WaitForExtractionAsync(file);
        var documentId = await DocumentIdOfAsync(file);

        using var anonymous = factory.CreateClient();
        var response = await anonymous.PutAsJsonAsync(
            $"/api/v1/documents/{documentId}",
            new { title = "Raport", visibility = "private", language = "ro" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await LanguageOfAsync(documentId)).ShouldBeNull();
    }

    [Fact]
    public async Task The_maintenance_sweep_gives_a_language_to_documents_read_before_it_existed()
    {
        var file = await UploadAsync(
            "vechi.txt",
            $"{nonce} Peștera se află în versantul nordic și a fost explorată prin galeria "
            + "principală, iar cursul de apă se pierde sub peretele de calcar.");
        await WaitForExtractionAsync(file);
        var documentId = await DocumentIdOfAsync(file);

        // What an installation that read its archive before any of this existed looks like:
        // the pages are there and the language column is empty. Reading the file again is not
        // what fixes it — the sweep skips a file already read by the reader registered now —
        // so the words already stored have to be enough.
        await ClearLanguageAsync(documentId);
        (await LanguageOfAsync(documentId)).ShouldBeNull();

        await RunBackfillAsync();

        (await LanguageOfAsync(documentId)).ShouldBe("ro");
    }

    /// <summary>
    /// A page may hold two million characters and the detector reads forty thousand of them, so
    /// the sweep fetches only that much — cut by the database, before any of it crosses the wire
    /// into the process that is also serving requests. What that leaves to prove is that the cut
    /// falls where the detector's own sample falls: the language is still read out of a document
    /// far longer than the sample, and it is read out of the <em>opening</em>, which is the part
    /// the cut keeps. The English report is the pair — same length, same route, opposite answer —
    /// so a passing "ro" cannot be the sweep having ignored the text and guessed.
    /// </summary>
    [Fact]
    public async Task The_sweep_reads_a_document_far_longer_than_the_sample_from_its_opening()
    {
        // Padding that says nothing in either language, long enough that the sample ends inside
        // it and everything after it is text the detector will never be handed.
        var padding = string.Concat(Enumerable.Repeat(" 1450 1451 1452 1453 1454 1455", 4000));

        var romanian = await UploadAsync(
            "lung-ro.txt",
            $"{nonce} Peștera se află în versantul nordic și a fost explorată prin galeria "
            + "principală. Sala mare este acoperită cu formațiuni, iar cursul de apă se pierde "
            + $"sub peretele de calcar.{padding}");
        var english = await UploadAsync(
            "lung-en.txt",
            $"{nonce} The cave is located on the northern slope of the massif and was explored "
            + "through the main gallery. The large chamber is covered with formations, and the "
            + $"stream sinks under the limestone wall.{padding}");

        await WaitForExtractionAsync(romanian);
        await WaitForExtractionAsync(english);
        var romanianId = await DocumentIdOfAsync(romanian);
        var englishId = await DocumentIdOfAsync(english);

        await ClearLanguageAsync(romanianId);
        await ClearLanguageAsync(englishId);

        await RunBackfillAsync();

        (await LanguageOfAsync(romanianId)).ShouldBe("ro");
        (await LanguageOfAsync(englishId)).ShouldBe("en");
    }

    // ---- helpers ----

    /// <summary>One term, all letters, unique to the run, so a hit is a hit on these documents.</summary>
    private static string Nonce()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        return "zq" + new string([.. bytes.Take(10).Select(b => (char)('a' + (b % 26)))]);
    }

    private Task<HttpResponseMessage> CorrectLanguageAsync(
        Guid documentId, string? language, HttpClient? client = null) =>
        (client ?? owner).PutAsJsonAsync(
            $"/api/v1/documents/{documentId}",
            new { title = "Raport", visibility = "private", language });

    private async Task<List<Guid>> FindAsync(HttpClient client, string term)
    {
        // Both terms, so only this run's documents can answer: the search ANDs what it is given.
        var url = $"/api/v1/search?q={Uri.EscapeDataString($"{term} {nonce}")}";
        var response = await client.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);

        return [.. JsonDocument.Parse(payload).RootElement
            .GetProperty("documents").GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid())];
    }

    private async Task<string?> LanguageOfAsync(Guid documentId)
    {
        var response = await owner.GetAsync($"/api/v1/documents/{documentId}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);

        var language = JsonDocument.Parse(payload).RootElement.GetProperty("language");
        return language.ValueKind == JsonValueKind.Null ? null : language.GetString();
    }

    private async Task<Guid> UploadAsync(string fileName, string text)
    {
        using var content = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
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

    private async Task ClearLanguageAsync(Guid documentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Documents.Where(d => d.Id == documentId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Language, (string?)null));
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

    private async Task RunBackfillAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.TextExtractionBackfill);
        await handler.ExecuteAsync(
            new ProcessingJob { Kind = ProcessingJobKinds.TextExtractionBackfill, Payload = "{}" },
            CancellationToken.None);
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

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        stranger?.Dispose();
        reader?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
