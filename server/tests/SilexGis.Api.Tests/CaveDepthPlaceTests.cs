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
        await DeclareAsync(cave, 540m, "grind.bivuac.BV0", "Bivuac P.W.G.");
        await DeclareAsync(cave, 0m, "grind.intrare.G0", "Intrare");
        await DeclareAsync(cave, 138m, "grind.niagara.3A", "P. Niagara");

        var rows = await ListAsync(owner, cave);
        rows.Select(r => r.GetProperty("depthM").GetDecimal()).ShouldBe([0m, 138m, 540m]);
        rows[1].GetProperty("stationName").GetString().ShouldBe("grind.niagara.3A");
        rows[1].GetProperty("placeLabel").GetString().ShouldBe("P. Niagara");
    }

    [Fact]
    public async Task Declaring_the_same_depth_twice_corrects_it_rather_than_answering_it_twice()
    {
        var cave = await CaveAsync();
        await DeclareAsync(cave, 138m, "grind.niagara.3A", "P. Niagara");
        await DeclareAsync(cave, 138m, "grind.niagara.4", "Baza Niagara");

        // The depth is the key, so changing one's mind is a correction. A cave answering its own
        // question twice would hand a reported depth two stations with nothing to choose between.
        var rows = await ListAsync(owner, cave);
        rows.Count.ShouldBe(1);
        rows[0].GetProperty("stationName").GetString().ShouldBe("grind.niagara.4");
        rows[0].GetProperty("placeLabel").GetString().ShouldBe("Baza Niagara");
    }

    [Fact]
    public async Task A_place_may_be_declared_without_a_word_for_it()
    {
        var cave = await CaveAsync();
        await DeclareAsync(cave, 775m, "grind.puturi.GP42", null);
        var rows = await ListAsync(owner, cave);
        rows[0].GetProperty("placeLabel").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_declaration_with_no_station_declares_nothing_and_is_refused()
    {
        var cave = await CaveAsync();
        var refused = await owner.PutAsJsonAsync($"/api/v1/caves/{cave}/depth-places",
            new { depthM = 138m, stationName = "", placeLabel = "P. Niagara" });
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The positive twin, so this is a rule about the station rather than a route that refuses
        // everything.
        await DeclareAsync(cave, 138m, "grind.niagara.3A", "P. Niagara");
    }

    [Fact]
    public async Task A_declaration_is_withdrawn_and_the_rest_stay()
    {
        var cave = await CaveAsync();
        await DeclareAsync(cave, 138m, "grind.niagara.3A", "P. Niagara");
        await DeclareAsync(cave, 540m, "grind.bivuac.BV0", "Bivuac");

        var rows = await ListAsync(owner, cave);
        var doomed = rows.First(r => r.GetProperty("depthM").GetDecimal() == 138m)
            .GetProperty("id").GetGuid();

        (await owner.DeleteAsync($"/api/v1/caves/{cave}/depth-places/{doomed}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var left = await ListAsync(owner, cave);
        left.Select(r => r.GetProperty("depthM").GetDecimal()).ShouldBe([540m]);

        // And a second withdrawal of the same row says so rather than answering as though it worked.
        (await owner.DeleteAsync($"/api/v1/caves/{cave}/depth-places/{doomed}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reading_takes_read_on_the_cave_and_writing_takes_write()
    {
        var cave = await CaveAsync();
        await DeclareAsync(cave, 138m, "grind.niagara.3A", "P. Niagara");

        // A reader may be told what a cave's depths mean, exactly as they may be told its name:
        // these rows carry no coordinate, and a depth below an entrance places nothing on earth.
        (await reader.GetAsync($"/api/v1/caves/{cave}/depth-places")).StatusCode
            .ShouldBe(HttpStatusCode.OK);

        // Writing is the cave's own Write, and a reader who has it not is refused.
        var refused = await reader.PutAsJsonAsync($"/api/v1/caves/{cave}/depth-places",
            new { depthM = 200m, stationName = "grind.x.1", placeLabel = (string?)null });
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Nothing is readable without an account at all.
        (await anonymous.GetAsync($"/api/v1/caves/{cave}/depth-places")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);

        // And the refusal changed nothing.
        (await ListAsync(owner, cave)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_cave_nobody_may_see_answers_what_an_absent_one_answers()
    {
        // One shape for missing and refused, so reaching for a cave learns nothing about whether
        // it exists.
        var invented = Guid.CreateVersion7();
        (await owner.GetAsync($"/api/v1/caves/{invented}/depth-places")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- helpers -----------------------------------------------------------------------------

    private async Task<Guid> CaveAsync()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Depth Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
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
}
