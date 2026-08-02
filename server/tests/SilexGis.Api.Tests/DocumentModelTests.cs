// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Metadata;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The document structure behind every stored file: a document holds ordered revisions,
/// exactly one of them current, and each revision holds the physical files (and their
/// pages) that carry its bytes. The upload surface does not change shape here — these
/// tests look at the rows the write path produces and at the invariants the database
/// itself refuses to break.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentModelTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!; // Editor — uploads and owns the documents below
    private Guid ownerId;

    public DocumentModelTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-docs-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"dm-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"dm-own-{suffix}@t.local");
    }

    [Fact]
    public async Task An_upload_becomes_a_document_with_one_current_version_carrying_the_file()
    {
        var uploaded = await UploadAsync("field-notes.txt", "notes"u8.ToArray(), "text/plain");
        var fileId = uploaded.GetProperty("id").GetGuid();
        uploaded.GetProperty("versionNumber").GetInt32().ShouldBe(1);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var file = await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == fileId);
        var version = await db.DocumentVersions.AsNoTracking().SingleAsync(v => v.Id == file.DocumentVersionId);
        version.VersionNumber.ShouldBe(1);
        version.IsCurrent.ShouldBeTrue();
        version.UploadedBy.ShouldBe(ownerId);
        version.DocumentDate.ShouldBeNull();

        var document = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == version.DocumentId);
        document.Id.ToString()[14].ShouldBe('7'); // uuid version nibble — must stay v7
        document.Title.ShouldBe("field-notes.txt");
        document.OwnerUserId.ShouldBe(ownerId);
        document.Visibility.ShouldBe(Visibility.Private);
        document.CreatedAt.ShouldBeGreaterThan(DateTimeOffset.UnixEpoch);

        // A document is auditable in its own right and a version lands on its timeline.
        var documentKey = document.Id.ToString();
        (await db.AuditEntries.CountAsync(a =>
            a.EntityType == nameof(Document) && a.EntityId == documentKey && a.Action == AuditActions.Created))
            .ShouldBe(1);
        (await db.AuditEntries.CountAsync(a =>
            a.EntityType == nameof(DocumentVersion) && a.RootEntityType == nameof(Document)
            && a.RootEntityId == documentKey))
            .ShouldBe(1);

        // A text file's page count is unknown until something extracts it; no page is invented.
        (await db.DocumentPages.CountAsync(p => p.FileId == fileId)).ShouldBe(0);
    }

    [Fact]
    public async Task An_image_gets_its_single_page_at_upload_and_reserves_the_text_column()
    {
        var uploaded = await UploadAsync("entrance.png", MakePng(48, 48), "image/png");
        var fileId = uploaded.GetProperty("id").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var page = await db.DocumentPages.AsNoTracking().SingleAsync(p => p.FileId == fileId);
        page.PageNumber.ShouldBe(1);
        page.Text.ShouldBeNull(); // extraction fills this in later; the column is reserved now
    }

    [Fact]
    public async Task A_new_version_supersedes_the_previous_one_under_the_same_document()
    {
        var v1 = await UploadAsync("survey.txt", "draft"u8.ToArray(), "text/plain");
        var v1Id = v1.GetProperty("id").GetGuid();

        // Version detail set on v1 must survive the upload of v2.
        (await owner.PutAsJsonAsync($"/api/v1/files/{v1Id}", new { documentDate = "1974-06-02" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var v2Id = await UploadVersionAsync(v1Id, "survey.txt", "corrected"u8.ToArray());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var first = await db.DocumentVersions.AsNoTracking()
            .SingleAsync(v => v.Id == db.StoredFiles.Single(f => f.Id == v1Id).DocumentVersionId);
        var second = await db.DocumentVersions.AsNoTracking()
            .SingleAsync(v => v.Id == db.StoredFiles.Single(f => f.Id == v2Id).DocumentVersionId);

        // Same document — the identity a link or a cabinet would anchor on does not move.
        second.DocumentId.ShouldBe(first.DocumentId);
        second.VersionNumber.ShouldBe(2);
        second.IsCurrent.ShouldBeTrue();
        first.IsCurrent.ShouldBeFalse();
        second.DocumentDate.ShouldBe(new DateOnly(1974, 6, 2)); // version detail carries across
        second.UploadedBy.ShouldBe(ownerId);

        // Both revisions keep their own bytes; nothing was rewritten in place.
        (await db.StoredFiles.CountAsync(f => f.DocumentVersionId == first.Id)).ShouldBe(1);
        (await db.StoredFiles.CountAsync(f => f.DocumentVersionId == second.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Two_uploads_racing_onto_one_version_leave_exactly_one_current_version()
    {
        var v1 = await UploadAsync("race.txt", "one"u8.ToArray(), "text/plain");
        var v1Id = v1.GetProperty("id").GetGuid();

        using var formA = BuildForm("race.txt", "attempt a"u8.ToArray(), "text/plain");
        using var formB = BuildForm("race.txt", "attempt b"u8.ToArray(), "text/plain");
        var responses = await Task.WhenAll(
            owner.PostAsync($"/api/v1/files/{v1Id}/versions", formA),
            owner.PostAsync($"/api/v1/files/{v1Id}/versions", formB));

        // One upload wins; the loser is told a newer version already exists rather than
        // silently producing a second "current" revision.
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        var loser = responses.Single(r => r.StatusCode != HttpStatusCode.Created);
        loser.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(loser)).ShouldBe("file.not_head");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var documentId = await db.StoredFiles.AsNoTracking()
            .Where(f => f.Id == v1Id)
            .Join(db.DocumentVersions.AsNoTracking(), f => f.DocumentVersionId, v => v.Id, (f, v) => v.DocumentId)
            .SingleAsync();

        var versions = await db.DocumentVersions.AsNoTracking()
            .Where(v => v.DocumentId == documentId)
            .ToListAsync();
        versions.Count.ShouldBe(2);
        versions.Count(v => v.IsCurrent).ShouldBe(1);
        versions.Select(v => v.VersionNumber).OrderBy(n => n).ShouldBe([1, 2]);

        // The loser's transaction rolled back whole: no orphan revision, no orphan file.
        (await db.StoredFiles.CountAsync(f => versions.Select(v => v.Id).Contains(f.DocumentVersionId)))
            .ShouldBe(2);

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task The_database_itself_refuses_a_second_current_version()
    {
        var uploaded = await UploadAsync("guarded.txt", "one"u8.ToArray(), "text/plain");
        var fileId = uploaded.GetProperty("id").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var current = await db.DocumentVersions.AsNoTracking()
            .SingleAsync(v => v.Id == db.StoredFiles.Single(f => f.Id == fileId).DocumentVersionId);

        // Written straight to the database, which is the point: the invariant must not
        // depend on every future write path remembering it.
        db.DocumentVersions.Add(new DocumentVersion
        {
            DocumentId = current.DocumentId,
            VersionNumber = 2,
            IsCurrent = true,
            UploadedBy = ownerId,
        });
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Deleting_a_superseded_version_takes_its_file_and_pages_with_it()
    {
        var v1 = await UploadAsync("purge.png", MakePng(32, 32), "image/png");
        var v1Id = v1.GetProperty("id").GetGuid();
        var v2Id = await UploadVersionAsync(v1Id, "purge.png", MakePng(64, 64), "image/png");

        Guid supersededVersionId;
        Guid documentId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var file = await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == v1Id);
            supersededVersionId = file.DocumentVersionId;
            documentId = await db.DocumentVersions.AsNoTracking()
                .Where(v => v.Id == supersededVersionId).Select(v => v.DocumentId).SingleAsync();
            (await db.DocumentPages.CountAsync(p => p.FileId == v1Id)).ShouldBe(1);
        }

        (await owner.DeleteAsync($"/api/v1/files/{v1Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.DocumentVersions.CountAsync(v => v.Id == supersededVersionId)).ShouldBe(0);
            (await db.StoredFiles.CountAsync(f => f.Id == v1Id)).ShouldBe(0);
            (await db.DocumentPages.CountAsync(p => p.FileId == v1Id)).ShouldBe(0);

            // The document and its current revision are untouched — a purge removes one
            // revision, never the document behind it.
            (await db.Documents.CountAsync(d => d.Id == documentId)).ShouldBe(1);
            var remaining = await db.DocumentVersions.AsNoTracking()
                .Where(v => v.DocumentId == documentId).ToListAsync();
            remaining.ShouldHaveSingleItem().IsCurrent.ShouldBeTrue();
            (await db.StoredFiles.CountAsync(f => f.Id == v2Id)).ShouldBe(1);
        }

        // The current revision cannot be deleted: a document with nothing current has
        // nothing to serve.
        var deleteCurrent = await owner.DeleteAsync($"/api/v1/files/{v2Id}");
        deleteCurrent.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(deleteCurrent)).ShouldBe("file.head_undeletable");
    }

    [Fact]
    public async Task An_avatar_replacement_removes_the_previous_document_whole()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var avatarUserId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"dm-av-{suffix}@t.local");
        using var me = await AuthHelper.BearerClientAsync(factory, $"dm-av-{suffix}@t.local");

        using (var form = BuildForm("me.png", MakePng(40, 40), "image/png"))
        {
            var response = await me.PostAsync("/api/v1/me/avatar", form);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        }

        Guid firstFileId;
        Guid firstDocumentId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            firstFileId = (await db.Users.AsNoTracking()
                .Where(u => u.Id == avatarUserId).Select(u => u.AvatarFileId).SingleAsync())!.Value;
            firstDocumentId = await db.StoredFiles.AsNoTracking()
                .Where(f => f.Id == firstFileId)
                .Join(db.DocumentVersions.AsNoTracking(), f => f.DocumentVersionId, v => v.Id, (f, v) => v.DocumentId)
                .SingleAsync();
        }

        using (var form = BuildForm("me2.png", MakePng(40, 40), "image/png"))
        {
            (await me.PostAsync("/api/v1/me/avatar", form)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Documents.CountAsync(d => d.Id == firstDocumentId)).ShouldBe(0);
            (await db.DocumentVersions.CountAsync(v => v.DocumentId == firstDocumentId)).ShouldBe(0);
            (await db.StoredFiles.CountAsync(f => f.Id == firstFileId)).ShouldBe(0);
        }
    }

    [Fact]
    public async Task An_upload_that_lost_the_race_cannot_supersede_the_version_that_beat_it()
    {
        var v1 = await UploadAsync("stale.txt", "one"u8.ToArray(), "text/plain");
        var v1Id = v1.GetProperty("id").GetGuid();
        var viewId = await CreateMapViewAsync("Stale race view");
        (await AttachAsync(v1Id, "mapView", viewId)).StatusCode.ShouldBe(HttpStatusCode.Created);

        Guid documentId;
        Guid v1VersionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            v1VersionId = (await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == v1Id)).DocumentVersionId;
            documentId = (await db.DocumentVersions.AsNoTracking().SingleAsync(v => v.Id == v1VersionId)).DocumentId;
        }

        // Hold an upload at the exact moment it has read the current revision and believes
        // it — the window the head check has to survive. Reading "is this still current" and
        // acting on the answer are two statements, and another upload fits between them.
        var hold = new HoldFirstVersionReadInterceptor();
        await using var staleScope = factory.Services.CreateAsyncScope();
        await using var staleDb = new SilexGisDbContext(
            new DbContextOptionsBuilder<SilexGisDbContext>(
                staleScope.ServiceProvider.GetRequiredService<DbContextOptions<SilexGisDbContext>>())
                .AddInterceptors(hold).Options);

        var stale = Task.Run(() => new DocumentWriteService(staleDb, new JsonSchemaPropertiesValidator()).AddVersionAsync(
            v1VersionId,
            new StoredContent("stale/held.txt", "stale.txt", "text/plain", 3, new string('b', 64), FileKind.Document),
            ownerId));
        await hold.Reached.WaitAsync(TimeSpan.FromSeconds(30));

        // A second upload lands and commits while the first still believes version 1 is current.
        var v2Id = await UploadVersionAsync(v1Id, "stale.txt", "two"u8.ToArray());
        hold.Release();

        // The held upload must be told it is off the current revision. Demoting whatever
        // happens to be current instead would discard the revision it never saw and leave
        // that revision's attachments behind it.
        var conflict = await Should.ThrowAsync<DocumentWriteException>(
            async () => await stale.WaitAsync(TimeSpan.FromSeconds(30)));
        conflict.Code.ShouldBe("file.not_head");

        await using var check = factory.Services.CreateAsyncScope();
        var verify = check.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var versions = await verify.DocumentVersions.AsNoTracking()
            .Where(v => v.DocumentId == documentId).ToListAsync();
        versions.Count.ShouldBe(2); // nothing of the losing upload survived
        var current = versions.Single(v => v.IsCurrent);
        current.VersionNumber.ShouldBe(2);

        // The invariant everything else rests on: what an attachment points at is the file of
        // the revision the document currently serves, never one left behind.
        var currentFileId = await verify.StoredFiles.AsNoTracking()
            .Where(f => f.DocumentVersionId == current.Id).Select(f => f.Id).SingleAsync();
        currentFileId.ShouldBe(v2Id);
        (await verify.Attachments.AsNoTracking()
            .SingleAsync(a => a.EntityType == AttachedEntityType.MapView && a.EntityId == viewId))
            .FileId.ShouldBe(currentFileId);
    }

    [Fact]
    public async Task A_revision_holding_two_files_collapses_their_attachments_and_tags_onto_the_new_one()
    {
        var v1 = await UploadAsync("sheet.txt", "original"u8.ToArray(), "text/plain");
        var v1Id = v1.GetProperty("id").GetGuid();

        // A second file inside the same revision — what raster normalization produces: the
        // same content in another encoding, not another revision.
        var renditionId = await AddRenditionAsync(v1Id, "sheet.cog.txt", "text/plain", FileKind.Document);

        var viewId = await CreateMapViewAsync("Collapse view");
        var tagName = $"collapse-{Guid.NewGuid():N}"[..18];
        foreach (var fileId in new[] { v1Id, renditionId })
        {
            (await AttachAsync(fileId, "mapView", viewId)).StatusCode.ShouldBe(HttpStatusCode.Created);
            (await owner.PostAsJsonAsync("/api/v1/taggings/", new
            {
                tagName, entityType = "storedFile", entityId = fileId,
            })).StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        // The new revision holds one file, so both rows collapse onto it. The tag pair is the
        // sharp edge: one tag may sit on one target only once, and that is a unique index —
        // reassigning both rows would fail the upload and report it as a version conflict the
        // caller has no way to resolve.
        var v2Id = await UploadVersionAsync(v1Id, "sheet.txt", "corrected"u8.ToArray());

        await using var check = factory.Services.CreateAsyncScope();
        var verify = check.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var attachments = await verify.Attachments.AsNoTracking()
            .Where(a => a.EntityType == AttachedEntityType.MapView && a.EntityId == viewId).ToListAsync();
        attachments.ShouldHaveSingleItem().FileId.ShouldBe(v2Id);

        var moved = new[] { v1Id, renditionId, v2Id };
        var taggings = await verify.Taggings.AsNoTracking()
            .Where(t => t.EntityType == AttachedEntityType.StoredFile
                && t.EntityId != null && moved.Contains(t.EntityId.Value))
            .ToListAsync();
        taggings.ShouldHaveSingleItem().EntityId.ShouldBe(v2Id);
    }

    [Fact]
    public async Task A_superseded_revision_whose_file_an_entity_points_at_is_refused_not_broken()
    {
        var v1 = await UploadAsync("raster.txt", "upload"u8.ToArray(), "text/plain");
        var v1Id = v1.GetProperty("id").GetGuid();
        var renditionId = await AddRenditionAsync(v1Id, "raster.cog.tif", "image/tiff", FileKind.Raster);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.GeoreferencedMaps.Add(new GeoreferencedMap
            {
                Name = $"Raster {Guid.NewGuid():N}"[..24],
                FileId = renditionId,
                OwnerUserId = ownerId,
                Visibility = Visibility.Authenticated,
            });
            await db.SaveChangesAsync();
        }

        // The upload is superseded, but nothing repoints an entity's own file: the catalog row
        // still names the rendition that now sits inside a superseded revision.
        _ = await UploadVersionAsync(v1Id, "raster.txt", "upload v2"u8.ToArray());

        // Deleting that revision whole would drag the rendition out from under the catalog row.
        // The foreign key restricts, so without a guard the caller gets a raw database failure
        // instead of an answer.
        var refused = await owner.DeleteAsync($"/api/v1/files/{v1Id}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync());
        (await ReadCodeAsync(refused)).ShouldBe("file.version_in_use");

        await using var check = factory.Services.CreateAsyncScope();
        var verify = check.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await verify.StoredFiles.CountAsync(f => f.Id == v1Id || f.Id == renditionId)).ShouldBe(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Replacing_an_avatar_deletes_its_document_unless_another_file_of_it_is_in_use(bool siblingInUse)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var avatarUserId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"dm-sib-{suffix}@t.local");
        using var me = await AuthHelper.BearerClientAsync(factory, $"dm-sib-{suffix}@t.local");

        using (var form = BuildForm("me.png", MakePng(40, 40), "image/png"))
        {
            var response = await me.PostAsync("/api/v1/me/avatar", form);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        }

        Guid avatarFileId;
        Guid documentId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            avatarFileId = (await db.Users.AsNoTracking()
                .Where(u => u.Id == avatarUserId).Select(u => u.AvatarFileId).SingleAsync())!.Value;
            documentId = await db.StoredFiles.AsNoTracking()
                .Where(f => f.Id == avatarFileId)
                .Join(db.DocumentVersions.AsNoTracking(), f => f.DocumentVersionId, v => v.Id, (f, v) => v.DocumentId)
                .SingleAsync();
        }

        // A second revision of the same document. The avatar still names the first file, so
        // the replacement below deletes on the strength of that one file while the delete
        // itself reaches the whole document.
        var supersedingId = await UploadVersionAsync(avatarFileId, "me.png", MakePng(48, 48), "image/png", me);

        var viewId = await CreateMapViewAsync("Avatar sibling view");
        if (siblingInUse)
        {
            // Seeded directly: the point is a row referencing the newer file, and the account
            // that could author it through the API is neither of the two users this test has.
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.Attachments.Add(new Attachment
            {
                FileId = supersedingId,
                EntityType = AttachedEntityType.MapView,
                EntityId = viewId,
                AddedBy = ownerId,
            });
            await db.SaveChangesAsync();
        }

        using (var form = BuildForm("me2.png", MakePng(40, 40), "image/png"))
        {
            (await me.PostAsync("/api/v1/me/avatar", form)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await using var check = factory.Services.CreateAsyncScope();
        var verify = check.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var expected = siblingInUse ? 1 : 0;

        // With nothing else pointing into it the document goes whole, bytes and all. With a
        // newer file in use it stays whole instead: taking it would drop that reference
        // through a cascade that leaves no trace of what it removed.
        (await verify.Documents.CountAsync(d => d.Id == documentId)).ShouldBe(expected);
        (await verify.StoredFiles.CountAsync(f => f.Id == avatarFileId)).ShouldBe(expected);
        (await verify.StoredFiles.CountAsync(f => f.Id == supersedingId)).ShouldBe(expected);
        (await verify.Attachments.CountAsync(a => a.FileId == supersedingId)).ShouldBe(expected);

        // Either way the replacement itself succeeded and the account has its new avatar.
        var currentAvatar = await verify.Users.AsNoTracking()
            .Where(u => u.Id == avatarUserId).Select(u => u.AvatarFileId).SingleAsync();
        currentAvatar.ShouldNotBeNull();
        currentAvatar.ShouldNotBe(avatarFileId);
    }

    [Fact]
    public async Task An_upload_whose_name_yields_no_title_gets_one_rather_than_failing()
    {
        // A file name that is nothing but an extension leaves nothing behind once the
        // extension is stripped, and the slices that title a document that way reach the
        // write path with an empty string. Rejecting it there would strand bytes that are
        // already in the store, so the title falls back instead.
        using var form = BuildForm(".geojson", """{"type":"FeatureCollection","features":[]}"""u8.ToArray(), "application/geo+json");
        var upload = await owner.PostAsync("/api/v1/geofiles/", form);
        upload.StatusCode.ShouldBe(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        var geofileFileId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fileId").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await TitleOfFileAsync(db, geofileFileId)).ShouldBe(".geojson"); // the whole name, since it is all there is

        // And when the name itself is blank there is nothing to fall back to but a placeholder.
        var documents = scope.ServiceProvider.GetRequiredService<DocumentWriteService>();
        var blank = documents.Create(
            new StoredContent("blank/x.bin", " ", "application/octet-stream", 3, new string('e', 64), FileKind.Other),
            " ",
            ownerId,
            ownerId);
        await db.SaveChangesAsync();
        (await TitleOfFileAsync(db, blank.File.Id)).ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_rejected_version_upload_leaves_no_bytes_behind()
    {
        var v1 = await UploadAsync("orphan.txt", "one"u8.ToArray(), "text/plain");
        var v1Id = v1.GetProperty("id").GetGuid();
        _ = await UploadVersionAsync(v1Id, "orphan.txt", "two"u8.ToArray());

        var before = StoredBlobCount();
        using var form = BuildForm("orphan.txt", "late"u8.ToArray(), "text/plain");
        var rejected = await owner.PostAsync($"/api/v1/files/{v1Id}/versions", form);
        rejected.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(rejected)).ShouldBe("file.not_head");

        // The content is streamed into the store before anything can reject it — there is
        // nowhere else to put an upload that may be hundreds of megabytes — so a refused
        // version has to take its bytes back out rather than leave a blob nothing references.
        StoredBlobCount().ShouldBe(before);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        factory.Dispose();
        try
        {
            if (Directory.Exists(filesRoot))
            {
                Directory.Delete(filesRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temp files; best effort.
        }
    }

    // ---- helpers ----

    /// <summary>
    /// Holds the first read of the version table until released, so the gap between "is this
    /// revision current" and acting on the answer can be opened deliberately instead of being
    /// hoped for.
    /// </summary>
    private sealed class HoldFirstVersionReadInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int seen;

        public Task Reached => reached.Task;

        public void Release() => released.TrySetResult();

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("document_versions", StringComparison.Ordinal)
                && Interlocked.Increment(ref seen) == 1)
            {
                reached.TrySetResult();
                await released.Task;
            }

            return result;
        }
    }

    /// <summary>Everything currently sitting in this test's own file store.</summary>
    private int StoredBlobCount() =>
        Directory.Exists(filesRoot) ? Directory.GetFiles(filesRoot, "*", SearchOption.AllDirectories).Length : 0;

    /// <summary>The title of the document a file belongs to.</summary>
    private static Task<string> TitleOfFileAsync(SilexGisDbContext db, Guid fileId) =>
        (from file in db.StoredFiles.AsNoTracking()
         join version in db.DocumentVersions.AsNoTracking() on file.DocumentVersionId equals version.Id
         join document in db.Documents.AsNoTracking() on version.DocumentId equals document.Id
         where file.Id == fileId
         select document.Title).SingleAsync();

    /// <summary>Adds a second file to an existing file's revision, the way a rendition arrives.</summary>
    private async Task<Guid> AddRenditionAsync(Guid fileId, string name, string mimeType, FileKind kind)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var documents = scope.ServiceProvider.GetRequiredService<DocumentWriteService>();
        var versionId = (await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == fileId)).DocumentVersionId;
        var rendition = documents.AddFile(versionId, new StoredContent(
            $"derived/{name}", name, mimeType, 16, new string('c', 64), kind));
        await db.SaveChangesAsync();
        return rendition.Id;
    }

    private Task<HttpResponseMessage> AttachAsync(Guid fileId, string entityType, Guid entityId) =>
        owner.PostAsJsonAsync("/api/v1/attachments/", new
        {
            fileId,
            entityType,
            entityId,
            role = "document",
            caption = (string?)null,
            sortOrder = 0,
        });

    private async Task<Guid> CreateMapViewAsync(string name)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/map-views", new
        {
            name = $"{name} {Guid.NewGuid():N}"[..40],
            config = new { center = new[] { 25.6, 45.5 }, zoom = 10 },
            isHome = false,
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> UploadAsync(string fileName, byte[] bytes, string contentType)
    {
        using var form = BuildForm(fileName, bytes, contentType);
        var response = await owner.PostAsync("/api/v1/files/", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    private async Task<Guid> UploadVersionAsync(
        Guid fileId, string fileName, byte[] bytes, string contentType = "text/plain", HttpClient? client = null)
    {
        using var form = BuildForm(fileName, bytes, contentType);
        var response = await (client ?? owner).PostAsync($"/api/v1/files/{fileId}/versions", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static byte[] MakePng(uint width, uint height)
    {
        using var image = new MagickImage(MagickColors.DarkSlateBlue, width, height);
        return image.ToByteArray(MagickFormat.Png);
    }

    private static MultipartFormDataContent BuildForm(string fileName, byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(contentType);
        return new MultipartFormDataContent { { content, "file", fileName } };
    }
}
