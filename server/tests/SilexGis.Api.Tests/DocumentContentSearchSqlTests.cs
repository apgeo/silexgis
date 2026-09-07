// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Features.Search;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The content query itself, against a real database: what it finds, in what order, and — the
/// part that matters — what it refuses to find for whom. Every negative here is driven by a
/// caller who genuinely has no grant reaching the row (a Viewer holds nothing over documents
/// beyond the built-ins) and every one of them is paired with the caller who does, in the same
/// test, so a query that returned nothing at all could not pass.
/// <para>
/// Every seeded page carries a nonsense word unique to the run and every query asks for it, so
/// results and totals are exact rather than "at least": the database is shared with every other
/// suite in the collection, and a total that counted somebody else's uploads would prove nothing
/// about the total this query computes.
/// </para>
/// </summary>
public sealed class DocumentContentSearchSqlTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private string nonce = string.Empty;

    private Guid ownerId;      // owns everything seeded here
    private Guid strangerId;   // no entries, no club — the built-ins alone
    private Guid readerId;     // Read on the revised document, and only Read
    private Guid writerId;     // Read and Write on the revised document

    private Guid docRomanian;  // ro, private to the owner, two pages, a PDF
    private Guid docEnglish;   // en, openly visible, one page, a word processor file, laid out
    private Guid docRevised;   // private, two revisions; the removed name is in the old one
    private Guid docUnlaid;    // private, a word processor file no converter ever laid out

    private Guid englishUpload; // the office file somebody put here
    private Guid englishCopy;   // the portable copy a converter made of it, same words again

    public DocumentContentSearchSqlTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        nonce = Nonce();

        Task<Guid> ViewerAsync(string tag) =>
            AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"content-{tag}-{suffix}@t.local");

        ownerId = await ViewerAsync("own");
        strangerId = await ViewerAsync("str");
        readerId = await ViewerAsync("read");
        writerId = await ViewerAsync("write");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // Diacritics on the way in, none required on the way in from the search box. The first
        // page says the word three times and the second once, so the first page is the one that
        // wins the document.
        docRomanian = Seed(db, $"Raport {suffix}", ownerId, Visibility.Private, "ro");
        var romanianFile = AddVersion(db, docRomanian, 1, current: true, "application/pdf");
        AddPage(db, romanianFile, 1,
            $"Peștera Ursilor are galerii lungi. Peștera a fost cartată în 1980, iar peștera "
            + $"rămâne cea mai lungă din zonă. {nonce} {nonce} {nonce}");
        AddPage(db, romanianFile, 2, $"Anexă. O galerie mică se află la nord. {nonce}");

        // Titled to sort ahead of the Romanian report, so the ordering test can only pass
        // because relevance decided it — the tie-break would have put this one first.
        docEnglish = Seed(db, $"Anexa {suffix}", ownerId, Visibility.Authenticated, "en");
        englishUpload = AddVersion(
            db, docEnglish, 1, current: true,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        // "Unexported" stands for everything a rendering leaves behind — a deck's speaker notes,
        // a hidden worksheet, a wide cell clipped at the column boundary. It is in the upload and
        // deliberately not in the copy.
        AddPage(db, englishUpload, 1,
            $"The surveying teams explored the passages and mapped them. Unexported. {nonce}");

        // An office document has no pages of its own, so an installation running the optional
        // converter holds a portable copy of it beside the upload — and that copy is read for
        // text like any other file, so most words are stored twice. The copy carries one word of
        // its own so that "which artifact did this hit come out of" can be asserted directly
        // rather than inferred from which file a shared word resolved to.
        englishCopy = AddConvertedCopy(
            db, englishUpload,
            $"The surveying teams explored the passages and mapped them. Redistilled. {nonce}");

        // The same format on an installation running no converter: an upload with nothing beside
        // it. It is what makes every statement about the copy a statement about the copy rather
        // than about the format, and what proves an installation without one is unaffected.
        docUnlaid = Seed(db, $"Bivouac {suffix}", ownerId, Visibility.Private, "en");
        var unlaidUpload = AddVersion(
            db, docUnlaid, 1, current: true,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        AddPage(db, unlaidUpload, 1, $"The bivouac stood beside the sump. {nonce}");

        // The retraction case: version one names the landowner, version two does not.
        docRevised = Seed(db, $"Revised {suffix}", ownerId, Visibility.Private, "en");
        var oldFile = AddVersion(db, docRevised, 1, current: false, "application/pdf");
        AddPage(db, oldFile, 1,
            $"The entrance sits on land owned by Wilkinson, who asked us to leave. {nonce}");
        var newFile = AddVersion(db, docRevised, 2, current: true, "application/pdf");
        AddPage(db, newFile, 1, $"The entrance sits on private land, by arrangement. {nonce}");

        db.AccessEntries.AddRange(
            Entry(readerId, AccessAction.Read, docRevised),
            Entry(writerId, AccessAction.Read | AccessAction.Write, docRevised));

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Finds_romanian_text_typed_without_diacritics_and_quotes_it_back_with_them()
    {
        var hits = await SearchAsync(ownerId, "pestera");

        var hit = hits.ShouldHaveSingleItem();
        hit.DocumentId.ShouldBe(docRomanian);

        // The stored text is what gets highlighted, so the archive is quoted as it was written
        // rather than as it was searched for.
        hit.Snippet.ShouldContain("[[Peștera]]");
        hit.Snippet.ShouldNotContain("[[Pestera]]");

        // The denser page wins the document, and the document appears once however many of its
        // pages matched.
        hit.PageNumber.ShouldBe(1);
        hit.TotalDocuments.ShouldBe(1);
    }

    [Fact]
    public async Task Stems_in_the_document_own_language()
    {
        // "explored" only reaches "explore" through the English stemmer.
        var english = await SearchAsync(ownerId, "explore");
        english.ShouldHaveSingleItem().DocumentId.ShouldBe(docEnglish);

        // "peșteri" only reaches "peștera" through the Romanian one, and only because the query
        // was folded to match text that was folded the same way.
        var romanian = await SearchAsync(ownerId, "pesteri");
        romanian.ShouldHaveSingleItem().DocumentId.ShouldBe(docRomanian);
    }

    [Fact]
    public async Task Ranks_the_denser_document_first()
    {
        var hits = await SearchAsync(ownerId, string.Empty);

        hits.Select(h => h.DocumentId).ShouldContain(docRomanian);
        hits.Count.ShouldBe(4);
        hits[0].DocumentId.ShouldBe(docRomanian);
        hits.Select(h => h.Rank).ShouldBe(hits.Select(h => h.Rank).OrderByDescending(r => r));
    }

    [Fact]
    public async Task Page_numbers_are_claimed_only_for_formats_that_number_anything()
    {
        var pdf = (await SearchAsync(ownerId, "pestera")).ShouldHaveSingleItem();
        pdf.Division.ShouldBe(PageDivision.Page);

        // A word-processor file has no pages of its own, so where nothing has laid it out its
        // whole text arrives as one row and the result says so rather than saying "page 1".
        var unlaid = (await SearchAsync(ownerId, "bivouac")).ShouldHaveSingleItem();
        unlaid.Division.ShouldBe(PageDivision.Whole);

        // The same format, on an installation that did lay it out: the words were read off the
        // copy that gets drawn, so the number counts real pages of the thing the reader sees.
        var laidOut = (await SearchAsync(ownerId, "surveying")).ShouldHaveSingleItem();
        laidOut.Division.ShouldBe(PageDivision.Page);
    }

    [Fact]
    public async Task A_hit_is_read_off_the_same_artifact_whose_pages_are_drawn()
    {
        // Fixture proof: the copy really is there, really is derived from the upload, and really
        // has a page of its own — so what follows is the query's doing and not an empty database.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var copy = await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == englishCopy);
            copy.ConvertedFromFileId.ShouldBe(englishUpload);
            (await db.DocumentPages.AsNoTracking().CountAsync(p => p.FileId == englishCopy)).ShouldBe(1);
        }

        // A word that exists only inside the copy finds the document: the copy is read for text
        // like any other file, and its pages are the pages the reader is shown pictures of.
        var fromCopy = (await SearchAsync(ownerId, "redistilled")).ShouldHaveSingleItem();
        fromCopy.DocumentId.ShouldBe(docEnglish);

        // Where both artifacts carry the word the copy answers, because a hit reported against
        // any other pagination would point at a page nobody drew. The document is still found
        // once, and the hit names the file somebody actually put here — the copy is how the
        // document is drawn, never what a reader downloads.
        var hit = (await SearchAsync(ownerId, "surveying")).ShouldHaveSingleItem();
        hit.DocumentId.ShouldBe(docEnglish);
        hit.FileId.ShouldBe(englishUpload);
        hit.FileId.ShouldNotBe(englishCopy);
        hit.MimeType.ShouldContain("wordprocessingml");
    }

    [Fact]
    public async Task Words_only_the_upload_carries_still_find_the_document()
    {
        // Fixture proof: the copy is really there and really has been read, so the upload is not
        // standing merely because nothing was ever laid out.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.DocumentPages.AsNoTracking().CountAsync(p => p.FileId == englishCopy)).ShouldBe(1);
        }

        // A rendering is not a superset of what it was rendered from: printing a deck to a
        // portable document drops its speaker notes, a hidden worksheet never appears, and a wide
        // cell is clipped at the column boundary. Had the copy's existence retired the upload's
        // own words, deploying the optional converter would have quietly made all of that
        // unfindable — the same archive answering fewer questions than the day before.
        var onlyInUpload = (await SearchAsync(ownerId, "unexported")).ShouldHaveSingleItem();
        onlyInUpload.DocumentId.ShouldBe(docEnglish);
        onlyInUpload.FileId.ShouldBe(englishUpload);

        // And the hit is named after the artifact that matched, which is the upload — a word
        // processor file numbers no divisions, so the honest answer is that this number counts
        // nothing, and the interface opens the document at its beginning rather than at a page
        // of a pagination this match knows nothing about.
        onlyInUpload.Division.ShouldBe(PageDivision.Whole);

        // The positive leg, in the same test: a word both artifacts carry is still answered by
        // the copy, whose pages are the ones drawn.
        (await SearchAsync(ownerId, "surveying")).ShouldHaveSingleItem()
            .Division.ShouldBe(PageDivision.Page);
    }

    [Fact]
    public async Task A_copy_that_read_to_nothing_leaves_the_upload_standing()
    {
        // The positive leg: while the copy has words, they answer.
        (await SearchAsync(ownerId, "redistilled")).ShouldHaveSingleItem().DocumentId.ShouldBe(docEnglish);

        // A portable document whose text layer yields nothing — outlined type, or fonts encoded
        // so nothing can read them back — still gets a row per page, and is recorded as read.
        // Those rows are pages, they are simply pages of no words, and a document must not
        // become unfindable because the copy of it happened to be one of those.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.DocumentPages.Where(p => p.FileId == englishCopy)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Text, string.Empty));
        }

        var stillFound = (await SearchAsync(ownerId, "surveying")).ShouldHaveSingleItem();
        stillFound.DocumentId.ShouldBe(docEnglish);
        stillFound.FileId.ShouldBe(englishUpload);
        stillFound.Division.ShouldBe(PageDivision.Whole);

        // Nothing invented in the other direction either: the copy's own word went with its text.
        (await SearchAsync(ownerId, "redistilled")).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_upload_stands_where_nothing_has_laid_it_out_yet()
    {
        // The positive leg: with the copy read, the copy's own word finds the document.
        (await SearchAsync(ownerId, "redistilled")).ShouldHaveSingleItem().DocumentId.ShouldBe(docEnglish);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.DocumentPages.Where(p => p.FileId == englishCopy).ExecuteDeleteAsync();
        }

        // A conversion whose reading has not finished — or failed — must not make a document
        // briefly unfindable, so the upload's own words stand until the copy has some.
        (await SearchAsync(ownerId, "redistilled")).ShouldBeEmpty();
        var waiting = (await SearchAsync(ownerId, "surveying")).ShouldHaveSingleItem();
        waiting.DocumentId.ShouldBe(docEnglish);
        waiting.FileId.ShouldBe(englishUpload);
        waiting.Division.ShouldBe(PageDivision.Whole);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.StoredFiles.Where(f => f.Id == englishCopy).ExecuteDeleteAsync();
        }

        // And with no copy at all — an installation running no converter — the answer is exactly
        // the one that installation has always had. What an optional service changes is how
        // precisely a match can be pointed at, never whether the document is found.
        var noConverter = (await SearchAsync(ownerId, "surveying")).ShouldHaveSingleItem();
        noConverter.DocumentId.ShouldBe(docEnglish);
        noConverter.FileId.ShouldBe(englishUpload);
        noConverter.Division.ShouldBe(PageDivision.Whole);
    }

    [Fact]
    public async Task A_caller_with_no_grant_matches_nothing_and_is_counted_the_same_way()
    {
        // Positive leg first, so a query that found nothing for anybody could not pass this.
        var owner = await SearchAsync(ownerId, string.Empty);
        owner.Count.ShouldBe(4);
        owner[0].TotalDocuments.ShouldBe(4);

        // The stranger holds no entry over documents and is in no club, so the two private
        // documents are genuinely out of reach rather than merely filtered late — and the
        // openly-visible one still is in reach, which is what makes that a statement about
        // those documents rather than about the caller's whole world.
        var stranger = await SearchAsync(strangerId, string.Empty);
        stranger.ShouldHaveSingleItem().DocumentId.ShouldBe(docEnglish);

        // The total is computed by the same statement as the rows, so it says what the rows say.
        stranger[0].TotalDocuments.ShouldBe(1);
    }

    [Fact]
    public async Task Superseded_text_is_found_only_by_a_caller_who_could_have_replaced_it()
    {
        // Both callers may read the document, and both find what it says now.
        (await SearchAsync(readerId, "arrangement", includeSuperseded: true))
            .ShouldHaveSingleItem().DocumentId.ShouldBe(docRevised);
        (await SearchAsync(writerId, "arrangement", includeSuperseded: true))
            .ShouldHaveSingleItem().DocumentId.ShouldBe(docRevised);

        // The name was removed by the second upload. Read alone does not find it again —
        // otherwise replacing a revision would retract nothing.
        (await SearchAsync(readerId, "Wilkinson", includeSuperseded: true)).ShouldBeEmpty();

        // Whoever may replace the revision may still read what it said.
        var editorHit = (await SearchAsync(writerId, "Wilkinson", includeSuperseded: true))
            .ShouldHaveSingleItem();
        editorHit.DocumentId.ShouldBe(docRevised);
        editorHit.IsCurrentVersion.ShouldBeFalse();
        editorHit.VersionNumber.ShouldBe(1);
    }

    [Fact]
    public async Task Superseded_revisions_stay_out_of_the_default_search_for_everyone()
    {
        // Even the caller who may write it: the old text is opt-in, not merely permitted.
        (await SearchAsync(writerId, "Wilkinson")).ShouldBeEmpty();
        (await SearchAsync(writerId, "arrangement")).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Paging_walks_the_same_ranking_and_reports_the_same_total()
    {
        var all = await SearchAsync(ownerId, string.Empty);
        all.Count.ShouldBe(4);

        var first = await SearchAsync(ownerId, string.Empty, limit: 1);
        var second = await SearchAsync(ownerId, string.Empty, limit: 1, offset: 1);

        first.ShouldHaveSingleItem().DocumentId.ShouldBe(all[0].DocumentId);
        second.ShouldHaveSingleItem().DocumentId.ShouldBe(all[1].DocumentId);

        // The total describes the whole result, not the slice, and is the same on every page.
        first[0].TotalDocuments.ShouldBe(4);
        second[0].TotalDocuments.ShouldBe(4);
    }

    [Fact]
    public async Task Correcting_a_document_language_reindexes_its_pages()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // Stemmed as Romanian, so the plural finds the singular.
        (await SearchAsync(ownerId, "pesteri")).ShouldHaveSingleItem().DocumentId.ShouldBe(docRomanian);

        await db.Documents.Where(d => d.Id == docRomanian)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Language, (string?)null));

        // Re-indexed language-neutrally by the language change alone, with nothing having
        // rewritten a single page row: the plural no longer reaches the singular, while accent
        // folding — which is not stemming — still does.
        (await SearchAsync(ownerId, "pesteri")).ShouldBeEmpty();
        (await SearchAsync(ownerId, "pestera")).ShouldHaveSingleItem().DocumentId.ShouldBe(docRomanian);
    }

    // ---- helpers ----

    /// <summary>
    /// One term, all letters, unique to the run: a word only this suite's pages contain, so a
    /// count over the whole archive is still a count of exactly these documents.
    /// </summary>
    private static string Nonce()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        return "zq" + new string([.. bytes.Take(10).Select(b => (char)('a' + (b % 26)))]);
    }

    /// <summary>
    /// Runs the query for one caller. The run's own word is always asked for alongside the
    /// caller's term (a space is AND in this query language); an empty term asks for the word
    /// alone, which matches every page this suite seeded.
    /// </summary>
    private async Task<IReadOnlyList<DocumentContentHit>> SearchAsync(
        Guid userId, string term, bool includeSuperseded = false, int limit = 20, int offset = 0)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var ctx = await RosterHelper.AccessContextOfAsync(db, userId);
        var query = string.IsNullOrEmpty(term) ? nonce : $"{term} {nonce}";
        return await DocumentContentSql.SearchAsync(
            db, ctx, query, limit, offset, includeSuperseded, null, CancellationToken.None);
    }

    private static Guid Seed(
        SilexGisDbContext db, string title, Guid ownerUserId, Visibility visibility, string? language)
    {
        var document = new Document
        {
            Title = title,
            OwnerUserId = ownerUserId,
            Visibility = visibility,
            Language = language,
        };
        db.Documents.Add(document);
        return document.Id;
    }

    private static Guid AddVersion(
        SilexGisDbContext db, Guid documentId, int number, bool current, string mimeType)
    {
        var version = new DocumentVersion
        {
            DocumentId = documentId,
            VersionNumber = number,
            IsCurrent = current,
        };
        db.DocumentVersions.Add(version);

        var file = new StoredFile
        {
            DocumentVersionId = version.Id,
            StoragePath = $"test/{version.Id:N}",
            OriginalName = $"{version.Id:N}.bin",
            MimeType = mimeType,
            Sha256 = version.Id.ToString("N") + version.Id.ToString("N"),
            SizeBytes = 1,
            TextExtraction = TextExtractionState.Extracted,
        };
        db.StoredFiles.Add(file);
        return file.Id;
    }

    /// <summary>
    /// The portable copy an optional converter makes of an office document: a second file on
    /// the same revision, marked as derived from the upload, with a page of its own.
    /// </summary>
    private static Guid AddConvertedCopy(SilexGisDbContext db, Guid uploadFileId, string text)
    {
        var upload = db.StoredFiles.Local.First(f => f.Id == uploadFileId);
        var digest = Guid.NewGuid().ToString("N");
        var copy = new StoredFile
        {
            DocumentVersionId = upload.DocumentVersionId,
            ConvertedFromFileId = upload.Id,
            StoragePath = $"test/{upload.Id:N}-converted",
            OriginalName = $"{upload.Id:N}.pdf",
            MimeType = "application/pdf",
            Sha256 = digest + digest,
            SizeBytes = 1,
            TextExtraction = TextExtractionState.Extracted,
        };
        db.StoredFiles.Add(copy);
        AddPage(db, copy.Id, 1, text);
        return copy.Id;
    }

    private static void AddPage(SilexGisDbContext db, Guid fileId, int pageNumber, string text) =>
        db.DocumentPages.Add(new DocumentPage
        {
            FileId = fileId,
            PageNumber = pageNumber,
            Text = text,
            Extractor = "test",
            ExtractorVersion = 1,
        });

    private static AccessEntry Entry(Guid userId, AccessAction actions, Guid documentId) => new()
    {
        SubjectKind = AccessSubjectKind.User,
        SubjectId = userId,
        Effect = AccessEffect.Allow,
        Domain = AccessDomain.Documents,
        Actions = actions,
        ScopeKind = AccessScopeKind.Object,
        ScopeId = documentId,
    };

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
