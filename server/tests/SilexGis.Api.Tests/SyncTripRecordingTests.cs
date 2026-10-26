// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Api.Tests;

/// <summary>
/// Trip recordings a phone sends up — the <c>tripRecordings</c> and <c>tripPoints</c> sections —
/// and the trips stream that hands them to the other phones of the club.
///
/// Every absence below is asserted beside a presence over the same fixture. The uploader is an
/// Editor and a member of the club; it created both caves and every place, and may place them all.
/// The club member is a Viewer in the same club: it reads every cave here, holds no exact-location
/// right anywhere, and so is withheld the guarded cave and the guarded place on this channel. The
/// stranger is a Viewer outside the club, who reads the caves and holds no right over the club's
/// trips: a recording visible to the club is not visible to it.
/// </summary>
public sealed class SyncTripRecordingTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string marker = Guid.NewGuid().ToString("N")[..8];

    private HttpClient uploader = null!; // Editor, club member — creates every cave and place
    private HttpClient member = null!;   // Viewer, club member — places nothing guarded
    private HttpClient stranger = null!; // Viewer, outside the club
    private HttpClient anonymous = null!;
    private Guid uploaderId;
    private Guid strangerId;
    private Guid clubId;
    private long caveTypeId;
    private long placeTypeId;

    public SyncTripRecordingTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-synctrips-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        },
        // These tests are about what an upload stores and what the stream hands out; the trips an
        // upload's recordings become are made by a job, tested on its own, and a worker running it
        // here would race the assertions about the recordings as stored.
        JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        uploaderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"st-up-{marker}@t.local");
        var memberId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"st-mem-{marker}@t.local");
        strangerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"st-str-{marker}@t.local");

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

        uploader = await AuthHelper.BearerClientAsync(factory, $"st-up-{marker}@t.local");
        member = await AuthHelper.BearerClientAsync(factory, $"st-mem-{marker}@t.local");
        stranger = await AuthHelper.BearerClientAsync(factory, $"st-str-{marker}@t.local");
        anonymous = factory.CreateClient();
    }

    [Fact]
    public async Task A_recording_reaches_another_member_of_its_club_in_a_cave_the_set_hands_them_and_never_in_a_withheld_one()
    {
        var open = await CreateCaveAsync(locationProtected: false);
        var guarded = await CreateCaveAsync(locationProtected: true);
        var uploadSet = await CreateSetAsync(uploader, [open, guarded], clubId);

        var inOpen = Guid.CreateVersion7();
        var inGuarded = Guid.CreateVersion7();
        var answer = await UploadAsync(uploadSet, recordings: [Recording(inOpen, open), Recording(inGuarded, guarded)]);
        Statuses(answer, "tripRecordings").ShouldBe(["created", "created"]);

        // Fixture proof: the member reads both caves through the web and is withheld the guarded
        // one by the feature stream, which hands it to the uploader; the stranger reads both too.
        (await member.GetAsync($"/api/v1/caves/{guarded}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await stranger.GetAsync($"/api/v1/caves/{open}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var membersSet = await CreateSetAsync(member, [open, guarded]);
        var strangersSet = await CreateSetAsync(stranger, [open, guarded]);
        var membersFeatures = FeatureIds(await GetPageAsync(member, $"/api/v1/sync/sets/{membersSet}/download"));
        membersFeatures.ShouldContain(open);
        membersFeatures.ShouldNotContain(guarded);
        FeatureIds(await GetPageAsync(uploader, $"/api/v1/sync/sets/{uploadSet}/download")).ShouldContain(guarded);

        // The member of the club is given the recording in the open cave and not the one in the
        // guarded cave; the uploader, who may place both, is given both.
        RecordingIds(await GetPageAsync(member, $"/api/v1/sync/sets/{membersSet}/trips")).ShouldBe([inOpen]);
        RecordingIds(await GetPageAsync(uploader, $"/api/v1/sync/sets/{uploadSet}/trips"))
            .ShouldBe([inOpen, inGuarded], ignoreOrder: true);

        // The stranger is handed the open cave by its own feature stream, and is not given the
        // club's recording in it: the recording is the club's, not the cave's.
        FeatureIds(await GetPageAsync(stranger, $"/api/v1/sync/sets/{strangersSet}/download")).ShouldContain(open);
        RecordingIds(await GetPageAsync(stranger, $"/api/v1/sync/sets/{strangersSet}/trips")).ShouldBeEmpty();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var stored = await db.TripRecordings.AsNoTracking().SingleAsync(r => r.Id == inOpen);
        stored.OwnerUserId.ShouldBe(uploaderId);
        stored.CavingGroupId.ShouldBe(clubId);
        stored.Visibility.ShouldBe(Visibility.CavingGroup);
        stored.ConvertedTripLogId.ShouldBeNull();
    }

    [Fact]
    public async Task A_scan_of_a_place_the_reader_is_not_handed_is_left_out_of_a_recording_that_is_otherwise_delivered()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var openPlace = await CreatePlaceAsync(cave, locationProtected: false);
        var guardedPlace = await CreatePlaceAsync(cave, locationProtected: true);
        var uploadSet = await CreateSetAsync(uploader, [cave], clubId);
        var membersSet = await CreateSetAsync(member, [cave]);

        var recording = Guid.CreateVersion7();
        var atOpen = Guid.CreateVersion7();
        var atGuarded = Guid.CreateVersion7();
        var atNoPlace = Guid.CreateVersion7();
        var answer = await UploadAsync(
            uploadSet,
            recordings: [Recording(recording, cave)],
            points:
            [
                Point(atOpen, recording, openPlace, minutes: 10),
                Point(atGuarded, recording, guardedPlace, minutes: 20),
                Point(atNoPlace, recording, null, minutes: 30),
            ]);
        Statuses(answer, "tripPoints").ShouldBe(["created", "created", "created"]);

        // Fixture proof: the member is handed the cave and the open place and is withheld the
        // guarded place, which the uploader is handed.
        var membersFeatures = FeatureIds(await GetPageAsync(member, $"/api/v1/sync/sets/{membersSet}/download"));
        membersFeatures.ShouldContain(cave);
        membersFeatures.ShouldContain(openPlace);
        membersFeatures.ShouldNotContain(guardedPlace);
        FeatureIds(await GetPageAsync(uploader, $"/api/v1/sync/sets/{uploadSet}/download")).ShouldContain(guardedPlace);

        // Recorded as the member is handed it: the recording with two of its three scans, and no
        // word of the third — a phone cannot tell a scan withheld from a scan never made.
        var membersPage = await new ContractFixture()
            .Literal(marker, "marker").Name(membersSet, "set").Name(cave, "cave").Name(openPlace, "place")
            .Name(recording, "recording").Name(atOpen, "scan-at-place").Name(atNoPlace, "scan-at-no-place")
            .RecordGetAsync("24-trips-first-page", member, $"/api/v1/sync/sets/{membersSet}/trips");
        var membersRecording = Single(membersPage, recording);
        PointIds(membersRecording).ShouldBe([atOpen, atNoPlace]);
        membersRecording.GetRawText().ShouldNotContain(guardedPlace.ToString());

        var uploadersRecording = Single(await GetPageAsync(uploader, $"/api/v1/sync/sets/{uploadSet}/trips"), recording);
        PointIds(uploadersRecording).ShouldBe([atOpen, atGuarded, atNoPlace]);
        ShouldCarryNoPosition(uploadersRecording);
    }

    [Fact]
    public async Task Recordings_and_scans_are_arbitrated_one_by_one_a_resend_writes_nothing_and_removals_reach_the_stream()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var place = await CreatePlaceAsync(cave, locationProtected: false);
        var set = await CreateSetAsync(uploader, [cave], clubId);
        var recording = Guid.CreateVersion7();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        var batchId = Guid.CreateVersion7();
        var created = await UploadAsync(
            set,
            recordings: [Recording(recording, cave)],
            points: [Point(first, recording, place, minutes: 5), Point(second, recording, place, minutes: 15)],
            batchId: batchId);
        created.GetProperty("written").GetInt32().ShouldBe(3);
        var revision = Result(created, "tripRecordings", recording).GetProperty("revision").GetDateTimeOffset();
        var secondRevision = Result(created, "tripPoints", second).GetProperty("revision").GetDateTimeOffset();
        created.TryGetProperty("people", out _).ShouldBeFalse("a section the upload did not carry is not answered");

        // The same batch again is answered from its record and writes nothing.
        var resent = await UploadAsync(
            set, recordings: [Recording(recording, cave, title: "Changed")], batchId: batchId);
        resent.GetProperty("replayed").GetBoolean().ShouldBeTrue();
        Statuses(resent, "tripRecordings").ShouldBe(["created"]);

        // Sent as new again, under another batch: already here, so unchanged, with its revision.
        var again = await UploadAsync(set, recordings: [Recording(recording, cave)]);
        Result(again, "tripRecordings", recording).GetProperty("status").GetString().ShouldBe("unchanged");

        var page = await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/trips");
        var cursor = page.GetProperty("nextCursor").GetString()!;
        Single(page, recording).GetProperty("title").GetString().ShouldBe("Trip");

        // An edit against the revision the phone holds lands; one against an older revision loses.
        var edited = await UploadAsync(set, recordings: [Recording(recording, cave, title: "Renamed", baseRevision: revision)]);
        Result(edited, "tripRecordings", recording).GetProperty("status").GetString().ShouldBe("updated");
        var stale = await UploadAsync(set, recordings: [Recording(recording, cave, title: "Lost", baseRevision: revision)]);
        Result(stale, "tripRecordings", recording).GetProperty("status").GetString().ShouldBe("conflict");

        // A scan removed is a change to its recording: the recording comes again on the cursor,
        // with the scan gone from its list.
        var removed = await UploadAsync(set, points:
        [
            new { id = second, recordingId = recording, baseRevision = secondRevision, deleted = true },
        ]);
        Result(removed, "tripPoints", second).GetProperty("status").GetString().ShouldBe("deleted");
        var moved = await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/trips?cursor={Uri.EscapeDataString(cursor)}");
        var after = Single(moved, recording);
        after.GetProperty("title").GetString().ShouldBe("Renamed");
        PointIds(after).ShouldBe([first]);

        // A recording removed reaches the stream as a tombstone on the same cursor.
        var current = after.GetProperty("revision").GetDateTimeOffset();
        var gone = await UploadAsync(set, recordings: [new { id = recording, baseRevision = current, deleted = true }]);
        Result(gone, "tripRecordings", recording).GetProperty("status").GetString().ShouldBe("deleted");
        var tail = await GetPageAsync(
            uploader, $"/api/v1/sync/sets/{set}/trips?cursor={Uri.EscapeDataString(moved.GetProperty("nextCursor").GetString()!)}");
        RecordingIds(tail).ShouldBeEmpty();
        Tombstones(tail).ShouldBe([recording]);

        // Gone is gone: a write to it is refused, a second removal changes nothing, and a new scan
        // finds no recording to belong to.
        var late = await UploadAsync(
            set,
            recordings: [Recording(recording, cave, title: "Late", baseRevision: current)],
            points: [Point(Guid.CreateVersion7(), recording, place, minutes: 40)]);
        Result(late, "tripRecordings", recording).GetProperty("code").GetString().ShouldBe("sync.row_deleted");
        late.GetProperty("tripPoints")[0].GetProperty("code").GetString().ShouldBe("sync.recording_not_found");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var batch = await db.ImportBatches.AsNoTracking().SingleAsync(b => b.SyncBatchId == batchId);
        var lines = await db.ImportBatchItems.AsNoTracking().Where(i => i.ImportBatchId == batch.Id).ToListAsync();
        lines.Count(l => l.TripRecordingId == recording && l.Action == ImportDecisionAction.Create).ShouldBe(1);
        lines.Where(l => l.TripRecordingPointId != null).Select(l => l.TripRecordingPointId!.Value)
            .ShouldBe([first, second], ignoreOrder: true);
    }

    [Fact]
    public async Task What_the_uploader_may_not_read_is_refused_alike_whether_it_exists_or_not()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var hiddenCave = await CreateCaveAsync(locationProtected: false, visibility: "private");
        var hiddenPlace = await CreatePlaceAsync(hiddenCave, locationProtected: false, visibility: Visibility.Private);

        // Fixture proof: the uploader reads the private cave, the member does not.
        (await uploader.GetAsync($"/api/v1/caves/{hiddenCave}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await member.GetAsync($"/api/v1/caves/{hiddenCave}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var uploadersSet = await CreateSetAsync(uploader, [cave], clubId);
        var uploadersRecording = Guid.CreateVersion7();
        Statuses(await UploadAsync(uploadersSet, recordings: [Recording(uploadersRecording, cave)]), "tripRecordings")
            .ShouldBe(["created"]);

        var membersSet = await CreateSetAsync(member, [cave]);
        var inHidden = Guid.CreateVersion7();
        var inNothing = Guid.CreateVersion7();
        var own = Guid.CreateVersion7();
        var answer = await UploadAsync(
            membersSet,
            member,
            recordings: [Recording(inHidden, hiddenCave), Recording(inNothing, Guid.CreateVersion7()), Recording(own, cave)],
            points:
            [
                Point(Guid.CreateVersion7(), own, hiddenPlace, minutes: 1),
                Point(Guid.CreateVersion7(), own, Guid.CreateVersion7(), minutes: 2),
                Point(Guid.CreateVersion7(), uploadersRecording, null, minutes: 3),
                Point(Guid.CreateVersion7(), Guid.CreateVersion7(), null, minutes: 4),
                Point(Guid.CreateVersion7(), own, null, minutes: 5),
            ]);

        // A Viewer may not add trips anywhere, so its recording in a cave it reads is refused for
        // that — after its two recordings in caves it cannot read are refused alike.
        Codes(answer, "tripRecordings").ShouldBe(["sync.cave_not_found", "sync.cave_not_found", CreateRules.ForbiddenCode]);

        // Its own recording never landed, so its scans find nothing to belong to — and neither do
        // a scan on the club's recording, which it may read and not write, or one on no recording.
        Codes(answer, "tripPoints").ShouldBe(
        [
            "sync.recording_not_found", "sync.recording_not_found", "sync.row_forbidden",
            "sync.recording_not_found", "sync.recording_not_found",
        ]);

        // The uploader may write its recording, and of three scans on it — a place nobody holds, a
        // place that is here and that it may not read, and a place it may read — the first two are
        // refused alike and the third is taken.
        var refusedPlace = await CreatePlaceAsync(cave, locationProtected: false, owner: strangerId);
        await DenyFeatureAsync(uploaderId, refusedPlace);
        (await uploader.GetAsync($"/api/v1/features/{refusedPlace}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await uploader.GetAsync($"/api/v1/features/{hiddenPlace}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var places = await UploadAsync(uploadersSet, points:
        [
            Point(Guid.CreateVersion7(), uploadersRecording, Guid.CreateVersion7(), minutes: 6),
            Point(Guid.CreateVersion7(), uploadersRecording, refusedPlace, minutes: 7),
            Point(Guid.CreateVersion7(), uploadersRecording, hiddenPlace, minutes: 8),
        ]);
        Codes(places, "tripPoints").ShouldBe(["sync.place_not_found", "sync.place_not_found", null]);
        Statuses(places, "tripPoints")[2].ShouldBe("created");
    }

    /// <summary>A rule refusing one account one feature, written straight into storage.</summary>
    private async Task DenyFeatureAsync(Guid userId, Guid featureId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Deny,
            Domain = AccessDomain.Features,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.Object,
            ScopeFeatureId = featureId,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task The_trips_stream_needs_an_account_its_own_set_and_its_own_cursor_and_an_upload_is_checked_whole()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var set = await CreateSetAsync(uploader, [cave], clubId);

        (await anonymous.GetAsync($"/api/v1/sync/sets/{set}/trips")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var notMine = await member.GetAsync($"/api/v1/sync/sets/{set}/trips");
        notMine.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOf(notMine)).ShouldBe("sync.set_not_found");

        // A position the feature stream issued is not one the trips stream will resume from.
        var featureCursor = (await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/download"))
            .GetProperty("nextCursor").GetString()!;
        var foreign = await uploader.GetAsync($"/api/v1/sync/sets/{set}/trips?cursor={Uri.EscapeDataString(featureCursor)}");
        foreign.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOf(foreign)).ShouldBe("sync.cursor_invalid");

        // A recording to be written needs its cave and its start; the whole upload is refused and
        // nothing of it is written.
        var recording = Guid.CreateVersion7();
        var invalid = await uploader.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            tripRecordings = new object[] { new { id = recording, title = "No cave" } },
        });
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // A walk of several pages hands every recording once.
        var ids = Enumerable.Range(0, 3).Select(_ => Guid.CreateVersion7()).ToList();
        await UploadAsync(set, recordings: [.. ids.Select(id => Recording(id, cave))]);
        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var url = $"/api/v1/sync/sets/{set}/trips?pageSize=2" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = await GetPageAsync(uploader, url);
            seen.AddRange(RecordingIds(page));
            cursor = page.GetProperty("hasMore").GetBoolean() ? page.GetProperty("nextCursor").GetString() : null;
        }
        while (cursor is not null);
        seen.ShouldBe(ids, ignoreOrder: true);
        seen.ShouldNotContain(recording);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.TripRecordings.IgnoreQueryFilters().AnyAsync(r => r.Id == recording)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_trip_removed_for_good_leaves_the_recording_it_was_made_from_with_no_report()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var set = await CreateSetAsync(uploader, [cave], clubId);
        var recording = Guid.CreateVersion7();
        Statuses(await UploadAsync(set, recordings: [Recording(recording, cave)]), "tripRecordings").ShouldBe(["created"]);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var trips = scope.ServiceProvider.GetRequiredService<TripLogWriteService>();
        var trip = new TripLog { Title = "Made from a recording", OwnerUserId = uploaderId, TripDate = new DateOnly(2026, 9, 12) };
        db.TripLogs.Add(trip);
        await db.SaveChangesAsync();
        await db.TripRecordings.Where(r => r.Id == recording)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ConvertedTripLogId, (Guid?)trip.Id));

        // Fixture proof: the recording names the report made from it.
        (await db.TripRecordings.AsNoTracking().SingleAsync(r => r.Id == recording)).ConvertedTripLogId.ShouldBe(trip.Id);

        await trips.PurgeAsync(trip);
        await db.SaveChangesAsync();

        (await db.TripLogs.IgnoreQueryFilters().AnyAsync(t => t.Id == trip.Id)).ShouldBeFalse();
        var kept = await db.TripRecordings.AsNoTracking().SingleAsync(r => r.Id == recording);
        kept.ConvertedTripLogId.ShouldBeNull("the recording outlives its report and may be made into one again");
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

    private static object Recording(
        Guid id, Guid caveId, string title = "Trip", DateTimeOffset? baseRevision = null) => new
    {
        id,
        baseRevision,
        caveId,
        title,
        description = "Invented for the test",
        log = "entered; turned back at the sump",
        startedAt = new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero),
        endedAt = new DateTimeOffset(2026, 9, 12, 16, 0, 0, TimeSpan.Zero),
        createdByDeviceUserId = Guid.CreateVersion7(),
    };

    private static object Point(Guid id, Guid recordingId, Guid? placeId, int minutes) => new
    {
        id,
        recordingId,
        placeId,
        scannedAt = new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero).AddMinutes(minutes),
        notes = $"scan {minutes}",
    };

    private Task<JsonElement> UploadAsync(
        Guid set, IReadOnlyList<object>? recordings = null, IReadOnlyList<object>? points = null, Guid? batchId = null) =>
        UploadAsync(set, uploader, recordings, points, batchId);

    private static async Task<JsonElement> UploadAsync(
        Guid set,
        HttpClient client,
        IReadOnlyList<object>? recordings = null,
        IReadOnlyList<object>? points = null,
        Guid? batchId = null)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId = batchId ?? Guid.CreateVersion7(),
            contractVersion = 1,
            tripRecordings = recordings,
            tripPoints = points,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    private static IReadOnlyList<string?> Statuses(JsonElement answer, string section) =>
        [.. answer.GetProperty(section).EnumerateArray().Select(r => r.GetProperty("status").GetString())];

    private static IReadOnlyList<string?> Codes(JsonElement answer, string section) =>
        [.. answer.GetProperty(section).EnumerateArray().Select(r =>
            r.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String ? code.GetString() : null)];

    private static JsonElement Result(JsonElement answer, string section, Guid id) =>
        answer.GetProperty(section).EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id);

    private static IReadOnlyList<Guid> FeatureIds(JsonElement page) =>
        [.. page.GetProperty("features").EnumerateArray().Select(f => f.GetProperty("id").GetGuid())];

    private static IReadOnlyList<Guid> RecordingIds(JsonElement page) =>
        [.. page.GetProperty("recordings").EnumerateArray().Select(r => r.GetProperty("id").GetGuid())];

    private static IReadOnlyList<Guid> Tombstones(JsonElement page) =>
        [.. page.GetProperty("tombstones").EnumerateArray().Select(t => t.GetProperty("id").GetGuid())];

    private static IReadOnlyList<Guid> PointIds(JsonElement recording) =>
        [.. recording.GetProperty("points").EnumerateArray().Select(p => p.GetProperty("id").GetGuid())];

    private static JsonElement Single(JsonElement page, Guid id) =>
        page.GetProperty("recordings").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id);

    /// <summary>No member names where anything is: a recording and its scans locate nothing themselves.</summary>
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
        return JsonDocument.Parse(payload).RootElement;
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();

    private async Task<Guid> CreateSetAsync(HttpClient client, IReadOnlyList<Guid> roots, Guid? cavingGroupId = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker}",
            cavingGroupId,
            uploadVisibility = "private",
            rootFeatureIds = roots,
            settings = new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCaveAsync(bool locationProtected, string visibility = "authenticated")
    {
        var response = await uploader.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Trips {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility,
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
    private async Task<Guid> CreatePlaceAsync(
        Guid caveId, bool locationProtected, Visibility visibility = Visibility.Authenticated, Guid? owner = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var feature = new Feature
        {
            Id = Guid.CreateVersion7(),
            Name = $"Marcaj {Guid.NewGuid():N}"[..14],
            FeatureTypeId = placeTypeId,
            OwnerUserId = owner ?? uploaderId,
            Visibility = visibility,
            LocationProtected = locationProtected,
            Properties = """{"speleolocSchemaVersion":1}""",
        };
        await writer.CreateGenericAsync(feature, [new ParentSpec(caveId, IsPrimary: true)]);
        await db.SaveChangesAsync();
        return feature.Id;
    }
}
