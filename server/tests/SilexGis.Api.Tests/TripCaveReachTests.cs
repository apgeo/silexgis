// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Which caves a trip names is read from several places outside the trip itself, and most of
/// them are deciding whether a position may be disclosed. None of those reads is announced by
/// the type system — a rewrite that dropped one would compile, pass every other suite, and
/// quietly widen or narrow what a caller is shown. This suite is the announcement: each case
/// here states one such reach in terms of what a caller actually receives over HTTP, so the
/// answer is pinned to behaviour rather than to the shape of the query behind it.
///
/// Every refusal is paired with the same request succeeding for somebody entitled to it, over
/// the same fixture. The reader is a Viewer, who holds no exact-location right anywhere; the
/// owner is an Editor who created the cave, so ownership gives them one. The seeded Editors
/// group reads past visibility by design, which is why the withheld side is never an Editor.
///
/// Two independent things can be withheld about a cave and the cases here say which they mean:
/// whether the reader may <em>open</em> it, and whether they may be told <em>where</em> it is.
/// A cave that is readable but guarded proves nothing about the first, and one that is unopenable
/// but unguarded proves nothing about the second — so caves are built with both said out loud,
/// and a case that means one of them creates a cave that fails only that gate.
/// </summary>
public sealed class TripCaveReachTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;   // Editor — creates the caves and the trips, may place them
    private HttpClient reader = null!;  // Viewer — reads them, may not place them
    private Guid readerId;
    private long caveTypeId;
    private long genericTypeId;

    public TripCaveReachTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-tripcave-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tcr-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"tcr-own-{suffix}@t.local");

        readerId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Viewer, $"tcr-read-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"tcr-read-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        genericTypeId = await db.FeatureTypes.Where(t => t.Code == "generic").Select(t => t.Id).SingleAsync();
    }

    /// <summary>
    /// The most consequential of these reads. A photograph filed under a trip is never attached
    /// to any cave, so nothing about the picture itself says where it was taken — except that
    /// the trip names the cave, which places it just as surely as hanging it on the cave would.
    /// If that reach is lost, the guarded cave's own photographs become downloadable with their
    /// capture points intact through the trip, and no error is raised anywhere.
    ///
    /// Both directions are asserted over one fixture, because the failure this guards against
    /// is not "the rule stopped working" but "the rule stopped applying to trips": a fixture
    /// that had quietly stopped placing the photographs at all would satisfy the refusal on its
    /// own, and the open trip beside it is what refuses to let that pass.
    /// </summary>
    [Fact]
    public async Task A_photo_filed_under_a_trip_is_as_placed_as_one_filed_under_the_cave_it_names()
    {
        var guardedCaveId = await CreateCaveAsync(locationProtected: true);
        var openCaveId = await CreateCaveAsync(locationProtected: false);

        var guardedTripId = await CreateTripAsync("Guarded", guardedCaveId);
        var openTripId = await CreateTripAsync("Open", openCaveId);

        var guardedPhotoId = await UploadAsync("guarded.jpg", GeotaggedJpeg(45.53127, 25.44721));
        await AttachToTripAsync(guardedPhotoId, guardedTripId);
        var openPhotoId = await UploadAsync("open.jpg", GeotaggedJpeg(45.11, 25.11));
        await AttachToTripAsync(openPhotoId, openTripId);

        // Fixture proof: both uploads really did read a capture point out of the image, so a
        // refusal below is the rule speaking and not an empty file.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var placed = await db.StoredFiles.AsNoTracking()
                .Where(f => f.Id == guardedPhotoId || f.Id == openPhotoId)
                .CountAsync(f => f.Geom != null);
            placed.ShouldBe(2);
        }

        // Fixture proof, the other half: the reader really can read the guarded cave, and
        // really is told it is only approximately placed for them.
        var seenCave = await ReadJsonAsync(await reader.GetAsync($"/api/v1/caves/{guardedCaveId}"));
        seenCave.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();

        // The picture on the trip that names the guarded cave: the bytes still hold the fix the
        // camera wrote, so they are not handed over, and the response says so up front.
        var guarded = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{guardedPhotoId}"));
        guarded.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeFalse();
        (await reader.GetAsync(guarded.GetProperty("contentUrl").GetString()))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The same caller, the same kind of request, one trip over: nothing about this cave is
        // being kept back, so the original is served. This is what makes the refusal above a
        // rule about the cave the trip names rather than a rule about trips.
        var open = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{openPhotoId}"));
        open.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeTrue();
        (await reader.GetAsync(open.GetProperty("contentUrl").GetString()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // And the owner may place the guarded cave exactly, so the withheld bytes are withheld
        // from this caller rather than from everybody.
        var held = await ReadJsonAsync(await owner.GetAsync($"/api/v1/files/{guardedPhotoId}"));
        held.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeTrue();
        (await owner.GetAsync(held.GetProperty("contentUrl").GetString()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// The same reach, one level up. A camp gathers a named set of trips, and a photograph filed
    /// under the camp rather than under one of them was taken on those same journeys — which of
    /// the two places the uploader chose says nothing about where the camera was. So the caves the
    /// camp's member trips name have to place it exactly as they place a picture on the trip
    /// itself. They are separate reaches in the code: a camp is visible in its own right, quite
    /// apart from the trips in it, so a camp readable by a wider audience than its trips is the
    /// short way round the trip rule if the camp is not followed through to them.
    ///
    /// Both directions over one fixture again: the camp holding only the open trip serves the
    /// bytes, and the same camp with the guarded trip added does not, so the refusal is the caves
    /// speaking rather than a build that had stopped placing camp photographs at all.
    /// </summary>
    [Fact]
    public async Task A_photo_filed_under_a_camp_is_placed_by_the_caves_its_trips_name()
    {
        var openCaveId = await CreateCaveAsync(locationProtected: false);
        var guardedCaveId = await CreateCaveAsync(locationProtected: true);
        var openTripId = await CreateTripAsync("Camp open leg", openCaveId);
        var guardedTripId = await CreateTripAsync("Camp guarded leg", guardedCaveId);

        var campId = await CreateCampAsync("Summer camp");
        await AddTripToCampAsync(campId, openTripId);

        var photoId = await UploadAsync("camp.jpg", GeotaggedJpeg(45.41, 25.41));
        await AttachToCampAsync(photoId, campId);

        // Fixture proof: the upload really did read a capture point out of the image, and the
        // camp really does reach this caller, so the refusal below is neither an empty file nor
        // a file nobody can get to.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == photoId)).Geom
                .ShouldNotBeNull();
        }

        // Only the open trip is in the camp: nothing is being kept back, so the original is
        // served. This is the half that would still pass on a build that never followed the camp
        // to its trips, which is why the guarded leg comes next over the same file.
        var before = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{photoId}"));
        before.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeTrue();
        before.GetProperty("position").ValueKind.ShouldNotBe(JsonValueKind.Null);
        (await reader.GetAsync(before.GetProperty("contentUrl").GetString()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await AddTripToCampAsync(campId, guardedTripId);

        // The capture point itself, not only the bytes: the fix the camera wrote is the thing the
        // guarded cave's position rule exists to keep, and it travels in the payload beside the
        // delivery token rather than inside the file.
        var after = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{photoId}"));
        after.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeFalse();
        after.GetProperty("position").ValueKind.ShouldBe(JsonValueKind.Null);
        (await reader.GetAsync(after.GetProperty("contentUrl").GetString()))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // And the owner, who may place the guarded cave exactly, is served the original over the
        // same camp — the bytes are withheld from this caller, not from everybody.
        var held = await ReadJsonAsync(await owner.GetAsync($"/api/v1/files/{photoId}"));
        held.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeTrue();
        (await owner.GetAsync(held.GetProperty("contentUrl").GetString()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// A trip naming two caves, one guarded and one open, is the case where a reach that was
    /// narrowed rather than lost still passes every single-cave test. The strictest of the
    /// caves a trip names has to win, or filing the photograph under a trip that also went
    /// somewhere public becomes a way to ask for the guarded bytes again.
    /// </summary>
    [Fact]
    public async Task One_guarded_cave_among_a_trips_caves_is_enough_to_hold_the_photo_back()
    {
        var openCaveId = await CreateCaveAsync(locationProtected: false);
        var guardedCaveId = await CreateCaveAsync(locationProtected: true);

        var tripId = await CreateTripAsync("Mixed", openCaveId);
        var photoId = await UploadAsync("mixed.jpg", GeotaggedJpeg(45.31, 25.31));
        await AttachToTripAsync(photoId, tripId);

        // Naming only the open cave, the original is served — the positive case, on the same
        // file, one cave before the refusal.
        var before = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{photoId}"));
        (await reader.GetAsync(before.GetProperty("contentUrl").GetString()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await SetTripCavesAsync(tripId, openCaveId, guardedCaveId);

        var after = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{photoId}"));
        after.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeFalse();
        (await reader.GetAsync(after.GetProperty("contentUrl").GetString()))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// What a trip names is answered role-agnostically: the relation carries no notion of what
    /// the trip did there, so every cave the trip is about counts the same. Naming two caves and
    /// asking each of them for its trips has to find the one trip twice — a reach that answered
    /// for only the first-named cave, or that counted the trip once per naming, would still pass
    /// a fixture with one cave on one trip.
    /// </summary>
    [Fact]
    public async Task A_trip_answers_for_every_cave_it_names_and_is_counted_once_for_each()
    {
        var firstId = await CreateCaveAsync(locationProtected: false);
        var secondId = await CreateCaveAsync(locationProtected: false);
        var tripId = await CreateTripAsync("Two caves", firstId, secondId);

        foreach (var caveId in new[] { firstId, secondId })
        {
            var page = await ReadJsonAsync(await owner.GetAsync($"/api/v1/trip-logs/?caveId={caveId}"));
            page.GetProperty("totalItems").GetInt32().ShouldBe(1);
            page.GetProperty("items").EnumerateArray().Single()
                .GetProperty("id").GetGuid().ShouldBe(tripId);

            var summary = await ReadJsonAsync(await owner.GetAsync($"/api/v1/caves/{caveId}/summary"));
            summary.GetProperty("tripLogCount").GetInt32().ShouldBe(1);
        }
    }

    /// <summary>
    /// Asking for the trips of a cave the caller may read but may not place is refused as an
    /// empty page with a total of zero, not as a partial list and not as an error: the trips
    /// carry their own geometries, so listing them would place the cave the filter names.
    ///
    /// The total is the load-bearing half and the one a length assertion misses. A build that
    /// counted before the cut and returned the real number beside no rows would be telling the
    /// caller exactly how many trips went to a cave they are not allowed to locate.
    /// </summary>
    [Fact]
    public async Task Asking_for_the_trips_of_a_cave_one_may_not_place_returns_an_empty_page_with_no_total()
    {
        var guardedCaveId = await CreateCaveAsync(locationProtected: true);
        var openCaveId = await CreateCaveAsync(locationProtected: false);
        await CreateTripAsync("Guarded", guardedCaveId);
        await CreateTripAsync("Open", openCaveId);

        var withheld = await ReadJsonAsync(await reader.GetAsync($"/api/v1/trip-logs/?caveId={guardedCaveId}"));
        withheld.GetProperty("items").GetArrayLength().ShouldBe(0);
        withheld.GetProperty("totalItems").GetInt32().ShouldBe(0);

        // The same caller, the same request, on a cave nobody is keeping the position of: the
        // trip is listed. So the empty page above is the protection rule, not a caller who
        // simply cannot see trips.
        var open = await ReadJsonAsync(await reader.GetAsync($"/api/v1/trip-logs/?caveId={openCaveId}"));
        open.GetProperty("items").GetArrayLength().ShouldBe(1);
        open.GetProperty("totalItems").GetInt32().ShouldBe(1);

        // And the owner, who may place the guarded cave, is answered normally — the withholding
        // is per caller, not a broken filter.
        var held = await ReadJsonAsync(await owner.GetAsync($"/api/v1/trip-logs/?caveId={guardedCaveId}"));
        held.GetProperty("totalItems").GetInt32().ShouldBe(1);
    }

    /// <summary>
    /// The cave page shows a count of trips over a list of them, and the two are answered by
    /// different endpoints. They have to be filtered by the same rules or the page contradicts
    /// itself: a caller who may read the cave but not place it is handed an empty list, and a
    /// count taken past that rule would print, immediately above the emptiness, exactly how many
    /// trips they were not shown — which is the disclosure the empty list exists to refuse.
    ///
    /// Both callers are asserted here in one case on purpose. The agreement is what is being
    /// pinned, and agreement at zero is satisfied by any build that has simply stopped finding
    /// trips at all; the caller who may place the cave, over the same cave and the same trips,
    /// is what makes the zero a withholding rather than an empty fixture.
    /// </summary>
    [Fact]
    public async Task The_cave_pages_trip_count_says_the_same_as_the_list_of_trips_beneath_it()
    {
        var guardedCaveId = await CreateCaveAsync(locationProtected: true);
        await CreateTripAsync("First", guardedCaveId);
        await CreateTripAsync("Second", guardedCaveId);

        // The owner created this cave, so they may place it exactly: they are told two trips and
        // handed two trips.
        var heldCount = await ReadJsonAsync(await owner.GetAsync($"/api/v1/caves/{guardedCaveId}/summary"));
        var heldList = await ReadJsonAsync(await owner.GetAsync($"/api/v1/trip-logs/?caveId={guardedCaveId}"));
        heldCount.GetProperty("tripLogCount").GetInt32().ShouldBe(2);
        heldList.GetProperty("totalItems").GetInt32().ShouldBe(2);
        heldList.GetProperty("items").GetArrayLength().ShouldBe(2);

        // The reader is a Viewer holding no exact-location right anywhere, so no group membership
        // reads past the rule for them. They may read the cave — asserted, so that what follows
        // is the location rule and not a caller who simply cannot see the cave at all.
        var seenCave = await ReadJsonAsync(await reader.GetAsync($"/api/v1/caves/{guardedCaveId}"));
        seenCave.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();

        var withheldCount = await ReadJsonAsync(await reader.GetAsync($"/api/v1/caves/{guardedCaveId}/summary"));
        var withheldList = await ReadJsonAsync(await reader.GetAsync($"/api/v1/trip-logs/?caveId={guardedCaveId}"));
        withheldCount.GetProperty("tripLogCount").GetInt32().ShouldBe(0);
        withheldList.GetProperty("totalItems").GetInt32().ShouldBe(0);
        withheldList.GetProperty("items").GetArrayLength().ShouldBe(0);

        // And the same reader on a cave nobody guards is told two and handed two, over trips
        // created the same way — so the zeros above are about this cave's position, not about
        // this caller's reach into trips.
        var openCaveId = await CreateCaveAsync(locationProtected: false);
        await CreateTripAsync("Third", openCaveId);
        await CreateTripAsync("Fourth", openCaveId);

        var openCount = await ReadJsonAsync(await reader.GetAsync($"/api/v1/caves/{openCaveId}/summary"));
        var openList = await ReadJsonAsync(await reader.GetAsync($"/api/v1/trip-logs/?caveId={openCaveId}"));
        openCount.GetProperty("tripLogCount").GetInt32().ShouldBe(2);
        openList.GetProperty("totalItems").GetInt32().ShouldBe(2);
        openList.GetProperty("items").GetArrayLength().ShouldBe(2);
    }

    /// <summary>
    /// A trip hands back only the caves the caller may place. The list a caller edits is
    /// therefore not the whole list, which is why the write path has to put back what it hid
    /// rather than treating the submitted list as the truth.
    /// </summary>
    [Fact]
    public async Task A_trips_cave_list_comes_back_without_the_caves_the_caller_may_not_place()
    {
        var guardedCaveId = await CreateCaveAsync(locationProtected: true);
        var openCaveId = await CreateCaveAsync(locationProtected: false);
        var tripId = await CreateTripAsync("Both", guardedCaveId, openCaveId);

        // The reader may open the guarded cave perfectly well — it is readable by every account
        // — so nothing but its position is being withheld here. That is what makes this case
        // about placement rather than about readability.
        (await reader.GetAsync($"/api/v1/caves/{guardedCaveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var seen = await ReadJsonAsync(await reader.GetAsync($"/api/v1/trip-logs/{tripId}"));
        var seenCaves = seen.GetProperty("caveIds").EnumerateArray().Select(x => x.GetGuid()).ToList();
        seenCaves.ShouldBe([openCaveId]);
        seen.GetProperty("cavesWithheld").GetInt32().ShouldBe(1);

        // The owner sees both over the same trip, so the shorter list is a redaction rather
        // than a trip that only ever named one cave.
        var held = await ReadJsonAsync(await owner.GetAsync($"/api/v1/trip-logs/{tripId}"));
        held.GetProperty("caveIds").EnumerateArray().Select(x => x.GetGuid())
            .OrderBy(x => x).ShouldBe(new[] { guardedCaveId, openCaveId }.OrderBy(x => x));
        held.GetProperty("cavesWithheld").GetInt32().ShouldBe(0);
    }

    /// <summary>
    /// A trip hands back only the caves the caller may <em>open</em>, which is a different
    /// question from whether they may place one. A cave nobody but its owner may read can be
    /// named on a trip half the club reads, and the identifier alone is enough to go and ask for
    /// that cave by it — on this endpoint, on the map, on every surface that takes an identifier.
    /// </summary>
    /// <remarks>
    /// The withheld cave here carries no location protection at all, which is the whole point:
    /// the position rule reads its rows past every visibility filter and returns the lot when
    /// nothing in the set is guarded, so a list filtered by position alone hands this cave over
    /// to every reader of the trip. Both caves sit on one trip and both readings are taken over
    /// the one fixture, so a build that withheld everything fails here exactly as loudly as one
    /// that withheld nothing.
    /// </remarks>
    [Fact]
    public async Task A_trips_cave_list_comes_back_without_the_caves_the_caller_may_not_open()
    {
        var hiddenCaveId = await CreateCaveAsync(locationProtected: false, visibility: "private");
        var openCaveId = await CreateCaveAsync(locationProtected: false);
        var tripId = await CreateTripAsync("Objectives", hiddenCaveId, openCaveId);

        // The fixture, stated rather than assumed: this reader genuinely cannot open the cave,
        // and is a Viewer rather than an Editor because the seeded Editors group reads past
        // visibility by design and would prove nothing here.
        (await reader.GetAsync($"/api/v1/caves/{hiddenCaveId}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/caves/{openCaveId}")).StatusCode
            .ShouldBe(HttpStatusCode.OK);

        var seen = await ReadJsonAsync(await reader.GetAsync($"/api/v1/trip-logs/{tripId}"));
        var seenCaves = seen.GetProperty("caveIds").EnumerateArray().Select(x => x.GetGuid()).ToList();
        seenCaves.ShouldBe([openCaveId]);

        // Counted, never named: the reader is told the list is short, and nothing more.
        seen.GetProperty("cavesWithheld").GetInt32().ShouldBe(1);

        // Whole payload, not just the field — the identifier must not travel anywhere on this
        // answer, and a future field carrying it would pass an assertion made on `caveIds` alone.
        var payload = await (await reader.GetAsync($"/api/v1/trip-logs/{tripId}")).Content.ReadAsStringAsync();
        payload.ShouldNotContain(hiddenCaveId.ToString());
        payload.ShouldContain(openCaveId.ToString());

        // The owner sees both over the same trip, so the shorter list is a withholding rather
        // than a trip that only ever named one cave.
        var held = await ReadJsonAsync(await owner.GetAsync($"/api/v1/trip-logs/{tripId}"));
        held.GetProperty("caveIds").EnumerateArray().Select(x => x.GetGuid())
            .OrderBy(x => x).ShouldBe(new[] { hiddenCaveId, openCaveId }.OrderBy(x => x));
        held.GetProperty("cavesWithheld").GetInt32().ShouldBe(0);
    }

    /// <summary>
    /// The same withholding on the listing, not only on the single read. A page of trips is
    /// built by a different call than one trip is, and a rule applied in only one of them is a
    /// rule that holds until somebody opens the list.
    /// </summary>
    [Fact]
    public async Task A_trip_listing_withholds_the_same_caves_the_single_read_does()
    {
        var hiddenCaveId = await CreateCaveAsync(locationProtected: false, visibility: "private");
        var openCaveId = await CreateCaveAsync(locationProtected: false);
        var tripId = await CreateTripAsync("Listed", hiddenCaveId, openCaveId);

        var listed = await ReadJsonAsync(
            await reader.GetAsync($"/api/v1/trip-logs/?caveId={openCaveId}"));
        var row = listed.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == tripId);
        row.GetProperty("caveIds").EnumerateArray().Select(x => x.GetGuid()).ShouldBe([openCaveId]);
        row.GetProperty("cavesWithheld").GetInt32().ShouldBe(1);

        var ownersRow = (await ReadJsonAsync(
                await owner.GetAsync($"/api/v1/trip-logs/?caveId={openCaveId}")))
            .GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == tripId);
        ownersRow.GetProperty("caveIds").GetArrayLength().ShouldBe(2);
        ownersRow.GetProperty("cavesWithheld").GetInt32().ShouldBe(0);
    }

    /// <summary>
    /// Withholding a cave from the rows is worth nothing while the same endpoint will take that
    /// cave as a filter: holding the identifier is the ordinary case — it survives access being
    /// narrowed, and people send each other links — and an id that answers differently from one
    /// that does not exist is an id anybody can go looking for. Worse, the answer is the trips
    /// that reached it, each carrying its own exact geometry, so the filter would place the very
    /// cave the rows decline to name.
    /// </summary>
    /// <remarks>
    /// Both halves over one fixture: the same caller filtering by the cave they may open still
    /// gets the trip, so a build that empties every filtered page fails here as loudly as one
    /// that empties none.
    /// </remarks>
    [Fact]
    public async Task Filtering_a_trip_listing_by_a_cave_the_caller_may_not_open_finds_nothing()
    {
        var hiddenCaveId = await CreateCaveAsync(locationProtected: false, visibility: "private");
        var openCaveId = await CreateCaveAsync(locationProtected: false);
        var tripId = await CreateTripAsync("Filtered", hiddenCaveId, openCaveId);

        // Fixture proof: the cave really is unopenable for this caller, and really is unguarded
        // — so a refusal below is the readability gate speaking, not the position one.
        (await reader.GetAsync($"/api/v1/caves/{hiddenCaveId}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        var byHidden = await ReadJsonAsync(
            await reader.GetAsync($"/api/v1/trip-logs/?caveId={hiddenCaveId}"));
        byHidden.GetProperty("totalItems").GetInt32().ShouldBe(0);
        byHidden.GetProperty("items").GetArrayLength().ShouldBe(0);

        var byOpen = await ReadJsonAsync(
            await reader.GetAsync($"/api/v1/trip-logs/?caveId={openCaveId}"));
        byOpen.GetProperty("items").EnumerateArray()
            .ShouldContain(x => x.GetProperty("id").GetGuid() == tripId);

        // And the cave is findable by the person it is not hidden from, so the empty page above
        // is about this caller rather than about the link never having been written.
        var ownersByHidden = await ReadJsonAsync(
            await owner.GetAsync($"/api/v1/trip-logs/?caveId={hiddenCaveId}"));
        ownersByHidden.GetProperty("items").EnumerateArray()
            .ShouldContain(x => x.GetProperty("id").GetGuid() == tripId);
    }

    /// <summary>
    /// Somebody may hold write on a trip and still be unable to open one of the caves it names,
    /// and that stops being a corner case the moment a trip's readership is a list somebody
    /// types. The list such a writer is handed is short of that cave, so a save that echoes the
    /// list back must not have the absence read as an instruction: whatever the read took out,
    /// the write puts back.
    /// </summary>
    /// <remarks>
    /// The other half is asserted over the same fixture, and it is what keeps the guard from
    /// degenerating into "a cave list can no longer remove anything" — the cave this writer
    /// <em>was</em> shown goes when they leave it out, by exactly the request shape that leaves
    /// the withheld one alone.
    /// </remarks>
    [Fact]
    public async Task A_writer_who_cannot_open_one_of_the_trips_caves_still_saves_it()
    {
        var hiddenCaveId = await CreateCaveAsync(locationProtected: false, visibility: "private");
        var openCaveId = await CreateCaveAsync(locationProtected: false);
        var tripId = await CreateTripAsync("Stranger", hiddenCaveId, openCaveId);
        await GrantTripAsync(tripId, readerId, AccessAction.Read | AccessAction.Write);

        // The fixture, stated rather than assumed: this writer genuinely cannot open the cave,
        // and genuinely was handed a list without it.
        (await reader.GetAsync($"/api/v1/caves/{hiddenCaveId}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        var seen = await ReadJsonAsync(await reader.GetAsync($"/api/v1/trip-logs/{tripId}"));
        seen.GetProperty("caveIds").EnumerateArray().Select(x => x.GetGuid()).ShouldBe([openCaveId]);
        seen.GetProperty("cavesWithheld").GetInt32().ShouldBe(1);

        var saved = await reader.PutWithIfMatchAsync(
            $"/api/v1/trip-logs/{tripId}", TripBody("Stranger edited", [openCaveId]));
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        var afterSave = await ReadJsonAsync(await owner.GetAsync($"/api/v1/trip-logs/{tripId}"));
        afterSave.GetProperty("title").GetString().ShouldBe("Stranger edited");
        afterSave.GetProperty("caveIds").EnumerateArray().Select(x => x.GetGuid())
            .OrderBy(x => x).ShouldBe(new[] { hiddenCaveId, openCaveId }.OrderBy(x => x));

        var dropped = await reader.PutWithIfMatchAsync(
            $"/api/v1/trip-logs/{tripId}", TripBody("Stranger dropped one", []));
        dropped.StatusCode.ShouldBe(HttpStatusCode.OK, await dropped.Content.ReadAsStringAsync());

        var afterDrop = await ReadJsonAsync(await owner.GetAsync($"/api/v1/trip-logs/{tripId}"));
        afterDrop.GetProperty("caveIds").EnumerateArray().Select(x => x.GetGuid())
            .ShouldBe([hiddenCaveId]);
    }

    /// <summary>
    /// Putting a record back the way it was handed over is the shape a restore takes, and it is
    /// offered to anybody holding write — so the least considered edit a trip can receive is
    /// also the one most likely to come from somebody who cannot open one of its caves. Every
    /// field travels, read-only ones included, and none of them may cost the trip a link.
    /// </summary>
    [Fact]
    public async Task Posting_a_trip_back_whole_keeps_the_cave_its_author_may_not_open()
    {
        var hiddenCaveId = await CreateCaveAsync(locationProtected: false, visibility: "private");
        var openCaveId = await CreateCaveAsync(locationProtected: false);
        var tripId = await CreateTripAsync("Whole", hiddenCaveId, openCaveId);
        await GrantTripAsync(tripId, readerId, AccessAction.Read | AccessAction.Write);

        (await reader.GetAsync($"/api/v1/caves/{hiddenCaveId}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        var seen = await ReadJsonAsync(await reader.GetAsync($"/api/v1/trip-logs/{tripId}"));

        // The trip exactly as this reader received it, with the one field a restore puts back to
        // an older value — the withheld count included, because a whole record posted back
        // carries the fields nobody writes as well as the ones somebody does.
        var body = new Dictionary<string, object?>
        {
            ["title"] = "Title as it was",
            ["tripDate"] = seen.GetProperty("tripDate").GetString(),
            ["visibility"] = seen.GetProperty("visibility").GetString(),
            ["participants"] = Array.Empty<object>(),
            ["caveIds"] = seen.GetProperty("caveIds").EnumerateArray().Select(x => x.GetGuid()).ToArray(),
            ["cavesWithheld"] = seen.GetProperty("cavesWithheld").GetInt32(),
        };

        var restored = await reader.PutWithIfMatchAsync($"/api/v1/trip-logs/{tripId}", body);
        restored.StatusCode.ShouldBe(HttpStatusCode.OK, await restored.Content.ReadAsStringAsync());

        var after = await ReadJsonAsync(await owner.GetAsync($"/api/v1/trip-logs/{tripId}"));
        after.GetProperty("title").GetString().ShouldBe("Title as it was");
        after.GetProperty("caveIds").EnumerateArray().Select(x => x.GetGuid())
            .OrderBy(x => x).ShouldBe(new[] { hiddenCaveId, openCaveId }.OrderBy(x => x));
    }

    /// <summary>
    /// The bare cave list is a coarse instrument, and deliberately so. It reads over every role a
    /// trip can name a cave under — "which caves was this trip about" has never meant one of
    /// them — while it writes and unnames under the plainest role alone. So a cave the trip
    /// holds only as an objective survives a list posted without it: one coarse list must not be
    /// able to erase a finer statement somebody made on purpose elsewhere, and the way to take
    /// such a cave off a trip is through the role that put it there.
    /// </summary>
    /// <remarks>
    /// Both halves are in one case, because the rule is only meaningful as a distinction: the
    /// same request that leaves the objective alone drops the cave held under the list's own
    /// role. Nothing about the outcome is concealed either — the write answers with the trip
    /// read afresh, so a caller who asked for the drop is told in that same round trip that the
    /// cave is still named.
    /// </remarks>
    [Fact]
    public async Task A_cave_the_trip_holds_only_as_an_objective_outlives_a_list_that_omits_it()
    {
        var listedCaveId = await CreateCaveAsync(locationProtected: false);
        var objectiveCaveId = await CreateCaveAsync(locationProtected: false);
        var tripId = await CreateTripAsync("Roles", listedCaveId);
        await NameAsync(tripId, objectiveCaveId, "trip-objective");

        // The trip names both, and the list says nothing about which role holds which — which
        // is precisely why the write has to decide what an omission means.
        var before = await ReadJsonAsync(await owner.GetAsync($"/api/v1/trip-logs/{tripId}"));
        before.GetProperty("caveIds").EnumerateArray().Select(x => x.GetGuid())
            .OrderBy(x => x).ShouldBe(new[] { listedCaveId, objectiveCaveId }.OrderBy(x => x));

        var response = await owner.PutWithIfMatchAsync(
            $"/api/v1/trip-logs/{tripId}", TripBody("Roles", []));
        var answered = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, answered);

        JsonDocument.Parse(answered).RootElement
            .GetProperty("caveIds").EnumerateArray().Select(x => x.GetGuid())
            .ShouldBe([objectiveCaveId]);

        var after = await ReadJsonAsync(await owner.GetAsync($"/api/v1/trip-logs/{tripId}"));
        after.GetProperty("caveIds").EnumerateArray().Select(x => x.GetGuid())
            .ShouldBe([objectiveCaveId]);
    }

    /// <summary>
    /// Swapping a trip's only cave for another one, in a single write. The removal and the
    /// addition are decided against the same unsaved unit of work, so a build that read the
    /// stored rows alone would put the new cave into the very link the removal had just condemned
    /// — and lose it with that link, or fail the save outright. Either way the caller is told the
    /// write succeeded, so the trip is asked afterwards what it now names.
    /// </summary>
    [Fact]
    public async Task Replacing_a_trips_only_cave_in_one_write_records_the_new_cave()
    {
        var firstId = await CreateCaveAsync(locationProtected: false);
        var secondId = await CreateCaveAsync(locationProtected: false);
        var tripId = await CreateTripAsync("Swap", firstId);

        await SetTripCavesAsync(tripId, secondId);

        var trip = await ReadJsonAsync(await owner.GetAsync($"/api/v1/trip-logs/{tripId}"));
        trip.GetProperty("caveIds").EnumerateArray().Select(x => x.GetGuid()).ShouldBe([secondId]);

        // Asked from the caves' side as well: the reach that answers "which trips is this cave
        // named on" is a different query from the trip's own list, and only both of them together
        // say the swap actually happened rather than being reported.
        var onSecond = await ReadJsonAsync(await owner.GetAsync($"/api/v1/trip-logs/?caveId={secondId}"));
        onSecond.GetProperty("totalItems").GetInt32().ShouldBe(1);
        var onFirst = await ReadJsonAsync(await owner.GetAsync($"/api/v1/trip-logs/?caveId={firstId}"));
        onFirst.GetProperty("totalItems").GetInt32().ShouldBe(0);
    }

    /// <summary>
    /// Emptying a trip's cave list in one write. Both caves sit on one link, so the two removals
    /// happen with nothing saved in between: a survivor count taken from the stored rows sees
    /// each of them as the only one going and keeps a link holding nothing but the trip — a
    /// one-ended association the link rules refuse and no surface can repair.
    /// </summary>
    [Fact]
    public async Task Dropping_every_cave_from_a_trip_leaves_no_link_behind()
    {
        var firstId = await CreateCaveAsync(locationProtected: false);
        var secondId = await CreateCaveAsync(locationProtected: false);
        var tripId = await CreateTripAsync("Emptied", firstId, secondId);

        // Fixture proof: one link really is holding both caves, which is what makes the two
        // removals share it.
        var before = await ReadJsonAsync(
            await owner.GetAsync($"/api/v1/reslinks/for-target?type=tripLog&id={tripId}"));
        before.GetProperty("totalItems").GetInt32().ShouldBe(1);

        await SetTripCavesAsync(tripId);

        var trip = await ReadJsonAsync(await owner.GetAsync($"/api/v1/trip-logs/{tripId}"));
        trip.GetProperty("caveIds").GetArrayLength().ShouldBe(0);

        var after = await ReadJsonAsync(
            await owner.GetAsync($"/api/v1/reslinks/for-target?type=tripLog&id={tripId}"));
        after.GetProperty("totalItems").GetInt32().ShouldBe(0);
    }

    /// <summary>
    /// A deleted trip takes its roles with it. A role says what <em>this trip</em> did somewhere,
    /// so a role link outliving the trip does not become a relation between the caves it named —
    /// it becomes a directed link with nothing distinguished in it, shown on each of those caves'
    /// pages as a relation to the others and refused by every later edit.
    /// </summary>
    [Fact]
    public async Task Deleting_a_trip_takes_the_role_links_it_named_caves_through()
    {
        var firstId = await CreateCaveAsync(locationProtected: false);
        var secondId = await CreateCaveAsync(locationProtected: false);
        var tripId = await CreateTripAsync("Doomed", firstId, secondId);

        // Fixture proof: the caves really are related through the trip before it goes, so an
        // empty answer afterwards is the deletion and not a fixture that never linked anything.
        var linked = await ReadJsonAsync(
            await owner.GetAsync($"/api/v1/reslinks/for-target?type=feature&id={firstId}"));
        linked.GetProperty("totalItems").GetInt32().ShouldBe(1);

        var deleted = await owner.DeleteAsync($"/api/v1/trip-logs/{tripId}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());

        foreach (var caveId in new[] { firstId, secondId })
        {
            var links = await ReadJsonAsync(
                await owner.GetAsync($"/api/v1/reslinks/for-target?type=feature&id={caveId}"));
            links.GetProperty("totalItems").GetInt32().ShouldBe(0);
        }
    }

    /// <summary>
    /// A role names anything linkable, and a trip's cave list is only the caves among them. A
    /// write of that list therefore has nothing to say about a spring or a shaft the same role
    /// names: those could never have appeared in the list being edited, so their absence from it
    /// is not an instruction to forget them.
    /// </summary>
    [Fact]
    public async Task Writing_a_trips_cave_list_leaves_the_non_caves_the_same_role_names()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        var tripId = await CreateTripAsync("Mixed targets", caveId);
        var springId = await CreateGenericFeatureAsync();

        var links = await ReadJsonAsync(
            await owner.GetAsync($"/api/v1/reslinks/for-target?type=tripLog&id={tripId}"));
        var linkId = links.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();
        var joined = await owner.PostAsJsonAsync($"/api/v1/reslinks/{linkId}/members", new
        {
            targetType = "feature",
            targetId = springId,
            isMain = false,
            sortOrder = 2,
            note = (string?)null,
            anchorKind = "whole",
            anchor = (object?)null,
            anchorFileId = (Guid?)null,
        });
        joined.StatusCode.ShouldBe(HttpStatusCode.Created, await joined.Content.ReadAsStringAsync());

        // The cave list comes back with the cave and nothing else — so saving it back is a write
        // that never mentions the spring.
        var seen = await ReadJsonAsync(await owner.GetAsync($"/api/v1/trip-logs/{tripId}"));
        seen.GetProperty("caveIds").EnumerateArray().Select(x => x.GetGuid()).ShouldBe([caveId]);

        await SetTripCavesAsync(tripId, caveId);

        var stillLinked = await ReadJsonAsync(
            await owner.GetAsync($"/api/v1/reslinks/for-target?type=feature&id={springId}"));
        stillLinked.GetProperty("totalItems").GetInt32().ShouldBe(1);
    }

    // ---- helpers ----

    private async Task<Guid> CreateGenericFeatureAsync()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Spring {Guid.NewGuid():N}"[..30],
            featureTypeId = genericTypeId,
            geometry = new { type = "Point", coordinates = new[] { 25.81, 45.81 } },
            locationProtected = false,
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// A cave, said in the two dimensions that decide what a trip may say about it: whether the
    /// reader may open it at all, and whether they may be told where it is. They are independent,
    /// and a fixture that fixed either of them would prove only half the rule — a suite in which
    /// every cave is readable can never catch a list that withholds by position alone.
    /// </summary>
    private async Task<Guid> CreateCaveAsync(
        bool locationProtected, string visibility = "authenticated")
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Reach {Guid.NewGuid():N}"[..30],
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

    private async Task<Guid> CreateTripAsync(string title, params Guid[] caveIds)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", TripBody(title, caveIds));
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task SetTripCavesAsync(Guid tripId, params Guid[] caveIds)
    {
        var response = await owner.PutWithIfMatchAsync(
            $"/api/v1/trip-logs/{tripId}", TripBody("Mixed", caveIds));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Gives somebody who is nobody in particular the right to read and change one trip, which
    /// is how a trip acquires a writer who was never given anything on the caves it names.
    /// </summary>
    private async Task GrantTripAsync(Guid tripId, Guid userId, AccessAction actions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.TripLogs,
            Actions = actions,
            ScopeKind = AccessScopeKind.Object,
            // Non-feature domains anchor object scope in ScopeId; ScopeFeatureId is the
            // feature-domain foreign key and means nothing here.
            ScopeId = tripId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Records that a trip is about a place under one named role, the way the trip page's role
    /// fields do — which is the only way to put a cave on a trip under anything but the
    /// plainest role.
    /// </summary>
    private async Task NameAsync(Guid tripId, Guid featureId, string roleCode)
    {
        var types = await owner.GetFromJsonAsync<JsonElement>("/api/v1/reslinks/relation-types");
        var roleId = types.EnumerateArray()
            .Single(r => r.GetProperty("code").GetString() == roleCode)
            .GetProperty("id").GetInt64();

        var response = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = roleId,
            description = (string?)null,
            members = new object[]
            {
                new
                {
                    targetType = "tripLog",
                    targetId = tripId,
                    isMain = true,
                    sortOrder = 0,
                    note = (string?)null,
                    anchorKind = "whole",
                    anchor = (object?)null,
                    anchorFileId = (Guid?)null,
                },
                new
                {
                    targetType = "feature",
                    targetId = featureId,
                    isMain = false,
                    sortOrder = 1,
                    note = (string?)null,
                    anchorKind = "whole",
                    anchor = (object?)null,
                    anchorFileId = (Guid?)null,
                },
            },
        });
        response.StatusCode.ShouldBe(
            HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static object TripBody(string title, Guid[] caveIds) => new
    {
        title,
        tripDate = "2026-07-01",
        caveIds,
        participants = Array.Empty<object>(),
        visibility = "authenticated",
    };

    private async Task<Guid> UploadAsync(string fileName, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("image/jpeg");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        var response = await owner.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task AttachToTripAsync(Guid fileId, Guid tripId) =>
        await AttachAsync(fileId, "tripLog", tripId);

    private async Task AttachToCampAsync(Guid fileId, Guid campId) =>
        await AttachAsync(fileId, "expedition", campId);

    private async Task AttachAsync(Guid fileId, string entityType, Guid entityId)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/attachments/", new
        {
            fileId,
            entityType,
            entityId,
            role = "other",
            sortOrder = 0,
        });
        response.StatusCode.ShouldBe(
            HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> CreateCampAsync(string name)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/expeditions/", new
        {
            name = $"{name} {Guid.NewGuid():N}"[..30],
            description = (string?)null,
            startDate = "2026-07-01",
            endDate = (string?)null,
            geom = (object?)null,
            cavingGroupId = (Guid?)null,
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task AddTripToCampAsync(Guid campId, Guid tripId)
    {
        var response = await owner.PostAsJsonAsync(
            $"/api/v1/expeditions/{campId}/trips", new { tripLogId = tripId });
        response.StatusCode.ShouldBe(
            HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    private static byte[] GeotaggedJpeg(double lat, double lon)
    {
        using var image = new MagickImage(MagickColors.ForestGreen, 240, 180);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, lat >= 0 ? "N" : "S");
        exif.SetValue(ExifTag.GPSLatitude, ToDms(Math.Abs(lat)));
        exif.SetValue(ExifTag.GPSLongitudeRef, lon >= 0 ? "E" : "W");
        exif.SetValue(ExifTag.GPSLongitude, ToDms(Math.Abs(lon)));
        image.SetProfile(exif);
        return image.ToByteArray(MagickFormat.Jpeg);
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
        owner?.Dispose();
        reader?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
