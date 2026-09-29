// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
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

    // ---- helpers -----------------------------------------------------------------------------

    private async Task<Guid> CaveAsync(string visibility = "authenticated")
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Depth Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility,
            locationProtected = false,
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
