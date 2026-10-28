// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Raster maps and pins a phone sends up — the <c>rasterMaps</c> and <c>rasterPins</c> sections —
/// and the rasters stream that hands them to other phones.
///
/// Every absence below is asserted beside a presence over the same fixture. The uploader is an
/// Editor and a member of the club; it created both caves and every place, and may place them all.
/// The member is a Viewer in the same club: it reads every cave here, holds no exact-location right
/// anywhere, and so is withheld the guarded cave and the guarded place on this channel, and may
/// change no cave.
/// </summary>
public sealed class SyncRasterTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string marker = Guid.NewGuid().ToString("N")[..8];

    private HttpClient uploader = null!; // Editor, club member — creates every cave and place
    private HttpClient member = null!;   // Viewer, club member — places nothing guarded, writes nothing
    private HttpClient anonymous = null!;
    private Guid uploaderId;
    private Guid clubId;
    private long caveTypeId;
    private long placeTypeId;

    public SyncRasterTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-syncraster-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async ValueTask InitializeAsync()
    {
        uploaderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"sr-up-{marker}@t.local");
        var memberId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"sr-mem-{marker}@t.local");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var club = new CavingGroup { Name = $"Club {marker}", Slug = $"club-{marker}" };
            db.CavingGroups.Add(club);
            await db.SaveChangesAsync();
            clubId = club.Id;
            await RosterHelper.AddMemberAsync(db, clubId, uploaderId);
            await RosterHelper.AddMemberAsync(db, clubId, memberId);
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            placeTypeId = await db.FeatureTypes.Where(t => t.Code == "cave_place").Select(t => t.Id).FirstAsync();
        }

        uploader = await AuthHelper.BearerClientAsync(factory, $"sr-up-{marker}@t.local");
        member = await AuthHelper.BearerClientAsync(factory, $"sr-mem-{marker}@t.local");
        anonymous = factory.CreateClient();
    }

    [Fact]
    public async Task A_map_reaches_a_phone_through_the_cave_the_set_hands_it_with_only_the_pins_of_places_it_is_handed()
    {
        var open = await CreateCaveAsync(locationProtected: false);
        var guarded = await CreateCaveAsync(locationProtected: true);
        var openPlace = await CreatePlaceAsync(open, locationProtected: false);
        var guardedPlace = await CreatePlaceAsync(open, locationProtected: true);
        var uploadSet = await CreateSetAsync(uploader, [open, guarded], clubId);

        var mapInOpen = Guid.CreateVersion7();
        var mapInGuarded = Guid.CreateVersion7();
        var pinAtOpen = Guid.CreateVersion7();
        var pinAtGuarded = Guid.CreateVersion7();
        var answer = await UploadAsync(
            uploader,
            uploadSet,
            maps: [await NewMapAsync(mapInOpen, open, "Plan", "planView"), await NewMapAsync(mapInGuarded, guarded, "Profil", "projectedProfile")],
            pins: [Pin(pinAtOpen, mapInOpen, openPlace, 120, 80), Pin(pinAtGuarded, mapInOpen, guardedPlace, 300, 210)]);
        Statuses(answer, "rasterMaps").ShouldBe(["created", "created"]);
        Statuses(answer, "rasterPins").ShouldBe(["created", "created"]);

        // Fixture proof: the member reads both caves through the web, is handed the open cave and
        // its open place by the feature stream, and is withheld the guarded cave and the guarded
        // place, which the uploader is handed.
        (await member.GetAsync($"/api/v1/caves/{guarded}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var memberSet = await CreateSetAsync(member, [open, guarded]);
        var memberFeatures = FeatureIds(await GetPageAsync(member, $"/api/v1/sync/sets/{memberSet}/download"));
        memberFeatures.ShouldContain(open);
        memberFeatures.ShouldContain(openPlace);
        memberFeatures.ShouldNotContain(guarded);
        memberFeatures.ShouldNotContain(guardedPlace);
        var uploaderFeatures = FeatureIds(await GetPageAsync(uploader, $"/api/v1/sync/sets/{uploadSet}/download"));
        uploaderFeatures.ShouldContain(guarded);
        uploaderFeatures.ShouldContain(guardedPlace);

        // The member is given the map of the open cave with the pin of the open place, and neither
        // the map of the guarded cave nor the pin of the guarded place, nor a word that names them.
        var members = await new ContractFixture()
            .Literal(marker, "marker").Name(memberSet, "set").Name(open, "cave").Name(openPlace, "place")
            .Name(mapInOpen, "map").Name(pinAtOpen, "pin")
            .RecordGetAsync("25-rasters-first-page", member, $"/api/v1/sync/sets/{memberSet}/rasters");
        MapIds(members).ShouldBe([mapInOpen]);
        PinIds(Map(members, mapInOpen)).ShouldBe([pinAtOpen]);
        var pin = Map(members, mapInOpen).GetProperty("pins")[0];
        pin.GetProperty("placeId").GetGuid().ShouldBe(openPlace);
        pin.GetProperty("x").GetInt32().ShouldBe(120);
        pin.GetProperty("y").GetInt32().ShouldBe(80);
        members.GetRawText().ShouldNotContain(guardedPlace.ToString());
        members.GetRawText().ShouldNotContain(pinAtGuarded.ToString());
        members.GetRawText().ShouldNotContain(mapInGuarded.ToString());
        ShouldCarryNoPosition(members);

        // The uploader, who may place everything, is given everything: the same fixture, the other way.
        var uploaders = await GetPageAsync(uploader, $"/api/v1/sync/sets/{uploadSet}/rasters");
        MapIds(uploaders).ShouldBe([mapInOpen, mapInGuarded], ignoreOrder: true);
        PinIds(Map(uploaders, mapInOpen)).ShouldBe([pinAtOpen, pinAtGuarded], ignoreOrder: true);
        var shaped = Map(uploaders, mapInOpen);
        shaped.GetProperty("mapType").GetString().ShouldBe("planView");
        shaped.GetProperty("caveId").GetGuid().ShouldBe(open);
        shaped.GetProperty("documentId").GetGuid().ShouldNotBe(Guid.Empty);
        Map(uploaders, mapInGuarded).GetProperty("mapType").GetString().ShouldBe("projectedProfile");

        (await anonymous.GetAsync($"/api/v1/sync/sets/{uploadSet}/rasters")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_map_names_the_area_it_hangs_in_only_to_a_reader_the_set_hands_that_area()
    {
        var open = await CreateCaveAsync(locationProtected: false);
        var guardedPlace = await CreatePlaceAsync(open, locationProtected: true);
        var uploadSet = await CreateSetAsync(uploader, [open], clubId);
        var map = Guid.CreateVersion7();
        var answer = await UploadAsync(
            uploader, uploadSet, maps: [await NewMapAsync(map, open, "Plan", "planView", caveAreaId: guardedPlace)]);
        Statuses(answer, "rasterMaps").ShouldBe(["created"]);

        var memberSet = await CreateSetAsync(member, [open]);
        FeatureIds(await GetPageAsync(member, $"/api/v1/sync/sets/{memberSet}/download")).ShouldNotContain(guardedPlace);

        var members = await GetPageAsync(member, $"/api/v1/sync/sets/{memberSet}/rasters");
        MapIds(members).ShouldBe([map]);
        members.GetRawText().ShouldNotContain(guardedPlace.ToString());

        var uploaders = await GetPageAsync(uploader, $"/api/v1/sync/sets/{uploadSet}/rasters");
        Map(uploaders, map).GetProperty("caveAreaId").GetGuid().ShouldBe(guardedPlace);
    }

    [Fact]
    public async Task A_map_is_filed_on_its_cave_as_a_map_and_its_picture_reaches_a_reader_by_the_location_rule()
    {
        var open = await CreateCaveAsync(locationProtected: false);
        var guarded = await CreateCaveAsync(locationProtected: true);
        var set = await CreateSetAsync(uploader, [open], clubId);
        var memberSet = await CreateSetAsync(member, [open]);
        var map = Guid.CreateVersion7();
        var picture = Corpus.GeotaggedJpeg(45.41, 25.71);
        var answer = await UploadAsync(uploader, set, maps: [await NewMapAsync(map, open, "Plan general", "planView", picture)]);
        Statuses(answer, "rasterMaps").ShouldBe(["created"]);

        // The picture is a document of the uploader's, bound to the set's club, hung on the cave with
        // the map role under the map's title, and the map names it.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var stored = await db.RasterMaps.AsNoTracking().SingleAsync(m => m.Id == map);
            stored.OwnerUserId.ShouldBe(uploaderId);
            stored.CaveFeatureId.ShouldBe(open);
            var document = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == stored.DocumentId);
            document.OwnerUserId.ShouldBe(uploaderId);
            document.CavingGroupId.ShouldBe(clubId);
            var link = await db.Attachments.AsNoTracking().SingleAsync(a => a.FeatureId == open && a.Role == AttachmentRole.Map2d);
            link.Caption.ShouldBe("Plan general");
            (await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == link.FileId)).DocumentVersionId.ShouldNotBe(Guid.Empty);
        }

        // Before the picture also hangs on a guarded cave, the reader may place everything it hangs
        // on, and is handed the stored file.
        var link0 = MapLink(await GetPageAsync(member, $"/api/v1/sync/sets/{memberSet}/documents"), open);
        link0.GetProperty("role").GetString().ShouldBe("map2d");
        link0.GetProperty("document").GetProperty("reach").GetString().ShouldBe("original");

        // Hung on a cave the reader cannot place as well, the picture's embedded position is one the
        // reader may not have: the reader gets the picture whole without its location, the uploader
        // the stored file.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var fileId = await db.Attachments.Where(a => a.FeatureId == open && a.Role == AttachmentRole.Map2d).Select(a => a.FileId).SingleAsync();
            db.Attachments.Add(new Attachment { FileId = fileId, FeatureId = guarded, Role = AttachmentRole.PhotoInterior });
            await db.SaveChangesAsync();
        }

        MapLink(await GetPageAsync(member, $"/api/v1/sync/sets/{memberSet}/documents"), open)
            .GetProperty("document").GetProperty("reach").GetString().ShouldBe("locationFree");
        MapLink(await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/documents"), open)
            .GetProperty("document").GetProperty("reach").GetString().ShouldBe("original");
    }

    [Fact]
    public async Task A_removed_pin_is_named_to_a_reader_who_would_be_given_it_and_is_silent_where_its_place_is_withheld()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var openPlace = await CreatePlaceAsync(cave, locationProtected: false);
        var guardedPlace = await CreatePlaceAsync(cave, locationProtected: true);
        var uploadSet = await CreateSetAsync(uploader, [cave], clubId);
        var memberSet = await CreateSetAsync(member, [cave]);

        var map = Guid.CreateVersion7();
        var atOpen = Guid.CreateVersion7();
        var atGuarded = Guid.CreateVersion7();
        var created = await UploadAsync(
            uploader, uploadSet,
            maps: [await NewMapAsync(map, cave, "Plan", "planView")],
            pins: [Pin(atOpen, map, openPlace, 10, 20), Pin(atGuarded, map, guardedPlace, 30, 40)]);
        var membersBefore = await GetPageAsync(member, $"/api/v1/sync/sets/{memberSet}/rasters");
        PinIds(Map(membersBefore, map)).ShouldBe([atOpen]);
        PinTombstones(membersBefore).ShouldBeEmpty();
        var membersCursor = membersBefore.GetProperty("nextCursor").GetString()!;
        var uploadersCursor = (await GetPageAsync(uploader, $"/api/v1/sync/sets/{uploadSet}/rasters")).GetProperty("nextCursor").GetString()!;

        var removed = await UploadAsync(uploader, uploadSet, pins:
        [
            PinRemoval(atOpen, map, Revision(created, "rasterPins", atOpen)),
            PinRemoval(atGuarded, map, Revision(created, "rasterPins", atGuarded)),
        ]);
        Statuses(removed, "rasterPins").ShouldBe(["deleted", "deleted"]);

        // The member is told the pin it was given went, beside the map that comes again without
        // it, and not a word of the pin on the place it is withheld. The tombstone names the map.
        var membersAfter = await GetPageAsync(member, $"/api/v1/sync/sets/{memberSet}/rasters?cursor={Uri.EscapeDataString(membersCursor)}");
        PinIds(Map(membersAfter, map)).ShouldBeEmpty();
        PinTombstones(membersAfter).ShouldBe([atOpen]);
        var tombstone = membersAfter.GetProperty("pinTombstones")[0];
        tombstone.GetProperty("mapId").GetGuid().ShouldBe(map);
        tombstone.TryGetProperty("deletedAt", out _).ShouldBeTrue();
        membersAfter.GetRawText().ShouldNotContain(atGuarded.ToString());
        membersAfter.GetRawText().ShouldNotContain(guardedPlace.ToString());
        Tombstones(membersAfter).ShouldBeEmpty();

        var uploadersAfter = await GetPageAsync(uploader, $"/api/v1/sync/sets/{uploadSet}/rasters?cursor={Uri.EscapeDataString(uploadersCursor)}");
        PinTombstones(uploadersAfter).ShouldBe([atOpen, atGuarded], ignoreOrder: true);

        // On the stream's one cursor: told once and not again, and on a first read from the start
        // too — one row a page, so the cut is what is tested.
        var caughtUp = await GetPageAsync(member, $"/api/v1/sync/sets/{memberSet}/rasters?cursor={Uri.EscapeDataString(membersAfter.GetProperty("nextCursor").GetString()!)}");
        PinTombstones(caughtUp).ShouldBeEmpty();
        MapIds(caughtUp).ShouldBeEmpty();
        var walked = new List<Guid>();
        string? cursor = null;
        for (var page = 0; page < 10; page++)
        {
            var url = $"/api/v1/sync/sets/{memberSet}/rasters?pageSize=1" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var answer = await GetPageAsync(member, url);
            (MapIds(answer).Count + PinTombstones(answer).Count + Tombstones(answer).Count).ShouldBeLessThanOrEqualTo(1);
            walked.AddRange(PinTombstones(answer));
            cursor = answer.GetProperty("nextCursor").GetString();
            if (!answer.GetProperty("hasMore").GetBoolean())
            {
                break;
            }
        }

        walked.ShouldBe([atOpen]);

        // A removed map takes its pins with it under its own tombstone: none is named again.
        await UploadAsync(uploader, uploadSet, maps: [MapRemoval(map, await CurrentRevisionAsync(map))]);
        var fromStart = await GetPageAsync(member, $"/api/v1/sync/sets/{memberSet}/rasters");
        Tombstones(fromStart).ShouldBe([map]);
        PinTombstones(fromStart).ShouldBeEmpty();
    }

    [Fact]
    public async Task Maps_and_pins_are_arbitrated_one_by_one_a_resend_writes_nothing_and_removals_reach_the_stream()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var placeA = await CreatePlaceAsync(cave, locationProtected: false);
        var placeB = await CreatePlaceAsync(cave, locationProtected: false);
        var set = await CreateSetAsync(uploader, [cave], clubId);
        var map = Guid.CreateVersion7();
        var pin = Guid.CreateVersion7();
        var batch = Guid.CreateVersion7();

        var created = await UploadAsync(
            uploader, set, batchId: batch,
            maps: [await NewMapAsync(map, cave, "Plan", "planView", sortOrder: 2)],
            pins: [Pin(pin, map, placeA, 10, 20)]);
        var mapRevision = Revision(created, "rasterMaps", map);
        var pinRevision = Revision(created, "rasterPins", pin);

        // The same batch again is answered from the record and the same rows sent as new are
        // answered unchanged, whichever way the phone retries.
        (await UploadAsync(uploader, set, batchId: batch, maps: [await NewMapAsync(map, cave, "Plan", "planView")]))
            .GetProperty("replayed").GetBoolean().ShouldBeTrue();
        var resent = await UploadAsync(uploader, set, maps: [await NewMapAsync(map, cave, "Plan", "planView")], pins: [Pin(pin, map, placeA, 10, 20)]);
        Statuses(resent, "rasterMaps").ShouldBe(["unchanged"]);
        Statuses(resent, "rasterPins").ShouldBe(["unchanged"]);

        // An edit against the revision the phone holds is applied; against a stale one it is not.
        var edited = await UploadAsync(
            uploader, set,
            maps: [NewMapRow(map, cave, "Plan v2", "extendedProfile", baseRevision: mapRevision, sortOrder: 7)],
            pins: [Pin(pin, map, placeB, 33, 44, baseRevision: pinRevision)]);
        Statuses(edited, "rasterMaps").ShouldBe(["updated"]);
        Statuses(edited, "rasterPins").ShouldBe(["updated"]);
        var stale = await UploadAsync(
            uploader, set,
            maps: [NewMapRow(map, cave, "Plan v3", "planView", baseRevision: mapRevision)],
            pins: [Pin(pin, map, placeA, 1, 1, baseRevision: pinRevision)]);
        Statuses(stale, "rasterMaps").ShouldBe(["conflict"]);
        Statuses(stale, "rasterPins").ShouldBe(["conflict"]);
        Code(stale, "rasterMaps", map).ShouldBe("sync.conflict");

        var page = await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/rasters");
        var shaped = Map(page, map);
        shaped.GetProperty("title").GetString().ShouldBe("Plan v2");
        shaped.GetProperty("mapType").GetString().ShouldBe("extendedProfile");
        shaped.GetProperty("sortOrder").GetInt32().ShouldBe(7);
        var movedPin = shaped.GetProperty("pins")[0];
        movedPin.GetProperty("placeId").GetGuid().ShouldBe(placeB);
        movedPin.GetProperty("x").GetInt32().ShouldBe(33);

        // A pin removed alone leaves the map with no pins, and the stream hands the map again to a
        // phone that had read past it — the map's own revision does not move, its list of pins did.
        var cursor = page.GetProperty("nextCursor").GetString();
        var pinGone = await UploadAsync(uploader, set, pins: [PinRemoval(pin, map, Revision(edited, "rasterPins", pin))]);
        Statuses(pinGone, "rasterPins").ShouldBe(["deleted"]);
        var afterPin = Map(await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/rasters"), map);
        PinIds(afterPin).ShouldBeEmpty();
        afterPin.GetProperty("revision").GetString().ShouldBe(shaped.GetProperty("revision").GetString());
        var sinceCursor = await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/rasters?cursor={Uri.EscapeDataString(cursor!)}");
        PinIds(Map(sinceCursor, map)).ShouldBeEmpty();

        // A removed map is a tombstone on the stream, its picture goes with it, and it stays gone.
        var removed = await UploadAsync(uploader, set, maps: [MapRemoval(map, Revision(edited, "rasterMaps", map))]);
        Statuses(removed, "rasterMaps").ShouldBe(["deleted"]);
        var afterRemoval = await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/rasters");
        MapIds(afterRemoval).ShouldBeEmpty();
        Tombstones(afterRemoval).ShouldBe([map]);
        Statuses(await UploadAsync(uploader, set, maps: [MapRemoval(map, Revision(removed, "rasterMaps", map))]), "rasterMaps")
            .ShouldBe(["unchanged"]);
        var back = await UploadAsync(uploader, set, maps: [NewMapRow(map, cave, "Plan", "planView", baseRevision: Revision(removed, "rasterMaps", map))]);
        Code(back, "rasterMaps", map).ShouldBe("sync.row_deleted");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var stored = await db.RasterMaps.AsNoTracking().IgnoreQueryFilters().SingleAsync(m => m.Id == map);
        (await db.Documents.AsNoTracking().IgnoreQueryFilters().SingleAsync(d => d.Id == stored.DocumentId)).DeletedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Rows_that_cannot_be_written_are_refused_with_their_own_codes_and_nothing_is_stored()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var otherCave = await CreateCaveAsync(locationProtected: false);
        var place = await CreatePlaceAsync(cave, locationProtected: false);
        var foreignPlace = await CreatePlaceAsync(otherCave, locationProtected: false);
        var set = await CreateSetAsync(uploader, [cave, otherCave], clubId);
        var memberSet = await CreateSetAsync(member, [cave]);

        // A viewer reads the cave and may not change it: its map is refused where the editor's is written.
        // (The viewer cannot even stage bytes, so its row names a transfer that does not exist: the
        // right to change the cave is asked before anything is said about the bytes.)
        var byMember = await UploadAsync(member, memberSet, maps: [NewMapRow(Guid.CreateVersion7(), cave, "Plan", "planView", picture: Corpus.Jpeg(), stagedUploadId: Guid.CreateVersion7())]);
        Statuses(byMember, "rasterMaps").ShouldBe(["rejected"]);
        Code(byMember, "rasterMaps", byMember.GetProperty("rasterMaps")[0].GetProperty("id").GetGuid()).ShouldBe("sync.row_forbidden");

        var map = Guid.CreateVersion7();
        var ok = await UploadAsync(uploader, set, maps: [await NewMapAsync(map, cave, "Plan", "planView")]);
        Statuses(ok, "rasterMaps").ShouldBe(["created"]);
        var mapRevision = Revision(ok, "rasterMaps", map);

        var unknownCave = Guid.CreateVersion7();
        var noBytes = Guid.CreateVersion7();
        var badHash = Guid.CreateVersion7();
        var notAnImage = Guid.CreateVersion7();
        var staged = Corpus.Jpeg();
        var pdf = Corpus.Pdf();
        var refused = await UploadAsync(uploader, set, maps:
        [
            await NewMapAsync(unknownCave, Guid.CreateVersion7(), "Nowhere", "planView"),
            NewMapRow(noBytes, cave, "No bytes", "planView", picture: staged, stagedUploadId: Guid.CreateVersion7()),
            await NewMapAsync(badHash, cave, "Wrong hash", "planView", staged, hashOf: Corpus.TaggedJpeg()),
            await NewMapAsync(notAnImage, cave, "A PDF", "other", pdf, fileName: "scan.pdf"),
        ]);
        Statuses(refused, "rasterMaps").ShouldBe(["rejected", "rejected", "rejected", "rejected"]);
        Code(refused, "rasterMaps", unknownCave).ShouldBe("sync.cave_not_found");
        Code(refused, "rasterMaps", noBytes).ShouldBe("sync.file_bytes_missing");
        Code(refused, "rasterMaps", badHash).ShouldBe("sync.file_hash_mismatch");
        Code(refused, "rasterMaps", notAnImage).ShouldBe("sync.raster_not_an_image");

        // A map stays with its cave; its area has to lie inside that cave.
        var moved = await UploadAsync(uploader, set, maps: [NewMapRow(map, otherCave, "Plan", "planView", baseRevision: mapRevision)]);
        Code(moved, "rasterMaps", map).ShouldBe("sync.map_cave_fixed");
        var wrongArea = await UploadAsync(uploader, set, maps: [NewMapRow(map, cave, "Plan", "planView", baseRevision: mapRevision, caveAreaId: foreignPlace)]);
        Code(wrongArea, "rasterMaps", map).ShouldBe("sync.area_not_found");

        // Pins: an unknown map, a place of another cave, a viewer, the same place twice.
        var strayPin = Guid.CreateVersion7();
        var foreignPin = Guid.CreateVersion7();
        var firstPin = Guid.CreateVersion7();
        var twicePin = Guid.CreateVersion7();
        var pins = await UploadAsync(uploader, set, pins:
        [
            Pin(strayPin, Guid.CreateVersion7(), place, 1, 1),
            Pin(foreignPin, map, foreignPlace, 1, 1),
            Pin(firstPin, map, place, 2, 2),
            Pin(twicePin, map, place, 3, 3),
        ]);
        Code(pins, "rasterPins", strayPin).ShouldBe("sync.raster_map_not_found");
        Code(pins, "rasterPins", foreignPin).ShouldBe("sync.place_not_found");
        Statuses(pins, "rasterPins").Skip(2).ShouldBe(["created", "rejected"]);
        Code(pins, "rasterPins", twicePin).ShouldBe("sync.pin_exists");

        var byViewer = await UploadAsync(member, memberSet, pins: [Pin(Guid.CreateVersion7(), map, place, 9, 9)]);
        Code(byViewer, "rasterPins", byViewer.GetProperty("rasterPins")[0].GetProperty("id").GetGuid()).ShouldBe("sync.row_forbidden");

        // A pixel is never negative: refused whole as a bad request, not half-applied.
        var bad = await member.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new { batchId = Guid.CreateVersion7(), contractVersion = 1, rasterPins = new[] { Pin(Guid.CreateVersion7(), map, place, -1, 0) } });
        (await uploader.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new { batchId = Guid.CreateVersion7(), contractVersion = 1, rasterPins = new[] { Pin(Guid.CreateVersion7(), map, place, -1, 0) } }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        bad.StatusCode.ShouldNotBe(HttpStatusCode.OK);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.RasterMaps.AsNoTracking().CountAsync(m => m.CaveFeatureId == cave)).ShouldBe(1, "only the map that was written");
        (await db.RasterPins.AsNoTracking().CountAsync(p => p.RasterMapId == map)).ShouldBe(1, "only the first pin of the place");
        // The refused PDF's bytes were not made into a document: nothing but the one picture hangs on the cave.
        (await db.Attachments.AsNoTracking().CountAsync(a => a.FeatureId == cave && a.Role == AttachmentRole.Map2d)).ShouldBe(1);
    }

    [Fact]
    public async Task A_new_picture_for_a_map_stacks_a_revision_and_an_undo_takes_each_change_of_the_upload_back()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var placeA = await CreatePlaceAsync(cave, locationProtected: false);
        var placeB = await CreatePlaceAsync(cave, locationProtected: false);
        var set = await CreateSetAsync(uploader, [cave], clubId);
        var map = Guid.CreateVersion7();
        var pin = Guid.CreateVersion7();
        var first = Corpus.Jpeg();
        var second = Corpus.GeotaggedJpeg(45.42, 25.72);

        var created = await UploadAsync(uploader, set,
            maps: [await NewMapAsync(map, cave, "Plan", "planView", first)], pins: [Pin(pin, map, placeA, 10, 10)]);
        var documentId = await DocumentOfAsync(map);

        // Undoing the upload that created the map removes the map, its pin, its picture and the
        // link that listed it among the cave's maps — and the stream says the map went.
        var undoCreate = await RevertAsync(uploader, created.GetProperty("importBatchId").GetGuid());
        undoCreate.GetProperty("items").EnumerateArray().ShouldAllBe(i => i.GetProperty("outcome").GetString() == "undone");
        undoCreate.GetProperty("items").EnumerateArray().Any(i => i.TryGetProperty("rasterMapId", out var id) && id.ValueKind == JsonValueKind.String).ShouldBeTrue();
        MapIds(await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/rasters")).ShouldBeEmpty();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.RasterMaps.AsNoTracking().IgnoreQueryFilters().SingleAsync(m => m.Id == map)).DeletedAt.ShouldNotBeNull();
            (await db.RasterPins.AsNoTracking().IgnoreQueryFilters().SingleAsync(p => p.Id == pin)).DeletedAt.ShouldNotBeNull();
            (await db.Documents.AsNoTracking().IgnoreQueryFilters().SingleAsync(d => d.Id == documentId)).DeletedAt.ShouldNotBeNull();
            (await db.Attachments.AsNoTracking().CountAsync(a => a.FeatureId == cave && a.Role == AttachmentRole.Map2d)).ShouldBe(0);
        }

        // A map that is there again: one new upload, then a second changing everything about it.
        var map2 = Guid.CreateVersion7();
        var pin2 = Guid.CreateVersion7();
        var made = await UploadAsync(uploader, set,
            maps: [await NewMapAsync(map2, cave, "Plan", "planView", first, sortOrder: 1)], pins: [Pin(pin2, map2, placeA, 10, 10)]);
        var doc2 = await DocumentOfAsync(map2);
        var changed = await UploadAsync(uploader, set,
            maps: [await NewMapAsync(map2, cave, "Renamed", "other", second, baseRevision: Revision(made, "rasterMaps", map2), sortOrder: 9)],
            pins: [Pin(pin2, map2, placeB, 77, 88, baseRevision: Revision(made, "rasterPins", pin2))]);
        Statuses(changed, "rasterMaps").ShouldBe(["updated"]);
        (await VersionCountAsync(doc2)).ShouldBe(2, "the new picture is a new revision; the earlier one is kept");
        (await CurrentHashAsync(doc2)).ShouldBe(Hash(second));

        var undoChange = await RevertAsync(uploader, changed.GetProperty("importBatchId").GetGuid());
        undoChange.GetProperty("items").EnumerateArray().ShouldAllBe(i => i.GetProperty("outcome").GetString() == "undone");
        var restored = Map(await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/rasters"), map2);
        restored.GetProperty("title").GetString().ShouldBe("Plan");
        restored.GetProperty("mapType").GetString().ShouldBe("planView");
        restored.GetProperty("sortOrder").GetInt32().ShouldBe(1);
        var pinBack = restored.GetProperty("pins")[0];
        pinBack.GetProperty("placeId").GetGuid().ShouldBe(placeA);
        pinBack.GetProperty("x").GetInt32().ShouldBe(10);
        (await CurrentHashAsync(doc2)).ShouldBe(Hash(first));
        (await VersionCountAsync(doc2)).ShouldBe(2, "the undone revision stays in the list");

        // A map removed by an upload comes back with its picture when that upload is undone.
        var removed = await UploadAsync(uploader, set, maps: [MapRemoval(map2, (await CurrentRevisionAsync(map2)))]);
        Statuses(removed, "rasterMaps").ShouldBe(["deleted"]);
        MapIds(await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/rasters")).ShouldBeEmpty();
        var undoRemoval = await RevertAsync(uploader, removed.GetProperty("importBatchId").GetGuid());
        undoRemoval.GetProperty("items").EnumerateArray().ShouldAllBe(i => i.GetProperty("outcome").GetString() == "undone");
        MapIds(await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/rasters")).ShouldBe([map2]);
        await using var verify = factory.Services.CreateAsyncScope();
        var check = verify.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await check.Documents.AsNoTracking().SingleAsync(d => d.Id == doc2)).DeletedAt.ShouldBeNull();
    }

    public async ValueTask DisposeAsync()
    {
        await factory.DisposeAsync();
        Dispose();
    }

    public void Dispose()
    {
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    // ---- request rows -------------------------------------------------------------------------

    private static object NewMapRow(
        Guid id, Guid caveId, string title, string mapType, DateTimeOffset? baseRevision = null, int? sortOrder = null,
        byte[]? picture = null, Guid? stagedUploadId = null, Guid? caveAreaId = null) => new
    {
        id,
        baseRevision,
        caveId,
        caveAreaId,
        title,
        mapType,
        sortOrder,
        fileName = "map.jpg",
        sha256 = picture is null ? null : Hash(picture),
        sizeBytes = picture?.Length,
        stagedUploadId,
    };

    /// <summary>A map row whose picture is staged first, as a phone does it.</summary>
    private async Task<object> NewMapAsync(
        Guid id, Guid caveId, string title, string mapType, byte[]? picture = null, DateTimeOffset? baseRevision = null,
        int? sortOrder = null, byte[]? hashOf = null, string fileName = "map.jpg", Guid? caveAreaId = null)
    {
        picture ??= Corpus.Jpeg();
        var staged = await StageAsync(uploader, fileName, picture);
        return new
        {
            id,
            baseRevision,
            caveId,
            caveAreaId,
            title,
            mapType,
            sortOrder,
            fileName,
            sha256 = Hash(hashOf ?? picture),
            sizeBytes = picture.Length,
            stagedUploadId = staged,
        };
    }

    private static object MapRemoval(Guid id, DateTimeOffset baseRevision) => new { id, baseRevision, deleted = true };

    private static object Pin(Guid id, Guid mapId, Guid placeId, int x, int y, DateTimeOffset? baseRevision = null) =>
        new { id, rasterMapId = mapId, baseRevision, placeId, x, y };

    private static object PinRemoval(Guid id, Guid mapId, DateTimeOffset baseRevision) =>
        new { id, rasterMapId = mapId, baseRevision, deleted = true };

    private static async Task<JsonElement> UploadAsync(
        HttpClient client, Guid set, IReadOnlyList<object>? maps = null, IReadOnlyList<object>? pins = null, Guid? batchId = null)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId = batchId ?? Guid.CreateVersion7(),
            contractVersion = 1,
            rasterMaps = maps,
            rasterPins = pins,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static async Task<Guid> StageAsync(HttpClient client, string fileName, byte[] bytes)
    {
        var opened = await client.PostAsJsonAsync("/api/v1/files/uploads", new { fileName, sizeBytes = bytes.Length, stageForSync = true });
        var payload = await opened.Content.ReadAsStringAsync();
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var session = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        (await client.PutAsync($"/api/v1/files/uploads/{session}?offset=0", content)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsync($"/api/v1/files/uploads/{session}/complete", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        return session;
    }

    private static async Task<JsonElement> RevertAsync(HttpClient client, Guid importBatchId)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/import-batches/{importBatchId}/revert", new { });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    // ---- reading answers ----------------------------------------------------------------------

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static IReadOnlyList<string?> Statuses(JsonElement answer, string section) =>
        [.. answer.GetProperty(section).EnumerateArray().Select(r => r.GetProperty("status").GetString())];

    private static string? Code(JsonElement answer, string section, Guid id)
    {
        var row = answer.GetProperty(section).EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id);
        return row.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String ? code.GetString() : null;
    }

    private static DateTimeOffset Revision(JsonElement answer, string section, Guid id) =>
        DateTimeOffset.Parse(
            answer.GetProperty(section).EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id)
                .GetProperty("revision").GetString()!);

    private static IReadOnlyList<Guid> FeatureIds(JsonElement page) =>
        [.. page.GetProperty("features").EnumerateArray().Select(f => f.GetProperty("id").GetGuid())];

    private static IReadOnlyList<Guid> MapIds(JsonElement page) =>
        [.. page.GetProperty("maps").EnumerateArray().Select(m => m.GetProperty("id").GetGuid())];

    private static IReadOnlyList<Guid> Tombstones(JsonElement page) =>
        [.. page.GetProperty("tombstones").EnumerateArray().Select(t => t.GetProperty("id").GetGuid())];

    private static IReadOnlyList<Guid> PinTombstones(JsonElement page) =>
        [.. page.GetProperty("pinTombstones").EnumerateArray().Select(t => t.GetProperty("id").GetGuid())];

    private static IReadOnlyList<Guid> PinIds(JsonElement map) =>
        [.. map.GetProperty("pins").EnumerateArray().Select(p => p.GetProperty("id").GetGuid())];

    private static JsonElement Map(JsonElement page, Guid id) =>
        page.GetProperty("maps").EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == id);

    private static JsonElement MapLink(JsonElement page, Guid caveId) =>
        page.GetProperty("links").EnumerateArray().Single(l =>
            l.GetProperty("target").GetProperty("id").GetGuid() == caveId && l.GetProperty("role").GetString() == "map2d");

    /// <summary>No member names where anything is: a map and its pins locate nothing themselves.</summary>
    private static void ShouldCarryNoPosition(JsonElement element)
    {
        var text = element.GetRawText();
        text.ShouldNotContain("\"geometry\"");
        text.ShouldNotContain("\"coordinates\"");
        text.ShouldNotContain("\"position\"");
    }

    private static async Task<JsonElement> GetPageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    // ---- reading the store --------------------------------------------------------------------

    private async Task<Guid> DocumentOfAsync(Guid mapId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.RasterMaps.AsNoTracking().IgnoreQueryFilters().Where(m => m.Id == mapId).Select(m => m.DocumentId).SingleAsync();
    }

    private async Task<DateTimeOffset> CurrentRevisionAsync(Guid mapId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.RasterMaps.AsNoTracking().Where(m => m.Id == mapId).Select(m => m.UpdatedAt).SingleAsync();
    }

    private async Task<int> VersionCountAsync(Guid documentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.DocumentVersions.AsNoTracking().CountAsync(v => v.DocumentId == documentId);
    }

    private async Task<string> CurrentHashAsync(Guid documentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (await (from version in db.DocumentVersions.AsNoTracking()
                         join file in db.StoredFiles.AsNoTracking() on version.Id equals file.DocumentVersionId
                         where version.DocumentId == documentId && version.IsCurrent
                         select file.Sha256).SingleAsync()).ToLowerInvariant();
    }

    // ---- fixtures -----------------------------------------------------------------------------

    private async Task<Guid> CreateSetAsync(HttpClient client, IReadOnlyList<Guid> roots, Guid? cavingGroupId = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker} {Guid.NewGuid():N}"[..30],
            cavingGroupId,
            uploadVisibility = "private",
            rootFeatureIds = roots,
            settings = new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCaveAsync(bool locationProtected)
    {
        var response = await uploader.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Rasters {Guid.NewGuid():N}"[..30],
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

    /// <summary>
    /// A marked place inside a cave, owned by the uploader. A guarded one is its own protection
    /// root, independently of the cave around it.
    /// </summary>
    private async Task<Guid> CreatePlaceAsync(Guid caveId, bool locationProtected)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var feature = new Feature
        {
            Id = Guid.CreateVersion7(),
            Name = $"Marcaj {Guid.NewGuid():N}"[..14],
            FeatureTypeId = placeTypeId,
            OwnerUserId = uploaderId,
            Visibility = Visibility.Authenticated,
            LocationProtected = locationProtected,
            Properties = """{"speleolocSchemaVersion":1}""",
        };
        await writer.CreateGenericAsync(feature, [new ParentSpec(caveId, IsPrimary: true)]);
        await db.SaveChangesAsync();
        return feature.Id;
    }
}
