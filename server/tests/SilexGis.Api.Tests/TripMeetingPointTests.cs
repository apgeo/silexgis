// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Where a trip's party gathers is a second position on the trip row, and a second position is a
/// second way of placing something. These cases pin the three questions that answers: that the
/// column and its spatial index really are in the database a fresh installation builds, that a
/// shape written into it comes back as it went in, and — the one that matters — which callers are
/// told it.
/// </summary>
/// <remarks>
/// The last of those is the reason this suite exists rather than a line in an existing one. A
/// meeting point stands where people actually park, which can be a few hundred metres from an
/// entrance nobody meant this reader to be able to find; a trip already refuses to name the caves
/// a reader may not open, so a position that arrives anyway is that refusal undone by a different
/// door. Every surface that emits it is asked here, not only the trip's own reading, because the
/// trip's own reading is the one door that was designed to be right.
/// </remarks>
public sealed class TripMeetingPointTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string WorldBbox = "-180,-90,180,90";

    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;    // Editor — creates the caves and the trips
    private HttpClient outsider = null!; // Viewer — holds nothing anywhere
    private long caveTypeId;

    public TripMeetingPointTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tmp-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"tmp-own-{suffix}@t.local");

        // A Viewer, deliberately. The seeded Editors group reads past visibility at the widest
        // scope by design, so a case built on an Editor being refused would prove nothing.
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tmp-out-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"tmp-out-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    /// <summary>
    /// The migration reached the database this suite is running against, and it brought the
    /// spatial index with it. Asked of the catalogue rather than of the model, because the model
    /// is what generated the migration and would agree with itself either way — what is being
    /// checked is that a database built from the migrations has somewhere to store the position
    /// and a way to find it by window.
    /// </summary>
    [Fact]
    public async Task The_meeting_point_column_and_its_spatial_index_exist_in_a_fresh_database()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var geometryColumns = await db.Database
            .SqlQuery<string>($"""
                SELECT f_geometry_column AS "Value"
                FROM geometry_columns
                WHERE f_table_name = 'trip_logs' AND srid = 4326 AND type = 'GEOMETRY'
                """)
            .ToListAsync();
        // Beside the trip's own sketch, and declared identically: same class, same reference
        // system, both nullable. One rule about what a trip's geometry is, not two.
        geometryColumns.ShouldContain("meeting_geom");
        geometryColumns.ShouldContain("geom");

        var index = await db.Database
            .SqlQuery<string>($"""
                SELECT indexdef AS "Value"
                FROM pg_indexes
                WHERE tablename = 'trip_logs' AND indexname = 'ix_trip_logs_meeting_geom'
                """)
            .SingleAsync();
        index.ShouldContain("USING gist");
        index.ShouldContain("meeting_geom");
    }

    /// <summary>
    /// A point and a line, because the column accepts any class and a club that draws the
    /// approach as well as the gathering place records both in it. Written on creation, changed
    /// on a save, and cleared by a save that states none — the same three answers the trip's own
    /// sketch gives, since a surface drawing one of them and not the other is exactly how a
    /// position gets silently erased.
    /// </summary>
    [Fact]
    public async Task A_meeting_point_round_trips_as_a_point_and_as_a_line()
    {
        var point = new { type = "Point", coordinates = new[] { 25.61234, 45.55678 } };
        var line = new
        {
            type = "LineString",
            coordinates = new[] { new[] { 25.61234, 45.55678 }, new[] { 25.61999, 45.56111 } },
        };

        var tripId = await CreateTripAsync("Meeting point", meetingGeom: point);

        var created = await ReadJsonAsync(await owner.GetAsync($"/api/v1/trip-logs/{tripId}"));
        var storedPoint = created.GetProperty("meetingGeom");
        storedPoint.GetProperty("type").GetString().ShouldBe("Point");
        storedPoint.GetProperty("coordinates")[0].GetDouble().ShouldBe(25.61234, 1e-9);
        storedPoint.GetProperty("coordinates")[1].GetDouble().ShouldBe(45.55678, 1e-9);

        await SaveAsync(tripId, "Meeting point", meetingGeom: line);
        var withLine = await ReadJsonAsync(await owner.GetAsync($"/api/v1/trip-logs/{tripId}"));
        withLine.GetProperty("meetingGeom").GetProperty("type").GetString().ShouldBe("LineString");
        withLine.GetProperty("meetingGeom").GetProperty("coordinates").GetArrayLength().ShouldBe(2);

        // A save that states no meeting point clears it. That is the same reading the sketch
        // gets, and it is why a surface that draws the trip whole has to send this field back
        // whether or not it drew it.
        await SaveAsync(tripId, "Meeting point", meetingGeom: null);
        var cleared = await ReadJsonAsync(await owner.GetAsync($"/api/v1/trip-logs/{tripId}"));
        cleared.GetProperty("meetingGeom").ValueKind.ShouldBe(JsonValueKind.Null);

        // Malformed geometry is refused with the code the sketch's own malformed shapes get.
        var bad = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = "Bad meeting point",
            tripDate = "2026-07-01",
            caveIds = Array.Empty<Guid>(),
            participants = Array.Empty<object>(),
            visibility = "authenticated",
            meetingGeom = new { type = "Point", coordinates = new[] { 25.6 } },
        });
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await bad.Content.ReadAsStringAsync());
        (await bad.Content.ReadAsStringAsync()).ShouldContain("trip_log.geometry_invalid");
    }

    /// <summary>
    /// The disclosure case, asked of every door rather than of the trip's own reading alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The trip is private and names a cave that is private too, so the outsider fails both
    /// gates: they may not open the cave, and they may not read the trip that would have told
    /// them where to meet. The refusal is asserted on the map layer as well as on the trip,
    /// because the map is a separate query with its own filter and a column added to it without
    /// the filter would hand the position to everybody with a browser.
    /// </para>
    /// <para>
    /// The other half is asserted in the same case and is deliberately the uncomfortable one: a
    /// caller who <em>may</em> read the trip but may not open the cave it names is still told the
    /// meeting point, exactly as they are told the trip's own sketch. That is what a plan is for
    /// — the people asked on a trip have to know where to be — and it is a known cost rather than
    /// an oversight: a meeting point two hundred metres from a guarded entrance places that
    /// entrance for a reader the very same response is refusing to tell which caves are named.
    /// Pinned here so that narrowing it later is a decision somebody takes rather than a test
    /// that quietly starts failing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_meeting_point_reaches_every_reader_of_the_trip_and_no_door_beyond_them()
    {
        var meeting = new { type = "Point", coordinates = new[] { 25.71357, 45.61357 } };
        var hiddenCaveId = await CreateCaveAsync(visibility: "private");

        var hiddenTripId = await CreateTripAsync(
            "Hidden meeting", meetingGeom: meeting, visibility: "private", caveIds: [hiddenCaveId]);
        var openTripId = await CreateTripAsync(
            "Open meeting", meetingGeom: meeting, visibility: "authenticated", caveIds: [hiddenCaveId]);

        // Fixture proof: both meeting points really were stored, so a refusal below is the rule
        // speaking rather than an empty column, and neither trip carries a sketch — what the
        // outsider is not shown can only be the meeting point.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var trips = await db.TripLogs.AsNoTracking()
                .Where(t => t.Id == hiddenTripId || t.Id == openTripId)
                .ToListAsync();
            trips.Count.ShouldBe(2);
            trips.ShouldAllBe(t => t.MeetingGeom != null && t.Geom == null);
        }

        // The trip the outsider may not read tells them nothing at all, through either door.
        (await outsider.GetAsync($"/api/v1/trip-logs/{hiddenTripId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var outsiderMap = await ReadJsonAsync(
            await outsider.GetAsync($"/api/v1/map/trip-logs?bbox={WorldBbox}"));
        MapIds(outsiderMap).ShouldNotContain(hiddenTripId);

        // The positive half over the same fixture: the owner is shown it on both doors, so the
        // refusal above is this caller's rights and not a surface that emits nothing.
        var ownerTrip = await ReadJsonAsync(await owner.GetAsync($"/api/v1/trip-logs/{hiddenTripId}"));
        ownerTrip.GetProperty("meetingGeom").ValueKind.ShouldBe(JsonValueKind.Object);
        var ownerMap = await ReadJsonAsync(await owner.GetAsync($"/api/v1/map/trip-logs?bbox={WorldBbox}"));
        MapIds(ownerMap).ShouldContain(hiddenTripId);
        // The map says which of the two positions each feature is, because "where the trip went"
        // and "where it starts" are different answers and a map that drew them alike would put a
        // car park where the reader read a cave.
        MeetingFeatures(ownerMap, hiddenTripId).Count.ShouldBe(1);

        // The readable trip, to the same outsider: they are told the meeting point and are still
        // not told which caves the trip names. Both assertions belong to one case — the second
        // is what makes the first a cost rather than a coincidence.
        var readable = await ReadJsonAsync(await outsider.GetAsync($"/api/v1/trip-logs/{openTripId}"));
        readable.GetProperty("caveIds").GetArrayLength().ShouldBe(0);
        readable.GetProperty("cavesWithheld").GetInt32().ShouldBe(1);
        readable.GetProperty("meetingGeom").GetProperty("coordinates")[0]
            .GetDouble().ShouldBe(25.71357, 1e-9);
        MeetingFeatures(
            await ReadJsonAsync(await outsider.GetAsync($"/api/v1/map/trip-logs?bbox={WorldBbox}")),
            openTripId).Count.ShouldBe(1);
    }

    /// <summary>
    /// A window asks what is inside it, and the answer holds nothing else.
    /// </summary>
    /// <remarks>
    /// A trip is now in the window if either of its two positions is, which is what puts a trip
    /// stating only where its party meets on the map at all. The row matching is not the same
    /// question as the shape matching, though: a party that meets at a car park here and works a
    /// cave system a county away would otherwise answer a request for this window with that
    /// distant cave system, and a viewport-driven caller draws whatever it is handed as though it
    /// were in view.
    /// </remarks>
    [Fact]
    public async Task A_window_is_answered_with_the_shapes_inside_it_and_not_the_trip_s_other_one()
    {
        // The meeting point is in Braşov; the sketch is in the Apuseni, some 300 km west.
        var meeting = new { type = "Point", coordinates = new[] { 25.60000, 45.65000 } };
        var sketch = new { type = "Point", coordinates = new[] { 22.76314, 46.81953 } };
        var tripId = await CreateTripAsync("Meet here, work there", meetingGeom: meeting, geom: sketch);

        // Fixture proof: both shapes really are stored, so a missing feature below is the window
        // speaking rather than a column nobody wrote to.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var stored = await db.TripLogs.AsNoTracking().SingleAsync(t => t.Id == tripId);
            stored.MeetingGeom.ShouldNotBeNull();
            stored.Geom.ShouldNotBeNull();
        }

        // A window over Braşov alone: the trip is on the map, by the position that is in view.
        var near = await ReadJsonAsync(
            await owner.GetAsync("/api/v1/map/trip-logs?bbox=25.5,45.55,25.7,45.75"));
        var kinds = Kinds(near, tripId);
        kinds.ShouldBe(["meeting"]);

        // And the whole world holds both, which is what makes the answer above a filter rather
        // than a column that stopped being emitted.
        Kinds(await ReadJsonAsync(await owner.GetAsync($"/api/v1/map/trip-logs?bbox={WorldBbox}")), tripId)
            .OrderBy(k => k).ShouldBe(["meeting", "sketch"]);
    }

    private static List<string?> Kinds(JsonElement collection, Guid tripId) =>
        [.. collection.GetProperty("features").EnumerateArray()
            .Where(f => f.GetProperty("properties").GetProperty("id").GetGuid() == tripId)
            .Select(f => f.GetProperty("properties").GetProperty("kind").GetString())];

    private static List<Guid> MapIds(JsonElement collection) =>
        [.. collection.GetProperty("features").EnumerateArray()
            .Select(f => f.GetProperty("properties").GetProperty("id").GetGuid())];

    private static List<JsonElement> MeetingFeatures(JsonElement collection, Guid tripId) =>
        [.. collection.GetProperty("features").EnumerateArray().Where(f =>
            f.GetProperty("properties").GetProperty("id").GetGuid() == tripId
            && f.GetProperty("properties").GetProperty("kind").GetString() == "meeting")];

    private async Task<Guid> CreateCaveAsync(string visibility)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Meet {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility,
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateTripAsync(
        string title,
        object? meetingGeom,
        string visibility = "authenticated",
        Guid[]? caveIds = null,
        object? geom = null)
    {
        var response = await owner.PostAsJsonAsync(
            "/api/v1/trip-logs/", Body(title, meetingGeom, visibility, caveIds, geom));
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task SaveAsync(Guid tripId, string title, object? meetingGeom)
    {
        var response = await owner.PutWithIfMatchAsync(
            $"/api/v1/trip-logs/{tripId}", Body(title, meetingGeom, "authenticated", null, null));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static object Body(
        string title, object? meetingGeom, string visibility, Guid[]? caveIds, object? geom) => new
    {
        title,
        tripDate = "2026-07-01",
        caveIds = caveIds ?? [],
        participants = Array.Empty<object>(),
        visibility,
        meetingGeom,
        geom,
    };

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        outsider?.Dispose();
        factory.Dispose();
    }
}
