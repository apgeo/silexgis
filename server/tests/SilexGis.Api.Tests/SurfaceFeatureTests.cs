// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Surface features end-to-end: CRUD, geometry-kind validation, visibility filtering,
/// map bbox endpoint and unified search.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SurfaceFeatureTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;    // Editor
    private HttpClient outsider = null!; // Editor, unrelated
    private HttpClient viewer = null!;   // Viewer role
    private long sinkholeTypeId;
    private long fractureTypeId;

    public SurfaceFeatureTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"sfown-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"sfout-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"sfview-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            sinkholeTypeId = await db.FeatureTypes.Where(t => t.Code == "sinkhole").Select(t => t.Id).SingleAsync();
            fractureTypeId = await db.FeatureTypes.Where(t => t.Code == "fracture_line").Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"sfown-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"sfout-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"sfview-{suffix}@t.local");
    }

    [Fact]
    public async Task Crud_validation_visibility_map_and_search_work_end_to_end()
    {
        // ---- auth and role gates
        using (var anonymous = factory.CreateClient())
        {
            (await anonymous.GetAsync("/api/v1/surface-features")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        (await viewer.PostAsJsonAsync("/api/v1/surface-features", Body("Viewer feature", sinkholeTypeId)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // ---- geometry validation
        var kindMismatch = await owner.PostAsJsonAsync(
            "/api/v1/surface-features",
            Body("Bad kind", sinkholeTypeId, geometry: Line()));
        kindMismatch.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await kindMismatch.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>()
            .ShouldBe("surface_feature.geometry_kind_mismatch");

        var malformed = await owner.PostAsJsonAsync(
            "/api/v1/surface-features",
            Body("Broken", sinkholeTypeId, geometry: new { type = "Point", coordinates = new[] { 25.0 } }));
        malformed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await malformed.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>()
            .ShouldBe("surface_feature.geometry_invalid");

        // ---- create point + line
        var marker = Guid.NewGuid().ToString("N")[..8];
        var privateId = await CreateAsync(owner, Body($"Dolina Priv {marker}", sinkholeTypeId, "private"));
        var publicId = await CreateAsync(owner, Body(
            $"Falia Publica {marker}", fractureTypeId, "authenticated", geometry: Line(), properties: new { depth = 4 }));

        // ---- read + jsonb passthrough
        var dto = (await owner.GetFromJsonAsync<JsonObject>($"/api/v1/surface-features/{publicId}"))!;
        dto["geometry"]!["type"]!.GetValue<string>().ShouldBe("LineString");
        dto["properties"]!["depth"]!.GetValue<int>().ShouldBe(4);

        // ---- visibility: outsider sees the authenticated feature but not the private one
        (await outsider.GetAsync($"/api/v1/surface-features/{privateId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.GetAsync($"/api/v1/surface-features/{publicId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var outsiderList = (await outsider.GetFromJsonAsync<JsonObject>(
            $"/api/v1/surface-features?search=Dolina Priv {marker}"))!;
        outsiderList["items"]!.AsArray().Count.ShouldBe(0);

        // ---- outsider cannot update or delete; owner can update
        (await outsider.PutAsJsonAsync(
            $"/api/v1/surface-features/{privateId}", Body("Hijack", sinkholeTypeId)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.DeleteAsync($"/api/v1/surface-features/{publicId}"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var renamed = Body($"Dolina Redenumita {marker}", sinkholeTypeId, "private");
        (await owner.PutWithIfMatchAsync($"/api/v1/surface-features/{privateId}", renamed))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // ---- map endpoint: bbox + type filter + visibility
        var bbox = "25.3,45.4,25.6,45.7";
        var ownerMap = (await owner.GetFromJsonAsync<JsonObject>(
            $"/api/v1/map/surface-features?bbox={bbox}"))!["features"]!.AsArray();
        ownerMap.Select(f => f!["properties"]!["id"]!.GetValue<Guid>()).ShouldContain(privateId);
        ownerMap.Select(f => f!["properties"]!["id"]!.GetValue<Guid>()).ShouldContain(publicId);

        var outsiderMap = (await outsider.GetFromJsonAsync<JsonObject>(
            $"/api/v1/map/surface-features?bbox={bbox}"))!["features"]!.AsArray();
        outsiderMap.Select(f => f!["properties"]!["id"]!.GetValue<Guid>()).ShouldNotContain(privateId);
        outsiderMap.Select(f => f!["properties"]!["id"]!.GetValue<Guid>()).ShouldContain(publicId);

        var typeFiltered = (await owner.GetFromJsonAsync<JsonObject>(
            $"/api/v1/map/surface-features?bbox={bbox}&featureTypeId={fractureTypeId}"))!["features"]!.AsArray();
        typeFiltered.Select(f => f!["properties"]!["id"]!.GetValue<Guid>()).ShouldNotContain(privateId);
        typeFiltered.Select(f => f!["properties"]!["id"]!.GetValue<Guid>()).ShouldContain(publicId);

        // ---- unified search includes features (accent-insensitive)
        var search = (await owner.GetFromJsonAsync<JsonObject>($"/api/v1/search?q=redenumita {marker}"))!;
        search["features"]!.AsArray()
            .Select(f => f!["id"]!.GetValue<Guid>())
            .ShouldContain(privateId);

        // ---- delete
        (await owner.DeleteAsync($"/api/v1/surface-features/{privateId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.GetAsync($"/api/v1/surface-features/{privateId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cave_link_is_redacted_when_the_linked_cave_location_is_protected()
    {
        // A location-protected but otherwise visible cave, linked from a feature with
        // exact coordinates: the link must vanish for callers without the
        // exact-location permission, or the feature would give the cave away.
        long caveTypeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        var caveResponse = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Linked Protected Cave {Guid.NewGuid():N}",
            caveTypeId,
            visibility = "authenticated",
            locationProtected = true,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        caveResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var caveId = (await caveResponse.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();

        var lon = 25.4611;
        var lat = 45.5322;
        var featureResponse = await owner.PostAsJsonAsync("/api/v1/surface-features", new
        {
            name = "Sinkhole above protected cave",
            featureTypeId = sinkholeTypeId,
            geometry = new { type = "Point", coordinates = new[] { lon, lat } },
            visibility = "authenticated",
            caveId,
        });
        featureResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await featureResponse.Content.ReadAsStringAsync());
        var featureId = (await featureResponse.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();

        // Owner (may view exact location) keeps the link everywhere.
        var ownerDetail = (await owner.GetFromJsonAsync<JsonObject>($"/api/v1/surface-features/{featureId}"))!;
        ownerDetail["caveId"]!.GetValue<Guid>().ShouldBe(caveId);

        // Outsider can read the feature (authenticated) but not the exact cave location.
        var outsiderDetail = (await outsider.GetFromJsonAsync<JsonObject>($"/api/v1/surface-features/{featureId}"))!;
        outsiderDetail["caveId"].ShouldBeNull();
        outsiderDetail["geometry"]!["coordinates"]![0]!.GetValue<double>().ShouldBe(lon); // geometry stays exact

        // List DTOs and map properties are redacted the same way.
        var listItem = (await outsider.GetFromJsonAsync<JsonObject>("/api/v1/surface-features/?search=above protected"))!
            ["items"]!.AsArray().Single(f => f!["id"]!.GetValue<Guid>() == featureId)!;
        listItem["caveId"].ShouldBeNull();

        var bbox = $"{lon - 0.01},{lat - 0.01},{lon + 0.01},{lat + 0.01}";
        var mapFeature = (await outsider.GetFromJsonAsync<JsonObject>($"/api/v1/map/surface-features?bbox={bbox}"))!
            ["features"]!.AsArray().Single(f => f!["properties"]!["id"]!.GetValue<Guid>() == featureId)!;
        mapFeature["properties"]!["caveId"].ShouldBeNull();

        // Filtering by the protected cave behaves as if nothing were linked.
        var filtered = (await outsider.GetFromJsonAsync<JsonObject>($"/api/v1/surface-features/?caveId={caveId}"))!;
        filtered["items"]!.AsArray().Count.ShouldBe(0);

        // The owner's filter still works.
        var ownerFiltered = (await owner.GetFromJsonAsync<JsonObject>($"/api/v1/surface-features/?caveId={caveId}"))!;
        ownerFiltered["items"]!.AsArray().Count.ShouldBe(1);
    }

    private static object Body(
        string name, long featureTypeId, string visibility = "private",
        object? geometry = null, object? properties = null) => new
    {
        name,
        featureTypeId,
        geometry = geometry ?? new { type = "Point", coordinates = new[] { 25.4455, 45.5301 } },
        properties,
        visibility,
    };

    private static object Line() => new
    {
        type = "LineString",
        coordinates = new[] { new[] { 25.44, 45.52 }, new[] { 25.46, 45.54 } },
    };

    private static async Task<Guid> CreateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/v1/surface-features", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var json = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        return json["id"]!.GetValue<Guid>();
    }

    public async Task DisposeAsync()
    {
        owner.Dispose();
        outsider.Dispose();
        viewer.Dispose();
        await Task.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
