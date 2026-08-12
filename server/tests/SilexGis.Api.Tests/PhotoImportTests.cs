// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The path from a trip's worth of photographs to caves, entrances and surface features: the
/// grouping, the proximity list, the confirmation and its undo — plus the four things this
/// feature could get wrong quietly. Answering "there is already something within twelve metres"
/// about a cave the caller may not locate; hanging pictures on somebody else's object without
/// write access; replacing a camera's own fix because somebody dragged something; and printing a
/// photograph's coordinates beside a file whose original is being withheld.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PhotoImportTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient editor = null!;
    private HttpClient other = null!;
    private Guid editorId;
    private string tag = null!;

    /// <summary>A fixed noon, so a case that talks about clocks has one to talk about.</summary>
    private static readonly DateTimeOffset Noon = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    public PhotoImportTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
        });
    }

    public async Task InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        editorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"pi-editor-{tag}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"pi-other-{tag}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"pi-editor-{tag}@t.local");
        other = await AuthHelper.BearerClientAsync(factory, $"pi-other-{tag}@t.local");
    }

    // ---------- the whole path ----------

    [Fact]
    public async Task A_drop_of_photographs_becomes_an_entrance_with_its_pictures_attached()
    {
        // Three pictures of one hole and one of a spring forty metres off.
        var a = await UploadAsync("Pestera Ursilor.jpg", At(45.610000, 25.510000, Noon));
        var b = await UploadAsync("IMG_2044.jpg", At(45.610030, 25.510020, Noon.AddMinutes(1)));
        var c = await UploadAsync("IMG_2045.jpg", At(45.610010, 25.510040, Noon.AddMinutes(2)));
        var far = await UploadAsync("IMG_2100.jpg", At(45.612000, 25.513000, Noon.AddMinutes(20)));

        var preview = await PreviewAsync(editor, [a, b, c, far]);

        preview.GetProperty("photoCount").GetInt32().ShouldBe(4);
        preview.GetProperty("placedCount").GetInt32().ShouldBe(2);
        preview.GetProperty("unplacedCount").GetInt32().ShouldBe(0);

        var items = Items(preview);
        items.Count.ShouldBe(2);

        // Twelve photographs of one entrance are one candidate with a gallery, not twelve points
        // to reject one at a time — that is the whole difference between a review somebody
        // finishes and one they abandon.
        var hole = items.Single(i => i.GetProperty("members").GetArrayLength() == 3);
        hole.GetProperty("proposedName").GetString().ShouldBe("Pestera Ursilor");
        hole.GetProperty("positionSource").GetString().ShouldBe("exif");

        // Nothing exists yet: a preview is a reading of the pictures, not a write.
        (await FeatureNamesAsync()).ShouldBeEmpty();

        var key = hole.GetProperty("key").GetGuid();
        var commit = await CommitAsync(editor, [a, b, c, far], [key], new Dictionary<string, object>
        {
            [key.ToString()] = new { action = "create", kind = "caveEntrance", name = $"Ursilor {tag}" },
        });

        commit.GetProperty("failures").EnumerateArray().ShouldBeEmpty();
        commit.GetProperty("batch").GetProperty("createdCount").GetInt32().ShouldBe(1);
        (await FeatureNamesAsync()).ShouldContain($"Ursilor {tag}");

        // Every picture of the place is hung on what it produced, in capture order, so the first
        // one is the object's first picture.
        var caveId = await FeatureIdAsync($"Ursilor {tag}");
        var attached = await AttachmentsOfAsync(caveId);
        attached.Count.ShouldBe(3);
        attached[0].ShouldBe(a);
    }

    [Fact]
    public async Task Undoing_a_confirmation_takes_back_the_objects_and_the_pictures_it_hung()
    {
        var mine = await UploadAsync("Aven.jpg", At(45.700000, 25.700000, Noon));
        var existingCave = await CreateCaveWithEntranceAsync($"Existing {tag}", 45.7100, 25.7100);
        var second = await UploadAsync("IMG_3001.jpg", At(45.710005, 25.710005, Noon.AddMinutes(5)));

        var preview = await PreviewAsync(editor, [mine, second]);
        var items = Items(preview);
        var newPlace = items.Single(i => Members(i).Contains(mine));
        var atExisting = items.Single(i => Members(i).Contains(second));

        // One place created, one filed onto something already in the registry — the two shapes
        // an undo has to reverse differently.
        var entranceId = EntranceIdOf(atExisting) ?? throw new InvalidOperationException("no proximity hit");
        var commit = await CommitAsync(
            editor,
            [mine, second],
            [newPlace.GetProperty("key").GetGuid(), atExisting.GetProperty("key").GetGuid()],
            new Dictionary<string, object>
            {
                [newPlace.GetProperty("key").GetGuid().ToString()] =
                    new { action = "create", kind = "cave", name = $"Aven {tag}" },
                [atExisting.GetProperty("key").GetGuid().ToString()] =
                    new { action = "attach", attachToFeatureId = entranceId },
            });

        commit.GetProperty("batch").GetProperty("createdCount").GetInt32().ShouldBe(1);
        commit.GetProperty("batch").GetProperty("attachedCount").GetInt32().ShouldBe(1);
        (await AttachmentsOfAsync(entranceId)).ShouldContain(second);

        var batchId = commit.GetProperty("batch").GetProperty("id").GetGuid();
        var revert = await editor.PostAsync($"/api/v1/import-batches/{batchId}/revert", null);
        revert.StatusCode.ShouldBe(HttpStatusCode.OK, await revert.Content.ReadAsStringAsync());

        // The created cave is gone, and so is the picture hung on somebody's existing entrance —
        // which soft-deleting the created objects alone would have left behind.
        (await FeatureNamesAsync()).ShouldNotContain($"Aven {tag}");
        (await AttachmentsOfAsync(entranceId)).ShouldNotContain(second);

        // The photographs themselves are untouched: they were uploaded, not created here.
        (await editor.GetAsync($"/api/v1/files/{second}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        _ = existingCave;
    }

    // ---------- what is already there ----------

    [Fact]
    public async Task The_proximity_list_names_what_is_already_near_a_picture()
    {
        await CreateCaveWithEntranceAsync($"Neighbour {tag}", 45.800000, 25.800000);
        var photo = await UploadAsync("IMG_4000.jpg", At(45.800200, 25.800000, Noon));

        var candidate = Items(await PreviewAsync(editor, [photo])).Single();
        var nearby = candidate.GetProperty("nearby").EnumerateArray().ToList();

        nearby.ShouldNotBeEmpty();
        nearby[0].GetProperty("caveName").GetString().ShouldBe($"Neighbour {tag}");
        nearby[0].GetProperty("distanceMeters").GetDouble().ShouldBeInRange(15, 30);
    }

    [Fact]
    public async Task The_proximity_list_is_a_location_oracle_and_says_nothing_about_a_guarded_cave()
    {
        // Two caves at the same distance from the picture; one is position-protected and the
        // second account may read it but not place it. "There is something within twenty metres"
        // is itself a position, so the guarded one must not appear — even at the cost of the
        // other account being offered a duplicate.
        await CreateCaveWithEntranceAsync($"Open {tag}", 45.900000, 25.900000);
        await CreateCaveWithEntranceAsync($"Guarded {tag}", 45.910000, 25.910000, locationProtected: true);

        var openPhoto = await UploadAsync("IMG_5000.jpg", At(45.900200, 25.900000, Noon), other);
        var guardedPhoto = await UploadAsync("IMG_5001.jpg", At(45.910200, 25.910000, Noon), other);

        var items = Items(await PreviewAsync(other, [openPhoto, guardedPhoto]));

        // The open one is answered — proving the search runs at all, so the guarded silence
        // below is protection rather than a query that finds nothing.
        var nearOpen = items.Single(i => Members(i).Contains(openPhoto));
        nearOpen.GetProperty("nearby").EnumerateArray()
            .Select(n => n.GetProperty("caveName").GetString())
            .ShouldContain($"Open {tag}");

        var nearGuarded = items.Single(i => Members(i).Contains(guardedPhoto));
        nearGuarded.GetProperty("nearby").EnumerateArray()
            .Select(n => n.GetProperty("caveName").GetString())
            .ShouldNotContain($"Guarded {tag}");
    }

    [Fact]
    public async Task Pictures_cannot_be_filed_onto_an_object_the_caller_may_not_write()
    {
        // This is the one place in the flow where a permission bites. Creating something new is
        // the caller's own to do and needs nobody's approval; hanging pictures on an object that
        // already exists is a write on that object, and is asked against it.
        var caveId = await CreateCaveWithEntranceAsync($"Mine {tag}", 46.000000, 26.000000);
        await DenyWriteAsync(caveId, $"pi-other-{tag}@t.local");
        var photo = await UploadAsync("IMG_6000.jpg", At(46.000100, 26.000000, Noon), other);

        var key = Items(await PreviewAsync(other, [photo])).Single().GetProperty("key").GetGuid();
        var response = await other.PostAsJsonAsync("/api/v1/photo-import/commit", new
        {
            options = DefaultOptions(),
            fileIds = new[] { photo },
            selection = new[] { key },
            decisions = new Dictionary<string, object>
            {
                [key.ToString()] = new { action = "attach", attachToFeatureId = caveId },
            },
        });

        // Nothing landed, and the refusal names the row it came from rather than a blank 403.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("code").GetString().ShouldBe("photo_import.nothing_created");
        (await AttachmentsOfAsync(caveId)).ShouldNotContain(photo);
    }

    // ---------- placing a picture ----------

    [Fact]
    public async Task A_picture_with_no_fix_is_its_own_unplaced_candidate_until_somebody_places_it()
    {
        var plain = await UploadAsync("IMG_7000.jpg", Plain(Noon));

        var preview = await PreviewAsync(editor, [plain]);
        preview.GetProperty("unplacedCount").GetInt32().ShouldBe(1);
        var candidate = Items(preview).Single();
        candidate.GetProperty("geom").ValueKind.ShouldBe(JsonValueKind.Null);
        candidate.GetProperty("positionSource").GetString().ShouldBe("none");

        // An unplaced picture is not selectable: confirming it would be a failure line per row.
        preview.GetProperty("selectableKeys").GetArrayLength().ShouldBe(0);

        var placed = await editor.PutAsJsonAsync($"/api/v1/files/{plain}/position", new
        {
            position = new[] { 25.123456, 45.123456 },
            replaceRecordedFix = false,
        });
        placed.StatusCode.ShouldBe(HttpStatusCode.OK, await placed.Content.ReadAsStringAsync());
        var body = JsonDocument.Parse(await placed.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("positionSource").GetString().ShouldBe("manual");

        var after = await PreviewAsync(editor, [plain]);
        after.GetProperty("placedCount").GetInt32().ShouldBe(1);
        after.GetProperty("selectableKeys").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Dragging_a_picture_that_already_carries_a_recorded_fix_has_to_say_so()
    {
        var photo = await UploadAsync("IMG_7100.jpg", At(45.300000, 25.300000, Noon));

        var refused = await editor.PutAsJsonAsync($"/api/v1/files/{photo}/position", new
        {
            position = new[] { 25.4, 45.4 },
            replaceRecordedFix = false,
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement
            .GetProperty("code").GetString().ShouldBe("file.position_recorded");

        // The camera's answer is still there, untouched.
        (await PositionOfAsync(photo)).GetProperty("positionSource").GetString().ShouldBe("exif");

        var accepted = await editor.PutAsJsonAsync($"/api/v1/files/{photo}/position", new
        {
            position = new[] { 25.4, 45.4 },
            replaceRecordedFix = true,
        });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);

        var now = await PositionOfAsync(photo);
        now.GetProperty("positionSource").GetString().ShouldBe("manual");
        // The altitude and bearing belonged to the reading that was replaced. A point somebody
        // dragged has neither, and keeping them would attach a camera's measurements to a
        // position it never took.
        now.GetProperty("altitudeMeters").ValueKind.ShouldBe(JsonValueKind.Null);
        now.GetProperty("directionDegrees").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_position_taken_off_by_hand_is_not_put_back_by_the_catch_up_pass()
    {
        // The catch-up pass exists to recover fixes nobody read at the time, and it must not
        // undo a decision somebody made. Taking a position off is such a decision — the camera's
        // guess is precisely what was overruled — so the pass has to leave it alone while still
        // recovering a picture whose fix was simply never read.
        var cleared = await UploadAsync("IMG_7200.jpg", At(45.200000, 25.200000, Noon));
        var unread = await UploadAsync("IMG_7201.jpg", At(45.210000, 25.210000, Noon));

        var removed = await editor.PutAsJsonAsync($"/api/v1/files/{cleared}/position", new
        {
            position = (double[]?)null,
            replaceRecordedFix = true,
        });
        removed.StatusCode.ShouldBe(HttpStatusCode.OK, await removed.Content.ReadAsStringAsync());

        // The second picture stands for an upload that predates reading fixes at all: it has a
        // point in its bytes and none on its row, and nobody has said anything about it.
        using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var file = await db.StoredFiles.FirstAsync(f => f.Id == unread);
            file.Geom = null;
            file.PositionSource = PhotoPositionSource.None;
            await db.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
                .First(h => h.Kind == ProcessingJobKinds.PhotoGeoBackfill);
            await handler.ExecuteAsync(
                new ProcessingJob { Kind = ProcessingJobKinds.PhotoGeoBackfill }, CancellationToken.None);

            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == cleared)).Geom.ShouldBeNull();
            (await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == unread)).Geom.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task A_photograph_never_moves_an_object_on_its_own_and_moving_it_is_an_ordinary_write()
    {
        var caveId = await CreateCaveWithEntranceAsync($"Moveable {tag}", 45.400000, 25.400000);
        var entranceId = await MainEntranceIdAsync(caveId);
        var photo = await UploadAsync("IMG_8000.jpg", At(45.400300, 25.400300, Noon));

        // Not attached yet: without that, this endpoint would be a way to move anybody's cave to
        // any coordinates a caller could put inside a photograph.
        var unattached = await editor.PostAsJsonAsync(
            $"/api/v1/features/{entranceId}/position-from-photo", new { fileId = photo });
        unattached.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await unattached.Content.ReadAsStringAsync()).RootElement
            .GetProperty("code").GetString().ShouldBe("feature.photo_not_attached");

        await AttachAsync(photo, entranceId);
        var moved = await editor.PostAsJsonAsync(
            $"/api/v1/features/{entranceId}/position-from-photo", new { fileId = photo });
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());

        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var feature = await db.Features.AsNoTracking().FirstAsync(f => f.Id == entranceId);
        feature.Geom!.Coordinate.Y.ShouldBe(45.400300, 1e-5);

        // It went through the ordinary audited write, so it is in the object's own history
        // rather than in a record only this feature knows how to read.
        var entity = entranceId.ToString();
        var audited = await db.AuditEntries.AsNoTracking()
            .AnyAsync(a => a.EntityId == entity && a.EntityType!.StartsWith(FeatureAudit.RootName));
        audited.ShouldBeTrue();
    }

    [Fact]
    public async Task An_account_without_write_access_may_not_move_an_object_from_a_picture()
    {
        var caveId = await CreateCaveWithEntranceAsync($"Theirs {tag}", 45.450000, 25.450000);
        var entranceId = await MainEntranceIdAsync(caveId);
        var photo = await UploadAsync("IMG_8100.jpg", At(45.450300, 25.450300, Noon));
        await AttachAsync(photo, entranceId);
        await DenyWriteAsync(caveId, $"pi-other-{tag}@t.local");

        var response = await other.PostAsJsonAsync(
            $"/api/v1/features/{entranceId}/position-from-photo", new { fileId = photo });

        // Accepting a photograph's position is an ordinary write on the object, so it is refused
        // exactly as an ordinary edit of that object would be — no queue, no proposal to review.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---------- placing by the clock ----------

    [Fact]
    public async Task A_picture_with_no_fix_is_placed_by_matching_its_time_against_a_track()
    {
        var geofileId = await UploadTrackAsync();
        var plain = await UploadAsync("IMG_9000.jpg", Plain(Noon.AddMinutes(2)));

        var options = DefaultOptions();
        options["trackGeofileId"] = geofileId;
        var preview = await PreviewAsync(editor, [plain], options);

        preview.GetProperty("trackFixCount").GetInt32().ShouldBeGreaterThan(0);
        preview.GetProperty("placedCount").GetInt32().ShouldBe(1);

        var candidate = Items(preview).Single();
        candidate.GetProperty("positionSource").GetString().ShouldBe("trackMatch");
        candidate.GetProperty("geom").GetProperty("coordinates")[1].GetDouble()
            .ShouldBe(45.62, 1e-6); // the fix recorded two minutes in
    }

    [Fact]
    public async Task A_camera_clock_an_hour_out_places_nothing_until_the_offset_says_so()
    {
        var geofileId = await UploadTrackAsync();
        // A camera left on the previous zone: every picture reads an hour early.
        var plain = await UploadAsync("IMG_9100.jpg", Plain(Noon.AddMinutes(2).AddHours(-1)));

        var options = DefaultOptions();
        options["trackGeofileId"] = geofileId;
        (await PreviewAsync(editor, [plain], options))
            .GetProperty("unplacedCount").GetInt32().ShouldBe(1);

        options["cameraClockOffsetSeconds"] = 3600;
        (await PreviewAsync(editor, [plain], options))
            .GetProperty("placedCount").GetInt32().ShouldBe(1);
    }

    // ---------- what a response may say about a position ----------

    [Fact]
    public async Task A_files_camera_panel_is_shown_where_its_coordinates_are_withheld()
    {
        // The picture hangs on a guarded cave, so the second account may see the picture but may
        // not be given the original — which carries the fix inside it. Printing that fix beside
        // the withheld file would be withholding nothing at all; the camera is a different
        // question and stays.
        var caveId = await CreateCaveWithEntranceAsync(
            $"Sensitive {tag}", 45.500000, 25.500000, locationProtected: true, visibility: "authenticated");
        var photo = await UploadAsync("IMG_9500.jpg", At(45.500000, 25.500000, Noon));
        await AttachAsync(photo, caveId);

        var mine = await ReadJsonAsync(await editor.GetAsync($"/api/v1/files/{photo}"));
        mine.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeTrue();
        mine.GetProperty("position").ValueKind.ShouldNotBe(JsonValueKind.Null);
        mine.GetProperty("photo").GetProperty("cameraMake").GetString().ShouldBe("SilexTest");

        var theirs = await ReadJsonAsync(await other.GetAsync($"/api/v1/files/{photo}"));
        theirs.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeFalse();
        theirs.GetProperty("position").ValueKind.ShouldBe(JsonValueKind.Null);
        theirs.GetProperty("photo").GetProperty("cameraMake").GetString().ShouldBe("SilexTest");
    }

    // ---------- filing a drop under a trip ----------

    /// <summary>
    /// Filing a drop under a trip writes on the trip twice: the pictures become part of its
    /// record, and a cave the drop brought into existence is named among the caves the trip is
    /// about. The second half is the one nothing else asserts — it happens in the commit
    /// service, far from the trip's own write path, and a trip that silently stopped naming the
    /// caves its own photographs created would look exactly like a trip nobody had filed
    /// anything under.
    /// </summary>
    [Fact]
    public async Task A_drop_filed_under_a_trip_names_the_cave_it_created_among_the_trips_caves()
    {
        var tripId = await CreateTripAsync($"Filing {tag}");

        // Fixture proof: the trip names nothing before the drop, so what it names afterwards
        // came from the drop rather than from the way it was created.
        (await TripCavesAsync(tripId)).ShouldBeEmpty();

        var photo = await UploadAsync("IMG_7000.jpg", At(45.640000, 25.540000, Noon));
        var options = DefaultOptions();
        options["tripLogId"] = tripId;

        var preview = await PreviewAsync(editor, [photo], options);
        var key = Items(preview).Single().GetProperty("key").GetGuid();
        var commit = await CommitAsync(editor, [photo], [key], new Dictionary<string, object>
        {
            [key.ToString()] = new { action = "create", kind = "cave", name = $"Filed {tag}" },
        }, options);
        commit.GetProperty("failures").EnumerateArray().ShouldBeEmpty();

        var caveId = await FeatureIdAsync($"Filed {tag}");
        (await TripCavesAsync(tripId)).ShouldBe([caveId]);

        // The pictures reach the trip as well, which is the half a reader would notice first —
        // asserted here so a fixture that stopped filing under the trip at all cannot pass the
        // naming assertion by accident.
        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Attachments.AsNoTracking().CountAsync(
            a => a.EntityType == AttachedEntityType.TripLog && a.EntityId == tripId))
            .ShouldBe(1);
    }

    /// <summary>
    /// An entrance added to a cave already in the registry names <em>the cave</em> on the trip.
    /// The drop produces an entrance object, and naming that instead would be invisible to every
    /// reader: the trip would list no caves, the cave would count no trips, and the photographs
    /// would lose the placement the cave's protection gives them — all of it silently, since an
    /// entrance is a linkable target like any other and nothing would refuse it.
    /// </summary>
    [Fact]
    public async Task An_entrance_added_to_an_existing_cave_under_a_trip_names_that_cave()
    {
        var tripId = await CreateTripAsync($"Entrance filing {tag}");
        var caveId = await CreateCaveWithEntranceAsync($"Second mouth {tag}", 45.680000, 25.580000);
        (await TripCavesAsync(tripId)).ShouldBeEmpty();

        // Far enough from the existing entrance to be a candidate of its own rather than a
        // proximity hit on it, so the decision below is what files it onto the cave.
        var photo = await UploadAsync("IMG_7200.jpg", At(45.684000, 25.584000, Noon));
        var options = DefaultOptions();
        options["tripLogId"] = tripId;

        var preview = await PreviewAsync(editor, [photo], options);
        var key = Items(preview).Single().GetProperty("key").GetGuid();
        var commit = await CommitAsync(editor, [photo], [key], new Dictionary<string, object>
        {
            [key.ToString()] = new
            {
                action = "create",
                kind = "caveEntrance",
                name = $"Upper entrance {tag}",
                attachToFeatureId = caveId,
            },
        }, options);
        commit.GetProperty("failures").EnumerateArray().ShouldBeEmpty();

        // The entrance really was created on that cave — so the naming below is about which of
        // the two objects the trip records, not about a commit that did nothing.
        (await FeatureNamesAsync()).ShouldContain($"Upper entrance {tag}");
        (await TripCavesAsync(tripId)).ShouldBe([caveId]);
    }

    /// <summary>
    /// A surface feature the drop created is not a cave the trip visited, so it is not named
    /// among them. The distinction is deliberate and easy to lose: the trip's cave list is a
    /// list of caves, and a spring added to it would be a different claim about the trip.
    /// </summary>
    [Fact]
    public async Task A_surface_feature_a_drop_created_is_not_named_among_the_trips_caves()
    {
        var tripId = await CreateTripAsync($"Surface {tag}");
        var photo = await UploadAsync("IMG_7100.jpg", At(45.660000, 25.560000, Noon));
        var options = DefaultOptions();
        options["tripLogId"] = tripId;

        var preview = await PreviewAsync(editor, [photo], options);
        var key = Items(preview).Single().GetProperty("key").GetGuid();
        var commit = await CommitAsync(editor, [photo], [key], new Dictionary<string, object>
        {
            [key.ToString()] = new
            {
                action = "create",
                kind = "surfaceFeature",
                featureTypeCode = "sinkhole",
                name = $"Doline {tag}",
            },
        }, options);
        commit.GetProperty("failures").EnumerateArray().ShouldBeEmpty();

        // The feature exists — so the empty cave list below is a claim the import declined to
        // make, not a commit that quietly did nothing.
        (await FeatureNamesAsync()).ShouldContain($"Doline {tag}");
        (await TripCavesAsync(tripId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_review_needs_an_account()
    {
        using var anonymous = factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/v1/photo-import/preview", new
        {
            options = DefaultOptions(),
            fileIds = Array.Empty<Guid>(),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_clustering_radius_larger_than_the_widest_offered_is_refused()
    {
        var options = DefaultOptions();
        options["clusterRadiusMeters"] = 999_999;
        var response = await editor.PostAsJsonAsync("/api/v1/photo-import/preview", new
        {
            options,
            fileIds = Array.Empty<Guid>(),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_review_survives_a_closed_tab()
    {
        var photo = await UploadAsync("IMG_9900.jpg", At(45.550000, 25.550000, Noon));
        var key = Items(await PreviewAsync(editor, [photo])).Single().GetProperty("key").GetGuid();

        var options = DefaultOptions();
        options["namePrefix"] = $"Recon {tag} ";
        var saved = await editor.PutAsJsonAsync("/api/v1/photo-import/session", new
        {
            fileIds = new[] { photo },
            options,
            decisions = new Dictionary<string, object>
            {
                [key.ToString()] = new { action = "create", kind = "cave", name = $"Kept {tag}" },
            },
        });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        var resumed = await ReadJsonAsync(await editor.GetAsync("/api/v1/photo-import/session"));
        resumed.GetProperty("fileIds").EnumerateArray().Single().GetGuid().ShouldBe(photo);
        resumed.GetProperty("options").GetProperty("namePrefix").GetString().ShouldBe($"Recon {tag} ");
        resumed.GetProperty("decisions").GetProperty(key.ToString())
            .GetProperty("name").GetString().ShouldBe($"Kept {tag}");

        // Another account's review is their own; nobody sees anybody else's.
        var theirs = await ReadJsonAsync(await other.GetAsync("/api/v1/photo-import/session"));
        theirs.GetProperty("fileIds").GetArrayLength().ShouldBe(0);
    }

    // ---------- helpers ----------

    private async Task<JsonElement> PreviewAsync(
        HttpClient client, IReadOnlyList<Guid> fileIds, Dictionary<string, object?>? options = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/photo-import/preview", new
        {
            options = options ?? DefaultOptions(),
            fileIds,
            pageSize = 100,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    private async Task<JsonElement> CommitAsync(
        HttpClient client,
        IReadOnlyList<Guid> fileIds,
        IReadOnlyList<Guid> selection,
        Dictionary<string, object>? decisions = null,
        Dictionary<string, object?>? options = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/photo-import/commit", new
        {
            options = options ?? DefaultOptions(),
            fileIds,
            selection,
            decisions = decisions ?? [],
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    private static Dictionary<string, object?> DefaultOptions() => new()
    {
        ["defaultKind"] = "caveEntrance",
        ["defaultCaveTypeCode"] = "cave",
        ["defaultEntranceTypeCode"] = "natural",
        ["clusterRadiusMeters"] = 25,
        ["proximityRadiusMeters"] = 80,
        ["visibility"] = "authenticated",
        ["elevation"] = "keep",
        ["locationProtected"] = false,
        ["tagIds"] = Array.Empty<long>(),
        ["cameraClockOffsetSeconds"] = 0,
        ["trackMatchToleranceSeconds"] = 120,
    };

    private static List<JsonElement> Items(JsonElement preview) =>
        [.. preview.GetProperty("items").EnumerateArray()];

    private static List<Guid> Members(JsonElement candidate) =>
        [.. candidate.GetProperty("members").EnumerateArray().Select(m => m.GetProperty("fileId").GetGuid())];

    /// <summary>The nearest entrance the proximity list offered, if it offered one.</summary>
    private static Guid? EntranceIdOf(JsonElement candidate)
    {
        var nearby = candidate.GetProperty("nearby").EnumerateArray().ToList();
        return nearby.Count == 0 ? null : nearby[0].GetProperty("featureId").GetGuid();
    }

    private async Task<JsonElement> PositionOfAsync(Guid fileId)
    {
        var file = await ReadJsonAsync(await editor.GetAsync($"/api/v1/files/{fileId}"));
        return file.GetProperty("position");
    }

    private async Task<Guid> UploadAsync(string fileName, byte[] bytes, HttpClient? client = null)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("image/jpeg");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        // These fixtures upload byte-identical content more than once, which the store now
        // warns about. Saying yes up front is what a person would do; deduplication is
        // asserted in its own suite rather than incidentally here.
        var response = await (client ?? editor).PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>A GPX track walking north, one fix a minute, starting at noon.</summary>
    private async Task<Guid> UploadTrackAsync()
    {
        var points = new StringBuilder();
        for (var i = 0; i < 10; i++)
        {
            var time = Noon.AddMinutes(i).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            var lat = (45.60 + (i * 0.01)).ToString("F5", CultureInfo.InvariantCulture);
            points.Append(CultureInfo.InvariantCulture, $"""<trkpt lat="{lat}" lon="25.50"><time>{time}</time></trkpt>""");
        }

        var gpx = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <gpx version="1.1" creator="test" xmlns="http://www.topografix.com/GPX/1/1">
              <trk><name>Walk {tag}</name><trkseg>{points}</trkseg></trk>
            </gpx>
            """;

        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(gpx));
        content.Headers.ContentType = new("application/gpx+xml");
        using var form = new MultipartFormDataContent
        {
            { content, "file", $"walk-{tag}.gpx" },
            { new StringContent($"Walk {tag}"), "name" },
        };
        var response = await editor.PostAsync("/api/v1/geofiles/", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task AttachAsync(Guid fileId, Guid featureId)
    {
        var response = await editor.PostAsJsonAsync("/api/v1/attachments/", new
        {
            fileId,
            entityType = "feature",
            entityId = featureId,
            role = "photoEntrance",
            sortOrder = 0,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Takes write on one object away from one account. Needed because the editor role this
    /// installation seeds grants write over the whole content domain — which is the intended
    /// default, and is why refusal has to be arranged deliberately to be tested at all.
    /// </summary>
    private async Task DenyWriteAsync(Guid featureId, string email)
    {
        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var userId = await db.Users.AsNoTracking()
            .Where(u => u.Email == email)
            .Select(u => u.Id)
            .FirstAsync();

        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Deny,
            Domain = AccessDomain.Features,
            Actions = AccessAction.Write,
            ScopeKind = AccessScopeKind.Subtree,
            ScopeFeatureId = featureId,
        });
        await db.SaveChangesAsync();
    }

    private async Task<List<Guid>> AttachmentsOfAsync(Guid featureId)
    {
        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Attachments.AsNoTracking()
            .Where(a => a.FeatureId == featureId)
            .OrderBy(a => a.SortOrder)
            .Select(a => a.FileId)
            .ToListAsync();
    }

    private async Task<List<string>> FeatureNamesAsync()
    {
        var page = await ReadJsonAsync(await editor.GetAsync($"/api/v1/features/?search={tag}&pageSize=100"));
        return [.. page.GetProperty("items").EnumerateArray()
            .Select(f => f.GetProperty("name").GetString())
            .Where(n => n is not null)
            .Select(n => n!)];
    }

    private async Task<Guid> CreateTripAsync(string title)
    {
        var response = await editor.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title,
            tripDate = "2026-08-08",
            caveIds = Array.Empty<Guid>(),
            participants = Array.Empty<object>(),
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>The caves a trip names, as the trip itself reports them to the caller who owns it.</summary>
    private async Task<List<Guid>> TripCavesAsync(Guid tripId)
    {
        var trip = await ReadJsonAsync(await editor.GetAsync($"/api/v1/trip-logs/{tripId}"));
        return [.. trip.GetProperty("caveIds").EnumerateArray().Select(x => x.GetGuid())];
    }

    private async Task<Guid> FeatureIdAsync(string name)
    {
        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Features.AsNoTracking().Where(f => f.Name == name).Select(f => f.Id).FirstAsync();
    }

    private async Task<Guid> MainEntranceIdAsync(Guid caveId)
    {
        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.CaveEntrances.AsNoTracking()
            .Where(e => e.CaveFeatureId == caveId)
            .OrderByDescending(e => e.IsMain)
            .Select(e => e.Id)
            .FirstAsync();
    }

    private async Task<Guid> CreateCaveWithEntranceAsync(
        string name,
        double lat,
        double lon,
        bool locationProtected = false,
        string visibility = "authenticated")
    {
        long caveTypeId;
        long entranceTypeId;
        using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Where(t => t.Code == "natural").Select(t => t.Id).FirstAsync();
        }

        var created = await editor.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "unknown",
            isShowCave = false,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var caveId = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement
            .GetProperty("id").GetGuid();

        var entrance = await editor.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            name = (string?)null,
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            positionQuality = "unknown",
        });
        entrance.StatusCode.ShouldBe(HttpStatusCode.Created, await entrance.Content.ReadAsStringAsync());
        return caveId;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    // ---------- pictures ----------

    /// <summary>A JPEG carrying a GPS fix, a capture time, an altitude, a bearing and a camera.</summary>
    private static byte[] At(double lat, double lon, DateTimeOffset takenAt)
    {
        using var image = new MagickImage(MagickColors.ForestGreen, 64, 48);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, lat >= 0 ? "N" : "S");
        exif.SetValue(ExifTag.GPSLatitude, ToDms(Math.Abs(lat)));
        exif.SetValue(ExifTag.GPSLongitudeRef, lon >= 0 ? "E" : "W");
        exif.SetValue(ExifTag.GPSLongitude, ToDms(Math.Abs(lon)));
        exif.SetValue(ExifTag.GPSAltitude, new Rational(812));
        exif.SetValue(ExifTag.GPSAltitudeRef, (byte)0);
        exif.SetValue(ExifTag.GPSImgDirection, new Rational(137));
        exif.SetValue(ExifTag.GPSImgDirectionRef, "T");
        exif.SetValue(ExifTag.GPSDOP, new Rational(1.4));
        StampCamera(exif, takenAt);
        image.SetProfile(exif);
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    /// <summary>A JPEG that says when it was taken and nothing about where.</summary>
    private static byte[] Plain(DateTimeOffset takenAt)
    {
        using var image = new MagickImage(MagickColors.SlateGray, 64, 48);
        var exif = new ExifProfile();
        StampCamera(exif, takenAt);
        image.SetProfile(exif);
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    private static void StampCamera(ExifProfile exif, DateTimeOffset takenAt)
    {
        exif.SetValue(ExifTag.Make, "SilexTest");
        exif.SetValue(ExifTag.Model, "Field 1");
        exif.SetValue(
            ExifTag.DateTimeOriginal,
            takenAt.UtcDateTime.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture));
        exif.SetValue(ExifTag.OffsetTimeOriginal, "+00:00");
    }

    /// <summary>Degrees → EXIF degrees/minutes/seconds rationals.</summary>
    private static Rational[] ToDms(double degrees)
    {
        var d = (uint)degrees;
        var minutesFull = (degrees - d) * 60d;
        var m = (uint)minutesFull;
        var seconds = (minutesFull - m) * 60d;
        return [new Rational(d), new Rational(m), new Rational(seconds)];
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        editor?.Dispose();
        other?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
