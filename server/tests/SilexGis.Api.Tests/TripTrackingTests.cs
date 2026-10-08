// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Api.Tests.Support;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Live trip tracking: an admin reports where cavers are, the log is append-only, and a
/// position is a station reference guarded by the same exact-location rules as the survey
/// model itself. The load-bearing test is the withholding one: it builds the protected state
/// and asserts both halves — the reader who must not see a station name, and the owner who
/// must — because a negative that passes for any reason is not a protection test.
/// </summary>
public sealed class TripTrackingTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string connectionString;
    private string ownerEmail = null!;
    private Guid ownerId;
    private Guid readerId;

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private HttpClient anonymous = null!;
    private long caveTypeId;

    public TripTrackingTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
        connectionString = postgres.ConnectionString;
        factory = new SilexGisApiFactory(
            connectionString,
            HostSettings(),
            // Workers off: the graph-extraction job would otherwise pick up the fake survey
            // file below, fail to parse it, and rewrite the very station rows these tests seed.
            JobWorkers.RemoveFrom);
    }

    /// <summary>
    /// What every host of this class is configured with, so that a test which needs a second one —
    /// a clock it can move, a setting of its own — builds it on the same files and keys.
    /// </summary>
    private Dictionary<string, string?> HostSettings() => new()
    {
        ["Files:Root"] = filesRoot,
        ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
    };

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerEmail = $"trk-own-{suffix}@t.local";
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"trk-own-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"trk-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"trk-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"trk-read-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        try { Directory.Delete(filesRoot, recursive: true); } catch { /* best effort */ }
    }

    // ---- the core loop -------------------------------------------------------------------

    [Fact]
    public async Task Tracking_arms_takes_reports_folds_the_latest_per_caver_and_a_wrong_report_can_be_taken_off_the_log()
    {
        var (trip, cavers) = await CreateTripAsync("Core loop", guests: 2);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);

        // An off watch takes no report: it names no survey, so a claimed station has nothing to
        // resolve against. A closed one does take them — see the correction tests of their own.
        var early = await PostEventAsync(owner, trip, new { caverIds = cavers, kind = "entered" });
        early.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await early.Content.ReadAsStringAsync()).ShouldContain("tracking.not_writable");

        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // One write, many cavers — everyone through the entrance at once.
        var entered = await PostEventAsync(owner, trip, new { caverIds = cavers, kind = "entered" });
        entered.StatusCode.ShouldBe(HttpStatusCode.OK, await entered.Content.ReadAsStringAsync());

        // One caver reported at a named station, the other at a depth the resolver answers.
        (await PostEventAsync(owner, trip, new
        {
            caverIds = new[] { cavers[0] },
            kind = "atStation",
            stationName = "cave.upper.2",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var atDepth = await PostEventAsync(owner, trip, new
        {
            caverIds = new[] { cavers[1] },
            kind = "atDepth",
            depthM = 118,
        });
        atDepth.StatusCode.ShouldBe(HttpStatusCode.OK, await atDepth.Content.ReadAsStringAsync());
        var depthRow = (await BodyAsync(atDepth))[0];
        depthRow.GetProperty("stationName").GetString().ShouldBe("cave.deep.3");
        depthRow.GetProperty("depthEnteredM").GetDecimal().ShouldBe(118);

        // The fold: each caver shows their latest report, and an exit flips them out. A note
        // says something happened, not where — the displayed position survives it.
        (await PostEventAsync(owner, trip, new { caverIds = new[] { cavers[0] }, kind = "note", note = "asked for rope" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostEventAsync(owner, trip, new { caverIds = new[] { cavers[1] }, kind = "exited" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var state = await StateAsync(owner, trip);
        var participants = state.GetProperty("participants").EnumerateArray().ToList();
        var first = participants.Single(p => p.GetProperty("caverId").GetGuid() == cavers[0]);
        first.GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
        first.GetProperty("lastKind").GetString().ShouldBe("note");
        first.GetProperty("out").GetBoolean().ShouldBeFalse();
        var second = participants.Single(p => p.GetProperty("caverId").GetGuid() == cavers[1]);
        second.GetProperty("lastKind").GetString().ShouldBe("exited");
        second.GetProperty("out").GetBoolean().ShouldBeTrue();

        // A wrong report is deleted, and the fold falls back to what remains.
        var events = await BodyAsync(await owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events?caverId={cavers[0]}"));
        var stationEventId = events.GetProperty("items").EnumerateArray()
            .First(e => e.GetProperty("kind").GetString() == "atStation").GetProperty("id").GetGuid();
        (await owner.DeleteAsync($"/api/v1/trip-logs/{trip}/tracking/events/{stationEventId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var refolded = await StateAsync(owner, trip);
        var corrected = refolded.GetProperty("participants").EnumerateArray()
            .Single(p => p.GetProperty("caverId").GetGuid() == cavers[0]);
        // The note stays the latest word about them, and with the station report gone no
        // earlier event claims a place — the displayed position falls back to nothing.
        corrected.GetProperty("lastKind").GetString().ShouldBe("note");
        corrected.GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
        TimeOf(corrected, "positionRecordedAt").ShouldBeNull();
        // Deleting a report re-folds the standing too, and this one still has the entry that put
        // them underground: losing the place they were at is not losing the fact they are inside.
        corrected.GetProperty("in").GetBoolean().ShouldBeTrue();
        corrected.GetProperty("out").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_depth_report_respects_the_trips_filter_because_parallel_shafts_share_depths()
    {
        var (trip, cavers) = await CreateTripAsync("Filtered depth", guests: 1);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model, depthFilter: new[] { "cave.parallel" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Unfiltered, −50 m is ambiguous between two branches; the filter names the one the
        // party went into, and the resolver may only answer from it.
        var resolved = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/resolve-depth", new { depthM = 50 });
        resolved.StatusCode.ShouldBe(HttpStatusCode.OK, await resolved.Content.ReadAsStringAsync());
        var candidates = (await BodyAsync(resolved)).EnumerateArray().ToList();
        candidates.ShouldNotBeEmpty();
        candidates.ShouldAllBe(c => c.GetProperty("stationName").GetString()!.StartsWith("cave.parallel"));

        var report = await PostEventAsync(owner, trip, new { caverIds = cavers, kind = "atDepth", depthM = -50 });
        report.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BodyAsync(report))[0].GetProperty("stationName").GetString().ShouldBe("cave.parallel.2");

        // Closing names no model and touches no config; re-arming keeps what was stored, so
        // later reports still resolve under the filter the earlier ones did.
        (await PutConfigAsync(owner, trip, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PutConfigAsync(owner, trip, new { state = "armed" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var again = await PostEventAsync(owner, trip, new { caverIds = cavers, kind = "atDepth", depthM = 50 });
        again.StatusCode.ShouldBe(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
        (await BodyAsync(again))[0].GetProperty("stationName").GetString().ShouldBe("cave.parallel.2");
    }

    // ---- who may do what -----------------------------------------------------------------

    [Fact]
    public async Task Only_a_trip_writer_records_and_a_stranger_is_never_shown_the_trip_at_all()
    {
        var (trip, cavers) = await CreateTripAsync("Authority", guests: 1, visibility: "authenticated");
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A reader sees the tracking state but cannot write any part of it.
        (await reader.GetAsync($"/api/v1/trip-logs/{trip}/tracking")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostEventAsync(reader, trip, new { caverIds = cavers, kind = "entered" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/teams", new { title = "Echipa 1" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A private trip's tracking is 404 to the uninvolved — indistinguishable from absent.
        var (privateTrip, _) = await CreateTripAsync("Private", guests: 1, visibility: "private");
        (await reader.GetAsync($"/api/v1/trip-logs/{privateTrip}/tracking")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // No account, no answer.
        (await anonymous.GetAsync($"/api/v1/trip-logs/{trip}/tracking")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The location-protection test, both halves in one place: the same armed trip, the same
    /// station report — the owner (who holds exact-location on their own cave) reads the
    /// station name back, and a reader without that right gets structure with no positions and
    /// a flag saying so. The two assertions together are what make either meaningful.
    /// </summary>
    [Fact]
    public async Task A_protected_caves_stations_reach_the_placer_and_never_a_reader_without_exact_location_rights()
    {
        var (trip, cavers) = await CreateTripAsync("Protected", guests: 1, visibility: "authenticated");
        var cave = await CreateCaveAsync(locationProtected: true);
        var model = await SeedModelWithStationsAsync(cave);

        // A watch that names no survey names no cave, to anybody.
        (await StateAsync(owner, trip)).GetProperty("caveFeatureId").ValueKind.ShouldBe(JsonValueKind.Null);

        (await PutConfigAsync(owner, trip, new
        {
            state = "armed",
            surveyModelId = model,
            referenceStationName = "cave.ent.0",
            depthFilter = new[] { "cave.upper" },
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostEventAsync(owner, trip, new
        {
            caverIds = cavers,
            kind = "atStation",
            stationName = "cave.upper.2",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Positive half: the cave's owner is allowed to place it, so the station comes back —
        // and so does the config's own station vocabulary.
        var mine = await StateAsync(owner, trip);
        mine.GetProperty("positionsWithheld").GetBoolean().ShouldBeFalse();
        mine.GetProperty("participants").EnumerateArray().Single()
            .GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
        TimeOf(mine.GetProperty("participants").EnumerateArray().Single(), "positionRecordedAt")
            .ShouldNotBeNull("the placer gets the station and the hour it was reported at");
        mine.GetProperty("referenceStationName").GetString().ShouldBe("cave.ent.0");
        mine.GetProperty("depthFilter").GetArrayLength().ShouldBe(1);
        // The cave the survey belongs to is told beside the survey, to the one who is told the survey.
        mine.GetProperty("surveyModelId").GetGuid().ShouldBe(model);
        mine.GetProperty("caveFeatureId").GetGuid().ShouldBe(cave);

        // Negative half: the reader may read the trip and still learns no station and no depth
        // — not from the fold, not from the event log, and not from the config either, whose
        // reference station and filter prefixes are station names too.
        var theirs = await StateAsync(reader, trip);
        theirs.GetProperty("positionsWithheld").GetBoolean().ShouldBeTrue();
        var folded = theirs.GetProperty("participants").EnumerateArray().Single();
        folded.GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
        folded.GetProperty("depthM").ValueKind.ShouldBe(JsonValueKind.Null);
        // The hour the position was reported at goes with the station, so that nothing downstream
        // can put an age on a place it was refused. The two assertions under it are the honest
        // bound on what that buys: the last-heard time and the kind of the last report are both
        // still sent, deliberately, because a reader who may not learn the place is still meant to
        // learn that somebody was heard from and when. This null is consistency, not a secret.
        TimeOf(folded, "positionRecordedAt").ShouldBeNull();
        folded.GetProperty("lastKind").GetString().ShouldBe("atStation");
        TimeOf(folded, "lastRecordedAt").ShouldNotBeNull();
        // What is likewise not withheld is that somebody is underground: the reader keeps the fact
        // this surface exists for, and learns no place from it.
        folded.GetProperty("in").GetBoolean().ShouldBeTrue();
        theirs.GetProperty("referenceStationName").ValueKind.ShouldBe(JsonValueKind.Null);
        theirs.GetProperty("surveyModelId").ValueKind.ShouldBe(JsonValueKind.Null);
        // Nor which cave the watch is in: withheld with the survey, on the same branch.
        theirs.GetProperty("caveFeatureId").ValueKind.ShouldBe(JsonValueKind.Null);
        theirs.GetProperty("depthFilter").GetArrayLength().ShouldBe(0);

        var log = await BodyAsync(await reader.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events"));
        var entry = log.GetProperty("items").EnumerateArray().First();
        entry.GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
        entry.GetProperty("surveyModelId").ValueKind.ShouldBe(JsonValueKind.Null);

        // And the resolver — a position computation — is closed to them outright.
        var resolve = await reader.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/resolve-depth", new { depthM = 50 });
        resolve.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// The journal the rest of the application asks tracking for says, to each reader, what the
    /// tracking screen's own two reads say to that reader — and to nobody a place those reads keep.
    /// </summary>
    /// <remarks>
    /// Two trips side by side, one in a cave whose position is protected and one in a cave that is
    /// not, read three ways: by the account that may place both caves, by a member who may read the
    /// trips and place neither protected cave, and under the reading that stands for every account
    /// at once — the one a document kept on a trip is worded for. The unprotected trip is the
    /// positive half of every refusal here: the same reader, the same reports, and the station told.
    /// </remarks>
    [Fact]
    public async Task The_journal_handed_to_the_rest_of_the_application_is_what_the_tracking_reads_answer_the_same_reader()
    {
        var guarded = await JournalledTripAsync(locationProtected: true);
        var open = await JournalledTripAsync(locationProtected: false);

        // The account that may place the cave: everything, on both, and nothing marked.
        foreach (var trip in new[] { guarded, open })
        {
            var mine = (await JournalAsync(trip, reading: ownerId, namedFor: ownerId)).ShouldNotBeNull();
            await ShouldBeWhatTheScreenAnswersAsync(mine, owner, trip);
            mine.AnyWithheld.ShouldBeFalse();
            mine.Entries.ShouldAllBe(entry => !entry.Withheld);
            mine.Entries.Count.ShouldBe(6, "two entries, a station, a depth, a note and an exit");
            mine.Entries.Select(entry => entry.Station).OfType<string>()
                .ShouldBe(new[] { "cave.upper.2", "cave.deep.3" });
            mine.Entries.Single(entry => entry.Kind == TripPositionEventKind.AtDepth).DepthM.ShouldBe(118);
            // The report taken off the log is in neither: it named a station nothing else names.
            JsonSerializer.Serialize(mine).ShouldNotContain("cave.parallel.2");
            mine.People.ShouldAllBe(person => person.Name != null && person.Name.StartsWith("Guest "));
            mine.People.ShouldAllBe(person => !person.PlaceWithheld);
            mine.State.ShouldBe(TripTrackingState.Closed);
            mine.StartedAt.ShouldNotBeNull();
            mine.ClosedAt.ShouldNotBeNull();
        }

        // The member who may read the trip and not place the protected cave.
        var theirsGuarded = (await JournalAsync(guarded, reading: readerId, namedFor: readerId)).ShouldNotBeNull();
        await ShouldBeWhatTheScreenAnswersAsync(theirsGuarded, reader, guarded);
        ShouldStateNoPlace(theirsGuarded);
        var theirsOpen = (await JournalAsync(open, reading: readerId, namedFor: readerId)).ShouldNotBeNull();
        await ShouldBeWhatTheScreenAnswersAsync(theirsOpen, reader, open);
        theirsOpen.AnyWithheld.ShouldBeFalse();
        theirsOpen.Entries.Select(entry => entry.Station).OfType<string>().ShouldBe(new[] { "cave.upper.2", "cave.deep.3" });

        // Every account at once — and named for the account that may place the cave, which is how
        // a document kept on a trip is made: whoever asked for it lends it names and no rights.
        var anyGuarded = (await JournalAsync(guarded, reading: null, namedFor: ownerId)).ShouldNotBeNull();
        ShouldStateNoPlace(anyGuarded);
        anyGuarded.People.ShouldAllBe(person => person.Name != null);
        var anyOpen = (await JournalAsync(open, reading: null, namedFor: ownerId)).ShouldNotBeNull();
        anyOpen.AnyWithheld.ShouldBeFalse();
        anyOpen.Entries.Select(entry => entry.Station).OfType<string>().ShouldBe(new[] { "cave.upper.2", "cave.deep.3" });
        anyOpen.People.Select(person => person.Station).ShouldBe(theirsOpen.People.Select(person => person.Station));

        // With no account to name people for, nobody is named — and nothing else moves.
        var unnamed = (await JournalAsync(open, reading: ownerId, namedFor: null)).ShouldNotBeNull();
        unnamed.People.ShouldAllBe(person => person.Name == null);
        unnamed.Entries.ShouldAllBe(entry => entry.Name == null);
        unnamed.Entries.Select(entry => entry.Station).OfType<string>().ShouldBe(new[] { "cave.upper.2", "cave.deep.3" });
    }

    /// <summary>
    /// A trip nobody followed has no journal, and neither has a deleted one — whose watch and
    /// reports are all still stored.
    /// </summary>
    [Fact]
    public async Task A_trip_never_followed_has_no_journal_and_a_deleted_trip_answers_nothing()
    {
        var (plain, _) = await CreateTripAsync("Never followed", guests: 1);
        (await JournalAsync(plain, reading: ownerId, namedFor: ownerId)).ShouldBeNull();

        var followed = await JournalledTripAsync(locationProtected: false);
        (await JournalAsync(followed, reading: ownerId, namedFor: ownerId)).ShouldNotBeNull();

        (await owner.DeleteAsync($"/api/v1/trip-logs/{followed}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await JournalAsync(followed, reading: ownerId, namedFor: ownerId)).ShouldBeNull();
        (await JournalAsync(followed, reading: null, namedFor: ownerId)).ShouldBeNull();
        // Nothing was destroyed to get that answer: the rows are there, behind the trip.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.TripPositionEvents.IgnoreQueryFilters().CountAsync(e => e.TripLogId == followed))
            .ShouldBeGreaterThan(0);
        (await db.TripTrackings.IgnoreQueryFilters().CountAsync(t => t.TripLogId == followed)).ShouldBe(1);
    }

    /// <summary>
    /// A finished watch with a little of everything on its log: both people in, one placed at a
    /// station and one at a depth, a note, an exit — and one report taken off again, at a station
    /// no other report names.
    /// </summary>
    private async Task<Guid> JournalledTripAsync(bool locationProtected)
    {
        var (trip, cavers) = await CreateTripAsync("Journalled", guests: 2);
        var cave = await CreateCaveAsync(locationProtected);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await ReportAsync(trip, new { caverIds = cavers, kind = "entered" }, At(9, 0));
        await ReportAsync(
            trip, new { caverIds = new[] { cavers[0] }, kind = "atStation", stationName = "cave.upper.2" }, At(10, 0));
        var mistaken = await PostEventAsync(owner, trip, new
        {
            caverIds = new[] { cavers[0] }, kind = "atStation", stationName = "cave.parallel.2", recordedAt = At(10, 30),
        });
        mistaken.StatusCode.ShouldBe(HttpStatusCode.OK, await mistaken.Content.ReadAsStringAsync());
        await ReportAsync(trip, new { caverIds = new[] { cavers[1] }, kind = "atDepth", depthM = 118 }, At(11, 0));
        await ReportAsync(
            trip, new { caverIds = new[] { cavers[0] }, kind = "note", note = "Rigging the second pitch" }, At(11, 30));
        await ReportAsync(trip, new { caverIds = new[] { cavers[0] }, kind = "exited" }, At(12, 0));

        (await owner.DeleteAsync(EventOf(trip, (await BodyAsync(mistaken))[0].GetProperty("id").GetGuid())))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PutConfigAsync(owner, trip, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        return trip;
    }

    /// <summary>
    /// The journal as the application's own seam answers it: for one account's rights, or — with
    /// no account given — for the rights every account holds, and named for whoever is given.
    /// </summary>
    private async Task<TripTrackingJournal?> JournalAsync(Guid trip, Guid? reading, Guid? namedFor)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var rights = reading is { } account
            ? await AccessContextResolver.ResolveAsync(db, account)
            : await AccessContextResolver.ResolveForAnyAccountAsync(db);
        return await scope.ServiceProvider.GetRequiredService<ITripTrackingJournal>().ReadAsync(
            trip, rights, namedFor is { } named ? new UserContext(named, []) : null, CancellationToken.None);
    }

    /// <summary>
    /// Field by field against the state read and the log read this client is answered over HTTP:
    /// the watch, each person in the screen's order, each report in time order.
    /// </summary>
    private static async Task ShouldBeWhatTheScreenAnswersAsync(TripTrackingJournal journal, HttpClient client, Guid trip)
    {
        static string Wire<T>(T value) where T : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
        static string? TextOf(JsonElement element, string property) =>
            element.GetProperty(property).ValueKind == JsonValueKind.Null ? null : element.GetProperty(property).GetString();
        static decimal? NumberOf(JsonElement element, string property) =>
            element.GetProperty(property).ValueKind == JsonValueKind.Null ? null : element.GetProperty(property).GetDecimal();

        var state = await StateAsync(client, trip);
        Wire(journal.State).ShouldBe(state.GetProperty("state").GetString());
        journal.StartedAt.ShouldBe(TimeOf(state, "firstArmedAt"));
        journal.ClosedAt.ShouldBe(TimeOf(state, "closedAt"));

        var folded = state.GetProperty("participants").EnumerateArray().ToList();
        journal.People.Select(person => person.CaverId).ShouldBe(folded.Select(p => p.GetProperty("caverId").GetGuid()));
        foreach (var (person, theirs) in journal.People.Zip(folded))
        {
            person.Station.ShouldBe(TextOf(theirs, "stationName"));
            person.DepthM.ShouldBe(NumberOf(theirs, "depthM"));
            person.PlaceAt.ShouldBe(TimeOf(theirs, "positionRecordedAt"));
            person.LastHeardAt.ShouldBe(TimeOf(theirs, "lastRecordedAt"));
            (person.Standing == TripStanding.Underground).ShouldBe(theirs.GetProperty("in").GetBoolean());
            (person.Standing == TripStanding.Out).ShouldBe(theirs.GetProperty("out").GetBoolean());
            person.OnRoster.ShouldBe(theirs.GetProperty("onRoster").GetBoolean());
            ((decimal?)person.Number).ShouldBe(NumberOf(theirs, "ordinal"));
        }

        // The screen lists the newest report first; the journal is read from the beginning.
        var log = await LogAsync(client, trip);
        log.Reverse();
        journal.Entries.Count.ShouldBe(log.Count);
        foreach (var (entry, told) in journal.Entries.Zip(log))
        {
            entry.At.ShouldBe(told.GetProperty("recordedAt").GetDateTimeOffset());
            entry.CaverId.ShouldBe(told.GetProperty("caverId").GetGuid());
            Wire(entry.Kind).ShouldBe(told.GetProperty("kind").GetString());
            entry.Station.ShouldBe(TextOf(told, "stationName"));
            entry.DepthM.ShouldBe(NumberOf(told, "depthEnteredM"));
            entry.Note.ShouldBe(TextOf(told, "note"));
        }
    }

    /// <summary>
    /// A journal of the protected trip as somebody who may not place its cave is told it: every
    /// report and every person is there, with its hour, and no station or depth anywhere in it.
    /// </summary>
    private static void ShouldStateNoPlace(TripTrackingJournal journal)
    {
        journal.AnyWithheld.ShouldBeTrue();
        journal.Entries.Count.ShouldBe(6);
        foreach (var entry in journal.Entries)
        {
            var saysWhere = entry.Kind is TripPositionEventKind.AtStation or TripPositionEventKind.AtDepth;
            entry.Withheld.ShouldBe(saysWhere);
            entry.Station.ShouldBeNull();
            entry.DepthM.ShouldBeNull();
        }

        // What is not a place stays: the note, and who is in and who is out.
        journal.Entries.Single(entry => entry.Kind == TripPositionEventKind.Note).Note.ShouldBe("Rigging the second pitch");
        journal.People.Count.ShouldBe(2);
        journal.People.ShouldAllBe(person => person.PlaceWithheld && person.Station == null && person.DepthM == null && person.PlaceAt == null);
        journal.People.ShouldAllBe(person => person.LastHeardAt != null);
        journal.People.Select(person => person.Standing).ShouldBe(
            new[] { TripStanding.Out, TripStanding.Underground }, ignoreOrder: true);
        // And on the bytes, whichever member a later change might put a station in.
        JsonSerializer.Serialize(journal).ShouldNotContain("cave.");
    }

    /// <summary>
    /// The anchor test: protection is evaluated against the cave snapshot on the row, so it
    /// outlives the model — an entitled reader keeps their history, an unentitled one still
    /// gets nothing — and when even the anchor is gone the row is withheld from everyone.
    /// Fail closed, never open.
    /// </summary>
    [Fact]
    public async Task Withholding_survives_the_model_being_deleted_and_fails_closed_when_the_anchor_is_gone_too()
    {
        var (trip, cavers) = await CreateTripAsync("Anchors", guests: 1, visibility: "authenticated");
        var cave = await CreateCaveAsync(locationProtected: true);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostEventAsync(owner, trip, new
        {
            caverIds = cavers,
            kind = "atStation",
            stationName = "cave.deep.3",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The model goes away (a re-survey replaced it); its FK nulls out on the events.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var modelRow = await db.SurveyModels.SingleAsync(m => m.Id == model);
            db.SurveyModels.Remove(modelRow);
            await db.SaveChangesAsync();
        }

        // The cave snapshot still answers: the owner keeps reading their history, the
        // reader still learns nothing.
        var mine = await StateAsync(owner, trip);
        mine.GetProperty("participants").EnumerateArray().Single()
            .GetProperty("stationName").GetString().ShouldBe("cave.deep.3");
        var theirs = await StateAsync(reader, trip);
        theirs.GetProperty("positionsWithheld").GetBoolean().ShouldBeTrue();
        theirs.GetProperty("participants").EnumerateArray().Single()
            .GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);

        // Sever the anchor itself: with nothing left to evaluate protection against, the
        // position is withheld from the owner too.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.TripPositionEvents
                .Where(e => e.TripLogId == trip)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.CaveFeatureId, (Guid?)null));
        }
        var orphaned = await StateAsync(owner, trip);
        orphaned.GetProperty("positionsWithheld").GetBoolean().ShouldBeTrue();
        var withheld = orphaned.GetProperty("participants").EnumerateArray().Single();
        withheld.GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
        // Fail closed on the whole position, its hour included — the time is withheld on exactly
        // the branch the station is, so there is no anchor state in which one survives the other.
        TimeOf(withheld, "positionRecordedAt").ShouldBeNull();
    }

    // ---- standing, and the age of a place ---------------------------------------------------

    /// <summary>
    /// Standing follows the last report that spoke to it, and a note never speaks.
    /// </summary>
    /// <remarks>
    /// Every caver here is a twin of another: the one whose note follows an exit against the one
    /// whose note follows an entry, and the one whose only word is a note against the same person
    /// once they have gone in. A test that only showed the note leaving somebody out would pass on
    /// a server that had simply stopped reading reports at all — the pairs are what say the fold
    /// still moves when something that speaks to presence arrives.
    /// </remarks>
    [Fact]
    public async Task A_note_moves_nobody_between_standings_in_either_direction()
    {
        var (trip, cavers) = await CreateTripAsync("Standing", guests: 4);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Everybody but the first goes in; word about the first arrives before the party sets off.
        await ReportAsync(trip, new { caverIds = new[] { cavers[0] }, kind = "note", note = "will be late" }, At(8, 0));
        await ReportAsync(trip, new { caverIds = new[] { cavers[1], cavers[2], cavers[3] }, kind = "entered" }, At(9, 0));
        await ReportAsync(trip, new { caverIds = new[] { cavers[1] }, kind = "note", note = "asked for rope" }, At(9, 5));
        await ReportAsync(trip, new { caverIds = new[] { cavers[2], cavers[3] }, kind = "exited" }, At(17, 0));
        await ReportAsync(trip, new { caverIds = new[] { cavers[2] }, kind = "note", note = "got a lift home" }, At(17, 5));

        var state = await StateAsync(owner, trip);

        // Nothing has placed the first one or said they went anywhere. Neither in nor out is a
        // state of its own: somebody still in the car park must not be counted underground.
        var waiting = Participant(state, cavers[0]);
        waiting.GetProperty("lastKind").GetString().ShouldBe("note");
        TimeOf(waiting, "lastRecordedAt").ShouldBe(At(8, 0));
        waiting.GetProperty("in").GetBoolean().ShouldBeFalse();
        waiting.GetProperty("out").GetBoolean().ShouldBeFalse();

        // A note about somebody underground leaves them underground.
        var inside = Participant(state, cavers[1]);
        inside.GetProperty("lastKind").GetString().ShouldBe("note");
        inside.GetProperty("in").GetBoolean().ShouldBeTrue();
        inside.GetProperty("out").GetBoolean().ShouldBeFalse();

        // And a note about somebody already out leaves them out. The assertion on lastKind is
        // load-bearing: without it this would pass on a server where the note never landed.
        var home = Participant(state, cavers[2]);
        home.GetProperty("lastKind").GetString().ShouldBe("note");
        TimeOf(home, "lastRecordedAt").ShouldBe(At(17, 5));
        home.GetProperty("out").GetBoolean().ShouldBeTrue();
        home.GetProperty("in").GetBoolean().ShouldBeFalse();

        // The control nobody wrote a note about: the exit itself still works.
        var plainlyOut = Participant(state, cavers[3]);
        plainlyOut.GetProperty("lastKind").GetString().ShouldBe("exited");
        plainlyOut.GetProperty("out").GetBoolean().ShouldBeTrue();

        // The twin of the first assertion: the moment something that does speak to presence
        // arrives, the same person moves.
        await ReportAsync(trip, new { caverIds = new[] { cavers[0] }, kind = "entered" }, At(18, 0));
        var arrived = Participant(await StateAsync(owner, trip), cavers[0]);
        arrived.GetProperty("in").GetBoolean().ShouldBeTrue();
        arrived.GetProperty("out").GetBoolean().ShouldBeFalse();
    }

    /// <summary>
    /// A station report places somebody nobody recorded entering, and does not undo a recorded
    /// exit — only a recorded entry does that.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both halves of one decision, so neither can be weakened without the other failing. A place
    /// has to answer on its own or a party whose station is on the screen reads as never heard
    /// from; and a place must not answer <em>over</em> an exit, because a report's time is the
    /// clock at the moment it was typed unless somebody set it, so "last seen at the bottom
    /// pitch", entered while tidying the log at 12:30, sorts after the 12:00 exit it is describing
    /// the run-up to. Under the other rule that report alone would move somebody already home back
    /// into the underground count, on a page their family is watching, with nothing on the page to
    /// explain it.
    /// </para>
    /// <para>
    /// The last three cavers are the triple that makes it a decision rather than an accident: the
    /// same exit, then a report claiming a place, a report claiming none, and a recorded entry.
    /// Only the third moves anybody. If the first moved too the exit would be undoable by
    /// accident; if the third did not, standing would simply have frozen after the first exit.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_station_report_places_the_unentered_and_only_a_recorded_entry_undoes_an_exit()
    {
        var (trip, cavers) = await CreateTripAsync("Presence", guests: 4);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Word arrives by relayed phone call, and what gets relayed first is routinely where a
        // team is rather than that they went in. Nobody recorded an entry for this one at all.
        await ReportAsync(
            trip,
            new { caverIds = new[] { cavers[0] }, kind = "atStation", stationName = "cave.upper.2" },
            At(9, 30));

        await ReportAsync(trip, new { caverIds = new[] { cavers[1], cavers[2], cavers[3] }, kind = "entered" }, At(9, 0));
        await ReportAsync(trip, new { caverIds = new[] { cavers[1], cavers[2], cavers[3] }, kind = "exited" }, At(12, 0));

        var afterExit = await StateAsync(owner, trip);

        // The one nobody recorded an entry for is underground, not unheard-from, and their station
        // is on the screen beside it.
        var placed = Participant(afterExit, cavers[0]);
        placed.GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
        placed.GetProperty("in").GetBoolean().ShouldBeTrue();
        placed.GetProperty("out").GetBoolean().ShouldBeFalse();

        // All three of the others are out, which is what the next three reports are measured
        // against.
        Participant(afterExit, cavers[1]).GetProperty("out").GetBoolean().ShouldBeTrue();
        Participant(afterExit, cavers[2]).GetProperty("out").GetBoolean().ShouldBeTrue();
        Participant(afterExit, cavers[3]).GetProperty("out").GetBoolean().ShouldBeTrue();

        // Retrospective word about where one of them was, typed after they came out and stamped
        // with the hour it was typed — the ordinary shape of somebody completing a log.
        await ReportAsync(
            trip,
            new { caverIds = new[] { cavers[1] }, kind = "atStation", stationName = "cave.deep.3" },
            At(12, 30));
        // The second gets word that claims no place at all, at the same hour.
        await ReportAsync(trip, new { caverIds = new[] { cavers[2] }, kind = "note", note = "called in" }, At(12, 30));
        // The third really did go back in, and somebody said so.
        await ReportAsync(trip, new { caverIds = new[] { cavers[3] }, kind = "entered" }, At(12, 30));

        var afterWord = await StateAsync(owner, trip);

        // The station lands, is shown, and is the freshest place known about them — and they stay
        // out. Reporting where somebody was is not a claim that they are back underground.
        var backfilled = Participant(afterWord, cavers[1]);
        backfilled.GetProperty("stationName").GetString().ShouldBe("cave.deep.3");
        TimeOf(backfilled, "positionRecordedAt").ShouldBe(At(12, 30));
        backfilled.GetProperty("out").GetBoolean().ShouldBeTrue();
        backfilled.GetProperty("in").GetBoolean().ShouldBeFalse();

        var noted = Participant(afterWord, cavers[2]);
        noted.GetProperty("lastKind").GetString().ShouldBe("note");
        noted.GetProperty("out").GetBoolean().ShouldBeTrue();
        noted.GetProperty("in").GetBoolean().ShouldBeFalse();

        // And the twin without which the two above would pass on a server that had simply stopped
        // folding after the first exit: saying they went in puts them back in.
        var wentBackIn = Participant(afterWord, cavers[3]);
        wentBackIn.GetProperty("in").GetBoolean().ShouldBeTrue();
        wentBackIn.GetProperty("out").GetBoolean().ShouldBeFalse();
    }

    /// <summary>
    /// A position carries its own time, and a later report that claims no place does not move it.
    /// </summary>
    /// <remarks>
    /// The twin is the first read: while the station <em>is</em> the latest word, the two times
    /// agree, so the second read's disagreement is the note arriving and nothing else. Without that
    /// first half, a server that simply never filled the field in would pass.
    /// </remarks>
    [Fact]
    public async Task A_positions_time_is_its_own_report_and_a_later_note_does_not_age_it()
    {
        var (trip, cavers) = await CreateTripAsync("Ages", guests: 2);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await ReportAsync(trip, new { caverIds = cavers, kind = "entered" }, At(9, 0));
        await ReportAsync(
            trip,
            new { caverIds = new[] { cavers[0] }, kind = "atStation", stationName = "cave.upper.2" },
            At(9, 30));

        // While the station is the latest word about them, the two times are the same report.
        var fresh = Participant(await StateAsync(owner, trip), cavers[0]);
        fresh.GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
        TimeOf(fresh, "lastRecordedAt").ShouldBe(At(9, 30));
        TimeOf(fresh, "positionRecordedAt").ShouldBe(At(9, 30));

        // Somebody nothing has placed has no position time at all, and still has a last word.
        var unplaced = Participant(await StateAsync(owner, trip), cavers[1]);
        unplaced.GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
        TimeOf(unplaced, "positionRecordedAt").ShouldBeNull();
        TimeOf(unplaced, "lastRecordedAt").ShouldBe(At(9, 0));

        // A note lands four hours later. The station is four hours old and has to keep saying so:
        // dating it by the note would present a place the party left as one they are at.
        await ReportAsync(trip, new { caverIds = new[] { cavers[0] }, kind = "note", note = "asked for rope" }, At(13, 30));

        var aged = Participant(await StateAsync(owner, trip), cavers[0]);
        aged.GetProperty("lastKind").GetString().ShouldBe("note");
        aged.GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
        TimeOf(aged, "lastRecordedAt").ShouldBe(At(13, 30));
        TimeOf(aged, "positionRecordedAt").ShouldBe(At(9, 30));
    }

    // ---- refusals that keep the log honest -------------------------------------------------

    [Fact]
    public async Task Confused_reports_are_refused_rather_than_reinterpreted()
    {
        var (trip, cavers) = await CreateTripAsync("Refusals", guests: 1);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A station report with no station, and a depth on a station report: validator refusals.
        var missing = await PostEventAsync(owner, trip, new { caverIds = cavers, kind = "atStation" });
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await missing.Content.ReadAsStringAsync()).ShouldContain("validation.failed");
        var confused = await PostEventAsync(owner, trip, new
        {
            caverIds = cavers,
            kind = "atStation",
            stationName = "cave.upper.2",
            depthM = 50,
        });
        confused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // A station the model does not have, a caver the roster does not name, a report from
        // the future — each named, none guessed at.
        var unknown = await PostEventAsync(owner, trip, new
        {
            caverIds = cavers,
            kind = "atStation",
            stationName = "cave.invented.9",
        });
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadAsStringAsync()).ShouldContain("tracking.station_unknown");

        var outsider = await PostEventAsync(owner, trip, new { caverIds = new[] { Guid.NewGuid() }, kind = "entered" });
        outsider.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await outsider.Content.ReadAsStringAsync()).ShouldContain("tracking.caver_not_participant");

        var future = await PostEventAsync(owner, trip, new
        {
            caverIds = cavers,
            kind = "entered",
            recordedAt = DateTimeOffset.UtcNow.AddHours(3),
        });
        future.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await future.Content.ReadAsStringAsync()).ShouldContain("tracking.recorded_in_future");

        // Arming needs a model, and the lifecycle never returns to off.
        var (bare, _) = await CreateTripAsync("Bare", guests: 1);
        var modelless = await PutConfigAsync(owner, bare, new { state = "armed" });
        modelless.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await modelless.Content.ReadAsStringAsync()).ShouldContain("tracking.model_missing");
        var backOff = await PutConfigAsync(owner, trip, new { state = "off" });
        backOff.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await backOff.Content.ReadAsStringAsync()).ShouldContain("tracking.state_invalid");

        // The config write rides the trip's version like every other trip-level arrangement.
        var unconditional = await owner.PutAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking", new { state = "armed" });
        unconditional.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
    }

    [Fact]
    public async Task Teams_are_labels_that_come_and_go_without_touching_history()
    {
        var (trip, cavers) = await CreateTripAsync("Teams", guests: 1);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var made = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/teams", new { title = "Echipa 1" });
        made.StatusCode.ShouldBe(HttpStatusCode.OK);
        var teamId = (await BodyAsync(made)).GetProperty("id").GetGuid();

        (await PostEventAsync(owner, trip, new { caverIds = cavers, kind = "entered", teamId }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StateAsync(owner, trip)).GetProperty("participants").EnumerateArray().Single()
            .GetProperty("teamId").GetGuid().ShouldBe(teamId);

        // Deleting the team degrades the label on history and deletes nothing else.
        (await owner.DeleteAsync($"/api/v1/trip-logs/{trip}/tracking/teams/{teamId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var after = (await StateAsync(owner, trip)).GetProperty("participants").EnumerateArray().Single();
        after.GetProperty("teamId").ValueKind.ShouldBe(JsonValueKind.Null);
        after.GetProperty("lastKind").GetString().ShouldBe("entered");
    }

    // ---- the survey under a live watch ------------------------------------------------------

    /// <summary>
    /// A survey a party is being followed against cannot be deleted out from under them, and
    /// becomes deletable again the moment nobody is being followed against it.
    /// </summary>
    /// <remarks>
    /// The refusal is the point and the second half is what makes it a rule rather than a block:
    /// a model of a cave somebody once tracked a trip in must not become undeletable for ever.
    /// </remarks>
    [Fact]
    public async Task The_survey_an_armed_watch_names_cannot_be_deleted_until_the_watch_is_closed()
    {
        var (trip, cavers) = await CreateTripAsync("Delete under a watch", guests: 1);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostEventAsync(owner, trip, new
        {
            caverIds = cavers,
            kind = "atStation",
            stationName = "cave.deep.3",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var refused = await owner.DeleteAsync($"/api/v1/survey-models/{model}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("survey_model.tracking_armed");

        // Refused means not deleted: the watch is still armed on a survey that is still here, and
        // a report still lands. A refusal that had removed the row anyway would pass a status
        // check and leave the co-ordinator exactly where this test exists to keep them out of.
        var still = await StateAsync(owner, trip);
        still.GetProperty("state").GetString().ShouldBe("armed");
        still.GetProperty("surveyModelId").GetGuid().ShouldBe(model);
        still.GetProperty("surveyModelMissing").GetBoolean().ShouldBeFalse();
        (await PostEventAsync(owner, trip, new
        {
            caverIds = cavers,
            kind = "atStation",
            stationName = "cave.upper.2",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The twin: nobody is being followed against it now, so it goes.
        (await PutConfigAsync(owner, trip, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.DeleteAsync($"/api/v1/survey-models/{model}")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// A watch whose survey has gone anyway says so, keeps saying which survey it was, and refuses
    /// to be armed again on the reference to nothing.
    /// </summary>
    /// <remarks>
    /// The route that would do this is refused above, so the model is removed here the way an
    /// operator or a future path could: straight out of the table. What is asserted is the state
    /// the application is left in — armed, useless, and <em>saying so</em>. The reference itself
    /// survives on purpose (the column carries no foreign key any more): blanked, it would arrive
    /// as the same null a caller who may not be told the configuration is sent, and the surface
    /// could not tell "the survey was deleted" from "you are not being told which survey".
    /// </remarks>
    [Fact]
    public async Task A_watch_whose_survey_was_deleted_says_so_instead_of_reading_as_one_that_never_had_one()
    {
        var (trip, cavers) = await CreateTripAsync("Survey gone", guests: 1);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostEventAsync(owner, trip, new
        {
            caverIds = cavers,
            kind = "atStation",
            stationName = "cave.deep.3",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The twin, before anything is removed: this flag is about the condition and not about
        // being armed, so it has to be false on the watch that is working.
        (await StateAsync(owner, trip)).GetProperty("surveyModelMissing").GetBoolean().ShouldBeFalse();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var row = await db.SurveyModels.SingleAsync(m => m.Id == model);
            db.SurveyModels.Remove(row);
            await db.SaveChangesAsync();
        }

        var after = await StateAsync(owner, trip);
        after.GetProperty("state").GetString().ShouldBe("armed");
        after.GetProperty("surveyModelMissing").GetBoolean().ShouldBeTrue();
        // The id is still there — that is what makes the flag readable as a condition rather than
        // as one more absence.
        after.GetProperty("surveyModelId").GetGuid().ShouldBe(model);

        // History is untouched by the survey going: the report still names its station and still
        // names the survey it was measured in, which is how nothing downstream draws it elsewhere.
        var folded = after.GetProperty("participants").EnumerateArray().Single();
        folded.GetProperty("stationName").GetString().ShouldBe("cave.deep.3");
        folded.GetProperty("positionSurveyModelId").GetGuid().ShouldBe(model);

        // And the watch cannot be re-armed onto the reference to nothing. Closing is allowed —
        // ending a watch is never blocked — but arming again takes a survey that exists.
        (await PutConfigAsync(owner, trip, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var rearm = await PutConfigAsync(owner, trip, new { state = "armed" });
        rearm.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await rearm.Content.ReadAsStringAsync()).ShouldContain("tracking.model_missing");
    }

    /// <summary>
    /// Replacing the survey under an armed watch is allowed, and every report already on the log
    /// keeps naming the survey it was made against rather than being re-read on the new one.
    /// </summary>
    /// <remarks>
    /// The two surveys here spell their stations identically, which is the whole danger: a report
    /// of <c>cave.deep.3</c> measured in the first one names a place the second one also has a
    /// name for, and nothing in the string says which was meant. The read hands over the survey
    /// each position was measured in so that no surface has to guess.
    /// </remarks>
    [Fact]
    public async Task A_place_reported_before_the_survey_changed_keeps_naming_the_survey_it_was_measured_in()
    {
        var (trip, cavers) = await CreateTripAsync("Survey replaced", guests: 1);
        var cave = await CreateCaveAsync(locationProtected: false);
        var first = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, first)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostEventAsync(owner, trip, new
        {
            caverIds = cavers,
            kind = "atStation",
            stationName = "cave.deep.3",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A corrected survey of the same cave arrives and the co-ordinator moves the watch onto
        // it. This is allowed: a re-import mid-trip is a real thing, and an application that
        // refused it would be answered by closing the watch.
        var second = await SeedModelWithStationsAsync(cave);
        var moved = await PutConfigAsync(owner, trip, new { state = "armed", surveyModelId = second });
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());

        var after = await StateAsync(owner, trip);
        after.GetProperty("surveyModelId").GetGuid().ShouldBe(second);
        var stale = after.GetProperty("participants").EnumerateArray().Single();
        // The name is kept — it is a true record of a report, and the log a co-ordinator reads
        // goes on showing it — while the survey beside it is the one that was measured in.
        stale.GetProperty("stationName").GetString().ShouldBe("cave.deep.3");
        stale.GetProperty("positionSurveyModelId").GetGuid().ShouldBe(first);
        stale.GetProperty("positionSurveyModelId").GetGuid().ShouldNotBe(second);

        // The twin, and the half that says the watch still works: a report made now is measured in
        // the survey now in use and comes back saying so.
        (await PostEventAsync(owner, trip, new
        {
            caverIds = cavers,
            kind = "atStation",
            stationName = "cave.upper.2",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var fresh = (await StateAsync(owner, trip)).GetProperty("participants").EnumerateArray().Single();
        fresh.GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
        fresh.GetProperty("positionSurveyModelId").GetGuid().ShouldBe(second);
    }

    /// <summary>
    /// An armed watch may be moved to another survey of its own cave and may not be moved to a
    /// survey of a different one.
    /// </summary>
    /// <remarks>
    /// A party is in one cave, and that cave is also the anchor every position on the log is
    /// protected by — so a swap that crosses caves either points a live watch at a place the party
    /// is not, or moves the protection anchor of the configuration's own station vocabulary to a
    /// cave nobody decided that about. Neither is visible afterwards on the screen it happens on.
    /// </remarks>
    [Fact]
    public async Task An_armed_watch_moves_survey_inside_its_cave_and_is_refused_a_survey_of_another()
    {
        var (trip, _) = await CreateTripAsync("Cave swap", guests: 1);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        var elsewhere = await CreateCaveAsync(locationProtected: false);
        var otherCavesModel = await SeedModelWithStationsAsync(elsewhere);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var refused = await PutConfigAsync(owner, trip, new
        {
            state = "armed",
            surveyModelId = otherCavesModel,
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("tracking.model_other_cave");
        // Refused means unchanged, not half-applied: the watch is still on the survey it was armed
        // on, in the cave the party is actually in.
        (await StateAsync(owner, trip)).GetProperty("surveyModelId").GetGuid().ShouldBe(model);

        // The twin: another survey of the same cave is exactly what this must not get in the way
        // of, and it is allowed while the watch is armed.
        var corrected = await SeedModelWithStationsAsync(cave);
        (await PutConfigAsync(owner, trip, new { state = "armed", surveyModelId = corrected }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // And the refusal is about following a party, not about the configuration: once the watch
        // is closed it may be pointed anywhere.
        (await PutConfigAsync(owner, trip, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PutConfigAsync(owner, trip, new { state = "closed", surveyModelId = otherCavesModel }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// The predicate is asked about the state the watch will be in, so ending a watch and pointing
    /// it elsewhere in one act is allowed, and arming a closed one straight onto another cave is
    /// not.
    /// </summary>
    /// <remarks>
    /// Asked about the state the watch is <em>in</em>, both answers come out backwards: a single
    /// write that says "closed, and here is another cave's survey" is refused for a fact about the
    /// past, while a closed watch is allowed to arm onto a different cave — which moves the anchor
    /// every position on its log is protected by, to a cave nobody decided that about. The rule
    /// itself is tested next door; what is tested here is which state the call site hands it,
    /// which no test of the rule can see.
    /// </remarks>
    [Fact]
    public async Task A_watch_may_be_closed_and_re_pointed_in_one_act_and_may_not_arm_onto_another_cave()
    {
        var (trip, _) = await CreateTripAsync("Close and repoint", guests: 1);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        var elsewhere = await CreateCaveAsync(locationProtected: false);
        var elsewhereModel = await SeedModelWithStationsAsync(elsewhere);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // One write, two statements: the party is no longer being followed, and the configuration
        // now names another cave's survey. Nothing is being followed by the time it applies.
        var repointed = await PutConfigAsync(owner, trip, new
        {
            state = "closed",
            surveyModelId = elsewhereModel,
        });
        repointed.StatusCode.ShouldBe(HttpStatusCode.OK, await repointed.Content.ReadAsStringAsync());
        var closed = await StateAsync(owner, trip);
        closed.GetProperty("state").GetString().ShouldBe("closed");
        closed.GetProperty("surveyModelId").GetGuid().ShouldBe(elsewhereModel);

        // And the other direction, which used to be let through: arming a closed watch straight
        // onto a third cave's survey.
        var third = await CreateCaveAsync(locationProtected: false);
        var thirdModel = await SeedModelWithStationsAsync(third);
        var refused = await PutConfigAsync(owner, trip, new
        {
            state = "armed",
            surveyModelId = thirdModel,
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("tracking.model_other_cave");
        // Refused means unchanged, not half-applied.
        var after = await StateAsync(owner, trip);
        after.GetProperty("state").GetString().ShouldBe("closed");
        after.GetProperty("surveyModelId").GetGuid().ShouldBe(elsewhereModel);

        // The twin: arming on the survey the watch is already anchored to is the ordinary act and
        // is allowed, so this is a rule about crossing caves and not about arming.
        (await PutConfigAsync(owner, trip, new { state = "armed", surveyModelId = elsewhereModel }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// The refusal that guards a live watch names the trip to a caller who may read it, so that it
    /// carries the act it is asking for rather than only the reason for refusing.
    /// </summary>
    /// <remarks>
    /// An armed watch stays armed until a person ends it, so this refusal is routinely about a
    /// trip nobody is thinking about any more. Told nothing, whoever holds it has a survey that
    /// cannot be deleted and an instruction they cannot carry out; told which trip, they go and
    /// close it. The naming goes through the trips' own visibility, so it discloses nothing a
    /// caller could not already read.
    /// </remarks>
    [Fact]
    public async Task The_refusal_over_an_armed_watch_names_the_trip_to_somebody_who_may_read_it()
    {
        var (trip, _) = await CreateTripAsync("Nameable watch", guests: 1);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var refused = await owner.DeleteAsync($"/api/v1/survey-models/{model}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var body = await refused.Content.ReadAsStringAsync();
        body.ShouldContain("survey_model.tracking_armed");
        // The trip, as a member of its own: the sentence beside it is written for a person and is
        // translated on the way to one, so a client recovering the name out of prose would break
        // the moment the prose is reworded.
        (JsonDocument.Parse(body).RootElement.GetProperty("armedTrips").GetString() ?? "")
            .ShouldContain("Nameable watch");

        // The twin, so the naming is a property of this refusal rather than of every refusal: once
        // the watch is closed the delete goes through and there is nothing to name.
        (await PutConfigAsync(owner, trip, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.DeleteAsync($"/api/v1/survey-models/{model}")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
    }

    // ---- plumbing --------------------------------------------------------------------------

    // ---- correcting what is already on the log ------------------------------------------------

    /// <summary>
    /// <b>The pair that was broken.</b> A closed watch's log takes a correction and takes a new
    /// report, where recording used to be refused on anything but an armed watch — which left a
    /// finished trip destroyable and unrepairable.
    /// </summary>
    [Fact]
    public async Task A_closed_watch_still_takes_a_correction_and_a_report_while_an_off_one_takes_neither()
    {
        var (trip, cavers) = await CreateTripAsync("Written up afterwards", guests: 1);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);

        // Off first, so the refusal below is a state being refused rather than the route missing.
        var beforeArming = await PostEventAsync(owner, trip, new { caverIds = cavers, kind = "entered" });
        beforeArming.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await beforeArming.Content.ReadAsStringAsync()).ShouldContain("tracking.not_writable");

        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var recorded = await PostEventAsync(owner, trip, new
        {
            caverIds = cavers,
            kind = "atStation",
            stationName = "cave.upper.2",
            recordedAt = At(10, 0),
        });
        recorded.StatusCode.ShouldBe(HttpStatusCode.OK);
        var eventId = (await recorded.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray().First().GetProperty("id").GetGuid();

        (await PutConfigAsync(owner, trip, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A correction, on a watch that is over. This is the act the old rule made impossible.
        var fixedUp = await PutEventAsync(owner, trip, eventId, new
        {
            kind = "atStation",
            stationName = "cave.deep.3",
            note = "read back off the tape",
            recordedAt = At(11, 30),
        });
        fixedUp.StatusCode.ShouldBe(HttpStatusCode.OK, await fixedUp.Content.ReadAsStringAsync());
        var body = await fixedUp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().ShouldBe(eventId, "a correction keeps the row it corrects");
        body.GetProperty("stationName").GetString().ShouldBe("cave.deep.3");
        body.GetProperty("note").GetString().ShouldBe("read back off the tape");
        body.GetProperty("caverId").GetGuid().ShouldBe(cavers[0], "and keeps its subject");

        // And a report that was never written down at all can still be added afterwards, which is
        // the other half of being able to write a trip up from notes.
        var added = await PostEventAsync(owner, trip, new
        {
            caverIds = cavers,
            kind = "exited",
            recordedAt = At(12, 0),
        });
        added.StatusCode.ShouldBe(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());
        var addedId = (await added.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray().First().GetProperty("id").GetGuid();

        // Taking a report off is the third of the three acts one rule governs, and a closed log
        // allows it as it allows the other two.
        (await owner.DeleteAsync($"/api/v1/trip-logs/{trip}/tracking/events/{addedId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // An off watch refuses all three, with one code, on a log that holds a row — the case the
        // refusal before arming above cannot reach, because there the log was empty and a
        // correction or a removal had nothing to name. No request moves a watch into Off, so the
        // state is written directly; what is under test is the rule refusing it on every route,
        // not how a log comes to be in it (an archive imported onto a trip whose watch was never
        // started is one way).
        await SetStateAsync(trip, TripTrackingState.Off);
        HttpResponseMessage[] acts =
        [
            await PostEventAsync(owner, trip, new { caverIds = cavers, kind = "exited", recordedAt = At(12, 30) }),
            await PutEventAsync(owner, trip, eventId, new { kind = "atStation", stationName = "cave.upper.2" }),
            await owner.DeleteAsync($"/api/v1/trip-logs/{trip}/tracking/events/{eventId}"),
        ];
        foreach (var act in acts)
        {
            var payload = await act.Content.ReadAsStringAsync();
            act.StatusCode.ShouldBe(HttpStatusCode.Conflict, $"{act.RequestMessage!.Method}: {payload}");
            payload.ShouldContain("tracking.not_writable");
        }

        // Refused means untouched: the one row left is the correction, exactly as it was made.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var rows = await db.TripPositionEvents.AsNoTracking().Where(e => e.TripLogId == trip).ToListAsync();
        rows.ShouldHaveSingleItem().Id.ShouldBe(eventId);
        rows[0].ViewerStationName.ShouldBe("cave.deep.3");
    }

    /// <summary>
    /// The log says which of its rows no longer read as they were first written down — and says
    /// it of the corrected row only, to everybody who reads the trip, place or no place.
    /// </summary>
    /// <remarks>
    /// A log is what somebody said at a moment, so a reader is owed knowing that a row was changed
    /// afterwards. The mark is a yes or a no and says nothing of where anybody was, which is why it
    /// stays on a row whose station is withheld; the last half of this test holds both of those at
    /// once, for a reader the cave is protected from.
    /// </remarks>
    [Fact]
    public async Task A_corrected_report_reads_as_corrected_and_an_untouched_one_does_not()
    {
        var (trip, cavers) = await CreateTripAsync("Read back afterwards", guests: 2);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var recorded = await PostEventAsync(owner, trip, new
        {
            caverIds = cavers,
            kind = "atStation",
            stationName = "cave.upper.2",
            recordedAt = At(10, 0),
        });
        recorded.StatusCode.ShouldBe(HttpStatusCode.OK, await recorded.Content.ReadAsStringAsync());
        var written = (await recorded.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        written.Count.ShouldBe(2);
        written.ShouldAllBe(e => !e.GetProperty("corrected").GetBoolean(), "a report just written is as written");
        var fixedId = written.Single(e => e.GetProperty("caverId").GetGuid() == cavers[0]).GetProperty("id").GetGuid();
        var leftId = written.Single(e => e.GetProperty("caverId").GetGuid() == cavers[1]).GetProperty("id").GetGuid();

        async Task<Dictionary<Guid, JsonElement>> LogAsync(HttpClient client, string query = "")
        {
            var response = await client.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events{query}");
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await BodyAsync(response)).GetProperty("items").EnumerateArray()
                .ToDictionary(e => e.GetProperty("id").GetGuid());
        }

        var before = await LogAsync(owner);
        before[fixedId].GetProperty("corrected").GetBoolean().ShouldBeFalse();
        before[leftId].GetProperty("corrected").GetBoolean().ShouldBeFalse();

        // A correction that sends back exactly what the row holds changes nothing, and a row
        // nothing was changed on has not been corrected.
        var same = await PutEventAsync(owner, trip, fixedId, new
        {
            kind = "atStation",
            stationName = "cave.upper.2",
            recordedAt = At(10, 0),
        });
        same.StatusCode.ShouldBe(HttpStatusCode.OK, await same.Content.ReadAsStringAsync());
        (await same.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("corrected").GetBoolean().ShouldBeFalse();
        (await LogAsync(owner))[fixedId].GetProperty("corrected").GetBoolean().ShouldBeFalse();

        var fixedUp = await PutEventAsync(owner, trip, fixedId, new
        {
            kind = "atStation",
            stationName = "cave.deep.3",
            recordedAt = At(10, 0),
        });
        fixedUp.StatusCode.ShouldBe(HttpStatusCode.OK, await fixedUp.Content.ReadAsStringAsync());
        (await fixedUp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("corrected").GetBoolean()
            .ShouldBeTrue("the answer to a correction says what the log will say");

        var after = await LogAsync(owner);
        after[fixedId].GetProperty("corrected").GetBoolean().ShouldBeTrue();
        after[leftId].GetProperty("corrected").GetBoolean().ShouldBeFalse("only the row that was changed");

        // The log narrowed to one person, and walked a page at a time, is the same rows saying
        // the same thing — which is how a screen reaches a report older than its first page.
        var oneCaver = await LogAsync(owner, $"?caverId={cavers[1]}");
        oneCaver.Keys.ShouldBe([leftId]);
        var pages = new List<Guid>();
        foreach (var page in new[] { 1, 2 })
        {
            var response = await owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events?page={page}&pageSize=1");
            var body = await BodyAsync(response);
            body.GetProperty("totalItems").GetInt32().ShouldBe(2);
            pages.Add(body.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid());
        }
        pages.ShouldBe([fixedId, leftId], ignoreOrder: true);

        // Somebody the cave is protected from still reads that the row was corrected, and still
        // reads no station on it; the placer beside them reads both.
        await SetLocationProtectedAsync(cave, true);
        var theirs = (await LogAsync(reader))[fixedId];
        theirs.GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
        theirs.GetProperty("corrected").GetBoolean().ShouldBeTrue();
        var mine = (await LogAsync(owner))[fixedId];
        mine.GetProperty("stationName").GetString().ShouldBe("cave.deep.3");
        mine.GetProperty("corrected").GetBoolean().ShouldBeTrue();
    }

    /// <summary>
    /// A correction passes every gate the original report passed, asked again through the same code.
    /// </summary>
    [Fact]
    public async Task A_correction_is_refused_for_the_same_reasons_a_report_is()
    {
        var (trip, cavers) = await CreateTripAsync("Refusals on a correction", guests: 1);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var recorded = await PostEventAsync(owner, trip, new
        {
            caverIds = cavers, kind = "atStation", stationName = "cave.upper.2",
        });
        recorded.StatusCode.ShouldBe(HttpStatusCode.OK);
        var eventId = (await recorded.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray().First().GetProperty("id").GetGuid();

        // A station the survey does not hold, which is what stops a correction landing somewhere
        // the party never was.
        var unknown = await PutEventAsync(owner, trip, eventId, new
        {
            kind = "atStation", stationName = "nowhere.at.all",
        });
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadAsStringAsync()).ShouldContain("tracking.station_unknown");

        // A moment after now, for the same reason a report cannot claim one: relayed word is late,
        // never early.
        var ahead = await PutEventAsync(owner, trip, eventId, new
        {
            kind = "atStation", stationName = "cave.upper.2",
            recordedAt = DateTimeOffset.UtcNow.AddHours(2),
        });
        ahead.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ahead.Content.ReadAsStringAsync()).ShouldContain("tracking.recorded_in_future");

        // A team belonging to another trip.
        var (other, _) = await CreateTripAsync("Somebody else's", guests: 1);
        var strangerTeam = await owner.PostAsJsonAsync(
            $"/api/v1/trip-logs/{other}/tracking/teams", new { title = "Theirs" });
        strangerTeam.StatusCode.ShouldBe(HttpStatusCode.OK, await strangerTeam.Content.ReadAsStringAsync());
        var strangerTeamId = (await strangerTeam.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var wrongTeam = await PutEventAsync(owner, trip, eventId, new
        {
            kind = "atStation", stationName = "cave.upper.2", teamId = strangerTeamId,
        });
        wrongTeam.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await wrongTeam.Content.ReadAsStringAsync()).ShouldContain("tracking.team_not_found");

        // And the report is still exactly as it was: a refused correction changes nothing.
        var log = await owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events");
        log.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rows = (await log.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items");
        var row = rows.EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == eventId);
        row.GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
    }

    /// <summary>A report of a trip somebody may not write to is not theirs to correct either.</summary>
    [Fact]
    public async Task A_reader_who_may_not_write_the_trip_cannot_correct_its_log()
    {
        var (trip, cavers) = await CreateTripAsync("Not yours", guests: 1);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var recorded = await PostEventAsync(owner, trip, new
        {
            caverIds = cavers, kind = "atStation", stationName = "cave.upper.2",
        });
        recorded.StatusCode.ShouldBe(HttpStatusCode.OK);
        var eventId = (await recorded.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray().First().GetProperty("id").GetGuid();

        var refused = await PutEventAsync(reader, trip, eventId, new
        {
            kind = "atStation", stationName = "cave.deep.3",
        });
        // Forbidden rather than not-found, because this account may read the trip: what is being
        // refused is the writing, and pretending the trip is absent to somebody looking at it
        // would be a worse answer than the true one.
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Asked again once the watch is over, which is when a trip is written up and its log is
        // open to correction by anybody who may write the trip — so the refusal is shown to be about
        // this account's right, and not something a closed watch happens to change.
        (await PutConfigAsync(owner, trip, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PutEventAsync(reader, trip, eventId, new { kind = "atStation", stationName = "cave.deep.3" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.DeleteAsync($"/api/v1/trip-logs/{trip}/tracking/events/{eventId}"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The positive twin: the same correction, by somebody who may, on the same closed watch.
        (await PutEventAsync(owner, trip, eventId, new { kind = "atStation", stationName = "cave.deep.3" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// A report belongs to one trip and the address has to say which: asked for under another
    /// trip's address it is not found, even by somebody who may write both trips.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The right to write is checked against the trip in the address. If the report were then
    /// looked up by its own identifier alone, holding write on any one trip would be enough to
    /// rewrite or remove a report of any other — the check would have been made on a trip the
    /// report has nothing to do with. Here the caller may write both, which leaves the pairing of
    /// report and trip as the only thing that can refuse.
    /// </para>
    /// <para>
    /// Everything else about the crossed request is acceptable on purpose: the other trip's watch
    /// is running on the same survey, so the station in the correction is one it knows, and the
    /// correction is the very one that succeeds at the end under the report's own trip. Asked
    /// again once the other trip's watch is closed, because a closed watch still takes
    /// corrections and is the state a trip is usually in when its log is tidied.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_report_is_not_found_under_another_trip_the_caller_may_also_write()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        var (mine, cavers) = await CreateTripAsync("Holds the report", guests: 1);
        var (other, others) = await CreateTripAsync("Another outing", guests: 1);
        (await ArmAsync(owner, mine, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ArmAsync(owner, other, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var recorded = await PostEventAsync(owner, mine, new
        {
            caverIds = cavers, kind = "atStation", stationName = "cave.upper.2",
        });
        recorded.StatusCode.ShouldBe(HttpStatusCode.OK, await recorded.Content.ReadAsStringAsync());
        var eventId = (await BodyAsync(recorded)).EnumerateArray().Single().GetProperty("id").GetGuid();

        // The other trip has a report of its own, so that "its log is as it was" below is said of
        // a log with something in it to disturb.
        (await PostEventAsync(owner, other, new
        {
            caverIds = others, kind = "atStation", stationName = "cave.upper.2",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var mineBefore = await LogTextAsync(mine);
        var otherBefore = await LogTextAsync(other);
        mineBefore.ShouldContain(eventId.ToString());
        otherBefore.ShouldNotContain(eventId.ToString());

        async Task ShouldNotBeFoundThroughTheOtherTripAsync(string when)
        {
            var corrected = await PutEventAsync(owner, other, eventId, new
            {
                kind = "atStation", stationName = "cave.deep.3",
            });
            corrected.StatusCode.ShouldBe(HttpStatusCode.NotFound, when);
            (await BodyAsync(corrected)).GetProperty("code").GetString()
                .ShouldBe("tracking.event_not_found", when);

            var removed = await owner.DeleteAsync($"/api/v1/trip-logs/{other}/tracking/events/{eventId}");
            removed.StatusCode.ShouldBe(HttpStatusCode.NotFound, when);
            (await BodyAsync(removed)).GetProperty("code").GetString()
                .ShouldBe("tracking.event_not_found", when);
        }

        await ShouldNotBeFoundThroughTheOtherTripAsync("the other watch running");
        (await PutConfigAsync(owner, other, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ShouldNotBeFoundThroughTheOtherTripAsync("the other watch closed");

        // Nothing was written on either side: both logs read exactly as they did.
        (await LogTextAsync(mine)).ShouldBe(mineBefore);
        (await LogTextAsync(other)).ShouldBe(otherBefore);

        // The positive twin: the same two requests under the report's own trip.
        var own = await PutEventAsync(owner, mine, eventId, new
        {
            kind = "atStation", stationName = "cave.deep.3",
        });
        own.StatusCode.ShouldBe(HttpStatusCode.OK, await own.Content.ReadAsStringAsync());
        (await BodyAsync(own)).GetProperty("stationName").GetString().ShouldBe("cave.deep.3");
        (await owner.DeleteAsync($"/api/v1/trip-logs/{mine}/tracking/events/{eventId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await LogTextAsync(mine)).ShouldNotContain(eventId.ToString());
        (await LogTextAsync(other)).ShouldBe(otherBefore);
    }

    /// <summary>A trip's whole log as the text the server sent, for comparing two readings of it.</summary>
    private async Task<string> LogTextAsync(Guid trip)
    {
        var log = await owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events");
        log.StatusCode.ShouldBe(HttpStatusCode.OK, await log.Content.ReadAsStringAsync());
        return (await BodyAsync(log)).GetProperty("items").GetRawText();
    }

    // ---- the places a report can name -------------------------------------------------------

    /// <summary>
    /// The places a report can name instead of a depth: served shallowest first to whoever runs the
    /// trip, refused to a reader, and refused to a writer who may not place the cave exactly as the
    /// depth preview beside it refuses them.
    /// </summary>
    /// <remarks>
    /// A declared place names a station, so the list says where in the cave something is. The
    /// writer without the placing right is built the way it happens: another editor runs a trip on
    /// the survey while the cave is open, and the cave is protected afterwards. They still write
    /// their trip; the list is served to them before and refused after, so the refusal is the
    /// protection and nothing else — and the cave's owner, on the same cave at the same moment, is
    /// still served it.
    /// </remarks>
    [Fact]
    public async Task The_places_list_is_shallowest_first_and_guarded_as_the_depth_preview_is()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        var (trip, _) = await CreateTripAsync("Places", guests: 1);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Declared deepest first, so the order the list comes back in is the list's own doing.
        foreach (var (depth, station, label) in new[]
        {
            (120m, "cave.deep.3", "Sala Mare"),
            (50m, "cave.upper.2", "Puțul"),
        })
        {
            var declared = await owner.PutAsJsonAsync($"/api/v1/caves/{cave}/depth-places", new
            {
                depthM = depth,
                stationName = station,
                placeLabel = label,
            });
            declared.StatusCode.ShouldBe(HttpStatusCode.OK, await declared.Content.ReadAsStringAsync());
        }

        var listed = await owner.GetAsync(PlacesOf(trip));
        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await listed.Content.ReadAsStringAsync());
        (await BodyAsync(listed)).EnumerateArray()
            .Select(p => (
                p.GetProperty("depthM").GetDecimal(),
                p.GetProperty("stationName").GetString(),
                p.GetProperty("placeLabel").GetString(),
                p.GetProperty("stationInModel").GetBoolean()))
            .ShouldBe([(50m, "cave.upper.2", "Puțul", true), (120m, "cave.deep.3", "Sala Mare", true)]);

        // A reader may read the trip and is refused the list, as they are refused the preview.
        (await reader.GetAsync(PlacesOf(trip))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var guideEmail = $"trk-guide-{Guid.NewGuid():N}"[..20] + "@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, guideEmail);
        var guide = await AuthHelper.BearerClientAsync(factory, guideEmail);
        var (theirs, _) = await CreateTripAsync("Guided", guests: 1, client: guide);
        (await ArmAsync(guide, theirs, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await guide.GetAsync(PlacesOf(theirs))).StatusCode.ShouldBe(HttpStatusCode.OK);

        await SetLocationProtectedAsync(cave, true);

        var refused = await guide.GetAsync(PlacesOf(theirs));
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("tracking.model_unavailable");
        var preview = await guide.PostAsJsonAsync(
            $"/api/v1/trip-logs/{theirs}/tracking/resolve-depth", new { depthM = 50 });
        preview.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await preview.Content.ReadAsStringAsync()).ShouldContain("tracking.model_unavailable");

        (await owner.GetAsync(PlacesOf(trip))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- the roster and the log ----------------------------------------------------------

    /// <summary>
    /// The trip's form sends the whole roster back, so an edit made for any other reason can take
    /// somebody off it. While the watch runs that is refused for anybody it has reports about;
    /// once it has closed it is allowed, and the watch goes on listing them — marked, and with
    /// nothing said about what a published page would call them, because it shows them no more.
    /// </summary>
    [Fact]
    public async Task Somebody_the_log_speaks_of_cannot_leave_a_running_watch_and_stays_in_sight_once_it_has_closed()
    {
        var (trip, cavers) = await CreateTripAsync("Roster and log", guests: 3);
        var (stays, reported, silent) = (cavers[0], cavers[1], cavers[2]);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(trip, new { caverIds = new[] { stays, reported }, kind = "entered" }, At(9, 0));
        await ReportAsync(
            trip, new { caverIds = new[] { reported }, kind = "atStation", stationName = "cave.upper.2" }, At(10, 0));
        // Captioned, so that the empty caption asserted at the end is a field that had something
        // in it to withhold and not one that was empty anyway.
        (await owner.PutAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/tracking/participants/{reported}", new { label = "R." }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Nobody has said anything about the third: the party changed at the entrance, and that is
        // an ordinary edit on a running watch.
        var trimmed = await PutRosterAsync(trip, [stays, reported]);
        trimmed.StatusCode.ShouldBe(HttpStatusCode.OK, await trimmed.Content.ReadAsStringAsync());
        (await StateAsync(owner, trip)).GetProperty("participants").EnumerateArray()
            .Select(p => p.GetProperty("caverId").GetGuid())
            .ShouldNotContain(silent, "somebody with no report and no place on the trip is not on its watch");

        // A change of job is not a departure: the reported person stops being a participant and
        // becomes the proposer, which removes one roster row and adds another, and is never refused.
        var moved = await PutRosterAsync(trip, [stays], proposers: [reported]);
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        Participant(await StateAsync(owner, trip), reported).GetProperty("onRoster").GetBoolean().ShouldBeTrue();
        (await PutRosterAsync(trip, [stays, reported])).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Off the trip altogether, while the watch runs: refused, by a code, naming nobody.
        var refused = await PutRosterAsync(trip, [stays]);
        var refusal = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, refusal);
        refusal.ShouldContain("trip_log.participant_tracked");
        refusal.Contains(reported.ToString(), StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        (await RosterAsync(trip)).ShouldBe([stays, reported], ignoreOrder: true);
        var running = Participant(await StateAsync(owner, trip), reported);
        running.GetProperty("in").GetBoolean().ShouldBeTrue();
        running.GetProperty("onRoster").GetBoolean().ShouldBeTrue();
        running.GetProperty("label").GetString().ShouldBe("R.");

        // Closed: the same write goes through, and the watch still shows the person.
        (await PutConfigAsync(owner, trip, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var removed = await PutRosterAsync(trip, [stays]);
        removed.StatusCode.ShouldBe(HttpStatusCode.OK, await removed.Content.ReadAsStringAsync());
        (await RosterAsync(trip)).ShouldBe([stays]);

        var state = await StateAsync(owner, trip);
        state.GetProperty("participants").EnumerateArray()
            .Select(p => p.GetProperty("caverId").GetGuid())
            .ShouldBe([stays, reported], "the people the trip names come first, the rest after them");

        var listed = Participant(state, stays);
        listed.GetProperty("onRoster").GetBoolean().ShouldBeTrue();
        listed.GetProperty("name").ValueKind.ShouldBe(JsonValueKind.Null, "the trip itself names the people on its roster");
        listed.GetProperty("publishedAs").ValueKind.ShouldBe(JsonValueKind.String);

        var gone = Participant(state, reported);
        gone.GetProperty("onRoster").GetBoolean().ShouldBeFalse();
        gone.GetProperty("in").GetBoolean().ShouldBeTrue();
        gone.GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
        gone.GetProperty("name").GetString().ShouldBe(await CaverNameAsync(reported));
        // A published page counts the party from the roster, so it shows this person no longer —
        // and a field saying what it would call them would be describing nothing.
        gone.GetProperty("label").ValueKind.ShouldBe(JsonValueKind.Null);
        gone.GetProperty("publishedAs").ValueKind.ShouldBe(JsonValueKind.Null);

        // The log has not changed, and now agrees with the table above it.
        var log = await BodyAsync(await owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events?caverId={reported}"));
        log.GetProperty("items").GetArrayLength().ShouldBe(2);

        // Nothing more can be recorded about somebody the trip no longer names: that rule is
        // unchanged, and it is why the screen keeps such a row out of the report's candidates.
        var late = await PostEventAsync(owner, trip, new { caverIds = new[] { reported }, kind = "exited" });
        late.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await late.Content.ReadAsStringAsync()).ShouldContain("tracking.caver_not_participant");
    }

    /// <summary>
    /// The party is listed in the order the trip names it, which is the order a published page
    /// numbers it in — not in the order its people happened to be entered in the club's register.
    /// </summary>
    [Fact]
    public async Task The_party_is_listed_in_the_order_the_trip_names_it()
    {
        // Somebody already in the register, from an earlier trip.
        var (_, earlier) = await CreateTripAsync("Order, an earlier trip", guests: 1);
        var registeredFirst = earlier[0];

        // This trip names a newcomer first and the long-standing member second.
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Order {Guid.NewGuid():N}",
            tripDate = "2026-09-12",
            participants = new object[]
            {
                new { newCaverName = $"Newcomer {Guid.NewGuid():N}"[..24] },
                new { caverId = registeredFirst },
            },
            visibility = "authenticated",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var trip = (await BodyAsync(response)).GetProperty("id").GetGuid();

        var named = await RosterAsync(trip);
        named.Count.ShouldBe(2);
        named[1].ShouldBe(registeredFirst);
        // What makes this a test of the order and not a coincidence: sorted by identity, the two
        // would come out the other way round.
        registeredFirst.CompareTo(named[0]).ShouldBeLessThan(0);

        (await StateAsync(owner, trip)).GetProperty("participants").EnumerateArray()
            .Select(p => p.GetProperty("caverId").GetGuid())
            .ShouldBe(named);
    }

    // ---- no word for hours -----------------------------------------------------------------

    /// <summary>
    /// The mark for somebody underground nobody has heard from, read on a clock the test moves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A host of its own with a ten-minute threshold, so that the silence can be made by moving
    /// that host's clock by minutes — inside the life of the token the coordinator signed in with —
    /// instead of by waiting or by back-dating everything. The reports carry their own moments,
    /// because recording stamps the machine's time and only the read asks the injected clock.
    /// </para>
    /// <para>
    /// Every "not quiet" below is asserted of somebody who was quiet a step earlier or stands
    /// beside somebody who still is, so that none of them passes merely because the mark is never
    /// set at all.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Somebody_underground_with_no_word_past_the_threshold_is_marked_quiet_and_nobody_else_is()
    {
        var (trip, cavers) = await CreateTripAsync("Quiet", guests: 4);
        var (silent, noted, leaver, unheard) = (cavers[0], cavers[1], cavers[2], cavers[3]);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);

        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        var settings = HostSettings();
        settings["TripTracking:QuietAfter"] = "00:10:00";
        using var host = new SilexGisApiFactory(connectionString, settings, services =>
        {
            JobWorkers.RemoveFrom(services);
            services.AddSingleton<TimeProvider>(clock);
        });
        var coordinator = await AuthHelper.BearerClientAsync(host, ownerEmail);
        var start = clock.Now;

        (await ArmAsync(coordinator, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostEventAsync(coordinator, trip, new
        {
            caverIds = new[] { silent, noted, leaver },
            kind = "entered",
            recordedAt = start.AddMinutes(-8),
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Eight minutes of silence against a threshold of ten: nobody yet, and the read says what
        // the threshold is.
        var early = await StateAsync(coordinator, trip);
        early.GetProperty("quietAfterSeconds").GetInt32().ShouldBe(600);
        new[] { silent, noted, leaver, unheard }.ShouldAllBe(caver => !Quiet(early, caver));

        // Three minutes on, with nothing recorded in between: the three underground have crossed
        // it. The one nobody has ever heard from has not — that is a state of its own.
        clock.Now = start.AddMinutes(3);
        var later = await StateAsync(coordinator, trip);
        new[] { silent, noted, leaver }.ShouldAllBe(caver => Quiet(later, caver));
        Quiet(later, unheard).ShouldBeFalse();
        Participant(later, unheard).GetProperty("lastRecordedAt").ValueKind.ShouldBe(JsonValueKind.Null);

        // A note names no place and ends a silence all the same; an exit ends the watch for a
        // person. The third has had neither and stays marked.
        (await PostEventAsync(coordinator, trip, new
        {
            caverIds = new[] { noted }, kind = "note", note = "voice contact", recordedAt = start,
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostEventAsync(coordinator, trip, new
        {
            caverIds = new[] { leaver }, kind = "exited", recordedAt = start.AddMinutes(-7),
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var answered = await StateAsync(coordinator, trip);
        Quiet(answered, silent).ShouldBeTrue();
        Quiet(answered, noted).ShouldBeFalse();
        Participant(answered, noted).GetProperty("in").GetBoolean().ShouldBeTrue();
        // Out for longer than the threshold, and not quiet: somebody out has nothing left to report.
        Quiet(answered, leaver).ShouldBeFalse();
        Participant(answered, leaver).GetProperty("out").GetBoolean().ShouldBeTrue();

        // The same trip at the same moment on an installation that switched the mark off: nobody
        // is marked and no threshold is sent.
        var offSettings = HostSettings();
        offSettings["TripTracking:QuietAfter"] = "00:00:00";
        using (var offHost = new SilexGisApiFactory(connectionString, offSettings, services =>
        {
            JobWorkers.RemoveFrom(services);
            services.AddSingleton<TimeProvider>(clock);
        }))
        {
            var switchedOff = await StateAsync(await AuthHelper.BearerClientAsync(offHost, ownerEmail), trip);
            switchedOff.GetProperty("quietAfterSeconds").ValueKind.ShouldBe(JsonValueKind.Null);
            new[] { silent, noted, leaver, unheard }.ShouldAllBe(caver => !Quiet(switchedOff, caver));
        }

        // And on one left at its default of three hours, read on the machine's own clock: eleven
        // minutes is nothing.
        var byDefault = await StateAsync(owner, trip);
        byDefault.GetProperty("quietAfterSeconds").GetInt32().ShouldBe(3 * 60 * 60);
        Quiet(byDefault, silent).ShouldBeFalse();

        // Closing the watch ends the subject: nobody is expected to report, so nobody is quiet —
        // the one who was marked a moment ago included, still underground by the log.
        (await PutConfigAsync(coordinator, trip, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var closed = await StateAsync(coordinator, trip);
        closed.GetProperty("quietAfterSeconds").ValueKind.ShouldBe(JsonValueKind.Null);
        Participant(closed, silent).GetProperty("in").GetBoolean().ShouldBeTrue();
        new[] { silent, noted, leaver, unheard }.ShouldAllBe(caver => !Quiet(closed, caver));
    }

    /// <summary>
    /// The default threshold on the machine's own clock, and that the mark is told to a reader who
    /// is refused the place beside it: it is a reading of when somebody was last heard from, which
    /// that reader is sent anyway, and it says nothing of where.
    /// </summary>
    [Fact]
    public async Task Four_silent_hours_mark_somebody_quiet_by_default_for_a_reader_who_is_refused_their_position_too()
    {
        var (trip, cavers) = await CreateTripAsync("Quiet by default", guests: 2);
        var (silent, fresh) = (cavers[0], cavers[1]);
        var cave = await CreateCaveAsync(locationProtected: true);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var now = DateTimeOffset.UtcNow;
        await ReportAsync(
            trip, new { caverIds = new[] { silent }, kind = "atStation", stationName = "cave.upper.2" }, now.AddHours(-4));
        await ReportAsync(
            trip, new { caverIds = new[] { fresh }, kind = "atStation", stationName = "cave.upper.1" }, now.AddHours(-2));

        foreach (var client in new[] { owner, reader })
        {
            var state = await StateAsync(client, trip);
            state.GetProperty("quietAfterSeconds").GetInt32().ShouldBe(3 * 60 * 60);
            Participant(state, silent).GetProperty("quiet").GetBoolean().ShouldBeTrue();
            Participant(state, fresh).GetProperty("quiet").GetBoolean().ShouldBeFalse();
        }

        // The reader really is the one the place is kept from, and the owner the one it is told to.
        var told = await StateAsync(owner, trip);
        var refused = await StateAsync(reader, trip);
        Participant(told, silent).GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
        refused.GetProperty("positionsWithheld").GetBoolean().ShouldBeTrue();
        Participant(refused, silent).GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
        TimeOf(Participant(refused, silent), "lastRecordedAt").ShouldNotBeNull();
    }

    // ---- where the party said it was going ------------------------------------------------

    /// <summary>
    /// The parts of the cave a watch declares are where the party said it was going. A station
    /// reported outside them is marked on the person's row and on the report; one inside is not;
    /// and with nothing declared nobody is outside of anything.
    /// </summary>
    [Fact]
    public async Task A_place_outside_the_declared_parts_is_marked_on_the_row_and_in_the_log_and_never_without_a_declaration()
    {
        var (trip, cavers) = await CreateTripAsync("Declared parts", guests: 2);
        var (strayed, asPlanned) = (cavers[0], cavers[1]);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model, depthFilter: ["cave.upper"])).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The answer to recording says what the next read of the log will say.
        var outside = await PostEventAsync(owner, trip, new
        {
            caverIds = new[] { strayed }, kind = "atStation", stationName = "cave.deep.3", recordedAt = At(10, 0),
        });
        outside.StatusCode.ShouldBe(HttpStatusCode.OK, await outside.Content.ReadAsStringAsync());
        (await BodyAsync(outside))[0].GetProperty("outsideDeclaredParts").GetBoolean().ShouldBeTrue();
        var inside = await PostEventAsync(owner, trip, new
        {
            caverIds = new[] { asPlanned }, kind = "atStation", stationName = "cave.upper.2", recordedAt = At(10, 5),
        });
        inside.StatusCode.ShouldBe(HttpStatusCode.OK, await inside.Content.ReadAsStringAsync());
        (await BodyAsync(inside))[0].GetProperty("outsideDeclaredParts").GetBoolean().ShouldBeFalse();
        // A later note claims no place: it is marked nothing, and the person stays marked by the
        // place they were last reported at.
        await ReportAsync(trip, new { caverIds = new[] { strayed }, kind = "note", note = "resting" }, At(10, 30));

        var state = await StateAsync(owner, trip);
        Participant(state, strayed).GetProperty("stationName").GetString().ShouldBe("cave.deep.3");
        Participant(state, strayed).GetProperty("outsideDeclaredParts").GetBoolean().ShouldBeTrue();
        Participant(state, asPlanned).GetProperty("outsideDeclaredParts").GetBoolean().ShouldBeFalse();

        var log = await LogAsync(owner, trip);
        log.Count.ShouldBe(3);
        OutsideIn(log, "cave.deep.3").ShouldBeTrue();
        OutsideIn(log, "cave.upper.2").ShouldBeFalse();
        log.Single(e => e.GetProperty("kind").GetString() == "note")
            .GetProperty("outsideDeclaredParts").GetBoolean().ShouldBeFalse();

        // A correction that moves the report inside the declared parts answers unmarked, and the
        // other way round answers marked.
        var strayedReport = log.Single(e => e.GetProperty("stationName").GetString() == "cave.deep.3")
            .GetProperty("id").GetGuid();
        var corrected = await PutEventAsync(owner, trip, strayedReport, new
        {
            kind = "atStation", stationName = "cave.upper.1", recordedAt = At(10, 0),
        });
        corrected.StatusCode.ShouldBe(HttpStatusCode.OK, await corrected.Content.ReadAsStringAsync());
        (await BodyAsync(corrected)).GetProperty("outsideDeclaredParts").GetBoolean().ShouldBeFalse();
        var back = await PutEventAsync(owner, trip, strayedReport, new
        {
            kind = "atStation", stationName = "cave.deep.3", recordedAt = At(10, 0),
        });
        back.StatusCode.ShouldBe(HttpStatusCode.OK, await back.Content.ReadAsStringAsync());
        (await BodyAsync(back)).GetProperty("outsideDeclaredParts").GetBoolean().ShouldBeTrue();

        // The declaration withdrawn: the same reports, and nobody is outside of anything.
        (await ArmAsync(owner, trip, model, depthFilter: [])).StatusCode.ShouldBe(HttpStatusCode.OK);
        var undeclared = await StateAsync(owner, trip);
        undeclared.GetProperty("depthFilter").GetArrayLength().ShouldBe(0);
        Participant(undeclared, strayed).GetProperty("stationName").GetString().ShouldBe("cave.deep.3");
        Participant(undeclared, strayed).GetProperty("outsideDeclaredParts").GetBoolean().ShouldBeFalse();
        (await LogAsync(owner, trip)).ShouldAllBe(e => !e.GetProperty("outsideDeclaredParts").GetBoolean());
    }

    /// <summary>
    /// The mark rides the place: somebody refused the station learns nothing about it from this
    /// either, while the person allowed to place in that cave reads the station and the mark.
    /// </summary>
    [Fact]
    public async Task Outside_the_declared_parts_is_never_said_beside_a_withheld_place()
    {
        var (trip, cavers) = await CreateTripAsync("Declared, protected", guests: 1, visibility: "authenticated");
        var cave = await CreateCaveAsync(locationProtected: true);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model, depthFilter: ["cave.upper"])).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(
            trip, new { caverIds = cavers, kind = "atStation", stationName = "cave.deep.3" }, At(11, 0));

        // Positive half: the placer is told the station, the declaration, and that the one lies
        // outside the other — on the row and in the log.
        var told = await StateAsync(owner, trip);
        told.GetProperty("depthFilter").GetArrayLength().ShouldBe(1);
        Participant(told, cavers[0]).GetProperty("stationName").GetString().ShouldBe("cave.deep.3");
        Participant(told, cavers[0]).GetProperty("outsideDeclaredParts").GetBoolean().ShouldBeTrue();
        OutsideIn(await LogAsync(owner, trip), "cave.deep.3").ShouldBeTrue();

        // Negative half: a reader of the trip with no right to the cave's exact location is told
        // neither the station nor the declaration, and so not how they compare.
        var refused = await StateAsync(reader, trip);
        refused.GetProperty("positionsWithheld").GetBoolean().ShouldBeTrue();
        refused.GetProperty("depthFilter").GetArrayLength().ShouldBe(0);
        Participant(refused, cavers[0]).GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
        Participant(refused, cavers[0]).GetProperty("outsideDeclaredParts").GetBoolean().ShouldBeFalse();
        var theirLog = await LogAsync(reader, trip);
        theirLog.Count.ShouldBe(1);
        theirLog[0].GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
        theirLog[0].GetProperty("outsideDeclaredParts").GetBoolean().ShouldBeFalse();
    }

    private static async Task<List<JsonElement>> LogAsync(HttpClient client, Guid trip)
    {
        var response = await client.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return [.. (await BodyAsync(response)).GetProperty("items").EnumerateArray()];
    }

    /// <summary>What the log says of the one report naming this station.</summary>
    private static bool OutsideIn(List<JsonElement> log, string stationName) =>
        log.Single(e => e.GetProperty("stationName").ValueKind == JsonValueKind.String
                && e.GetProperty("stationName").GetString() == stationName)
            .GetProperty("outsideDeclaredParts").GetBoolean();

    /// <summary>
    /// Rewrites a trip's roster the way its form does: the two lists sent are the whole of it.
    /// </summary>
    private Task<HttpResponseMessage> PutRosterAsync(
        Guid trip, IEnumerable<Guid> participants, IEnumerable<Guid>? proposers = null) =>
        owner.PutWithIfMatchAsync($"/api/v1/trip-logs/{trip}", new
        {
            title = $"Roster {Guid.NewGuid():N}"[..28],
            tripDate = "2026-09-12",
            participants = participants.Select(id => new { caverId = id }).ToArray(),
            proposers = (proposers ?? []).Select(id => new { caverId = id }).ToArray(),
            visibility = "authenticated",
        });

    /// <summary>The people a trip names, in the order it first named each of them.</summary>
    private async Task<List<Guid>> RosterAsync(Guid trip)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var rows = await db.TripLogParticipants.Where(p => p.TripLogId == trip)
            .OrderBy(p => p.Id).Select(p => p.CaverId).ToListAsync();
        return [.. rows.Distinct()];
    }

    private async Task<string> CaverNameAsync(Guid caver)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Cavers.Where(c => c.Id == caver).Select(c => c.FullName).SingleAsync();
    }

    private static string PlacesOf(Guid trip) => $"/api/v1/trip-logs/{trip}/tracking/places";

    private static Task<HttpResponseMessage> PutEventAsync(
        HttpClient client, Guid trip, Guid eventId, object body) =>
        client.PutAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events/{eventId}", body);

    /// <summary>
    /// The moment a watch was first started is kept when the watch is closed and started again.
    /// </summary>
    /// <remarks>
    /// Starting a closed watch again exists for a party that turns out to be still underground,
    /// and it overwrites the start it reports — at the one moment the true beginning matters,
    /// because somebody is overdue. So the first start is a fact of its own: stamped once, sent on
    /// the signed-in read beside the latest start, and moved by nothing afterwards. The latest
    /// start is asserted to have moved in the same breath, so that the first one is shown to stay
    /// put against a clock that demonstrably went on.
    /// </remarks>
    [Fact]
    public async Task The_first_start_of_a_watch_is_kept_when_it_is_closed_and_started_again()
    {
        var (trip, _) = await CreateTripAsync("First start", guests: 1);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);

        var firstStart = new DateTimeOffset(DateTimeOffset.UtcNow.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, TimeSpan.Zero);
        var clock = new TestTimeProvider(firstStart);
        using var host = new SilexGisApiFactory(connectionString, HostSettings(), services =>
        {
            JobWorkers.RemoveFrom(services);
            services.AddSingleton<TimeProvider>(clock);
        });

        // Signed in afresh after every move of the clock: how long a sign-in lasts is measured on
        // the same clock, and this test is not about that.
        async Task<JsonElement> AtAsync(DateTimeOffset moment, object? write)
        {
            clock.Now = moment;
            var coordinator = await AuthHelper.BearerClientAsync(host, ownerEmail);
            if (write is not null)
            {
                var written = await PutConfigAsync(coordinator, trip, write);
                written.StatusCode.ShouldBe(HttpStatusCode.OK, await written.Content.ReadAsStringAsync());
            }
            return await StateAsync(coordinator, trip);
        }

        var never = await AtAsync(firstStart, write: null);
        TimeOf(never, "armedAt").ShouldBeNull();
        TimeOf(never, "firstArmedAt").ShouldBeNull();

        var started = await AtAsync(firstStart, new { state = "armed", surveyModelId = model });
        TimeOf(started, "armedAt").ShouldBe(firstStart);
        TimeOf(started, "firstArmedAt").ShouldBe(firstStart);

        var closed = await AtAsync(firstStart.AddHours(4), new { state = "closed" });
        TimeOf(closed, "firstArmedAt").ShouldBe(firstStart);

        var again = await AtAsync(firstStart.AddHours(5), new { state = "armed" });
        TimeOf(again, "armedAt").ShouldBe(firstStart.AddHours(5));
        TimeOf(again, "firstArmedAt").ShouldBe(firstStart);
        TimeOf(again, "closedAt").ShouldBeNull();

        // And a third time, so that "kept" is not merely "kept once".
        await AtAsync(firstStart.AddHours(6), new { state = "closed" });
        var thirdTime = await AtAsync(firstStart.AddHours(7), new { state = "armed" });
        TimeOf(thirdTime, "armedAt").ShouldBe(firstStart.AddHours(7));
        TimeOf(thirdTime, "firstArmedAt").ShouldBe(firstStart);
    }

    /// <summary>
    /// A watch that an import wrote already closed has never been started, and says so; the first
    /// time somebody starts it is its first start.
    /// </summary>
    /// <remarks>
    /// The row is written here as the import writes it — closed, with a closing moment and no
    /// start — because that is the only way a watch comes to be closed without ever having run.
    /// </remarks>
    [Fact]
    public async Task A_watch_written_closed_by_an_import_gets_its_first_start_when_it_is_first_started()
    {
        var (trip, _) = await CreateTripAsync("Imported closed", guests: 1);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.TripTrackings.Add(new TripTracking
            {
                TripLogId = trip,
                State = TripTrackingState.Closed,
                SurveyModelId = model,
                CaveFeatureId = cave,
                ClosedAt = DateTimeOffset.UtcNow.AddDays(-2),
            });
            await db.SaveChangesAsync();
        }

        var imported = await StateAsync(owner, trip);
        imported.GetProperty("state").GetString().ShouldBe("closed");
        TimeOf(imported, "armedAt").ShouldBeNull();
        TimeOf(imported, "firstArmedAt").ShouldBeNull();

        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        (await PutConfigAsync(owner, trip, new { state = "armed" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var started = await StateAsync(owner, trip);
        TimeOf(started, "firstArmedAt").ShouldNotBeNull();
        TimeOf(started, "firstArmedAt")!.Value.ShouldBeGreaterThan(before);
        TimeOf(started, "firstArmedAt").ShouldBe(TimeOf(started, "armedAt"));
    }

    /// <summary>
    /// Writes a watch's state directly, for the one state no request moves a watch into. The state
    /// is a plain recorded fact with nothing derived from it, which is what makes writing it this
    /// way faithful — unlike protection, which goes through the service that maintains its derived
    /// columns.
    /// </summary>
    // ---- a report taken off the log is kept ---------------------------------------------------

    /// <summary>
    /// Taking a report off the log does not destroy it: it leaves the log and the fold, stays
    /// listed for those who may write the log, and comes back as the same row saying what it said.
    /// </summary>
    [Fact]
    public async Task A_report_taken_off_the_log_leaves_every_signed_in_read_and_comes_back_as_it_was()
    {
        var (trip, cavers) = await CreateTripAsync("Taken off", guests: 1);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(trip, new { caverIds = cavers, kind = "entered" }, At(9, 0));
        var placed = await PostEventAsync(owner, trip, new
        {
            caverIds = cavers, kind = "atStation", stationName = "cave.upper.2", recordedAt = At(10, 0),
        });
        placed.StatusCode.ShouldBe(HttpStatusCode.OK, await placed.Content.ReadAsStringAsync());
        var eventId = (await BodyAsync(placed))[0].GetProperty("id").GetGuid();

        // Before: on the log, in the fold, and nothing is listed as removed.
        (await LogAsync(owner, trip)).Select(e => e.GetProperty("id").GetGuid()).ShouldContain(eventId);
        Participant(await StateAsync(owner, trip), cavers[0]).GetProperty("stationName").GetString()
            .ShouldBe("cave.upper.2");
        (await RemovedAsync(owner, trip)).ShouldBeEmpty();

        (await owner.DeleteAsync(EventOf(trip, eventId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Off the log and out of the fold, which falls back to the entry that is left.
        var log = await LogAsync(owner, trip);
        log.Count.ShouldBe(1);
        log.Select(e => e.GetProperty("id").GetGuid()).ShouldNotContain(eventId);
        var folded = Participant(await StateAsync(owner, trip), cavers[0]);
        folded.GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
        folded.GetProperty("lastKind").GetString().ShouldBe("entered");
        folded.GetProperty("in").GetBoolean().ShouldBeTrue();

        // Kept, and listed with what it said and when it was taken off.
        var removed = (await RemovedAsync(owner, trip)).ShouldHaveSingleItem();
        removed.GetProperty("report").GetProperty("id").GetGuid().ShouldBe(eventId);
        removed.GetProperty("report").GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
        removed.GetProperty("report").GetProperty("corrected").GetBoolean().ShouldBeFalse();
        TimeOf(removed, "removedAt").ShouldNotBeNull();
        Guid recordedBy;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.TripPositionEvents.CountAsync(e => e.Id == eventId)).ShouldBe(0, "the model hides it");
            var row = await db.TripPositionEvents.IgnoreQueryFilters().SingleAsync(e => e.Id == eventId);
            row.RemovedAt.ShouldNotBeNull();
            row.RemovedByUserId.ShouldNotBeNull("the mark says who");
            row.RemovedByUserId.ShouldBe(row.RecordedByUserId, "the same account recorded it and took it off");
            recordedBy = row.RecordedByUserId!.Value;
        }

        // A report that is off the log is not there to correct or to take off a second time.
        (await PutEventAsync(owner, trip, eventId, new { kind = "atStation", stationName = "cave.deep.3" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var again = await owner.DeleteAsync(EventOf(trip, eventId));
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await again.Content.ReadAsStringAsync()).ShouldContain("tracking.event_not_found");

        // Put back: the same id, the same place, and not marked as corrected — nobody corrected it.
        var restored = await owner.PostAsync(RestoreOf(trip, eventId), null);
        restored.StatusCode.ShouldBe(HttpStatusCode.OK, await restored.Content.ReadAsStringAsync());
        var back = await BodyAsync(restored);
        back.GetProperty("id").GetGuid().ShouldBe(eventId);
        back.GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
        back.GetProperty("corrected").GetBoolean().ShouldBeFalse();
        TimeOf(back, "recordedAt").ShouldBe(At(10, 0));

        var relisted = (await LogAsync(owner, trip)).Single(e => e.GetProperty("id").GetGuid() == eventId);
        relisted.GetProperty("corrected").GetBoolean().ShouldBeFalse();
        Participant(await StateAsync(owner, trip), cavers[0]).GetProperty("stationName").GetString()
            .ShouldBe("cave.upper.2");
        (await RemovedAsync(owner, trip)).ShouldBeEmpty();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var row = await db.TripPositionEvents.SingleAsync(e => e.Id == eventId);
            row.RemovedAt.ShouldBeNull();
            row.RemovedByUserId.ShouldBeNull();
            row.RecordedByUserId.ShouldBe(recordedBy);
            row.UpdatedAt.ShouldBe(row.CreatedAt, "being taken off and put back is not a change to the report");
        }

        // Asked again, it is answered as it stands: the caller wanted it on the log, and it is.
        var twice = await owner.PostAsync(RestoreOf(trip, eventId), null);
        twice.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BodyAsync(twice)).GetProperty("id").GetGuid().ShouldBe(eventId);
    }

    /// <summary>
    /// Destroying a report takes two acts. Asked of a report that is still on the log it is
    /// refused by name; asked of one already taken off it leaves nothing behind.
    /// </summary>
    [Fact]
    public async Task A_report_is_destroyed_for_good_only_after_it_has_been_taken_off_the_log()
    {
        var (trip, cavers) = await CreateTripAsync("For good", guests: 1);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var placed = await PostEventAsync(owner, trip, new
        {
            caverIds = cavers, kind = "atStation", stationName = "cave.upper.2",
        });
        var eventId = (await BodyAsync(placed))[0].GetProperty("id").GetGuid();

        // Straight to "for good" is refused, and the report is still on the log.
        var early = await owner.DeleteAsync($"{EventOf(trip, eventId)}?permanent=true");
        early.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await early.Content.ReadAsStringAsync()).ShouldContain("tracking.event_not_removed");
        (await LogAsync(owner, trip)).ShouldHaveSingleItem().GetProperty("id").GetGuid().ShouldBe(eventId);

        (await owner.DeleteAsync(EventOf(trip, eventId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RemovedAsync(owner, trip)).Count.ShouldBe(1);

        // The acts on a removed report are under the log's own rule: an off watch takes none.
        await SetStateAsync(trip, TripTrackingState.Off);
        var offRestore = await owner.PostAsync(RestoreOf(trip, eventId), null);
        offRestore.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await offRestore.Content.ReadAsStringAsync()).ShouldContain("tracking.not_writable");
        (await owner.DeleteAsync($"{EventOf(trip, eventId)}?permanent=true"))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await SetStateAsync(trip, TripTrackingState.Closed);

        (await owner.DeleteAsync($"{EventOf(trip, eventId)}?permanent=true"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await RemovedAsync(owner, trip)).ShouldBeEmpty();
        (await LogAsync(owner, trip)).ShouldBeEmpty();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.TripPositionEvents.IgnoreQueryFilters().CountAsync(e => e.Id == eventId)).ShouldBe(0);
        }

        // Nothing is left to put back or to destroy again.
        var gone = await owner.PostAsync(RestoreOf(trip, eventId), null);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await gone.Content.ReadAsStringAsync()).ShouldContain("tracking.event_not_found");
        (await owner.DeleteAsync($"{EventOf(trip, eventId)}?permanent=true"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Removed reports are shown to those who may write the trip's log and to nobody else: a
    /// reader of the trip keeps the log and is refused the removed list and both acts on it, and
    /// a caller with no account gets nothing at all.
    /// </summary>
    [Fact]
    public async Task Somebody_who_may_not_write_the_log_is_never_shown_a_removed_report()
    {
        var (trip, cavers) = await CreateTripAsync("Whose to see", guests: 1, visibility: "authenticated");
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(trip, new { caverIds = cavers, kind = "entered" }, At(9, 0));
        var placed = await PostEventAsync(owner, trip, new
        {
            caverIds = cavers, kind = "note", note = "removed-note-marker", recordedAt = At(10, 0),
        });
        var eventId = (await BodyAsync(placed))[0].GetProperty("id").GetGuid();
        (await owner.DeleteAsync(EventOf(trip, eventId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Positive half: the writer is listed the removed report, and the reader really does read
        // this trip's log — what they are refused below is not the trip.
        (await RemovedAsync(owner, trip)).ShouldHaveSingleItem()
            .GetProperty("report").GetProperty("note").GetString().ShouldBe("removed-note-marker");
        var theirLog = await reader.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events");
        theirLog.StatusCode.ShouldBe(HttpStatusCode.OK);
        var theirLogText = await theirLog.Content.ReadAsStringAsync();
        theirLogText.ShouldContain("entered");
        theirLogText.ShouldNotContain("removed-note-marker");
        theirLogText.ShouldNotContain(eventId.ToString());
        (await (await reader.GetAsync($"/api/v1/trip-logs/{trip}/tracking")).Content.ReadAsStringAsync())
            .ShouldNotContain("removed-note-marker");

        // Negative half: a reader of the trip, with no right to write it.
        var refusedList = await reader.GetAsync(RemovedOf(trip));
        refusedList.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refusedList.Content.ReadAsStringAsync()).ShouldNotContain("removed-note-marker");
        (await reader.PostAsync(RestoreOf(trip, eventId), null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.DeleteAsync($"{EventOf(trip, eventId)}?permanent=true"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // No account: none of the three routes exists for them.
        (await anonymous.GetAsync(RemovedOf(trip))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync(RestoreOf(trip, eventId), null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync($"{EventOf(trip, eventId)}?permanent=true"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // And the refusals changed nothing: it is still there for the writer to put back.
        (await RemovedAsync(owner, trip)).Count.ShouldBe(1);
    }

    /// <summary>
    /// A position kept from a caller on the log is kept from them in the removed list and in the
    /// answer to putting the report back. The caller here may write the trip — it is theirs — and
    /// loses the right to place the cave only when the cave is protected, so the same account
    /// reads the station before and is refused it after.
    /// </summary>
    [Fact]
    public async Task A_withheld_position_stays_withheld_in_the_removed_list_and_on_being_put_back()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        var guideEmail = $"trk-rmv-{Guid.NewGuid():N}"[..20] + "@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, guideEmail);
        var guide = await AuthHelper.BearerClientAsync(factory, guideEmail);
        var (trip, cavers) = await CreateTripAsync("Guided, removed", guests: 1, client: guide);
        (await ArmAsync(guide, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var placed = await PostEventAsync(guide, trip, new
        {
            caverIds = cavers, kind = "atStation", stationName = "cave.upper.2",
        });
        placed.StatusCode.ShouldBe(HttpStatusCode.OK, await placed.Content.ReadAsStringAsync());
        var eventId = (await BodyAsync(placed))[0].GetProperty("id").GetGuid();
        (await guide.DeleteAsync(EventOf(trip, eventId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Positive half: while the cave is open the writer is told where the removed report was.
        var open = (await RemovedAsync(guide, trip)).ShouldHaveSingleItem().GetProperty("report");
        open.GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
        open.GetProperty("surveyModelId").GetGuid().ShouldBe(model);

        await SetLocationProtectedAsync(cave, true);

        // Negative half: the same writer, now with no right to the cave's exact location. That the
        // report exists and when it was taken off are still theirs; where it was is not.
        var listed = await guide.GetAsync(RemovedOf(trip));
        listed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await listed.Content.ReadAsStringAsync()).ShouldNotContain("cave.upper");
        var closed = (await BodyAsync(listed)).GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        closed.GetProperty("report").GetProperty("id").GetGuid().ShouldBe(eventId);
        closed.GetProperty("report").GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
        closed.GetProperty("report").GetProperty("surveyModelId").ValueKind.ShouldBe(JsonValueKind.Null);
        closed.GetProperty("report").GetProperty("depthEnteredM").ValueKind.ShouldBe(JsonValueKind.Null);

        var restored = await guide.PostAsync(RestoreOf(trip, eventId), null);
        restored.StatusCode.ShouldBe(HttpStatusCode.OK, await restored.Content.ReadAsStringAsync());
        (await restored.Content.ReadAsStringAsync()).ShouldNotContain("cave.upper");
        (await BodyAsync(restored)).GetProperty("id").GetGuid().ShouldBe(eventId);

        // The place was withheld, not lost: with the cave open again the log names it.
        await SetLocationProtectedAsync(cave, false);
        (await LogAsync(guide, trip)).ShouldHaveSingleItem()
            .GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
    }

    /// <summary>
    /// A removed report still holds its person in place: deleting them is refused with a reason
    /// rather than failing on the constraint, and merging them moves the removed report to the
    /// survivor, where putting it back finds it.
    /// </summary>
    [Fact]
    public async Task A_removed_report_still_blocks_deleting_its_person_and_follows_them_through_a_merge()
    {
        var (trip, cavers) = await CreateTripAsync("Held by a removed report", guests: 2);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var (stays, leaves) = (cavers[0], cavers[1]);
        var placed = await PostEventAsync(owner, trip, new
        {
            caverIds = new[] { leaves }, kind = "atStation", stationName = "cave.upper.2",
        });
        var eventId = (await BodyAsync(placed))[0].GetProperty("id").GetGuid();
        (await owner.DeleteAsync(EventOf(trip, eventId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // With their only report off the log, nothing the watch shows speaks of them, and they
        // may be taken off the roster — which leaves the removed report as all that names them.
        var rewritten = await PutRosterAsync(trip, [stays]);
        rewritten.StatusCode.ShouldBe(HttpStatusCode.OK, await rewritten.Content.ReadAsStringAsync());

        // Which is as far as it goes while the watch runs: putting the report back would have a
        // running watch speak of somebody its trip no longer lists, and is refused as recording
        // one about them would be. The report stays where it was, kept.
        var offRoster = await owner.PostAsync(RestoreOf(trip, eventId), null);
        offRoster.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await offRoster.Content.ReadAsStringAsync());
        (await offRoster.Content.ReadAsStringAsync()).ShouldContain("tracking.caver_not_participant");
        (await LogAsync(owner, trip)).ShouldBeEmpty();
        (await RemovedAsync(owner, trip)).ShouldHaveSingleItem()
            .GetProperty("report").GetProperty("id").GetGuid().ShouldBe(eventId);

        // A closed watch asks nothing of the roster: its log is a record, and the same report
        // goes back onto it about the same person.
        await SetStateAsync(trip, TripTrackingState.Closed);
        var onceClosed = await owner.PostAsync(RestoreOf(trip, eventId), null);
        onceClosed.StatusCode.ShouldBe(HttpStatusCode.OK, await onceClosed.Content.ReadAsStringAsync());
        (await BodyAsync(onceClosed)).GetProperty("caverId").GetGuid().ShouldBe(leaves);
        (await owner.DeleteAsync(EventOf(trip, eventId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await SetStateAsync(trip, TripTrackingState.Armed);

        var keeperEmail = $"trk-keep-{Guid.NewGuid():N}"[..20] + "@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, keeperEmail);
        var keeper = await AuthHelper.BearerClientAsync(factory, keeperEmail);

        var refused = await keeper.DeleteAsync($"/api/v1/cavers/{leaves}");
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        var refusal = await refused.Content.ReadAsStringAsync();
        refusal.ShouldContain("caver.referenced_by_trips");
        refusal.ShouldContain("tracking history");

        var merged = await keeper.PostAsJsonAsync($"/api/v1/cavers/{stays}/merge", new { sourceCaverId = leaves });
        merged.StatusCode.ShouldBe(HttpStatusCode.OK, await merged.Content.ReadAsStringAsync());

        var removed = (await RemovedAsync(owner, trip)).ShouldHaveSingleItem();
        removed.GetProperty("report").GetProperty("id").GetGuid().ShouldBe(eventId);
        removed.GetProperty("report").GetProperty("caverId").GetGuid().ShouldBe(stays);
        // The watch is running again, and the report is now about somebody the trip does list.
        var restored = await owner.PostAsync(RestoreOf(trip, eventId), null);
        restored.StatusCode.ShouldBe(HttpStatusCode.OK, await restored.Content.ReadAsStringAsync());
        (await BodyAsync(restored)).GetProperty("caverId").GetGuid().ShouldBe(stays);
    }

    // ---- when somebody asks to be removed -----------------------------------------------------

    private static string ReportsOf(Guid trip, Guid caver) =>
        $"/api/v1/trip-logs/{trip}/tracking/participants/{caver}/events";

    /// <summary>
    /// How many reports of a trip are about each person as the table holds them — on the log and
    /// taken off it alike, which is what "gone" has to be measured against.
    /// </summary>
    private async Task<Dictionary<Guid, int>> ReportsHeldAsync(Guid trip)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripPositionEvents.IgnoreQueryFilters().AboutPeople().Where(e => e.TripLogId == trip)
            .GroupBy(e => e.CaverId!.Value).ToDictionaryAsync(g => g.Key, g => g.Count());
    }

    /// <summary>A full administrator: the refusal over a person is about history, not rights.</summary>
    private async Task<HttpClient> AdministratorAsync()
    {
        var email = $"trk-adm-{Guid.NewGuid():N}"[..20] + "@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, email);
        return await AuthHelper.BearerClientAsync(factory, email);
    }

    /// <summary>The trips a refused delete of a person names, and whether anything else holds them.</summary>
    private static async Task<(List<JsonElement> Trips, bool HeldElsewhere)> HeldByAsync(HttpClient client, Guid caver)
    {
        var refused = await client.DeleteAsync($"/api/v1/cavers/{caver}");
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        var problem = await BodyAsync(refused);
        problem.GetProperty("code").GetString().ShouldBe("caver.referenced_by_trips");
        return ([.. problem.GetProperty("trips").EnumerateArray()], problem.GetProperty("heldElsewhere").GetBoolean());
    }

    /// <summary>
    /// The act destroys that person's reports of that trip — the ones on the log and the ones
    /// taken off it and kept — and nobody else's of either kind; each is on the trip's history;
    /// and with nothing left it is a refusal, not a quiet nought.
    /// </summary>
    [Fact]
    public async Task Removing_a_persons_reports_destroys_theirs_kept_ones_included_and_nobody_elses()
    {
        var (trip, cavers) = await CreateTripAsync("Asked to be removed", guests: 3);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var (asks, other, third) = (cavers[0], cavers[1], cavers[2]);

        await ReportAsync(trip, new { caverIds = cavers, kind = "entered" }, At(9, 0));
        await ReportAsync(trip, new { caverIds = new[] { asks }, kind = "atStation", stationName = "cave.upper.1" }, At(10, 0));
        await ReportAsync(trip, new { caverIds = new[] { other }, kind = "atStation", stationName = "cave.upper.2" }, At(10, 5));
        var asksNote = await PostEventAsync(owner, trip, new
        {
            caverIds = new[] { asks }, kind = "note", note = "theirs, taken off", recordedAt = At(11, 0),
        });
        var othersNote = await PostEventAsync(owner, trip, new
        {
            caverIds = new[] { other }, kind = "note", note = "somebody else's, taken off", recordedAt = At(11, 5),
        });
        var asksKept = (await BodyAsync(asksNote))[0].GetProperty("id").GetGuid();
        var othersKept = (await BodyAsync(othersNote))[0].GetProperty("id").GetGuid();
        (await owner.DeleteAsync(EventOf(trip, asksKept))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.DeleteAsync(EventOf(trip, othersKept))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // What there is to lose, stated before anything is removed: three about the person (one
        // of them kept), three about the second (one kept), one about the third.
        var before = await ReportsHeldAsync(trip);
        before[asks].ShouldBe(3);
        before[other].ShouldBe(3);
        before[third].ShouldBe(1);

        // The same numbers as whoever is about to press is told them: the log narrowed to the
        // person, and the kept reports narrowed to the person, add up to what will go.
        async Task<int> ToldAsync(string list, Guid caver)
        {
            var response = await owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking/{list}?caverId={caver}&pageSize=1");
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var page = await BodyAsync(response);
            page.GetProperty("items").EnumerateArray()
                .ShouldAllBe(item => (list == "events" ? item : item.GetProperty("report")).GetProperty("caverId").GetGuid() == caver);
            return page.GetProperty("totalItems").GetInt32();
        }

        (await ToldAsync("events", asks)).ShouldBe(2);
        (await ToldAsync("events/removed", asks)).ShouldBe(1);
        (await ToldAsync("events/removed", third)).ShouldBe(0);
        // Unnarrowed, the kept list is everybody's — so the narrowing above is what gave one.
        (await RemovedAsync(owner, trip)).Count.ShouldBe(2);

        var removed = await owner.DeleteAsync(ReportsOf(trip, asks));
        removed.StatusCode.ShouldBe(HttpStatusCode.OK, await removed.Content.ReadAsStringAsync());
        (await BodyAsync(removed)).GetProperty("removed").GetInt32().ShouldBe(3);

        var after = await ReportsHeldAsync(trip);
        after.ContainsKey(asks).ShouldBeFalse("nothing about the person is left, kept copies included");
        after[other].ShouldBe(3);
        after[third].ShouldBe(1);

        // And as the application reads it: the log no longer speaks of them, the other two are
        // where they were, and the only report left to put back is somebody else's.
        var log = await LogAsync(owner, trip);
        log.Count.ShouldBe(3);
        log.ShouldAllBe(e => e.GetProperty("caverId").GetGuid() != asks);
        (await RemovedAsync(owner, trip)).ShouldHaveSingleItem()
            .GetProperty("report").GetProperty("id").GetGuid().ShouldBe(othersKept);
        (await owner.PostAsync(RestoreOf(trip, asksKept), null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // They are still on the trip's roster: the act removes reports, not the person's name.
        (await RosterAsync(trip)).ShouldContain(asks);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.AuditEntries.CountAsync(a => a.RootEntityId == trip.ToString()
                && a.EntityType == nameof(TripPositionEvent) && a.Action == AuditActions.Deleted)).ShouldBe(3);

            // What the confirmation tells whoever presses this, held to the fact: the reports are
            // gone, and the trip's history still says what each one said.
            var kept = await db.AuditEntries
                .Where(a => a.EntityId == asksKept.ToString() && a.Action == AuditActions.Deleted)
                .Select(a => a.Changes).SingleAsync();
            kept.ShouldNotBeNull().ShouldContain("theirs, taken off");
        }

        // Asked again, and asked about somebody the trip has never had a report of.
        var again = await owner.DeleteAsync(ReportsOf(trip, asks));
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await again.Content.ReadAsStringAsync()).ShouldContain("tracking.no_reports_of_person");
        var nobody = await owner.DeleteAsync(ReportsOf(trip, Guid.NewGuid()));
        nobody.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await nobody.Content.ReadAsStringAsync()).ShouldContain("tracking.no_reports_of_person");
        (await ReportsHeldAsync(trip)).Values.Sum().ShouldBe(4);
    }

    /// <summary>
    /// The act is refused wherever destroying one report is: to nobody signed in, to a reader of
    /// the trip, as "no such trip" to somebody who may not read it, and on a watch whose log takes
    /// no writes — and in each case nothing is removed. The same request from the trip's writer on
    /// a writable log goes through, which is what shows the refusals were about the caller and the
    /// state, not about the request.
    /// </summary>
    [Fact]
    public async Task Removing_a_persons_reports_is_refused_wherever_destroying_one_is()
    {
        var (trip, cavers) = await CreateTripAsync("Not theirs to remove", guests: 2, visibility: "authenticated");
        var (hidden, hiddenCavers) = await CreateTripAsync("Not theirs to see", guests: 1, visibility: "private");
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ArmAsync(owner, hidden, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(trip, new { caverIds = cavers, kind = "entered" }, At(9, 0));
        await ReportAsync(hidden, new { caverIds = hiddenCavers, kind = "entered" }, At(9, 0));

        (await anonymous.DeleteAsync(ReportsOf(trip, cavers[0]))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The reader does read this trip — what they are refused is the write.
        (await reader.GetAsync($"/api/v1/trip-logs/{trip}/tracking")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.DeleteAsync(ReportsOf(trip, cavers[0]))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // And does not read the private one, which answers as a trip that is not there.
        (await reader.GetAsync($"/api/v1/trip-logs/{hidden}/tracking")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var masked = await reader.DeleteAsync(ReportsOf(hidden, hiddenCavers[0]));
        masked.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await masked.Content.ReadAsStringAsync()).ShouldContain("trip_log.not_found");

        // A watch that is off takes no writes, from its writer either.
        await SetStateAsync(trip, TripTrackingState.Off);
        var off = await owner.DeleteAsync(ReportsOf(trip, cavers[0]));
        off.StatusCode.ShouldBe(HttpStatusCode.Conflict, await off.Content.ReadAsStringAsync());
        (await off.Content.ReadAsStringAsync()).ShouldContain("tracking.not_writable");

        // A trip that was never followed has no log to remove from.
        var (never, neverCavers) = await CreateTripAsync("Never followed", guests: 1);
        var unwatched = await owner.DeleteAsync(ReportsOf(never, neverCavers[0]));
        unwatched.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await unwatched.Content.ReadAsStringAsync()).ShouldContain("tracking.not_writable");

        var untouched = await ReportsHeldAsync(trip);
        untouched[cavers[0]].ShouldBe(1);
        untouched[cavers[1]].ShouldBe(1);
        (await ReportsHeldAsync(hidden))[hiddenCavers[0]].ShouldBe(1);

        await SetStateAsync(trip, TripTrackingState.Closed);
        var allowed = await owner.DeleteAsync(ReportsOf(trip, cavers[0]));
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await BodyAsync(allowed)).GetProperty("removed").GetInt32().ShouldBe(1);
        (await ReportsHeldAsync(trip))[cavers[1]].ShouldBe(1);
    }

    /// <summary>
    /// The way out for somebody who is not a duplicate: the refusal to delete them names each trip
    /// and what holds them there; a person held only by reports can be deleted once the act has
    /// been done on each trip named; a person still on a roster is still refused, and the list
    /// says so.
    /// </summary>
    [Fact]
    public async Task A_person_held_only_by_reports_can_be_deleted_after_the_act_and_one_on_a_roster_cannot()
    {
        var (first, cavers) = await CreateTripAsync("Held, first trip", guests: 3);
        var (leaves, stays, bystander) = (cavers[0], cavers[1], cavers[2]);
        var second = await CreateTripOfAsync("Held, second trip", [leaves, bystander]);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, first, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ArmAsync(owner, second, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(first, new { caverIds = cavers, kind = "entered" }, At(9, 0));
        await ReportAsync(first, new { caverIds = new[] { leaves }, kind = "exited" }, At(15, 0));
        await ReportAsync(second, new { caverIds = new[] { leaves, bystander }, kind = "entered" }, At(9, 30));
        var kept = await PostEventAsync(owner, second, new
        {
            caverIds = new[] { leaves }, kind = "note", note = "kept", recordedAt = At(10, 0),
        });
        (await owner.DeleteAsync(EventOf(second, (await BodyAsync(kept))[0].GetProperty("id").GetGuid())))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Both watches are closed and the person is taken off both trips: their reports are now
        // all that names them.
        await SetStateAsync(first, TripTrackingState.Closed);
        await SetStateAsync(second, TripTrackingState.Closed);
        (await PutRosterAsync(first, [stays, bystander])).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PutRosterAsync(second, [bystander])).StatusCode.ShouldBe(HttpStatusCode.OK);

        var administrator = await AdministratorAsync();
        var (trips, elsewhere) = await HeldByAsync(administrator, leaves);
        elsewhere.ShouldBeFalse();
        trips.Count.ShouldBe(2);
        var onFirst = trips.Single(t => t.GetProperty("id").GetGuid() == first);
        onFirst.GetProperty("onRoster").GetBoolean().ShouldBeFalse();
        onFirst.GetProperty("reports").GetInt32().ShouldBe(2);
        onFirst.GetProperty("reportsRemovable").GetBoolean().ShouldBeTrue();
        onFirst.GetProperty("tripDate").GetString().ShouldBe("2026-09-12");
        onFirst.GetProperty("title").GetString()!.ShouldStartWith("Roster ");
        // The one taken off the log and kept is counted for somebody who may write that log.
        trips.Single(t => t.GetProperty("id").GetGuid() == second).GetProperty("reports").GetInt32().ShouldBe(2);

        // One trip dealt with is not enough, and the list then names the one that is left.
        (await administrator.DeleteAsync(ReportsOf(first, leaves))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var (left, _) = await HeldByAsync(administrator, leaves);
        left.ShouldHaveSingleItem().GetProperty("id").GetGuid().ShouldBe(second);
        (await administrator.DeleteAsync(ReportsOf(second, leaves))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await administrator.DeleteAsync($"/api/v1/cavers/{leaves}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Nobody else lost anything on the way.
        var firstHeld = await ReportsHeldAsync(first);
        firstHeld[stays].ShouldBe(1);
        firstHeld[bystander].ShouldBe(1);
        (await ReportsHeldAsync(second))[bystander].ShouldBe(1);

        // Somebody still on a roster: the list says so, removing their reports changes the count
        // and not the refusal, and there is then nothing here to remove.
        var (held, _) = await HeldByAsync(administrator, stays);
        var named = held.ShouldHaveSingleItem();
        named.GetProperty("onRoster").GetBoolean().ShouldBeTrue();
        named.GetProperty("reports").GetInt32().ShouldBe(1);
        named.GetProperty("reportsRemovable").GetBoolean().ShouldBeTrue();
        (await administrator.DeleteAsync(ReportsOf(first, stays))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var (still, _) = await HeldByAsync(administrator, stays);
        var roster = still.ShouldHaveSingleItem();
        roster.GetProperty("onRoster").GetBoolean().ShouldBeTrue();
        roster.GetProperty("reports").GetInt32().ShouldBe(0);
        roster.GetProperty("reportsRemovable").GetBoolean().ShouldBeFalse();
    }

    /// <summary>
    /// A report taken off a log is listed only for those who may write that log, and the refusal
    /// over a person follows the same rule: a caller who keeps the roster and only reads the trip
    /// is told that something holds the person, never that this trip has removed reports about
    /// them. The trip's writer, asked the same question, is told.
    /// </summary>
    [Fact]
    public async Task A_kept_report_is_named_in_the_refusal_only_to_somebody_who_may_write_that_log()
    {
        var (trip, cavers) = await CreateTripAsync("Kept and unseen", guests: 2, visibility: "authenticated");
        var (leaves, stays) = (cavers[0], cavers[1]);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var placed = await PostEventAsync(owner, trip, new { caverIds = new[] { leaves }, kind = "entered" });
        (await owner.DeleteAsync(EventOf(trip, (await BodyAsync(placed))[0].GetProperty("id").GetGuid())))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PutRosterAsync(trip, [stays])).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Somebody who may delete people and write no trip: an account with no role of its own
        // and one rule over the roster.
        var administrator = await AdministratorAsync();
        var keeperEmail = $"trk-ros-{Guid.NewGuid():N}"[..20] + "@t.local";
        var keeperId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, keeperEmail);
        var rules = await administrator.PostAsJsonAsync("/api/v1/permission-groups/", new
        {
            name = $"Roster only {Guid.NewGuid():N}"[..30], description = (string?)null,
        });
        rules.StatusCode.ShouldBe(HttpStatusCode.Created, await rules.Content.ReadAsStringAsync());
        var rulesId = (await BodyAsync(rules)).GetProperty("id").GetGuid();
        (await administrator.PostAsJsonAsync($"/api/v1/permission-groups/{rulesId}/members", new
        {
            memberKind = "user", memberId = keeperId,
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await administrator.PutAsJsonAsync($"/api/v1/permission-groups/{rulesId}/entries", new
        {
            entries = new[] { new { effect = "allow", domain = "cavers", actions = "read, delete", scopeKind = "all" } },
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var keeper = await AuthHelper.BearerClientAsync(factory, keeperEmail);

        // They do read the trip, and are named it for the person who is on its roster — so the
        // empty list below is not a caller who is told nothing.
        (await keeper.GetAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var (control, controlElsewhere) = await HeldByAsync(keeper, stays);
        control.ShouldHaveSingleItem().GetProperty("id").GetGuid().ShouldBe(trip);
        control[0].GetProperty("reportsRemovable").GetBoolean().ShouldBeFalse();
        controlElsewhere.ShouldBeFalse();

        var (theirs, elsewhere) = await HeldByAsync(keeper, leaves);
        theirs.ShouldBeEmpty();
        elsewhere.ShouldBeTrue();

        var (writers, writersElsewhere) = await HeldByAsync(administrator, leaves);
        var told = writers.ShouldHaveSingleItem();
        told.GetProperty("id").GetGuid().ShouldBe(trip);
        told.GetProperty("onRoster").GetBoolean().ShouldBeFalse();
        told.GetProperty("reports").GetInt32().ShouldBe(1);
        told.GetProperty("reportsRemovable").GetBoolean().ShouldBeTrue();
        writersElsewhere.ShouldBeFalse();
    }

    /// <summary>A trip naming people who already exist, as its form would send them.</summary>
    private async Task<Guid> CreateTripOfAsync(string title, IEnumerable<Guid> people)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            tripDate = "2026-09-12",
            participants = people.Select(id => new { caverId = id }).ToArray(),
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    // ---- a report that cannot be written twice ------------------------------------------------

    /// <summary>
    /// A send repeated under its key answers the reports already written and writes nothing; the
    /// repeat is asked "may you write this log" like the send it repeats; and the key itself is
    /// in no answer, no read and no history row.
    /// </summary>
    [Fact]
    public async Task A_report_sent_again_under_its_key_answers_what_was_written_and_writes_nothing()
    {
        var (trip, cavers) = await CreateTripAsync("Sent twice", guests: 2);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var key = Guid.NewGuid();
        var body = new
        {
            caverIds = cavers, kind = "atStation", stationName = "cave.upper.2", recordedAt = At(10, 0),
            clientKey = key,
        };

        var first = await PostEventAsync(owner, trip, body);
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var written = (await BodyAsync(first)).EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
        written.Count.ShouldBe(2);

        var again = await PostEventAsync(owner, trip, body);
        again.StatusCode.ShouldBe(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
        var answered = (await BodyAsync(again)).EnumerateArray().ToList();
        // The same rows, in the order the send names its people — and the place with them, since
        // this caller may be told it.
        answered.Select(e => e.GetProperty("id").GetGuid()).ShouldBe(written);
        answered.Select(e => e.GetProperty("caverId").GetGuid()).ShouldBe(cavers);
        answered.ShouldAllBe(e => e.GetProperty("stationName").GetString() == "cave.upper.2");
        (await LogAsync(owner, trip)).Count.ShouldBe(2);

        // The key names the act, not what it says: a repeat saying something else is answered
        // with what was written and changes nothing.
        var different = await PostEventAsync(owner, trip, new
        {
            caverIds = new[] { cavers[0] }, kind = "exited", recordedAt = At(11, 0), clientKey = key,
        });
        different.StatusCode.ShouldBe(HttpStatusCode.OK, await different.Content.ReadAsStringAsync());
        (await BodyAsync(different)).EnumerateArray().Select(e => e.GetProperty("id").GetGuid())
            .ShouldBe(written, ignoreOrder: true);
        var log = await LogAsync(owner, trip);
        log.Count.ShouldBe(2);
        log.ShouldAllBe(e => e.GetProperty("kind").GetString() == "atStation");

        // Somebody who reads the trip and may not write its log is refused on a repeat exactly as
        // on a first send, and is handed nothing of what the key would have answered; a caller
        // with no account is not let in at all. The owner's repeat above is the positive half.
        var byReader = await PostEventAsync(reader, trip, body);
        byReader.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await byReader.Content.ReadAsStringAsync());
        var refusal = await byReader.Content.ReadAsStringAsync();
        written.ShouldAllBe(id => !refusal.Contains(id.ToString()));
        refusal.ShouldNotContain("cave.upper");
        (await PostEventAsync(anonymous, trip, body)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The key is taken in and never given out: not in an answer, not on the log, not on the
        // watch, not in what the trip's history kept of the new rows.
        var keyText = key.ToString();
        (await first.Content.ReadAsStringAsync()).ShouldNotContain(keyText);
        (await again.Content.ReadAsStringAsync()).ShouldNotContain(keyText);
        (await LogTextAsync(trip)).ShouldNotContain(keyText);
        (await StateAsync(owner, trip)).GetRawText().ShouldNotContain(keyText);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var eventIds = written.Select(id => id.ToString()).ToList();
        var history = await db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityId != null && eventIds.Contains(a.EntityId)).Select(a => a.Changes).ToListAsync();
        // The rows are in the history — so "the key is not there" is said about something.
        history.Count.ShouldBe(2);
        history.ShouldAllBe(changes => changes != null && !changes.Contains(keyText));
        // And it is stored, on both rows: what was proved above is a repeat recognised by its
        // key, not two sends that happened to be told apart some other way.
        (await db.TripPositionEvents.CountAsync(e => e.TripLogId == trip
            && EF.Property<Guid?>(e, TripPositionEvent.ClientKeyProperty) == key)).ShouldBe(2);
    }

    /// <summary>
    /// A repeat does not put back what somebody took off the log: a partly removed act answers
    /// what is left, an act removed entirely answers nothing, and neither writes.
    /// </summary>
    [Fact]
    public async Task A_repeat_answers_what_is_left_of_its_act_and_never_rewrites_a_report_taken_off()
    {
        var (trip, cavers) = await CreateTripAsync("Sent twice, partly removed", guests: 3);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = new { caverIds = cavers, kind = "entered", recordedAt = At(9, 0), clientKey = Guid.NewGuid() };
        var first = await PostEventAsync(owner, trip, body);
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var rows = (await BodyAsync(first)).EnumerateArray()
            .ToDictionary(e => e.GetProperty("caverId").GetGuid(), e => e.GetProperty("id").GetGuid());
        rows.Count.ShouldBe(3);

        (await owner.DeleteAsync(EventOf(trip, rows[cavers[1]]))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var partial = await PostEventAsync(owner, trip, body);
        partial.StatusCode.ShouldBe(HttpStatusCode.OK, await partial.Content.ReadAsStringAsync());
        (await BodyAsync(partial)).EnumerateArray().Select(e => e.GetProperty("id").GetGuid())
            .ShouldBe([rows[cavers[0]], rows[cavers[2]]]);
        (await LogAsync(owner, trip)).Count.ShouldBe(2);
        (await RemovedAsync(owner, trip)).ShouldHaveSingleItem()
            .GetProperty("report").GetProperty("id").GetGuid().ShouldBe(rows[cavers[1]]);

        (await owner.DeleteAsync(EventOf(trip, rows[cavers[0]]))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.DeleteAsync(EventOf(trip, rows[cavers[2]]))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Nothing of the act is left on the log. The repeat is still a success — its report was
        // received — and still writes nothing; the three removed reports are the same three rows.
        var none = await PostEventAsync(owner, trip, body);
        none.StatusCode.ShouldBe(HttpStatusCode.OK, await none.Content.ReadAsStringAsync());
        (await BodyAsync(none)).GetArrayLength().ShouldBe(0);
        (await LogAsync(owner, trip)).ShouldBeEmpty();
        (await RemovedAsync(owner, trip)).Select(e => e.GetProperty("report").GetProperty("id").GetGuid())
            .ShouldBe(rows.Values, ignoreOrder: true);

        // Put back, a report answers a repeat again: it was hidden, not forgotten.
        (await owner.PostAsync(RestoreOf(trip, rows[cavers[1]]), null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterRestore = await PostEventAsync(owner, trip, body);
        (await BodyAsync(afterRestore)).EnumerateArray().ShouldHaveSingleItem()
            .GetProperty("id").GetGuid().ShouldBe(rows[cavers[1]]);
        (await LogAsync(owner, trip)).Count.ShouldBe(1);
    }

    /// <summary>
    /// Two acts that say the same thing are two reports: under two keys, and under none.
    /// </summary>
    [Fact]
    public async Task Two_acts_saying_the_same_thing_write_two_reports()
    {
        var (trip, cavers) = await CreateTripAsync("Twins", guests: 1);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        object Saying(Guid? key) => new
        {
            caverIds = cavers, kind = "atStation", stationName = "cave.deep.3", recordedAt = At(12, 0), clientKey = key,
        };
        var ids = new List<Guid>();
        foreach (var key in new Guid?[] { Guid.NewGuid(), Guid.NewGuid(), null, null })
        {
            var sent = await PostEventAsync(owner, trip, Saying(key));
            sent.StatusCode.ShouldBe(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());
            ids.Add((await BodyAsync(sent)).EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid());
        }

        ids.Distinct().Count().ShouldBe(4);
        (await LogAsync(owner, trip)).Count.ShouldBe(4);
    }

    /// <summary>
    /// A repeat is told what a read of the log would tell its caller, not what the first answer
    /// said. The caller owns the trip, so may write its log throughout; they are told the place
    /// while the cave is open and not once it is protected.
    /// </summary>
    [Fact]
    public async Task A_repeat_withholds_the_place_from_a_caller_who_may_no_longer_be_told_it()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        var guideEmail = $"trk-rpl-{Guid.NewGuid():N}"[..20] + "@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, guideEmail);
        var guide = await AuthHelper.BearerClientAsync(factory, guideEmail);
        var (trip, cavers) = await CreateTripAsync("Guided, sent twice", guests: 1, client: guide);
        (await ArmAsync(guide, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = new
        {
            caverIds = cavers, kind = "atDepth", depthM = 120m, clientKey = Guid.NewGuid(),
        };
        var first = await PostEventAsync(guide, trip, body);
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var eventId = (await BodyAsync(first))[0].GetProperty("id").GetGuid();

        // Positive half: with the cave open, the repeat names the place.
        var open = (await BodyAsync(await PostEventAsync(guide, trip, body))).EnumerateArray().ShouldHaveSingleItem();
        open.GetProperty("id").GetGuid().ShouldBe(eventId);
        open.GetProperty("stationName").GetString().ShouldBe("cave.deep.3");
        open.GetProperty("surveyModelId").GetGuid().ShouldBe(model);
        open.GetProperty("depthEnteredM").GetDecimal().ShouldBe(120m);

        await SetLocationProtectedAsync(cave, true);

        // Negative half: the same account, the same key, the cave now protected. The report is
        // still theirs to be told of; where it was is not.
        var repeated = await PostEventAsync(guide, trip, body);
        repeated.StatusCode.ShouldBe(HttpStatusCode.OK, await repeated.Content.ReadAsStringAsync());
        (await repeated.Content.ReadAsStringAsync()).ShouldNotContain("cave.deep");
        var closed = (await BodyAsync(repeated)).EnumerateArray().ShouldHaveSingleItem();
        closed.GetProperty("id").GetGuid().ShouldBe(eventId);
        closed.GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
        closed.GetProperty("surveyModelId").ValueKind.ShouldBe(JsonValueKind.Null);
        closed.GetProperty("depthEnteredM").ValueKind.ShouldBe(JsonValueKind.Null);
        (await LogAsync(guide, trip)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_empty_key_is_refused_and_nothing_is_written()
    {
        var (trip, cavers) = await CreateTripAsync("Empty key", guests: 1);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var refused = await PostEventAsync(owner, trip, new { caverIds = cavers, kind = "entered", clientKey = Guid.Empty });
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        (await refused.Content.ReadAsStringAsync()).ShouldContain("clientKey", Case.Insensitive);
        (await LogAsync(owner, trip)).ShouldBeEmpty();

        // Beside it, the two sends that are not refused: a real key, and none.
        (await PostEventAsync(owner, trip, new { caverIds = cavers, kind = "entered", clientKey = Guid.NewGuid() }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostEventAsync(owner, trip, new { caverIds = cavers, kind = "exited" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await LogAsync(owner, trip)).Count.ShouldBe(2);
    }

    /// <summary>
    /// One act sent many times at once is written once, and every send is answered with that one
    /// report — whichever of them got there first, and whether the others found it by looking or
    /// were stopped at the constraint.
    /// </summary>
    [Fact]
    public async Task One_act_sent_many_times_at_once_leaves_one_report_and_every_send_succeeds()
    {
        var (trip, cavers) = await CreateTripAsync("Sent at once", guests: 2);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = new
        {
            caverIds = cavers, kind = "atStation", stationName = "cave.upper.1", recordedAt = At(13, 0),
            clientKey = Guid.NewGuid(),
        };

        var sends = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => PostEventAsync(owner, trip, body)));

        var answers = new List<List<Guid>>();
        foreach (var send in sends)
        {
            send.StatusCode.ShouldBe(HttpStatusCode.OK, await send.Content.ReadAsStringAsync());
            answers.Add([.. (await BodyAsync(send)).EnumerateArray().Select(e => e.GetProperty("id").GetGuid())]);
        }

        var log = (await LogAsync(owner, trip)).Select(e => e.GetProperty("id").GetGuid()).ToList();
        log.Count.ShouldBe(2);
        answers.ShouldAllBe(ids => ids.Count == 2 && ids.All(id => log.Contains(id)));
        // No history row was left behind by a send that lost: one "created" per report, no more.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var logIds = log.Select(id => id.ToString()).ToList();
        (await db.AuditEntries.CountAsync(a => a.RootEntityId == trip.ToString()
            && a.EntityType == nameof(TripPositionEvent))).ShouldBe(2);
        (await db.AuditEntries.CountAsync(a => a.EntityId != null && logIds.Contains(a.EntityId))).ShouldBe(2);
    }

    /// <summary>
    /// The same of an act that names several people, which is where two sends can do worse than
    /// arrive second: each writes a report per person, and were they to write the people in
    /// different orders, each could get one person in and wait for the other's until the database
    /// failed one of them — a send answered with an error for a report that was received. So
    /// every send has to write the people in one order, whatever order it names them in: half of
    /// these name them backwards. Asked several times over, because the first sends an
    /// application ever takes are slow enough to fall into an order by themselves.
    /// </summary>
    [Fact]
    public async Task One_act_naming_several_people_sent_many_times_at_once_leaves_one_report_each_and_every_send_succeeds()
    {
        const int People = 6;
        const int Acts = 4;
        var (trip, cavers) = await CreateTripAsync("Several sent at once", guests: People);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var backwards = Enumerable.Reverse(cavers).ToList();

        var answers = new List<List<Guid>>();
        for (var act = 0; act < Acts; act++)
        {
            var clientKey = Guid.NewGuid();
            var recordedAt = At(13, act);
            var sends = await Task.WhenAll(Enumerable.Range(0, 24).Select(send => PostEventAsync(owner, trip, new
            {
                caverIds = send % 2 == 0 ? cavers : backwards,
                kind = "atStation", stationName = "cave.upper.1", recordedAt, clientKey,
            })));

            foreach (var send in sends)
            {
                send.StatusCode.ShouldBe(HttpStatusCode.OK, await send.Content.ReadAsStringAsync());
                answers.Add([.. (await BodyAsync(send)).EnumerateArray().Select(e => e.GetProperty("id").GetGuid())]);
            }
        }

        var log = (await LogAsync(owner, trip)).Select(e => e.GetProperty("id").GetGuid()).ToList();
        log.Count.ShouldBe(People * Acts);
        answers.ShouldAllBe(ids => ids.Count == People && ids.All(id => log.Contains(id)));
        // Each act answered every one of its sends with the same reports, and no two acts share one.
        answers.Select(ids => string.Join(',', ids.Order())).Distinct().Count().ShouldBe(Acts);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.AuditEntries.CountAsync(a => a.RootEntityId == trip.ToString()
            && a.EntityType == nameof(TripPositionEvent))).ShouldBe(People * Acts);
    }

    /// <summary>
    /// Two people named by one act can be folded into one: the report that moves stops claiming
    /// the act, so the fold does not fail on "one act, one report per person", and a later repeat
    /// is still recognised by the survivor's own report and still writes nothing. An act that
    /// named only the entry merged away keeps its key on the report that moved, so its repeat —
    /// which still names an entry that no longer exists — is answered with that report too.
    /// </summary>
    [Fact]
    public async Task Folding_together_two_people_named_by_one_act_keeps_both_reports_and_the_act_recognised()
    {
        var (trip, cavers) = await CreateTripAsync("Folded after one act", guests: 2);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var (stays, leaves) = (cavers[0], cavers[1]);
        var body = new { caverIds = new[] { stays, leaves }, kind = "entered", recordedAt = At(9, 0), clientKey = Guid.NewGuid() };
        var first = await PostEventAsync(owner, trip, body);
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var rows = (await BodyAsync(first)).EnumerateArray()
            .ToDictionary(e => e.GetProperty("caverId").GetGuid(), e => e.GetProperty("id").GetGuid());
        var alone = new
        {
            caverIds = new[] { leaves }, kind = "atStation", stationName = "cave.upper.2",
            recordedAt = At(10, 0), clientKey = Guid.NewGuid(),
        };
        var sent = await PostEventAsync(owner, trip, alone);
        sent.StatusCode.ShouldBe(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());
        var aloneId = (await BodyAsync(sent))[0].GetProperty("id").GetGuid();

        var keeperEmail = $"trk-fold-{Guid.NewGuid():N}"[..20] + "@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, keeperEmail);
        var keeper = await AuthHelper.BearerClientAsync(factory, keeperEmail);
        var merged = await keeper.PostAsJsonAsync($"/api/v1/cavers/{stays}/merge", new { sourceCaverId = leaves });
        merged.StatusCode.ShouldBe(HttpStatusCode.OK, await merged.Content.ReadAsStringAsync());

        var log = await LogAsync(owner, trip);
        log.Select(e => e.GetProperty("id").GetGuid()).ShouldBe([.. rows.Values, aloneId], ignoreOrder: true);
        log.ShouldAllBe(e => e.GetProperty("caverId").GetGuid() == stays);

        var repeat = await PostEventAsync(owner, trip, body);
        repeat.StatusCode.ShouldBe(HttpStatusCode.OK, await repeat.Content.ReadAsStringAsync());
        (await BodyAsync(repeat)).EnumerateArray().ShouldHaveSingleItem()
            .GetProperty("id").GetGuid().ShouldBe(rows[stays]);
        (await LogAsync(owner, trip)).Count.ShouldBe(3);

        // The act that named only the entry merged away. Its repeat names somebody the register
        // no longer holds, which a first send would be refused for; it is a repeat, so it is
        // answered with the report it wrote, now about the survivor, and writes nothing.
        var late = await PostEventAsync(owner, trip, alone);
        late.StatusCode.ShouldBe(HttpStatusCode.OK, await late.Content.ReadAsStringAsync());
        var answered = (await BodyAsync(late)).EnumerateArray().ShouldHaveSingleItem();
        answered.GetProperty("id").GetGuid().ShouldBe(aloneId);
        answered.GetProperty("caverId").GetGuid().ShouldBe(stays);
        (await LogAsync(owner, trip)).Count.ShouldBe(3);

        // The fixture half: without its key that same send is a first one, and is refused.
        var unkeyed = await PostEventAsync(owner, trip, new { caverIds = new[] { leaves }, kind = "atStation", stationName = "cave.upper.2" });
        unkeyed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unkeyed.Content.ReadAsStringAsync()).ShouldContain("tracking.caver_not_participant");
    }

    /// <summary>
    /// How long an act is remembered: as long as any report it wrote exists. Taken off the log,
    /// its repeat is answered and writes nothing; destroyed for good, the key went with the
    /// report, and a repeat arriving after that is written as a first send.
    /// </summary>
    [Fact]
    public async Task An_act_is_remembered_while_its_report_is_kept_and_forgotten_once_it_is_destroyed()
    {
        var (trip, cavers) = await CreateTripAsync("Destroyed, then sent again", guests: 1);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = new { caverIds = cavers, kind = "entered", recordedAt = At(9, 0), clientKey = Guid.NewGuid() };
        var first = await PostEventAsync(owner, trip, body);
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var eventId = (await BodyAsync(first))[0].GetProperty("id").GetGuid();

        // Taken off: the act is still on record, so its repeat succeeds with nothing and writes nothing.
        (await owner.DeleteAsync(EventOf(trip, eventId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var kept = await PostEventAsync(owner, trip, body);
        kept.StatusCode.ShouldBe(HttpStatusCode.OK, await kept.Content.ReadAsStringAsync());
        (await BodyAsync(kept)).GetArrayLength().ShouldBe(0);
        (await LogAsync(owner, trip)).ShouldBeEmpty();

        // Destroyed: nothing carries the key any more, and the same send is a new report.
        (await owner.DeleteAsync($"{EventOf(trip, eventId)}?permanent=true"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var forgotten = await PostEventAsync(owner, trip, body);
        forgotten.StatusCode.ShouldBe(HttpStatusCode.OK, await forgotten.Content.ReadAsStringAsync());
        var written = (await BodyAsync(forgotten)).EnumerateArray().ShouldHaveSingleItem();
        written.GetProperty("id").GetGuid().ShouldNotBe(eventId);
        (await LogAsync(owner, trip)).ShouldHaveSingleItem()
            .GetProperty("id").GetGuid().ShouldBe(written.GetProperty("id").GetGuid());
    }

    // ---- a party number is given once -----------------------------------------------------------

    /// <summary>
    /// Two writes that name somebody new on one trip at the same moment reach for the same next
    /// party number, and the table lets one of them in. The other is answered as a conflict with a
    /// code, has written nothing, and succeeds when it is repeated.
    /// </summary>
    /// <remarks>
    /// The other writer is played here by a transaction that has taken the next number and not
    /// yet committed: the save cannot see it, picks the same number, and waits on it. Committing
    /// is the other writer winning. The wait is observed before the commit, so the test cannot
    /// pass by the save simply having finished first.
    /// </remarks>
    [Fact]
    public async Task A_roster_save_that_loses_the_race_for_the_next_party_number_is_a_conflict_and_can_be_repeated()
    {
        var (trip, cavers) = await CreateTripAsync("Numbered at once", guests: 1);
        var (_, registered) = await CreateTripAsync("Numbered at once, the register", guests: 1);
        var newcomer = registered[0];

        Task<HttpResponseMessage> save;
        int taken;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            taken = await db.TripPartyNumbers.Where(n => n.TripLogId == trip).MaxAsync(n => n.Number) + 1;
            await using var winner = await db.Database.BeginTransactionAsync();
            db.TripPartyNumbers.Add(new TripPartyNumber { TripLogId = trip, Number = taken });
            await db.SaveChangesAsync();

            save = PutRosterAsync(trip, [cavers[0], newcomer]);
            using (var watching = factory.Services.CreateScope())
            {
                var observer = watching.ServiceProvider.GetRequiredService<SilexGisDbContext>();
                var waiting = false;
                for (var attempt = 0; attempt < 300 && !waiting && !save.IsCompleted; attempt++)
                {
                    await Task.Delay(100);
                    waiting = await observer.Database
                        .SqlQuery<int>($"""
                            SELECT count(*)::int AS "Value" FROM pg_stat_activity
                            WHERE datname = current_database() AND wait_event_type = 'Lock'
                            """)
                        .SingleAsync() > 0;
                }

                waiting.ShouldBeTrue("the save never came to wait on the number the other writer holds");
            }

            await winner.CommitAsync();
        }

        var lost = await save;
        var refusal = await lost.Content.ReadAsStringAsync();
        lost.StatusCode.ShouldBe(HttpStatusCode.Conflict, refusal);
        refusal.ShouldContain("trip_log.concurrent_roster_write");
        (await RosterAsync(trip)).ShouldBe([cavers[0]], "the save that lost wrote nothing");

        var repeated = await PutRosterAsync(trip, [cavers[0], newcomer]);
        repeated.StatusCode.ShouldBe(HttpStatusCode.OK, await repeated.Content.ReadAsStringAsync());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.TripPartyNumbers.Where(n => n.TripLogId == trip && n.CaverId == newcomer)
                .Select(n => n.Number).SingleAsync())
                .ShouldBe(taken + 1, "the number the other writer took stays taken");
        }
    }

    private static string EventOf(Guid trip, Guid eventId) => $"/api/v1/trip-logs/{trip}/tracking/events/{eventId}";

    private static string RestoreOf(Guid trip, Guid eventId) => $"{EventOf(trip, eventId)}/restore";

    private static string RemovedOf(Guid trip) => $"/api/v1/trip-logs/{trip}/tracking/events/removed";

    /// <summary>The reports taken off a trip's log, as this caller is listed them.</summary>
    private static async Task<List<JsonElement>> RemovedAsync(HttpClient client, Guid trip)
    {
        var response = await client.GetAsync(RemovedOf(trip));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return [.. (await BodyAsync(response)).GetProperty("items").EnumerateArray()];
    }

    private async Task SetStateAsync(Guid trip, TripTrackingState state)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var tracking = await db.TripTrackings.SingleAsync(t => t.TripLogId == trip);
        tracking.State = state;
        await db.SaveChangesAsync();
    }

    private async Task SetLocationProtectedAsync(Guid caveFeatureId, bool value)
    {
        using var scope = factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
        await writer.SetLocationProtectedAsync(caveFeatureId, value);
        await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().SaveChangesAsync();
    }

    private async Task<(Guid Trip, List<Guid> Cavers)> CreateTripAsync(
        string title, int guests, string visibility = "authenticated", HttpClient? client = null)
    {
        var participants = Enumerable.Range(1, guests)
            .Select(i => new { newCaverName = $"Guest {i} {Guid.NewGuid():N}"[..24] })
            .ToArray();
        var response = await (client ?? owner).PostAsJsonAsync("/api/v1/trip-logs/", new
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
            name = $"Track Cave {Guid.NewGuid():N}"[..30],
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
    /// A survey model created the real way (upload — a file row cannot exist outside the
    /// documents chain), then stations seeded straight into the graph tables: an entrance at
    /// 350 m, two branches that share the −50 m horizon (the ambiguity the depth filter
    /// exists for), and a deep point. The extraction job never runs here — workers are off.
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
            Station(modelId, "cave.upper.1", "cave.upper", 340, SurveyStationFlags.Underground),
            Station(modelId, "cave.upper.2", "cave.upper", 300, SurveyStationFlags.Underground),
            Station(modelId, "cave.parallel.2", "cave.parallel", 300, SurveyStationFlags.Underground),
            Station(modelId, "cave.deep.3", "cave.deep", 230, SurveyStationFlags.Underground));
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

    private static Task<HttpResponseMessage> ArmAsync(HttpClient client, Guid trip, Guid model, string[]? depthFilter = null) =>
        PutConfigAsync(client, trip, new
        {
            state = "armed",
            surveyModelId = model,
            depthFilter,
        });

    private static Task<HttpResponseMessage> PostEventAsync(HttpClient client, Guid trip, object body) =>
        client.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", body);

    /// <summary>
    /// The trip's own day. Reports are folded in the order the <em>reporter</em> gave, not the
    /// order they were typed, so a test about that order has to state its own times rather than
    /// lean on how fast it posts.
    /// </summary>
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

    /// <summary>Whether the read marks this person as not heard from for too long.</summary>
    private static bool Quiet(JsonElement state, Guid caverId) =>
        Participant(state, caverId).GetProperty("quiet").GetBoolean();

    private static JsonElement Participant(JsonElement state, Guid caverId) =>
        state.GetProperty("participants").EnumerateArray()
            .Single(p => p.GetProperty("caverId").GetGuid() == caverId);

    /// <summary>A nullable instant off the wire — null is a real answer on every time this slice sends.</summary>
    private static DateTimeOffset? TimeOf(JsonElement element, string property) =>
        element.GetProperty(property).ValueKind == JsonValueKind.Null
            ? null
            : element.GetProperty(property).GetDateTimeOffset();

    private static async Task<JsonElement> StateAsync(HttpClient client, Guid trip)
    {
        var response = await client.GetAsync($"/api/v1/trip-logs/{trip}/tracking");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await BodyAsync(response);
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
