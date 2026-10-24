// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a protected cave looks like to somebody who may read it but not place it, on every path
/// that hands a cave's position out of the application: the map, a public share link, a file
/// export, the device sync and the shared feature routes.
/// </summary>
/// <remarks>
/// <para>
/// Written as a fixed point rather than as a feature test. Every assertion here held before caves
/// carried a feature type, and has to keep holding after: a cave's protected display is decided by
/// its kind, and nothing about the type a cave carries — nor any value stored on that type — may
/// move a cave between "snapped" and "withheld", or let an exact coordinate through.
/// </para>
/// <para>
/// Each refusal is paired with a positive on the same call: an unprotected cave in the same window
/// that does arrive exactly, so a fixture that produced nothing cannot pass as a protection.
/// </para>
/// </remarks>
public sealed class CaveProtectionParityTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const double GuardedLon = 25.4611;
    private const double GuardedLat = 45.5322;
    private const double OpenLon = 25.4711;
    private const double OpenLat = 45.5422;
    private const string Bbox = "25.40,45.50,25.50,45.60";

    private readonly SilexGisApiFactory factory;
    private HttpClient owner = null!;  // Editor, owns every row
    private HttpClient reader = null!; // Viewer, reads authenticated rows, owns none, holds no exact view
    private string marker = null!;
    private long caveTypeId;
    private long entranceTypeId;
    private long surfaceAreaTypeId;

    public CaveProtectionParityTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        marker = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"cpp-own-{marker}@t.local");

        // A Viewer on purpose: the seeded Editors group reads past visibility and would satisfy
        // every "cannot place it" assertion below for the wrong reason.
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cpp-read-{marker}@t.local");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
            surfaceAreaTypeId = await db.FeatureTypes
                .Where(t => t.Code == "surface_area").Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"cpp-own-{marker}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"cpp-read-{marker}@t.local");
    }

    [Fact]
    public async Task The_map_snaps_a_protected_caves_entrance_and_places_an_open_one_exactly()
    {
        var (guarded, guardedEntrance, _, openEntrance) = await TwoCavesAsync();

        var points = await GetAsync(reader, $"/api/v1/map/cave-entrances?bbox={Bbox}&zoom=14");
        var byId = points.GetProperty("features").EnumerateArray()
            .ToDictionary(f => f.GetProperty("properties").GetProperty("id").GetGuid());

        var snapped = byId[guardedEntrance];
        snapped.GetProperty("properties").GetProperty("caveId").GetGuid().ShouldBe(guarded);
        snapped.GetProperty("properties").GetProperty("approximate").GetBoolean().ShouldBeTrue();
        AssertSnapped(snapped.GetProperty("geometry").GetProperty("coordinates"));

        var exact = byId[openEntrance];
        exact.GetProperty("properties").GetProperty("approximate").GetBoolean().ShouldBeFalse();
        exact.GetProperty("geometry").GetProperty("coordinates")[0].GetDouble().ShouldBe(OpenLon, 1e-9);

        // The cave itself through the shared feature routes: snapped, never withheld, never exact.
        var envelope = await GetAsync(reader, $"/api/v1/features/{guarded}");
        envelope.GetProperty("feature").GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();
        AssertSnapped(envelope.GetProperty("feature").GetProperty("geometry").GetProperty("coordinates"));
        envelope.GetProperty("cave").GetProperty("closestAddress").ValueKind.ShouldBe(JsonValueKind.Null);

        var listed = (await GetAsync(reader, $"/api/v1/features?pageSize=100&search={marker}"))
            .GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("id").GetGuid() == guarded);
        listed.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();
        AssertSnapped(listed.GetProperty("geometry").GetProperty("coordinates"));

        (await GetRawAsync(reader, $"/api/v1/features/{guarded}")).ShouldNotContain(GuardedLon.ToString("0.0000"));
    }

    [Fact]
    public async Task A_public_share_of_a_protected_cave_answers_snapped_and_never_withholds_it()
    {
        var (guarded, _, open, _) = await TwoCavesAsync();
        using var anonymous = factory.CreateClient();

        var token = await MintAsync(guarded);
        var response = await anonymous.GetAsync($"/api/v1/shared/features/{token}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var shared = JsonDocument.Parse(body).RootElement;
        shared.GetProperty("kind").GetString().ShouldBe("cave");
        shared.GetProperty("feature").GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();
        AssertSnapped(shared.GetProperty("feature").GetProperty("geometry").GetProperty("coordinates"));
        shared.GetProperty("cave").GetProperty("closestAddress").ValueKind.ShouldBe(JsonValueKind.Null);
        body.ShouldNotContain(GuardedLon.ToString("0.0000"));
        body.ShouldNotContain(GuardedLat.ToString("0.0000"));

        // The protected cave inside a shared area is listed as a child, not hidden.
        var area = await CreateAreaAsync([guarded, open]);
        var areaShare = JsonDocument.Parse(
            await (await anonymous.GetAsync($"/api/v1/shared/features/{await MintAsync(area)}"))
                .Content.ReadAsStringAsync()).RootElement;
        var children = areaShare.GetProperty("children").EnumerateArray()
            .Select(c => c.GetProperty("id").GetGuid()).ToList();
        children.ShouldContain(guarded);
        children.ShouldContain(open);

        // The open cave's own share is exact, so the snap above is the protection answering.
        var openShared = JsonDocument.Parse(
            await (await anonymous.GetAsync($"/api/v1/shared/features/{await MintAsync(open)}"))
                .Content.ReadAsStringAsync()).RootElement;
        openShared.GetProperty("feature").GetProperty("approximateLocation").GetBoolean().ShouldBeFalse();
        openShared.GetProperty("feature").GetProperty("geometry").GetProperty("coordinates")[0]
            .GetDouble().ShouldBe(OpenLon, 1e-9);
    }

    [Fact]
    public async Task An_export_carries_a_protected_cave_as_a_snapped_point_and_an_open_one_exactly()
    {
        _ = await TwoCavesAsync();

        foreach (var route in new[]
        {
            $"/api/v1/export/features?format=geojson&search={marker}",
            "/api/v1/export/caves?format=geojson",
        })
        {
            var body = await GetRawAsync(reader, route);
            var rows = JsonDocument.Parse(body).RootElement.GetProperty("features").EnumerateArray()
                .Where(f => f.GetProperty("properties").TryGetProperty("name", out var n)
                    && n.GetString() is { } name && name.Contains(marker, StringComparison.Ordinal))
                .ToList();

            var guardedRow = rows.Single(r => r.GetProperty("properties").GetProperty("name").GetString()!
                .StartsWith("Guarded", StringComparison.Ordinal));
            AssertSnapped(guardedRow.GetProperty("geometry").GetProperty("coordinates"));

            var openRow = rows.Single(r => r.GetProperty("properties").GetProperty("name").GetString()!
                .StartsWith("Open", StringComparison.Ordinal));
            openRow.GetProperty("geometry").GetProperty("coordinates")[0].GetDouble().ShouldBe(OpenLon, 1e-9);

            body.ShouldNotContain(GuardedLon.ToString("0.0000"), customMessage: route);
        }
    }

    [Fact]
    public async Task A_device_download_leaves_a_protected_cave_out_and_hands_an_open_one_over_exactly()
    {
        var (guarded, guardedEntrance, open, openEntrance) = await TwoCavesAsync();
        var area = await CreateAreaAsync([guarded, open]);

        var setResponse = await reader.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker}",
            uploadVisibility = "private",
            rootFeatureIds = new[] { area },
            settings = new { },
        });
        setResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await setResponse.Content.ReadAsStringAsync());
        var set = (await setResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var page = await GetAsync(reader, $"/api/v1/sync/sets/{set}/download");
        var features = page.GetProperty("features").EnumerateArray()
            .ToDictionary(f => f.GetProperty("id").GetGuid());

        features.Keys.ShouldContain(area);
        features.Keys.ShouldContain(open);
        features.Keys.ShouldContain(openEntrance);
        features.Keys.ShouldNotContain(guarded);
        features.Keys.ShouldNotContain(guardedEntrance);
        features[open].GetProperty("kind").GetString().ShouldBe("cave");
        features[openEntrance].GetProperty("geometry").GetProperty("coordinates")[0]
            .GetDouble().ShouldBe(OpenLon, 1e-9);
        page.GetProperty("tombstones").EnumerateArray().ShouldBeEmpty();
    }

    private async Task<(Guid Guarded, Guid GuardedEntrance, Guid Open, Guid OpenEntrance)> TwoCavesAsync()
    {
        var guarded = await CreateCaveAsync($"Guarded cave {marker}", locationProtected: true);
        var guardedEntrance = await CreateEntranceAsync(guarded, GuardedLon, GuardedLat);
        var open = await CreateCaveAsync($"Open cave {marker}", locationProtected: false);
        var openEntrance = await CreateEntranceAsync(open, OpenLon, OpenLat);
        return (guarded, guardedEntrance, open, openEntrance);
    }

    private async Task<Guid> CreateCaveAsync(string name, bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "unknown",
            isShowCave = false,
            closestAddress = "Drumul pădurii 3",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateEntranceAsync(Guid cave, double lon, double lat)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/caves/{cave}/entrances", new
        {
            name = $"Entrance {marker}",
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat, 812.0 } },
            positionQuality = "gps",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>An open, readable surface area holding the given caves, as a sync root and a share subtree.</summary>
    private async Task<Guid> CreateAreaAsync(IReadOnlyList<Guid> caves)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Area {marker}",
            featureTypeId = surfaceAreaTypeId,
            visibility = "authenticated",
            locationProtected = false,
            parents = Array.Empty<object>(),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var area = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        foreach (var cave in caves)
        {
            var moved = await owner.PutAsJsonAsync($"/api/v1/features/{cave}/parents", new
            {
                parents = new[] { new { parentId = area, isPrimary = true } },
            });
            moved.IsSuccessStatusCode.ShouldBeTrue(await moved.Content.ReadAsStringAsync());
        }

        return area;
    }

    private async Task<string> MintAsync(Guid featureId)
    {
        var response = await owner.PostAsJsonAsync(
            $"/api/v1/features/{featureId}/shares", new { mode = "public", includeSubtree = true });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string url) =>
        JsonDocument.Parse(await GetRawAsync(client, url)).RootElement;

    private static async Task<string> GetRawAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{url}: {body}");
        return body;
    }

    /// <summary>A two-ordinate point on the obfuscation grid, which no exact position here sits on.</summary>
    private static void AssertSnapped(JsonElement coordinates)
    {
        coordinates.GetArrayLength().ShouldBe(2);
        var cell = LocationProtection.CellDegrees(5000);
        foreach (var value in new[] { coordinates[0].GetDouble(), coordinates[1].GetDouble() })
        {
            (value / cell).ShouldBe(Math.Round(value / cell), 1e-6);
        }

        Math.Abs(coordinates[0].GetDouble() - GuardedLon).ShouldBeGreaterThan(1e-9);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        owner?.Dispose();
        reader?.Dispose();
    }
}
