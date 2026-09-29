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

        // The same instants and the same people, with one note written properly. Person and
        // instant are the key, so this is the same six reports said again.
        var corrected = Sheet.Replace("apa mare", "apa mare in meandru");

        var second = await CommitAsync(trip, corrected, replaceExisting: true);
        second.GetProperty("created").GetInt32().ShouldBe(0);
        second.GetProperty("updated").GetInt32().ShouldBe(6);
        (await EventCountAsync(trip)).ShouldBe(6);

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
    /// <remarks>
    /// The configuration write is guarded by an <c>If-Match</c> against the trip, so a request
    /// without one is refused before any of this feature is reached. Read from the tracking read's
    /// own ETag rather than from the trip's, because that is the version the route compares.
    /// </remarks>
    private async Task<HttpResponseMessage> ArmAsync(Guid trip, Guid model)
    {
        var current = await owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking");
        current.StatusCode.ShouldBe(HttpStatusCode.OK, await current.Content.ReadAsStringAsync());
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/trip-logs/{trip}/tracking")
        {
            Content = JsonContent.Create(new { state = "armed", surveyModelId = model }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", current.Headers.ETag!.ToString());
        return await owner.SendAsync(request);
    }

    private async Task<(Guid Trip, List<Guid> Cavers)> CreateTripAsync()
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
        var cavers = await db.TripLogParticipants.Where(p => p.TripLogId == trip)
            .Select(p => p.CaverId).Distinct().ToListAsync();
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
