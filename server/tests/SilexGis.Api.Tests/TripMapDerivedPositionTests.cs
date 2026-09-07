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
/// Most trips carry no shape of their own. A trip read out of a club's spreadsheet records which
/// cave, never where, so the only thing that can put it on a map is a position borrowed from a
/// cave it names — and a borrowed position is a cave's position, handed to somebody who asked
/// about a trip. These cases pin the four answers that has to give.
/// </summary>
/// <remarks>
/// <para>
/// Every case is stated over the map response for a <em>Viewer</em>, deliberately. The seeded
/// Editors group reads past visibility at the widest scope by design, so a case built on an
/// Editor being refused would prove nothing about the rule.
/// </para>
/// <para>
/// And every case asserts a positive alongside its negative, over one fixture: a layer that had
/// stopped answering with anything at all satisfies a bare "is not there" perfectly. The
/// positive half is what makes the missing half a refusal rather than an empty database.
/// </para>
/// </remarks>
public sealed class TripMapDerivedPositionTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    // This suite's own corner of the world and its own day. The database is shared between
    // suites, and both the unlocated figure and the truncation flag are counts over everything
    // the caller may read in the window — so the window has to belong to this suite alone or the
    // numbers below are somebody else's fixtures as much as they are these.
    private const double Lat = 46.71834;
    private const double Lon = 22.83951;
    // A day of its own per case, and that is not tidiness. Both figures this suite asserts —
    // how many trips have no position, and whether the cap was reached — are counts over
    // everything the caller may read in the window, so two cases sharing a day would each be
    // counting the other's fixtures and would pass or fail depending on which ran first.
    private const string PlacedDay = "2033-05-11";
    private const string UnplaceableDay = "2033-05-12";
    private const string UnreadableDay = "2033-05-13";
    private const string NowhereDay = "2033-05-14";
    private const string CappedDay = "2033-05-15";

    // Wide enough to hold every cave this suite places, small enough to exclude the rest of the
    // world — so "in this viewport" is a claim the fixture actually controls.
    private const string NearBbox = "22.5,46.5,23.2,47.0";
    private const string FarBbox = "10.0,40.0,10.5,40.5";

    private readonly SilexGisApiFactory factory;
    private readonly string connectionString;

    private HttpClient owner = null!;  // Editor — creates the caves and the trips, may place them
    private HttpClient reader = null!; // Viewer — may read a trip, may not place a guarded cave
    private long caveTypeId;
    private long entranceTypeId;

    public TripMapDerivedPositionTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        factory = new SilexGisApiFactory(connectionString);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tmd-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"tmd-own-{suffix}@t.local");

        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tmd-read-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"tmd-read-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
    }

    /// <summary>
    /// A cave this caller may both read and place puts the trip that names it on the map, at the
    /// cave's entrance, labelled as the borrowed position it is.
    /// </summary>
    [Fact]
    public async Task A_readable_and_placeable_cave_puts_its_trip_on_the_map()
    {
        var cave = await CreateCaveAsync(locationProtected: false, visibility: "authenticated");
        await AddEntranceAsync(cave.Id, Lon, Lat);
        var tripId = await CreateTripAsync(PlacedDay, caveIds: [cave.Id]);

        // The fixture, stated rather than assumed: this reader genuinely opens both the trip and
        // the cave, so what follows is the position rule and nothing else.
        (await reader.GetAsync($"/api/v1/trip-logs/{tripId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/caves/{cave.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var collection = await MapAsync(reader, NearBbox, PlacedDay);
        var mine = FeaturesOf(collection, tripId);
        mine.Count.ShouldBe(1);

        var properties = mine[0].GetProperty("properties");
        properties.GetProperty("kind").GetString().ShouldBe("cave");
        properties.GetProperty("caveId").GetGuid().ShouldBe(cave.Id);
        properties.GetProperty("caveName").GetString().ShouldBe(cave.Name);

        // Exactly where the entrance is, not a grid square near it: a borrowed position is either
        // handed over or it is not, and a rounded one would be read as where the trip went.
        var coordinates = mine[0].GetProperty("geometry").GetProperty("coordinates");
        coordinates[0].GetDouble().ShouldBe(Lon, 1e-6);
        coordinates[1].GetDouble().ShouldBe(Lat, 1e-6);

        // Placed, therefore not counted as unlocated — the two are the same question answered once.
        collection.GetProperty("unlocatedCount").GetInt32().ShouldBe(0);

        // And it is the viewport's answer rather than the window's: the same trip is not returned
        // for a viewport the cave is nowhere near.
        FeaturesOf(await MapAsync(reader, FarBbox, PlacedDay), tripId).ShouldBeEmpty();
    }

    /// <summary>
    /// A cave this caller may read but may not place exactly contributes nothing at all — no
    /// snapped point, no blurred one, no dot.
    /// </summary>
    /// <remarks>
    /// This is the uncomfortable half and the reason the whole third position source needs a
    /// suite. Elsewhere on the map a protected point is snapped to a grid and still drawn, which
    /// is honest there because the layer says what it is showing. It would not be honest here: a
    /// reader looking at a trip's dot reads it as where that trip went, and a cave rounded to the
    /// nearest grid square is still a cave placed to within the grid by a response that was
    /// refusing to place it. So the answer is nothing, and the trip is reported as unlocated
    /// exactly as though the cave had never been named.
    /// </remarks>
    [Fact]
    public async Task A_readable_but_unplaceable_cave_contributes_no_dot_at_all()
    {
        var guarded = await CreateCaveAsync(locationProtected: true, visibility: "authenticated");
        await AddEntranceAsync(guarded.Id, Lon + 0.01, Lat + 0.01);
        var guardedTrip = await CreateTripAsync(UnplaceableDay, caveIds: [guarded.Id]);

        var open = await CreateCaveAsync(locationProtected: false, visibility: "authenticated");
        await AddEntranceAsync(open.Id, Lon, Lat);
        var openTrip = await CreateTripAsync(UnplaceableDay, caveIds: [open.Id]);

        // The fixture: the reader really does open the guarded cave — this is a placement refusal
        // and not a readability one — and the creator really can place it, which is what makes the
        // reader's empty answer a redaction rather than a cave nobody ever gave a position to.
        (await reader.GetAsync($"/api/v1/caves/{guarded.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        FeaturesOf(await MapAsync(owner, NearBbox, UnplaceableDay), guardedTrip).Count.ShouldBe(1);

        var collection = await MapAsync(reader, NearBbox, UnplaceableDay);

        // Nothing: not a dot elsewhere, not a dot rounded, not a dot at all.
        FeaturesOf(collection, guardedTrip).ShouldBeEmpty();

        // The whole world, so this is "nowhere" rather than "not in this viewport".
        FeaturesOf(await MapAsync(reader, "-180,-90,180,90", UnplaceableDay), guardedTrip).ShouldBeEmpty();

        // The positive half over the same fixture: the unguarded cave still places its trip, so
        // the layer has not simply stopped answering.
        FeaturesOf(collection, openTrip).Count.ShouldBe(1);

        // And the refused trip is reported rather than dropped.
        collection.GetProperty("unlocatedCount").GetInt32().ShouldBe(1);
    }

    /// <summary>
    /// A cave this caller may not read at all is invisible, and its absence is not explained: the
    /// trip that names it is reported in exactly the same terms as a trip that names nothing.
    /// </summary>
    /// <remarks>
    /// An answer that distinguished "this trip names a cave you may not see" from "this trip names
    /// nowhere" would be a question anybody could ask of any trip until it said yes — and for a
    /// cave the yes is the one thing worth protecting. So the two collapse into one figure on
    /// purpose, and this case is what stops that collapse being optimised away later.
    /// </remarks>
    [Fact]
    public async Task A_cave_this_caller_may_not_read_is_invisible_and_its_absence_is_not_explained()
    {
        var hidden = await CreateCaveAsync(locationProtected: false, visibility: "private");
        await AddEntranceAsync(hidden.Id, Lon, Lat);
        var hiddenTrip = await CreateTripAsync(UnreadableDay, caveIds: [hidden.Id]);
        await CreateTripAsync(UnreadableDay, caveIds: []);

        // The fixture: the reader genuinely cannot open the cave, and genuinely can open the trip
        // that names it.
        (await reader.GetAsync($"/api/v1/caves/{hidden.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/trip-logs/{hiddenTrip}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The positive half: the creator, who may read the cave, sees the trip placed by it.
        FeaturesOf(await MapAsync(owner, NearBbox, UnreadableDay), hiddenTrip).Count.ShouldBe(1);

        var raw = await MapTextAsync(reader, NearBbox, UnreadableDay);
        var collection = JsonDocument.Parse(raw).RootElement;
        FeaturesOf(collection, hiddenTrip).ShouldBeEmpty();

        // Nowhere in the payload — the identifier is the whole of what a reader would need to go
        // and ask for the cave somewhere else, so withholding the name alone would withhold nothing.
        raw.ShouldNotContain(hidden.Id.ToString());
        raw.ShouldNotContain(hidden.Name);

        // Two trips, one naming a cave this reader may not see and one naming nothing, and the
        // response says the same thing about both: two with no position, and no word about why.
        collection.GetProperty("unlocatedCount").GetInt32().ShouldBe(2);
        collection.EnumerateObject().Select(p => p.Name).OrderBy(n => n)
            .ShouldBe(["features", "truncated", "type", "unlocatedCount"]);
    }

    /// <summary>
    /// A trip with no position anywhere — no shape, no meeting point, no cave — is counted rather
    /// than dropped.
    /// </summary>
    /// <remarks>
    /// The alternative this application refuses is an invented one: the legacy program resolved
    /// every unrecognised place name to a single fixed point, and a screenful of trips at that
    /// point reads as a real cluster of real activity. A trip nobody recorded a position for is
    /// reported as having none, which is both true and useful — it is the sentence that tells a
    /// club its archive is missing something.
    /// </remarks>
    [Fact]
    public async Task A_trip_with_no_position_anywhere_is_counted_rather_than_dropped()
    {
        var placedCave = await CreateCaveAsync(locationProtected: false, visibility: "authenticated");
        await AddEntranceAsync(placedCave.Id, Lon, Lat);
        var placedTrip = await CreateTripAsync(NowhereDay, caveIds: [placedCave.Id]);

        var nowhereTrip = await CreateTripAsync(NowhereDay, caveIds: []);

        // A cave that exists and is perfectly readable but that nobody has ever surveyed an
        // entrance for. It reaches the same answer as naming no cave at all, and it must: the
        // trip is still in no place anybody could draw.
        var unsurveyed = await CreateCaveAsync(locationProtected: false, visibility: "authenticated");
        var unsurveyedTrip = await CreateTripAsync(NowhereDay, caveIds: [unsurveyed.Id]);

        var collection = await MapAsync(reader, NearBbox, NowhereDay);

        FeaturesOf(collection, nowhereTrip).ShouldBeEmpty();
        FeaturesOf(collection, unsurveyedTrip).ShouldBeEmpty();

        // The positive half: the trip that does have a cave to borrow from is drawn, so the two
        // above are absent by the rule rather than because nothing works.
        FeaturesOf(collection, placedTrip).Count.ShouldBe(1);

        collection.GetProperty("unlocatedCount").GetInt32().ShouldBe(2);
        collection.GetProperty("truncated").GetBoolean().ShouldBeFalse();
    }

    /// <summary>
    /// The truncation flag counts trips and not the shapes they state, so a trip that draws both
    /// a route and a meeting point does not push a complete answer over the cap on its own.
    /// </summary>
    /// <remarks>
    /// The cap is applied to rows and one row answers with up to two features, so a client
    /// comparing a feature count against the published cap gets the wrong answer whenever a trip
    /// states both of its own positions — and it gets it in the direction that matters, calling a
    /// complete map truncated. Which is why the server says so rather than leaving the arithmetic
    /// to the layer.
    /// </remarks>
    [Fact]
    public async Task Truncation_is_counted_in_trips_and_not_in_the_shapes_they_state()
    {
        using var capped = new SilexGisApiFactory(
            connectionString, new Dictionary<string, string?> { ["Map:MaxPoints"] = "2" });
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(capped, GlobalRoles.Editor, $"tmd-cap-{suffix}@t.local");
        using var author = await AuthHelper.BearerClientAsync(capped, $"tmd-cap-{suffix}@t.local");

        // One trip, both of its own positions: two features out of one row. A cap of two would
        // read as reached if features were what was counted.
        var both = await CreateTripAsync(
            CappedDay,
            client: author,
            caveIds: [],
            geom: new { type = "Point", coordinates = new[] { Lon, Lat } },
            meetingGeom: new { type = "Point", coordinates = new[] { Lon + 0.02, Lat + 0.02 } });

        var one = await MapAsync(author, NearBbox, CappedDay);
        FeaturesOf(one, both).Count.ShouldBe(2);
        one.GetProperty("truncated").GetBoolean().ShouldBeFalse();

        // Two more rows take the row count past the cap, and now it is reached.
        await CreateTripAsync(CappedDay, client: author, caveIds: [], geom: new { type = "Point", coordinates = new[] { Lon, Lat } });
        await CreateTripAsync(CappedDay, client: author, caveIds: [], geom: new { type = "Point", coordinates = new[] { Lon, Lat } });

        (await MapAsync(author, NearBbox, CappedDay)).GetProperty("truncated").GetBoolean().ShouldBeTrue();
    }

    /// <summary>
    /// A narrowing the layer does not have a word for is refused with a code, never dropped: a
    /// layer that quietly ignored half a filter would draw the answer to a question nobody asked.
    /// </summary>
    [Fact]
    public async Task An_unknown_filter_word_is_refused_with_a_code()
    {
        var response = await reader.GetAsync(
            $"/api/v1/map/trip-logs?bbox={NearBbox}&from={PlacedDay}&to={PlacedDay}&states=nonsense");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("code").GetString().ShouldBe("map.invalid_trip_filter");
    }

    /// <summary>The layer answers nobody who has not signed in.</summary>
    [Fact]
    public async Task The_layer_refuses_a_caller_who_is_not_signed_in()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/map/trip-logs?bbox={NearBbox}&from={PlacedDay}&to={PlacedDay}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<JsonElement> MapAsync(HttpClient client, string bbox, string day) =>
        JsonDocument.Parse(await MapTextAsync(client, bbox, day)).RootElement;

    private static async Task<string> MapTextAsync(HttpClient client, string bbox, string day)
    {
        var response = await client.GetAsync($"/api/v1/map/trip-logs?bbox={bbox}&from={day}&to={day}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return payload;
    }

    private static List<JsonElement> FeaturesOf(JsonElement collection, Guid tripId) =>
        [.. collection.GetProperty("features").EnumerateArray()
            .Where(f => f.GetProperty("properties").GetProperty("id").GetGuid() == tripId)];

    private async Task<(Guid Id, string Name)> CreateCaveAsync(bool locationProtected, string visibility)
    {
        var name = $"Derived {Guid.NewGuid():N}"[..24];
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return (JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid(), name);
    }

    /// <summary>The cave's position: it hangs on an entrance, not on the cave row.</summary>
    private async Task AddEntranceAsync(Guid caveId, double lon, double lat)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            positionQuality = "Gps",
        });
        response.StatusCode.ShouldBe(
            HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> CreateTripAsync(
        string day,
        Guid[] caveIds,
        HttpClient? client = null,
        object? geom = null,
        object? meetingGeom = null)
    {
        var response = await (client ?? owner).PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Derived trip {Guid.NewGuid():N}"[..28],
            tripDate = day,
            caveIds,
            participants = Array.Empty<object>(),
            // Readable by every account here, so the Viewer below genuinely reads the trip — the
            // only state on which a rule about the trip's caves can be proved at all.
            visibility = "authenticated",
            geom,
            meetingGeom,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        reader?.Dispose();
        factory.Dispose();
    }
}
