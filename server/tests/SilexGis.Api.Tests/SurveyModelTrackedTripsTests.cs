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
/// The trips tracked on one survey model, asked from the model's side. Every negative here is
/// built explicitly and sits beside its positive: the owner (an Editor who created the cave and
/// the trips) sees what the reader (a Viewer with no grant on the private trip and no placing
/// right on the protected cave) must not, in the same test — so an empty answer can only mean
/// the rule, never a broken query.
/// </summary>
public sealed class SurveyModelTrackedTripsTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private HttpClient anonymous = null!;
    private long caveTypeId;

    public SurveyModelTrackedTripsTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            // Workers off: the graph-extraction job would otherwise pick up the fake survey
            // file below, fail to parse it, and rewrite the very station rows these tests seed.
            JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"smtt-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"smtt-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"smtt-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"smtt-read-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        try { Directory.Delete(filesRoot, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// One trip watches the model throughout; the other reported on it and was then re-pointed at
    /// a newer survey of the same cave. Both are tracked on the older model — each with exactly
    /// the reports it made there — and only the re-pointed one is tracked on the newer.
    /// </summary>
    [Fact]
    public async Task A_watched_trip_and_a_re_pointed_one_are_both_listed_with_the_reports_each_made_on_the_model()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var older = await SeedModelWithStationsAsync(cave);
        var newer = await SeedModelWithStationsAsync(cave);

        var (watched, watchedCavers) = await CreateTripAsync("Watched", guests: 1);
        (await ArmAsync(owner, watched, older)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(watched, AtStation(watchedCavers), At(10, 0));
        await ReportAsync(watched, AtStation(watchedCavers), At(11, 0));

        var (moved, movedCavers) = await CreateTripAsync("Moved", guests: 1);
        (await ArmAsync(owner, moved, older)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(moved, AtStation(movedCavers), At(9, 0));
        (await ArmAsync(owner, moved, newer)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(moved, AtStation(movedCavers), At(12, 0));

        foreach (var client in new[] { owner, reader })
        {
            var onOlder = await TrackedAsync(client, older);
            onOlder.Select(TripOf).ShouldBe([watched, moved], "latest activity first");

            var first = onOlder[0];
            first.GetProperty("state").GetString().ShouldBe("armed");
            first.GetProperty("watchesThisModel").GetBoolean().ShouldBeTrue();
            first.GetProperty("reportCount").GetInt32().ShouldBe(2);
            TimeOf(first, "firstReportAt").ShouldBe(At(10, 0));
            TimeOf(first, "lastReportAt").ShouldBe(At(11, 0));
            TimeOf(first, "armedAt").ShouldNotBeNull();
            first.GetProperty("tripDate").GetString().ShouldBe("2026-09-12");
            first.GetProperty("title").GetString()!.ShouldStartWith("Watched");

            var second = onOlder[1];
            second.GetProperty("watchesThisModel").GetBoolean().ShouldBeFalse();
            second.GetProperty("reportCount").GetInt32().ShouldBe(1, "only the report made before the re-point");
            TimeOf(second, "firstReportAt").ShouldBe(At(9, 0));
            TimeOf(second, "lastReportAt").ShouldBe(At(9, 0));

            var onNewer = await TrackedAsync(client, newer);
            onNewer.Select(TripOf).ShouldBe([moved]);
            onNewer[0].GetProperty("watchesThisModel").GetBoolean().ShouldBeTrue();
            onNewer[0].GetProperty("reportCount").GetInt32().ShouldBe(1);
            TimeOf(onNewer[0], "lastReportAt").ShouldBe(At(12, 0));
        }
    }

    [Fact]
    public async Task No_account_is_401_and_an_unknown_model_is_404_while_a_real_one_answers()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);

        (await owner.GetAsync($"/api/v1/survey-models/{model}/tracked-trips")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync($"/api/v1/survey-models/{model}/tracked-trips"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var unknown = await owner.GetAsync($"/api/v1/survey-models/{Guid.NewGuid()}/tracked-trips");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unknown.Content.ReadAsStringAsync()).ShouldContain("survey_model.not_found");
    }

    /// <summary>
    /// The model is gated as its own read gates it. The reader may read the trip (its tracking
    /// state answers) and still gets the model's 404, word for word the unknown model's answer;
    /// the owner, who may place the cave, gets the trip.
    /// </summary>
    [Fact]
    public async Task A_model_of_a_cave_the_caller_may_not_place_answers_404_exactly_as_an_unknown_one()
    {
        var cave = await CreateCaveAsync(locationProtected: true);
        var model = await SeedModelWithStationsAsync(cave);
        var (trip, cavers) = await CreateTripAsync("Protected", guests: 1);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(trip, AtStation(cavers), At(10, 0));

        (await TrackedAsync(owner, model)).Select(TripOf).ShouldBe([trip]);
        (await owner.GetAsync($"/api/v1/survey-models/{model}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The state the negative rests on, asserted rather than assumed: the reader reads the trip,
        // and the model's own read refuses them.
        (await reader.GetAsync($"/api/v1/trip-logs/{trip}/tracking")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/survey-models/{model}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var refused = await reader.GetAsync($"/api/v1/survey-models/{model}/tracked-trips");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var unknown = await reader.GetAsync($"/api/v1/survey-models/{Guid.NewGuid()}/tracked-trips");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(refused)).ShouldBe(await ProblemCodeAsync(unknown));
        (await ProblemCodeAsync(refused)).ShouldBe("survey_model.not_found");
    }

    [Fact]
    public async Task A_trip_the_caller_may_not_read_is_absent_while_a_readable_one_is_listed()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);

        var (open, openCavers) = await CreateTripAsync("Readable", guests: 1, visibility: "authenticated");
        (await ArmAsync(owner, open, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(open, AtStation(openCavers), At(10, 0));

        var (hidden, hiddenCavers) = await CreateTripAsync("Private", guests: 1, visibility: "private");
        (await ArmAsync(owner, hidden, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(hidden, AtStation(hiddenCavers), At(11, 0));

        // The unreadable state, asserted: the reader has no grant on the private trip.
        (await reader.GetAsync($"/api/v1/trip-logs/{hidden}/tracking")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await TrackedAsync(owner, model)).Select(TripOf).ShouldBe([hidden, open]);
        (await TrackedAsync(reader, model)).Select(TripOf).ShouldBe([open]);
    }

    /// <summary>
    /// A watch configured on the model but never armed tracked nothing there. It sits beside an
    /// armed one with no reports yet, which is listed — so what keeps the first out is its state,
    /// not the missing reports.
    /// </summary>
    [Fact]
    public async Task An_off_watch_pointing_at_the_model_with_no_reports_is_absent_and_an_armed_one_is_not()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);

        var (off, _) = await CreateTripAsync("Off", guests: 1);
        (await PutConfigAsync(owner, off, new { state = "off", surveyModelId = model }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var (armed, _) = await CreateTripAsync("Armed", guests: 1);
        (await ArmAsync(owner, armed, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var row = await db.TripTrackings.SingleAsync(t => t.TripLogId == off);
            row.State.ShouldBe(TripTrackingState.Off);
            row.SurveyModelId.ShouldBe(model, "the off watch really does point at the model");
            (await db.TripPositionEvents.CountAsync(e => e.TripLogId == off)).ShouldBe(0);
        }

        var listed = await TrackedAsync(owner, model);
        listed.Select(TripOf).ShouldBe([armed]);
        listed[0].GetProperty("watchesThisModel").GetBoolean().ShouldBeTrue();
        listed[0].GetProperty("reportCount").GetInt32().ShouldBe(0);
        listed[0].GetProperty("firstReportAt").ValueKind.ShouldBe(JsonValueKind.Null);
        listed[0].GetProperty("lastReportAt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>
    /// The counts follow the event log's per-report withholding. A report whose cave snapshot is a
    /// cave the reader may not place is not counted for them and is for the owner; a trip whose only
    /// tie to the model is such a report is not listed to the reader at all, and is to the owner.
    /// </summary>
    [Fact]
    public async Task Reports_whose_place_is_withheld_are_neither_counted_nor_enough_to_list_a_trip()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        var newer = await SeedModelWithStationsAsync(cave);
        var guarded = await CreateCaveAsync(locationProtected: true);

        var (counted, countedCavers) = await CreateTripAsync("Counted", guests: 1);
        (await ArmAsync(owner, counted, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(counted, AtStation(countedCavers), At(10, 0));
        await ReportAsync(counted, AtStation(countedCavers), At(11, 0));

        var (tiedOnlyByWithheld, tiedCavers) = await CreateTripAsync("Withheld", guests: 1);
        (await ArmAsync(owner, tiedOnlyByWithheld, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(tiedOnlyByWithheld, AtStation(tiedCavers), At(9, 0));
        (await ArmAsync(owner, tiedOnlyByWithheld, newer)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Anchor the 11:00 report of the first trip and the only report of the second to a cave
        // only the owner may place — the per-row test is evaluated against each row's own snapshot.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var late = At(11, 0);
            (await db.TripPositionEvents
                .Where(e => (e.TripLogId == counted && e.RecordedAt == late) || e.TripLogId == tiedOnlyByWithheld)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.CaveFeatureId, (Guid?)guarded)))
                .ShouldBe(2);
        }
        // And the reader really is refused that cave's positions, by the event log itself.
        var log = await BodyAsync(await reader.GetAsync($"/api/v1/trip-logs/{tiedOnlyByWithheld}/tracking/events"));
        log.GetProperty("items").EnumerateArray().Single().GetProperty("surveyModelId").ValueKind
            .ShouldBe(JsonValueKind.Null);

        var mine = await TrackedAsync(owner, model);
        mine.Select(TripOf).ShouldBe([counted, tiedOnlyByWithheld]);
        mine[0].GetProperty("reportCount").GetInt32().ShouldBe(2);
        TimeOf(mine[0], "lastReportAt").ShouldBe(At(11, 0));
        mine[1].GetProperty("reportCount").GetInt32().ShouldBe(1);

        var theirs = await TrackedAsync(reader, model);
        theirs.Select(TripOf).ShouldBe([counted]);
        theirs[0].GetProperty("reportCount").GetInt32().ShouldBe(1);
        TimeOf(theirs[0], "firstReportAt").ShouldBe(At(10, 0));
        TimeOf(theirs[0], "lastReportAt").ShouldBe(At(10, 0), "the withheld 11:00 report moves nothing");
    }

    /// <summary>
    /// Latest activity first: the last counted report, else the moment the watch was armed. The
    /// armed-only trip was armed now, later than any report dated on the trip's day.
    /// </summary>
    [Fact]
    public async Task Trips_are_ordered_by_latest_activity_first()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);

        var (early, earlyCavers) = await CreateTripAsync("Early", guests: 1);
        (await ArmAsync(owner, early, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(early, AtStation(earlyCavers), At(8, 0));

        var (late, lateCavers) = await CreateTripAsync("Late", guests: 1);
        (await ArmAsync(owner, late, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(late, AtStation(lateCavers), At(15, 0));

        var (quiet, _) = await CreateTripAsync("Quiet", guests: 1);
        (await ArmAsync(owner, quiet, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var listed = await TrackedAsync(owner, model);
        listed.Select(TripOf).ShouldBe([quiet, late, early]);
        TimeOf(listed[0], "armedAt")!.Value.ShouldBeGreaterThan(At(15, 0));
    }

    // ---- helpers -----------------------------------------------------------------------------

    private static object AtStation(List<Guid> cavers) =>
        new { caverIds = cavers, kind = "atStation", stationName = "cave.upper.2" };

    private static Guid TripOf(JsonElement row) => row.GetProperty("tripLogId").GetGuid();

    private static async Task<List<JsonElement>> TrackedAsync(HttpClient client, Guid model)
    {
        var response = await client.GetAsync($"/api/v1/survey-models/{model}/tracked-trips");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return [.. (await BodyAsync(response)).EnumerateArray()];
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response) =>
        (await BodyAsync(response)).GetProperty("code").GetString();

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
            name = $"Movie Cave {Guid.NewGuid():N}"[..30],
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
    private static async Task<HttpResponseMessage> PutConfigAsync(HttpClient client, Guid trip, object body)
    {
        var current = await client.GetAsync($"/api/v1/trip-logs/{trip}/tracking");
        current.StatusCode.ShouldBe(HttpStatusCode.OK, await current.Content.ReadAsStringAsync());
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/trip-logs/{trip}/tracking")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("If-Match", current.Headers.ETag!.ToString());
        return await client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> ArmAsync(HttpClient client, Guid trip, Guid model) =>
        PutConfigAsync(client, trip, new { state = "armed", surveyModelId = model });

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

    private static DateTimeOffset? TimeOf(JsonElement element, string property) =>
        element.GetProperty(property).ValueKind == JsonValueKind.Null
            ? null
            : element.GetProperty(property).GetDateTimeOffset();

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
