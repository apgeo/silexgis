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
using SilexGis.Infrastructure.Jobs;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
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

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private HttpClient anonymous = null!;
    private long caveTypeId;

    public TripTrackingTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-files-{Guid.NewGuid():N}");
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
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"trk-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"trk-read-{suffix}@t.local");

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
    public async Task Tracking_arms_takes_reports_folds_the_latest_per_caver_and_corrections_are_deletions()
    {
        var (trip, cavers) = await CreateTripAsync("Core loop", guests: 2);
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);

        // Reports land on armed tracking only.
        var early = await PostEventAsync(owner, trip, new { caverIds = cavers, kind = "entered" });
        early.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await early.Content.ReadAsStringAsync()).ShouldContain("tracking.not_armed");

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
