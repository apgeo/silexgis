// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The read of several trips' replays in one request: what it answers about a trip is what that
/// trip's own reads answer the same caller, and nothing else.
/// </summary>
/// <remarks>
/// The callers are the owner — an Editor who created the caves and the trips, and so may place
/// the protected cave and read the private trip — and the reader, a Viewer with no grant on the
/// private trip and no placing right on the protected cave. Every absence asserted for the reader
/// is asserted beside the owner's presence, on the same request shape.
/// </remarks>
public sealed class SurveyModelTrackedTripReplayTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private HttpClient anonymous = null!;
    private long caveTypeId;

    public SurveyModelTrackedTripReplayTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            // Workers off: the graph-extraction job would otherwise pick up the fake survey file
            // below, fail to parse it, and rewrite the very station rows these tests seed.
            JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"smtr-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"smtr-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"smtr-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"smtr-read-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        try { Directory.Delete(filesRoot, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// Three trips read both ways by both callers. One is ordinary; one has a report anchored to a
    /// cave only the owner may place; one is private. For every trip in an answer, its state and
    /// its log are the very JSON the trip's own reads give that caller, its title and its people
    /// are the trip's own; for every trip left out, the trip's own reads refuse that caller.
    /// </summary>
    [Fact]
    public async Task Each_trip_is_answered_as_its_own_reads_answer_the_same_caller_and_an_unreadable_one_is_left_out()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        var guarded = await CreateCaveAsync(locationProtected: true);

        var (plain, plainCavers) = await CreateTripAsync("Plain", guests: 2);
        (await ArmAsync(owner, plain, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(plain, AtStation(plainCavers), At(9, 0));
        await ReportAsync(plain, new { caverIds = plainCavers.Take(1), kind = "note", note = "resting" }, At(9, 30));
        await ReportAsync(plain, AtStation([plainCavers[1]]), At(10, 0));

        var (partly, partlyCavers) = await CreateTripAsync("Partly withheld", guests: 1);
        (await ArmAsync(owner, partly, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(partly, AtStation(partlyCavers), At(10, 0));
        await ReportAsync(partly, AtStation(partlyCavers), At(11, 0));

        var (hidden, hiddenCavers) = await CreateTripAsync("Private", guests: 1, visibility: "private");
        (await ArmAsync(owner, hidden, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(hidden, AtStation(hiddenCavers), At(12, 0));

        // The later report of the second trip is anchored to a cave only the owner may place: the
        // per-row rule is evaluated against each row's own snapshot.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var late = At(11, 0);
            (await db.TripPositionEvents
                .Where(e => e.TripLogId == partly && e.RecordedAt == late)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.CaveFeatureId, (Guid?)guarded)))
                .ShouldBe(1);
        }

        // Asked out of creation order and with one id twice: the answer follows the asking, once each.
        Guid[] asked = [partly, plain, hidden, plain];

        var toOwner = await ReplayAsync(owner, model, asked);
        toOwner.Select(TripOf).ShouldBe([partly, plain, hidden]);
        var toReader = await ReplayAsync(reader, model, asked);
        toReader.Select(TripOf).ShouldBe([partly, plain]);

        foreach (var (client, answer) in new[] { (owner, toOwner), (reader, toReader) })
        {
            foreach (var trip in answer)
            {
                await ShouldEqualItsOwnReadsAsync(client, trip);
            }
        }

        // What the equality rests on, said outright, so that two reads agreeing on nothing at all
        // would not pass: the owner is told the place of the anchored report and the reader is not,
        // on the log and on the folded state alike.
        var ownersLate = EventAt(toOwner[0], At(11, 0));
        ownersLate.GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
        ownersLate.GetProperty("surveyModelId").GetGuid().ShouldBe(model);
        toOwner[0].GetProperty("tracking").GetProperty("positionsWithheld").GetBoolean().ShouldBeFalse();

        var readersLate = EventAt(toReader[0], At(11, 0));
        readersLate.GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
        readersLate.GetProperty("surveyModelId").ValueKind.ShouldBe(JsonValueKind.Null);
        toReader[0].GetProperty("tracking").GetProperty("positionsWithheld").GetBoolean().ShouldBeTrue();
        // The report before it, in the cave the reader may place, is told to them in full.
        EventAt(toReader[0], At(10, 0)).GetProperty("stationName").GetString().ShouldBe("cave.upper.2");

        // The trip left out of the reader's answer is one its own reads refuse them, and answer the owner.
        (await reader.GetAsync($"/api/v1/trip-logs/{hidden}/tracking")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/trip-logs/{hidden}/tracking/events")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/trip-logs/{hidden}/tracking")).StatusCode.ShouldBe(HttpStatusCode.OK);
        // Asked for alone, it is an empty answer and not a refusal: nothing says why it is absent.
        (await ReplayAsync(reader, model, [hidden])).ShouldBeEmpty();
        (await ReplayAsync(reader, model, [Guid.NewGuid()])).ShouldBeEmpty();
    }

    /// <summary>
    /// A report taken off the log is in no answer until it is put back, and a deleted trip is in no
    /// answer at all — to the owner, who could read both a moment before.
    /// </summary>
    [Fact]
    public async Task A_removed_report_and_a_deleted_trip_are_absent_from_the_answer()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);

        var (kept, keptCavers) = await CreateTripAsync("Kept", guests: 1);
        (await ArmAsync(owner, kept, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(kept, AtStation(keptCavers), At(9, 0));
        var placed = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{kept}/tracking/events", AtStation(keptCavers));
        placed.StatusCode.ShouldBe(HttpStatusCode.OK, await placed.Content.ReadAsStringAsync());
        var removedId = (await BodyAsync(placed))[0].GetProperty("id").GetGuid();

        var (gone, goneCavers) = await CreateTripAsync("Deleted", guests: 1);
        (await ArmAsync(owner, gone, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(gone, AtStation(goneCavers), At(10, 0));

        var before = await ReplayAsync(owner, model, [kept, gone]);
        before.Select(TripOf).ShouldBe([kept, gone]);
        EventIds(before[0]).Count.ShouldBe(2);
        EventIds(before[0]).ShouldContain(removedId);

        (await owner.DeleteAsync($"/api/v1/trip-logs/{kept}/tracking/events/{removedId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.DeleteAsync($"/api/v1/trip-logs/{gone}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = (await ReplayAsync(owner, model, [kept, gone])).ShouldHaveSingleItem();
        TripOf(after).ShouldBe(kept);
        EventIds(after).ShouldHaveSingleItem().ShouldNotBe(removedId);
        after.GetProperty("eventsComplete").GetBoolean().ShouldBeTrue();
        await ShouldEqualItsOwnReadsAsync(owner, after);

        (await owner.PostAsync($"/api/v1/trip-logs/{kept}/tracking/events/{removedId}/restore", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        EventIds((await ReplayAsync(owner, model, [kept])).ShouldHaveSingleItem()).ShouldContain(removedId);
    }

    /// <summary>
    /// No trip named, more than fifty, and something that is not an id are each refused as a
    /// failed validation naming the member; fifty are answered.
    /// </summary>
    [Fact]
    public async Task No_trips_too_many_trips_and_a_malformed_id_are_each_refused_while_fifty_are_answered()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        var url = $"/api/v1/survey-models/{model}/tracked-trips/replay";

        string Ids(int count) => string.Join('&', Enumerable.Range(0, count).Select(_ => $"tripLogIds={Guid.NewGuid()}"));

        var fifty = await owner.GetAsync($"{url}?{Ids(50)}");
        fifty.StatusCode.ShouldBe(HttpStatusCode.OK, await fifty.Content.ReadAsStringAsync());

        foreach (var refused in new[] { url, $"{url}?{Ids(51)}", $"{url}?tripLogIds=not-an-id", $"{url}?{Ids(2)}&tripLogIds=" })
        {
            var response = await owner.GetAsync(refused);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, refused);
            var problem = await BodyAsync(response);
            problem.GetProperty("code").GetString().ShouldBe("validation.failed");
            problem.GetProperty("errors").GetProperty("tripLogIds").GetArrayLength().ShouldBe(1, refused);
        }
    }

    /// <summary>
    /// Nobody without an account is answered; an unknown model, and a model of a cave the reader
    /// may not place, are the same 404 — while the owner is answered on that very model.
    /// </summary>
    [Fact]
    public async Task No_account_is_401_and_a_model_the_caller_may_not_place_is_the_unknown_models_404()
    {
        var guarded = await CreateCaveAsync(locationProtected: true);
        var model = await SeedModelWithStationsAsync(guarded);
        var (trip, cavers) = await CreateTripAsync("On a protected cave", guests: 1);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(trip, AtStation(cavers), At(9, 0));
        var url = $"/api/v1/survey-models/{model}/tracked-trips/replay?tripLogIds={trip}";

        TripOf((await ReplayAsync(owner, model, [trip])).ShouldHaveSingleItem()).ShouldBe(trip);

        (await anonymous.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var closed = await reader.GetAsync(url);
        closed.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var unknown = await owner.GetAsync(
            $"/api/v1/survey-models/{Guid.NewGuid()}/tracked-trips/replay?tripLogIds={trip}");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await BodyAsync(closed)).GetProperty("code").GetString().ShouldBe("survey_model.not_found");
        (await BodyAsync(unknown)).GetProperty("code").GetString().ShouldBe("survey_model.not_found");

        // The reader may read the trip itself, and is told no place on it there either.
        var own = await reader.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events");
        own.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BodyAsync(own)).GetProperty("items")[0].GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>
    /// A log longer than one answer carries is flagged, with its newest reports and no more; a log
    /// exactly as long as the bound is whole.
    /// </summary>
    [Fact]
    public async Task A_log_longer_than_one_answer_carries_is_flagged_incomplete()
    {
        const int bound = 10_000;
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        var (trip, cavers) = await CreateTripAsync("Very long", guests: 1);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(trip, AtStation(cavers), At(9, 0));

        // Notes written straight into the log, a second apart, up to the bound.
        async Task AddNotesAsync(int from, int count)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.TripPositionEvents.AddRange(Enumerable.Range(from, count).Select(i => new TripPositionEvent
            {
                TripLogId = trip,
                CaverId = cavers[0],
                Kind = TripPositionEventKind.Note,
                Note = "n",
                RecordedAt = At(9, 0).AddSeconds(i),
            }));
            await db.SaveChangesAsync();
        }

        await AddNotesAsync(1, bound - 1);
        var whole = (await ReplayAsync(owner, model, [trip])).ShouldHaveSingleItem();
        whole.GetProperty("eventsComplete").GetBoolean().ShouldBeTrue();
        whole.GetProperty("events").GetArrayLength().ShouldBe(bound);

        await AddNotesAsync(bound, 1);
        var cut = (await ReplayAsync(owner, model, [trip])).ShouldHaveSingleItem();
        cut.GetProperty("eventsComplete").GetBoolean().ShouldBeFalse();
        cut.GetProperty("events").GetArrayLength().ShouldBe(bound);
        // Newest first: what is missing is the oldest report, the only one with a place.
        cut.GetProperty("events")[0].GetProperty("recordedAt").GetDateTimeOffset().ShouldBe(At(9, 0).AddSeconds(bound));
        cut.GetProperty("events").EnumerateArray()
            .ShouldAllBe(e => e.GetProperty("stationName").ValueKind == JsonValueKind.Null);
    }

    // ---- helpers -----------------------------------------------------------------------------

    private static object AtStation(IEnumerable<Guid> cavers) =>
        new { caverIds = cavers, kind = "atStation", stationName = "cave.upper.2" };

    private static Guid TripOf(JsonElement row) => row.GetProperty("tripLogId").GetGuid();

    private static List<Guid> EventIds(JsonElement trip) =>
        [.. trip.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("id").GetGuid())];

    private static JsonElement EventAt(JsonElement trip, DateTimeOffset recordedAt) =>
        trip.GetProperty("events").EnumerateArray()
            .Single(e => e.GetProperty("recordedAt").GetDateTimeOffset() == recordedAt);

    private static async Task<List<JsonElement>> ReplayAsync(HttpClient client, Guid model, IEnumerable<Guid> trips)
    {
        var query = string.Join('&', trips.Select(id => $"tripLogIds={id}"));
        var response = await client.GetAsync($"/api/v1/survey-models/{model}/tracked-trips/replay?{query}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return [.. (await BodyAsync(response)).EnumerateArray()];
    }

    /// <summary>
    /// One trip of an answer against the three reads it stands for, as the same caller: the state
    /// and the log member for member, the title, and the roster as a set of person and name.
    /// </summary>
    private static async Task ShouldEqualItsOwnReadsAsync(HttpClient client, JsonElement answered)
    {
        var trip = TripOf(answered);

        var state = await client.GetAsync($"/api/v1/trip-logs/{trip}/tracking");
        state.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldBeTheSameJson(answered.GetProperty("tracking"), await BodyAsync(state), $"the state of {trip}");

        var log = await client.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events?pageSize=500");
        log.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await BodyAsync(log);
        page.GetProperty("totalItems").GetInt32().ShouldBe(page.GetProperty("items").GetArrayLength());
        page.GetProperty("items").GetArrayLength().ShouldBeGreaterThan(0);
        ShouldBeTheSameJson(answered.GetProperty("events"), page.GetProperty("items"), $"the log of {trip}");
        answered.GetProperty("eventsComplete").GetBoolean().ShouldBeTrue();

        var own = await client.GetAsync($"/api/v1/trip-logs/{trip}");
        own.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await BodyAsync(own);
        answered.GetProperty("title").GetString().ShouldBe(body.GetProperty("title").GetString());
        var people = body.GetProperty("participants").EnumerateArray()
            .Select(p => (p.GetProperty("caverId").GetGuid(), p.GetProperty("name").GetString()))
            .Distinct().OrderBy(p => p.Item1).ToList();
        people.ShouldNotBeEmpty();
        people.ShouldAllBe(p => !string.IsNullOrEmpty(p.Item2));
        answered.GetProperty("participants").EnumerateArray()
            .Select(p => (p.GetProperty("caverId").GetGuid(), p.GetProperty("name").GetString()))
            .OrderBy(p => p.Item1).ToList()
            .ShouldBe(people);
    }

    private static void ShouldBeTheSameJson(JsonElement actual, JsonElement expected, string what) =>
        JsonNode.DeepEquals(JsonNode.Parse(actual.GetRawText()), JsonNode.Parse(expected.GetRawText()))
            .ShouldBeTrue($"{what} differs between the two reads");

    private async Task<(Guid Trip, List<Guid> Cavers)> CreateTripAsync(
        string title, int guests, string visibility = "authenticated")
    {
        var participants = Enumerable.Range(1, guests)
            .Select(i => new { newCaverName = $"Guest {i} {Guid.NewGuid():N}"[..24] })
            .ToArray();
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            tripDate = "2026-09-12",
            participants,
            visibility,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var trip = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var cavers = await db.TripLogParticipants.Where(p => p.TripLogId == trip)
            .Select(p => p.CaverId).Distinct().OrderBy(c => c).ToListAsync();
        cavers.Count.ShouldBe(guests);
        return (trip, cavers);
    }

    private async Task<Guid> CreateCaveAsync(bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Replay Cave {Guid.NewGuid():N}"[..30],
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
    /// A survey model created the real way (upload), marked ready, with a few stations seeded
    /// straight into the graph tables — the extraction job never runs here.
    /// </summary>
    private async Task<Guid> SeedModelWithStationsAsync(Guid caveId)
    {
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new("application/octet-stream");
        form.Add(bytes, "file", "tracking.3d");
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var modelId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.SingleAsync(m => m.Id == modelId);
        model.Status = SurveyModelStatus.Ready;
        db.SurveyStations.AddRange(
            Station(modelId, "cave.ent.0", "cave.ent", 350, SurveyStationFlags.Entrance),
            Station(modelId, "cave.upper.2", "cave.upper", 300, SurveyStationFlags.Underground));
        await db.SaveChangesAsync();
        return modelId;
    }

    private static SurveyStation Station(Guid modelId, string name, string survey, double z, SurveyStationFlags flags) =>
        new()
        {
            SurveyModelId = modelId,
            Name = name,
            SurveyName = survey,
            Position = new Point(new CoordinateZ(25.5, 45.5, z)) { SRID = 4326 },
            Flags = flags,
        };

    /// <summary>Config writes ride the trip's version: fetch the ETag, then PUT with If-Match.</summary>
    private static async Task<HttpResponseMessage> ArmAsync(HttpClient client, Guid trip, Guid model)
    {
        var current = await client.GetAsync($"/api/v1/trip-logs/{trip}/tracking");
        current.StatusCode.ShouldBe(HttpStatusCode.OK, await current.Content.ReadAsStringAsync());
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/trip-logs/{trip}/tracking")
        {
            Content = JsonContent.Create(new { state = "armed", surveyModelId = model }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", current.Headers.ETag!.ToString());
        return await client.SendAsync(request);
    }

    private static DateTimeOffset At(int hour, int minute) =>
        new(2026, 9, 12, hour, minute, 0, TimeSpan.Zero);

    /// <summary>One report, stamped with the hour the reporter gave, asserted to have landed.</summary>
    private async Task ReportAsync(Guid trip, object body, DateTimeOffset recordedAt)
    {
        var json = JsonSerializer.SerializeToNode(body)!.AsObject();
        json["recordedAt"] = recordedAt.ToString("O");
        var response = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", json);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
