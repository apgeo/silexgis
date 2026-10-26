// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The documents a phone is told about and the addresses it is then given: the catalogue of links
/// on a sync set's features, the removals that ride its cursor, the default-set flag, and the
/// per-item re-check of the address route.
///
/// Every absence below is asserted beside a presence over the same fixture. The reader is a
/// Viewer: it reads every cave here, holds no exact-location right anywhere, and so is withheld
/// the protected cave on this channel; the owner created both caves and may place them. A photo
/// that hangs on the open cave and the guarded one is readable by both and placeable by the owner
/// only — the case that is handed to the reader without its location.
/// </summary>
public sealed class SyncDocumentStreamTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string marker = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;   // Editor — creates both caves, may place them
    private HttpClient reader = null!;  // Viewer — reads both caves, may place neither
    private HttpClient anonymous = null!;
    private Guid readerId;
    private long caveTypeId;

    public SyncDocumentStreamTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-syncdocs-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async ValueTask InitializeAsync()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"sd-own-{marker}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"sd-own-{marker}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"sd-read-{marker}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"sd-read-{marker}@t.local");
        anonymous = factory.CreateClient();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    [Fact]
    public async Task A_link_on_a_cave_the_caller_is_handed_is_listed_and_one_on_a_withheld_cave_is_not()
    {
        var open = await CreateCaveAsync(locationProtected: false);
        var guarded = await CreateCaveAsync(locationProtected: true);
        var openPhoto = await UploadAsync("open.jpg", PlacedJpeg(45.51, 25.41), "image/jpeg");
        var guardedPhoto = await UploadAsync("guarded.jpg", PlacedJpeg(45.52, 25.42), "image/jpeg");
        var openLink = await AttachAsync(openPhoto, open);
        var guardedLink = await AttachAsync(guardedPhoto, guarded);

        // Fixture proof: the reader may read both caves through the web, and the feature stream
        // withholds the guarded one from it while handing it to the owner.
        (await reader.GetAsync($"/api/v1/caves/{guarded}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var readersSet = await CreateSetAsync(reader, [open, guarded]);
        var ownersSet = await CreateSetAsync(owner, [open, guarded]);
        var readersFeatures = FeatureIds(await GetPageAsync(reader, $"/api/v1/sync/sets/{readersSet}/download"));
        readersFeatures.ShouldContain(open);
        readersFeatures.ShouldNotContain(guarded);
        FeatureIds(await GetPageAsync(owner, $"/api/v1/sync/sets/{ownersSet}/download")).ShouldContain(guarded);

        var readers = await GetPageAsync(reader, $"/api/v1/sync/sets/{readersSet}/documents");
        LinkIds(readers).ShouldBe([openLink]);
        var owners = await GetPageAsync(owner, $"/api/v1/sync/sets/{ownersSet}/documents");
        LinkIds(owners).ShouldBe([openLink, guardedLink], ignoreOrder: true);

        // The open photo places nothing guarded, so the reader is handed the stored file — and
        // the entry says so, with the stored file's size and hash, and says nothing about where.
        var entry = Link(readers, openLink);
        entry.GetProperty("documentId").GetGuid().ShouldBe(await DocumentIdOfAsync(openPhoto));
        entry.GetProperty("target").GetProperty("kind").GetString().ShouldBe("feature");
        entry.GetProperty("target").GetProperty("id").GetGuid().ShouldBe(open);
        entry.GetProperty("inDefaultSet").GetBoolean().ShouldBeFalse();
        var document = entry.GetProperty("document");
        document.GetProperty("fileKind").GetString().ShouldBe("photo");
        document.GetProperty("reach").GetString().ShouldBe("original");
        document.GetProperty("sha256").GetString().ShouldNotBeNullOrEmpty();
        document.GetProperty("sizeBytes").GetInt64().ShouldBeGreaterThan(0);
        document.GetProperty("renditions").EnumerateArray().Select(r => r.GetInt32()).ShouldBe([2400, 1200, 480, 160]);
        ShouldCarryNoPosition(readers);
        ShouldCarryNoPosition(owners);
    }

    [Fact]
    public async Task A_document_the_caller_may_not_read_is_absent_though_what_it_hangs_on_is_handed()
    {
        var open = await CreateCaveAsync(locationProtected: false);
        var kept = await UploadAsync("kept.jpg", PlacedJpeg(45.53, 25.43), "image/jpeg");
        var refused = await UploadAsync("refused.jpg", PlacedJpeg(45.54, 25.44), "image/jpeg");
        var keptLink = await AttachAsync(kept, open);
        var refusedLink = await AttachAsync(refused, open);
        var readersSet = await CreateSetAsync(reader, [open]);
        var ownersSet = await CreateSetAsync(owner, [open]);

        // Before the rule: both reach the reader through the cave they hang on.
        LinkIds(await GetPageAsync(reader, $"/api/v1/sync/sets/{readersSet}/documents"))
            .ShouldBe([keptLink, refusedLink], ignoreOrder: true);

        await DenyAsync(readerId, await DocumentIdOfAsync(refused));

        // A page of one, so the absence is asked of the paging as well as of the rows: the
        // refused document must not take a place on a page the reader is cut at.
        var first = await GetPageAsync(reader, $"/api/v1/sync/sets/{readersSet}/documents?pageSize=1");
        LinkIds(first).ShouldBe([keptLink]);
        first.GetProperty("hasMore").GetBoolean().ShouldBeFalse();
        LinkIds(await GetPageAsync(owner, $"/api/v1/sync/sets/{ownersSet}/documents"))
            .ShouldBe([keptLink, refusedLink], ignoreOrder: true);

        // And the address route says the same about it, in its own words, item by item.
        var links = await LinksAsync(reader, (await DocumentIdOfAsync(kept), "original"), (await DocumentIdOfAsync(refused), "original"));
        links.GetProperty("links").EnumerateArray().Select(l => l.GetProperty("documentId").GetGuid())
            .ShouldBe([await DocumentIdOfAsync(kept)]);
        var refusal = links.GetProperty("refused").EnumerateArray().ShouldHaveSingleItem();
        refusal.GetProperty("documentId").GetGuid().ShouldBe(await DocumentIdOfAsync(refused));
        refusal.GetProperty("code").GetString().ShouldBe("sync.document_unavailable");
        refusal.GetProperty("rendition").GetString().ShouldBe("original");

        var ownersLinks = await LinksAsync(owner, (await DocumentIdOfAsync(refused), "original"));
        ownersLinks.GetProperty("links").GetArrayLength().ShouldBe(1);
        ownersLinks.GetProperty("refused").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_photo_the_reader_may_not_place_is_handed_whole_without_its_location_through_a_sync_link()
    {
        var open = await CreateCaveAsync(locationProtected: false);
        var guarded = await CreateCaveAsync(locationProtected: true);
        var uploaded = PlacedJpeg(45.55, 25.45);
        var photo = await UploadAsync("both.jpg", uploaded, "image/jpeg");
        var link = await AttachAsync(photo, open);
        await AttachAsync(photo, guarded);
        var documentId = await DocumentIdOfAsync(photo);

        var readersSet = await CreateSetAsync(reader, [open]);
        var ownersSet = await CreateSetAsync(owner, [open]);

        // The catalogue: the reader is told the photo comes without its location and is quoted no
        // size or hash, because those of the stored file are not what it would receive. Recorded,
        // because this is the answer a phone is most likely to misread: a null hash here is not a
        // missing value but the statement that the bytes it will be given are not the stored ones.
        var recorder = new ContractFixture()
            .Literal(marker, "marker").Name(readersSet, "set").Name(open, "cave")
            .Name(link, "link").Name(documentId, "document").Name(photo, "file");
        var readersPage = await recorder.RecordGetAsync(
            "17-documents-first-page", reader, $"/api/v1/sync/sets/{readersSet}/documents");
        var readers = Link(readersPage, link).GetProperty("document");
        readers.GetProperty("reach").GetString().ShouldBe("locationFree");
        readers.GetProperty("sizeBytes").ValueKind.ShouldBe(JsonValueKind.Null);
        readers.GetProperty("sha256").ValueKind.ShouldBe(JsonValueKind.Null);
        var owners = Link(await GetPageAsync(owner, $"/api/v1/sync/sets/{ownersSet}/documents"), link)
            .GetProperty("document");
        owners.GetProperty("reach").GetString().ShouldBe("original");
        owners.GetProperty("sha256").GetString().ShouldBe(Sha(uploaded), StringCompareShould.IgnoreCase);

        // The bytes: full size, no fix, no packet, camera record kept — and the owner's are the
        // stored file, hash for hash.
        var readersBytes = await FetchAsync(reader, Single(await LinksAsync(reader, (documentId, "original"))));
        Sha(readersBytes).ShouldNotBe(Sha(uploaded));
        PhotoDelivery.ShouldCarryNoLocation(readersBytes);
        using (var source = new MagickImage(uploaded))
        using (var copy = new MagickImage(readersBytes))
        {
            copy.Width.ShouldBe(source.Width);
            copy.GetExifProfile().ShouldNotBeNull().GetValue(ExifTag.Model).ShouldNotBeNull();
        }

        var ownersLink = Single(await LinksAsync(owner, (documentId, "original")));
        Sha(await FetchAsync(owner, ownersLink)).ShouldBe(Sha(uploaded), StringCompareShould.IgnoreCase);

        // The address route asked for the whole file and one picture of it in one call, recorded:
        // each item is answered on its own, and the reader's original is the location-free copy.
        var recordedLinks = await recorder.RecordPostAsync(
            "18-files-links",
            reader,
            "/api/v1/sync/files/links",
            new
            {
                items = new object[]
                {
                    new { documentId, rendition = "original" },
                    new { documentId, rendition = 480 },
                },
            });
        recordedLinks.GetProperty("links").GetArrayLength().ShouldBe(2);
        recordedLinks.GetProperty("refused").GetArrayLength().ShouldBe(0);

        // An original's address lives for a sitting, a picture's for one fetch; a picture opens.
        var now = DateTimeOffset.UtcNow;
        ownersLink.GetProperty("expiresAt").GetDateTimeOffset().ShouldBeGreaterThan(now.AddHours(3));
        var picture = Single(await LinksAsync(reader, (documentId, 480)));
        picture.GetProperty("rendition").GetInt32().ShouldBe(480);
        picture.GetProperty("expiresAt").GetDateTimeOffset().ShouldBeLessThan(now.AddMinutes(11));
        using (var drawn = new MagickImage(await FetchAsync(reader, picture)))
        {
            drawn.Format.ShouldBe(MagickFormat.WebP);
        }

        // Switch off: the owner, who may place it, is handed the same location-free copy.
        await SetKeepLocationAsync(false);
        try
        {
            var off = Link(await GetPageAsync(owner, $"/api/v1/sync/sets/{ownersSet}/documents"), link)
                .GetProperty("document");
            off.GetProperty("reach").GetString().ShouldBe("locationFree");
            var offBytes = await FetchAsync(owner, Single(await LinksAsync(owner, (documentId, "original"))));
            Sha(offBytes).ShouldNotBe(Sha(uploaded));
            PhotoDelivery.ShouldCarryNoLocation(offBytes);
        }
        finally
        {
            await SetKeepLocationAsync(true);
        }
    }

    [Fact]
    public async Task A_video_the_reader_may_not_place_has_no_original_and_no_picture_on_either_route()
    {
        var open = await CreateCaveAsync(locationProtected: false);
        var guarded = await CreateCaveAsync(locationProtected: true);
        var video = await UploadAsync("descent.mp4", Mp4Bytes(), "video/mp4");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            // Nothing reads a position out of a video, so the fixture records one the way a placed
            // file holds it — the case under test is a file with one that cannot be rewritten.
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var file = await db.StoredFiles.FirstAsync(f => f.Id == video);
            file.Kind.ShouldBe(FileKind.Video);
            file.Geom = new Point(25.5, 45.5) { SRID = 4326 };
            await db.SaveChangesAsync();
        }

        var link = await AttachAsync(video, open);
        await AttachAsync(video, guarded);
        var documentId = await DocumentIdOfAsync(video);
        var readersSet = await CreateSetAsync(reader, [open]);

        var entry = Link(await GetPageAsync(reader, $"/api/v1/sync/sets/{readersSet}/documents"), link)
            .GetProperty("document");
        entry.GetProperty("fileKind").GetString().ShouldBe("video");
        entry.GetProperty("reach").GetString().ShouldBe("none");
        entry.GetProperty("renditions").GetArrayLength().ShouldBe(0);

        var readers = await LinksAsync(reader, (documentId, "original"), (documentId, 480));
        readers.GetProperty("links").GetArrayLength().ShouldBe(0);
        readers.GetProperty("refused").EnumerateArray().Select(r => r.GetProperty("code").GetString())
            .ShouldBe(["sync.original_withheld", "sync.rendition_unavailable"]);

        // The owner may place both caves and the switch is on: the stored video, as always.
        var ownersLink = Single(await LinksAsync(owner, (documentId, "original")));
        (await FetchAsync(owner, ownersLink)).ShouldBe(Mp4Bytes());
    }

    [Fact]
    public async Task A_phone_video_with_no_recorded_position_has_no_original_for_a_reader_who_may_not_place_it()
    {
        // What an upload really produces: no position is read out of a video, so its row records
        // none while its bytes may hold the one the phone wrote.
        var open = await CreateCaveAsync(locationProtected: false);
        var guarded = await CreateCaveAsync(locationProtected: true);
        var video = await UploadAsync("phone.mp4", Mp4Bytes(), "video/mp4");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == video);
            file.Kind.ShouldBe(FileKind.Video);
            file.Geom.ShouldBeNull();
        }

        var link = await AttachAsync(video, open);
        await AttachAsync(video, guarded);
        var documentId = await DocumentIdOfAsync(video);
        var readersSet = await CreateSetAsync(reader, [open]);

        var entry = Link(await GetPageAsync(reader, $"/api/v1/sync/sets/{readersSet}/documents"), link)
            .GetProperty("document");
        entry.GetProperty("reach").GetString().ShouldBe("none");
        entry.GetProperty("sha256").ValueKind.ShouldBe(JsonValueKind.Null);

        var readers = await LinksAsync(reader, (documentId, "original"));
        readers.GetProperty("links").GetArrayLength().ShouldBe(0);
        readers.GetProperty("refused").EnumerateArray().Single().GetProperty("code").GetString()
            .ShouldBe("sync.original_withheld");

        (await FetchAsync(owner, Single(await LinksAsync(owner, (documentId, "original"))))).ShouldBe(Mp4Bytes());
    }

    [Fact]
    public async Task A_photo_whose_recorded_position_was_cleared_still_reaches_the_reader_without_its_location()
    {
        var open = await CreateCaveAsync(locationProtected: false);
        var guarded = await CreateCaveAsync(locationProtected: true);
        var uploaded = PlacedJpeg(45.43, 25.43);
        var photo = await UploadAsync("cleared.jpg", uploaded, "image/jpeg");
        var link = await AttachAsync(photo, open);
        await AttachAsync(photo, guarded);
        var documentId = await DocumentIdOfAsync(photo);

        // The record loses its position; the stored bytes keep their GPS fix.
        var cleared = await owner.PutAsJsonAsync($"/api/v1/files/{photo}/position", new
        {
            position = (double[]?)null,
            replaceRecordedFix = true,
        });
        cleared.StatusCode.ShouldBe(HttpStatusCode.OK, await cleared.Content.ReadAsStringAsync());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == photo)).Geom.ShouldBeNull();
        }

        var readersSet = await CreateSetAsync(reader, [open]);
        Link(await GetPageAsync(reader, $"/api/v1/sync/sets/{readersSet}/documents"), link)
            .GetProperty("document").GetProperty("reach").GetString().ShouldBe("locationFree");

        var readersBytes = await FetchAsync(reader, Single(await LinksAsync(reader, (documentId, "original"))));
        Sha(readersBytes).ShouldNotBe(Sha(uploaded));
        PhotoDelivery.ShouldCarryNoLocation(readersBytes);

        Sha(await FetchAsync(owner, Single(await LinksAsync(owner, (documentId, "original")))))
            .ShouldBe(Sha(uploaded), StringCompareShould.IgnoreCase);
    }

    [Fact]
    public async Task A_cave_handed_again_after_its_guard_is_lifted_brings_its_photos_on_the_documents_cursor()
    {
        // The guarded cave's photo is linked first and the open cave's after it, so a device that
        // has read to the end holds a cursor already past the guarded link's own change time.
        var guarded = await CreateCaveAsync(locationProtected: true);
        var guardedLink = await AttachAsync(await UploadAsync("held.jpg", PlacedJpeg(45.44, 25.44), "image/jpeg"), guarded);
        var open = await CreateCaveAsync(locationProtected: false);
        var openLink = await AttachAsync(await UploadAsync("seen.jpg", PlacedJpeg(45.45, 25.45), "image/jpeg"), open);
        var set = await CreateSetAsync(reader, [open, guarded]);

        var first = await GetPageAsync(reader, $"/api/v1/sync/sets/{set}/documents");
        LinkIds(first).ShouldBe([openLink]);
        first.GetProperty("hasMore").GetBoolean().ShouldBeFalse();
        var cursor = first.GetProperty("nextCursor").GetString()!;
        FeatureIds(await GetPageAsync(reader, $"/api/v1/sync/sets/{set}/download")).ShouldNotContain(guarded);

        // The guard is lifted by an edit of the cave's own row; neither the link nor the photo
        // changes.
        var current = await GetPageAsync(owner, $"/api/v1/caves/{guarded}");
        (await owner.PutWithIfMatchAsync($"/api/v1/caves/{guarded}", new
        {
            name = current.GetProperty("name").GetString(),
            caveTypeId,
            visibility = "authenticated",
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        FeatureIds(await GetPageAsync(reader, $"/api/v1/sync/sets/{set}/download")).ShouldContain(guarded);
        var resumed = await GetPageAsync(reader, $"/api/v1/sync/sets/{set}/documents?cursor={Uri.EscapeDataString(cursor)}");
        LinkIds(resumed).ShouldBe([guardedLink]);
    }

    [Fact]
    public async Task A_detached_link_and_a_deleted_documents_links_arrive_as_tombstones_on_the_same_cursor()
    {
        var open = await CreateCaveAsync(locationProtected: false);
        var detached = await AttachAsync(await UploadAsync("a.jpg", PlacedJpeg(45.56, 25.46), "image/jpeg"), open);
        var bulkRemoved = await AttachAsync(await UploadAsync("b.jpg", PlacedJpeg(45.57, 25.47), "image/jpeg"), open);
        var deletedPhoto = await UploadAsync("c.jpg", PlacedJpeg(45.58, 25.48), "image/jpeg");
        var orphaned = await AttachAsync(deletedPhoto, open);
        var kept = await AttachAsync(await UploadAsync("d.jpg", PlacedJpeg(45.59, 25.49), "image/jpeg"), open);
        var set = await CreateSetAsync(reader, [open]);

        var before = await GetPageAsync(reader, $"/api/v1/sync/sets/{set}/documents");
        LinkIds(before).ShouldBe([detached, bulkRemoved, orphaned, kept], ignoreOrder: true);
        Tombstones(before).ShouldBeEmpty();
        var cursor = before.GetProperty("nextCursor").GetString()!;

        // Three ways a link goes: detached through the route, deleted in bulk without the rows
        // ever being loaded (the import undo does exactly this), and left behind by a document
        // that was itself deleted.
        (await owner.DeleteAsync($"/api/v1/attachments/{detached}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Attachments.Where(a => a.Id == bulkRemoved).ExecuteDeleteAsync()).ShouldBe(1);
            var documentId = await DocumentIdOfAsync(deletedPhoto);
            (await db.Documents.Where(d => d.Id == documentId)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.DeletedAt, DateTimeOffset.UtcNow))).ShouldBe(1);
        }

        var after = await GetPageAsync(reader, $"/api/v1/sync/sets/{set}/documents?cursor={Uri.EscapeDataString(cursor)}");
        LinkIds(after).ShouldBeEmpty();
        Tombstones(after).ShouldBe([detached, bulkRemoved, orphaned], ignoreOrder: true);

        // From the beginning, the gone links are stubs and the kept one is a row.
        var whole = await GetPageAsync(reader, $"/api/v1/sync/sets/{set}/documents");
        LinkIds(whole).ShouldBe([kept]);
        Tombstones(whole).ShouldBe([detached, bulkRemoved, orphaned], ignoreOrder: true);

        // Caught up, the device keeps its position rather than being sent back to the start.
        var quiet = await GetPageAsync(
            reader, $"/api/v1/sync/sets/{set}/documents?cursor={Uri.EscapeDataString(after.GetProperty("nextCursor").GetString()!)}");
        LinkIds(quiet).ShouldBeEmpty();
        Tombstones(quiet).ShouldBeEmpty();
        quiet.GetProperty("nextCursor").GetString().ShouldBe(after.GetProperty("nextCursor").GetString());
    }

    [Fact]
    public async Task The_documents_stream_walks_a_page_at_a_time_and_refuses_the_feature_streams_cursor()
    {
        var open = await CreateCaveAsync(locationProtected: false);
        var links = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            links.Add(await AttachAsync(await UploadAsync($"p{i}.jpg", PlacedJpeg(45.6 + i / 100d, 25.6), "image/jpeg"), open));
        }

        var set = await CreateSetAsync(reader, [open]);
        var seen = new List<Guid>();
        string? cursor = null;
        for (var page = 0; page < 5; page++)
        {
            var url = $"/api/v1/sync/sets/{set}/documents?pageSize=1"
                + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var body = await GetPageAsync(reader, url);
            seen.AddRange(LinkIds(body));
            cursor = body.GetProperty("nextCursor").GetString();
            if (!body.GetProperty("hasMore").GetBoolean())
            {
                break;
            }
        }

        seen.ShouldBe(links, ignoreOrder: true);
        seen.Distinct().Count().ShouldBe(3);

        // Each stream orders its own rows, so one stream's position means nothing to the other.
        var featureCursor = (await GetPageAsync(reader, $"/api/v1/sync/sets/{set}/download"))
            .GetProperty("nextCursor").GetString()!;
        var refused = await reader.GetAsync(
            $"/api/v1/sync/sets/{set}/documents?cursor={Uri.EscapeDataString(featureCursor)}");
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe("sync.cursor_invalid");
        var backwards = await reader.GetAsync(
            $"/api/v1/sync/sets/{set}/download?cursor={Uri.EscapeDataString(cursor!)}");
        backwards.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Somebody else's set is answered as one that is not there, on this stream as on the other.
        var ownersSet = await CreateSetAsync(owner, [open]);
        var foreign = await reader.GetAsync($"/api/v1/sync/sets/{ownersSet}/documents");
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync($"/api/v1/sync/sets/{set}/documents")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_default_set_flag_is_set_by_who_may_write_the_target_and_reaches_the_stream()
    {
        var open = await CreateCaveAsync(locationProtected: false);
        var link = await AttachAsync(await UploadAsync("entrance.jpg", PlacedJpeg(45.61, 25.51), "image/jpeg"), open);
        var set = await CreateSetAsync(reader, [open]);

        // The reader may read the cave but not write it, so it is refused as a caller who can see
        // the attachment; nobody signed in is refused before anything; an unknown one is not there.
        (await reader.PutAsync($"/api/v1/attachments/{link}/device-default-set?included=true", null))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await anonymous.PutAsync($"/api/v1/attachments/{link}/device-default-set?included=true", null))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await owner.PutAsync($"/api/v1/attachments/{Guid.NewGuid()}/device-default-set?included=true", null))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        Link(await GetPageAsync(reader, $"/api/v1/sync/sets/{set}/documents"), link)
            .GetProperty("inDefaultSet").GetBoolean().ShouldBeFalse();

        var cursor = (await GetPageAsync(reader, $"/api/v1/sync/sets/{set}/documents")).GetProperty("nextCursor").GetString()!;
        (await owner.PutAsync($"/api/v1/attachments/{link}/device-default-set?included=true", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The change moves the link past a caught-up device's position, and the web listing agrees.
        var moved = await GetPageAsync(reader, $"/api/v1/sync/sets/{set}/documents?cursor={Uri.EscapeDataString(cursor)}");
        Link(moved, link).GetProperty("inDefaultSet").GetBoolean().ShouldBeTrue();
        var listed = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/attachments/?entityType=feature&entityId={open}");
        listed.EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == link)
            .GetProperty("inDeviceDefaultSet").GetBoolean().ShouldBeTrue();

        (await owner.PutAsync($"/api/v1/attachments/{link}/device-default-set?included=false", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Link(await GetPageAsync(reader, $"/api/v1/sync/sets/{set}/documents"), link)
            .GetProperty("inDefaultSet").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task The_address_route_refuses_a_malformed_request_whole()
    {
        var photo = await UploadAsync("one.jpg", PlacedJpeg(45.62, 25.52), "image/jpeg");
        var documentId = await DocumentIdOfAsync(photo);

        // The well-formed request first, so each refusal below is about its shape alone.
        Single(await LinksAsync(owner, (documentId, 160))).GetProperty("rendition").GetInt32().ShouldBe(160);

        foreach (var body in new object[]
        {
            new { items = Array.Empty<object>() },
            new { items = Enumerable.Range(0, 201).Select(_ => new { documentId, rendition = 160 }).ToArray() },
            new { items = new object[] { new { documentId, rendition = "huge" } } },
            new { items = new object[] { new { documentId, rendition = 333 } } },
            new { items = new object[] { new { documentId = Guid.Empty, rendition = "original" } } },
            new { items = new object?[] { null } },
            new { items = new object?[] { new { documentId, rendition = 160 }, null } },
        })
        {
            var response = await owner.PostAsJsonAsync("/api/v1/sync/files/links", body);
            var payload = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, payload);
            JsonDocument.Parse(payload).RootElement.GetProperty("code").GetString().ShouldBe("validation.failed", payload);
        }

        (await anonymous.PostAsJsonAsync("/api/v1/sync/files/links", new { items = new[] { new { documentId, rendition = 160 } } }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- helpers ----

    [Fact]
    public async Task A_location_free_originals_address_carries_the_size_and_hash_of_the_copy_only_while_the_switch_asks_for_them()
    {
        var open = await CreateCaveAsync(locationProtected: false);
        var guarded = await CreateCaveAsync(locationProtected: true);
        var uploaded = PlacedJpeg(45.51, 25.41);
        var photo = await UploadAsync("described.jpg", uploaded, "image/jpeg");
        var link = await AttachAsync(photo, open);
        await AttachAsync(photo, guarded);
        var documentId = await DocumentIdOfAsync(photo);
        var readersSet = await CreateSetAsync(reader, [open]);

        // Off (the default): the address comes without numbers.
        var silent = Single(await LinksAsync(reader, (documentId, "original")));
        silent.GetProperty("sizeBytes").ValueKind.ShouldBe(JsonValueKind.Null);
        silent.GetProperty("sha256").ValueKind.ShouldBe(JsonValueKind.Null);

        await SetProtectionAsync(p => p with { DescribeLocationFreeCopies = true });
        try
        {
            var described = Single(await LinksAsync(reader, (documentId, "original")));
            var bytes = await FetchAsync(reader, described);

            // The numbers are those of the bytes the address then serves — the copy, not the
            // stored file.
            described.GetProperty("sizeBytes").GetInt64().ShouldBe(bytes.LongLength);
            described.GetProperty("sha256").GetString().ShouldBe(Sha(bytes));
            Sha(bytes).ShouldNotBe(Sha(uploaded));
            PhotoDelivery.ShouldCarryNoLocation(bytes);

            // The catalogue still quotes none for that reach, and a picture's address none at all.
            var entry = Link(await GetPageAsync(reader, $"/api/v1/sync/sets/{readersSet}/documents"), link)
                .GetProperty("document");
            entry.GetProperty("reach").GetString().ShouldBe("locationFree");
            entry.GetProperty("sizeBytes").ValueKind.ShouldBe(JsonValueKind.Null);
            entry.GetProperty("sha256").ValueKind.ShouldBe(JsonValueKind.Null);
            Single(await LinksAsync(reader, (documentId, 480))).GetProperty("sizeBytes").ValueKind
                .ShouldBe(JsonValueKind.Null);

            // The owner is handed the stored file, which the catalogue already describes.
            var ownersLink = Single(await LinksAsync(owner, (documentId, "original")));
            ownersLink.GetProperty("sizeBytes").ValueKind.ShouldBe(JsonValueKind.Null);
            Sha(await FetchAsync(owner, ownersLink)).ShouldBe(Sha(uploaded));
        }
        finally
        {
            await SetProtectionAsync(p => p with { DescribeLocationFreeCopies = false });
        }
    }

    [Fact]
    public async Task With_the_location_switch_off_the_second_switch_hands_a_video_to_the_owner_and_never_to_the_reader()
    {
        var open = await CreateCaveAsync(locationProtected: false);
        var guarded = await CreateCaveAsync(locationProtected: true);
        var video = await UploadAsync("switched.mp4", Mp4Bytes(), "video/mp4");
        var link = await AttachAsync(video, open);
        await AttachAsync(video, guarded);
        var documentId = await DocumentIdOfAsync(video);
        var readersSet = await CreateSetAsync(reader, [open]);
        var ownersSet = await CreateSetAsync(owner, [open]);

        await SetProtectionAsync(p => p with { KeepLocationInPhotoFiles = false });
        try
        {
            // Second switch off: not even the owner, who may place both caves, gets the video.
            Link(await GetPageAsync(owner, $"/api/v1/sync/sets/{ownersSet}/documents"), link)
                .GetProperty("document").GetProperty("reach").GetString().ShouldBe("none");
            (await LinksAsync(owner, (documentId, "original"))).GetProperty("refused").EnumerateArray()
                .Single().GetProperty("code").GetString().ShouldBe("sync.original_withheld");

            await SetProtectionAsync(p => p with
            {
                KeepLocationInPhotoFiles = false,
                ServeUnstrippableOriginalsToLocationReaders = true,
            });

            var owners = Link(await GetPageAsync(owner, $"/api/v1/sync/sets/{ownersSet}/documents"), link)
                .GetProperty("document");
            owners.GetProperty("reach").GetString().ShouldBe("original");
            owners.GetProperty("sha256").GetString().ShouldBe(Sha(Mp4Bytes()));
            (await FetchAsync(owner, Single(await LinksAsync(owner, (documentId, "original"))))).ShouldBe(Mp4Bytes());

            // The reader may not place the guarded cave: still nothing, on either route.
            Link(await GetPageAsync(reader, $"/api/v1/sync/sets/{readersSet}/documents"), link)
                .GetProperty("document").GetProperty("reach").GetString().ShouldBe("none");
            var readers = await LinksAsync(reader, (documentId, "original"));
            readers.GetProperty("links").GetArrayLength().ShouldBe(0);
            readers.GetProperty("refused").EnumerateArray().Single().GetProperty("code").GetString()
                .ShouldBe("sync.original_withheld");
        }
        finally
        {
            await SetProtectionAsync(p => p with
            {
                KeepLocationInPhotoFiles = true,
                ServeUnstrippableOriginalsToLocationReaders = false,
            });
        }
    }

    private static IReadOnlyList<Guid> FeatureIds(JsonElement page) =>
        [.. page.GetProperty("features").EnumerateArray().Select(f => f.GetProperty("id").GetGuid())];

    private static IReadOnlyList<Guid> LinkIds(JsonElement page) =>
        [.. page.GetProperty("links").EnumerateArray().Select(l => l.GetProperty("id").GetGuid())];

    private static IReadOnlyList<Guid> Tombstones(JsonElement page) =>
        [.. page.GetProperty("tombstones").EnumerateArray().Select(t => t.GetProperty("id").GetGuid())];

    private static JsonElement Link(JsonElement page, Guid id) =>
        page.GetProperty("links").EnumerateArray().Single(l => l.GetProperty("id").GetGuid() == id);

    private static JsonElement Single(JsonElement links)
    {
        links.GetProperty("refused").GetArrayLength().ShouldBe(0, links.ToString());
        return links.GetProperty("links").EnumerateArray().ShouldHaveSingleItem();
    }

    /// <summary>No member of any link names where anything is: not the photo, not its target.</summary>
    private static void ShouldCarryNoPosition(JsonElement page)
    {
        var text = page.GetRawText();
        text.ShouldNotContain("\"position\"");
        text.ShouldNotContain("\"geometry\"");
        text.ShouldNotContain("\"coordinates\"");
        text.ShouldNotContain("\"geom\"");
    }

    private async Task<JsonElement> GetPageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    private static async Task<JsonElement> LinksAsync(HttpClient client, params (Guid DocumentId, object Rendition)[] items)
    {
        var response = await client.PostAsJsonAsync("/api/v1/sync/files/links", new
        {
            items = items.Select(i => new { documentId = i.DocumentId, rendition = i.Rendition }).ToArray(),
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    private static async Task<byte[]> FetchAsync(HttpClient client, JsonElement link)
    {
        var response = await client.GetAsync(link.GetProperty("url").GetString());
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task<Guid> CreateSetAsync(HttpClient client, IReadOnlyList<Guid> roots)
    {
        var response = await client.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker}",
            uploadVisibility = "private",
            rootFeatureIds = roots,
            settings = new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCaveAsync(bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Docs {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> UploadAsync(string fileName, byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(contentType);
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        var response = await owner.PostAsync("/api/v1/files/?allowDuplicate=true", form);
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
            role = "photoInterior",
            sortOrder = 0,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> DocumentIdOfAsync(Guid fileId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var versionId = await db.StoredFiles.AsNoTracking().Where(f => f.Id == fileId)
            .Select(f => f.DocumentVersionId).FirstAsync();
        return await db.DocumentVersions.AsNoTracking().Where(v => v.Id == versionId)
            .Select(v => v.DocumentId).FirstAsync();
    }

    /// <summary>
    /// A rule refusing one person one document, written straight into storage — the authoring
    /// surface refuses rules handing out more than their author holds.
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

    private async Task SetKeepLocationAsync(bool keep)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<IAppSettingsService>();
        var current = await settings.GetProtectionAsync();
        await settings.SaveAsync(AppSettingSections.Protection, current with { KeepLocationInPhotoFiles = keep });
    }

    private async Task SetProtectionAsync(Func<ProtectionSettings, ProtectionSettings> change)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<IAppSettingsService>();
        await settings.SaveAsync(AppSettingSections.Protection, change(await settings.GetProtectionAsync()));
    }

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>
    /// A generated picture with an invented GPS fix and an invented camera record — never a real
    /// photograph.
    /// </summary>
    private static byte[] PlacedJpeg(double lat, double lon)
    {
        using var image = new MagickImage(MagickColors.SteelBlue, 200, 150);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.GPSLatitude, ToDms(lat));
        exif.SetValue(ExifTag.GPSLongitudeRef, "E");
        exif.SetValue(ExifTag.GPSLongitude, ToDms(lon));
        exif.SetValue(ExifTag.Make, "Invented Optics");
        exif.SetValue(ExifTag.Model, "Test Body 2");
        image.SetProfile(exif);
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    private static Rational[] ToDms(double degrees)
    {
        var d = (uint)degrees;
        var minutesFull = (degrees - d) * 60d;
        var m = (uint)minutesFull;
        return [new Rational(d), new Rational(m), new Rational((minutesFull - m) * 60d)];
    }

    /// <summary>A minimal ISO media file: enough for the upload to type it as a video.</summary>
    private static byte[] Mp4Bytes()
    {
        byte[] ftyp = [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m',
            0, 0, 2, 0, (byte)'i', (byte)'s', (byte)'o', (byte)'m', (byte)'m', (byte)'p', (byte)'4', (byte)'1'];
        byte[] mdat = [0, 0, 0, 0x10, (byte)'m', (byte)'d', (byte)'a', (byte)'t', 1, 2, 3, 4, 5, 6, 7, 8];
        return [.. ftyp, .. mdat];
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        owner?.Dispose();
        reader?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
