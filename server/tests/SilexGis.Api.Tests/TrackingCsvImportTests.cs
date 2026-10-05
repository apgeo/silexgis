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
/// Reading a club's spreadsheet of tracking reports onto a trip's log.
///
/// <para>
/// The two properties these tests exist for are the ones the feature turns on. Every gate a typed
/// report passes, an imported one passes through the same code — so a sheet cannot name somebody
/// who was not on the trip, and cannot place anybody at a station the survey does not have. And
/// the key is the person and the instant, so importing the same sheet twice leaves one log and a
/// corrected sheet re-imported changes the rows it corrected instead of doubling them.
/// </para>
/// <para>
/// The sheets below are invented and deliberately small; the participants are created by these
/// tests, so the names written in the sheets are names this trip actually has.
/// </para>
/// </summary>
public sealed class TrackingCsvImportTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private long caveTypeId;

    public TrackingCsvImportTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            // Workers off: the graph-extraction job would otherwise pick up the placeholder survey
            // file below, fail to read it, and rewrite the station rows these tests seed.
            JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"csvimp-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"csvimp-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"csvimp-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"csvimp-read-{suffix}@t.local");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        try { Directory.Delete(filesRoot, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>A short trip: in together, apart at two depths, out together.</summary>
    private const string Sheet =
        "Data si ora,Adancime,Statie,Loc,Speologi,Echipa,Nota,Stare\r\n"
        + "12.09.2026 08:15,0,,,\"Ion Popescu; Maria Pop\",,,intrare\r\n"
        + "12.09.2026 09:40,50,,,Ion Popescu,,,\r\n"
        + "12.09.2026 09:45,50,,,Maria Pop,,apa mare,\r\n"
        + "12.09.2026 16:45,0,,,\"Ion Popescu; Maria Pop\",,,iesire\r\n";

    [Fact]
    public async Task A_sheet_of_reports_becomes_reports_on_the_trips_log()
    {
        var trip = await ArmedTripAsync();

        var preview = await PreviewAsync(trip, Sheet);
        preview.GetProperty("rowsRead").GetInt32().ShouldBe(4);
        // Four rows, two of them about a party of two: six reports.
        preview.GetProperty("creates").GetInt32().ShouldBe(6);
        preview.GetProperty("replaces").GetInt32().ShouldBe(0);
        preview.GetProperty("unmatchedCavers").EnumerateArray().ShouldBeEmpty();
        preview.GetProperty("refused").EnumerateArray().ShouldBeEmpty();

        // A preview writes nothing: a sheet somebody opened and thought better of leaves the log
        // exactly as it was.
        (await EventCountAsync(trip)).ShouldBe(0);

        var commit = await CommitAsync(trip, Sheet);
        commit.GetProperty("created").GetInt32().ShouldBe(6);
        commit.GetProperty("updated").GetInt32().ShouldBe(0);
        (await EventCountAsync(trip)).ShouldBe(6);

        // The depth rows landed on the station 50 m below the entrance, in the words the viewer
        // uses — a name stored in any other spelling is a marker that silently never appears.
        (await StoredNamesAsync(trip)).ShouldBe(["upper.2", "upper.2"]);
    }

    [Fact]
    public async Task Re_importing_a_corrected_sheet_changes_the_rows_it_corrected_rather_than_doubling_them()
    {
        var trip = await ArmedTripAsync();
        (await CommitAsync(trip, Sheet)).GetProperty("created").GetInt32().ShouldBe(6);
        var firstIds = await EventIdsAsync(trip);

        // The same instants and the same people, with one note written properly. Person and
        // instant are the key, so this is the same six reports said again.
        var corrected = Sheet.Replace("apa mare", "apa mare in meandru");

        var second = await CommitAsync(trip, corrected, replaceExisting: true);
        second.GetProperty("created").GetInt32().ShouldBe(0);
        second.GetProperty("updated").GetInt32().ShouldBe(6);
        (await EventCountAsync(trip)).ShouldBe(6);

        // Changed on the rows they were, not replaced by six new ones: a picture pinned to a report,
        // or a link somebody holds to it, names the row, and a re-import that deleted and recreated
        // would count the same and leave every one of those pointing at nothing.
        (await EventIdsAsync(trip)).ShouldBe(firstIds);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.TripPositionEvents.CountAsync(e => e.TripLogId == trip && e.Note == "apa mare in meandru"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Without_leave_to_overwrite_a_report_the_log_already_holds_is_left_alone_and_said_so()
    {
        // Re-import is the intended use and also the one act that silently rewrites history, so
        // overwriting is asked for rather than assumed.
        var trip = await ArmedTripAsync();
        await CommitAsync(trip, Sheet);

        var again = await CommitAsync(trip, Sheet.Replace("apa mare", "altceva"));
        again.GetProperty("created").GetInt32().ShouldBe(0);
        again.GetProperty("updated").GetInt32().ShouldBe(0);
        again.GetProperty("skipped").GetInt32().ShouldBe(6);
        again.GetProperty("refused").EnumerateArray()
            .Select(d => d.GetProperty("problem").GetString())
            .ShouldAllBe(p => p == "AlreadyRecorded");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.TripPositionEvents.AnyAsync(e => e.TripLogId == trip && e.Note == "altceva"))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task A_name_nobody_on_the_trip_answers_to_costs_that_report_and_not_the_row()
    {
        var trip = await ArmedTripAsync();

        var sheet = "Data si ora,Adancime,Speologi\r\n"
            + "12.09.2026 09:00,50,\"Ion Popescu; Gheorghe Necunoscut\"\r\n";

        var preview = await PreviewAsync(trip, sheet);
        preview.GetProperty("creates").GetInt32().ShouldBe(1);
        preview.GetProperty("unmatchedCavers").EnumerateArray()
            .Select(n => n.GetString()).ShouldBe(["Gheorghe Necunoscut"]);
        preview.GetProperty("refused").EnumerateArray()
            .Select(d => d.GetProperty("problem").GetString()).ShouldContain("CaverNotOnRoster");

        (await CommitAsync(trip, sheet)).GetProperty("created").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task A_given_name_alone_finds_the_one_person_on_the_trip_it_can_mean()
    {
        // What a sheet typed from phone calls actually carries. Matched against this trip's own
        // roster, so it is a small enough set of people for a first name to be unambiguous.
        var trip = await ArmedTripAsync();

        var preview = await PreviewAsync(trip, "Data si ora,Adancime,Speologi\r\n12.09.2026 09:00,50,Ion\r\n");

        var row = preview.GetProperty("rows").EnumerateArray().Single();
        row.GetProperty("caverMatched").GetString().ShouldBe("Ion Popescu");
        row.GetProperty("matchedBy").GetString().ShouldBe("GivenName");
    }

    [Fact]
    public async Task A_place_the_cave_declared_lands_on_the_station_the_declaration_names()
    {
        var (trip, caveId) = await ArmedTripWithCaveAsync();

        var declared = await owner.PutAsJsonAsync($"/api/v1/caves/{caveId}/depth-places", new
        {
            depthM = 110m,
            stationName = "deep.3",
            placeLabel = "Sala Mare",
        });
        declared.StatusCode.ShouldBe(HttpStatusCode.OK, await declared.Content.ReadAsStringAsync());

        // Named rather than measured — which is what a caver on the phone actually says.
        var byName = await PreviewAsync(trip,
            "Data si ora,Loc,Speologi\r\n12.09.2026 09:00,Sala Mare,Ion Popescu\r\n");
        var row = byName.GetProperty("rows").EnumerateArray().Single();
        row.GetProperty("stationName").GetString().ShouldBe("deep.3");
        row.GetProperty("depthM").GetDecimal().ShouldBe(110m);

        // And the declaration outranks measuring: 110 m below the entrance is nearer the station
        // at 100 m, and the club's own answer wins anyway.
        var byDepth = await PreviewAsync(trip,
            "Data si ora,Adancime,Speologi\r\n12.09.2026 09:00,110,Ion Popescu\r\n");
        byDepth.GetProperty("rows").EnumerateArray().Single()
            .GetProperty("stationName").GetString().ShouldBe("deep.3");
    }

    [Fact]
    public async Task A_row_about_the_future_is_refused_in_the_preview_and_not_first_at_the_commit()
    {
        var trip = await ArmedTripAsync();
        var future = DateTimeOffset.UtcNow.AddDays(2).ToString("dd.MM.yyyy HH:mm");
        var sheet = $"Data si ora,Adancime,Speologi,Stare\r\n{future},0,Ion Popescu,intrare\r\n"
            + "12.09.2026 08:15,0,Maria Pop,intrare\r\n";

        // The preview says it, so the reviewer learns of a mistyped year before anything is
        // written, and the commit agrees with the preview about what will land.
        var preview = await PreviewAsync(trip, sheet);
        preview.GetProperty("creates").GetInt32().ShouldBe(1);
        preview.GetProperty("refused").EnumerateArray()
            .Select(d => d.GetProperty("problem").GetString()).ShouldContain("MomentInFuture");

        var commit = await CommitAsync(trip, sheet);
        commit.GetProperty("created").GetInt32().ShouldBe(1);
        commit.GetProperty("refused").EnumerateArray()
            .Select(d => d.GetProperty("problem").GetString()).ShouldContain("MomentInFuture");
        (await EventCountAsync(trip)).ShouldBe(1);
    }

    [Fact]
    public async Task Two_rows_of_one_sheet_that_are_the_same_report_preview_as_one_and_commit_as_one()
    {
        var trip = await ArmedTripAsync();
        var sheet = "Data si ora,Adancime,Speologi,Nota\r\n"
            + "12.09.2026 08:20,50,Maria Pop,prima\r\n"
            + "12.09.2026 08:20,50,Maria Pop,a doua\r\n";

        // One report, the last row's, and both rows told about it — never one create and one
        // "already recorded" refusal for a row the log had not held when the sheet was previewed.
        var preview = await PreviewAsync(trip, sheet);
        preview.GetProperty("creates").GetInt32().ShouldBe(1);
        preview.GetProperty("replaces").GetInt32().ShouldBe(0);
        preview.GetProperty("rows").EnumerateArray().Single().GetProperty("note").GetString().ShouldBe("a doua");
        preview.GetProperty("refused").EnumerateArray()
            .Select(d => d.GetProperty("problem").GetString()).ShouldBe(["DuplicateInFile"]);

        var commit = await CommitAsync(trip, sheet);
        commit.GetProperty("created").GetInt32().ShouldBe(1);
        commit.GetProperty("skipped").GetInt32().ShouldBe(0);
        commit.GetProperty("refused").EnumerateArray()
            .Select(d => d.GetProperty("problem").GetString()).ShouldNotContain("AlreadyRecorded");
        (await EventCountAsync(trip)).ShouldBe(1);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.TripPositionEvents.SingleAsync(e => e.TripLogId == trip)).Note.ShouldBe("a doua");
    }

    [Fact]
    public async Task A_station_the_survey_does_not_have_is_refused_exactly_as_a_typed_one_is()
    {
        var trip = await ArmedTripAsync();

        var preview = await PreviewAsync(trip,
            "Data si ora,Statie,Speologi\r\n12.09.2026 09:00,nowhere.9,Ion Popescu\r\n");

        preview.GetProperty("creates").GetInt32().ShouldBe(0);
        preview.GetProperty("refused").EnumerateArray()
            .Select(d => d.GetProperty("problem").GetString()).ShouldContain("StationNotInModel");
    }

    [Fact]
    public async Task The_sample_template_is_a_sheet_this_installation_reads_without_a_word_of_configuration()
    {
        // The loop worth closing: the file offered as an example is fed straight back in. A
        // template whose own headers the detector does not recognise teaches the wrong layout.
        var trip = await ArmedTripAsync();

        var download = await owner.GetAsync("/api/v1/tracking-csv-import/template");
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        // Named, because the client cannot open this route as a link — it is authenticated, so the
        // bytes are fetched with the caller's token and saved by the browser under whatever name
        // this header gives. Without one the reviewer gets a file called "export" with no
        // extension, which a spreadsheet will not open by being double-clicked.
        // Trimmed of quotes before comparing: whether a header value is quoted is an HTTP
        // detail the framework decides, and the claim here is the name the reviewer's
        // spreadsheet will see.
        download.Content.Headers.ContentDisposition!.FileName!.Trim('"')
            .ShouldBe("tracking-reports-sample.csv");
        var template = await download.Content.ReadAsStringAsync();

        var preview = await PreviewAsync(trip, template);
        preview.GetProperty("unmappedColumns").EnumerateArray().ShouldBeEmpty();
        preview.GetProperty("rowsRead").GetInt32().ShouldBe(7);
        preview.GetProperty("fileDiagnostics").EnumerateArray().ShouldBeEmpty();

        // Its two named people are the two this trip has, so every row about a place resolves and
        // only the sample's own invented station and place do not.
        preview.GetProperty("unmatchedCavers").EnumerateArray().ShouldBeEmpty();

        // Every one of the template's eight headers is read as the field it teaches — not merely
        // "none unmapped", which a header claimed by the wrong field would satisfy just as well.
        preview.GetProperty("resolvedColumns").EnumerateObject()
            .Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal)
            .ShouldBe(["Cavers", "Depth", "Note", "Place", "RecordedAt", "State", "Station", "Team"]);

        // And its rows import. Two people going in (line 2) and out (line 8), the two at 96 m twice
        // (lines 3 and 7) and one at 150 m (line 5) on the seeded survey's nearest stations: nine
        // reports, while exactly the sample's two invented places are refused with their reasons.
        // Asserted on what would land rather than implied by the headers, because a template whose
        // depth rows all came back without a station, or whose state words stopped being
        // recognised, passed the assertions above unnoticed.
        preview.GetProperty("creates").GetInt32().ShouldBe(9);
        preview.GetProperty("refused").EnumerateArray()
            .Select(d => (d.GetProperty("problem").GetString(), d.GetProperty("line").GetInt32(),
                d.GetProperty("detail").GetString()))
            .ShouldBe([("PlaceLabelUnknown", 4, "Meandru"), ("StationNotInModel", 6, "3.14")]);
        var kinds = preview.GetProperty("rows").EnumerateArray()
            .GroupBy(r => r.GetProperty("line").GetInt32())
            .ToDictionary(g => g.Key, g => g.Select(r => r.GetProperty("kind").GetString()).Distinct().Single());
        kinds[2].ShouldBe("entered");
        kinds[3].ShouldBe("atDepth");
        kinds[8].ShouldBe("exited");
    }

    [Fact]
    public async Task A_reading_choice_the_preview_refuses_is_refused_by_the_commit_too()
    {
        // The two read the sheet under the same choices or the reviewer commits something they
        // never saw: the reader drops a delimiter that is not one character and reads with the
        // default, so a commit that accepted ";;" after the preview had refused it wrote a file
        // previewed under no such choice.
        var trip = await ArmedTripAsync();
        var body = new { text = Sheet, options = new { delimiter = ";;" } };

        (await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/csv-import/preview", body))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/csv-import/commit", body))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await EventCountAsync(trip)).ShouldBe(0);

        // A second rule from the same set, so what is shared is the whole set and not one line.
        var words = new { text = Sheet, options = new { wentInWords = new[] { new string('w', 101) } } };
        (await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/csv-import/commit", words))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await EventCountAsync(trip)).ShouldBe(0);
    }

    [Fact]
    public async Task Importing_needs_what_recording_needs()
    {
        var trip = await ArmedTripAsync();
        var body = new { text = Sheet };

        var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/csv-import/preview", body))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // A reader may read the trip; what is refused is writing to its log, so this is forbidden
        // rather than not-found.
        (await reader.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/csv-import/preview", body))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/csv-import/commit", body))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await EventCountAsync(trip)).ShouldBe(0);
    }

    [Fact]
    public async Task A_sheet_is_refused_onto_a_watch_that_was_never_armed()
    {
        var (trip, _) = await CreateTripAsync();

        var refused = await owner.PostAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/tracking/csv-import/commit", new { text = Sheet });

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BodyAsync(refused)).GetProperty("code").GetString().ShouldBe("tracking.not_configured");
    }

    /// <summary>
    /// An import passes the write gate a typed report passes: a watch that is over still takes a
    /// sheet, because writing a trip up from notes afterwards is what the import is for, and a
    /// watch that is off refuses it with the one code every write to the log is refused with.
    /// </summary>
    /// <remarks>
    /// The off watch here holds a row, so the refusal is the state being refused rather than the
    /// "no watch at all" answer an empty trip gets. No request moves a watch into Off, so the state
    /// is written directly; what is under test is the gate, not how a trip comes to be there.
    /// </remarks>
    [Fact]
    public async Task A_closed_watch_takes_a_sheet_and_an_off_one_with_a_report_on_it_is_refused_it()
    {
        var closed = await ArmedTripAsync();
        (await PutConfigAsync(closed, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var commit = await CommitAsync(closed, Sheet);
        commit.GetProperty("created").GetInt32().ShouldBe(6);
        (await EventCountAsync(closed)).ShouldBe(6);

        var (off, cavers) = await CreateTripAsync();
        var (model, _) = await SeedModelAsync();
        (await ArmAsync(off, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var typed = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{off}/tracking/events", new
        {
            caverIds = new[] { cavers[0] },
            kind = "entered",
            recordedAt = new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero),
        });
        typed.StatusCode.ShouldBe(HttpStatusCode.OK, await typed.Content.ReadAsStringAsync());
        await SetStateAsync(off, TripTrackingState.Off);

        var refused = await owner.PostAsJsonAsync(
            $"/api/v1/trip-logs/{off}/tracking/csv-import/commit", new { text = Sheet });
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync());
        (await BodyAsync(refused)).GetProperty("code").GetString().ShouldBe("tracking.not_writable");
        (await EventCountAsync(off)).ShouldBe(1);
    }

    [Fact]
    public async Task Only_the_lines_a_reviewer_names_are_committed()
    {
        var trip = await ArmedTripAsync();

        // A reviewer commits what they read, and naming the lines is what makes that exact.
        var commit = await owner.PostAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/tracking/csv-import/commit",
            new { text = Sheet, lines = new[] { 2 } });
        commit.StatusCode.ShouldBe(HttpStatusCode.OK, await commit.Content.ReadAsStringAsync());

        var body = await BodyAsync(commit);
        body.GetProperty("created").GetInt32().ShouldBe(2);
        body.GetProperty("skipped").GetInt32().ShouldBe(4);
        (await EventCountAsync(trip)).ShouldBe(2);
    }

    [Fact]
    public async Task A_selection_that_names_no_line_commits_nothing_and_a_missing_one_commits_everything()
    {
        var trip = await ArmedTripAsync();

        // "None" and "nothing said" used to be read alike, so a reviewer who unticked every row
        // and pressed the button anyway imported the whole sheet. An empty list is a decision.
        var none = await owner.PostAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/tracking/csv-import/commit",
            new { text = Sheet, lines = Array.Empty<int>() });
        none.StatusCode.ShouldBe(HttpStatusCode.OK, await none.Content.ReadAsStringAsync());

        var body = await BodyAsync(none);
        body.GetProperty("created").GetInt32().ShouldBe(0);
        body.GetProperty("updated").GetInt32().ShouldBe(0);
        body.GetProperty("skipped").GetInt32().ShouldBe(6);
        (await EventCountAsync(trip)).ShouldBe(0);

        // Saying nothing about the lines is still every importable row.
        var all = await owner.PostAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/tracking/csv-import/commit",
            new { text = Sheet });
        all.StatusCode.ShouldBe(HttpStatusCode.OK, await all.Content.ReadAsStringAsync());
        (await BodyAsync(all)).GetProperty("created").GetInt32().ShouldBe(6);
        (await EventCountAsync(trip)).ShouldBe(6);
    }

    [Fact]
    public async Task A_log_holding_two_reports_at_one_instant_refuses_that_row_and_still_imports_the_rest()
    {
        // Nothing keeps a log from holding two reports about one person at one minute: a typed
        // "entered" and a typed note both filed at 08:15 is an ordinary write-up. Every commit on
        // such a trip used to fail whole, with a generic error and nothing naming the rows, while
        // the preview kept promising creates.
        var (trip, cavers) = await CreateTripAsync();
        var (model, _) = await SeedModelAsync();
        (await ArmAsync(trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var at = new DateTimeOffset(2026, 9, 12, 8, 15, 0, TimeSpan.Zero);
        foreach (var kind in new[] { "entered", "note" })
        {
            var typed = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", new
            {
                caverIds = new[] { cavers[0] },
                kind,
                note = kind == "note" ? "typed twice at one minute" : null,
                recordedAt = at,
            });
            typed.StatusCode.ShouldBe(HttpStatusCode.OK, await typed.Content.ReadAsStringAsync());
        }

        (await EventCountAsync(trip)).ShouldBe(2);
        var typedRows = await IdsAtAsync(trip, cavers[0], at);
        typedRows.Count.ShouldBe(2);

        // The preview names the collision on the row, in Ion's name, and still plans Maria's
        // report from the same row and every report from the other rows.
        var preview = await PreviewAsync(trip, Sheet);
        var refused = preview.GetProperty("refused").EnumerateArray().Single();
        refused.GetProperty("problem").GetString().ShouldBe("AlreadyRecordedSeveralTimes");
        refused.GetProperty("line").GetInt32().ShouldBe(2);
        refused.GetProperty("detail").GetString().ShouldBe("Ion Popescu");
        preview.GetProperty("creates").GetInt32().ShouldBe(5);

        var commit = await CommitAsync(trip, Sheet, replaceExisting: true);
        commit.GetProperty("created").GetInt32().ShouldBe(5);
        commit.GetProperty("updated").GetInt32().ShouldBe(0);
        commit.GetProperty("refused").EnumerateArray()
            .Select(d => d.GetProperty("problem").GetString())
            .ShouldBe(["AlreadyRecordedSeveralTimes"]);

        // Both typed reports are still there, untouched — the same two rows, so the importer
        // chose neither and rewrote neither. Counted for Ion alone: the refused row also names
        // Maria, and her report at that same instant is one of the five creates, so a count of
        // everything at 08:15 is three and says nothing about whether Ion's rows survived.
        (await EventCountAsync(trip)).ShouldBe(7);
        (await IdsAtAsync(trip, cavers[0], at)).ShouldBe(typedRows);
        (await IdsAtAsync(trip, cavers[1], at)).Count.ShouldBe(1);
    }

    /// <summary>The ids of one person's reports at one instant, in a stable order.</summary>
    private async Task<List<Guid>> IdsAtAsync(Guid trip, Guid caver, DateTimeOffset at)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripPositionEvents
            .Where(e => e.TripLogId == trip && e.CaverId == caver && e.RecordedAt == at)
            .OrderBy(e => e.Id)
            .Select(e => e.Id)
            .ToListAsync();
    }

    [Fact]
    public async Task A_person_with_two_jobs_on_the_trip_is_imported_and_not_refused_as_two_people()
    {
        // The roster holds one row per person per job, so somebody who proposed the trip and went
        // on it is two rows. That is the person a sheet is most likely to name, and they used to be
        // refused as ambiguous with a message naming them against themselves.
        var (trip, _) = await CreateTripAsync(proposers: ["Ion Popescu"]);
        var (model, _) = await SeedModelAsync();
        (await ArmAsync(trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var preview = await PreviewAsync(trip, Sheet);
        preview.GetProperty("refused").EnumerateArray().ShouldBeEmpty();
        preview.GetProperty("creates").GetInt32().ShouldBe(6);

        (await CommitAsync(trip, Sheet)).GetProperty("created").GetInt32().ShouldBe(6);
    }

    [Fact]
    public async Task A_note_too_long_for_a_report_costs_that_row_and_not_the_sheet()
    {
        // The typed route refuses a note past the bound; the imported one used to preview as fine
        // and then fail the whole file's write on the stored column's length.
        var trip = await ArmedTripAsync();
        var sheet = Sheet + $"12.09.2026 12:00,50,,,Ion Popescu,,{new string('n', 2001)},\r\n";

        var preview = await PreviewAsync(trip, sheet);
        var refused = preview.GetProperty("refused").EnumerateArray().Single();
        refused.GetProperty("problem").GetString().ShouldBe("NoteTooLong");
        refused.GetProperty("line").GetInt32().ShouldBe(6);
        preview.GetProperty("creates").GetInt32().ShouldBe(6);

        var commit = await CommitAsync(trip, sheet);
        commit.GetProperty("created").GetInt32().ShouldBe(6);
        (await EventCountAsync(trip)).ShouldBe(6);
    }

    // ---- plumbing --------------------------------------------------------------------------

    private async Task<JsonElement> PreviewAsync(Guid trip, string text)
    {
        var response = await owner.PostAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/tracking/csv-import/preview", new { text });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await BodyAsync(response);
    }

    private async Task<JsonElement> CommitAsync(Guid trip, string text, bool replaceExisting = false)
    {
        var response = await owner.PostAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/tracking/csv-import/commit", new { text, replaceExisting });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await BodyAsync(response);
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<Guid> ArmedTripAsync() => (await ArmedTripWithCaveAsync()).Trip;

    private async Task<(Guid Trip, Guid Cave)> ArmedTripWithCaveAsync()
    {
        var (trip, _) = await CreateTripAsync();
        var (model, cave) = await SeedModelAsync();
        var armed = await ArmAsync(trip, model);
        armed.StatusCode.ShouldBe(HttpStatusCode.OK, await armed.Content.ReadAsStringAsync());
        return (trip, cave);
    }

    /// <summary>
    /// Arms the watch on a model, the way the page does — reading the trip's current version first.
    /// </summary>
    private Task<HttpResponseMessage> ArmAsync(Guid trip, Guid model) =>
        PutConfigAsync(trip, new { state = "armed", surveyModelId = model });

    /// <summary>
    /// Writes the watch's configuration the way the page does — reading the trip's current version
    /// first.
    /// </summary>
    /// <remarks>
    /// The configuration write is guarded by an <c>If-Match</c> against the trip, so a request
    /// without one is refused before any of this feature is reached. Read from the tracking read's
    /// own ETag rather than from the trip's, because that is the version the route compares.
    /// </remarks>
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

    /// <summary>
    /// Writes a watch's state directly, for the one state no request moves a watch into. A plain
    /// recorded fact with nothing derived from it, which is what makes writing it this way faithful.
    /// </summary>
    private async Task SetStateAsync(Guid trip, TripTrackingState state)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var tracking = await db.TripTrackings.SingleAsync(t => t.TripLogId == trip);
        tracking.State = state;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A trip with Ion and Maria on it. A proposer named here must be one of the two, so that the
    /// same person holds two roster rows — which is what the one test that names one is about.
    /// </summary>
    private async Task<(Guid Trip, List<Guid> Cavers)> CreateTripAsync(string[]? proposers = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Csv import {Guid.NewGuid():N}"[..28],
            tripDate = "2026-09-12",
            participants = new[]
            {
                new { newCaverName = "Ion Popescu" },
                new { newCaverName = "Maria Pop" },
            },
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var trip = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        // In the order the participants were written, so cavers[0] is Ion.
        var cavers = await db.TripLogParticipants.Where(p => p.TripLogId == trip)
            .Join(db.Cavers, p => p.CaverId, c => c.Id, (p, c) => new { c.Id, c.FullName })
            .Distinct()
            .OrderByDescending(c => c.FullName == "Ion Popescu")
            .Select(c => c.Id)
            .ToListAsync();

        if (proposers is { Length: > 0 })
        {
            // The roster is written whole, so the participants are sent back with the proposers.
            var names = new Dictionary<string, Guid>();
            foreach (var c in await db.Cavers.Where(c => cavers.Contains(c.Id)).ToListAsync())
            {
                names[c.FullName] = c.Id;
            }

            var updated = await owner.PutWithIfMatchAsync($"/api/v1/trip-logs/{trip}", new
            {
                title = $"Csv import {Guid.NewGuid():N}"[..28],
                tripDate = "2026-09-12",
                participants = cavers.Select(id => new { caverId = id }).ToArray(),
                proposers = proposers.Select(name => new { caverId = names[name] }).ToArray(),
                visibility = "authenticated",
            });
            updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());

            (await db.TripLogParticipants.CountAsync(p => p.TripLogId == trip))
                .ShouldBe(cavers.Count + proposers.Length);
        }

        return (trip, cavers);
    }

    /// <summary>
    /// A survey model uploaded the real way, with the station rows a reading of such a file would
    /// have produced seeded into the graph tables. An entrance at 350 m and stations at 50, 100
    /// and 120 m below it, so a depth has something to resolve to and a declaration has something
    /// to disagree with.
    /// </summary>
    private async Task<(Guid Model, Guid Cave)> SeedModelAsync()
    {
        var caveResponse = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Csv Cave {Guid.NewGuid():N}"[..28],
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
        form.Add(bytes, "file", "tracking.3d");
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var modelId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.SingleAsync(m => m.Id == modelId);
        model.Status = SurveyModelStatus.Ready;
        db.SurveyStations.AddRange(
            Station(modelId, "ent.0", "ent", 350, SurveyStationFlags.Entrance),
            Station(modelId, "upper.2", "upper", 300, SurveyStationFlags.Underground),
            Station(modelId, "deep.3", "deep", 250, SurveyStationFlags.Underground),
            Station(modelId, "deep.4", "deep", 230, SurveyStationFlags.Underground));
        await db.SaveChangesAsync();
        return (modelId, caveId);
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

    private async Task<int> EventCountAsync(Guid trip)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripPositionEvents.CountAsync(e => e.TripLogId == trip);
    }

    /// <summary>The ids of a trip's reports, in id order, so two readings compare as lists.</summary>
    private async Task<List<Guid>> EventIdsAsync(Guid trip)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripPositionEvents.AsNoTracking()
            .Where(e => e.TripLogId == trip)
            .OrderBy(e => e.Id)
            .Select(e => e.Id)
            .ToListAsync();
    }

    private async Task<List<string>> StoredNamesAsync(Guid trip)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripPositionEvents.AsNoTracking()
            .Where(e => e.TripLogId == trip && e.ViewerStationName != null)
            .OrderBy(e => e.RecordedAt).ThenBy(e => e.Id)
            .Select(e => e.ViewerStationName!)
            .ToListAsync();
    }
}
