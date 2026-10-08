// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
/// How the list of trips tracked on a survey model counts a trip's reports: in the database, one
/// line per trip and cave snapshot, kept or dropped whole by the rule the trip's own log applies
/// to each report.
/// </summary>
/// <remarks>
/// Two things are held here. What is counted for whom — the owner (an Editor who created the caves
/// and the trip) is told of every report whose cave still exists, the reader (a Viewer with no
/// placing right on the protected cave) only of those in the cave they may place, and each of them
/// exactly as many as their own read of the trip's log shows with a place. And what the read costs
/// — the same number of commands for one trip as for several, and the same number of rows read
/// however long the log has become.
/// </remarks>
public sealed class SurveyModelTrackedTripCountsTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly DatabaseMeter meter = new();

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private long caveTypeId;

    public SurveyModelTrackedTripCountsTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            services =>
            {
                // Workers off: the graph-extraction job would otherwise pick up the fake survey
                // file below, fail to parse it, and rewrite the very station rows these tests
                // seed — and would send commands of its own in the middle of a measured request.
                JobWorkers.RemoveFrom(services);
                services.ConfigureDbContext<SilexGisDbContext>(options => options.AddInterceptors(meter));
            });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"smtc-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"smtc-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"smtc-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"smtc-read-{suffix}@t.local");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        try { Directory.Delete(filesRoot, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// One trip, five reports on one model, under three anchors: two in the cave everybody may
    /// place, two in a cave only the owner may place, one whose cave is gone. The withheld ones
    /// are the earliest and the latest but one, so a count that kept them would also move both
    /// ends of the span.
    /// </summary>
    [Fact]
    public async Task A_trip_reporting_under_two_cave_snapshots_is_counted_only_where_the_caller_may_place_it()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        var guarded = await CreateCaveAsync(locationProtected: true);

        var (trip, cavers) = await CreateTripAsync("Two snapshots", guests: 1);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        foreach (var hour in new[] { 8, 9, 10, 12, 13 })
        {
            await ReportAsync(trip, AtStation(cavers), At(hour, 0));
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var (first, late, last) = (At(8, 0), At(12, 0), At(13, 0));
            (await db.TripPositionEvents
                .Where(e => e.TripLogId == trip && (e.RecordedAt == first || e.RecordedAt == late))
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.CaveFeatureId, (Guid?)guarded)))
                .ShouldBe(2);
            (await db.TripPositionEvents
                .Where(e => e.TripLogId == trip && e.RecordedAt == last)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.CaveFeatureId, (Guid?)null)))
                .ShouldBe(1);

            // The state the assertions below rest on, read back rather than assumed: every report
            // names the model, under three different anchors.
            var anchors = await db.TripPositionEvents
                .Where(e => e.TripLogId == trip && e.SurveyModelId == model)
                .GroupBy(e => e.CaveFeatureId)
                .Select(g => new { Cave = g.Key, Reports = g.Count() })
                .ToListAsync();
            anchors.Count.ShouldBe(3);
            anchors.Single(a => a.Cave == cave).Reports.ShouldBe(2);
            anchors.Single(a => a.Cave == guarded).Reports.ShouldBe(2);
            anchors.Single(a => a.Cave is null).Reports.ShouldBe(1);
        }

        // What each caller's own read of the trip's log shows with a place — the rule the count
        // has to agree with. The reader really is refused the guarded cave's positions there.
        (await PlacedInLogAsync(owner, trip, model)).ShouldBe((Placed: 4, All: 5));
        (await PlacedInLogAsync(reader, trip, model)).ShouldBe((Placed: 2, All: 5));

        var mine = (await TrackedAsync(owner, model)).ShouldHaveSingleItem();
        mine.GetProperty("reportCount").GetInt32().ShouldBe(4, "both caves the owner may place, never the lost one");
        TimeOf(mine, "firstReportAt").ShouldBe(At(8, 0));
        TimeOf(mine, "lastReportAt").ShouldBe(At(12, 0), "a report whose cave is gone moves nothing, for anyone");

        var theirs = (await TrackedAsync(reader, model)).ShouldHaveSingleItem();
        theirs.GetProperty("tripLogId").GetGuid().ShouldBe(trip);
        theirs.GetProperty("reportCount").GetInt32().ShouldBe(2);
        TimeOf(theirs, "firstReportAt").ShouldBe(At(9, 0), "the withheld 08:00 report moves nothing");
        TimeOf(theirs, "lastReportAt").ShouldBe(At(10, 0), "the withheld 12:00 report moves nothing");
    }

    /// <summary>
    /// A deleted trip is not on the model's list and its reports are counted for nobody; restored,
    /// it is back with every one of them. The trip beside it is listed throughout, so an empty
    /// place can only mean the deleted one was left out.
    /// </summary>
    [Fact]
    public async Task A_deleted_trip_is_off_the_list_with_its_reports_and_back_with_them_once_restored()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);

        var (kept, keptCavers) = await CreateTripAsync("Kept", guests: 1);
        (await ArmAsync(owner, kept, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(kept, AtStation(keptCavers), At(9, 0));

        var (gone, goneCavers) = await CreateTripAsync("Deleted", guests: 1);
        (await ArmAsync(owner, gone, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(gone, AtStation(goneCavers), At(10, 0));
        await ReportAsync(gone, AtStation(goneCavers), At(11, 0));

        foreach (var client in new[] { owner, reader })
        {
            (await TrackedAsync(client, model)).Select(TripOf).ShouldBe([gone, kept]);
        }

        (await owner.DeleteAsync($"/api/v1/trip-logs/{gone}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        foreach (var client in new[] { owner, reader })
        {
            var listed = (await TrackedAsync(client, model)).ShouldHaveSingleItem();
            TripOf(listed).ShouldBe(kept);
            listed.GetProperty("reportCount").GetInt32().ShouldBe(1);
        }

        (await owner.PostAsync($"/api/v1/trip-logs/{gone}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        foreach (var client in new[] { owner, reader })
        {
            var listed = await TrackedAsync(client, model);
            listed.Select(TripOf).ShouldBe([gone, kept]);
            listed[0].GetProperty("reportCount").GetInt32().ShouldBe(2);
            TimeOf(listed[0], "lastReportAt").ShouldBe(At(11, 0));
        }
    }

    /// <summary>
    /// The read sends the same number of commands for a model one trip was tracked on as for a
    /// model three were, and reads the same number of rows when a trip's log has grown fivefold.
    /// </summary>
    /// <remarks>
    /// Equal numbers prove nothing by themselves, so each has its twin on the same host and the
    /// same meter: the list really does report five times as many reports afterwards, and the
    /// trip's own log — which does hand every report over — reads more rows by at least the
    /// number added. A meter that was never attached, or that did not see rows, would fail there.
    /// </remarks>
    [Fact]
    public async Task The_list_sends_the_same_commands_for_one_trip_as_for_several_and_reads_no_more_rows_as_a_log_grows()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var single = await SeedModelWithStationsAsync(cave);
        var several = await SeedModelWithStationsAsync(cave);

        var (grown, grownCavers) = await CreateTripAsync("Grown", guests: 5);
        (await ArmAsync(owner, grown, single)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(grown, AtStation(grownCavers), At(9, 0));

        foreach (var title in new[] { "First", "Second", "Third" })
        {
            var (trip, cavers) = await CreateTripAsync(title, guests: 1);
            (await ArmAsync(owner, trip, several)).StatusCode.ShouldBe(HttpStatusCode.OK);
            await ReportAsync(trip, AtStation(cavers), At(10, 0));
        }

        var listOfSingle = $"/api/v1/survey-models/{single}/tracked-trips";
        var listOfSeveral = $"/api/v1/survey-models/{several}/tracked-trips";
        var logOfGrown = $"/api/v1/trip-logs/{grown}/tracking/events?pageSize=500";

        // Once unmeasured, so that nothing a first request alone pays for is in the numbers.
        (await reader.GetAsync(listOfSingle)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync(listOfSeveral)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync(logOfGrown)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var oneTrip = await MeteredAsync(reader, listOfSingle);
        var threeTrips = await MeteredAsync(reader, listOfSeveral);
        var logBefore = await MeteredAsync(reader, logOfGrown);
        oneTrip.Body.GetArrayLength().ShouldBe(1);
        oneTrip.Body[0].GetProperty("reportCount").GetInt32().ShouldBe(5);
        threeTrips.Body.GetArrayLength().ShouldBe(3);
        oneTrip.Commands.ShouldBeGreaterThan(0);
        oneTrip.Rows.ShouldBeGreaterThan(0);
        threeTrips.Commands.ShouldBe(oneTrip.Commands, "a fixed number of commands whatever the number of trips");

        foreach (var hour in new[] { 10, 11, 12, 13 })
        {
            await ReportAsync(grown, AtStation(grownCavers), At(hour, 0));
        }

        var afterGrowth = await MeteredAsync(reader, listOfSingle);
        var logAfter = await MeteredAsync(reader, logOfGrown);
        afterGrowth.Body[0].GetProperty("reportCount").GetInt32().ShouldBe(25, "the twenty new reports are counted");
        TimeOf(afterGrowth.Body[0], "lastReportAt").ShouldBe(At(13, 0));
        afterGrowth.Commands.ShouldBe(oneTrip.Commands);
        afterGrowth.Rows.ShouldBe(oneTrip.Rows, "twenty more reports, and not one more row read");

        logAfter.Body.GetProperty("items").GetArrayLength().ShouldBe(25);
        (logAfter.Rows - logBefore.Rows).ShouldBeGreaterThanOrEqualTo(20, "the same meter sees rows where rows are read");
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

    /// <summary>
    /// How many reports of the trip this caller's own read of its log shows placed on the model,
    /// and how many it shows at all.
    /// </summary>
    private static async Task<(int Placed, int All)> PlacedInLogAsync(HttpClient client, Guid trip, Guid model)
    {
        var response = await client.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events?pageSize=500");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var items = (await BodyAsync(response)).GetProperty("items").EnumerateArray().ToList();
        var placed = items.Count(item =>
            item.GetProperty("surveyModelId").ValueKind != JsonValueKind.Null
            && item.GetProperty("surveyModelId").GetGuid() == model);
        return (placed, items.Count);
    }

    private async Task<(JsonElement Body, int Commands, long Rows)> MeteredAsync(HttpClient client, string url)
    {
        meter.Reset();
        var response = await client.GetAsync(url);
        var (commands, rows) = (meter.Commands, meter.Rows);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await BodyAsync(response), commands, rows);
    }

    private async Task<(Guid Trip, List<Guid> Cavers)> CreateTripAsync(string title, int guests)
    {
        var participants = Enumerable.Range(1, guests)
            .Select(i => new { newCaverName = $"Guest {i} {Guid.NewGuid():N}"[..24] })
            .ToArray();
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            tripDate = "2026-09-12",
            participants,
            visibility = "authenticated",
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
            name = $"Count Cave {Guid.NewGuid():N}"[..30],
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

    private static DateTimeOffset? TimeOf(JsonElement element, string property) =>
        element.GetProperty(property).ValueKind == JsonValueKind.Null
            ? null
            : element.GetProperty(property).GetDateTimeOffset();

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    /// <summary>
    /// Every command the host sends to the database and every row it reads back, counted and
    /// nothing else.
    /// </summary>
    private sealed class DatabaseMeter : DbCommandInterceptor
    {
        private int commands;
        private long rows;

        public int Commands => Volatile.Read(ref commands);

        public long Rows => Interlocked.Read(ref rows);

        public void Reset()
        {
            Interlocked.Exchange(ref commands, 0);
            Interlocked.Exchange(ref rows, 0);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref commands);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref commands);
            return new(result);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Interlocked.Increment(ref commands);
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref commands);
            return new(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Interlocked.Increment(ref commands);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref commands);
            return new(result);
        }

        // A reader is disposed once, whichever way it was read, and by then it knows how many
        // rows were taken from it.
        public override InterceptionResult DataReaderDisposing(
            DbCommand command, DataReaderDisposingEventData eventData, InterceptionResult result)
        {
            Interlocked.Add(ref rows, eventData.ReadCount);
            return result;
        }
    }
}
