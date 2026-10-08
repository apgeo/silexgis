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
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A note about the cave — "loose rock above the second pitch" — which is about nobody: written,
/// corrected, taken off, put back and destroyed like any report, and seen by nothing that reads
/// the log as a party.
/// </summary>
/// <remarks>
/// <para>
/// The row has no person. Every reader that folds the log into people is shown here to be exactly
/// what it was before the note was written: the party table, the camp's count, the statistics, the
/// trips of a survey, the sheet, the folding of two people into one. And every read that needs no
/// account is shown to hold neither the note's words nor its station.
/// </para>
/// <para>
/// Every "is not there" is asserted beside the same fact being there for a caller who is told it,
/// from the same rows. The people of these tests are only ever reported at <c>cave.upper.2</c> and
/// the notes about the cave are only ever at <c>cave.deep.3</c>, with words that occur nowhere
/// else, so either can be searched for in the raw text of an answer.
/// </para>
/// </remarks>
public sealed class TripTrackingCaveNoteTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string PartyStation = "cave.upper.2";
    private const string HazardStation = "cave.deep.3";
    private const string Words = "loose rock above the second pitch qzx";

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private HttpClient anonymous = null!;
    private long caveTypeId;

    public TripTrackingCaveNoteTests(PostgresFixture postgres)
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
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"cvn-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cvn-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"cvn-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"cvn-read-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        try { Directory.Delete(filesRoot, recursive: true); } catch { /* best effort */ }
    }

    // ---- the life of one note ------------------------------------------------------------

    [Fact]
    public async Task A_note_about_the_cave_is_written_corrected_taken_off_put_back_and_destroyed()
    {
        var (trip, cavers) = await ArmedTripAsync("Cave note", guests: 1);

        var recorded = await PostEventAsync(owner, trip, CaveNote());
        recorded.StatusCode.ShouldBe(HttpStatusCode.OK, await recorded.Content.ReadAsStringAsync());
        var written = (await BodyAsync(recorded)).EnumerateArray().ShouldHaveSingleItem();
        written.GetProperty("caverId").ValueKind.ShouldBe(JsonValueKind.Null);
        Text(written, "kind").ShouldBe("caveNote");
        Text(written, "stationName").ShouldBe(HazardStation);
        Text(written, "note").ShouldBe(Words);
        var noteId = written.GetProperty("id").GetGuid();

        // Placed like a station report: the survey it names a station of, and that survey's cave.
        var stored = await StoredAsync(noteId);
        stored.CaverId.ShouldBeNull();
        stored.SurveyModelId.ShouldNotBeNull();
        stored.CaveFeatureId.ShouldNotBeNull();

        // Corrected: other words and no station — the survey and the cave leave with the station.
        var reworded = await PutEventAsync(owner, trip, noteId, new { kind = "caveNote", note = "water rising, qzx" });
        reworded.StatusCode.ShouldBe(HttpStatusCode.OK, await reworded.Content.ReadAsStringAsync());
        var corrected = await BodyAsync(reworded);
        corrected.GetProperty("caverId").ValueKind.ShouldBe(JsonValueKind.Null);
        Text(corrected, "stationName").ShouldBeNull();
        corrected.GetProperty("corrected").GetBoolean().ShouldBeTrue();
        stored = await StoredAsync(noteId);
        stored.Note.ShouldBe("water rising, qzx");
        stored.SurveyModelId.ShouldBeNull();
        stored.CaveFeatureId.ShouldBeNull();

        // And placed again by a correction.
        (await PutEventAsync(owner, trip, noteId, CaveNoteEdit())).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StoredAsync(noteId)).ViewerStationName.ShouldBe(HazardStation);

        // Who a report is about is not editable, by this door either: a note about the cave does
        // not become a person's report, and a person's report does not become one.
        var personal = await PostEventAsync(owner, trip, new { caverIds = cavers, kind = "note", note = "radio contact" });
        personal.StatusCode.ShouldBe(HttpStatusCode.OK, await personal.Content.ReadAsStringAsync());
        var personalId = (await BodyAsync(personal)).EnumerateArray().Single().GetProperty("id").GetGuid();
        await ShouldBeRefusedAsync(
            await PutEventAsync(owner, trip, noteId, new { kind = "note", note = Words }),
            HttpStatusCode.BadRequest, "tracking.report_subject_fixed");
        await ShouldBeRefusedAsync(
            await PutEventAsync(owner, trip, personalId, new { kind = "caveNote", note = Words }),
            HttpStatusCode.BadRequest, "tracking.report_subject_fixed");
        (await StoredAsync(noteId)).Kind.ShouldBe(TripPositionEventKind.CaveNote);
        (await StoredAsync(personalId)).CaverId.ShouldBe(cavers[0]);

        // Taken off: gone from the log, kept among the removed.
        (await owner.DeleteAsync(EventOf(trip, noteId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await LogAsync(owner, trip)).ShouldNotContain(e => e.GetProperty("id").GetGuid() == noteId);
        var removedList = await owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events/removed");
        removedList.StatusCode.ShouldBe(HttpStatusCode.OK, await removedList.Content.ReadAsStringAsync());
        var kept = (await BodyAsync(removedList)).GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("report").GetProperty("id").GetGuid() == noteId).GetProperty("report");
        Text(kept, "note").ShouldBe(Words);
        kept.GetProperty("caverId").ValueKind.ShouldBe(JsonValueKind.Null);

        // Put back while the watch runs. A person's report is asked whether its person is still
        // on the roster; a note about the cave has nobody to be missing from it.
        var restored = await owner.PostAsync($"{EventOf(trip, noteId)}/restore", null);
        restored.StatusCode.ShouldBe(HttpStatusCode.OK, await restored.Content.ReadAsStringAsync());
        Text(await BodyAsync(restored), "stationName").ShouldBe(HazardStation);
        (await LogAsync(owner, trip)).ShouldContain(e => e.GetProperty("id").GetGuid() == noteId);

        // Destroyed: only once it has been taken off, and then it is nowhere.
        (await owner.DeleteAsync($"{EventOf(trip, noteId)}?permanent=true")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await owner.DeleteAsync(EventOf(trip, noteId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.DeleteAsync($"{EventOf(trip, noteId)}?permanent=true")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.TripPositionEvents.IgnoreQueryFilters().AnyAsync(e => e.Id == noteId)).ShouldBeFalse();
        (await db.TripPositionEvents.IgnoreQueryFilters().AnyAsync(e => e.Id == personalId)).ShouldBeTrue();
    }

    // ---- what may be said, and by whom ---------------------------------------------------

    [Fact]
    public async Task What_a_note_about_the_cave_may_say_is_decided_before_anything_is_written()
    {
        var (trip, cavers) = await ArmedTripAsync("Cave note, refused", guests: 1);

        // Nobody without an account, and nobody who may only read the trip.
        (await PostEventAsync(anonymous, trip, CaveNote())).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await PostEventAsync(reader, trip, CaveNote())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        async Task InvalidAsync(object body)
        {
            var response = await PostEventAsync(owner, trip, body);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        }

        // About nobody: a list of people is refused, not dropped.
        await InvalidAsync(new { caverIds = cavers, kind = "caveNote", note = Words });
        // Its words are the whole of it.
        await InvalidAsync(new { kind = "caveNote", stationName = HazardStation });
        await InvalidAsync(new { kind = "caveNote", note = "   " });
        // One station or none: no blank, no stretch, no depth. And no team.
        await InvalidAsync(new { kind = "caveNote", note = Words, stationName = "" });
        await InvalidAsync(new { kind = "caveNote", note = Words, stationName = PartyStation, toStationName = HazardStation });
        await InvalidAsync(new { kind = "caveNote", note = Words, depthM = 40 });
        await InvalidAsync(new { kind = "caveNote", note = Words, teamId = Guid.NewGuid() });
        // And the other way round: every other kind still names somebody.
        await InvalidAsync(new { kind = "note", note = Words });
        await InvalidAsync(new { caverIds = Array.Empty<Guid>(), kind = "entered" });

        // A station the survey does not have is refused as it is for a person's report.
        await ShouldBeRefusedAsync(
            await PostEventAsync(owner, trip, new { kind = "caveNote", note = Words, stationName = "cave.nowhere.9" }),
            HttpStatusCode.BadRequest, "tracking.station_unknown");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.TripPositionEvents.IgnoreQueryFilters().CountAsync(e => e.TripLogId == trip)).ShouldBe(0);
        }

        // The same rules pass a note that keeps them — with a station, and with none and an
        // empty list of people, which is how a sender that always sends the field says "nobody".
        (await PostEventAsync(owner, trip, CaveNote())).StatusCode.ShouldBe(HttpStatusCode.OK);
        var bare = await PostEventAsync(owner, trip, new { caverIds = Array.Empty<Guid>(), kind = "caveNote", note = Words });
        bare.StatusCode.ShouldBe(HttpStatusCode.OK, await bare.Content.ReadAsStringAsync());
        var unplaced = await StoredAsync((await BodyAsync(bare)).EnumerateArray().Single().GetProperty("id").GetGuid());
        unplaced.SurveyModelId.ShouldBeNull();
        unplaced.CaveFeatureId.ShouldBeNull();
    }

    [Fact]
    public async Task The_table_itself_keeps_a_person_off_a_note_about_the_cave_and_on_every_other_report()
    {
        var (trip, cavers) = await ArmedTripAsync("Cave note, by hand", guests: 1);
        var note = await PostEventAsync(owner, trip, CaveNote());
        note.StatusCode.ShouldBe(HttpStatusCode.OK, await note.Content.ReadAsStringAsync());
        var noteId = (await BodyAsync(note)).EnumerateArray().Single().GetProperty("id").GetGuid();
        var entered = await PostEventAsync(owner, trip, new { caverIds = cavers, kind = "entered" });
        entered.StatusCode.ShouldBe(HttpStatusCode.OK, await entered.Content.ReadAsStringAsync());
        var enteredId = (await BodyAsync(entered)).EnumerateArray().Single().GetProperty("id").GetGuid();

        async Task ShouldBeRefusedByTheTableAsync(Guid eventId, Action<TripPositionEvent> careless)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var row = await db.TripPositionEvents.SingleAsync(e => e.Id == eventId);
            careless(row);
            var refused = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
            refused.InnerException!.Message.ShouldContain("ck_trip_position_events_cave_note");
        }

        // A person's report that lost its person; a note about the cave that gained one, changed
        // kind, lost its words, or took a depth.
        await ShouldBeRefusedByTheTableAsync(enteredId, row => row.CaverId = null);
        await ShouldBeRefusedByTheTableAsync(enteredId, row => row.Kind = TripPositionEventKind.CaveNote);
        await ShouldBeRefusedByTheTableAsync(noteId, row => row.CaverId = cavers[0]);
        await ShouldBeRefusedByTheTableAsync(noteId, row => row.Kind = TripPositionEventKind.Note);
        await ShouldBeRefusedByTheTableAsync(noteId, row => row.Note = null);
        await ShouldBeRefusedByTheTableAsync(noteId, row => row.Note = "  ");
        await ShouldBeRefusedByTheTableAsync(noteId, row => row.DepthEnteredM = 40);

        // Both rows stand as written: the rule refused those writes, not every write.
        (await StoredAsync(noteId)).Note.ShouldBe(Words);
        (await StoredAsync(enteredId)).CaverId.ShouldBe(cavers[0]);
    }

    // ---- a repeated send -----------------------------------------------------------------

    [Fact]
    public async Task A_note_about_the_cave_sent_again_or_many_times_at_once_is_written_once()
    {
        var (trip, _) = await ArmedTripAsync("Cave note, sent twice", guests: 1);

        // One after the other: the second is answered with what the first wrote.
        var key = Guid.NewGuid();
        var first = await PostEventAsync(owner, trip, CaveNote(key));
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var firstId = (await BodyAsync(first)).EnumerateArray().Single().GetProperty("id").GetGuid();
        var again = await PostEventAsync(owner, trip, CaveNote(key));
        again.StatusCode.ShouldBe(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
        (await BodyAsync(again)).EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid().ShouldBe(firstId);

        // All at once, under a second key: every send looks before any has written, so what lets
        // exactly one in is the table — and a row with no person must count there as a row.
        var racedKey = Guid.NewGuid();
        var sends = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => PostEventAsync(owner, trip, CaveNote(racedKey))));
        var answered = new HashSet<Guid>();
        foreach (var send in sends)
        {
            send.StatusCode.ShouldBe(HttpStatusCode.OK, await send.Content.ReadAsStringAsync());
            answered.Add((await BodyAsync(send)).EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid());
        }
        answered.Count.ShouldBe(1, "every send of one act is answered with the one report it wrote");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var rows = await db.TripPositionEvents.IgnoreQueryFilters().Where(e => e.TripLogId == trip).ToListAsync();
        rows.Count.ShouldBe(2, "one row per act: the pair sent in turn, and the twelve sent at once");
        rows.ShouldAllBe(e => e.CaverId == null && e.Kind == TripPositionEventKind.CaveNote);
        // No history row was left behind by a send that lost.
        (await db.AuditEntries.CountAsync(a => a.RootEntityId == trip.ToString()
            && a.EntityType == nameof(TripPositionEvent))).ShouldBe(2);

        // Two acts saying the same thing are still two notes: the key names the act, not its words.
        (await PostEventAsync(owner, trip, CaveNote(Guid.NewGuid()))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await LogAsync(owner, trip)).Count.ShouldBe(3);
    }

    // ---- who is told ---------------------------------------------------------------------

    [Fact]
    public async Task Its_station_is_kept_from_a_reader_who_may_not_be_told_the_caves_positions_and_its_words_are_not()
    {
        var (trip, _) = await ArmedTripAsync("Cave note, protected", guests: 1, locationProtected: true);
        var recorded = await PostEventAsync(owner, trip, CaveNote());
        recorded.StatusCode.ShouldBe(HttpStatusCode.OK, await recorded.Content.ReadAsStringAsync());
        var noteId = (await BodyAsync(recorded)).EnumerateArray().Single().GetProperty("id").GetGuid();

        // Positive half, from the same row: whoever may place the cave reads the station.
        var mine = (await LogAsync(owner, trip)).ShouldHaveSingleItem();
        Text(mine, "stationName").ShouldBe(HazardStation);
        mine.GetProperty("surveyModelId").ValueKind.ShouldNotBe(JsonValueKind.Null);

        // Negative half: the reader reads the trip, so reads the note and its words — and is told
        // neither the station nor the survey.
        var theirLog = await reader.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events");
        theirLog.StatusCode.ShouldBe(HttpStatusCode.OK);
        var theirText = await theirLog.Content.ReadAsStringAsync();
        theirText.ShouldNotContain(HazardStation);
        var theirs = JsonDocument.Parse(theirText).RootElement.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        theirs.GetProperty("id").GetGuid().ShouldBe(noteId);
        Text(theirs, "kind").ShouldBe("caveNote");
        Text(theirs, "note").ShouldBe(Words);
        Text(theirs, "stationName").ShouldBeNull();
        theirs.GetProperty("surveyModelId").ValueKind.ShouldBe(JsonValueKind.Null);

        // The trip's history keeps the note and its words and, as for every report, no station —
        // asked by the person who may place the cave, so what is missing is missing for everybody.
        var response = await owner.GetAsync($"/api/v1/history?entityType=tripLog&entityId={trip}&pageSize=200");
        var history = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, history);
        history.ShouldContain(noteId.ToString(), Case.Insensitive);
        history.ShouldContain(Words);
        history.ShouldNotContain(HazardStation);
    }

    /// <summary>
    /// Somebody who writes the log and may not be told the cave's positions reads a placed note
    /// exactly as one that names no place, so their correction of its words arrives with no
    /// station. The place they were never shown stays where it was, and is not handed to them.
    /// </summary>
    [Fact]
    public async Task Correcting_its_words_without_being_told_its_place_leaves_the_place_standing()
    {
        // Another editor's own trip, on the owner's cave while it is open: they write its log.
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        var guideEmail = $"cvn-guide-{Guid.NewGuid():N}"[..20] + "@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, guideEmail);
        var guide = await AuthHelper.BearerClientAsync(factory, guideEmail);
        var (trip, _) = await CreateTripAsync("Cave note, corrected blind", guests: 1, client: guide);
        (await ArmAsync(guide, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var recorded = await PostEventAsync(guide, trip, CaveNote());
        recorded.StatusCode.ShouldBe(HttpStatusCode.OK, await recorded.Content.ReadAsStringAsync());
        var noteId = (await BodyAsync(recorded)).EnumerateArray().Single().GetProperty("id").GetGuid();
        var placedAs = await StoredAsync(noteId);
        placedAs.ViewerStationName.ShouldBe(HazardStation);

        await SetLocationProtectedAsync(cave, true);

        // The state the correction is made from: the writer reads the note and no place on it.
        var theirs = (await LogAsync(guide, trip)).ShouldHaveSingleItem();
        Text(theirs, "note").ShouldBe(Words);
        Text(theirs, "stationName").ShouldBeNull();

        const string corrected = "loose rock above the second pitch, left wall qzx";
        var answer = await PutEventAsync(guide, trip, noteId, new { kind = "caveNote", note = corrected });
        var answered = await answer.Content.ReadAsStringAsync();
        answer.StatusCode.ShouldBe(HttpStatusCode.OK, answered);
        // The answer is theirs to read, so it holds the words and none of the place.
        answered.ShouldContain(corrected);
        answered.ShouldNotContain(HazardStation);
        answered.ShouldNotContain(model.ToString(), Case.Insensitive);

        var stored = await StoredAsync(noteId);
        stored.Note.ShouldBe(corrected);
        stored.ViewerStationName.ShouldBe(HazardStation);
        stored.SurveyModelId.ShouldBe(placedAs.SurveyModelId);
        stored.CaveFeatureId.ShouldBe(placedAs.CaveFeatureId);

        // Whoever may be told the cave's positions still reads where the hazard is.
        var mine = (await LogAsync(owner, trip)).ShouldHaveSingleItem();
        Text(mine, "note").ShouldBe(corrected);
        Text(mine, "stationName").ShouldBe(HazardStation);

        // And the same request from the same account once it is shown the place means what it
        // says: the station is taken off the note.
        await SetLocationProtectedAsync(cave, false);
        Text((await LogAsync(guide, trip)).ShouldHaveSingleItem(), "stationName").ShouldBe(HazardStation);
        var removed = await PutEventAsync(guide, trip, noteId, new { kind = "caveNote", note = corrected });
        removed.StatusCode.ShouldBe(HttpStatusCode.OK, await removed.Content.ReadAsStringAsync());
        var cleared = await StoredAsync(noteId);
        cleared.ViewerStationName.ShouldBeNull();
        cleared.SurveyModelId.ShouldBeNull();
        cleared.CaveFeatureId.ShouldBeNull();
    }

    // ---- nobody in the party is touched --------------------------------------------------

    /// <summary>
    /// The party table before a note about the cave and after it are the same text: nobody's
    /// place, standing, last word, quiet mark or number moved, nobody was added, and nothing is
    /// newly said to be withheld.
    /// </summary>
    [Fact]
    public async Task Nobodys_place_standing_last_word_or_number_is_changed_by_a_note_about_the_cave()
    {
        var (trip, cavers) = await ArmedTripAsync("Cave note, beside a party", guests: 3);
        var earlier = DateTimeOffset.UtcNow.AddMinutes(-40);
        // One underground at a station, one in and out again, one never heard from.
        await ReportAsync(trip, new { caverIds = new[] { cavers[0] }, kind = "entered", recordedAt = earlier });
        await ReportAsync(trip, new
        {
            caverIds = new[] { cavers[0] }, kind = "atStation", stationName = PartyStation,
            recordedAt = earlier.AddMinutes(5),
        });
        await ReportAsync(trip, new { caverIds = new[] { cavers[1] }, kind = "entered", recordedAt = earlier });
        await ReportAsync(trip, new { caverIds = new[] { cavers[1] }, kind = "exited", recordedAt = earlier.AddMinutes(10) });

        var before = await StateAsync(owner, trip);
        before.GetProperty("participants").GetArrayLength().ShouldBe(3);
        Text(Participant(before, cavers[0]), "stationName").ShouldBe(PartyStation);

        // Later than every report about a person, and at another station: were it anybody's, it
        // would be their latest word and their latest place.
        await ReportAsync(trip, new
        {
            kind = "caveNote", note = Words, stationName = HazardStation, recordedAt = earlier.AddMinutes(30),
        });

        var after = await StateAsync(owner, trip);
        after.GetProperty("participants").GetRawText().ShouldBe(before.GetProperty("participants").GetRawText());
        after.GetProperty("positionsWithheld").GetBoolean().ShouldBe(before.GetProperty("positionsWithheld").GetBoolean());
        after.GetRawText().ShouldNotContain(HazardStation);

        // It is on the log — the whole of it, and not the log of any one person.
        var log = await LogAsync(owner, trip);
        log.Count.ShouldBe(5);
        log.ShouldContain(e => Text(e, "kind") == "caveNote" && Text(e, "stationName") == HazardStation);
        foreach (var caver in cavers)
        {
            var own = await owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events?caverId={caver}");
            (await own.Content.ReadAsStringAsync()).ShouldNotContain(Words);
        }
    }

    /// <summary>
    /// The camp's count of who is inside, the statistics' tracked trips and hours, and the trips a
    /// survey has reports from are each the same answer before the note and after it.
    /// </summary>
    [Fact]
    public async Task The_camps_count_the_statistics_and_a_surveys_trips_are_what_they_were_before_the_note()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        var (trip, cavers) = await CreateTripAsync("Cave note, at camp", guests: 2);
        (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        // A second trip of the camp whose log will hold a note about the cave and nothing else.
        var (quiet, _) = await CreateTripAsync("Cave note, nobody followed", guests: 1);
        (await ArmAsync(owner, quiet, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var camp = await owner.PostAsJsonAsync("/api/v1/expeditions", new
        {
            name = $"Cave note camp {Guid.NewGuid():N}"[..36],
            startDate = "2026-09-10",
            endDate = "2026-09-20",
            visibility = "authenticated",
        });
        camp.StatusCode.ShouldBe(HttpStatusCode.Created, await camp.Content.ReadAsStringAsync());
        var campId = (await BodyAsync(camp)).GetProperty("id").GetGuid();
        foreach (var joining in new[] { trip, quiet })
        {
            (await owner.PostAsJsonAsync($"/api/v1/expeditions/{campId}/trips", new { tripLogId = joining }))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var earlier = DateTimeOffset.UtcNow.AddHours(-6);
        await ReportAsync(trip, new { caverIds = cavers, kind = "entered", recordedAt = earlier });
        await ReportAsync(trip, new
        {
            caverIds = new[] { cavers[0] }, kind = "atStation", stationName = PartyStation,
            recordedAt = earlier.AddHours(1),
        });
        await ReportAsync(trip, new { caverIds = new[] { cavers[0] }, kind = "exited", recordedAt = earlier.AddHours(4) });

        async Task<(string Camp, string Survey, string Statistics)> ReadAsync()
        {
            var surface = await owner.GetAsync($"/api/v1/expeditions/{campId}/surface-log");
            surface.StatusCode.ShouldBe(HttpStatusCode.OK, await surface.Content.ReadAsStringAsync());
            var tracked = await owner.GetAsync($"/api/v1/survey-models/{model}/tracked-trips");
            tracked.StatusCode.ShouldBe(HttpStatusCode.OK, await tracked.Content.ReadAsStringAsync());
            var statistics = await owner.GetAsync($"/api/v1/stats/expeditions/{campId}");
            statistics.StatusCode.ShouldBe(HttpStatusCode.OK, await statistics.Content.ReadAsStringAsync());
            var figures = await BodyAsync(statistics);
            return (
                (await BodyAsync(surface)).GetProperty("trips").GetRawText(),
                await tracked.Content.ReadAsStringAsync(),
                string.Join('/', new[] { "trackedTrips", "watchUndergroundMinutes", "watchTimedPersonTrips" }
                    .Select(name => figures.GetProperty(name).GetInt32())));
        }

        var before = await ReadAsync();
        // What is being held still is something, not three empty answers.
        before.Statistics.ShouldBe("1/240/1");
        before.Camp.ShouldContain(trip.ToString(), Case.Insensitive);
        before.Survey.ShouldContain("\"reportCount\":1");

        // A note about the cave on each trip, later than every report about a person.
        await ReportAsync(trip, new
        {
            kind = "caveNote", note = Words, stationName = HazardStation, recordedAt = earlier.AddHours(5),
        });
        await ReportAsync(quiet, new
        {
            kind = "caveNote", note = Words, stationName = HazardStation, recordedAt = earlier.AddHours(5),
        });
        (await LogAsync(owner, quiet)).ShouldHaveSingleItem();

        var after = await ReadAsync();
        after.Camp.ShouldBe(before.Camp);
        after.Survey.ShouldBe(before.Survey);
        after.Statistics.ShouldBe(before.Statistics);

        // Both trips were listed for the survey because their watches are on it, which says
        // nothing of what a report does. So the watches move to another survey, and what is left
        // tying a trip to the first one is its reports alone: the report about a person still
        // does, the note about the cave does not.
        var elsewhere = await SeedModelWithStationsAsync(cave);
        foreach (var moved in new[] { trip, quiet })
        {
            (await ArmAsync(owner, moved, elsewhere)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        (await StoredAsync((await LogAsync(owner, quiet)).ShouldHaveSingleItem().GetProperty("id").GetGuid()))
            .SurveyModelId.ShouldBe(model);
        var left = await owner.GetAsync($"/api/v1/survey-models/{model}/tracked-trips");
        var leftText = await left.Content.ReadAsStringAsync();
        left.StatusCode.ShouldBe(HttpStatusCode.OK, leftText);
        leftText.ShouldContain(trip.ToString(), Case.Insensitive);
        leftText.ShouldNotContain(quiet.ToString(), Case.Insensitive);
    }

    [Fact]
    public async Task Folding_two_people_into_one_leaves_a_note_about_the_cave_as_it_was()
    {
        var (trip, cavers) = await ArmedTripAsync("Cave note, at a fold", guests: 2);
        var (stays, leaves) = (cavers[0], cavers[1]);
        var key = Guid.NewGuid();
        await ReportAsync(trip, new { caverIds = new[] { stays, leaves }, kind = "entered" });
        var note = await PostEventAsync(owner, trip, CaveNote(key));
        note.StatusCode.ShouldBe(HttpStatusCode.OK, await note.Content.ReadAsStringAsync());
        var noteId = (await BodyAsync(note)).EnumerateArray().Single().GetProperty("id").GetGuid();

        var keeperEmail = $"cvn-fold-{Guid.NewGuid():N}"[..20] + "@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, keeperEmail);
        var keeper = await AuthHelper.BearerClientAsync(factory, keeperEmail);
        var merged = await keeper.PostAsJsonAsync($"/api/v1/cavers/{stays}/merge", new { sourceCaverId = leaves });
        merged.StatusCode.ShouldBe(HttpStatusCode.OK, await merged.Content.ReadAsStringAsync());

        // The fold moved the reports about the person merged away, and only those.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var rows = await db.TripPositionEvents.IgnoreQueryFilters().Where(e => e.TripLogId == trip).ToListAsync();
            rows.Count.ShouldBe(3);
            rows.Count(e => e.CaverId == stays).ShouldBe(2);
            rows.Single(e => e.CaverId == null).Id.ShouldBe(noteId);
        }

        // Its act is still recognised: a repeat of the send is answered with it and writes nothing.
        var again = await PostEventAsync(owner, trip, CaveNote(key));
        again.StatusCode.ShouldBe(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
        (await BodyAsync(again)).EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid().ShouldBe(noteId);
        (await LogAsync(owner, trip)).Count.ShouldBe(3);
    }

    // ---- the sheet -----------------------------------------------------------------------

    /// <summary>
    /// The log written out as a sheet holds the reports about people and not the note about the
    /// cave; read straight back, it changes nothing and the note is still on the log.
    /// </summary>
    [Fact]
    public async Task The_sheet_leaves_a_note_about_the_cave_out_and_reading_it_back_leaves_the_note_on_the_log()
    {
        var (trip, cavers) = await ArmedTripAsync("Cave note, as a sheet", guests: 1);
        await ReportAsync(trip, new
        {
            caverIds = cavers, kind = "atStation", stationName = PartyStation, recordedAt = "2026-09-12T09:00:00Z",
        });
        // At the very instant of the person's report, so that a reader keyed by the moment alone
        // would take one for the other.
        await ReportAsync(trip, new
        {
            kind = "caveNote", note = Words, stationName = HazardStation, recordedAt = "2026-09-12T09:00:00Z",
        });
        var noteId = (await LogAsync(owner, trip)).Single(e => Text(e, "kind") == "caveNote").GetProperty("id").GetGuid();

        var sheet = await SheetAsync(owner, trip);
        var lines = sheet.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(2, "the header and the one report about a person");
        lines[1].ShouldContain(PartyStation);
        sheet.ShouldNotContain(Words);
        sheet.ShouldNotContain(HazardStation);

        var body = new { text = sheet, replaceExisting = true };
        var commit = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/csv-import/commit", body);
        var committed = await BodyAsync(commit);
        commit.StatusCode.ShouldBe(HttpStatusCode.OK, committed.GetRawText());
        committed.GetProperty("created").GetInt32().ShouldBe(0);
        committed.GetProperty("updated").GetInt32().ShouldBe(0);
        committed.GetProperty("unchanged").GetInt32().ShouldBe(1);

        var log = await LogAsync(owner, trip);
        log.Count.ShouldBe(2);
        var still = log.Single(e => e.GetProperty("id").GetGuid() == noteId);
        Text(still, "note").ShouldBe(Words);
        Text(still, "stationName").ShouldBe(HazardStation);
        still.GetProperty("corrected").GetBoolean().ShouldBeFalse();
    }

    // ---- nobody without an account -------------------------------------------------------

    /// <summary>
    /// No read that needs no account holds a note about the cave — its words, its station or its
    /// kind — of the trip the link is for or of another party in the same cave, while the watches
    /// run or once one is history.
    /// </summary>
    /// <remarks>
    /// Two trips in one unprotected cave, both published, each with somebody reported at one
    /// station and a note about the cave at another. Every public route is read through the first
    /// trip's link and searched as raw text; each is first shown to carry the person's station or
    /// the other trip, so that a route answering with nothing at all cannot pass. The signed-in log
    /// of the same trips carries the note throughout. A third trip, closed, holds a note about the
    /// cave at a station and no report about anybody: it has no track to offer.
    /// </remarks>
    [Fact]
    public async Task No_read_without_an_account_holds_a_note_about_the_cave()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);
        var (mine, myCavers) = await CreateTripAsync("Cave note, published", guests: 1);
        var (theirs, theirCavers) = await CreateTripAsync("Cave note, beside it", guests: 1);
        var (empty, _) = await CreateTripAsync("Cave note, nobody placed", guests: 1);
        foreach (var (trip, cavers) in new[] { (mine, myCavers), (theirs, theirCavers) })
        {
            (await ArmAsync(owner, trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
            await ReportAsync(trip, new { caverIds = cavers, kind = "entered" });
            await ReportAsync(trip, new { caverIds = cavers, kind = "atStation", stationName = PartyStation });
            await ReportAsync(trip, CaveNote());
        }
        (await ArmAsync(owner, empty, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(empty, CaveNote());

        var token = await PublishAsync(mine);
        _ = await PublishAsync(theirs);
        _ = await PublishAsync(empty);
        var link = $"/api/v1/public/trips/{Uri.EscapeDataString(token)}";

        async Task<string> PublicAsync(string url)
        {
            var response = await anonymous.GetAsync(url);
            var text = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{url}: {text}");
            text.ShouldNotContain(Words);
            text.ShouldNotContain(HazardStation);
            text.ShouldNotContain("caveNote", Case.Insensitive);
            return text;
        }

        async Task SignedInHoldsTheNoteAsync(Guid trip)
        {
            (await LogAsync(owner, trip)).ShouldContain(e =>
                Text(e, "kind") == "caveNote" && Text(e, "note") == Words && Text(e, "stationName") == HazardStation);
        }

        // While the watches run: the followed page, and the cave's other parties.
        await SignedInHoldsTheNoteAsync(mine);
        await SignedInHoldsTheNoteAsync(theirs);
        (await PublicAsync(link)).ShouldContain(PartyStation);
        var beside = await PublicAsync($"{link}/live");
        beside.ShouldContain(theirs.ToString(), Case.Insensitive);
        beside.ShouldContain(PartyStation);

        // Once the other two are history: the list of past trips, and a track.
        await CloseAsync(theirs, DateTimeOffset.UtcNow.AddDays(-5));
        await CloseAsync(empty, DateTimeOffset.UtcNow.AddDays(-6));
        var past = await PublicAsync($"{link}/past");
        past.ShouldContain(theirs.ToString(), Case.Insensitive);
        (await PublicAsync($"{link}/past/{theirs}")).ShouldContain(PartyStation);

        // A station named only by a note about the cave is nobody's place: that trip is listed
        // with no track, where the trip beside it — the same note, and a person at a station —
        // has one.
        var listed = JsonDocument.Parse(past).RootElement.GetProperty("trips").EnumerateArray().ToList();
        bool HasTrack(Guid trip) => listed
            .Single(row => row.GetProperty("tripLogId").GetGuid() == trip).GetProperty("playable").GetBoolean();
        HasTrack(theirs).ShouldBeTrue();
        HasTrack(empty).ShouldBeFalse();

        // And the closed trips still hold the note for those who are signed in.
        await SignedInHoldsTheNoteAsync(theirs);
        await SignedInHoldsTheNoteAsync(empty);
    }

    // ---- plumbing ------------------------------------------------------------------------

    /// <summary>A note about the cave at the hazard's station, under a key where one is given.</summary>
    private static object CaveNote(Guid? clientKey = null) => clientKey is { } key
        ? new { kind = "caveNote", note = Words, stationName = HazardStation, clientKey = key }
        : new { kind = "caveNote", note = Words, stationName = HazardStation };

    private static object CaveNoteEdit() => new { kind = "caveNote", note = Words, stationName = HazardStation };

    /// <summary>A string member, or null where it is null — and a failure where it is absent.</summary>
    private static string? Text(JsonElement element, string property) =>
        element.GetProperty(property).ValueKind == JsonValueKind.Null
            ? null
            : element.GetProperty(property).GetString();

    private static string EventOf(Guid trip, Guid eventId) => $"/api/v1/trip-logs/{trip}/tracking/events/{eventId}";

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, text);
        JsonDocument.Parse(text).RootElement.GetProperty("code").GetString().ShouldBe(code);
    }

    private async Task ReportAsync(Guid trip, object body)
    {
        var response = await PostEventAsync(owner, trip, body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

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

    private async Task<(Guid Trip, List<Guid> Cavers)> CreateTripAsync(string title, int guests, HttpClient? client = null)
    {
        var participants = Enumerable.Range(1, guests)
            .Select(i => new { newCaverName = $"Guest {i} {Guid.NewGuid():N}"[..24] })
            .ToArray();
        var response = await (client ?? owner).PostAsJsonAsync("/api/v1/trip-logs/", new
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
            name = $"Note Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>Protects a cave, or lifts it, through the service that keeps the derived protection in step.</summary>
    private async Task SetLocationProtectedAsync(Guid caveFeatureId, bool value)
    {
        using var scope = factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
        await writer.SetLocationProtectedAsync(caveFeatureId, value);
        await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().SaveChangesAsync();
    }

    private async Task<Guid> SeedModelWithStationsAsync(Guid caveId)
    {
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new("application/octet-stream");
        form.Add(bytes, "file", "cave-note.3d");
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
            Station(modelId, PartyStation, "cave.upper", 300, SurveyStationFlags.Underground),
            Station(modelId, HazardStation, "cave.deep", 230, SurveyStationFlags.Underground));
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
