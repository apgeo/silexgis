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
/// Which words a recorded position is written down in, across the one format where the survey rows
/// and the viewer that draws the model do not agree.
///
/// <para>
/// The model these tests seed is the shape the disagreement needs: a Therion file whose root survey
/// is named, so that every station's stored name carries one leading component the viewer's own
/// reading of the same file never had. A report is made in the words the viewer uses — the words a
/// press on the model arrives in — and what must be true afterwards is that it was accepted and
/// that what came back is a name the viewer can resolve. Both halves matter: accepting the report
/// and then storing a name nothing can draw is the defect, not a fix for it.
/// </para>
/// <para>
/// Two controls are seeded beside it, and both are models whose spellings coincide: a Survex file,
/// which has no survey tree at all, and a Therion file whose root survey is unnamed — which is the
/// shape every compiled Therion file examined actually has. A rule that cut a component off either
/// would rename every station in it, so the tests that nothing is cut there are what tell a working
/// conversion from an eager one. The named root the tests above use is a shape the format permits
/// and nothing here has been observed to write.
/// </para>
/// </summary>
public sealed class TripTrackingStationNamesTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private long caveTypeId;

    public TripTrackingStationNamesTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            // Workers off: the graph-extraction job would otherwise pick up the placeholder survey
            // file below, fail to read it, and rewrite the very station rows these tests seed.
            JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"trkname-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"trkname-{suffix}@t.local");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        try { Directory.Delete(filesRoot, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task A_station_pressed_on_a_therion_model_is_recorded_and_stored_in_the_viewers_words()
    {
        var (trip, cavers) = await CreateTripAsync();
        var model = await SeedModelAsync(SurveyModelFormat.Lox, rootSurveyName: "cave");
        (await ArmAsync(trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The name the viewer hands over when somebody presses that station: the stored name is
        // "cave.ent.0", and the viewer's reading of the same file never had the root's component.
        var recorded = await ReportStationAsync(trip, cavers, "ent.0");
        recorded.StatusCode.ShouldBe(HttpStatusCode.OK, await recorded.Content.ReadAsStringAsync());
        (await BodyAsync(recorded)).EnumerateArray().Single()
            .GetProperty("stationName").GetString().ShouldBe("ent.0");

        // And the row itself, because the thing that fails silently is what was written down, not
        // what one response happened to echo.
        (await StoredNamesAsync(trip)).ShouldBe(["ent.0"]);

        // What the watch shows the panel that draws the markers.
        (await StateAsync(trip)).GetProperty("participants").EnumerateArray().Single()
            .GetProperty("stationName").GetString().ShouldBe("ent.0");
    }

    [Fact]
    public async Task A_name_belonging_to_neither_spelling_is_still_refused()
    {
        var (trip, cavers) = await CreateTripAsync();
        var model = await SeedModelAsync(SurveyModelFormat.Lox, rootSurveyName: "cave");
        (await ArmAsync(trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The negative twin of the two accepted spellings: the conversion widens what is accepted
        // to the names of stations that exist, and to nothing else. "cave.cave.ent.0" is what
        // converting an already-converted name would produce, and it names no station.
        var refused = await ReportStationAsync(trip, cavers, "cave.cave.ent.0");
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await BodyAsync(refused)).GetProperty("code").GetString().ShouldBe("tracking.station_unknown");
        (await StoredNamesAsync(trip)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_surveys_own_spelling_is_accepted_and_written_down_in_the_viewers()
    {
        var (trip, cavers) = await CreateTripAsync();
        var model = await SeedModelAsync(SurveyModelFormat.Lox, rootSurveyName: "cave");
        (await ArmAsync(trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A name in the words the survey rows use — which is what a depth preview offers, what an
        // import proposes, and what somebody reading a survey's own listing types. Accepted,
        // because it names a station that exists; normalised, because what is stored is handed to
        // the viewer to draw.
        var recorded = await ReportStationAsync(trip, cavers, "cave.ent.0");
        recorded.StatusCode.ShouldBe(HttpStatusCode.OK, await recorded.Content.ReadAsStringAsync());
        (await StoredNamesAsync(trip)).ShouldBe(["ent.0"]);
    }

    [Fact]
    public async Task A_depth_report_stamps_a_station_the_viewer_can_resolve()
    {
        var (trip, cavers) = await CreateTripAsync();
        var model = await SeedModelAsync(SurveyModelFormat.Lox, rootSurveyName: "cave");
        (await ArmAsync(trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The preview first: it is a list somebody picks a station out of, so it has to be offered
        // in the same words the report it leads to will be stored in.
        var preview = await owner.PostAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/tracking/resolve-depth", new { depthM = 50 });
        preview.StatusCode.ShouldBe(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        (await BodyAsync(preview)).EnumerateArray().First()
            .GetProperty("stationName").GetString().ShouldBe("upper.2");

        var recorded = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", new
        {
            caverIds = cavers,
            kind = "atDepth",
            depthM = 50,
        });
        recorded.StatusCode.ShouldBe(HttpStatusCode.OK, await recorded.Content.ReadAsStringAsync());

        // −50 m below the entrance at 350 m is the station at 300 m, stored as "cave.upper.2" and
        // addressed by the viewer as "upper.2". The depth path resolves its candidate out of the
        // survey rows, so this is the assertion that the conversion happens before the stamp and
        // not only on the path a person typed a name into.
        var stamped = (await StoredNamesAsync(trip)).Single();
        stamped.ShouldBe("upper.2");

        // Resolvable, stated as the thing that actually matters rather than as a string comparison:
        // the name that was stamped is one this model answers to, which is the same question the
        // viewer asks of it when it goes to place a marker.
        (await ReportStationAsync(trip, cavers, stamped)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_depth_preview_names_the_declared_station_first_because_that_is_what_recording_writes()
    {
        var (trip, cavers) = await CreateTripAsync();
        var model = await SeedModelAsync(SurveyModelFormat.Lox, rootSurveyName: "cave");
        (await ArmAsync(trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var cave = await CaveOfAsync(model);

        // The club says 110 m is the gallery at the station 50 m down; measuring says the station
        // 120 m down is nearer. Recording honours the club, so the preview the report form and the
        // log rows are drawn from has to say the same, or the coordinator is warned about a gap to
        // a station the log never receives.
        var declared = await owner.PutAsJsonAsync($"/api/v1/caves/{cave}/depth-places",
            new { depthM = 110m, stationName = "upper.2", placeLabel = "Galeria" });
        declared.StatusCode.ShouldBe(HttpStatusCode.OK, await declared.Content.ReadAsStringAsync());

        var preview = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/resolve-depth",
            new { depthM = 110 });
        preview.StatusCode.ShouldBe(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        var candidates = (await BodyAsync(preview)).EnumerateArray().ToList();
        candidates[0].GetProperty("stationName").GetString().ShouldBe("upper.2");
        candidates[0].GetProperty("declared").GetBoolean().ShouldBeTrue();
        // Its distance is measured honestly, so a declaration far from its depth is visible.
        candidates[0].GetProperty("depthM").GetDouble().ShouldBe(50);
        candidates[0].GetProperty("deltaM").GetDouble().ShouldBe(60);
        candidates[1].GetProperty("stationName").GetString().ShouldBe("deep.3");
        candidates[1].GetProperty("declared").GetBoolean().ShouldBeFalse();
        candidates.Count(c => c.GetProperty("stationName").GetString() == "upper.2").ShouldBe(1);

        // And the record lands where the preview said.
        var recorded = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events",
            new { caverIds = cavers, kind = "atDepth", depthM = 110 });
        recorded.StatusCode.ShouldBe(HttpStatusCode.OK, await recorded.Content.ReadAsStringAsync());
        (await BodyAsync(recorded)).EnumerateArray().Single()
            .GetProperty("stationName").GetString().ShouldBe("upper.2");

        // A depth nobody declared previews as it always did: nearest first, nothing declared.
        var measured = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/resolve-depth",
            new { depthM = 45 });
        var nearest = (await BodyAsync(measured)).EnumerateArray().First();
        nearest.GetProperty("stationName").GetString().ShouldBe("upper.2");
        nearest.GetProperty("declared").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_report_whose_moment_carries_a_zone_other_than_utc_is_recorded_at_that_instant()
    {
        var (trip, cavers) = await CreateTripAsync();
        var model = await SeedModelAsync(SurveyModelFormat.Lox, rootSurveyName: "cave");
        (await ArmAsync(trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The bundled client sends Z, so this never showed there; any other client — a script, a
        // phone form — writes the zone it is in, and the instant column refuses every offset but
        // zero. Stored as the same instant, spelled in UTC, rather than answered with a 500.
        var recorded = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", new
        {
            caverIds = cavers,
            kind = "entered",
            recordedAt = "2026-09-12T14:05:00+03:00",
        });
        recorded.StatusCode.ShouldBe(HttpStatusCode.OK, await recorded.Content.ReadAsStringAsync());
        var entry = (await BodyAsync(recorded)).EnumerateArray().Single();
        entry.GetProperty("recordedAt").GetDateTimeOffset()
            .ShouldBe(new DateTimeOffset(2026, 9, 12, 11, 5, 0, TimeSpan.Zero));

        // The correction route takes the same shape and has the same column behind it.
        var corrected = await owner.PutAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/tracking/events/{entry.GetProperty("id").GetGuid()}", new
            {
                kind = "entered",
                recordedAt = "2026-09-12T14:10:00+03:00",
            });
        corrected.StatusCode.ShouldBe(HttpStatusCode.OK, await corrected.Content.ReadAsStringAsync());
        (await BodyAsync(corrected)).GetProperty("recordedAt").GetDateTimeOffset()
            .ShouldBe(new DateTimeOffset(2026, 9, 12, 11, 10, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task A_log_that_cannot_be_corrected_cannot_be_emptied_either_and_both_say_the_same_thing()
    {
        // A trip whose watch was never armed, holding a row the way an archive imported onto an
        // existing trip leaves one. One rule governs recording, correcting and removing, so the
        // row is neither correctable nor deletable here — never deletable-but-unrepairable — and
        // the watch, once armed, opens all three at once.
        var (trip, cavers) = await CreateTripAsync();
        var model = await SeedModelAsync(SurveyModelFormat.Survex3d, rootSurveyName: null);
        Guid eventId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var cave = await CaveOfAsync(model);
            var row = new TripPositionEvent
            {
                TripLogId = trip,
                CaverId = cavers[0],
                Kind = TripPositionEventKind.AtStation,
                SurveyModelId = model,
                CaveFeatureId = cave,
                ViewerStationName = "cave.upper.2",
                RecordedAt = new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero),
                RecordedByUserId = await db.Users.Select(u => u.Id).FirstAsync(),
            };
            db.TripPositionEvents.Add(row);
            await db.SaveChangesAsync();
            eventId = row.Id;
        }

        // What a page is told about such a trip, which is all it has to decide by. The watch reads
        // as off and never started — a trip with no tracking row answers exactly as one whose row
        // is off — while the log lists the row. So a page that offers its controls by the first
        // and reads its rows from the second can tell this log from one it may change, and need
        // not offer a correction that can only be refused.
        var told = await BodyAsync(await owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking"));
        told.GetProperty("state").GetString().ShouldBe("off");
        told.GetProperty("armedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        (await BodyAsync(await owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events")))
            .GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid())
            .ShouldBe([eventId]);

        var corrected = await owner.PutAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events/{eventId}",
            new { kind = "note", note = "corrected" });
        corrected.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BodyAsync(corrected)).GetProperty("code").GetString().ShouldBe("tracking.not_writable");

        var removed = await owner.DeleteAsync($"/api/v1/trip-logs/{trip}/tracking/events/{eventId}");
        removed.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BodyAsync(removed)).GetProperty("code").GetString().ShouldBe("tracking.not_writable");

        (await ArmAsync(trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        // The same read is what says the controls now work: it changes with the rule, not beside it.
        (await BodyAsync(await owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking")))
            .GetProperty("state").GetString().ShouldBe("armed");
        (await owner.DeleteAsync($"/api/v1/trip-logs/{trip}/tracking/events/{eventId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_declared_place_whose_station_the_model_lacks_is_listed_as_such_and_a_report_at_it_says_it_was_measured()
    {
        var (trip, cavers) = await CreateTripAsync();
        var model = await SeedModelAsync(SurveyModelFormat.Lox, rootSurveyName: "cave");
        (await ArmAsync(trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var cave = await CaveOfAsync(model);

        // One declaration the model resolves, and one naming a station it does not have — a typo,
        // or a survey re-exported under other names. Both are the cave's own answers.
        foreach (var (depth, station, label) in new[] { (50m, "upper.2", "Galeria"), (120m, "cave.typo.9", "Bivuac") })
        {
            var declared = await owner.PutAsJsonAsync($"/api/v1/caves/{cave}/depth-places",
                new { depthM = depth, stationName = station, placeLabel = label });
            declared.StatusCode.ShouldBe(HttpStatusCode.OK, await declared.Content.ReadAsStringAsync());
        }

        // Listed, both of them, and told apart: the broken one is offered with the fault on it
        // rather than as a place that quietly records somewhere else.
        var places = await owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking/places");
        places.StatusCode.ShouldBe(HttpStatusCode.OK, await places.Content.ReadAsStringAsync());
        var listed = (await BodyAsync(places)).EnumerateArray()
            .ToDictionary(p => p.GetProperty("placeLabel").GetString()!, p => p.GetProperty("stationInModel").GetBoolean());
        listed["Galeria"].ShouldBeTrue();
        listed["Bivuac"].ShouldBeFalse();

        // Recording at the broken place is measured instead — 120 m below 350 m is the station at
        // 230 m — and the answer says so, which is the only thing that tells the person who picked
        // "Bivuac" that the log holds something else.
        var recorded = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", new
        {
            caverIds = cavers,
            kind = "atDepth",
            depthM = 120,
        });
        recorded.StatusCode.ShouldBe(HttpStatusCode.OK, await recorded.Content.ReadAsStringAsync());
        var answer = (await BodyAsync(recorded)).EnumerateArray().Single();
        answer.GetProperty("stationName").GetString().ShouldBe("deep.3");
        answer.GetProperty("depthPlacement").GetString().ShouldBe("measured");

        // And at the good one the declaration is honoured, and says that too.
        var honoured = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", new
        {
            caverIds = cavers,
            kind = "atDepth",
            depthM = 50,
        });
        honoured.StatusCode.ShouldBe(HttpStatusCode.OK, await honoured.Content.ReadAsStringAsync());
        (await BodyAsync(honoured)).EnumerateArray().Single()
            .GetProperty("depthPlacement").GetString().ShouldBe("declared");
    }

    [Fact]
    public async Task A_depth_typed_finer_than_the_log_keeps_is_placed_stored_and_echoed_as_one_number()
    {
        var (trip, cavers) = await CreateTripAsync();
        var model = await SeedModelAsync(SurveyModelFormat.Lox, rootSurveyName: "cave");
        (await ArmAsync(trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var cave = await CaveOfAsync(model);

        // The cave says 120 m is the upper gallery, where the arithmetic would say the station at
        // 230 m. A declaration is exact on its depth, and the column keeps one decimal.
        var declared = await owner.PutAsJsonAsync($"/api/v1/caves/{cave}/depth-places",
            new { depthM = 120m, stationName = "upper.1", placeLabel = "Galeria" });
        declared.StatusCode.ShouldBe(HttpStatusCode.OK, await declared.Content.ReadAsStringAsync());

        // Typed with a decimal the column does not keep. Stored, the row reads 120.0 beside any
        // report of 120 — so it has to land where that one lands, and say the depth it will keep.
        var recorded = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", new
        {
            caverIds = cavers,
            kind = "atDepth",
            depthM = 120.04m,
        });
        recorded.StatusCode.ShouldBe(HttpStatusCode.OK, await recorded.Content.ReadAsStringAsync());
        var answer = (await BodyAsync(recorded)).EnumerateArray().Single();
        answer.GetProperty("depthPlacement").GetString().ShouldBe("declared");
        answer.GetProperty("stationName").GetString().ShouldBe("upper.1");
        answer.GetProperty("depthEnteredM").GetDecimal().ShouldBe(120.0m);

        // With nothing declared the depth measures, and it measures on the stored form too: 49.96
        // is 50.0 to the log, and the answer says 50.0.
        var measured = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", new
        {
            caverIds = cavers,
            kind = "atDepth",
            depthM = 49.96m,
        });
        measured.StatusCode.ShouldBe(HttpStatusCode.OK, await measured.Content.ReadAsStringAsync());
        var nearest = (await BodyAsync(measured)).EnumerateArray().Single();
        nearest.GetProperty("depthPlacement").GetString().ShouldBe("measured");
        nearest.GetProperty("stationName").GetString().ShouldBe("upper.2");
        nearest.GetProperty("depthEnteredM").GetDecimal().ShouldBe(50.0m);

        // Read back, the log says exactly what the two answers said.
        var listed = await owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events");
        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await listed.Content.ReadAsStringAsync());
        var depths = (await BodyAsync(listed)).GetProperty("items").EnumerateArray()
            .Where(e => e.GetProperty("kind").GetString() == "atDepth")
            .ToDictionary(e => e.GetProperty("stationName").GetString()!, e => e.GetProperty("depthEnteredM").GetDecimal());
        depths.Count.ShouldBe(2);
        depths["upper.1"].ShouldBe(120.0m);
        depths["upper.2"].ShouldBe(50.0m);
    }

    [Fact]
    public async Task A_therion_model_whose_root_survey_is_unnamed_is_left_exactly_as_its_rows_are()
    {
        var (trip, cavers) = await CreateTripAsync();
        // The shape every compiled Therion file examined actually has: a survey tree whose root
        // carries no name, so the rows start at the first named survey — which is where the
        // viewer's tree starts too. This is the production case, and the conversion must do nothing
        // to it. A rule that cut a component whenever the format was Therion would rename every
        // station of every real file, which is a worse defect than the one it was written for.
        var model = await SeedModelAsync(SurveyModelFormat.Lox, rootSurveyName: null);
        (await ArmAsync(trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ReportStationAsync(trip, cavers, "cave.ent.0")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StoredNamesAsync(trip)).ShouldBe(["cave.ent.0"]);

        // And nothing new is accepted: with no root name there is no second reading, so the cut
        // name names no station of this model.
        var refused = await ReportStationAsync(trip, cavers, "ent.0");
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await BodyAsync(refused)).GetProperty("code").GetString().ShouldBe("tracking.station_unknown");
    }

    [Fact]
    public async Task A_survex_model_is_left_spelled_exactly_as_its_rows_are()
    {
        var (trip, cavers) = await CreateTripAsync();
        // No survey tree, so no root survey name and nothing that could be dropped: the control
        // that fails if the conversion ever becomes "cut at the first separator".
        var model = await SeedModelAsync(SurveyModelFormat.Survex3d, rootSurveyName: null);
        (await ArmAsync(trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ReportStationAsync(trip, cavers, "cave.ent.0")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StoredNamesAsync(trip)).ShouldBe(["cave.ent.0"]);
    }

    [Fact]
    public async Task The_depth_datum_may_be_named_in_either_spelling_and_a_station_that_is_neither_is_refused()
    {
        var (trip, _) = await CreateTripAsync();
        var model = await SeedModelAsync(SurveyModelFormat.Lox, rootSurveyName: "cave");

        // Named as it reads on the model, which is where somebody picking a datum reads it.
        var armed = await PutConfigAsync(trip, new
        {
            state = "armed",
            surveyModelId = model,
            referenceStationName = "ent.0",
        });
        armed.StatusCode.ShouldBe(HttpStatusCode.OK, await armed.Content.ReadAsStringAsync());

        // Comes back as it was typed. The box it is typed into is filled from what is stored, so a
        // datum kept in the survey's own words would answer an administrator with a name they can
        // find nowhere on the model they picked it off.
        (await StateAsync(trip)).GetProperty("referenceStationName").GetString().ShouldBe("ent.0");

        // A depth resolves against it either way: the datum is matched under both of a station's
        // names, so which vocabulary it was stored in is not something the arithmetic has to know.
        var preview = await owner.PostAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/tracking/resolve-depth", new { depthM = 50 });
        preview.StatusCode.ShouldBe(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        (await BodyAsync(preview)).EnumerateArray().First()
            .GetProperty("stationName").GetString().ShouldBe("upper.2");

        // The survey's own spelling is accepted too — an import proposes names in it, and so does a
        // survey listing — and is written down the same way a reported station is.
        var byRowName = await PutConfigAsync(trip, new
        {
            state = "armed",
            surveyModelId = model,
            referenceStationName = "cave.ent.0",
        });
        byRowName.StatusCode.ShouldBe(HttpStatusCode.OK, await byRowName.Content.ReadAsStringAsync());
        (await StateAsync(trip)).GetProperty("referenceStationName").GetString().ShouldBe("ent.0");

        var refused = await PutConfigAsync(trip, new
        {
            state = "armed",
            surveyModelId = model,
            referenceStationName = "nowhere.9",
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BodyAsync(refused)).GetProperty("code").GetString().ShouldBe("tracking.reference_unknown");
    }

    [Fact]
    public async Task A_depth_filter_typed_from_the_preview_keeps_the_stations_it_names()
    {
        var (trip, cavers) = await CreateTripAsync();
        var model = await SeedModelAsync(SurveyModelFormat.Lox, rootSurveyName: "cave");

        // The filter is written by somebody reading the preview beside it, and the preview is in
        // the viewer's words. A filter that only understood the rows' words would keep nothing from
        // a name copied out of that list, and every depth report under it would be refused with
        // nothing on screen saying why.
        var configured = await PutConfigAsync(trip, new
        {
            state = "armed",
            surveyModelId = model,
            depthFilter = new[] { "upper" },
        });
        configured.StatusCode.ShouldBe(HttpStatusCode.OK, await configured.Content.ReadAsStringAsync());

        var preview = await owner.PostAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/tracking/resolve-depth", new { depthM = 50 });
        preview.StatusCode.ShouldBe(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        (await BodyAsync(preview)).EnumerateArray().Select(c => c.GetProperty("stationName").GetString())
            .ShouldAllBe(name => name!.StartsWith("upper."));

        var recorded = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", new
        {
            caverIds = cavers,
            kind = "atDepth",
            depthM = 50,
        });
        recorded.StatusCode.ShouldBe(HttpStatusCode.OK, await recorded.Content.ReadAsStringAsync());
        (await StoredNamesAsync(trip)).ShouldBe(["upper.2"]);

        // Widened, not opened: a prefix of neither spelling still keeps nothing, and the report
        // that would have landed on it is refused rather than placed somewhere arbitrary.
        var narrowed = await PutConfigAsync(trip, new
        {
            state = "armed",
            surveyModelId = model,
            depthFilter = new[] { "nowhere" },
        });
        narrowed.StatusCode.ShouldBe(HttpStatusCode.OK, await narrowed.Content.ReadAsStringAsync());
        var nothing = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", new
        {
            caverIds = cavers,
            kind = "atDepth",
            depthM = 50,
        });
        nothing.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BodyAsync(nothing)).GetProperty("code").GetString().ShouldBe("tracking.no_station_at_depth");
    }

    // ---- plumbing --------------------------------------------------------------------------

    private async Task<(Guid Trip, List<Guid> Cavers)> CreateTripAsync()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Station names {Guid.NewGuid():N}"[..30],
            tripDate = "2026-09-16",
            participants = new[] { new { newCaverName = $"Guest {Guid.NewGuid():N}"[..20] } },
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var trip = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var cavers = await db.TripLogParticipants.Where(p => p.TripLogId == trip)
            .Select(p => p.CaverId).Distinct().ToListAsync();
        return (trip, cavers);
    }

    /// <summary>
    /// A survey model of the given format, uploaded the real way — a file row cannot exist outside
    /// the documents chain — with the station rows a reading of such a file would have produced
    /// seeded straight into the graph tables, and the root survey named as that reading would have
    /// named it. An entrance at 350 m and two branches sharing the −50 m horizon, so a depth report
    /// has something to resolve to.
    /// </summary>
    private async Task<Guid> SeedModelAsync(SurveyModelFormat format, string? rootSurveyName)
    {
        var caveResponse = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Name Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        caveResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await caveResponse.Content.ReadAsStringAsync());
        var caveId = (await caveResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new("application/octet-stream");
        form.Add(bytes, "file", format == SurveyModelFormat.Lox ? "tracking.lox" : "tracking.3d");
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var modelId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.SingleAsync(m => m.Id == modelId);
        model.Format.ShouldBe(format);
        model.Status = SurveyModelStatus.Ready;
        model.RootSurveyName = rootSurveyName;
        db.SurveyStations.AddRange(
            Station(modelId, "cave.ent.0", "cave.ent", 350, SurveyStationFlags.Entrance),
            Station(modelId, "cave.upper.1", "cave.upper", 340, SurveyStationFlags.Underground),
            Station(modelId, "cave.upper.2", "cave.upper", 300, SurveyStationFlags.Underground),
            Station(modelId, "cave.deep.3", "cave.deep", 230, SurveyStationFlags.Underground));
        await db.SaveChangesAsync();
        return modelId;
    }

    private static SurveyStation Station(
        Guid modelId, string name, string survey, double z, SurveyStationFlags flags) =>
        new()
        {
            SurveyModelId = modelId,
            Name = name,
            SurveyName = survey,
            Position = new Point(new CoordinateZ(25.5, 45.5, z)) { SRID = 4326 },
            Flags = flags,
        };

    /// <summary>Every station name this trip's log actually holds, in the order it was written.</summary>
    private async Task<Guid> CaveOfAsync(Guid model)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.SurveyModels.Where(m => m.Id == model).Select(m => m.CaveFeatureId).SingleAsync();
    }

    private async Task<List<string>> StoredNamesAsync(Guid trip)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripPositionEvents.AsNoTracking()
            .Where(e => e.TripLogId == trip && e.ViewerStationName != null)
            .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .Select(e => e.ViewerStationName!)
            .ToListAsync();
    }

    private Task<HttpResponseMessage> ReportStationAsync(Guid trip, List<Guid> cavers, string stationName) =>
        owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", new
        {
            caverIds = cavers,
            kind = "atStation",
            stationName,
        });

    /// <summary>Config writes ride the trip's version: fetch the ETag, then PUT with If-Match.</summary>
    private async Task<HttpResponseMessage> PutConfigAsync(Guid trip, object body)
    {
        var current = await owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking");
        current.StatusCode.ShouldBe(HttpStatusCode.OK, await current.Content.ReadAsStringAsync());
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/trip-logs/{trip}/tracking")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("If-Match", current.Headers.ETag!.ToString());
        return await owner.SendAsync(request);
    }

    private Task<HttpResponseMessage> ArmAsync(Guid trip, Guid model) =>
        PutConfigAsync(trip, new { state = "armed", surveyModelId = model });

    private async Task<JsonElement> StateAsync(Guid trip)
    {
        var response = await owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await BodyAsync(response);
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
