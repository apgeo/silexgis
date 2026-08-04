// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Text;
using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Reading an upload's text: the queue that carries the work, the rows it writes, what happens
/// when it is run again, and who is allowed to see the result.
/// <para>
/// The upload itself is never touched by any of this. What a reading produces is derived data
/// hanging off the file, which is what lets it be re-run over and over without the stored bytes,
/// their hash or their recorded format having moved.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TextExtractionPipelineTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!; // Editor — uploads and owns the documents below

    public TextExtractionPipelineTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-extract-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tx-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"tx-own-{suffix}@t.local");
    }

    [Fact]
    public async Task An_uploaded_text_file_is_queued_and_read_into_a_stamped_page()
    {
        const string Text = "Peștera Şura Mare — galeria activă, notițe de teren.";
        var fileId = (await UploadAsync("notite.txt", Encoding.UTF8.GetBytes(Text), "text/plain"))
            .GetProperty("id").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // The work is queued rather than done inline, and the queue row is written in the same
        // unit of work as the file: an upload that was rejected leaves neither behind.
        (await db.ProcessingJobs.AsNoTracking()
            .CountAsync(j => j.Kind == ProcessingJobKinds.TextExtraction))
            .ShouldBeGreaterThan(0);

        var file = await WaitForExtractionAsync(fileId);
        file.TextExtraction.ShouldBe(TextExtractionState.Extracted);
        file.TextExtractionError.ShouldBeNull();
        file.PageCount.ShouldBe(1);

        var page = await db.DocumentPages.AsNoTracking().SingleAsync(p => p.FileId == fileId);
        page.PageNumber.ShouldBe(1);
        page.Text.ShouldBe(Text);

        // The reader stamps itself on every page it writes. Without this a page holding no text
        // and a page nothing has looked at are the same row, and only one of them is worth
        // reading again.
        page.Extractor.ShouldNotBeNullOrEmpty();
        page.ExtractorVersion.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_upload_itself_is_untouched_by_being_read()
    {
        var bytes = Encoding.UTF8.GetBytes("un rând de text");
        var fileId = (await UploadAsync("intact.txt", bytes, "text/plain")).GetProperty("id").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var before = await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == fileId);
        var after = await WaitForExtractionAsync(fileId);

        after.StoragePath.ShouldBe(before.StoragePath);
        after.Sha256.ShouldBe(before.Sha256);
        after.SizeBytes.ShouldBe(before.SizeBytes);
        after.MimeType.ShouldBe(before.MimeType);
        after.OriginalName.ShouldBe(before.OriginalName);

        // And the bytes on disk are still the bytes that were sent.
        var store = scope.ServiceProvider.GetRequiredService<IFileStore>();
        (await File.ReadAllBytesAsync(store.GetAbsolutePath(after.StoragePath))).ShouldBe(bytes);
    }

    [Fact]
    public async Task A_picture_is_never_queued_while_a_document_beside_it_is()
    {
        // The positive case first, so the negative one below is a difference between two
        // uploads through the same path rather than an assertion about nothing happening.
        var readable = (await UploadAsync("readable.txt", "ceva de citit"u8.ToArray(), "text/plain"))
            .GetProperty("id").GetGuid();
        var picture = (await UploadAsync("scan.png", MakePng(40, 40), "image/png"))
            .GetProperty("id").GetGuid();

        (await WaitForExtractionAsync(readable)).TextExtraction.ShouldBe(TextExtractionState.Extracted);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // A picture holds no text layer, so nothing is queued for it and it rests where it
        // started. That is a settled answer, not a wait — no reading of it is outstanding.
        var image = await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == picture);
        image.TextExtraction.ShouldBe(TextExtractionState.NotApplicable);

        var page = await db.DocumentPages.AsNoTracking().SingleAsync(p => p.FileId == picture);
        page.Text.ShouldBeNull();
        page.Extractor.ShouldBeNull();
        page.ExtractorVersion.ShouldBeNull();
    }

    [Fact]
    public async Task A_new_version_is_read_on_its_own_terms()
    {
        var firstId = (await UploadAsync("raport.txt", "prima variantă"u8.ToArray(), "text/plain"))
            .GetProperty("id").GetGuid();
        await WaitForExtractionAsync(firstId);

        var secondId = await UploadVersionAsync(firstId, "raport.txt", "a doua variantă"u8.ToArray());
        var second = await WaitForExtractionAsync(secondId);
        second.TextExtraction.ShouldBe(TextExtractionState.Extracted);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // Each revision's file carries its own pages; a new upload does not rewrite the old
        // revision's text, because the old bytes have not changed.
        (await db.DocumentPages.AsNoTracking().SingleAsync(p => p.FileId == firstId)).Text
            .ShouldBe("prima variantă");
        (await db.DocumentPages.AsNoTracking().SingleAsync(p => p.FileId == secondId)).Text
            .ShouldBe("a doua variantă");
    }

    [Fact]
    public async Task Running_the_reading_again_neither_duplicates_pages_nor_re_reads_a_finished_file()
    {
        var fileId = (await UploadAsync("iar.txt", "acelasi text"u8.ToArray(), "text/plain"))
            .GetProperty("id").GetGuid();
        await WaitForExtractionAsync(fileId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var pageId = (await db.DocumentPages.AsNoTracking().SingleAsync(p => p.FileId == fileId)).Id;

        // The queue re-delivers work — every worker start re-queues whatever was running — so
        // the handler has to survive being handed the same file twice.
        await RunHandlerAsync(fileId);
        await RunHandlerAsync(fileId);

        var pages = await db.DocumentPages.AsNoTracking().Where(p => p.FileId == fileId).ToListAsync();
        pages.Count.ShouldBe(1);

        // The row is updated in place rather than replaced, so anything that ever points at a
        // page keeps its target across a re-reading.
        pages[0].Id.ShouldBe(pageId);
        pages[0].Text.ShouldBe("acelasi text");
        (await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == fileId)).PageCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_reading_that_stopped_partway_is_finished_by_running_it_again()
    {
        var fileId = (await UploadAsync("reluat.txt", "text de reluat"u8.ToArray(), "text/plain"))
            .GetProperty("id").GetGuid();
        await WaitForExtractionAsync(fileId);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            // What a run interrupted before it finished leaves behind: the file marked as read,
            // and pages that carry no reader's stamp because nothing got as far as writing one.
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.DocumentPages.Where(p => p.FileId == fileId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Text, (string?)null)
                    .SetProperty(p => p.Extractor, (string?)null)
                    .SetProperty(p => p.ExtractorVersion, (int?)null));
        }

        await RunHandlerAsync(fileId);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var page = await db.DocumentPages.AsNoTracking().SingleAsync(p => p.FileId == fileId);
            page.Text.ShouldBe("text de reluat");
            page.Extractor.ShouldNotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task Page_text_is_only_reachable_through_the_document_that_owns_it()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        // A Viewer, not an Editor: the seeded editors group reads every document in the
        // installation by design, so it cannot construct a document nobody may read.
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tx-see-{suffix}@t.local");
        var stranger = await AuthHelper.BearerClientAsync(factory, $"tx-see-{suffix}@t.local");

        var fileId = (await UploadAsync("privat.txt", "text confidențial"u8.ToArray(), "text/plain"))
            .GetProperty("id").GetGuid();
        await WaitForExtractionAsync(fileId);

        Guid documentId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var file = await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == fileId);
            documentId = await db.DocumentVersions.AsNoTracking()
                .Where(v => v.Id == file.DocumentVersionId).Select(v => v.DocumentId).SingleAsync();

            // Private to its owner: no rule names it and no rule names its uploader's club,
            // so the viewer below holds nothing at all that reaches it.
            await db.Documents.Where(d => d.Id == documentId)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Visibility, Visibility.Private));
        }

        // The owner sees the document and what became of its reading.
        var mine = await owner.GetAsync($"/api/v1/documents/{documentId}");
        var payload = await mine.Content.ReadAsStringAsync();
        mine.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        JsonDocument.Parse(payload).RootElement.GetProperty("textExtraction").GetString()
            .ShouldBe("extracted");

        // Reading a document's text is reading the document: someone who may not open it is
        // told nothing, not even that it exists.
        (await stranger.GetAsync($"/api/v1/documents/{documentId}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        // And it is the grant that decides, not the sign-in: the same request without one
        // gets no further either.
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/documents/{documentId}")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The state every file stored before this installation could read anything carries, and
    /// the sentence it puts on the document panel: this kind of file holds no text to read.
    /// It is false of a report and true of a photograph, and nothing about the row itself can
    /// tell the two apart — which is what the sweep exists to correct.
    /// </summary>
    [Fact]
    public async Task A_document_stored_before_anything_could_read_it_is_found_by_the_sweep()
    {
        var readable = (await UploadAsync("vechi.txt", "raport de teren"u8.ToArray(), "text/plain"))
            .GetProperty("id").GetGuid();
        var picture = (await UploadAsync("scan.png", MakePng(40, 40), "image/png"))
            .GetProperty("id").GetGuid();
        await WaitForExtractionAsync(readable);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            // What such a row looks like: the resting state, and pages nothing ever stamped.
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.StoredFiles.Where(f => f.Id == readable).ExecuteUpdateAsync(
                s => s.SetProperty(f => f.TextExtraction, TextExtractionState.NotApplicable));
            await db.DocumentPages.Where(p => p.FileId == readable).ExecuteDeleteAsync();
        }

        await RunBackfillAsync();

        // The document is queued; the photograph beside it is not, and the difference is the
        // format rather than anything about how the two were stored.
        (await QueuedForAsync(readable)).ShouldBeTrue();
        (await QueuedForAsync(picture)).ShouldBeFalse();

        var file = await WaitForExtractionAsync(readable);
        file.TextExtraction.ShouldBe(TextExtractionState.Extracted);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var page = await db.DocumentPages.AsNoTracking().SingleAsync(p => p.FileId == readable);
            page.Text.ShouldBe("raport de teren");
            page.Extractor.ShouldNotBeNullOrEmpty();
        }

        // And the photograph's own answer is untouched: it was never a document with text.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == picture)).TextExtraction
                .ShouldBe(TextExtractionState.NotApplicable);
        }
    }

    /// <summary>
    /// Page text records which reader and which version produced it so that a better reading is
    /// a decidable question. Nothing acts on the answer unless something asks it, so a file read
    /// by a reader this installation has moved on from has to be found and read again — while a
    /// file read by the reader registered now is left alone, or every sweep would re-read the
    /// whole archive.
    /// </summary>
    [Fact]
    public async Task A_page_read_by_an_older_reader_is_read_again_while_a_current_one_is_left_alone()
    {
        var stale = (await UploadAsync("invechit.txt", "text vechi"u8.ToArray(), "text/plain"))
            .GetProperty("id").GetGuid();
        var current = (await UploadAsync("proaspat.txt", "text nou"u8.ToArray(), "text/plain"))
            .GetProperty("id").GetGuid();
        await WaitForExtractionAsync(stale);
        await WaitForExtractionAsync(current);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            // A page written by a version that is no longer the one this installation emits.
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.DocumentPages.Where(p => p.FileId == stale)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ExtractorVersion, 0));
        }

        await RunBackfillAsync();

        (await QueuedForAsync(stale)).ShouldBeTrue();
        (await QueuedForAsync(current)).ShouldBeFalse();

        // The re-reading actually lands: the page carries the current version again.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (true)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var page = await db.DocumentPages.AsNoTracking().SingleAsync(p => p.FileId == stale);
            if (page.ExtractorVersion is > 0)
            {
                page.Text.ShouldBe("text vechi");
                break;
            }

            DateTimeOffset.UtcNow.ShouldBeLessThan(deadline, "the stale page was never read again");
            await Task.Delay(200);
        }
    }

    /// <summary>
    /// Starting a sweep over the whole archive is an operator's decision, so it takes the same
    /// right the other maintenance sweeps take.
    /// </summary>
    [Fact]
    public async Task Starting_the_sweep_needs_the_right_to_run_jobs()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tx-vw-{suffix}@t.local");
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"tx-adm-{suffix}@t.local");
        using var viewer = await AuthHelper.BearerClientAsync(factory, $"tx-vw-{suffix}@t.local");
        using var admin = await AuthHelper.BearerClientAsync(factory, $"tx-adm-{suffix}@t.local");
        using var anonymous = factory.CreateClient();

        (await anonymous.PostAsync("/api/v1/jobs/text-extraction-backfill", null)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await viewer.PostAsync("/api/v1/jobs/text-extraction-backfill", null)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        var enqueue = await admin.PostAsync("/api/v1/jobs/text-extraction-backfill", null);
        var payload = await enqueue.Content.ReadAsStringAsync();
        enqueue.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        JsonDocument.Parse(payload).RootElement.GetProperty("kind").GetString()
            .ShouldBe("text-extraction-backfill");
    }

    /// <summary>Runs the sweep directly, standing in for an operator starting it.</summary>
    private async Task RunBackfillAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.TextExtractionBackfill);
        await handler.ExecuteAsync(
            new ProcessingJob { Kind = ProcessingJobKinds.TextExtractionBackfill },
            CancellationToken.None);
    }

    /// <summary>Whether a reading of this file has been queued by anything other than its upload.</summary>
    private async Task<bool> QueuedForAsync(Guid fileId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var payloads = await db.ProcessingJobs.AsNoTracking()
            .Where(j => j.Kind == ProcessingJobKinds.TextExtraction)
            .Select(j => j.Payload)
            .ToListAsync();

        // The upload queued one of its own, so the sweep's is the second — which is why this
        // counts rather than merely looking for one.
        return payloads.Count(p =>
            JsonSerializer.Deserialize<TextExtractionPayload>(p, JsonSerializerOptions.Web)?.FileId
                == fileId) > 1;
    }

    /// <summary>
    /// Waits for the queued reading of a file to settle, then returns the file's row. The
    /// worker polls, so a test that looked immediately would be racing it.
    /// </summary>
    private async Task<StoredFile> WaitForExtractionAsync(Guid fileId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (true)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var file = await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == fileId);
            if (file.TextExtraction is not TextExtractionState.Pending)
            {
                return file;
            }

            DateTimeOffset.UtcNow.ShouldBeLessThan(deadline, "the file's text was never read");
            await Task.Delay(200);
        }
    }

    /// <summary>Runs the reading directly, standing in for the queue re-delivering the work.</summary>
    private async Task RunHandlerAsync(Guid fileId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.TextExtraction);
        await handler.ExecuteAsync(
            new ProcessingJob
            {
                Kind = ProcessingJobKinds.TextExtraction,
                Payload = JsonSerializer.Serialize(new TextExtractionPayload(fileId), JsonSerializerOptions.Web),
            },
            CancellationToken.None);
    }

    private async Task<JsonElement> UploadAsync(string fileName, byte[] bytes, string contentType)
    {
        using var form = BuildForm(fileName, bytes, contentType);
        var response = await owner.PostAsync("/api/v1/files/", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    private async Task<Guid> UploadVersionAsync(Guid fileId, string fileName, byte[] bytes)
    {
        using var form = BuildForm(fileName, bytes, "text/plain");
        var response = await owner.PostAsync($"/api/v1/files/{fileId}/versions", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static byte[] MakePng(uint width, uint height)
    {
        using var image = new MagickImage(MagickColors.SlateGray, width, height);
        return image.ToByteArray(MagickFormat.Png);
    }

    private static MultipartFormDataContent BuildForm(string fileName, byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(contentType);
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
