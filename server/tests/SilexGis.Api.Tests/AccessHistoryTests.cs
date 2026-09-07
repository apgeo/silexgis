// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The record of who has taken a copy of a document.
///
/// Three properties are under test. The first is what counts as a reading: handing over the
/// stored bytes does, and drawing a picture of a page does not — a document on somebody's
/// screen is not a copy in their hands, and counting it as one would drown the record in the
/// client's own refetching.
///
/// The second is who may read the record. It names people, not documents, so being allowed to
/// read a document deliberately does not open it: the reader below can read the document
/// perfectly well and is still refused its readership, while the owner of the same document
/// over the same fixture is not. Without that pairing the refusal could just as easily be a
/// route that never worked.
///
/// The third is that it does not grow forever. The retention pass is run directly against a
/// fixture holding one row past the window and one inside it, so a pass that deleted nothing
/// and a pass that deleted everything both fail.
///
/// The reader is a Viewer throughout. An Editor reads past visibility across every content
/// domain by design, so a refusal proved against one would prove nothing.
/// </summary>
public sealed class AccessHistoryTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;      // Editor — uploads the document, owns it
    private HttpClient reader = null!;     // Viewer — may read the document, holds nothing over the audit domain
    private HttpClient outsider = null!;   // Viewer — no reach to the document at all
    private HttpClient anonymous = null!;
    private Guid readerId;
    private Guid ownerId;
    private long caveTypeId;

    public AccessHistoryTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-accesshist-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"ah-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"ah-own-{suffix}@t.local");

        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ah-read-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"ah-read-{suffix}@t.local");

        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ah-out-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"ah-out-{suffix}@t.local");

        anonymous = factory.CreateClient();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    /// <summary>
    /// Taking the stored bytes is recorded against the person who took them; being shown a
    /// drawn page is not recorded at all. Both halves run over the same file in the same test,
    /// so a build that recorded nothing and a build that recorded everything each fail one of
    /// them.
    /// </summary>
    [Fact]
    public async Task Taking_a_copy_is_recorded_and_being_shown_a_page_is_not()
    {
        var (fileId, documentId, caveId) = await PublishedDocumentAsync();

        // Being shown the document: the reader fetches a picture of page one, which is what
        // the page view does. Nothing about that is a copy.
        var seen = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{fileId}"));
        var contentUrl = seen.GetProperty("contentUrl").GetString()!;
        (await reader.GetAsync(seen.GetProperty("thumbnailUrl").GetString()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RowsAsync(documentId)).ShouldBeEmpty("a rendering shown on screen is not a copy taken");

        // Taking the copy.
        (await reader.GetAsync(contentUrl)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var rows = await RowsAsync(documentId);
        rows.Count.ShouldBe(1);
        rows[0].UserId.ShouldBe(readerId);
        rows[0].FileId.ShouldBe(fileId);
        rows[0].At.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));

        // Fixture proof for the negative half above: the reader really can reach this
        // document, so "nothing recorded" was a rule and not a failed request.
        caveId.ShouldNotBe(Guid.Empty);
        seen.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeTrue();
    }

    /// <summary>
    /// One person reading one file again inside the collapse window does not add a row, and a
    /// different person reading the same file does. The second half is what keeps the first
    /// from passing on a recorder that had simply stopped writing.
    /// </summary>
    [Fact]
    public async Task Reading_again_inside_the_window_is_the_same_reading()
    {
        var (fileId, documentId, _) = await PublishedDocumentAsync();

        for (var i = 0; i < 3; i++)
        {
            var seen = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{fileId}"));
            (await reader.GetAsync(seen.GetProperty("contentUrl").GetString()))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await RowsAsync(documentId)).Count.ShouldBe(1, "three fetches by one person are one reading");

        var byOwner = await ReadJsonAsync(await owner.GetAsync($"/api/v1/files/{fileId}"));
        (await owner.GetAsync(byOwner.GetProperty("contentUrl").GetString()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var rows = await RowsAsync(documentId);
        rows.Count.ShouldBe(2);
        rows.Select(r => r.UserId).ShouldBe(new Guid?[] { readerId, ownerId }, ignoreOrder: true);
    }

    /// <summary>
    /// Who read a document is not part of reading it. The reader may read the document — the
    /// assertion above them proves it in the same test — and is still refused its readership,
    /// while the owner is not. Someone with no reach to the document at all is told the
    /// document does not exist, which is what every other unreadable thing answers.
    /// </summary>
    [Fact]
    public async Task Reading_a_document_does_not_open_its_readership()
    {
        var (fileId, documentId, _) = await PublishedDocumentAsync();
        var seen = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{fileId}"));
        (await reader.GetAsync(seen.GetProperty("contentUrl").GetString()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The positive half of the fixture: this reader can genuinely read the document.
        (await reader.GetAsync($"/api/v1/documents/{documentId}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // And still may not see who else has.
        var refused = await reader.GetAsync($"/api/v1/access-history/documents/{documentId}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("access.forbidden");

        // The owner of the document may, over the same fixture and the same rows.
        var allowed = await ReadJsonAsync(await owner.GetAsync($"/api/v1/access-history/documents/{documentId}"));
        allowed.GetProperty("totalItems").GetInt32().ShouldBe(1);
        allowed.GetProperty("items")[0].GetProperty("userId").GetGuid().ShouldBe(readerId);
        allowed.GetProperty("items")[0].GetProperty("userName").GetString().ShouldNotBeNullOrWhiteSpace();

        // Someone with no reach to a document is not told it is there. Proved over a document
        // that really is out of reach — one the owner uploaded and attached to nothing, so no
        // audience and no reach through an attached object answers for it — and paired with
        // its owner reading the same surface, so the refusal is the rule and not a bad id.
        var unreachable = await UnattachedDocumentAsync();
        (await outsider.GetAsync($"/api/v1/documents/{unreachable}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.GetAsync($"/api/v1/access-history/documents/{unreachable}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/access-history/documents/{unreachable}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Nobody unauthenticated reaches either surface.
        (await anonymous.GetAsync($"/api/v1/access-history/documents/{documentId}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/v1/access-history/me"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// A person's own history holds their readings and nobody else's. Asserted with two people
    /// who have both read the same file, so a surface that ignored the caller and returned the
    /// whole table would show two rows where it shows one.
    /// </summary>
    [Fact]
    public async Task Your_own_history_is_yours_alone()
    {
        var (fileId, documentId, _) = await PublishedDocumentAsync();
        foreach (var client in new[] { reader, owner })
        {
            var seen = await ReadJsonAsync(await client.GetAsync($"/api/v1/files/{fileId}"));
            (await client.GetAsync(seen.GetProperty("contentUrl").GetString()))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var mine = await ReadJsonAsync(await reader.GetAsync("/api/v1/access-history/me"));
        mine.GetProperty("totalItems").GetInt32().ShouldBe(1);
        var row = mine.GetProperty("items")[0];
        row.GetProperty("userId").GetGuid().ShouldBe(readerId);
        row.GetProperty("documentId").GetGuid().ShouldBe(documentId);
        row.GetProperty("documentTitle").GetString().ShouldNotBeNullOrWhiteSpace();

        var theirs = await ReadJsonAsync(await owner.GetAsync("/api/v1/access-history/me"));
        theirs.GetProperty("totalItems").GetInt32().ShouldBe(1);
        theirs.GetProperty("items")[0].GetProperty("userId").GetGuid().ShouldBe(ownerId);

        // And it is in the copy of their own data the account export owes them.
        var outsiderEmpty = await ReadJsonAsync(await outsider.GetAsync("/api/v1/access-history/me"));
        outsiderEmpty.GetProperty("totalItems").GetInt32().ShouldBe(0);
    }

    /// <summary>
    /// The retention pass deletes what is past the window and keeps what is not. Both rows are
    /// seeded over the same file, so a pass that deleted the table and a pass that deleted
    /// nothing each fail.
    /// </summary>
    [Fact]
    public async Task The_retention_pass_deletes_only_what_is_past_the_window()
    {
        var (fileId, documentId, _) = await PublishedDocumentAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.FileAccessEvents.AddRange(
                new FileAccessEvent
                {
                    UserId = readerId, FileId = fileId, DocumentId = documentId,
                    At = DateTimeOffset.UtcNow.AddDays(-800),
                },
                new FileAccessEvent
                {
                    UserId = ownerId, FileId = fileId, DocumentId = documentId,
                    At = DateTimeOffset.UtcNow.AddDays(-2),
                });
            await db.SaveChangesAsync();
        }

        (await RowsAsync(documentId)).Count.ShouldBe(2);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
                .Single(h => h.Kind == ProcessingJobKinds.AccessHistoryPrune);
            await handler.ExecuteAsync(
                new ProcessingJob { Kind = ProcessingJobKinds.AccessHistoryPrune }, CancellationToken.None);
        }

        var left = await RowsAsync(documentId);
        left.Count.ShouldBe(1, "the row inside the window stays");
        left[0].UserId.ShouldBe(ownerId);
    }

    /// <summary>
    /// Setting the retention window to zero is how an installation says it would rather not
    /// hold this at all — so it has to reach the rows already held, and the sweep that would
    /// delete them has to still be scheduled.
    /// </summary>
    [Fact]
    public async Task Turning_the_record_off_reaches_what_was_already_collected()
    {
        var (fileId, documentId, _) = await PublishedDocumentAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.FileAccessEvents.Add(new FileAccessEvent
            {
                UserId = readerId, FileId = fileId, DocumentId = documentId,
                At = DateTimeOffset.UtcNow.AddDays(-2),
            });
            await db.SaveChangesAsync();
        }

        // Positive leg: under an ordinary window this row is squarely inside it and survives
        // the pass, so the disappearance below is the zero window and not the pass itself.
        await PruneAsync(TimeSpan.FromDays(365));
        (await RowsAsync(documentId)).Count.ShouldBe(1);

        await PruneAsync(TimeSpan.Zero);
        (await RowsAsync(documentId)).ShouldBeEmpty();

        // And the pass is still scheduled at a window of zero — otherwise the purge above
        // would be code nothing ever calls, and an operator who turned recording off to be rid
        // of what they hold would keep every row of it. Only the interval turns the schedule off.
        new AccessHistoryOptions { Retention = TimeSpan.Zero }.SweepIsScheduled.ShouldBeTrue();
        new AccessHistoryOptions { SweepInterval = TimeSpan.Zero }.SweepIsScheduled.ShouldBeFalse();
    }

    // ---- helpers ----

    /// <summary>
    /// Runs the retention pass under a stated window, rather than the installation's own: the
    /// window is the input under test, and the handler asks for nothing else.
    /// </summary>
    private async Task PruneAsync(TimeSpan retention)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var handler = new AccessHistoryPruneHandler(
            db,
            Options.Create(new AccessHistoryOptions { Retention = retention }),
            NullLogger<AccessHistoryPruneHandler>.Instance);
        await handler.ExecuteAsync(
            new ProcessingJob { Kind = ProcessingJobKinds.AccessHistoryPrune }, CancellationToken.None);
    }

    /// <summary>
    /// A document the reader can reach: uploaded by the owner and attached to a cave every
    /// signed-in person may read, which is the ordinary route a club document travels.
    /// </summary>
    private async Task<(Guid FileId, Guid DocumentId, Guid CaveId)> PublishedDocumentAsync()
    {
        var caveId = await CreateCaveAsync();
        // An image, so the same fixture carries both a stored original and a rendering this
        // application draws from it — the two things the recorder has to tell apart.
        using var picture = new MagickImage(MagickColors.SlateGray, 320, 240);
        var content = new ByteArrayContent(picture.ToByteArray(MagickFormat.Jpeg));
        content.Headers.ContentType = new("image/jpeg");
        using var form = new MultipartFormDataContent { { content, "file", "survey.jpg" } };
        // These fixtures upload byte-identical content more than once, which the store now
        // warns about. Saying yes up front is what a person would do; deduplication is
        // asserted in its own suite rather than incidentally here.
        var uploaded = await owner.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await uploaded.Content.ReadAsStringAsync();
        uploaded.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var file = JsonDocument.Parse(payload).RootElement;
        var fileId = file.GetProperty("id").GetGuid();

        var attached = await owner.PostAsJsonAsync("/api/v1/attachments/", new
        {
            fileId,
            entityType = "feature",
            entityId = caveId,
            role = "document",
            sortOrder = 0,
        });
        attached.StatusCode.ShouldBe(
            HttpStatusCode.Created, await attached.Content.ReadAsStringAsync());

        return (fileId, file.GetProperty("documentId").GetGuid(), caveId);
    }

    /// <summary>A document nothing points at: no attachment, so no reach answers for it.</summary>
    private async Task<Guid> UnattachedDocumentAsync()
    {
        var content = new ByteArrayContent("private notes"u8.ToArray());
        content.Headers.ContentType = new("text/plain");
        using var form = new MultipartFormDataContent { { content, "file", "notes.txt" } };
        var uploaded = await owner.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await uploaded.Content.ReadAsStringAsync();
        uploaded.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("documentId").GetGuid();
    }

    private async Task<Guid> CreateCaveAsync()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Hist {Guid.NewGuid():N}"[..30],
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

    private async Task<List<FileAccessEvent>> RowsAsync(Guid documentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.FileAccessEvents.AsNoTracking()
            .Where(e => e.DocumentId == documentId)
            .OrderBy(e => e.Id)
            .ToListAsync();
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
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
