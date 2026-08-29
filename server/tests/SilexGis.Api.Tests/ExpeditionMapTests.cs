// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a camp draws on a map, and — the point of the endpoint existing at all — what it refuses to
/// draw. Three sources of geometry meet on one canvas and are governed differently: the camp's own
/// working area by the camp's visibility, the member trips' sketches by each trip's, and the
/// entrances of the caves those trips name by the exact-location rule. The last of those is why the
/// answer is assembled on the server: a page handed the trips and left to fetch positions for the
/// caves they name would be deciding, on the client, what a camp's map may show.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ExpeditionMapTests : IAsyncLifetime, IDisposable
{
    private const double ExactLon = 25.44721;
    private const double ExactLat = 45.53127;
    private const double OpenLon = 25.51033;
    private const double OpenLat = 45.60218;

    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;

    // An Editor, and deliberately so for the protection tests: the seeded Editors group holds the
    // read over every content domain but holds ViewExactLocation over nothing, so it is exactly a
    // caller who may read a protected cave and may not place it.
    private HttpClient reader = null!;
    private Guid readerId;

    // A plain Viewer for the visibility tests: an Editor reads past visibility at the widest
    // scope, so an Editor who "cannot see" a camp proves nothing at all.
    private HttpClient outsider = null!;
    private Guid outsiderId;

    private long caveTypeId;
    private long entranceTypeId;

    public ExpeditionMapTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"xmap-own-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"xmap-read-{suffix}@t.local");
        outsiderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"xmap-out-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"xmap-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"xmap-read-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"xmap-out-{suffix}@t.local");
    }

    [Fact]
    public async Task The_map_takes_an_account()
    {
        var camp = await CreateCampAsync("Anonymous camp");
        using var anonymous = factory.CreateClient();

        (await anonymous.GetAsync($"/api/v1/expeditions/{camp}/map"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await owner.GetAsync($"/api/v1/expeditions/{camp}/map"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_camp_the_caller_may_not_read_has_no_map_and_no_different_answer()
    {
        var camp = await CreateCampAsync("Unreadable camp");

        // The Viewer holds nothing over camps, so this is genuinely a camp they may not read —
        // and the answer is the one a camp that does not exist gets, in the same words.
        var refused = await outsider.GetAsync($"/api/v1/expeditions/{camp}/map");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await refused.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("expedition.not_found");

        var missing = await owner.GetAsync($"/api/v1/expeditions/{Guid.NewGuid()}/map");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await missing.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("expedition.not_found");

        // The positive half: the very same camp draws for the person who may read it.
        (await owner.GetAsync($"/api/v1/expeditions/{camp}/map"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_map_draws_the_working_area_and_only_the_sketches_the_reader_may_see()
    {
        var camp = await CreateCampAsync("Drawn camp", withArea: true);
        var shared = await CreateTripAsync("Shared trip", withSketch: true);
        var hidden = await CreateTripAsync("Hidden trip", withSketch: true);
        await AddTripAsync(camp, shared);
        await AddTripAsync(camp, hidden);

        // The Viewer is let into the camp and into one of its trips, and into nothing else.
        await GrantReadAsync("expedition", camp, outsiderId);
        await GrantReadAsync("tripLog", shared, outsiderId);

        var mine = await FeaturesAsync(owner, camp);
        mine.Count(f => Kind(f) == "area").ShouldBe(1);
        Names(mine, "trip").ShouldBe(["Hidden trip", "Shared trip"], ignoreOrder: true);

        // The same camp, the same working area, one sketch fewer — because the trip the reader
        // was not let into is not on the camp's map either.
        var theirs = await FeaturesAsync(outsider, camp);
        theirs.Count(f => Kind(f) == "area").ShouldBe(1);
        Names(theirs, "trip").ShouldBe(["Shared trip"]);
    }

    [Fact]
    public async Task An_entrance_the_reader_may_not_place_is_left_off_the_map_until_they_may()
    {
        var camp = await CreateCampAsync("Karst camp", withArea: true);
        var open = await CreateCaveAsync("Open Cave", locationProtected: false);
        var guarded = await CreateCaveAsync("Guarded Cave", locationProtected: true);
        await AddEntranceAsync(open, OpenLon, OpenLat);
        await AddEntranceAsync(guarded, ExactLon, ExactLat);

        var trip = await CreateTripAsync("Survey trip", withSketch: true, caveIds: [open, guarded]);
        await AddTripAsync(camp, trip);

        // The reader may read both caves — an Editor reads every content domain — and may place
        // neither by any grant of their own. Only protection separates the two answers.
        var before = await FeaturesAsync(reader, camp);
        Names(before, "entrance").ShouldBe(["Open Cave"]);
        Coordinates(before, "Open Cave")[0].ShouldBe(OpenLon, 1e-9);

        // The owner of the protected cave sees it all along, which is what proves the absence
        // above is the protection rule rather than a missing link or an empty query.
        Names(await FeaturesAsync(owner, camp), "entrance")
            .ShouldBe(["Guarded Cave", "Open Cave"], ignoreOrder: true);

        await GrantAsync("feature", guarded, readerId, "Read,ViewExactLocation");

        var after = await FeaturesAsync(reader, camp);
        Names(after, "entrance").ShouldBe(["Guarded Cave", "Open Cave"], ignoreOrder: true);
        var point = Coordinates(after, "Guarded Cave");
        point[0].ShouldBe(ExactLon, 1e-9);
        point[1].ShouldBe(ExactLat, 1e-9);
    }

    [Fact]
    public async Task A_cave_read_through_a_grant_puts_its_entrance_on_the_map_like_it_does_on_the_cave()
    {
        var camp = await CreateCampAsync("Granted camp");
        var hidden = await CreateCaveAsync("Granted Cave", locationProtected: true, visibility: "private");
        await AddEntranceAsync(hidden, ExactLon, ExactLat);
        var trip = await CreateTripAsync("Granted trip", withSketch: false, caveIds: [hidden]);
        await AddTripAsync(camp, trip);

        // Everything this caller has, they have by a grant made on the row itself: the camp, the
        // trip, and the cave. Nothing was granted on the entrance, because an entrance is not a
        // thing anybody makes a grant on — it is read through the cave that owns it.
        await GrantReadAsync("expedition", camp, outsiderId);
        await GrantReadAsync("tripLog", trip, outsiderId);
        await GrantAsync("feature", hidden, outsiderId, "Read,ViewExactLocation");

        // The cave's own page shows the entrance at its exact position...
        var page = await outsider.GetAsync($"/api/v1/caves/{hidden}/entrances");
        var pagePayload = await page.Content.ReadAsStringAsync();
        page.StatusCode.ShouldBe(HttpStatusCode.OK, pagePayload);
        JsonDocument.Parse(pagePayload).RootElement.GetArrayLength().ShouldBe(1);

        // ...so the camp's map shows the same entrance, at the same position. A map that re-asked
        // the visibility question of the entrance row would show this reader an empty plateau,
        // because a grant made on a cave is not a grant on its entrances.
        var drawn = await FeaturesAsync(outsider, camp);
        Names(drawn, "entrance").ShouldBe(["Granted Cave"]);
        var point = Coordinates(drawn, "Granted Cave");
        point[0].ShouldBe(ExactLon, 1e-9);
        point[1].ShouldBe(ExactLat, 1e-9);
    }

    // ---- helpers -------------------------------------------------------------------------

    private static string? Kind(JsonElement feature) =>
        feature.GetProperty("properties").GetProperty("kind").GetString();

    private static List<string> Names(IReadOnlyList<JsonElement> features, string kind) =>
        [.. features
            .Where(f => Kind(f) == kind)
            .Select(f => f.GetProperty("properties").GetProperty("name").GetString() ?? string.Empty)
            .Order()];

    private static double[] Coordinates(IReadOnlyList<JsonElement> features, string name) =>
        [.. features
            .First(f => f.GetProperty("properties").GetProperty("name").GetString() == name)
            .GetProperty("geometry").GetProperty("coordinates")
            .EnumerateArray()
            .Select(x => x.GetDouble())];

    private static async Task<List<JsonElement>> FeaturesAsync(HttpClient client, Guid campId)
    {
        var response = await client.GetAsync($"/api/v1/expeditions/{campId}/map");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        var body = JsonDocument.Parse(payload).RootElement;
        body.GetProperty("type").GetString().ShouldBe("FeatureCollection");
        return [.. body.GetProperty("features").EnumerateArray()];
    }

    private async Task<Guid> CreateCampAsync(string name, bool withArea = false)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/expeditions/", new
        {
            name = $"{name} {Guid.NewGuid():N}"[..40],
            description = (string?)null,
            startDate = "2026-07-01",
            endDate = "2026-07-14",
            geom = withArea
                ? new
                {
                    type = "Polygon",
                    coordinates = new[]
                    {
                        new[]
                        {
                            new[] { 25.40, 45.50 }, new[] { 25.60, 45.50 },
                            new[] { 25.60, 45.70 }, new[] { 25.40, 45.70 },
                            new[] { 25.40, 45.50 },
                        },
                    },
                }
                : (object?)null,
            cavingGroupId = (Guid?)null,
            visibility = "private",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateTripAsync(string title, bool withSketch, Guid[]? caveIds = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title,
            tripDate = "2026-07-02",
            participants = Array.Empty<object>(),
            caveIds = caveIds ?? [],
            geom = withSketch
                ? new { type = "Point", coordinates = new[] { 25.45, 45.55 } }
                : (object?)null,
            visibility = "private",
            hadIncident = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task AddTripAsync(Guid campId, Guid tripId)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/expeditions/{campId}/trips", new
        {
            tripLogId = tripId,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> CreateCaveAsync(
        string name, bool locationProtected, string visibility = "authenticated")
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task AddEntranceAsync(Guid caveId, double lon, double lat)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            positionQuality = "Gps",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private Task GrantReadAsync(string routeType, Guid entityId, Guid subjectId) =>
        GrantAsync(routeType, entityId, subjectId, "Read");

    private async Task GrantAsync(string routeType, Guid entityId, Guid subjectId, string actions)
    {
        var response = await owner.PutAsJsonAsync($"/api/v1/objects/{routeType}/{entityId}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId,
                    effect = "allow",
                    actions,
                    scopeKind = "object",
                },
            },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
