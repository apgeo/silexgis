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
/// What a cave has declared its depths mean, read and written through the cave's own rights.
/// </summary>
public sealed class CaveDepthPlaceTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private HttpClient colleague = null!;
    private HttpClient anonymous = null!;
    private long caveTypeId;

    public CaveDepthPlaceTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(AppContext.BaseDirectory, "test-data", $"depthplace-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        }, JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"dp-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"dp-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"dp-read-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"dp-read-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"dp-col-{suffix}@t.local");
        colleague = await AuthHelper.BearerClientAsync(factory, $"dp-col-{suffix}@t.local");
        anonymous = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        try { Directory.Delete(filesRoot, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task A_cave_declares_its_depths_and_reads_them_back_shallowest_first()
    {
        var cave = await CaveAsync();

        // Written out of order deliberately: the order they come back in is the order a chooser
        // offers them, and somebody picking where a party is thinks downwards from the entrance.
        await DeclareAsync(cave, 210m, "cave.lower.7", "Bivouac");
        await DeclareAsync(cave, 0m, "cave.entrance.0", "Entrance");
        await DeclareAsync(cave, 45m, "cave.upper.12", "Camp one");

        var rows = await ListAsync(owner, cave);
        rows.Select(r => r.GetProperty("depthM").GetDecimal()).ShouldBe([0m, 45m, 210m]);
        rows[1].GetProperty("stationName").GetString().ShouldBe("cave.upper.12");
        rows[1].GetProperty("placeLabel").GetString().ShouldBe("Camp one");
    }

    [Fact]
    public async Task Declaring_the_same_depth_twice_corrects_it_rather_than_answering_it_twice()
    {
        var cave = await CaveAsync();
        await DeclareAsync(cave, 45m, "cave.upper.12", "Camp one");
        await DeclareAsync(cave, 45m, "cave.upper.13", "Camp two");

        // The depth is the key, so changing one's mind is a correction. A cave answering its own
        // question twice would hand a reported depth two stations with nothing to choose between.
        var rows = await ListAsync(owner, cave);
        rows.Count.ShouldBe(1);
        rows[0].GetProperty("stationName").GetString().ShouldBe("cave.upper.13");
        rows[0].GetProperty("placeLabel").GetString().ShouldBe("Camp two");
    }

    [Fact]
    public async Task A_depth_typed_with_more_decimals_than_the_cave_keeps_corrects_the_row_it_rounds_to()
    {
        var cave = await CaveAsync();
        await DeclareAsync(cave, 120m, "cave.a.1", "Sala");

        // The column holds one decimal. Looked up raw, 120.04 matched nothing beside the row at
        // 120 and the insert that followed was refused by the unique index — an unexplained
        // failure for the ordinary act of changing one's mind.
        var corrected = await owner.PutAsJsonAsync($"/api/v1/caves/{cave}/depth-places",
            new { depthM = 120.04m, stationName = "cave.a.2", placeLabel = "Sala" });
        corrected.StatusCode.ShouldBe(HttpStatusCode.OK, await corrected.Content.ReadAsStringAsync());
        // What is echoed is what was stored, not what was typed.
        (await corrected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("depthM").GetDecimal()
            .ShouldBe(120.0m);

        var rows = await ListAsync(owner, cave);
        rows.Count.ShouldBe(1);
        rows[0].GetProperty("stationName").GetString().ShouldBe("cave.a.2");
    }

    [Fact]
    public async Task A_depth_written_with_the_field_notes_sign_is_the_same_depth()
    {
        var cave = await CaveAsync();
        await DeclareAsync(cave, 120m, "cave.a.1", "Sala");

        // −120 is how field notes write 120 metres down, and every report path reads it so. A
        // declaration keyed on the signed value would let a cave answer one depth twice.
        await DeclareAsync(cave, -120m, "cave.a.2", "Sala");

        var rows = await ListAsync(owner, cave);
        rows.Count.ShouldBe(1);
        rows[0].GetProperty("depthM").GetDecimal().ShouldBe(120m);
        rows[0].GetProperty("stationName").GetString().ShouldBe("cave.a.2");
    }

    [Fact]
    public async Task A_place_may_be_declared_without_a_word_for_it()
    {
        var cave = await CaveAsync();
        await DeclareAsync(cave, 330m, "cave.sump.1", null);
        var rows = await ListAsync(owner, cave);
        rows[0].GetProperty("placeLabel").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_declaration_with_no_station_declares_nothing_and_is_refused()
    {
        var cave = await CaveAsync();
        var refused = await owner.PutAsJsonAsync($"/api/v1/caves/{cave}/depth-places",
            new { depthM = 45m, stationName = "", placeLabel = "Camp one" });
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The positive twin, so this is a rule about the station rather than a route that refuses
        // everything.
        await DeclareAsync(cave, 45m, "cave.upper.12", "Camp one");
    }

    [Fact]
    public async Task A_declaration_is_withdrawn_and_the_rest_stay()
    {
        var cave = await CaveAsync();
        await DeclareAsync(cave, 45m, "cave.upper.12", "Camp one");
        await DeclareAsync(cave, 210m, "cave.lower.7", "Lower camp");

        var rows = await ListAsync(owner, cave);
        var doomed = rows.First(r => r.GetProperty("depthM").GetDecimal() == 45m)
            .GetProperty("id").GetGuid();

        (await owner.DeleteAsync($"/api/v1/caves/{cave}/depth-places/{doomed}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var left = await ListAsync(owner, cave);
        left.Select(r => r.GetProperty("depthM").GetDecimal()).ShouldBe([210m]);

        // And a second withdrawal of the same row says so rather than answering as though it worked.
        (await owner.DeleteAsync($"/api/v1/caves/{cave}/depth-places/{doomed}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reading_takes_read_on_the_cave_and_writing_takes_write()
    {
        var cave = await CaveAsync();
        await DeclareAsync(cave, 45m, "cave.upper.12", "Camp one");

        // A reader may be told what a cave's depths mean, exactly as they may be told its name:
        // these rows carry no coordinate, and a depth below an entrance places nothing on earth.
        (await reader.GetAsync($"/api/v1/caves/{cave}/depth-places")).StatusCode
            .ShouldBe(HttpStatusCode.OK);

        // Writing is the cave's own Write, and a reader who has it not is refused.
        var refused = await reader.PutAsJsonAsync($"/api/v1/caves/{cave}/depth-places",
            new { depthM = 200m, stationName = "cave.x.1", placeLabel = (string?)null });
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Nothing is readable without an account at all.
        (await anonymous.GetAsync($"/api/v1/caves/{cave}/depth-places")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);

        // And the refusal changed nothing.
        (await ListAsync(owner, cave)).Count.ShouldBe(1);
    }

    /// <summary>
    /// A cave the caller may not read answers exactly what a cave that does not exist answers.
    /// The refused cave is built, and read by somebody who may, so that what this proves is the
    /// masking — and not merely that an invented identifier is unknown, which a route with no
    /// access check at all answers just as well.
    /// </summary>
    [Fact]
    public async Task A_cave_nobody_may_see_answers_what_an_absent_one_answers()
    {
        // The positive half: a private cave with a place declared on it, which its owner reads.
        var hidden = await CaveAsync(visibility: "private");
        await DeclareAsync(hidden, 45m, "cave.upper.12", "Camp one");
        (await ListAsync(owner, hidden)).Count.ShouldBe(1);

        // A caller with no way to read the cave. Refused before the rows are looked at, so the
        // station names and the place labels — which say where in the cave things are — never
        // enter the answer; and refused in one shape with a cave that is not there, so reaching
        // for a cave learns nothing about whether it exists. The invented id is the baseline the
        // hidden one has to match.
        var invented = Guid.CreateVersion7();
        var baseline = await RefusalShapeAsync(await reader.GetAsync($"/api/v1/caves/{invented}/depth-places"));
        baseline.ShouldStartWith("404 ");
        (await RefusalShapeAsync(await reader.GetAsync($"/api/v1/caves/{hidden}/depth-places")))
            .ShouldBe(baseline);

        // Writing is masked the same way: a declaration on a cave the caller cannot see is refused
        // as it would be on one that is not there, and changes nothing.
        var refusedWrite = await reader.PutAsJsonAsync($"/api/v1/caves/{hidden}/depth-places",
            new { depthM = 200m, stationName = "cave.x.1", placeLabel = (string?)null });
        (await RefusalShapeAsync(refusedWrite)).ShouldBe(baseline);
        (await ListAsync(owner, hidden)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_declared_place_says_whether_the_caves_survey_holds_its_station()
    {
        var cave = await CaveAsync();
        await DeclareAsync(cave, 45m, "cave.upper.1", "Camp one");
        await DeclareAsync(cave, 200m, "cave.gone.9", "Old sump");

        // No survey at all: nothing can be said, and nothing is — which is not the same answer as
        // "the survey does not have it".
        (await ListAsync(owner, cave)).Select(InSurvey).ShouldBe([null, null]);

        // A survey that has been uploaded and not yet read holds no stations to compare with, and
        // answers for nothing.
        var model = await UploadSurveyAsync(cave);
        (await ListAsync(owner, cave)).Select(InSurvey).ShouldBe([null, null]);

        await ReadSurveyAsync(model);
        (await ListAsync(owner, cave)).Select(InSurvey).ShouldBe([true, false]);

        // The answer to a write says it too, to the person who has just typed the name.
        InSurvey(await WriteAsync(owner, cave, 120m, "cave.deep.3")).ShouldBe(true);
        InSurvey(await WriteAsync(owner, cave, 200m, "cave.gone.10")).ShouldBe(false);
    }

    [Fact]
    public async Task A_declared_place_is_judged_against_the_survey_marked_current_and_no_other()
    {
        var cave = await CaveAsync();
        await DeclareAsync(cave, 45m, "cave.upper.1", "Camp one");
        await DeclareAsync(cave, 200m, "cave.second.7", "Old sump");

        // The first upload takes the cave's mark by arriving, and then cannot be read. It keeps
        // the mark: the page goes on calling it the current survey.
        var unreadable = await UploadSurveyAsync(cave);
        await FailSurveyAsync(unreadable);

        // A second upload is read. It now answers for the cave's figures, standing in for the
        // marked one — and it is not the survey the page calls current, so nothing is said about
        // it here: not "there" for the station both hold, not "missing" for the other.
        var standIn = await UploadSurveyAsync(cave);
        await ReadSurveyAsync(standIn, "cave.ent.0", "cave.second.7");
        (await IsCurrentAsync(unreadable)).ShouldBeTrue();
        (await IsCurrentAsync(standIn)).ShouldBeFalse();
        (await ListAsync(owner, cave)).Select(InSurvey).ShouldBe([null, null]);
        InSurvey(await WriteAsync(owner, cave, 45m, "cave.upper.1")).ShouldBeNull();

        // Made the current one on purpose, it is judged — against its own stations.
        var made = await owner.PutAsync($"/api/v1/survey-models/{standIn}/current", null);
        made.StatusCode.ShouldBe(HttpStatusCode.OK, await made.Content.ReadAsStringAsync());
        (await ListAsync(owner, cave)).Select(InSurvey).ShouldBe([false, true]);
    }

    [Fact]
    public async Task Whether_the_survey_holds_a_station_is_not_said_to_somebody_who_may_not_place_the_cave()
    {
        var cave = await CaveAsync(locationProtected: true);
        await ReadSurveyAsync(await UploadSurveyAsync(cave));
        await DeclareAsync(cave, 45m, "cave.upper.1", "Camp one");
        await DeclareAsync(cave, 200m, "cave.gone.9", "Old sump");

        // The positive half, in the same test: the cave's owner may place it and is told both
        // answers.
        (await ListAsync(owner, cave)).Select(InSurvey).ShouldBe([true, false]);

        // A genuine Viewer with no grant at all. They may read the cave, so the declarations
        // reach them as before; they may not place it, and which names its survey does and does
        // not hold is that survey's vocabulary — so each row says nothing, exactly as it does for
        // a cave with no survey.
        var theirs = await ListAsync(reader, cave);
        theirs.Count.ShouldBe(2);
        theirs.Select(InSurvey).ShouldBe([null, null]);

        // Writing the cave is not placing it either: somebody who may change the declarations
        // and may not see the position is told nothing by the answer to their own write, whether
        // the name they typed is in the survey or not.
        InSurvey(await WriteAsync(colleague, cave, 120m, "cave.deep.3")).ShouldBeNull();
        InSurvey(await WriteAsync(colleague, cave, 130m, "cave.gone.11")).ShouldBeNull();
        (await ListAsync(colleague, cave)).Select(InSurvey).ShouldBe([null, null, null, null]);
        (await ListAsync(owner, cave)).Select(InSurvey).ShouldBe([true, true, false, false]);
    }

    // ---- a station the survey file gives no name ----------------------------------------------

    [Fact]
    public async Task A_station_the_survey_file_gives_no_name_cannot_be_declared_and_one_somebody_named_can()
    {
        var cave = await CaveAsync();
        // A Survex survey: a named station, a station the file gives no name (called by the number
        // the file wrote it at), and a station a surveyor really did name with a hash sign.
        await SeedSurveyAsync(cave, SurveyModelFormat.Survex3d, null,
            ("cave.a.1", 0), ("#1", 1), ("#5", 2));
        // And a Therion survey of the same cave, whose nameless station carries the viewer's label.
        await SeedSurveyAsync(cave, SurveyModelFormat.Lox, "cave", ("cave.a.[3]", 3), ("cave.a.2", 4));

        foreach (var (depth, station) in new[] { (10m, "#1"), (20m, "a.[3]"), (30m, "cave.a.[3]") })
        {
            var refused = await owner.PutAsJsonAsync($"/api/v1/caves/{cave}/depth-places",
                new { depthM = depth, stationName = station, placeLabel = "Sala" });
            var said = await refused.Content.ReadAsStringAsync();
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, said);
            JsonDocument.Parse(said).RootElement.GetProperty("code").GetString()
                .ShouldBe("cave_depth_place.station_nameless");
        }

        (await ListAsync(owner, cave)).ShouldBeEmpty();

        // Beside each refusal, what is still accepted: a named station of either survey, the
        // station whose name merely looks like a file number, and a name no survey of the cave
        // holds at all — which nothing here has ever judged.
        await DeclareAsync(cave, 10m, "cave.a.1", "Sala");
        await DeclareAsync(cave, 20m, "a.2", null);
        await DeclareAsync(cave, 30m, "#5", null);
        await DeclareAsync(cave, 40m, "#77", null);
        (await ListAsync(owner, cave)).Count.ShouldBe(4);
    }

    [Fact]
    public async Task The_nameless_refusal_tells_nothing_to_somebody_who_may_not_see_the_caves_surveys()
    {
        // A cave whose position is protected, with a survey holding a nameless station. Its owner
        // may see the survey; another editor may write to the cave and may not see where it is.
        var cave = await CaveAsync(locationProtected: true);
        var model = await SeedSurveyAsync(cave, SurveyModelFormat.Survex3d, null, ("cave.a.1", 0), ("#1", 1));

        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"dp-other-{suffix}@t.local");
        var other = await AuthHelper.BearerClientAsync(factory, $"dp-other-{suffix}@t.local");

        // The state the test stands on, shown rather than assumed: the survey is there for its
        // owner and is not for the other editor, who can nevertheless declare a depth.
        (await owner.GetAsync($"/api/v1/survey-models/{model}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await other.GetAsync($"/api/v1/survey-models/{model}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var writes = await other.PutAsJsonAsync($"/api/v1/caves/{cave}/depth-places",
            new { depthM = 5m, stationName = "cave.a.1", placeLabel = (string?)null });
        writes.StatusCode.ShouldBe(HttpStatusCode.OK, await writes.Content.ReadAsStringAsync());

        // To that editor a nameless station is answered like any other name: the refusal would
        // confirm that the survey has a station at that number, and how many it has.
        var unseen = await other.PutAsJsonAsync($"/api/v1/caves/{cave}/depth-places",
            new { depthM = 10m, stationName = "#1", placeLabel = (string?)null });
        unseen.StatusCode.ShouldBe(HttpStatusCode.OK, await unseen.Content.ReadAsStringAsync());

        // The owner, who may see the survey, is told.
        var seen = await owner.PutAsJsonAsync($"/api/v1/caves/{cave}/depth-places",
            new { depthM = 20m, stationName = "#1", placeLabel = (string?)null });
        seen.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await seen.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString()
            .ShouldBe("cave_depth_place.station_nameless");
    }

    // ---- helpers -----------------------------------------------------------------------------

    private static bool? InSurvey(JsonElement row) =>
        row.GetProperty("stationInSurvey").ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };

    private static async Task<JsonElement> WriteAsync(HttpClient client, Guid cave, decimal depth, string station)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/caves/{cave}/depth-places",
            new { depthM = depth, stationName = station, placeLabel = (string?)null });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    /// <summary>A survey uploaded the real way and left as the upload leaves it: not yet read.</summary>
    private async Task<Guid> UploadSurveyAsync(Guid cave)
    {
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new("application/octet-stream");
        form.Add(bytes, "file", "places.3d");
        var created = await owner.PostAsync($"/api/v1/caves/{cave}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Marks the survey as read and writes its stations straight into the station table — the job
    /// that reads a file never runs in this class, and the names here are invented.
    /// </summary>
    private async Task ReadSurveyAsync(Guid modelId, params string[] stations)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.SingleAsync(m => m.Id == modelId);
        model.Status = SurveyModelStatus.Ready;
        string[] names = stations.Length > 0 ? stations : ["cave.ent.0", "cave.upper.1", "cave.deep.3"];
        db.SurveyStations.AddRange(names.Select(name => new SurveyStation
        {
            SurveyModelId = modelId,
            Name = name,
            SurveyName = null,
            Position = new Point(new CoordinateZ(25.5, 45.5, 300)) { SRID = 4326 },
            Flags = SurveyStationFlags.Underground,
        }));
        await db.SaveChangesAsync();
    }

    /// <summary>Leaves the survey as a reading that could not be done leaves it.</summary>
    private async Task FailSurveyAsync(Guid modelId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.SingleAsync(m => m.Id == modelId);
        model.Status = SurveyModelStatus.Failed;
        await db.SaveChangesAsync();
    }

    private async Task<bool> IsCurrentAsync(Guid modelId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.SurveyModels.Where(m => m.Id == modelId).Select(m => m.IsCurrent).SingleAsync();
    }

    /// <summary>
    /// A survey model of the cave as a reading would have left it: uploaded through the door, then
    /// marked read with the given station rows written straight into the graph table.
    /// </summary>
    private async Task<Guid> SeedSurveyAsync(
        Guid cave, SurveyModelFormat format, string? rootSurveyName, params (string Name, long FileStationId)[] stations)
    {
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new("application/octet-stream");
        form.Add(bytes, "file", format == SurveyModelFormat.Lox ? "depth.lox" : "depth.3d");
        var created = await owner.PostAsync($"/api/v1/caves/{cave}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var modelId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.SingleAsync(m => m.Id == modelId);
        model.Status = SurveyModelStatus.Ready;
        model.RootSurveyName = rootSurveyName;
        db.SurveyStations.AddRange(stations.Select((s, i) => new SurveyStation
        {
            SurveyModelId = modelId,
            Name = s.Name,
            FileStationId = s.FileStationId,
            Position = new Point(new CoordinateZ(25.5, 45.5, 300 - (10 * i))) { SRID = 4326 },
            Flags = SurveyStationFlags.Underground,
        }));
        await db.SaveChangesAsync();
        return modelId;
    }

    private async Task<Guid> CaveAsync(string visibility = "authenticated", bool locationProtected = false)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Depth Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task DeclareAsync(Guid cave, decimal depth, string station, string? label)
    {
        var response = await owner.PutAsJsonAsync($"/api/v1/caves/{cave}/depth-places",
            new { depthM = depth, stationName = station, placeLabel = label });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<List<JsonElement>> ListAsync(HttpClient client, Guid cave)
    {
        var response = await client.GetAsync($"/api/v1/caves/{cave}/depth-places");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        return [.. body.EnumerateArray()];
    }

    /// <summary>
    /// A refusal as a caller can see it, with the trace identifier left out: that one differs per
    /// request by design, while every other member has to be the same whichever way the cave was
    /// out of reach.
    /// </summary>
    private static async Task<string> RefusalShapeAsync(HttpResponseMessage response)
    {
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var members = body.EnumerateObject()
            .Where(p => p.Name != "traceId")
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={p.Value}");
        return $"{(int)response.StatusCode} {string.Join('&', members)}";
    }
}
