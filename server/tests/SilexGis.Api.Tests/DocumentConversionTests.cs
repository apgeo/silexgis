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
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Turning an office document into one with pages, where an installation has something that
/// can. The service that does it is optional and most installations will not run it, so the
/// case that matters most here is the one where it is absent: the document must still be
/// stored, listed and downloaded, and the interface must be told plainly that the gap is on
/// this side rather than in the file.
/// <para>
/// The converter itself is stood in for. What is under test is everything around it — what is
/// queued, what is written, what the upload is protected from, and what the response says —
/// none of which should depend on which office suite an operator deployed.
/// </para>
/// </summary>
public sealed class DocumentConversionTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string WordMediaType = "application/msword";

    /// <summary>
    /// A real legacy word-processor document. It has to be real: the upload path decides a
    /// file's format from its own bytes rather than from what the client claimed, so bytes
    /// that only pretend would be stored as plain text and never queued for conversion at all.
    /// </summary>
    private static byte[] WordDocument() => CompoundFileSamples.CompoundFile((
        CompoundFileSamples.WordStream,
        CompoundFileSamples.WordDocumentStream(CompoundFileSamples.Word97Version, encrypted: false)));

    /// <summary>Enough of a portable document to be stored and recognised as one.</summary>
    private static readonly byte[] PortableDocument = "%PDF-1.4\n%âãÏÓ\ntrailer\n"u8.ToArray();

    private readonly StubDocumentConverter converter = new();
    private readonly SilexGisApiFactory factory;
    private readonly string connectionString;
    private readonly string filesRoot;

    private HttpClient owner = null!;    // Editor — uploads and owns the documents below
    private HttpClient outsider = null!; // Viewer with no grant of any kind — genuinely cannot read

    public DocumentConversionTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-convert-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                // This class runs the import handler by hand, twice, to prove a retry does not
                // duplicate what the first run committed. The background worker claims the same
                // queued row on its own schedule, so left running it supplies a third execution
                // nobody asked for and the fixed batch id collides — a duplicate-key failure in
                // a test whose subject is precisely that there is no duplicate.
                ["Jobs:PollSeconds"] = "0",
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),

                // An installation that says it has a converter. Which one is irrelevant here:
                // the double below answers instead of the network.
                ["Conversion:Enabled"] = "true",
                ["Conversion:Url"] = "http://converter.test.invalid",
            },
            services => services.AddSingleton<IDocumentConverter>(converter));
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"cv-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"cv-own-{suffix}@t.local");

        // A regular user, not an editor: the seeded editors group reads every document there
        // is, so an editor could never prove that anything is withheld from anyone.
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cv-out-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"cv-out-{suffix}@t.local");
    }

    [Fact]
    public async Task A_converted_copy_is_stored_beside_the_upload_and_the_upload_is_untouched()
    {
        converter.Produce = PortableDocument;
        var fileId = await UploadAsync("report.doc", WordDocument(), WordMediaType);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var upload = await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == fileId);
            upload.Conversion.ShouldBe(ConversionState.Pending);
            (await db.ProcessingJobs.CountAsync(j => j.Kind == ProcessingJobKinds.DocumentConversion))
                .ShouldBeGreaterThan(0);
        }

        await RunConversionAsync(fileId);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var upload = await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == fileId);
            var copy = await db.StoredFiles.AsNoTracking().SingleAsync(f => f.ConvertedFromFileId == fileId);

            upload.Conversion.ShouldBe(ConversionState.Converted);

            // The upload is what somebody put here, and it is exactly what it was: the copy is
            // another row with its own bytes, not a rewrite of these.
            upload.MimeType.ShouldBe(WordMediaType);
            upload.OriginalName.ShouldBe("report.doc");
            upload.ConvertedFromFileId.ShouldBeNull();
            copy.StoragePath.ShouldNotBe(upload.StoragePath);
            copy.Sha256.ShouldNotBe(upload.Sha256);
            copy.MimeType.ShouldBe(ConvertibleFormats.TargetMediaType);

            // Same revision, so nothing about the version sequence moved and the document the
            // world points at is still the upload.
            copy.DocumentVersionId.ShouldBe(upload.DocumentVersionId);
            var version = await db.DocumentVersions.AsNoTracking()
                .SingleAsync(v => v.Id == upload.DocumentVersionId);
            version.VersionNumber.ShouldBe(1);
            version.IsCurrent.ShouldBeTrue();
            (await db.DocumentVersions.CountAsync(v => v.DocumentId == version.DocumentId)).ShouldBe(1);
        }

        // The response names the copy as the thing whose pages can be drawn — the client is
        // never left to work that out from a media type.
        var file = await ReadFileAsync(owner, fileId);
        var pagesUrl = file.GetProperty("pagesUrl").GetString();
        pagesUrl.ShouldNotBeNull();
        pagesUrl.ShouldNotContain(fileId.ToString());
        file.GetProperty("conversion").GetString().ShouldBe("converted");

        // Existence of a document a Viewer has no grant on is not disclosed, and neither is
        // the copy made of it. The owner's read above is the same request succeeding.
        (await outsider.GetAsync($"/api/v1/files/{fileId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_converted_copy_never_becomes_what_the_document_serves()
    {
        converter.Produce = PortableDocument;
        var fileId = await UploadAsync("minutes.doc", WordDocument(), WordMediaType);
        await RunConversionAsync(fileId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var upload = await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == fileId);
        var copy = await db.StoredFiles.AsNoTracking().SingleAsync(f => f.ConvertedFromFileId == fileId);

        var current = await SilexGis.Infrastructure.Documents.DocumentQueries
            .CurrentFileOfDocumentAsync(db, fileId);
        current.ShouldNotBeNull();
        current.Id.ShouldBe(upload.Id);
        current.Id.ShouldNotBe(copy.Id);

        // Attachments, taggings and every access rule are evaluated against the upload, so the
        // copy must never be the row they land on.
        (await db.StoredFiles.CountAsync(f => f.DocumentVersionId == upload.DocumentVersionId)).ShouldBe(2);
    }

    [Fact]
    public async Task A_converter_that_did_not_answer_is_not_an_installation_without_a_converter()
    {
        // The two sentences an interface must never merge. This installation has a converter —
        // it said so, and the ordinary case below proves it — so a restart or a timeout during
        // one document must not leave that document telling every reader, permanently, that
        // nothing here can lay out office documents.
        converter.Unreachable = true;
        var fileId = await UploadAsync("interrupted.doc", WordDocument(), WordMediaType);

        // The job fails rather than swallowing the outage, so it is visible in the queue.
        await Should.ThrowAsync<InvalidOperationException>(RunConversionAsync(fileId));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var upload = await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == fileId);
            upload.Conversion.ShouldBe(ConversionState.Deferred);
            upload.Conversion.ShouldNotBe(ConversionState.Unavailable);
            (await db.StoredFiles.CountAsync(f => f.ConvertedFromFileId == fileId)).ShouldBe(0);
        }

        (await ReadFileAsync(owner, fileId)).GetProperty("conversion").GetString().ShouldBe("deferred");

        // And the state is not a dead end: once the service answers again, the sweep an
        // administrator can start picks the document up and it converts like any other. Without
        // this leg the assertion above would be satisfied by a document nothing could ever fix.
        converter.Unreachable = false;
        converter.Produce = PortableDocument;
        await RunBackfillAsync(fileId);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == fileId))
                .Conversion.ShouldBe(ConversionState.Pending);
        }

        await RunConversionAsync(fileId);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == fileId))
                .Conversion.ShouldBe(ConversionState.Converted);
            (await db.StoredFiles.CountAsync(f => f.ConvertedFromFileId == fileId)).ShouldBe(1);
        }
    }

    [Fact]
    public async Task Without_a_converter_the_file_is_stored_and_the_installation_says_so()
    {
        // A second installation of the same version, with nothing deployed to lay documents
        // out. This is the ordinary case, and it must not read as a damaged document.
        var bareRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-noconvert-{Guid.NewGuid():N}");
        // Deliberately not a `using` declaration: that would dispose the application at the end
        // of the method, which is after the cleanup below has already deleted the directory it
        // stores files in. The application has to be shut down first — see the finally block.
        var bare = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = bareRoot,
                ["Keys:Path"] = Path.Combine(bareRoot, "keys"),
            });

        // Everything from here on is inside the try, including creating the account. Touching this
        // factory starts a second, complete application — background job workers and all — against
        // the database every other class in this assembly is sharing. A setup call that threw
        // outside the try would leave that application running for the rest of the run, still
        // claiming rows from the shared job table, and the tests it then broke would be in classes
        // with nothing to do with this one.
        HttpClient? bareOwner = null;

        try
        {
            var suffix = Guid.NewGuid().ToString("N")[..8];
            await AuthHelper.CreateUserAsync(bare, GlobalRoles.Editor, $"cv-bare-{suffix}@t.local");
            bareOwner = await AuthHelper.BearerClientAsync(bare, $"cv-bare-{suffix}@t.local");

            var officeId = await UploadAsync(bareOwner, "notes.doc", WordDocument(), WordMediaType);

            await using (var scope = bare.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
                var upload = await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == officeId);

                // Nothing is queued for a service that is not there, and nothing waits forever.
                upload.Conversion.ShouldBe(ConversionState.Unavailable);
                (await db.StoredFiles.CountAsync(f => f.ConvertedFromFileId == officeId)).ShouldBe(0);
            }

            var office = await ReadFileAsync(bareOwner, officeId);
            office.GetProperty("conversion").GetString().ShouldBe("unavailable");
            office.ValueKind.ShouldBe(JsonValueKind.Object);
            office.GetProperty("pagesUrl").ValueKind.ShouldBe(JsonValueKind.Null);
            // The file itself is unaffected: it is stored and it can be fetched.
            office.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeTrue();

            // The same installation, a format that paginates itself: pages are drawn as ever.
            // Nothing about the missing converter took that away.
            var pdfId = await UploadAsync(bareOwner, "survey.pdf", PortableDocument, "application/pdf");
            var pdf = await ReadFileAsync(bareOwner, pdfId);
            pdf.GetProperty("pagesUrl").GetString().ShouldNotBeNull();
            pdf.GetProperty("conversion").GetString().ShouldBe("notApplicable");
        }
        finally
        {
            bareOwner?.Dispose();

            // The application owns everything under bareRoot, and its background workers
            // keep uploaded files open while they read them. Windows refuses to delete a file
            // another process still holds a handle to, so shutting the application down is part
            // of the cleanup rather than something that can be left to happen afterwards.
            bare.Dispose();

            if (Directory.Exists(bareRoot))
            {
                Directory.Delete(bareRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task A_converter_that_refuses_the_bytes_is_recorded_as_a_failure_of_this_document()
    {
        // The one case that really is about the document: the service was reached, looked at
        // the bytes and could not lay them out.
        converter.Produce = null;
        var fileId = await UploadAsync("broken.doc", WordDocument(), WordMediaType);
        await RunConversionAsync(fileId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var upload = await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == fileId);
        upload.Conversion.ShouldBe(ConversionState.Failed);
        (await db.StoredFiles.CountAsync(f => f.ConvertedFromFileId == fileId)).ShouldBe(0);

        // And the upload survives its own failed conversion untouched.
        upload.OriginalName.ShouldBe("broken.doc");
        upload.MimeType.ShouldBe(WordMediaType);
    }

    [Fact]
    public async Task Running_the_same_conversion_twice_leaves_one_copy()
    {
        // Interrupted work is re-delivered when the queue restarts, so the second run has to
        // find the copy that already exists rather than make a second one.
        converter.Produce = PortableDocument;
        var fileId = await UploadAsync("repeat.doc", WordDocument(), WordMediaType);
        await RunConversionAsync(fileId);
        await RunConversionAsync(fileId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.StoredFiles.CountAsync(f => f.ConvertedFromFileId == fileId)).ShouldBe(1);
    }

    private Task<Guid> UploadAsync(string name, byte[] bytes, string contentType) =>
        UploadAsync(owner, name, bytes, contentType);

    private static async Task<Guid> UploadAsync(
        HttpClient client, string name, byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(contentType);
        using var form = new MultipartFormDataContent { { content, "file", name } };
        // These fixtures upload byte-identical content more than once, which the store now
        // warns about. Saying yes up front is what a person would do; deduplication is
        // asserted in its own suite rather than incidentally here.
        var response = await client.PostAsync("/api/v1/files?allowDuplicate=true", form);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> ReadFileAsync(HttpClient client, Guid fileId)
    {
        var response = await client.GetAsync($"/api/v1/files/{fileId}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// Runs the conversion handler directly rather than waiting for the queue, so the test
    /// asserts on what the handler did and not on how quickly a worker got to it.
    /// </summary>
    private async Task RunConversionAsync(Guid fileId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.DocumentConversion);
        await handler.ExecuteAsync(
            new ProcessingJob
            {
                Kind = ProcessingJobKinds.DocumentConversion,
                Payload = JsonSerializer.Serialize(
                    new DocumentConversionPayload(fileId), JsonSerializerOptions.Web),
            },
            CancellationToken.None);
    }

    /// <summary>
    /// Runs the archive-wide sweep that looks for office documents with no readable copy, and
    /// then undoes everything it did to files other than <paramref name="keep"/>.
    /// </summary>
    /// <remarks>
    /// The sweep is archive-wide by design and this database is shared with every other class in
    /// the collection, so left alone it would queue conversions for their fixtures and convert
    /// documents belonging to tests that never deployed a converter. The assertion here is about
    /// one file; the rest is put back exactly as it was.
    /// </remarks>
    private async Task RunBackfillAsync(Guid keep)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var before = await db.StoredFiles.AsNoTracking()
            .Where(f => f.Id != keep
                && f.Conversion != ConversionState.NotApplicable
                && f.Conversion != ConversionState.Converted)
            .Select(f => new { f.Id, f.Conversion })
            .ToListAsync();

        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.DocumentConversionBackfill);
        await handler.ExecuteAsync(
            new ProcessingJob { Kind = ProcessingJobKinds.DocumentConversionBackfill },
            CancellationToken.None);

        // The payload is a jsonb column, so it is read out and matched here rather than in SQL.
        var queued = await db.ProcessingJobs.AsNoTracking()
            .Where(j => j.Kind == ProcessingJobKinds.DocumentConversion
                && j.Status == ProcessingJobStatus.Queued)
            .Select(j => new { j.Id, j.Payload })
            .ToListAsync();
        var foreign = queued
            .Where(j => !j.Payload.Contains(keep.ToString(), StringComparison.OrdinalIgnoreCase))
            .Select(j => j.Id)
            .ToList();
        await db.ProcessingJobs.Where(j => foreign.Contains(j.Id)).ExecuteDeleteAsync();

        foreach (var row in before)
        {
            await db.StoredFiles.Where(f => f.Id == row.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.Conversion, row.Conversion));
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        outsider?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    /// <summary>
    /// Stands in for the office suite. <see cref="Produce"/> null means the service answered
    /// and refused the bytes, which is the only outcome that is a fact about the document;
    /// <see cref="Unreachable"/> is the deployed service that did not answer at all, which the
    /// real client also raises as an ordinary failure rather than as a verdict on the file.
    /// </summary>
    private sealed class StubDocumentConverter : IDocumentConverter
    {
        public byte[]? Produce { get; set; }

        public bool Unreachable { get; set; }

        public bool IsConfigured => true;

        public async Task ConvertToPortableAsync(
            Stream source, string originalName, Stream destination, CancellationToken ct = default)
        {
            if (Unreachable)
            {
                throw new InvalidOperationException("The stub converter could not be reached.");
            }

            // Read the source the way a real converter would, so a test can never pass because
            // nothing ever opened the stored file.
            using var drain = new MemoryStream();
            await source.CopyToAsync(drain, ct);

            if (Produce is null)
            {
                throw new DocumentConversionException("The stub converter refuses these bytes.");
            }

            await destination.WriteAsync(Produce, ct);
        }
    }
}
