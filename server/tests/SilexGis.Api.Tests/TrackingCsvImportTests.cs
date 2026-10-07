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
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
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

    /// <summary>
    /// A sheet kept on a zone's clocks: one row in summer, one in winter, and one that wrote its
    /// own offset. All three are entry rows, so nothing but the moment is under test.
    /// </summary>
    private const string ClockSheet =
        "Data si ora,Speologi,Stare\r\n"
        + "12.07.2026 14:05,Ion Popescu,intrare\r\n"
        + "12.01.2026 14:05,Ion Popescu,intrare\r\n"
        + "2026-07-12T14:05:00+01:00,Maria Pop,intrare\r\n";

    [Fact]
    public async Task A_sheet_read_in_a_named_zone_lands_apart_from_its_reading_as_written_by_that_zones_offset_on_each_date()
    {
        var trip = await ArmedTripAsync();

        var written = await PreviewAsync(trip, ClockSheet, options: null);
        var zoned = await PreviewAsync(trip, ClockSheet, new { timeZone = "Europe/Bucharest" });

        // The answer says which reading it is: nothing for as written, the zone by name otherwise.
        written.GetProperty("timeZone").ValueKind.ShouldBe(JsonValueKind.Null);
        zoned.GetProperty("timeZone").GetString().ShouldBe("Europe/Bucharest");

        // As written, a cell with no offset is the instant it spells, as UTC — what it always was.
        MomentOf(written, line: 2).ShouldBe(new DateTimeOffset(2026, 7, 12, 14, 5, 0, TimeSpan.Zero));
        MomentOf(written, line: 3).ShouldBe(new DateTimeOffset(2026, 1, 12, 14, 5, 0, TimeSpan.Zero));

        // In the zone, the same cells are earlier by exactly what Bucharest was ahead of UTC on
        // that date: three hours in July, two in January. One figure for the sheet would get one
        // of the two wrong.
        (MomentOf(written, line: 2) - MomentOf(zoned, line: 2)).ShouldBe(TimeSpan.FromHours(3));
        (MomentOf(written, line: 3) - MomentOf(zoned, line: 3)).ShouldBe(TimeSpan.FromHours(2));

        // The row that wrote its own offset is the same instant under either reading.
        MomentOf(zoned, line: 4).ShouldBe(new DateTimeOffset(2026, 7, 12, 13, 5, 0, TimeSpan.Zero));
        MomentOf(written, line: 4).ShouldBe(MomentOf(zoned, line: 4));

        // And the commit writes what its preview showed, not the reading as written.
        var commit = await CommitAsync(trip, ClockSheet, options: new { timeZone = "Europe/Bucharest" });
        commit.GetProperty("created").GetInt32().ShouldBe(3);
        (await StoredMomentsAsync(trip)).ShouldBe(
        [
            new DateTimeOffset(2026, 1, 12, 12, 5, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 12, 11, 5, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 12, 13, 5, 0, TimeSpan.Zero),
        ]);
    }

    [Fact]
    public async Task A_row_read_in_the_zone_corrects_the_report_typed_for_the_same_local_time()
    {
        // A coordinator in Bucharest typed that Ion went in at 14:05 by the wall clock; the page
        // sent the instant, 11:05 UTC. Their sheet says "14:05" for the same call.
        var (trip, cavers) = await CreateTripAsync();
        var (model, _) = await SeedModelAsync();
        (await ArmAsync(trip, model)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var typed = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", new
        {
            caverIds = new[] { cavers[0] },
            kind = "entered",
            recordedAt = new DateTimeOffset(2026, 9, 12, 14, 5, 0, TimeSpan.FromHours(3)),
        });
        typed.StatusCode.ShouldBe(HttpStatusCode.OK, await typed.Content.ReadAsStringAsync());
        var before = await EventIdsAsync(trip);

        const string sheet = "Data si ora,Speologi,Stare,Nota\r\n12.09.2026 14:05,Ion Popescu,intrare,de pe foaie\r\n";
        var bucharest = new { timeZone = "Europe/Bucharest" };

        // Read as written the row is another report, three hours after the typed one: this is the
        // doubling the zone is there to prevent, shown on the same sheet.
        var written = await PreviewAsync(trip, sheet, options: null);
        written.GetProperty("creates").GetInt32().ShouldBe(1);
        written.GetProperty("replaces").GetInt32().ShouldBe(0);

        // Read in the zone it is the typed report, said again.
        var zoned = await PreviewAsync(trip, sheet, bucharest);
        zoned.GetProperty("creates").GetInt32().ShouldBe(0);
        zoned.GetProperty("replaces").GetInt32().ShouldBe(1);
        zoned.GetProperty("rows")[0].GetProperty("replaces").GetBoolean().ShouldBeTrue();

        var commit = await CommitAsync(trip, sheet, replaceExisting: true, options: bucharest);
        commit.GetProperty("created").GetInt32().ShouldBe(0);
        commit.GetProperty("updated").GetInt32().ShouldBe(1);

        // One report still, the row it was, at the instant it was — now carrying the sheet's note.
        (await EventIdsAsync(trip)).ShouldBe(before);
        (await StoredMomentsAsync(trip)).ShouldBe([new DateTimeOffset(2026, 9, 12, 11, 5, 0, TimeSpan.Zero)]);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.TripPositionEvents.AsNoTracking().SingleAsync(e => e.TripLogId == trip))
            .Note.ShouldBe("de pe foaie");
    }

    [Fact]
    public async Task A_zone_this_server_does_not_know_is_refused_by_the_preview_and_by_the_commit_and_nothing_is_read_without_it()
    {
        var trip = await ArmedTripAsync();

        // A name shaped like a zone that no zone database carries; the host's own zone under the
        // name its files give it; and a name that is no zone name at all. Each would otherwise be
        // read as something else — as written, or on the server's clock — without a word.
        foreach (var name in new[] { "Mars/Olympus_Mons", "localtime", "GTB Standard Time" })
        {
            var body = new { text = ClockSheet, options = new { timeZone = name } };

            var preview = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/csv-import/preview", body);
            preview.StatusCode.ShouldBe(HttpStatusCode.BadRequest, name);
            (await BodyAsync(preview)).GetProperty("code").GetString().ShouldBe("tracking_csv.zone_unknown");

            var commit = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/csv-import/commit", body);
            commit.StatusCode.ShouldBe(HttpStatusCode.BadRequest, name);
            (await BodyAsync(commit)).GetProperty("code").GetString().ShouldBe("tracking_csv.zone_unknown");
        }

        // The bound on the name is the shared rule set's, so it too is refused by both.
        var overlong = new { text = ClockSheet, options = new { timeZone = "Europe/" + new string('a', 64) } };
        (await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/csv-import/preview", overlong))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/csv-import/commit", overlong))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await EventCountAsync(trip)).ShouldBe(0);

        // The twin: the same sheet with a zone the server does know is read, and with the choice
        // left empty it is read as written — an empty choice is the caller saying nothing.
        (await PreviewAsync(trip, ClockSheet, new { timeZone = "UTC" }))
            .GetProperty("timeZone").GetString().ShouldBe("UTC");
        (await PreviewAsync(trip, ClockSheet, new { timeZone = "" }))
            .GetProperty("timeZone").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_zone_named_by_the_old_spelling_of_a_respelled_place_is_read_and_answered_under_the_name_that_was_sent()
    {
        var trip = await ArmedTripAsync();

        // What one family of browsers hands over for a machine in Ukraine or in India, whether or
        // not this host's zone files still carry the old spelling. Read as the zone it is — three
        // hours ahead in July, five and a half all year — and answered under the name the screen
        // chose, which is the one it will format the rows with.
        var kiev = await PreviewAsync(trip, ClockSheet, new { timeZone = "Europe/Kiev" });
        kiev.GetProperty("timeZone").GetString().ShouldBe("Europe/Kiev");
        MomentOf(kiev, line: 2).ShouldBe(new DateTimeOffset(2026, 7, 12, 11, 5, 0, TimeSpan.Zero));
        MomentOf(kiev, line: 3).ShouldBe(new DateTimeOffset(2026, 1, 12, 12, 5, 0, TimeSpan.Zero));

        var calcutta = await PreviewAsync(trip, ClockSheet, new { timeZone = "Asia/Calcutta" });
        calcutta.GetProperty("timeZone").GetString().ShouldBe("Asia/Calcutta");
        MomentOf(calcutta, line: 2).ShouldBe(new DateTimeOffset(2026, 7, 12, 8, 35, 0, TimeSpan.Zero));

        // The new spelling is the same reading, and the commit takes the old one as the preview did.
        var kyiv = await PreviewAsync(trip, ClockSheet, new { timeZone = "Europe/Kyiv" });
        MomentOf(kyiv, line: 2).ShouldBe(MomentOf(kiev, line: 2));
        (await CommitAsync(trip, ClockSheet, options: new { timeZone = "Europe/Kiev" }))
            .GetProperty("created").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task A_cell_dated_at_the_edge_of_the_calendar_costs_its_own_row_and_not_the_request()
    {
        var trip = await ArmedTripAsync();

        // Read in a zone east of Greenwich, the first row's instant is before the first date there
        // is; the second wrote an offset that carries it past the last. Each is a finding on its
        // row, and the true row between them is read.
        const string sheet =
            "Data si ora,Speologi,Stare\r\n"
            + "01.01.0001 00:30,Ion Popescu,intrare\r\n"
            + "12.07.2026 14:05,Ion Popescu,intrare\r\n"
            + "9999-12-31T23:30-05:00,Maria Pop,intrare\r\n";

        var preview = await PreviewAsync(trip, sheet, new { timeZone = "Europe/Bucharest" });
        preview.GetProperty("creates").GetInt32().ShouldBe(1);
        MomentOf(preview, line: 3).ShouldBe(new DateTimeOffset(2026, 7, 12, 11, 5, 0, TimeSpan.Zero));
        preview.GetProperty("refused").EnumerateArray()
            .Select(d => (d.GetProperty("line").GetInt32(), d.GetProperty("problem").GetString()))
            .ShouldBe([(2, "MomentUnreadable"), (4, "MomentUnreadable")]);

        (await CommitAsync(trip, sheet, options: new { timeZone = "Europe/Bucharest" }))
            .GetProperty("created").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task What_is_in_the_future_and_what_is_the_same_report_twice_are_decided_on_the_instants_the_zone_gives()
    {
        var trip = await ArmedTripAsync();

        // Two clock readings an hour either side of now, as a sheet would spell them. Zones with
        // one offset all year, so the arithmetic holds on whatever day this runs.
        var now = DateTimeOffset.UtcNow;
        var ahead = now.AddHours(1).ToString("dd.MM.yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        var behind = now.AddHours(-1).ToString("dd.MM.yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        var aheadSheet = $"Data si ora,Speologi,Stare\r\n{ahead},Ion Popescu,intrare\r\n";
        var behindSheet = $"Data si ora,Speologi,Stare\r\n{behind},Ion Popescu,intrare\r\n";

        // A reading an hour ahead of UTC is in the future as written, and four and a half hours
        // ago on a clock five and a half hours ahead of UTC.
        var aheadWritten = await PreviewAsync(trip, aheadSheet, options: null);
        aheadWritten.GetProperty("creates").GetInt32().ShouldBe(0);
        ProblemsOf(aheadWritten.GetProperty("refused")).ShouldBe(["MomentInFuture"]);
        var aheadZoned = await PreviewAsync(trip, aheadSheet, new { timeZone = "Asia/Kolkata" });
        aheadZoned.GetProperty("creates").GetInt32().ShouldBe(1);
        aheadZoned.GetProperty("refused").EnumerateArray().ShouldBeEmpty();

        // The other way round: a reading an hour behind UTC is in the past as written, and four
        // hours ahead on a clock five hours behind UTC. The commit refuses it too.
        var behindWritten = await PreviewAsync(trip, behindSheet, options: null);
        behindWritten.GetProperty("creates").GetInt32().ShouldBe(1);
        var west = new { timeZone = "America/Bogota" };
        var behindZoned = await PreviewAsync(trip, behindSheet, west);
        behindZoned.GetProperty("creates").GetInt32().ShouldBe(0);
        ProblemsOf(behindZoned.GetProperty("refused")).ShouldBe(["MomentInFuture"]);
        var refusedCommit = await CommitAsync(trip, behindSheet, options: west);
        refusedCommit.GetProperty("created").GetInt32().ShouldBe(0);
        ProblemsOf(refusedCommit.GetProperty("refused")).ShouldBe(["MomentInFuture"]);
        (await EventCountAsync(trip)).ShouldBe(0);

        // Two rows about Ion that spell different times and are one instant once the first is
        // read in Bucharest: 14:05 there on a July day is 11:05 UTC, which the second row says
        // outright. As written they are two reports three hours apart; in the zone they are one,
        // and each row is told about the other.
        const string twice =
            "Data si ora,Speologi,Stare\r\n"
            + "12.07.2026 14:05,Ion Popescu,intrare\r\n"
            + "2026-07-12T11:05:00Z,Ion Popescu,intrare\r\n";
        var twiceWritten = await PreviewAsync(trip, twice, options: null);
        twiceWritten.GetProperty("creates").GetInt32().ShouldBe(2);
        twiceWritten.GetProperty("refused").EnumerateArray().ShouldBeEmpty();
        var twiceZoned = await PreviewAsync(trip, twice, new { timeZone = "Europe/Bucharest" });
        twiceZoned.GetProperty("creates").GetInt32().ShouldBe(1);
        ProblemsOf(twiceZoned.GetProperty("refused")).ShouldBe(["DuplicateInFile"]);
    }

    [Fact]
    public async Task A_sheet_that_keeps_the_date_and_the_time_in_two_columns_is_joined_and_imported()
    {
        var trip = await ArmedTripAsync();
        const string sheet =
            "Data,Ora,Speologi,Stare\r\n"
            + "12.09.2026,08:15,\"Ion Popescu; Maria Pop\",intrare\r\n"
            + "13.09.2026,0640,Ion Popescu,iesire\r\n"
            + ",07:00,Maria Pop,iesire\r\n";

        var preview = await PreviewAsync(trip, sheet);

        preview.GetProperty("fileDiagnostics").EnumerateArray().ShouldBeEmpty();
        preview.GetProperty("unmappedColumns").EnumerateArray().ShouldBeEmpty();
        preview.GetProperty("resolvedColumns").EnumerateObject()
            .Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal)
            .ShouldBe(["Cavers", "Date", "State", "Time"]);
        // No day was named and none was needed: the sheet says its own.
        preview.GetProperty("day").ValueKind.ShouldBe(JsonValueKind.Null);
        preview.GetProperty("creates").GetInt32().ShouldBe(3);
        MomentOf(preview, line: 3).ShouldBe(new DateTimeOffset(2026, 9, 13, 6, 40, 0, TimeSpan.Zero));
        // The row that left its date blank is refused on the row; the day above is not carried down.
        preview.GetProperty("refused").EnumerateArray()
            .Select(d => (d.GetProperty("problem").GetString(), d.GetProperty("line").GetInt32(),
                d.GetProperty("column").GetString()))
            .ShouldBe([("MomentWithoutDate", 4, "Data + Ora")]);

        // Joined cells meet the sheet's zone exactly as one cell does.
        var zoned = await PreviewAsync(trip, sheet, new { timeZone = "Europe/Bucharest" });
        MomentOf(zoned, line: 3).ShouldBe(new DateTimeOffset(2026, 9, 13, 3, 40, 0, TimeSpan.Zero));

        var commit = await CommitAsync(trip, sheet);
        commit.GetProperty("created").GetInt32().ShouldBe(3);
        (await StoredMomentsAsync(trip)).ShouldBe(
        [
            new DateTimeOffset(2026, 9, 12, 8, 15, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 12, 8, 15, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 13, 6, 40, 0, TimeSpan.Zero),
        ]);
    }

    /// <summary>
    /// A sheet of times with no dates that runs past midnight: Ion in at 22:10, out at 23:50, and
    /// a row at half past midnight written on the same page.
    /// </summary>
    private const string NightSheet =
        "Ora,Speologi,Stare\r\n"
        + "22:10,Ion Popescu,intrare\r\n"
        + "23:50,Ion Popescu,iesire\r\n"
        + "00:30,Ion Popescu,intrare\r\n";

    [Fact]
    public async Task A_sheet_of_times_is_refused_until_its_day_is_named_and_then_lands_on_that_day()
    {
        var trip = await ArmedTripAsync();

        // Without a day: refused once for the file, with the reason and the column, and no row is
        // read — by the preview and by a commit sent regardless.
        var refused = await PreviewAsync(trip, NightSheet);
        refused.GetProperty("rowsRead").GetInt32().ShouldBe(0);
        refused.GetProperty("creates").GetInt32().ShouldBe(0);
        refused.GetProperty("fileDiagnostics").EnumerateArray()
            .Select(d => (d.GetProperty("severity").GetString(), d.GetProperty("problem").GetString(),
                d.GetProperty("column").GetString()))
            .ShouldBe([("Error", "TimeColumnNeedsADay", "Ora")]);
        refused.GetProperty("resolvedColumns").GetProperty("Time").GetString().ShouldBe("Ora");
        (await CommitAsync(trip, NightSheet)).GetProperty("created").GetInt32().ShouldBe(0);
        (await EventCountAsync(trip)).ShouldBe(0);

        // With the day: every row is on it, the answer says which day was used, and the row whose
        // time falls before one Ion already has is imported there and told — not moved to the 13th.
        var named = new { day = "2026-09-12" };
        var preview = await PreviewAsync(trip, NightSheet, named);
        preview.GetProperty("fileDiagnostics").EnumerateArray().ShouldBeEmpty();
        preview.GetProperty("day").GetString().ShouldBe("2026-09-12");
        preview.GetProperty("creates").GetInt32().ShouldBe(3);
        MomentOf(preview, line: 2).ShouldBe(new DateTimeOffset(2026, 9, 12, 22, 10, 0, TimeSpan.Zero));
        MomentOf(preview, line: 4).ShouldBe(new DateTimeOffset(2026, 9, 12, 0, 30, 0, TimeSpan.Zero));
        var told = preview.GetProperty("rows").EnumerateArray()
            .Where(r => r.GetProperty("diagnostics").GetArrayLength() > 0)
            .Select(r => (r.GetProperty("line").GetInt32(),
                r.GetProperty("diagnostics")[0].GetProperty("problem").GetString(),
                r.GetProperty("diagnostics")[0].GetProperty("severity").GetString(),
                r.GetProperty("diagnostics")[0].GetProperty("detail").GetString()))
            .ToList();
        told.ShouldBe([(4, "ClockRunsBackwards", "Warning", "3")]);

        // The named day's times are on the sheet's zone like any other time without an offset.
        var zoned = await PreviewAsync(trip, NightSheet, new { day = "2026-09-12", timeZone = "Europe/Bucharest" });
        MomentOf(zoned, line: 2).ShouldBe(new DateTimeOffset(2026, 9, 12, 19, 10, 0, TimeSpan.Zero));

        var commit = await CommitAsync(trip, NightSheet, options: named);
        commit.GetProperty("created").GetInt32().ShouldBe(3);
        (await StoredMomentsAsync(trip)).ShouldBe(
        [
            new DateTimeOffset(2026, 9, 12, 0, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 12, 22, 10, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 12, 23, 50, 0, TimeSpan.Zero),
        ]);
    }

    [Fact]
    public async Task A_named_day_changes_nothing_for_a_sheet_that_writes_its_own_dates_and_a_day_off_the_calendar_is_refused()
    {
        var trip = await ArmedTripAsync();

        // The sheet every other test imports, with a day named beside it: the same answer, and
        // the answer does not claim the day was used.
        var plain = await PreviewAsync(trip, ClockSheet);
        var withADay = await PreviewAsync(trip, ClockSheet, new { day = "2020-01-01" });
        withADay.GetProperty("day").ValueKind.ShouldBe(JsonValueKind.Null);
        foreach (var line in new[] { 2, 3, 4 })
        {
            MomentOf(withADay, line).ShouldBe(MomentOf(plain, line));
        }

        // A day no sheet was kept on is refused as a reading choice, by both routes alike.
        foreach (var day in new[] { "0001-01-01", "9999-12-31" })
        {
            var body = new { text = NightSheet, options = new { day } };
            (await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/csv-import/preview", body))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/csv-import/commit", body))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        (await EventCountAsync(trip)).ShouldBe(0);
    }

    [Fact]
    public async Task The_column_roles_published_for_a_mapping_screen_include_the_date_and_the_time_kept_apart()
    {
        var response = await owner.GetAsync("/api/v1/tracking-csv-import/fields");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var listed = (await BodyAsync(response)).EnumerateArray().ToList();
        var fields = listed.ToDictionary(
            f => f.GetProperty("field").GetString()!,
            f => f.GetProperty("candidates").EnumerateArray().Select(c => c.GetString()!).ToList());

        // Three roles for the moment, listed together and first, each under spellings of its own:
        // a header one of them is detected under is never one another would have claimed.
        listed.Take(3).Select(f => f.GetProperty("field").GetString())
            .ShouldBe(["RecordedAt", "Date", "Time"]);
        fields["Date"].ShouldBe(["data", "date"]);
        fields["Time"].ShouldBe(["ora", "timp", "time"]);
        fields["RecordedAt"].ShouldContain("data si ora");
        fields["RecordedAt"].Intersect(fields["Date"].Concat(fields["Time"])).ShouldBeEmpty();

        (await factory.CreateClient().GetAsync("/api/v1/tracking-csv-import/fields"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
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

    private Task<JsonElement> PreviewAsync(Guid trip, string text) => PreviewAsync(trip, text, options: null);

    /// <summary>A preview under reading choices; null sends none, which is how a sheet was always read.</summary>
    private async Task<JsonElement> PreviewAsync(Guid trip, string text, object? options)
    {
        var response = await owner.PostAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/tracking/csv-import/preview", new { text, options });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await BodyAsync(response);
    }

    private async Task<JsonElement> CommitAsync(
        Guid trip, string text, bool replaceExisting = false, object? options = null)
    {
        var response = await owner.PostAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/tracking/csv-import/commit", new { text, options, replaceExisting });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await BodyAsync(response);
    }

    /// <summary>The instant the preview gives the one report read from a physical line.</summary>
    private static DateTimeOffset MomentOf(JsonElement preview, int line) =>
        preview.GetProperty("rows").EnumerateArray()
            .Single(r => r.GetProperty("line").GetInt32() == line)
            .GetProperty("recordedAt").GetDateTimeOffset();

    private static List<string> ProblemsOf(JsonElement diagnostics) =>
        [.. diagnostics.EnumerateArray().Select(d => d.GetProperty("problem").GetString()!)];

    private async Task<List<DateTimeOffset>> StoredMomentsAsync(Guid trip)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripPositionEvents.AsNoTracking()
            .Where(e => e.TripLogId == trip)
            .OrderBy(e => e.RecordedAt)
            .Select(e => e.RecordedAt)
            .ToListAsync();
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
