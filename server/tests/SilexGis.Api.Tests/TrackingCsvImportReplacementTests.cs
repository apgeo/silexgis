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
/// What a sheet's row may learn from, and may take away from, a report the log already holds.
///
/// <para>
/// Two properties, both about a row that lands on a stored report. A stored report is a stored
/// position, and somebody may write a trip's log without being allowed to learn where one of its
/// caves is — so nothing an import answers may depend on whether a place tried against such a
/// report was the right one, its counts included. And a row replaces only what the sheet says: a
/// team cell that cannot be read as one team is not the sheet saying "no team".
/// </para>
/// <para>
/// The sheets are invented and deliberately small; the people in them are created by these tests.
/// </para>
/// </summary>
public sealed class TrackingCsvImportReplacementTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset At = new(2026, 9, 12, 9, 40, 0, TimeSpan.Zero);

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private long caveTypeId;

    public TrackingCsvImportReplacementTests(PostgresFixture postgres)
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
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"csvrep-own-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"csvrep-own-{suffix}@t.local");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        try { Directory.Delete(filesRoot, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// <b>A log that spans two caves, one of which the importer may not be told the place of.</b>
    /// The watch is on a survey of a cave the importer may place people in; two older reports sit
    /// at one station of another cave, which has since been protected. The importer tries a
    /// station against each — the station the report really is at, and one it is not — and the
    /// preview, the import that does not overwrite and the import that does all answer the two
    /// alike, count for count. The cave's own author, who may be told, does get two different
    /// answers for the same two rows: that is what shows the comparison can tell them apart.
    /// </summary>
    /// <remarks>
    /// Both surveys hold a station of each name, as two surveys easily do, so that the tried names
    /// are stations of the survey the watch is on and the rows are importable — a row refused for
    /// naming no station would never reach the question.
    /// </remarks>
    [Fact]
    public async Task A_station_tried_against_a_report_in_a_cave_the_importer_may_not_be_told_is_counted_the_same_right_or_wrong()
    {
        var (elsewhere, withheldCave) = await SeedModelAsync();
        var (here, _) = await SeedModelAsync();

        var guideEmail = $"csvrep-guide-{Guid.NewGuid():N}"[..22] + "@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, guideEmail);
        var guide = await AuthHelper.BearerClientAsync(factory, guideEmail);

        // The same log twice, one per account: the importer under test, and the cave's author.
        var theirs = await TripMovedBetweenCavesAsync(guide, elsewhere, here);
        var mine = await TripMovedBetweenCavesAsync(owner, elsewhere, here);

        await SetLocationProtectedAsync(withheldCave, true);

        // ---- the state itself: withheld from one, told to the other ---------------------------
        (await ListedStationsAsync(guide, theirs)).ShouldBe([null, null]);
        (await ListedStationsAsync(owner, mine)).ShouldBe(["upper.2", "upper.2"]);
        // And the importer may place people on the survey the watch is on now.
        var places = await guide.PostAsJsonAsync($"/api/v1/trip-logs/{theirs}/tracking/events", new
        {
            caverIds = new[] { await CaverAsync(theirs, "Maria Pop") },
            kind = "atStation",
            stationName = "deep.4",
            recordedAt = At.AddHours(3),
        });
        places.StatusCode.ShouldBe(HttpStatusCode.OK, await places.Content.ReadAsStringAsync());

        static string Naming(string station, string person) =>
            $"Data si ora,Statie,Speologi\r\n12.09.2026 09:40,{station},{person}\r\n";

        // ---- the preview, one stored report, the right station and a wrong one ----------------
        var right = await PreviewAsync(guide, theirs, Naming("upper.2", "Ion Popescu"));
        var wrong = await PreviewAsync(guide, theirs, Naming("deep.3", "Ion Popescu"));
        foreach (var member in new[] { "creates", "replaces", "refused", "unmatchedCavers", "fileDiagnostics" })
        {
            right.GetProperty(member).GetRawText().ShouldBe(wrong.GetProperty(member).GetRawText(), member);
        }

        right.GetProperty("replaces").GetInt32().ShouldBe(1);
        var rightRow = right.GetProperty("rows").EnumerateArray().ShouldHaveSingleItem();
        var wrongRow = wrong.GetProperty("rows").EnumerateArray().ShouldHaveSingleItem();
        // Everything on the row but the station the importer typed themselves.
        foreach (var member in rightRow.EnumerateObject().Select(m => m.Name).Where(name => name != "stationName"))
        {
            rightRow.GetProperty(member).GetRawText().ShouldBe(wrongRow.GetProperty(member).GetRawText(), member);
        }

        rightRow.GetProperty("before").GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
        rightRow.GetProperty("before").GetProperty("surveyModelId").ValueKind.ShouldBe(JsonValueKind.Null);
        right.GetRawText().ShouldNotContain(elsewhere.ToString());

        // ---- an import that does not overwrite: the right station for Ion, a wrong one for Maria
        var keptRight = await CommitAsync(guide, theirs, Naming("upper.2", "Ion Popescu"), replaceExisting: false);
        var keptWrong = await CommitAsync(guide, theirs, Naming("deep.3", "Maria Pop"), replaceExisting: false);
        Counts(keptRight).ShouldBe(Counts(keptWrong));
        ProblemsOf(keptRight).ShouldBe(ProblemsOf(keptWrong));
        Counts(keptRight).ShouldBe((Created: 0, Updated: 0, Unchanged: 0, Skipped: 1));

        // ---- and one that does: the load-bearing comparison -----------------------------------
        var wroteRight = await CommitAsync(guide, theirs, Naming("upper.2", "Ion Popescu"), replaceExisting: true);
        var wroteWrong = await CommitAsync(guide, theirs, Naming("deep.3", "Maria Pop"), replaceExisting: true);
        Counts(wroteRight).ShouldBe(Counts(wroteWrong));
        ProblemsOf(wroteRight).ShouldBe(ProblemsOf(wroteWrong));
        (Counts(wroteRight).Updated + Counts(wroteRight).Unchanged).ShouldBe(1);
        // Treated alike as well as counted alike: either both reports are still anchored to the
        // cave they were made in, or neither is.
        var anchors = await AnchorsAtAsync(theirs);
        anchors.Count.ShouldBe(2);
        anchors.Select(a => a.Cave == withheldCave).Distinct().ShouldHaveSingleItem();

        // ---- the same two rows from somebody who may be told: answered differently ------------
        // The right station is the report as it stands, so nothing is written; the wrong one is a
        // correction. This is the difference the importer above must not be shown.
        var toldRight = await CommitAsync(owner, mine, Naming("upper.2", "Ion Popescu"), replaceExisting: true);
        var toldWrong = await CommitAsync(owner, mine, Naming("deep.3", "Maria Pop"), replaceExisting: true);
        Counts(toldRight).ShouldBe((Created: 0, Updated: 0, Unchanged: 1, Skipped: 0));
        Counts(toldWrong).ShouldBe((Created: 0, Updated: 1, Unchanged: 0, Skipped: 0));
    }

    /// <summary>
    /// Two teams of one trip can be given the same title. A team cell that fits both is not "no
    /// such team": with overwriting ticked, the report it lands on keeps the team it has, and the
    /// row says why. A name no team has, on the row beside it, still clears the team as it always
    /// did, and a new report made from such a cell has no team.
    /// </summary>
    [Fact]
    public async Task A_team_cell_that_fits_two_of_the_trips_teams_leaves_the_reports_team_as_it_was_and_says_so()
    {
        var (model, _) = await SeedModelAsync();
        var trip = await CreateTripAsync(owner);
        (await PutConfigAsync(owner, trip, new { state = "armed", surveyModelId = model }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var first = await TeamAsync(trip, "Echipa 1");
        var again = await TeamAsync(trip, "Echipa 1");
        var other = await TeamAsync(trip, "Echipa 2");
        again.ShouldNotBe(first);

        var ion = await CaverAsync(trip, "Ion Popescu");
        var maria = await CaverAsync(trip, "Maria Pop");
        await TypeAsync(owner, trip, new
        {
            caverIds = new[] { ion }, kind = "atStation", stationName = "upper.2", teamId = first, recordedAt = At,
        });
        await TypeAsync(owner, trip, new
        {
            caverIds = new[] { maria }, kind = "atStation", stationName = "upper.2", teamId = other, recordedAt = At,
        });

        const string sheet =
            "Data si ora,Statie,Speologi,Echipa\r\n"
            + "12.09.2026 09:40,upper.2,Ion Popescu,Echipa 1\r\n"
            + "12.09.2026 09:40,upper.2,Maria Pop,Echipa 3\r\n"
            + "12.09.2026 10:00,upper.2,Ion Popescu,Echipa 1\r\n";

        var preview = await PreviewAsync(owner, trip, sheet);
        var commit = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/csv-import/commit", new
        {
            text = sheet,
            replaceExisting = true,
            // The plan the preview named is the plan that is written.
            planDigest = preview.GetProperty("planDigest").GetString(),
        });
        commit.StatusCode.ShouldBe(HttpStatusCode.OK, await commit.Content.ReadAsStringAsync());
        var written = await BodyAsync(commit);

        // ---- what the log holds afterwards ----------------------------------------------------
        var teams = await TeamsByKeyAsync(trip);
        // The report whose cell fits two teams keeps the team it had.
        teams[(ion, At)].ShouldBe(first);
        // A name no team has is still "no team", written over the report's.
        teams[(maria, At)].ShouldBeNull();
        // And a new report cannot be given a team nobody can decide on.
        teams[(ion, At.AddMinutes(20))].ShouldBeNull();

        // Ion's stored report says what the row says and keeps its team: not written. Maria's
        // lost its team: written. One report is new.
        Counts(written).ShouldBe((Created: 1, Updated: 1, Unchanged: 1, Skipped: 0));

        // ---- and what the reviewer was told before pressing the button ------------------------
        JsonElement RowAt(int line) => preview.GetProperty("rows").EnumerateArray()
            .Single(r => r.GetProperty("line").GetInt32() == line);
        List<string> FindingsAt(int line) =>
            [.. RowAt(line).GetProperty("diagnostics").EnumerateArray().Select(d => d.GetProperty("problem").GetString()!)];

        FindingsAt(2).ShouldBe(["TeamAmbiguous"]);
        FindingsAt(3).ShouldBe(["TeamNotOnTrip"]);
        FindingsAt(4).ShouldBe(["TeamAmbiguous"]);
        // The row is shown with the team the report will have, which is the one it has.
        RowAt(2).GetProperty("teamId").GetGuid().ShouldBe(first);
        RowAt(2).GetProperty("before").GetProperty("teamId").GetGuid().ShouldBe(first);
        RowAt(3).GetProperty("teamId").ValueKind.ShouldBe(JsonValueKind.Null);
        RowAt(3).GetProperty("before").GetProperty("teamId").GetGuid().ShouldBe(other);
        RowAt(4).GetProperty("teamId").ValueKind.ShouldBe(JsonValueKind.Null);

        // Read again, the sheet changes nothing more: the team it left alone is still left alone.
        var second = await CommitAsync(owner, trip, sheet, replaceExisting: true);
        Counts(second).ShouldBe((Created: 0, Updated: 0, Unchanged: 3, Skipped: 0));
        (await TeamsByKeyAsync(trip))[(ion, At)].ShouldBe(first);
    }

    // ---- reading answers ---------------------------------------------------------------------

    private static (int Created, int Updated, int Unchanged, int Skipped) Counts(JsonElement commit) => (
        commit.GetProperty("created").GetInt32(),
        commit.GetProperty("updated").GetInt32(),
        commit.GetProperty("unchanged").GetInt32(),
        commit.GetProperty("skipped").GetInt32());

    private static List<string> ProblemsOf(JsonElement commit) =>
        [.. commit.GetProperty("refused").EnumerateArray().Select(d => d.GetProperty("problem").GetString()!)];

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<JsonElement> PreviewAsync(HttpClient client, Guid trip, string text)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/tracking/csv-import/preview", new { text });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await BodyAsync(response);
    }

    private static async Task<JsonElement> CommitAsync(HttpClient client, Guid trip, string text, bool replaceExisting)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/tracking/csv-import/commit", new { text, replaceExisting });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await BodyAsync(response);
    }

    /// <summary>The station each report at the shared moment is listed at for this caller, null where withheld.</summary>
    private static async Task<List<string?>> ListedStationsAsync(HttpClient client, Guid trip)
    {
        var listed = await client.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events");
        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await listed.Content.ReadAsStringAsync());
        return [.. (await BodyAsync(listed)).GetProperty("items").EnumerateArray()
            .Where(e => e.GetProperty("recordedAt").GetDateTimeOffset() == At)
            .Select(e => e.GetProperty("stationName").GetString())];
    }

    /// <summary>The survey and the cave each report at the shared moment is anchored to.</summary>
    private async Task<List<(Guid? Model, Guid? Cave)>> AnchorsAtAsync(Guid trip)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var rows = await db.TripPositionEvents.AsNoTracking()
            .Where(e => e.TripLogId == trip && e.RecordedAt == At)
            .Select(e => new { e.SurveyModelId, e.CaveFeatureId })
            .ToListAsync();
        return [.. rows.Select(r => (r.SurveyModelId, r.CaveFeatureId))];
    }

    private async Task<Dictionary<(Guid Caver, DateTimeOffset At), Guid?>> TeamsByKeyAsync(Guid trip)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var rows = await db.TripPositionEvents.AsNoTracking()
            .Where(e => e.TripLogId == trip)
            .Select(e => new { e.CaverId, e.RecordedAt, e.TeamId })
            .ToListAsync();
        return rows.ToDictionary(r => (r.CaverId, r.RecordedAt), r => r.TeamId);
    }

    // ---- seeding -----------------------------------------------------------------------------

    /// <summary>
    /// A trip of this account's whose watch was on one survey when Ion and Maria were reported at
    /// "upper.2", and has since been ended and pointed at a survey of another cave.
    /// </summary>
    private async Task<Guid> TripMovedBetweenCavesAsync(HttpClient client, Guid first, Guid then)
    {
        var trip = await CreateTripAsync(client);
        (await PutConfigAsync(client, trip, new { state = "armed", surveyModelId = first }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await TypeAsync(client, trip, new
        {
            caverIds = new[] { await CaverAsync(trip, "Ion Popescu"), await CaverAsync(trip, "Maria Pop") },
            kind = "atStation",
            stationName = "upper.2",
            recordedAt = At,
        });
        // Ended and re-pointed in one write: an armed watch does not move to another cave.
        var moved = await PutConfigAsync(client, trip, new { state = "closed", surveyModelId = then });
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        return trip;
    }

    private static async Task TypeAsync(HttpClient client, Guid trip, object report)
    {
        var typed = await client.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", report);
        typed.StatusCode.ShouldBe(HttpStatusCode.OK, await typed.Content.ReadAsStringAsync());
    }

    private async Task<Guid> TeamAsync(Guid trip, string title)
    {
        var made = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/teams", new { title });
        made.StatusCode.ShouldBe(HttpStatusCode.OK, await made.Content.ReadAsStringAsync());
        return (await BodyAsync(made)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CaverAsync(Guid trip, string name)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripLogParticipants.Where(p => p.TripLogId == trip)
            .Join(db.Cavers, p => p.CaverId, c => c.Id, (p, c) => new { c.Id, c.FullName })
            .Where(c => c.FullName == name)
            .Select(c => c.Id)
            .Distinct()
            .SingleAsync();
    }

    /// <summary>A trip with Ion and Maria on it.</summary>
    private static async Task<Guid> CreateTripAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Csv replace {Guid.NewGuid():N}"[..28],
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
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Writes the watch's configuration the way the page does — reading the trip's current
    /// version first, because the write is guarded by an <c>If-Match</c> against it.
    /// </summary>
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

    private async Task SetLocationProtectedAsync(Guid caveFeatureId, bool value)
    {
        using var scope = factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
        await writer.SetLocationProtectedAsync(caveFeatureId, value);
        await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().SaveChangesAsync();
    }

    /// <summary>
    /// A cave of the first account's with a survey model uploaded the real way, and the station
    /// rows a reading of such a file would have produced seeded into the graph tables.
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
}
