// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Importing a recording made on a phone underground so that it becomes a tracked trip's position
/// history.
/// </summary>
/// <remarks>
/// <para>
/// Every archive here is built by these tests, row by row, out of invented caves, invented places
/// and invented people. Nothing in this file came off anybody's device, and the fixture builder is
/// deliberately in the test rather than in a checked-in binary so that it stays that way.
/// </para>
/// <para>
/// Five things are on trial. The reading itself, because the values that decide everything
/// downstream — an epoch-millisecond clock reading and a metre-and-a-fraction depth — are exactly
/// the two a format-translating reader quietly corrupts. The translation from a physical marker to
/// a survey station, including the two ways it cannot be made: a place carrying no depth and a
/// place this installation has never heard of. The review surviving the archive being read again,
/// which is the whole reason there is no staging table. The undo, as one act. And the protection
/// matrix, which is the load-bearing one: an imported position has to be withheld by exactly the
/// rule that withholds a typed one, so the test holds both of them on one trip and reads them back
/// as two callers.
/// </para>
/// </remarks>
public sealed class SpeleolocTripImportTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient editor = null!;
    private HttpClient stranger = null!;
    private HttpClient viewer = null!;
    private HttpClient anonymous = null!;
    private string tag = null!;
    private long caveTypeId;
    private long placeTypeId;

    // The recording, and the markers it was made at. Identifiers are fixed inside one run so the
    // archive and the register can be seeded with the same values without passing them around.
    private readonly Guid recordingId = Guid.CreateVersion7();
    private readonly Guid deviceUserId = Guid.CreateVersion7();
    private readonly Guid placeAtFifty = Guid.CreateVersion7();
    private readonly Guid placeWithNoDepth = Guid.CreateVersion7();
    private readonly Guid placeNobodyHolds = Guid.CreateVersion7();
    private readonly Guid placeDeep = Guid.CreateVersion7();

    /// <summary>
    /// The four scans, in the order the party made them. The clock readings are past the
    /// thirty-two-bit horizon by more than a decade, which is the point of stating them here: a
    /// reader that let the width be decided for it turns every one of them into January 2038.
    /// </summary>
    private static readonly DateTimeOffset ScanOne = new(2026, 9, 10, 9, 5, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ScanTwo = new(2026, 9, 10, 9, 42, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ScanThree = new(2026, 9, 10, 10, 17, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ScanFour = new(2026, 9, 10, 11, 3, 0, TimeSpan.Zero);

    /// <summary>A depth with a fraction in it, which is the half of the reading that fails silently.</summary>
    private const double FractionalDepth = -87.4;

    /// <summary>
    /// The two ceilings, pinned far below their shipped figures so that the tests which prove them
    /// cost a megabyte and sixty rows rather than half a gigabyte and a hundred and fifty thousand.
    /// Nothing else in this file comes near either: the invented archive is tens of kilobytes and
    /// its recording is four scans.
    /// </summary>
    private const int ArchiveCeiling = 1024 * 1024;

    private const int ScanCeiling = 50;

    public SpeleolocTripImportTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
                ["Import:MaxArchiveDatabaseBytes"] =
                    ArchiveCeiling.ToString(CultureInfo.InvariantCulture),
                ["Import:MaxScanRows"] = ScanCeiling.ToString(CultureInfo.InvariantCulture),
            },
            // Workers off: the graph-extraction job would otherwise pick up the placeholder survey
            // file below, fail to parse it, and rewrite the very station rows these tests seed.
            JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"spl-ed-{tag}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"spl-str-{tag}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"spl-vw-{tag}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"spl-ed-{tag}@t.local");
        stranger = await AuthHelper.BearerClientAsync(factory, $"spl-str-{tag}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"spl-vw-{tag}@t.local");
        anonymous = factory.CreateClient();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        placeTypeId = await db.FeatureTypes.Where(t => t.Code == "cave_place").Select(t => t.Id).FirstAsync();
    }

    // ---------- the reading ----------

    /// <summary>
    /// What the archive says, read back through the preview: four scans in order, the clock
    /// readings intact to the minute, and the fractional depth intact to the tenth of a metre.
    ///
    /// <para>
    /// Both are regression assertions against a specific wrong answer rather than general
    /// sanity. A reader that takes SQLite's declared column affinity at face value clamps the
    /// timestamps to 2147483647 milliseconds and truncates −87.4 to −87, and neither failure looks
    /// like a failure: the first produces a plausible date and the second a plausible depth, which
    /// then chooses a plausible and wrong station.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_archive_is_read_back_with_its_clock_and_its_depths_intact()
    {
        var cave = await CreateCaveAsync(editor, locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        await SeedPlacesAsync(cave);
        var fileId = await UploadArchiveAsync();

        var recordings = await BodyAsync(await editor.GetAsync($"/api/v1/speleoloc-imports/{fileId}/recordings"));
        var recording = recordings.GetProperty("recordings").EnumerateArray().Single();
        recording.GetProperty("id").GetString().ShouldBe(recordingId.ToString());
        recording.GetProperty("pointCount").GetInt32().ShouldBe(4);
        // Documents are counted, never opened: the archive below files two under this recording.
        recording.GetProperty("documentCount").GetInt32().ShouldBe(2);
        recording.GetProperty("startedAt").GetDateTimeOffset().ShouldBe(ScanOne.AddMinutes(-5));

        var preview = await PreviewAsync(editor, fileId, Options(model));
        var points = preview.GetProperty("points").EnumerateArray().ToList();
        points.Count.ShouldBe(4);
        points[0].GetProperty("scannedAt").GetDateTimeOffset().ShouldBe(ScanOne);
        points[3].GetProperty("scannedAt").GetDateTimeOffset().ShouldBe(ScanFour);

        // The one place the register does not hold reports the depth the archive itself carries,
        // which is where the fraction has to survive.
        var unheld = points.Single(p => p.GetProperty("state").GetString() == "placeUnknown");
        unheld.GetProperty("placeDepthM").GetDouble().ShouldBe(FractionalDepth, 0.0001);
    }

    /// <summary>
    /// The translation, and the two ways it cannot be made.
    ///
    /// <para>
    /// The resolved scans are what make the refusals mean something: an assertion that a place
    /// with no depth proposes nothing passes equally well against an importer that proposes
    /// nothing at all, so the same recording carries two markers that do resolve, to the two
    /// stations their depths actually name.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_marker_becomes_the_station_at_its_depth_and_a_marker_without_one_becomes_nothing()
    {
        var cave = await CreateCaveAsync(editor, locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        await SeedPlacesAsync(cave);
        var fileId = await UploadArchiveAsync();
        // Somebody for the device account to be. What "select all" offers is answered against
        // everything the confirmation will ask, and one of those questions is who the scan is
        // about — so a recording whose account is nobody offers nothing, whatever resolved.
        var (_, cavers) = await CreateTripAsync(editor, "People", guests: 1);

        var preview = await PreviewAsync(editor, fileId, Options(model, caver: cavers[0]));
        var points = preview.GetProperty("points").EnumerateArray()
            .ToDictionary(p => p.GetProperty("pointId").GetString()!, p => p);

        // Positive half: the two markers whose depth the register holds resolve, nearest first.
        var shallow = points.Values.Single(p => p.GetProperty("placeId").GetString() == placeAtFifty.ToString());
        shallow.GetProperty("state").GetString().ShouldBe("proposed");
        shallow.GetProperty("candidates").EnumerateArray().First()
            .GetProperty("stationName").GetString().ShouldBe("cave.upper.2");

        var deep = points.Values.Single(p => p.GetProperty("placeId").GetString() == placeDeep.ToString());
        deep.GetProperty("state").GetString().ShouldBe("proposed");
        deep.GetProperty("candidates").EnumerateArray().First()
            .GetProperty("stationName").GetString().ShouldBe("cave.deep.3");

        // Negative halves. A marker the register holds with no depth proposes nothing — and is
        // still shown, with its own name, because the reviewer is the one who can settle it.
        var noDepth = points.Values.Single(p => p.GetProperty("placeId").GetString() == placeWithNoDepth.ToString());
        noDepth.GetProperty("state").GetString().ShouldBe("depthUnknown");
        noDepth.GetProperty("candidates").GetArrayLength().ShouldBe(0);

        // A marker nothing here answers to proposes nothing either, and says so as its own state
        // rather than as an empty candidate list that would read like a survey with no stations.
        var unknown = points.Values.Single(p => p.GetProperty("state").GetString() == "placeUnknown");
        unknown.GetProperty("placeId").ValueKind.ShouldBe(JsonValueKind.Null);
        unknown.GetProperty("candidates").GetArrayLength().ShouldBe(0);

        preview.GetProperty("proposedCount").GetInt32().ShouldBe(2);
        preview.GetProperty("unresolvedCount").GetInt32().ShouldBe(2);
        preview.GetProperty("modelUsable").GetBoolean().ShouldBeTrue();

        // Only the two that resolved are offered for selection: a scan that cannot be placed must
        // not ride in on "select all" and become a failure line nobody was warned about.
        var selectable = preview.GetProperty("selectablePointIds").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        selectable.Count.ShouldBe(2);
    }

    /// <summary>
    /// The review survives the archive being read again, which is the whole reason nothing is
    /// staged: a decision is keyed by the scan's own identifier, so it is still that scan's
    /// decision after the file has been re-opened and re-parsed twice.
    /// </summary>
    [Fact]
    public async Task A_saved_decision_is_still_attached_after_the_archive_is_read_again()
    {
        var cave = await CreateCaveAsync(editor, locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        await SeedPlacesAsync(cave);
        var fileId = await UploadArchiveAsync();
        var (trip, cavers) = await CreateTripAsync(editor, "Decisions", guests: 1);

        var first = await PreviewAsync(editor, fileId, Options(model, trip, cavers[0]));
        var undecided = first.GetProperty("points").EnumerateArray()
            .Single(p => p.GetProperty("state").GetString() == "depthUnknown")
            .GetProperty("pointId").GetString()!;

        // The reviewer settles the one nothing could be proposed for, by naming a station.
        var saved = await editor.PutAsJsonAsync($"/api/v1/speleoloc-imports/{fileId}/session", new
        {
            options = Options(model, trip, cavers[0]),
            decisions = new Dictionary<string, object>
            {
                [undecided] = new { stationName = "cave.upper.1" },
            },
        });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        // Read back through the session, and again through a fresh parse of the archive.
        var session = await BodyAsync(await editor.GetAsync($"/api/v1/speleoloc-imports/{fileId}/session"));
        session.GetProperty("positionsWithheld").GetBoolean().ShouldBeFalse();
        session.GetProperty("decisions").GetProperty(undecided)
            .GetProperty("stationName").GetString().ShouldBe("cave.upper.1");

        var second = await PreviewAsync(editor, fileId, Options(model, trip, cavers[0]));
        var row = second.GetProperty("points").EnumerateArray()
            .Single(p => p.GetProperty("pointId").GetString() == undecided);
        row.GetProperty("decision").GetProperty("stationName").GetString().ShouldBe("cave.upper.1");
        // And a scan that was unresolvable before is now one "select all" would take.
        second.GetProperty("selectablePointIds").EnumerateArray()
            .Select(e => e.GetString()).ShouldContain(undecided);

        // The confirmation reads the saved decision without being told it again, which is what a
        // review spread over nine pages depends on.
        var committed = await CommitAsync(editor, fileId, Options(model, trip, cavers[0]), AllPointsOf(second));
        committed.StatusCode.ShouldBe(HttpStatusCode.OK, await committed.Content.ReadAsStringAsync());
        var result = await BodyAsync(committed);
        result.GetProperty("createdEventCount").GetInt32().ShouldBe(3);

        var log = await BodyAsync(await editor.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events"));
        var stations = log.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("stationName").GetString()).ToList();
        stations.ShouldContain("cave.upper.1");
        stations.ShouldContain("cave.upper.2");
        stations.ShouldContain("cave.deep.3");

        // The review is spent: its decisions describe scans that are now positions.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.SpeleolocImportSessions.CountAsync(s => s.StoredFileId == fileId)).ShouldBe(0);
    }

    // ---------- protection ----------

    /// <summary>
    /// The load-bearing one, both halves in one place: an imported position and a typed one on the
    /// same trip in the same protected cave, read back by the person who may place it and by
    /// somebody who may not.
    ///
    /// <para>
    /// The positive half is what makes the negative half mean anything — an assertion that a
    /// reader learns no station passes just as well against a surface that shows nobody anything.
    /// And the two events together are the actual claim: they have to be indistinguishable, so
    /// that "this one came out of a phone" is not itself a fact about the party's movements that
    /// leaks past the withholding.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_imported_position_is_withheld_by_exactly_the_rule_that_withholds_a_typed_one()
    {
        var cave = await CreateCaveAsync(editor, locationProtected: true);
        var model = await SeedModelWithStationsAsync(cave);
        await SeedPlacesAsync(cave);
        var fileId = await UploadArchiveAsync();
        var (trip, cavers) = await CreateTripAsync(editor, "Protected", guests: 1, visibility: "authenticated");

        // A typed report first, through the live path, on armed tracking.
        (await ArmAsync(editor, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await editor.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", new
        {
            caverIds = new[] { cavers[0] },
            kind = "atStation",
            stationName = "cave.upper.2",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Then the import, onto the same trip.
        var options = Options(model, trip, cavers[0]);
        var preview = await PreviewAsync(editor, fileId, options);
        var committed = await CommitAsync(editor, fileId, options, ProposedPointsOf(preview));
        committed.StatusCode.ShouldBe(HttpStatusCode.OK, await committed.Content.ReadAsStringAsync());
        (await BodyAsync(committed)).GetProperty("createdEventCount").GetInt32().ShouldBe(2);

        // Positive half: the cave's owner may place it, so every position comes back — the typed
        // one and the imported ones alike, with no way to tell which was which.
        var mine = await BodyAsync(await editor.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events"));
        var minePositions = mine.GetProperty("items").EnumerateArray().ToList();
        minePositions.Count.ShouldBe(3);
        minePositions.ShouldAllBe(e => e.GetProperty("stationName").ValueKind == JsonValueKind.String);
        minePositions.ShouldAllBe(e => e.GetProperty("surveyModelId").ValueKind == JsonValueKind.String);

        // Negative half: a reader who may read the trip and not place the cave learns nothing
        // about where anybody was — from the imported rows exactly as from the typed one.
        var theirs = await BodyAsync(await viewer.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events"));
        var theirPositions = theirs.GetProperty("items").EnumerateArray().ToList();
        theirPositions.Count.ShouldBe(3);
        theirPositions.ShouldAllBe(e => e.GetProperty("stationName").ValueKind == JsonValueKind.Null);
        theirPositions.ShouldAllBe(e => e.GetProperty("surveyModelId").ValueKind == JsonValueKind.Null);
        theirPositions.ShouldAllBe(e => e.GetProperty("depthEnteredM").ValueKind == JsonValueKind.Null);

        var state = await BodyAsync(await viewer.GetAsync($"/api/v1/trip-logs/{trip}/tracking"));
        state.GetProperty("positionsWithheld").GetBoolean().ShouldBeTrue();
        state.GetProperty("participants").EnumerateArray().Single()
            .GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);

        // The rows really are of two different origins, and really do carry the same anchor. The
        // provenance column exists and does its job; nothing above could see it, which is the
        // property under test.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var rows = await db.TripPositionEvents.AsNoTracking().Where(e => e.TripLogId == trip).ToListAsync();
        rows.Count(e => e.Source == TripPositionEventSource.SpeleolocArchive).ShouldBe(2);
        rows.Count(e => e.Source == TripPositionEventSource.Reported).ShouldBe(1);
        rows.ShouldAllBe(e => e.CaveFeatureId == cave);
        rows.Select(e => e.CaveFeatureId).Distinct().Count().ShouldBe(1);
    }

    /// <summary>
    /// A caller who may read a cave and not place it cannot import into its survey — and the same
    /// caller, on a cave they may place, can. One refusal is a refusal; the pair is the rule.
    /// </summary>
    [Fact]
    public async Task A_caller_who_may_not_place_the_cave_is_refused_and_the_same_caller_is_allowed_on_their_own()
    {
        // Somebody else's cave, readable and position-guarded.
        var guarded = await CreateCaveAsync(stranger, locationProtected: true);
        var guardedModel = await SeedModelWithStationsAsync(guarded, asOwner: stranger);
        var fileId = await UploadArchiveAsync();

        var refused = await CommitAsync(
            editor, fileId, Options(guardedModel, createTrip: true), [Guid.CreateVersion7().ToString()]);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync());
        CodeOf(await refused.Content.ReadAsStringAsync()).ShouldBe("speleoloc_import.model_unavailable");

        // The preview is closed to them too, and says so as a state on every row rather than as an
        // empty list: a survey nobody may place must not read as a survey with nothing in it.
        var blind = await PreviewAsync(editor, fileId, Options(guardedModel, createTrip: true));
        blind.GetProperty("modelUsable").GetBoolean().ShouldBeFalse();
        blind.GetProperty("points").EnumerateArray()
            .ShouldAllBe(p => p.GetProperty("state").GetString() == "modelUnavailable");
        blind.GetProperty("selectablePointIds").GetArrayLength().ShouldBe(0);

        // The same caller, the same archive, a cave of their own: allowed.
        var own = await CreateCaveAsync(editor, locationProtected: true);
        var ownModel = await SeedModelWithStationsAsync(own);
        await SeedPlacesAsync(own);
        var mine = await PreviewAsync(editor, fileId, Options(ownModel, createTrip: true));
        mine.GetProperty("modelUsable").GetBoolean().ShouldBeTrue();
        mine.GetProperty("proposedCount").GetInt32().ShouldBe(2);
    }

    /// <summary>
    /// No account, no answer; an account that may not record trips is told why rather than told
    /// about somebody's archive.
    /// </summary>
    [Fact]
    public async Task Without_an_account_nothing_answers_and_without_the_right_nothing_is_created()
    {
        var cave = await CreateCaveAsync(editor, locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        await SeedPlacesAsync(cave);
        var fileId = await UploadArchiveAsync();

        (await anonymous.GetAsync($"/api/v1/speleoloc-imports/{fileId}/recordings"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/api/v1/speleoloc-imports/{fileId}/session"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync($"/api/v1/speleoloc-imports/{fileId}/preview",
            new { options = Options(model, createTrip: true) }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // A viewer may not record trips, and the refusal comes before the archive is fetched —
        // on every route of the review, not only the one that would create something.
        var refused = await viewer.PostAsJsonAsync($"/api/v1/speleoloc-imports/{fileId}/preview",
            new { options = Options(model, createTrip: true) });
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await refused.Content.ReadAsStringAsync());
        (await viewer.GetAsync($"/api/v1/speleoloc-imports/{fileId}/recordings"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.GetAsync($"/api/v1/speleoloc-imports/{fileId}/session"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // An upload nobody was ever shown answers as one that does not exist, to a caller who
        // passes every gate before it.
        (await editor.GetAsync($"/api/v1/speleoloc-imports/{Guid.CreateVersion7()}/recordings"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---------- what is never inferred ----------

    /// <summary>
    /// A device account is not a person. Unmapped, every scan it made is refused by name; mapped,
    /// they land — and the mapping is the only thing that changed between the two halves.
    /// </summary>
    [Fact]
    public async Task A_device_account_becomes_a_person_only_because_somebody_said_so()
    {
        var cave = await CreateCaveAsync(editor, locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        await SeedPlacesAsync(cave);
        var fileId = await UploadArchiveAsync();
        var (trip, cavers) = await CreateTripAsync(editor, "Identity", guests: 1);

        var unmappedOptions = Options(model, trip, caver: null);
        var preview = await PreviewAsync(editor, fileId, unmappedOptions);
        preview.GetProperty("unmappedDeviceUsers").EnumerateArray()
            .Select(e => e.GetString()).ShouldContain(deviceUserId.ToString());
        preview.GetProperty("points").EnumerateArray()
            .ShouldAllBe(p => p.GetProperty("caverId").ValueKind == JsonValueKind.Null);
        // Not offered either: an account nobody has said is anybody costs every scan it made, and
        // the dry run is the step whose purpose is to say so first.
        preview.GetProperty("selectablePointIds").GetArrayLength().ShouldBe(0);

        // Named outright anyway — what the dry run offers is not what enforces this — and the
        // confirmation refuses each of them, and so the whole act.
        var named = preview.GetProperty("points").EnumerateArray()
            .Where(p => p.GetProperty("state").GetString() == "proposed")
            .Select(p => p.GetProperty("pointId").GetString()!)
            .ToList();
        named.Count.ShouldBe(2);
        var refused = await CommitAsync(editor, fileId, unmappedOptions, named);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        CodeOf(await refused.Content.ReadAsStringAsync()).ShouldBe("speleoloc_import.nothing_created");

        // Say who it is, and the same scans land.
        var mapped = Options(model, trip, cavers[0]);
        var second = await PreviewAsync(editor, fileId, mapped);
        second.GetProperty("unmappedDeviceUsers").GetArrayLength().ShouldBe(0);
        var committed = await CommitAsync(editor, fileId, mapped, ProposedPointsOf(second));
        committed.StatusCode.ShouldBe(HttpStatusCode.OK, await committed.Content.ReadAsStringAsync());
        (await BodyAsync(committed)).GetProperty("createdEventCount").GetInt32().ShouldBe(2);
    }

    // ---------- the undo ----------

    /// <summary>
    /// The batch reverts as one unit: the positions go, a trip the confirmation created goes with
    /// them, and a trip that already existed does not.
    /// </summary>
    [Fact]
    public async Task An_undo_takes_back_the_positions_and_only_a_trip_the_import_itself_created()
    {
        var cave = await CreateCaveAsync(editor, locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        await SeedPlacesAsync(cave);
        var fileId = await UploadArchiveAsync();
        var (existing, cavers) = await CreateTripAsync(editor, "Kept", guests: 1);

        // Onto a trip that already existed.
        var ontoExisting = Options(model, existing, cavers[0]);
        var preview = await PreviewAsync(editor, fileId, ontoExisting);
        var firstResponse = await CommitAsync(editor, fileId, ontoExisting, ProposedPointsOf(preview));
        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await firstResponse.Content.ReadAsStringAsync());
        var firstCommit = await BodyAsync(firstResponse);
        firstCommit.GetProperty("createdTrip").GetBoolean().ShouldBeFalse();
        var firstBatch = firstCommit.GetProperty("batchId").GetGuid();

        // Into one the confirmation creates. The reviewer maps the device account to the same
        // person, who is put on the created trip's roster because nothing else would.
        var creating = Options(model, trip: null, caver: cavers[0], createTrip: true);
        var secondPreview = await PreviewAsync(editor, fileId, creating);
        var secondResponse = await CommitAsync(editor, fileId, creating, ProposedPointsOf(secondPreview));
        secondResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await secondResponse.Content.ReadAsStringAsync());
        var secondCommit = await BodyAsync(secondResponse);
        secondCommit.GetProperty("createdTrip").GetBoolean().ShouldBeTrue();
        var createdTrip = secondCommit.GetProperty("tripLogId").GetGuid();
        var secondBatch = secondCommit.GetProperty("batchId").GetGuid();
        createdTrip.ShouldNotBe(existing);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.TripPositionEvents.CountAsync(e => e.TripLogId == existing)).ShouldBe(2);
            (await db.TripPositionEvents.CountAsync(e => e.TripLogId == createdTrip)).ShouldBe(2);
            // A trip this confirmation created is created tracked and closed, against the model the
            // reviewer chose — there is nobody else whose arrangement that could be overwriting.
            (await db.TripTrackings.SingleAsync(t => t.TripLogId == createdTrip)).SurveyModelId.ShouldBe(model);
            // And a trip that already existed is left exactly as it was. Writing a configuration
            // for somebody else's trip is an operational statement about it — tracked, closed,
            // against this model, anchored to this cave — and it is one the undo below cannot take
            // back: nothing on a batch line points at a tracking row, the lifecycle has no
            // transition back to off, and no route deletes one. The positions do not need it: each
            // carries its own model and its own cave anchor.
            (await db.TripTrackings.CountAsync(t => t.TripLogId == existing)).ShouldBe(0);
        }

        var firstRevert = await editor.PostAsync($"/api/v1/import-batches/{firstBatch}/revert", null);
        firstRevert.StatusCode.ShouldBe(HttpStatusCode.OK, await firstRevert.Content.ReadAsStringAsync());
        var secondRevert = await editor.PostAsync($"/api/v1/import-batches/{secondBatch}/revert", null);
        secondRevert.StatusCode.ShouldBe(HttpStatusCode.OK, await secondRevert.Content.ReadAsStringAsync());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.TripPositionEvents.CountAsync(e => e.TripLogId == existing)).ShouldBe(0);
            // The trip the import wrote onto is still there — undoing an import must not delete
            // somebody's trip because positions were once recorded onto it.
            (await db.TripLogs.CountAsync(t => t.Id == existing)).ShouldBe(1);
            // The trip the import created is not.
            (await db.TripLogs.CountAsync(t => t.Id == createdTrip)).ShouldBe(0);
            (await db.TripPositionEvents.CountAsync(e => e.TripLogId == createdTrip)).ShouldBe(0);
            // Nor its tracking row: it is keyed by the trip and goes with it, which is why the
            // import is allowed to write one there and nowhere else. Nothing the confirmation made
            // outlives the undo.
            (await db.TripTrackings.CountAsync(t => t.TripLogId == createdTrip)).ShouldBe(0);
        }

        // Undoing twice is refused rather than repeated.
        var again = await editor.PostAsync($"/api/v1/import-batches/{firstBatch}/revert", null);
        again.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        CodeOf(await again.Content.ReadAsStringAsync()).ShouldBe("import_batch.already_reverted");
    }

    /// <summary>
    /// A person named for one scan is a person on the trip that scan creates.
    ///
    /// <para>
    /// Two questions with one answer: who a created trip's roster is, and whose position each row
    /// is. They were asked in opposite orders — the roster took the recording-wide mapping first
    /// and the row took the override first — so overriding a scan put somebody on it whom the
    /// roster had been built without, and the confirmation then refused that scan for not being on
    /// a roster it had just written. Override every scan and the whole act failed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_person_named_for_one_scan_is_on_the_roster_of_the_trip_that_scan_creates()
    {
        var cave = await CreateCaveAsync(editor, locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        await SeedPlacesAsync(cave);
        var fileId = await UploadArchiveAsync();
        var (_, people) = await CreateTripAsync(editor, "People", guests: 2);

        var options = Options(model, caver: people[0], createTrip: true);
        var preview = await PreviewAsync(editor, fileId, options);
        var points = ProposedPointsOf(preview);
        points.Count.ShouldBe(2);

        // The reviewer disagrees about one of them: that scan was somebody else.
        var committed = await editor.PostAsJsonAsync($"/api/v1/speleoloc-imports/{fileId}/commit", new
        {
            options,
            pointIds = points,
            decisions = new Dictionary<string, object> { [points[0]] = new { caverId = people[1] } },
        });
        committed.StatusCode.ShouldBe(HttpStatusCode.OK, await committed.Content.ReadAsStringAsync());
        var result = await BodyAsync(committed);
        result.GetProperty("createdEventCount").GetInt32().ShouldBe(2);
        result.GetProperty("failures").GetArrayLength().ShouldBe(0);

        var trip = result.GetProperty("tripLogId").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var roster = await db.TripLogParticipants.Where(p => p.TripLogId == trip)
            .Select(p => p.CaverId).ToListAsync();
        roster.ShouldContain(people[0]);
        roster.ShouldContain(people[1]);
        var recorded = await db.TripPositionEvents.Where(e => e.TripLogId == trip)
            .Select(e => e.CaverId).ToListAsync();
        recorded.ShouldContain(people[0]);
        recorded.ShouldContain(people[1]);
    }

    /// <summary>
    /// A person the trip does not have is named by the dry run, not discovered by the confirmation.
    ///
    /// <para>
    /// The mapping can be complete — every device account is somebody — and every scan still be
    /// refused, because putting somebody on a trip is a statement about who went and this import
    /// will not make it. Offering those scans for selection and then refusing all of them, which is
    /// a whole confirmation lost, is the dry run failing at the one thing it is for.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_person_the_trip_does_not_have_is_named_by_the_dry_run_rather_than_the_confirmation()
    {
        var cave = await CreateCaveAsync(editor, locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        await SeedPlacesAsync(cave);
        var fileId = await UploadArchiveAsync();
        var (target, insiders) = await CreateTripAsync(editor, "Roster", guests: 1);
        var (_, outsiders) = await CreateTripAsync(editor, "Elsewhere", guests: 1);

        var off = await PreviewAsync(editor, fileId, Options(model, target, outsiders[0]));
        off.GetProperty("unmappedDeviceUsers").GetArrayLength().ShouldBe(0);
        off.GetProperty("caversNotOnRoster").EnumerateArray()
            .Select(e => e.GetGuid()).ShouldContain(outsiders[0]);
        off.GetProperty("selectablePointIds").GetArrayLength().ShouldBe(0);
        // Still shown, and still resolved — the reviewer has to be able to see what they would be
        // importing in order to decide who these people are.
        off.GetProperty("proposedCount").GetInt32().ShouldBe(2);

        // Positive half: the same archive, the same trip, mapped to somebody who is on it.
        var on = await PreviewAsync(editor, fileId, Options(model, target, insiders[0]));
        on.GetProperty("caversNotOnRoster").GetArrayLength().ShouldBe(0);
        on.GetProperty("selectablePointIds").GetArrayLength().ShouldBe(2);
        var committed = await CommitAsync(editor, fileId, Options(model, target, insiders[0]), ProposedPointsOf(on));
        committed.StatusCode.ShouldBe(HttpStatusCode.OK, await committed.Content.ReadAsStringAsync());
        (await BodyAsync(committed)).GetProperty("createdEventCount").GetInt32().ShouldBe(2);
    }

    /// <summary>
    /// The dry run is guarded against the trip it was handed, and borrows no other cave's station
    /// vocabulary while reading it.
    ///
    /// <para>
    /// What the dry run reads off the named trip is its tracking configuration, and the two fields
    /// it uses — the reference station and the depth filter — are exactly the two the live tracking
    /// surface withholds from a caller who cannot place that configuration's cave. Reading them
    /// unguarded and applying them to another cave's survey turns the shape of the answer into a
    /// question about somebody else's: whether their reference station name exists in your model,
    /// and then, one prefix at a time, what their filter says. The fix is that a configuration is
    /// honoured only where its own cave anchor is the cave the scans are being placed in — which is
    /// also the only case in which it means anything, a datum being an altitude in one survey.
    /// </para>
    /// <para>
    /// The guard itself is the other half: the confirmation decides the trip with the trip-write
    /// rule, and a dry run that answered where the confirmation refuses is a dry run of something
    /// else. Note what it is not: in this installation an account that may record trips may write
    /// any trip it can see, so the guard separates a trip nobody has from one somebody does, not
    /// one editor from another. The cave gate below is what closes the disclosure.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_dry_run_is_guarded_against_its_trip_and_borrows_no_other_caves_vocabulary()
    {
        var cave = await CreateCaveAsync(editor, locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        await SeedPlacesAsync(cave);
        var fileId = await UploadArchiveAsync();

        // A trip nobody has. The dry run used to read a tracking configuration for it, find none,
        // and answer two hundred with a full set of proposals — then the confirmation answered 404.
        var nobodys = Guid.CreateVersion7();
        var refused = await editor.PostAsJsonAsync(
            $"/api/v1/speleoloc-imports/{fileId}/preview",
            new { options = Options(model, nobodys), page = 1, pageSize = 100 });
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound, await refused.Content.ReadAsStringAsync());
        CodeOf(await refused.Content.ReadAsStringAsync()).ShouldBe("trip_log.not_found");
        var alsoRefused = await CommitAsync(editor, fileId, Options(model, nobodys), [Guid.CreateVersion7().ToString()]);
        alsoRefused.StatusCode.ShouldBe(HttpStatusCode.NotFound, await alsoRefused.Content.ReadAsStringAsync());
        CodeOf(await alsoRefused.Content.ReadAsStringAsync()).ShouldBe("trip_log.not_found");

        // Positive half: the caller's own trip answers, with the scans resolved.
        var (mine, cavers) = await CreateTripAsync(editor, "Mine", guests: 1);
        var ok = await PreviewAsync(editor, fileId, Options(model, mine, cavers[0]));
        ok.GetProperty("proposedCount").GetInt32().ShouldBe(2);

        // The filter does bite, when it belongs to the cave being imported into. Armed against this
        // model with a prefix none of its stations carry, every scan resolves to nothing — which is
        // the control that makes the next assertion mean something rather than passing against a
        // resolver that ignores filters altogether.
        (await ArmAsync(editor, mine, model, depthFilter: ["nowhere.at.all"]))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var filtered = await PreviewAsync(editor, fileId, Options(model, mine, cavers[0]));
        filtered.GetProperty("proposedCount").GetInt32().ShouldBe(0);

        // The same filter, now belonging to a different cave's survey, steers nothing here.
        var other = await CreateCaveAsync(editor, locationProtected: false);
        var otherModel = await SeedModelWithStationsAsync(other);
        (await ArmAsync(editor, mine, otherModel, depthFilter: ["nowhere.at.all"]))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var unfiltered = await PreviewAsync(editor, fileId, Options(model, mine, cavers[0]));
        unfiltered.GetProperty("proposedCount").GetInt32().ShouldBe(2);
    }

    // ---------- refusals about the archive itself ----------

    /// <summary>
    /// An entry that is not a database is refused by name, whether it states a size past the
    /// ceiling or fits inside it.
    ///
    /// <para>
    /// Nothing is staged between the upload and the import, by design, so the database is unpacked
    /// again on every listing, every preview and the confirmation, and nothing serialises those.
    /// That makes the ceiling an amplification factor rather than a storage limit: whatever it
    /// permits is what one uploaded half-megabyte of zeros can have written to the scratch
    /// filesystem, repeatedly, on demand. Two things bound it — the ceiling itself, lowered to what
    /// a phone's database actually is, and a check of the format's own first sixteen bytes before
    /// any of the entry is written.
    /// </para>
    /// <para>
    /// What this test cannot see is the second of those: the refusal below reads the same whether it
    /// cost sixteen bytes or a ceiling's worth of writes, because the difference is in resource use
    /// and not in the answer. What it does hold is that both shapes are refused by name and that a
    /// real archive still opens — so the check cannot be removed without something going red, and
    /// cannot start refusing real archives either.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_entry_that_is_not_a_database_is_refused_whether_or_not_it_states_a_size()
    {
        // Past the ceiling by its own stated length: refused off the zip directory, never copied.
        var oversized = await UploadAsync(
            "big.zip", ZipOf(("speleo_loc.sqlite", new byte[ArchiveCeiling + 65536])));
        var tooLarge = await editor.GetAsync($"/api/v1/speleoloc-imports/{oversized}/recordings");
        tooLarge.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await tooLarge.Content.ReadAsStringAsync());
        CodeOf(await tooLarge.Content.ReadAsStringAsync()).ShouldBe("speleoloc_import.archive_too_large");

        // Inside the ceiling and not a database: this is the shape that used to be copied whole
        // before anything looked at it, once per request, with no concurrency bound in front.
        var zeros = await UploadAsync(
            "zeros.zip", ZipOf(("speleo_loc.sqlite", new byte[ArchiveCeiling - 65536])));
        var refused = await editor.GetAsync($"/api/v1/speleoloc-imports/{zeros}/recordings");
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        CodeOf(await refused.Content.ReadAsStringAsync()).ShouldBe("speleoloc_import.archive_unreadable");

        // Positive half: an entry that does say SQLite is unpacked and read as before.
        var real = await UploadArchiveAsync();
        var listed = await BodyAsync(await editor.GetAsync($"/api/v1/speleoloc-imports/{real}/recordings"));
        listed.GetProperty("recordings").GetArrayLength().ShouldBe(1);
    }

    /// <summary>
    /// A database whose tables are all present and whose columns are not is refused by name.
    ///
    /// <para>
    /// Opening the file proves it is SQLite and that five tables exist under the names this import
    /// checks; it proves nothing about their columns. An export written by an older device schema
    /// fails on the first statement instead, which is a read fault in the middle of a scan — and
    /// without a guard around the reads it leaves the slice as an unhandled exception with no code
    /// on it rather than as the refusal the route already knows how to answer with.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_database_missing_a_column_is_refused_by_name_rather_than_faulting()
    {
        var older = await UploadAsync("older.zip", BuildArchive(withLogColumn: false));
        var refused = await editor.GetAsync($"/api/v1/speleoloc-imports/{older}/recordings");
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        CodeOf(await refused.Content.ReadAsStringAsync()).ShouldBe("speleoloc_import.archive_unreadable");

        // Positive half: the same builder with the column present reads, so the refusal above is
        // about the column and not about the fixture.
        var complete = await UploadArchiveAsync();
        var listed = await BodyAsync(await editor.GetAsync($"/api/v1/speleoloc-imports/{complete}/recordings"));
        listed.GetProperty("recordings").GetArrayLength().ShouldBe(1);
    }

    /// <summary>
    /// A recording holding more scans than one review reads is refused rather than cut off.
    ///
    /// <para>
    /// The reader materialises a recording whole — that is what a review of one recording is, and
    /// the paging happens after — so an unbounded read makes the size of one request the archive's
    /// choice. Truncating instead would be worse than refusing: a list cut off at a ceiling reads
    /// exactly like a complete one, and the whole point of the step is that somebody saw every scan.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_recording_larger_than_one_review_reads_is_refused_rather_than_cut_off()
    {
        var cave = await CreateCaveAsync(editor, locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        await SeedPlacesAsync(cave);
        var crowded = Guid.CreateVersion7();
        var fileId = await UploadAsync("crowded.zip", BuildArchive(crowded, ScanCeiling + 10));

        // Positive half, and it is in the same archive: the recording that fits is read in full.
        var fits = await PreviewAsync(editor, fileId, Options(model));
        fits.GetProperty("totalItems").GetInt32().ShouldBe(4);

        var refused = await editor.PostAsJsonAsync(
            $"/api/v1/speleoloc-imports/{fileId}/preview",
            new { options = Options(model, recording: crowded), page = 1, pageSize = 100 });
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        CodeOf(await refused.Content.ReadAsStringAsync()).ShouldBe("speleoloc_import.recording_too_large");
    }

    /// <summary>
    /// A collection the body says is null is a refusal, not a fault.
    ///
    /// <para>
    /// A property initialiser is not a null check: the serialiser assigns whatever the body said, so
    /// <c>"cavers": null</c> leaves the property null however it was initialised, and a validation
    /// rule that reached inside it faulted — a 500 answering a malformed request, which is the one
    /// answer validation exists to prevent.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_null_collection_in_the_body_is_refused_rather_than_faulting()
    {
        var cave = await CreateCaveAsync(editor, locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        var fileId = await UploadArchiveAsync();

        var nullCavers = await editor.PostAsJsonAsync(
            $"/api/v1/speleoloc-imports/{fileId}/preview",
            Json($$$"""
                {"options":{"tripUuid":"{{{recordingId}}}","createTrip":true,
                 "surveyModelId":"{{{model}}}","cavers":null,"candidateCount":5}}
                """));
        nullCavers.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await nullCavers.Content.ReadAsStringAsync());

        // A hole in a saved review is tolerated on the way in and dropped, rather than refused: a
        // review is durable and one bad entry must not make the archive unreviewable. What it may
        // not do is fault.
        var hole = Guid.CreateVersion7();
        var nullDecision = await editor.PutAsJsonAsync(
            $"/api/v1/speleoloc-imports/{fileId}/session",
            Json($$$"""
                {"options":{"tripUuid":"{{{recordingId}}}","createTrip":true,
                 "surveyModelId":"{{{model}}}","candidateCount":5},
                 "decisions":{"{{{hole}}}":null}}
                """));
        nullDecision.StatusCode.ShouldBe(HttpStatusCode.OK, await nullDecision.Content.ReadAsStringAsync());
        var session = await BodyAsync(await editor.GetAsync($"/api/v1/speleoloc-imports/{fileId}/session"));
        session.GetProperty("decisions").TryGetProperty(hole.ToString(), out _).ShouldBeFalse();

        // Positive half: the same routes with the collections present are accepted.
        var ok = await PreviewAsync(editor, fileId, Options(model, createTrip: true));
        ok.GetProperty("modelUsable").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task A_file_that_is_not_a_device_archive_is_refused_by_name()
    {
        var notAnArchive = await UploadAsync("notes.zip", ZipOf(("readme.txt", "nothing to see"u8.ToArray())));
        var refused = await editor.GetAsync($"/api/v1/speleoloc-imports/{notAnArchive}/recordings");
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        CodeOf(await refused.Content.ReadAsStringAsync()).ShouldBe("speleoloc_import.archive_unreadable");

        var plainText = await UploadAsync("plain.zip", "not a zip at all"u8.ToArray());
        var alsoRefused = await editor.GetAsync($"/api/v1/speleoloc-imports/{plainText}/recordings");
        alsoRefused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        CodeOf(await alsoRefused.Content.ReadAsStringAsync()).ShouldBe("speleoloc_import.archive_unreadable");
    }

    // ---------- the invented archive ----------

    /// <summary>
    /// One export archive, built here out of nothing: four tables of the device's schema, one
    /// recording, four scans at four markers, and two decoy entries beside the database — the
    /// manifest an export really carries, and the credentials file some builds do. Neither is ever
    /// opened by the importer, and the recordings listing above proves it by answering at all.
    /// </summary>
    /// <param name="crowdedRecording">
    /// When given, a second recording is written beside the first holding
    /// <paramref name="crowdedScans"/> scans — a recording past what one review reads.
    /// </param>
    /// <param name="withLogColumn">
    /// False writes <c>cave_trips</c> without its <c>log</c> column, which is what an export from
    /// an older device schema looks like: all five tables present under the names the import
    /// checks, and one of them missing a column the import reads.
    /// </param>
    private byte[] BuildArchive(Guid? crowdedRecording = null, int crowdedScans = 0, bool withLogColumn = true)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"silexgis-speleoloc-fixture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "speleo_loc.sqlite");
        try
        {
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString()))
            {
                connection.Open();
                Execute(connection, """
                    create table caves (
                        uuid blob primary key not null, title text not null, description text,
                        surface_area_uuid blob, cave_local_index text,
                        created_at integer, updated_at integer, deleted_at integer,
                        created_by_user_uuid blob, last_modified_by_user_uuid blob);
                    create table cave_places (
                        uuid blob primary key not null, title text not null, description text,
                        cave_uuid blob not null, place_code_identifier text, qr_code_resource_identifier text,
                        cave_area_uuid blob, latitude numeric(10,8), longitude numeric(11,8),
                        altitude numeric(7,2), depth_in_cave numeric(7,2),
                        is_entrance integer not null default 0, is_main_entrance integer not null default 0,
                        created_at integer, updated_at integer, deleted_at integer,
                        created_by_user_uuid blob, last_modified_by_user_uuid blob);
                    create table cave_trip_points (
                        uuid blob primary key not null, cave_trip_uuid blob not null, cave_place_uuid blob,
                        scanned_at integer not null, notes text,
                        created_at integer, updated_at integer, deleted_at integer,
                        created_by_user_uuid blob, last_modified_by_user_uuid blob);
                    create table documentation_files (
                        uuid blob primary key not null, title text not null, file_name text not null,
                        file_size integer not null, file_hash text, file_type text not null,
                        created_at integer, updated_at integer, deleted_at integer);
                    create table documentation_files_to_cave_trips (
                        uuid blob primary key not null, documentation_file_uuid blob not null,
                        cave_trip_uuid blob not null, created_at integer, deleted_at integer);
                    """);

                Execute(connection, $"""
                    create table cave_trips (
                        uuid blob primary key not null, cave_uuid blob not null, title text not null,
                        description text, trip_started_at integer not null, trip_ended_at integer,
                        {(withLogColumn ? "log text," : string.Empty)}
                        created_at integer, updated_at integer, deleted_at integer,
                        created_by_user_uuid blob, last_modified_by_user_uuid blob, device_uuid blob);
                    """);

                var caveId = Guid.CreateVersion7();
                Insert(connection,
                    "insert into caves (uuid, title, cave_local_index) values (@u, @t, @i)",
                    ("@u", Bytes(caveId)), ("@t", "Pestera inventata 1"), ("@i", "INV-1"));

                Place(connection, placeAtFifty, caveId, "Marcaj 1", -50.0);
                Place(connection, placeWithNoDepth, caveId, "Marcaj 2", null);
                Place(connection, placeNobodyHolds, caveId, "Marcaj 3", FractionalDepth);
                Place(connection, placeDeep, caveId, "Marcaj 4", -120.0);

                Recording(connection, recordingId, caveId, "Tura inventata", withLogColumn);

                Scan(connection, recordingId, placeAtFifty, ScanOne, "la baza puțului");
                Scan(connection, recordingId, placeWithNoDepth, ScanTwo, null);
                Scan(connection, recordingId, placeNobodyHolds, ScanThree, null);
                Scan(connection, recordingId, placeDeep, ScanFour, null);

                if (crowdedRecording is { } crowded)
                {
                    Recording(connection, crowded, caveId, "Tura aglomerata", withLogColumn);
                    for (var i = 0; i < crowdedScans; i++)
                    {
                        Scan(connection, crowded, placeAtFifty, ScanOne.AddSeconds(i), null);
                    }
                }

                // A scan somebody deleted on the phone, which must not be imported and must not be
                // counted: a soft delete the reader forgot is the one kind of wrongness nobody
                // reviewing the list could spot.
                var deleted = Guid.CreateVersion7();
                Insert(
                    connection,
                    "insert into cave_trip_points (uuid, cave_trip_uuid, cave_place_uuid, scanned_at, "
                    + "deleted_at, created_by_user_uuid) values (@u, @t, @p, @s, @d, @c)",
                    ("@u", Bytes(deleted)),
                    ("@t", Bytes(recordingId)),
                    ("@p", Bytes(placeDeep)),
                    ("@s", ScanFour.AddMinutes(1).ToUnixTimeMilliseconds()),
                    ("@d", ScanFour.AddMinutes(30).ToUnixTimeMilliseconds()),
                    ("@c", Bytes(deviceUserId)));

                for (var i = 0; i < 2; i++)
                {
                    var documentId = Guid.CreateVersion7();
                    Insert(connection,
                        "insert into documentation_files (uuid, title, file_name, file_size, file_type) "
                        + "values (@u, @t, @n, @s, @y)",
                        ("@u", Bytes(documentId)), ("@t", $"Schita {i}"), ("@n", $"schita-{i}.jpg"),
                        ("@s", 1024L), ("@y", "image/jpeg"));
                    Insert(connection,
                        "insert into documentation_files_to_cave_trips (uuid, documentation_file_uuid, "
                        + "cave_trip_uuid) values (@u, @d, @t)",
                        ("@u", Bytes(Guid.CreateVersion7())), ("@d", Bytes(documentId)), ("@t", Bytes(recordingId)));
                }
            }

            SqliteConnection.ClearAllPools();
            return ZipOf(
                ("speleo_loc.sqlite", File.ReadAllBytes(databasePath)),
                ("manifest.json", Encoding.UTF8.GetBytes(
                    """{"version":1,"exportedAt":1789000000000,"isDiff":false}""")),
                // Never opened. Present because a real archive from such a build has it, and a
                // fixture without it would not be exercising the reader's refusal to look.
                ("ftp_credentials.json", Encoding.UTF8.GetBytes("""{"host":"invented","password":"invented"}""")),
                ("media/schita-0.jpg", [0xFF, 0xD8, 0xFF, 0xDB]));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        }
    }

    private void Place(SqliteConnection connection, Guid id, Guid caveId, string title, double? depth) =>
        Insert(connection,
            "insert into cave_places (uuid, title, cave_uuid, place_code_identifier, "
            + "qr_code_resource_identifier, depth_in_cave, created_by_user_uuid) "
            + "values (@u, @t, @c, @p, @q, @d, @b)",
            ("@u", Bytes(id)),
            ("@t", title),
            ("@c", Bytes(caveId)),
            ("@p", $"P{title[^1]}"),
            ("@q", $"Q{title[^1]}"),
            ("@d", depth is null ? DBNull.Value : depth.Value),
            ("@b", Bytes(deviceUserId)));

    private void Recording(SqliteConnection connection, Guid id, Guid caveId, string title, bool withLog) =>
        Insert(connection,
            "insert into cave_trips (uuid, cave_uuid, title, trip_started_at, trip_ended_at, "
            + (withLog ? "log, " : string.Empty)
            + "created_by_user_uuid) values (@u, @c, @t, @s, @e, "
            + (withLog ? "@l, " : string.Empty)
            + "@d)",
            withLog
                ? [
                    ("@u", Bytes(id)),
                    ("@c", Bytes(caveId)),
                    ("@t", (object)title),
                    ("@s", ScanOne.AddMinutes(-5).ToUnixTimeMilliseconds()),
                    ("@e", ScanFour.AddMinutes(20).ToUnixTimeMilliseconds()),
                    ("@l", "jurnal inventat"),
                    ("@d", Bytes(deviceUserId)),
                ]
                : [
                    ("@u", Bytes(id)),
                    ("@c", Bytes(caveId)),
                    ("@t", (object)title),
                    ("@s", ScanOne.AddMinutes(-5).ToUnixTimeMilliseconds()),
                    ("@e", ScanFour.AddMinutes(20).ToUnixTimeMilliseconds()),
                    ("@d", Bytes(deviceUserId)),
                ]);

    private void Scan(
        SqliteConnection connection, Guid tripId, Guid placeId, DateTimeOffset at, string? notes) =>
        Insert(connection,
            "insert into cave_trip_points (uuid, cave_trip_uuid, cave_place_uuid, scanned_at, notes, "
            + "created_by_user_uuid) values (@u, @t, @p, @s, @n, @c)",
            ("@u", Bytes(Guid.CreateVersion7())),
            ("@t", Bytes(tripId)),
            ("@p", Bytes(placeId)),
            ("@s", at.ToUnixTimeMilliseconds()),
            ("@n", notes is null ? DBNull.Value : notes),
            ("@c", Bytes(deviceUserId)));

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Insert(SqliteConnection connection, string sql, params (string Name, object Value)[] values)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in values)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// The identifier as the device writes it: sixteen bytes in the order the standard prints
    /// them, which is not the order this platform's own layout uses.
    /// </summary>
    private static byte[] Bytes(Guid id)
    {
        var bytes = new byte[16];
        id.TryWriteBytes(bytes, bigEndian: true, out _);
        return bytes;
    }

    private static byte[] ZipOf(params (string Name, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }

    // ---------- the installation's own side ----------

    /// <summary>
    /// Three of the four markers, as the register holds them: a device place is a feature whose
    /// identifier is the one the device minted, which is what makes a scan resolvable with no
    /// mapping table anywhere. The fourth is deliberately absent.
    /// </summary>
    private async Task SeedPlacesAsync(Guid caveId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var owner = await db.Features.Where(f => f.Id == caveId).Select(f => f.OwnerUserId).SingleAsync();

        await CreatePlaceAsync(writer, caveId, owner, placeAtFifty, "Marcaj 1", -50);
        await CreatePlaceAsync(writer, caveId, owner, placeWithNoDepth, "Marcaj 2", null);
        await CreatePlaceAsync(writer, caveId, owner, placeDeep, "Marcaj 4", -120);
        await db.SaveChangesAsync();
    }

    private async Task CreatePlaceAsync(
        FeatureWriteService writer, Guid caveId, Guid owner, Guid placeId, string name, double? depth)
    {
        var properties = depth is null
            ? """{"speleolocSchemaVersion":1}"""
            : string.Create(CultureInfo.InvariantCulture,
                $$"""{"speleolocSchemaVersion":1,"speleolocDepthInCave":{{depth}}}""");

        var feature = new Feature
        {
            // Both systems mint the same kind of identifier and the device's is the one the sync
            // writes, so this is the whole of the correspondence between a marker and a feature.
            Id = placeId,
            Name = name,
            FeatureTypeId = placeTypeId,
            OwnerUserId = owner,
            Visibility = Visibility.Authenticated,
            Properties = properties,
        };
        await writer.CreateGenericAsync(feature, [new ParentSpec(caveId, IsPrimary: true)]);
    }

    private async Task<Guid> CreateCaveAsync(HttpClient client, bool locationProtected)
    {
        var response = await client.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Spl Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// A survey model created the real way (a file row cannot exist outside the documents chain),
    /// then stations seeded straight into the graph tables: an entrance at 350 m and three points
    /// below it, so that −50 m and −120 m each name exactly one station.
    /// </summary>
    private async Task<Guid> SeedModelWithStationsAsync(Guid caveId, HttpClient? asOwner = null)
    {
        var client = asOwner ?? editor;
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new("application/octet-stream");
        form.Add(bytes, "file", "speleoloc.3d");
        var created = await client.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var modelId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.SingleAsync(m => m.Id == modelId);
        model.Status = SurveyModelStatus.Ready;
        db.SurveyStations.AddRange(
            Station(modelId, "cave.ent.0", "cave.ent", 350, SurveyStationFlags.Entrance),
            Station(modelId, "cave.upper.1", "cave.upper", 330, SurveyStationFlags.Underground),
            Station(modelId, "cave.upper.2", "cave.upper", 300, SurveyStationFlags.Underground),
            Station(modelId, "cave.deep.3", "cave.deep", 230, SurveyStationFlags.Underground));
        await db.SaveChangesAsync();
        return modelId;
    }

    private static SurveyStation Station(
        Guid modelId, string name, string survey, double z, SurveyStationFlags flags) =>
        new()
        {
            SurveyModelId = modelId,
            Name = name,
            SurveyName = survey,
            Position = new Point(new CoordinateZ(25.5, 45.5, z)) { SRID = 4326 },
            Flags = flags,
        };

    private async Task<(Guid Trip, List<Guid> Cavers)> CreateTripAsync(
        HttpClient client, string title, int guests, string visibility = "authenticated")
    {
        var participants = Enumerable.Range(1, guests)
            .Select(i => new { newCaverName = $"Invitat {i} {Guid.NewGuid():N}"[..24] })
            .ToArray();
        var response = await client.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            tripDate = "2026-09-10",
            participants,
            visibility,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var trip = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var cavers = await db.TripLogParticipants.Where(p => p.TripLogId == trip)
            .Select(p => p.CaverId).Distinct().OrderBy(c => c).ToListAsync();
        cavers.Count.ShouldBe(guests);
        return (trip, cavers);
    }

    // ---------- calling the routes ----------

    private object Options(
        Guid model,
        Guid? trip = null,
        Guid? caver = null,
        bool createTrip = false,
        Guid? recording = null) => new
    {
        tripUuid = (recording ?? recordingId).ToString(),
        tripLogId = trip,
        createTrip,
        surveyModelId = model,
        cavers = caver is null
            ? new Dictionary<string, Guid>()
            : new Dictionary<string, Guid> { [deviceUserId.ToString()] = caver.Value },
        visibility = "private",
        candidateCount = 5,
    };

    private async Task<JsonElement> PreviewAsync(HttpClient client, Guid fileId, object options)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/speleoloc-imports/{fileId}/preview", new { options, page = 1, pageSize = 100 });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await BodyAsync(response);
    }

    private static Task<HttpResponseMessage> CommitAsync(
        HttpClient client, Guid fileId, object options, IReadOnlyList<string> pointIds) =>
        client.PostAsJsonAsync($"/api/v1/speleoloc-imports/{fileId}/commit", new { options, pointIds });

    private static List<string> ProposedPointsOf(JsonElement preview) =>
        [.. preview.GetProperty("selectablePointIds").EnumerateArray().Select(e => e.GetString()!)];

    private static List<string> AllPointsOf(JsonElement preview) =>
        [.. preview.GetProperty("selectablePointIds").EnumerateArray().Select(e => e.GetString()!)];

    /// <summary>Config writes ride the trip's version: fetch the ETag, then PUT with If-Match.</summary>
    private static async Task<HttpResponseMessage> ArmAsync(
        HttpClient client, Guid trip, Guid model, string[]? depthFilter = null)
    {
        var current = await client.GetAsync($"/api/v1/trip-logs/{trip}/tracking");
        current.StatusCode.ShouldBe(HttpStatusCode.OK, await current.Content.ReadAsStringAsync());
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/trip-logs/{trip}/tracking")
        {
            Content = JsonContent.Create(new
            {
                state = "armed",
                surveyModelId = model,
                depthFilter = depthFilter ?? [],
            }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", current.Headers.ETag!.ToString());
        return await client.SendAsync(request);
    }

    /// <summary>
    /// A body written out as text rather than built from an object, because what is under test is a
    /// shape no anonymous type can express: a property whose value is the literal <c>null</c>.
    /// </summary>
    private static JsonElement Json(string body) => JsonDocument.Parse(body).RootElement.Clone();

    private Task<Guid> UploadArchiveAsync() => UploadAsync("speleo_loc_2026-09-10_12-00-00.zip", BuildArchive());

    private async Task<Guid> UploadAsync(string fileName, byte[] content)
    {
        var body = new ByteArrayContent(content);
        body.Headers.ContentType = new("application/zip");
        using var form = new MultipartFormDataContent { { body, "file", fileName } };
        var response = await editor.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static string? CodeOf(string problemBody) =>
        JsonDocument.Parse(problemBody).RootElement.GetProperty("code").GetString();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        try { Directory.Delete(filesRoot, recursive: true); } catch { /* best effort */ }
    }
}
