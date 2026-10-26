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
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Photos on a phone's trip recordings and on the scans within them: the <c>documentLinks</c>
/// section naming a recording or a scan, the documents stream handing those links to the club's
/// other phones, the reach of a photo's position, the conversion that copies the photos to the
/// trip, and the undo of an upload that hung them.
///
/// Three accounts. The uploader is an Editor and a member of the club: it creates every cave and
/// place and may place them all. The member is a Viewer in the same club: it reads the club's
/// recordings, holds no exact-location right anywhere, and so is withheld the guarded place on this
/// channel. The stranger is a Viewer outside the club, who reads the caves and none of the club's
/// recordings. Every absence is asserted beside a presence over the same fixture. Every picture is
/// generated, with an invented position.
/// </summary>
public sealed class SyncRecordingPhotoTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string marker = Guid.NewGuid().ToString("N")[..8];
    private int pictures;

    private HttpClient uploader = null!;
    private HttpClient member = null!;
    private HttpClient stranger = null!;
    private Guid uploaderId;
    private Guid memberId;
    private Guid strangerId;
    private Guid clubId;
    private long caveTypeId;
    private long placeTypeId;

    public SyncRecordingPhotoTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-recphotos-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            // The trips an upload's recordings become are made by a job, which a test here runs
            // by hand when it is the subject; a worker would race the assertions about the links.
            JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        uploaderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"rp-up-{marker}@t.local");
        memberId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"rp-mem-{marker}@t.local");
        strangerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"rp-str-{marker}@t.local");

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

        uploader = await AuthHelper.BearerClientAsync(factory, $"rp-up-{marker}@t.local");
        member = await AuthHelper.BearerClientAsync(factory, $"rp-mem-{marker}@t.local");
        stranger = await AuthHelper.BearerClientAsync(factory, $"rp-str-{marker}@t.local");
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

    [Fact]
    public async Task A_photo_on_a_recording_or_a_scan_reaches_the_clubs_phones_with_that_target_and_never_on_a_scan_of_a_withheld_place()
    {
        var world = await UploadWorldAsync();

        // Fixture proof: the member is handed the open place and the cave, and not the guarded
        // place; the uploader is handed all of them.
        var membersFeatures = FeatureIds(await GetPageAsync(member, $"/api/v1/sync/sets/{world.MemberSet}/download"));
        membersFeatures.ShouldContain(world.OpenPlace);
        membersFeatures.ShouldNotContain(world.GuardedPlace);
        FeatureIds(await GetPageAsync(uploader, $"/api/v1/sync/sets/{world.UploadSet}/download"))
            .ShouldContain(world.GuardedPlace);

        // The uploader is given all four links, each naming what it hangs on.
        var all = Links(await GetPageAsync(uploader, $"/api/v1/sync/sets/{world.UploadSet}/documents"));
        all.Select(l => (l.Kind, l.Target)).ShouldBe(
            [
                ("tripRecording", world.Recording),
                ("tripPoint", world.AtOpen),
                ("tripPoint", world.AtGuarded),
                ("tripPoint", world.AtNoPlace),
            ],
            ignoreOrder: true);

        // The member is given the links on the recording and on the scans of the open place and of
        // no place, and none on the scan of the place it is withheld.
        var mine = Links(await GetPageAsync(member, $"/api/v1/sync/sets/{world.MemberSet}/documents"));
        mine.Select(l => (l.Kind, l.Target)).ShouldBe(
            [
                ("tripRecording", world.Recording),
                ("tripPoint", world.AtOpen),
                ("tripPoint", world.AtNoPlace),
            ],
            ignoreOrder: true);
        mine.Select(l => l.Document).ShouldNotContain(world.DocumentAtGuarded);
        mine.Select(l => l.Document).ShouldContain(world.DocumentOnRecording);

        // The stranger is handed the cave and is given no link on the club's recording.
        var strangersSet = await CreateSetAsync(stranger, [world.Cave]);
        FeatureIds(await GetPageAsync(stranger, $"/api/v1/sync/sets/{strangersSet}/download")).ShouldContain(world.Cave);
        Links(await GetPageAsync(stranger, $"/api/v1/sync/sets/{strangersSet}/documents")).ShouldBeEmpty();

        // A photo on a recording is filed as other, as a picture on a trip says nothing about what
        // it shows.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var stored = await db.Attachments.AsNoTracking()
            .SingleAsync(a => a.EntityType == AttachedEntityType.TripRecording && a.EntityId == world.Recording);
        stored.Role.ShouldBe(AttachmentRole.Other);
        stored.FeatureId.ShouldBeNull();
    }

    [Fact]
    public async Task A_link_on_a_scan_of_a_withheld_place_is_refused_to_the_account_withheld_it_and_lands_for_the_one_that_may_place_it()
    {
        var world = await UploadWorldAsync();
        await AllowDocumentCreateAsync(memberId);
        await AllowDocumentCreateAsync(strangerId);

        // The member sends a picture of its own and names the scan of the place it is withheld. The
        // gate on the place answers before the question of the right to write is asked.
        var bytes = Picture();
        var staged = await StageAsync(member, "IMG_m.jpg", bytes);
        var documentId = Guid.CreateVersion7();
        var membersSet = world.MemberSet;
        var refused = await UploadAsync(member, membersSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documents = new[] { NewDocument(documentId, bytes, staged) },
            documentLinks = new[]
            {
                Link(documentId, "tripPoint", world.AtGuarded),
                Link(documentId, "tripPoint", world.AtOpen),
            },
        });
        var codes = Codes(refused, "documentLinks");
        codes[0].ShouldBe("sync.location_forbidden");

        // The scan of the open place is readable to the member and is the club's to change, not the
        // member's: refused for the right to write, which is a different answer from the gate's.
        codes[1].ShouldBe("sync.row_forbidden");

        // The stranger cannot read the recording, so it is answered as one that is not here.
        var strangersBytes = Picture();
        var strangersStaged = await StageAsync(stranger, "IMG_s.jpg", strangersBytes);
        var strangersDocument = Guid.CreateVersion7();
        var strangersSet = await CreateSetAsync(stranger, [world.Cave]);
        var notHere = await UploadAsync(stranger, strangersSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documents = new[] { NewDocument(strangersDocument, strangersBytes, strangersStaged) },
            documentLinks = new[] { Link(strangersDocument, "tripRecording", world.Recording) },
        });
        Codes(notHere, "documentLinks").ShouldBe(["sync.target_not_found"]);

        // Beside both: the uploader hangs a picture on the same scan of the guarded place.
        var ownBytes = Picture();
        var ownStaged = await StageAsync(uploader, "IMG_u.jpg", ownBytes);
        var ownDocument = Guid.CreateVersion7();
        var landed = await UploadAsync(uploader, world.UploadSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documents = new[] { NewDocument(ownDocument, ownBytes, ownStaged) },
            documentLinks = new[] { Link(ownDocument, "tripPoint", world.AtGuarded) },
        });
        Statuses(landed, "documentLinks").ShouldBe(["created"]);

        // A kind the section does not name is a refusal of the section and not a guess.
        var unknown = await uploader.PostAsJsonAsync($"/api/v1/sync/sets/{world.UploadSet}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documentLinks = new[] { Link(ownDocument, "trip", world.Recording) },
        });
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_link_taken_down_and_a_recording_removed_reach_the_phone_as_tombstones_for_the_links_that_hung_on_them()
    {
        var world = await UploadWorldAsync();

        var before = await GetPageAsync(member, $"/api/v1/sync/sets/{world.MemberSet}/documents");
        var onRecording = Links(before).Single(l => l.Kind == "tripRecording");
        var onOpen = Links(before).Single(l => l.Target == world.AtOpen);

        // The uploader takes the picture on the open scan down; the member is told by link.
        var down = await UploadAsync(uploader, world.UploadSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documentLinks = new[] { Link(world.DocumentAtOpen, "tripPoint", world.AtOpen, deleted: true) },
        });
        Statuses(down, "documentLinks").ShouldBe(["deleted"]);
        var afterDetach = await GetPageAsync(member, $"/api/v1/sync/sets/{world.MemberSet}/documents");
        Tombstones(afterDetach).ShouldContain(onOpen.Id);
        Tombstones(afterDetach).ShouldNotContain(onRecording.Id);
        Links(afterDetach).Select(l => l.Id).ShouldContain(onRecording.Id);

        // The recording is removed: its remaining links go with it. The link on the guarded scan,
        // which the member was never given, is not told of either.
        var removed = await UploadAsync(uploader, world.UploadSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            tripRecordings = new[] { Recording(world.Recording, world.Cave, deleted: true, baseRevision: world.RecordingRevision) },
        });
        Statuses(removed, "tripRecordings").ShouldBe(["deleted"]);
        var afterRemoval = await GetPageAsync(member, $"/api/v1/sync/sets/{world.MemberSet}/documents");
        Tombstones(afterRemoval).ShouldContain(onRecording.Id);
        Links(afterRemoval).ShouldBeEmpty();

        var uploadersView = await GetPageAsync(uploader, $"/api/v1/sync/sets/{world.UploadSet}/documents");
        Tombstones(uploadersView).Count.ShouldBeGreaterThan(Tombstones(afterRemoval).Count);
        Links(uploadersView).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_photo_on_a_recording_is_placed_by_the_cave_and_a_photo_on_a_scan_by_the_cave_and_its_place()
    {
        var world = await UploadWorldAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var photos = scope.ServiceProvider.GetRequiredService<PhotoPositionDisclosure>();
        var fileOf = async (Guid document) => await (
            from version in db.DocumentVersions
            join file in db.StoredFiles on version.Id equals file.DocumentVersionId
            where version.DocumentId == document && version.IsCurrent
            select file.Id).FirstAsync();

        var onRecording = await fileOf(world.DocumentOnRecording);
        var atGuarded = await fileOf(world.DocumentAtGuarded);
        var atNoPlace = await fileOf(world.DocumentAtNoPlace);
        var chains = await photos.ProtectionChainsAsync([onRecording, atGuarded, atNoPlace]);

        // Fail-closed: without these links in the chain a picture's position would be disclosed to
        // readers who may not be told where its cave or place is.
        chains[onRecording].ShouldBe([world.Cave]);
        chains[atGuarded].ShouldBe([world.Cave, world.GuardedPlace], ignoreOrder: true);
        chains[atNoPlace].ShouldBe([world.Cave]);

        // A scan that has been removed still places the picture: the camera was where it was.
        await db.TripRecordingPoints.Where(p => p.Id == world.AtGuarded)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.DeletedAt, DateTimeOffset.UtcNow));
        (await photos.ProtectionChainsAsync([atGuarded]))[atGuarded]
            .ShouldBe([world.Cave, world.GuardedPlace], ignoreOrder: true);
    }

    [Fact]
    public async Task A_picture_on_a_recording_is_read_by_those_who_may_read_the_recording_in_both_forms_of_the_question()
    {
        var world = await UploadWorldAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<IAccessService>();
        var files = new List<Guid>();
        foreach (var document in new[] { world.DocumentOnRecording, world.DocumentAtOpen, world.DocumentAtGuarded })
        {
            files.Add(await (from version in db.DocumentVersions
                             join file in db.StoredFiles on version.Id equals file.DocumentVersionId
                             where version.DocumentId == document && version.IsCurrent
                             select file.Id).FirstAsync());
        }

        foreach (var (user, readsRecording) in new[] { (memberId, true), (strangerId, false), (uploaderId, true) })
        {
            var ctx = await AccessContextResolver.ResolveAsync(db, user, CancellationToken.None);
            var batched = await FileAccessRules.ReadableFileIdsAsync(db, ctx, files, CancellationToken.None);
            foreach (var file in files)
            {
                var single = await FileAccessRules.CanAccessAsync(
                    db, access, ctx, await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == file), CancellationToken.None);
                batched.Contains(file).ShouldBe(single, $"user {user}, file {file}");
                single.ShouldBe(readsRecording, $"user {user}, file {file}");
            }
        }

        // A scan removed hangs nothing readable, and the two forms still agree.
        await db.TripRecordingPoints.Where(p => p.Id == world.AtOpen)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.DeletedAt, DateTimeOffset.UtcNow));
        var memberCtx = await AccessContextResolver.ResolveAsync(db, memberId, CancellationToken.None);
        var onOpen = files[1];
        (await FileAccessRules.ReadableFileIdsAsync(db, memberCtx, [onOpen], CancellationToken.None)).ShouldNotContain(onOpen);
        (await FileAccessRules.CanAccessAsync(
            db, access, memberCtx, await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == onOpen), CancellationToken.None))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Conversion_copies_the_recordings_photos_to_the_trip_once_and_an_undo_of_the_upload_takes_the_trip_back()
    {
        var world = await UploadWorldAsync(convertible: true);
        var batch = world.ImportBatchId;

        await RunConversionAsync(batch);
        var tripId = await TripOfAsync(world.Recording);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var copies = await (
                from link in db.Attachments.AsNoTracking()
                where link.EntityType == AttachedEntityType.TripLog && link.EntityId == tripId
                join file in db.StoredFiles on link.FileId equals file.Id
                join version in db.DocumentVersions on file.DocumentVersionId equals version.Id
                orderby link.SortOrder
                select version.DocumentId).ToListAsync();

            // Every picture of the recording and its scans, once each, the recording's own first;
            // the originals stay where they were. The picture taken at the place with a guard of
            // its own is not copied: the trip names the cave alone, and a copy would be judged on
            // the cave once the scan's link went, handing its position to readers who may not
            // place that place.
            copies.Count.ShouldBe(3);
            copies[0].ShouldBe(world.DocumentOnRecording);
            copies.ShouldBe(
                [world.DocumentOnRecording, world.DocumentAtOpen, world.DocumentAtNoPlace],
                ignoreOrder: true);
            copies.ShouldNotContain(world.DocumentAtGuarded);
            (await db.Attachments.CountAsync(a => a.EntityType == AttachedEntityType.TripRecording && a.EntityId == world.Recording))
                .ShouldBe(1);
        }

        // A second run of the same conversion finds its work done and copies nothing.
        await RunConversionAsync(batch);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Attachments.CountAsync(a => a.EntityType == AttachedEntityType.TripLog && a.EntityId == tripId))
                .ShouldBe(3);
        }

        // The photos the conversion hung are the conversion's own work, so they do not count as the
        // trip having been touched since: the upload is taken back, links and report with it.
        var response = await uploader.PostAsJsonAsync($"/api/v1/import-batches/{batch}/revert", new { });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        var items = JsonDocument.Parse(payload).RootElement.GetProperty("items").EnumerateArray().ToList();
        items.ShouldNotBeEmpty();
        items.ShouldAllBe(i => i.GetProperty("outcome").GetString() == "undone", payload);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var points = new[] { world.AtOpen, world.AtGuarded, world.AtNoPlace };
            (await db.Attachments.CountAsync(a =>
                (a.EntityType == AttachedEntityType.TripRecording && a.EntityId == world.Recording)
                || (a.EntityType == AttachedEntityType.TripRecordingPoint && points.Contains(a.EntityId!.Value))))
                .ShouldBe(0);
            (await db.TripRecordings.IgnoreQueryFilters().SingleAsync(r => r.Id == world.Recording)).DeletedAt.ShouldNotBeNull();
            (await db.TripLogs.IgnoreQueryFilters().SingleAsync(t => t.Id == tripId)).DeletedAt.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task A_recording_and_its_scans_have_no_timeline_that_would_name_their_cave_and_places()
    {
        var world = await UploadWorldAsync();

        foreach (var client in new[] { uploader, member })
        {
            (await client.GetAsync($"/api/v1/history?entityType=TripRecording&entityId={world.Recording}"))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await client.GetAsync($"/api/v1/history?entityType=TripRecordingPoint&entityId={world.AtGuarded}"))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task Taking_down_a_photo_on_a_recording_is_undone_by_hanging_it_again_and_not_while_the_recording_is_gone()
    {
        var world = await UploadWorldAsync();

        var down = await UploadAsync(uploader, world.UploadSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documentLinks = new[] { Link(world.DocumentOnRecording, "tripRecording", world.Recording, deleted: true) },
        });
        Statuses(down, "documentLinks").ShouldBe(["deleted"]);
        (await LinkCountAsync(world.DocumentOnRecording, world.Recording)).ShouldBe(0);

        var revert = await RevertAsync(uploader, down.GetProperty("importBatchId").GetGuid());
        revert.GetProperty("items").EnumerateArray().Single().GetProperty("outcome").GetString().ShouldBe("undone");
        (await LinkCountAsync(world.DocumentOnRecording, world.Recording)).ShouldBe(1);

        // Taken down again, and the recording removed: the link is not hung back on a recording
        // that nothing would hand to a phone.
        var again = await UploadAsync(uploader, world.UploadSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documentLinks = new[] { Link(world.DocumentOnRecording, "tripRecording", world.Recording, deleted: true) },
        });
        await UploadAsync(uploader, world.UploadSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            tripRecordings = new[] { Recording(world.Recording, world.Cave, deleted: true, baseRevision: world.RecordingRevision) },
        });
        var refused = await RevertAsync(uploader, again.GetProperty("importBatchId").GetGuid());
        var line = refused.GetProperty("items").EnumerateArray().Single();
        line.GetProperty("outcome").GetString().ShouldBe("not_undone");
        line.GetProperty("reason").GetString().ShouldBe("container_deleted");
        (await LinkCountAsync(world.DocumentOnRecording, world.Recording)).ShouldBe(0);
    }

    // ---------------------------------------------------------------------------------------------
    // The fixture: one cave, an open place and a guarded one, a recording with a scan at each and a
    // scan at none, a picture on every one of the four, all sent in a single upload.
    // ---------------------------------------------------------------------------------------------

    private sealed record World(
        Guid Cave,
        Guid OpenPlace,
        Guid GuardedPlace,
        Guid UploadSet,
        Guid MemberSet,
        Guid Recording,
        Guid AtOpen,
        Guid AtGuarded,
        Guid AtNoPlace,
        Guid DocumentOnRecording,
        Guid DocumentAtOpen,
        Guid DocumentAtGuarded,
        Guid DocumentAtNoPlace,
        Guid ImportBatchId,
        DateTimeOffset RecordingRevision);

    private async Task<World> UploadWorldAsync(bool convertible = false)
    {
        var cave = await CreateCaveAsync();
        var openPlace = await CreatePlaceAsync(cave, locationProtected: false);
        var guardedPlace = await CreatePlaceAsync(cave, locationProtected: true);
        var uploadSet = await CreateSetAsync(uploader, [cave], clubId);
        var memberSet = await CreateSetAsync(member, [cave]);

        var recording = Guid.CreateVersion7();
        var atOpen = Guid.CreateVersion7();
        var atGuarded = Guid.CreateVersion7();
        var atNoPlace = Guid.CreateVersion7();
        var documents = new List<object>();
        var ids = new Dictionary<string, Guid>();
        foreach (var name in new[] { "recording", "open", "guarded", "noplace" })
        {
            var id = Guid.CreateVersion7();
            var bytes = Picture();
            documents.Add(NewDocument(id, bytes, await StageAsync(uploader, $"IMG_{name}.jpg", bytes)));
            ids[name] = id;
        }

        var answer = await UploadAsync(uploader, uploadSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documents,
            tripRecordings = new[] { Recording(recording, cave, endedAt: convertible ? Ended : null) },
            tripPoints = new[]
            {
                Point(atOpen, recording, openPlace, 10),
                Point(atGuarded, recording, guardedPlace, 20),
                Point(atNoPlace, recording, null, 30),
            },
            documentLinks = new[]
            {
                Link(ids["recording"], "tripRecording", recording),
                Link(ids["open"], "tripPoint", atOpen),
                Link(ids["guarded"], "tripPoint", atGuarded),
                Link(ids["noplace"], "tripPoint", atNoPlace),
            },
        });
        Statuses(answer, "documents").ShouldBe(["created", "created", "created", "created"]);
        Statuses(answer, "tripRecordings").ShouldBe(["created"]);
        Statuses(answer, "tripPoints").ShouldBe(["created", "created", "created"]);
        Statuses(answer, "documentLinks").ShouldBe(["created", "created", "created", "created"]);

        return new World(
            cave, openPlace, guardedPlace, uploadSet, memberSet, recording, atOpen, atGuarded, atNoPlace,
            ids["recording"], ids["open"], ids["guarded"], ids["noplace"],
            answer.GetProperty("importBatchId").GetGuid(),
            answer.GetProperty("tripRecordings").EnumerateArray().Single().GetProperty("revision").GetDateTimeOffset());
    }

    private static readonly DateTimeOffset Started = new(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Ended = Started.AddHours(8);

    private static object Recording(
        Guid id, Guid caveId, DateTimeOffset? endedAt = null, bool deleted = false, DateTimeOffset? baseRevision = null) => new
    {
        id,
        baseRevision,
        caveId,
        title = "Trip",
        description = "Invented for the test",
        log = "entered; turned back at the sump",
        startedAt = Started,
        endedAt,
        deleted,
        createdByDeviceUserId = Guid.CreateVersion7(),
    };

    private static object Point(Guid id, Guid recordingId, Guid? placeId, int minutes) => new
    {
        id,
        recordingId,
        placeId,
        scannedAt = Started.AddMinutes(minutes),
        notes = $"scan {minutes}",
    };

    private static object NewDocument(Guid id, byte[] bytes, Guid staged) => new
    {
        id,
        fileName = "photo.jpg",
        sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
        sizeBytes = bytes.Length,
        stagedUploadId = staged,
    };

    private static object Link(Guid documentId, string kind, Guid target, bool deleted = false) =>
        new { documentId, target = new { kind, id = target }, deleted };

    /// <summary>A generated picture, different from every other, with an invented position.</summary>
    private byte[] Picture() => Corpus.GeotaggedJpeg(45.30 + (0.01 * ++pictures), 25.60);

    private sealed record LinkRow(Guid Id, string Kind, Guid Target, Guid Document);

    private static IReadOnlyList<LinkRow> Links(JsonElement page) =>
    [
        .. page.GetProperty("links").EnumerateArray().Select(l => new LinkRow(
            l.GetProperty("id").GetGuid(),
            l.GetProperty("target").GetProperty("kind").GetString()!,
            l.GetProperty("target").GetProperty("id").GetGuid(),
            l.GetProperty("documentId").GetGuid())),
    ];

    private static IReadOnlyList<Guid> Tombstones(JsonElement page) =>
        [.. page.GetProperty("tombstones").EnumerateArray().Select(t => t.GetProperty("id").GetGuid())];

    private static IReadOnlyList<Guid> FeatureIds(JsonElement page) =>
        [.. page.GetProperty("features").EnumerateArray().Select(f => f.GetProperty("id").GetGuid())];

    private static IReadOnlyList<string?> Statuses(JsonElement answer, string section) =>
        [.. answer.GetProperty(section).EnumerateArray().Select(r => r.GetProperty("status").GetString())];

    private static IReadOnlyList<string?> Codes(JsonElement answer, string section) =>
        [.. answer.GetProperty(section).EnumerateArray().Select(r =>
            r.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String ? code.GetString() : null)];

    private static async Task<JsonElement> UploadAsync(HttpClient client, Guid setId, object body)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/sync/sets/{setId}/upload", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static async Task<JsonElement> RevertAsync(HttpClient client, Guid importBatchId)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/import-batches/{importBatchId}/revert", new { });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static async Task<JsonElement> GetPageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
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

    private async Task<Guid> CreateCaveAsync()
    {
        var response = await uploader.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Recphotos {Guid.NewGuid():N}"[..30],
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

    /// <summary>A rule letting one person add documents anywhere, written straight into storage.</summary>
    private async Task AllowDocumentCreateAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Documents,
            Actions = AccessAction.Create,
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();
    }

    private async Task<int> LinkCountAsync(Guid documentId, Guid recordingId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await (from link in db.Attachments
                      join file in db.StoredFiles on link.FileId equals file.Id
                      join version in db.DocumentVersions on file.DocumentVersionId equals version.Id
                      where link.EntityType == AttachedEntityType.TripRecording
                          && link.EntityId == recordingId
                          && version.DocumentId == documentId
                      select link.Id).CountAsync();
    }

    private async Task RunConversionAsync(Guid importBatchId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var jobs = await db.ProcessingJobs.AsNoTracking()
            .Where(j => j.Kind == ProcessingJobKinds.TripRecordingConvert)
            .ToListAsync();
        var job = jobs.Single(j => JsonSerializer.Deserialize<TripRecordingConvertPayload>(
            j.Payload, JsonSerializerOptions.Web)!.ImportBatchId == importBatchId);
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.TripRecordingConvert);
        await handler.ExecuteAsync(job, CancellationToken.None);
    }

    private async Task<Guid> TripOfAsync(Guid recording)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (await db.TripRecordings.AsNoTracking().SingleAsync(r => r.Id == recording)).ConvertedTripLogId
            ?? throw new ShouldAssertException("no trip was made");
    }
}
