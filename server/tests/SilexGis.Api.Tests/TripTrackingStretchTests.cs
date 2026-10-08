// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
/// A report that says somebody was between two stations: written, read, corrected, taken off and
/// put back with both ends; refused for whichever end is wrong; and told to nobody without an
/// account.
/// </summary>
/// <remarks>
/// <para>
/// The far end of a stretch is place in every sense the first station is. So each place the first
/// station is kept from somebody has its test here for the second: a reader who may not be told
/// the cave's positions, the trip's history, and — the one that differs — every read that needs no
/// account, which goes on carrying the first station alone.
/// </para>
/// <para>
/// Every "is not there" below is asserted beside the same fact being there for a caller who is
/// told it, from the same rows, so that an absence is the rule working and not a report that was
/// never written. The two ends are always <c>cave.upper.2</c> and <c>cave.deep.3</c>: the first is
/// what a published read may carry and the second is what it may not, so the second's name is
/// searched for in the raw text of every answer.
/// </para>
/// </remarks>
public sealed class TripTrackingStretchTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Near = "cave.upper.2";
    private const string Far = "cave.deep.3";

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private HttpClient anonymous = null!;
    private long caveTypeId;

    public TripTrackingStretchTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            // Workers off: the graph-extraction job would otherwise pick up the invented survey
            // file below, fail to read it, and rewrite the very station rows these tests seed.
            JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        // An editor writes the log and may place the cave. The reader is a plain viewer: the
        // editors' group reads past protection by design and could not stand for "may not be told".
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"str-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"str-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"str-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"str-read-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        try { Directory.Delete(filesRoot, recursive: true); } catch { /* best effort */ }
    }

    // ---- the life of one stretch ---------------------------------------------------------

    [Fact]
    public async Task A_stretch_is_recorded_read_corrected_taken_off_and_put_back_with_both_ends()
    {
        var (trip, cavers) = await ArmedTripAsync("Stretch", guests: 2);

        var recorded = await PostEventAsync(owner, trip, Stretch(cavers));
        recorded.StatusCode.ShouldBe(HttpStatusCode.OK, await recorded.Content.ReadAsStringAsync());
        var written = (await BodyAsync(recorded)).EnumerateArray().ToList();
        written.Count.ShouldBe(2, "one report per person, each the same stretch");
        written.ShouldAllBe(e => Text(e, "stationName") == Near && Text(e, "toStationName") == Far);
        var eventId = written.Single(e => e.GetProperty("caverId").GetGuid() == cavers[0]).GetProperty("id").GetGuid();

        // The party table says the stretch for each of them; the mark's own station is the first.
        var state = await StateAsync(owner, trip);
        foreach (var caver in cavers)
        {
            var row = Participant(state, caver);
            Text(row, "stationName").ShouldBe(Near);
            Text(row, "toStationName").ShouldBe(Far);
        }

        // The log — which is also what a replay, of one trip or of several, is read from.
        (await LogAsync(owner, trip)).ShouldAllBe(e => Text(e, "stationName") == Near && Text(e, "toStationName") == Far);

        // Corrected to another far end, then to one station — a correction that names no far end
        // leaves none standing — then back to a stretch, written the other way round.
        var moved = await PutEventAsync(owner, trip, eventId, new
        {
            kind = "atStation", stationName = Near, toStationName = "cave.upper.1",
        });
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        Text(await BodyAsync(moved), "toStationName").ShouldBe("cave.upper.1");

        var single = await PutEventAsync(owner, trip, eventId, new { kind = "atStation", stationName = Near });
        single.StatusCode.ShouldBe(HttpStatusCode.OK, await single.Content.ReadAsStringAsync());
        Text(await BodyAsync(single), "toStationName").ShouldBeNull();
        (await StoredAsync(eventId)).ViewerToStationName.ShouldBeNull();
        Text(Participant(await StateAsync(owner, trip), cavers[0]), "toStationName").ShouldBeNull();
        Text(Participant(await StateAsync(owner, trip), cavers[1]), "toStationName")
            .ShouldBe(Far, "the other person's report is another report and was not touched");

        var reversed = await PutEventAsync(owner, trip, eventId, new
        {
            kind = "atStation", stationName = Far, toStationName = Near,
        });
        reversed.StatusCode.ShouldBe(HttpStatusCode.OK, await reversed.Content.ReadAsStringAsync());
        var stored = await StoredAsync(eventId);
        stored.ViewerStationName.ShouldBe(Far);
        stored.ViewerToStationName.ShouldBe(Near);

        // A correction to a kind that claims no place takes both ends, and the survey, with it.
        var noted = await PutEventAsync(owner, trip, eventId, new { kind = "note", note = "radio contact" });
        noted.StatusCode.ShouldBe(HttpStatusCode.OK, await noted.Content.ReadAsStringAsync());
        stored = await StoredAsync(eventId);
        stored.ViewerStationName.ShouldBeNull();
        stored.ViewerToStationName.ShouldBeNull();

        // Taken off the log and put back: the other person's stretch, both ends each time.
        var otherId = written.Single(e => e.GetProperty("caverId").GetGuid() == cavers[1]).GetProperty("id").GetGuid();
        (await owner.DeleteAsync(EventOf(trip, otherId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await LogAsync(owner, trip)).ShouldNotContain(e => e.GetProperty("id").GetGuid() == otherId);
        Text(Participant(await StateAsync(owner, trip), cavers[1]), "stationName").ShouldBeNull();

        var removedList = await owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events/removed");
        removedList.StatusCode.ShouldBe(HttpStatusCode.OK, await removedList.Content.ReadAsStringAsync());
        var kept = (await BodyAsync(removedList)).GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("report").GetProperty("id").GetGuid() == otherId).GetProperty("report");
        Text(kept, "stationName").ShouldBe(Near);
        Text(kept, "toStationName").ShouldBe(Far);

        var restored = await owner.PostAsync($"{EventOf(trip, otherId)}/restore", null);
        restored.StatusCode.ShouldBe(HttpStatusCode.OK, await restored.Content.ReadAsStringAsync());
        Text(await BodyAsync(restored), "toStationName").ShouldBe(Far);
        var back = Participant(await StateAsync(owner, trip), cavers[1]);
        Text(back, "stationName").ShouldBe(Near);
        Text(back, "toStationName").ShouldBe(Far);
    }

    [Fact]
    public async Task A_stretch_sent_again_under_its_key_is_answered_with_both_ends_and_written_once()
    {
        var (trip, cavers) = await ArmedTripAsync("Stretch, sent twice", guests: 1);
        var key = Guid.NewGuid();
        object Send() => new
        {
            caverIds = cavers, kind = "atStation", stationName = Near, toStationName = Far, clientKey = key,
        };

        var first = await PostEventAsync(owner, trip, Send());
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var again = await PostEventAsync(owner, trip, Send());
        again.StatusCode.ShouldBe(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());

        var answered = (await BodyAsync(again)).EnumerateArray().ShouldHaveSingleItem();
        answered.GetProperty("id").GetGuid()
            .ShouldBe((await BodyAsync(first)).EnumerateArray().Single().GetProperty("id").GetGuid());
        Text(answered, "stationName").ShouldBe(Near);
        Text(answered, "toStationName").ShouldBe(Far);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.TripPositionEvents.IgnoreQueryFilters().CountAsync(e => e.TripLogId == trip)).ShouldBe(1);
    }

    // ---- refusals ------------------------------------------------------------------------

    [Fact]
    public async Task Either_end_the_survey_lacks_is_refused_under_its_own_code_and_so_is_one_station_twice()
    {
        var (trip, cavers) = await ArmedTripAsync("Stretch, refused", guests: 1);

        async Task RefusedAsync(object body, HttpStatusCode status, string code)
        {
            var response = await PostEventAsync(owner, trip, body);
            var payload = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(status, payload);
            payload.ShouldContain(code);
        }

        // Which end is wrong is said by the code, so a form can point at the right field.
        await RefusedAsync(
            new { caverIds = cavers, kind = "atStation", stationName = "cave.nowhere.9", toStationName = Far },
            HttpStatusCode.BadRequest, "tracking.station_unknown");
        await RefusedAsync(
            new { caverIds = cavers, kind = "atStation", stationName = Near, toStationName = "cave.nowhere.9" },
            HttpStatusCode.BadRequest, "tracking.to_station_unknown");
        await RefusedAsync(
            new { caverIds = cavers, kind = "atStation", stationName = Near, toStationName = Near },
            HttpStatusCode.BadRequest, "tracking.stretch_same_station");

        // A far end belongs to a station report, and a blank one is a field somebody forgot.
        await RefusedAsync(
            new { caverIds = cavers, kind = "atDepth", depthM = 50, toStationName = Far },
            HttpStatusCode.BadRequest, "validation");
        await RefusedAsync(
            new { caverIds = cavers, kind = "note", note = "on the way", toStationName = Far },
            HttpStatusCode.BadRequest, "validation");
        await RefusedAsync(
            new { caverIds = cavers, kind = "atStation", stationName = Near, toStationName = "" },
            HttpStatusCode.BadRequest, "validation");

        (await LogAsync(owner, trip)).ShouldBeEmpty("a refused report writes nothing");

        // The twin, and what the corrections below are refused against: the stretch is accepted.
        var accepted = await PostEventAsync(owner, trip, Stretch(cavers));
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var eventId = (await BodyAsync(accepted)).EnumerateArray().Single().GetProperty("id").GetGuid();

        var wrongFar = await PutEventAsync(owner, trip, eventId, new
        {
            kind = "atStation", stationName = Near, toStationName = "cave.nowhere.9",
        });
        wrongFar.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await wrongFar.Content.ReadAsStringAsync()).ShouldContain("tracking.to_station_unknown");
        var twice = await PutEventAsync(owner, trip, eventId, new
        {
            kind = "atStation", stationName = Far, toStationName = Far,
        });
        twice.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await twice.Content.ReadAsStringAsync()).ShouldContain("tracking.stretch_same_station");

        var stored = await StoredAsync(eventId);
        stored.ViewerStationName.ShouldBe(Near);
        stored.ViewerToStationName.ShouldBe(Far, "a refused correction leaves the report as it was");
    }

    [Fact]
    public async Task The_table_itself_refuses_a_far_end_left_beside_a_place_it_was_not_recorded_with()
    {
        var (trip, cavers) = await ArmedTripAsync("Stretch, by hand", guests: 1);
        var accepted = await PostEventAsync(owner, trip, Stretch(cavers));
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var eventId = (await BodyAsync(accepted)).EnumerateArray().Single().GetProperty("id").GetGuid();

        // What a writer that rewrites a report's place and forgets the far end would do, three
        // ways: another kind, no first station, and the first station moved onto the far end.
        async Task ShouldBeRefusedAsync(Action<TripPositionEvent> forgetful)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var row = await db.TripPositionEvents.SingleAsync(e => e.Id == eventId);
            forgetful(row);
            var refused = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
            refused.InnerException!.Message.ShouldContain("ck_trip_position_events_stretch");
        }

        await ShouldBeRefusedAsync(row => row.Kind = TripPositionEventKind.Note);
        await ShouldBeRefusedAsync(row => row.ViewerStationName = null);
        await ShouldBeRefusedAsync(row => row.ViewerStationName = Far);

        // The stretch stands as it was written: the rule refused the three, not every write.
        var stored = await StoredAsync(eventId);
        stored.ViewerStationName.ShouldBe(Near);
        stored.ViewerToStationName.ShouldBe(Far);
    }

    // ---- who is told ---------------------------------------------------------------------

    [Fact]
    public async Task Both_ends_reach_the_placer_and_neither_reaches_a_reader_who_may_not_be_told_the_caves_positions()
    {
        var (trip, cavers) = await ArmedTripAsync("Stretch, protected", guests: 1, locationProtected: true);
        (await PostEventAsync(owner, trip, Stretch(cavers))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Positive half, from the same rows: whoever may place the cave is told both ends.
        var mine = Participant(await StateAsync(owner, trip), cavers[0]);
        Text(mine, "stationName").ShouldBe(Near);
        Text(mine, "toStationName").ShouldBe(Far);
        var myLog = (await LogAsync(owner, trip)).ShouldHaveSingleItem();
        Text(myLog, "toStationName").ShouldBe(Far);
        var mySheet = await SheetAsync(owner, trip);
        mySheet.ShouldContain(Near);
        mySheet.ShouldContain(Far);

        // Negative half: the reader reads the trip, sees that a report exists, and is told
        // neither end — on the party table, on the log, or on the log written out as a sheet.
        var theirState = await StateAsync(reader, trip);
        theirState.GetProperty("positionsWithheld").GetBoolean().ShouldBeTrue();
        var theirs = Participant(theirState, cavers[0]);
        Text(theirs, "stationName").ShouldBeNull();
        Text(theirs, "toStationName").ShouldBeNull();
        theirState.GetRawText().ShouldNotContain(Far);
        theirState.GetRawText().ShouldNotContain(Near);

        var theirLog = await reader.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events");
        theirLog.StatusCode.ShouldBe(HttpStatusCode.OK);
        var theirLogText = await theirLog.Content.ReadAsStringAsync();
        theirLogText.ShouldContain(myLog.GetProperty("id").GetGuid().ToString(), Case.Insensitive);
        theirLogText.ShouldNotContain(Far);
        theirLogText.ShouldNotContain(Near);

        var theirSheet = await SheetAsync(reader, trip);
        theirSheet.ShouldNotContain(Far);
        theirSheet.ShouldNotContain(Near);
        theirSheet.ShouldNotContain("Pana la statia");
    }

    [Fact]
    public async Task The_trips_history_keeps_the_report_and_neither_end_of_its_stretch()
    {
        var (trip, cavers) = await ArmedTripAsync("Stretch, in history", guests: 1);
        var recorded = await PostEventAsync(owner, trip, new
        {
            caverIds = cavers, kind = "atStation", stationName = Near, toStationName = Far,
            note = "heard at the pitch head",
        });
        recorded.StatusCode.ShouldBe(HttpStatusCode.OK, await recorded.Content.ReadAsStringAsync());
        var eventId = (await BodyAsync(recorded)).EnumerateArray().Single().GetProperty("id").GetGuid();
        (await PutEventAsync(owner, trip, eventId, new
        {
            kind = "atStation", stationName = Far, toStationName = "cave.upper.1", note = "heard at the pitch head",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await owner.GetAsync($"/api/v1/history?entityType=tripLog&entityId={trip}&pageSize=200");
        var history = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, history);

        // The report is on the timeline — asked by the person who may place the cave, so what is
        // missing below is missing for everybody — and it says its words.
        history.ShouldContain(eventId.ToString(), Case.Insensitive);
        history.ShouldContain("heard at the pitch head");
        // No station of either write, under either end's name or under the column's.
        history.ShouldNotContain(Near);
        history.ShouldNotContain(Far);
        history.ShouldNotContain("cave.upper.1");
        history.ShouldNotContain("ToStationName", Case.Insensitive);
    }

    // ---- the sheet -----------------------------------------------------------------------

    [Fact]
    public async Task The_log_goes_out_as_a_sheet_with_both_ends_and_reads_back_changing_nothing()
    {
        var (trip, cavers) = await ArmedTripAsync("Stretch, as a sheet", guests: 2);
        (await PostEventAsync(owner, trip, new
        {
            caverIds = new[] { cavers[0] }, kind = "atStation", stationName = Near, toStationName = Far,
            recordedAt = "2026-09-12T09:00:00Z",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostEventAsync(owner, trip, new
        {
            caverIds = new[] { cavers[1] }, kind = "atStation", stationName = Far,
            recordedAt = "2026-09-12T09:10:00Z",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var sheet = await SheetAsync(owner, trip);
        var lines = sheet.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].ShouldBe("Data si ora,Adancime,Statie,Pana la statia,Loc,Speologi,Echipa,Nota,Stare");
        lines.Length.ShouldBe(3);
        lines[1].ShouldContain($",{Near},{Far},");
        lines[2].ShouldContain($",{Far},,");

        // Read straight back: nothing is created, nothing is written over, nobody is "corrected".
        var body = new { text = sheet, replaceExisting = true };
        var preview = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/csv-import/preview", body);
        var previewText = await preview.Content.ReadAsStringAsync();
        preview.StatusCode.ShouldBe(HttpStatusCode.OK, previewText);
        var previewed = JsonDocument.Parse(previewText).RootElement;
        previewed.GetProperty("rows").EnumerateArray()
            .Select(r => (Text(r, "stationName"), Text(r, "toStationName")))
            .ShouldBe([(Near, Far), (Far, null)]);

        var commit = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/csv-import/commit", body);
        var committed = await BodyAsync(commit);
        commit.StatusCode.ShouldBe(HttpStatusCode.OK, committed.GetRawText());
        committed.GetProperty("created").GetInt32().ShouldBe(0);
        committed.GetProperty("updated").GetInt32().ShouldBe(0);
        committed.GetProperty("unchanged").GetInt32().ShouldBe(2);
        (await LogAsync(owner, trip)).ShouldAllBe(e => !e.GetProperty("corrected").GetBoolean());
        (await SheetAsync(owner, trip)).ShouldBe(sheet);

        // The twin: the same sheet with the far end's cell emptied is another statement, and is
        // written — the stretch becomes a report at its first station.
        var cut = new { text = sheet.Replace($",{Near},{Far},", $",{Near},,"), replaceExisting = true };
        var recommit = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/csv-import/commit", cut);
        var recommitted = await BodyAsync(recommit);
        recommit.StatusCode.ShouldBe(HttpStatusCode.OK, recommitted.GetRawText());
        recommitted.GetProperty("updated").GetInt32().ShouldBe(1);
        var first = Participant(await StateAsync(owner, trip), cavers[0]);
        Text(first, "stationName").ShouldBe(Near);
        Text(first, "toStationName").ShouldBeNull();
    }

    // ---- nobody without an account -------------------------------------------------------

    /// <summary>
    /// No read that needs no account carries the far end of a stretch, of the trip the link is for
    /// or of another party in the same cave, while its watch runs or once it is history.
    /// </summary>
    /// <remarks>
    /// Two trips in one unprotected cave, each with a stretch from the same first station to the
    /// same far end, both published. Every public route is read through the first trip's link and
    /// searched as raw text for the far end's name and for the field's — and each is first shown to
    /// carry the first station or the other trip, so that a route answering with nothing at all
    /// cannot pass. The signed-in read of the same two trips carries the far end throughout.
    /// </remarks>
    [Fact]
    public async Task No_read_without_an_account_carries_the_far_end_of_a_stretch()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        var (mine, myCavers) = await CreateTripAsync("Stretch, published", guests: 1);
        var (theirs, theirCavers) = await CreateTripAsync("Stretch, beside it", guests: 1);
        foreach (var (trip, cavers) in new[] { (mine, myCavers), (theirs, theirCavers) })
        {
            (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await PostEventAsync(owner, trip, new { caverIds = cavers, kind = "entered" }))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
            (await PostEventAsync(owner, trip, Stretch(cavers))).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var token = await PublishAsync(mine);
        _ = await PublishAsync(theirs);
        var link = $"/api/v1/public/trips/{Uri.EscapeDataString(token)}";

        async Task<string> PublicAsync(string url)
        {
            var response = await anonymous.GetAsync(url);
            var text = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{url}: {text}");
            text.ShouldNotContain(Far);
            text.ShouldNotContain("toStation", Case.Insensitive);
            return text;
        }

        void SignedInCarriesBoth(JsonElement state, Guid caver)
        {
            Text(Participant(state, caver), "stationName").ShouldBe(Near);
            Text(Participant(state, caver), "toStationName").ShouldBe(Far);
        }

        // While both watches run: the followed page, and the cave's other parties.
        SignedInCarriesBoth(await StateAsync(owner, mine), myCavers[0]);
        SignedInCarriesBoth(await StateAsync(owner, theirs), theirCavers[0]);
        (await PublicAsync(link)).ShouldContain(Near);
        var beside = await PublicAsync($"{link}/live");
        beside.ShouldContain(theirs.ToString(), Case.Insensitive);
        beside.ShouldContain(Near);

        // Once the other trip is history: the list of past trips, and its track.
        await CloseAsync(theirs, DateTimeOffset.UtcNow.AddDays(-5));
        (await PublicAsync($"{link}/past")).ShouldContain(theirs.ToString(), Case.Insensitive);
        (await PublicAsync($"{link}/past/{theirs}")).ShouldContain(Near);

        // And the closed trip still says both ends to those who are signed in.
        SignedInCarriesBoth(await StateAsync(owner, theirs), theirCavers[0]);
        (await LogAsync(owner, theirs)).ShouldContain(e => Text(e, "toStationName") == Far);
    }

    // ---- plumbing ------------------------------------------------------------------------

    private static object Stretch(IReadOnlyList<Guid> cavers) => new
    {
        caverIds = cavers, kind = "atStation", stationName = Near, toStationName = Far,
    };

    /// <summary>A string member, or null where it is null — and a failure where it is absent.</summary>
    private static string? Text(JsonElement element, string property) =>
        element.GetProperty(property).ValueKind == JsonValueKind.Null
            ? null
            : element.GetProperty(property).GetString();

    private static string EventOf(Guid trip, Guid eventId) => $"/api/v1/trip-logs/{trip}/tracking/events/{eventId}";

    private async Task<TripPositionEvent> StoredAsync(Guid eventId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripPositionEvents.AsNoTracking().IgnoreQueryFilters().SingleAsync(e => e.Id == eventId);
    }

    private async Task<(Guid Trip, List<Guid> Cavers)> ArmedTripAsync(
        string title, int guests, bool locationProtected = false)
    {
        var (trip, cavers) = await CreateTripAsync(title, guests);
        var cave = await CreateCaveAsync(locationProtected);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        return (trip, cavers);
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

    /// <summary>
    /// A cave, protected or not through the route that keeps the derived protection in step —
    /// never by writing the column, which would leave a test passing whether or not the rule works.
    /// </summary>
    private async Task<Guid> CreateCaveAsync(bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Stretch Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> SeedModelWithStationsAsync(Guid caveId)
    {
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new("application/octet-stream");
        form.Add(bytes, "file", "stretch.3d");
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var modelId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.SingleAsync(m => m.Id == modelId);
        model.Status = SurveyModelStatus.Ready;
        model.SourceEpsg = 31700;
        db.SurveyStations.AddRange(
            Station(modelId, "cave.ent.0", "cave.ent", 350, SurveyStationFlags.Entrance),
            Station(modelId, "cave.upper.1", "cave.upper", 340, SurveyStationFlags.Underground),
            Station(modelId, Near, "cave.upper", 300, SurveyStationFlags.Underground),
            Station(modelId, Far, "cave.deep", 230, SurveyStationFlags.Underground));
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

    /// <summary>Closes the watch through the API, then backdates the instant it closed at.</summary>
    /// <remarks>
    /// The instant is a plain recorded fact read against the clock on every request, so a row
    /// written this way is exactly a watch that was closed that long ago.
    /// </remarks>
    private async Task CloseAsync(Guid trip, DateTimeOffset closedAt)
    {
        (await PutConfigAsync(owner, trip, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var tracking = await db.TripTrackings.SingleAsync(t => t.TripLogId == trip);
        tracking.ClosedAt = closedAt;
        await db.SaveChangesAsync();
    }

    private async Task<string> PublishAsync(Guid trip)
    {
        var minted = await owner.PostAsync($"/api/v1/trip-logs/{trip}/tracking/shares", null);
        var payload = await minted.Content.ReadAsStringAsync();
        minted.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("token").GetString()!;
    }

    private static Task<HttpResponseMessage> PostEventAsync(HttpClient client, Guid trip, object body) =>
        client.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", body);

    private static Task<HttpResponseMessage> PutEventAsync(HttpClient client, Guid trip, Guid eventId, object body) =>
        client.PutAsJsonAsync(EventOf(trip, eventId), body);

    private static async Task<List<JsonElement>> LogAsync(HttpClient client, Guid trip)
    {
        var response = await client.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return [.. (await BodyAsync(response)).GetProperty("items").EnumerateArray()];
    }

    /// <summary>The trip's log written out as a sheet, without the mark a spreadsheet wants in front.</summary>
    private static async Task<string> SheetAsync(HttpClient client, Guid trip)
    {
        var response = await client.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events/export");
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return text.TrimStart('﻿');
    }

    private static JsonElement Participant(JsonElement state, Guid caverId) =>
        state.GetProperty("participants").EnumerateArray()
            .Single(p => p.GetProperty("caverId").GetGuid() == caverId);

    private static async Task<JsonElement> StateAsync(HttpClient client, Guid trip)
    {
        var response = await client.GetAsync($"/api/v1/trip-logs/{trip}/tracking");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await BodyAsync(response);
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
