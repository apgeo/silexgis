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
/// The cross-kind feature surface end-to-end: generic CRUD with schema-validated
/// properties and geometry-class validation (Multi* included), the typed-endpoint guard
/// on POST, visibility filtering, the map layer, and the resolver envelope for subtyped
/// kinds (cave, entrance).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FeatureTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;    // Editor
    private HttpClient outsider = null!; // Editor, unrelated
    private HttpClient viewer = null!;   // Viewer role
    private long sinkholeTypeId;
    private long fractureTypeId;
    private long caveTypeId;
    private long entranceTypeId;

    public FeatureTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"ftown-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"ftout-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ftview-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            sinkholeTypeId = await db.FeatureTypes.Where(t => t.Code == "sinkhole").Select(t => t.Id).SingleAsync();
            fractureTypeId = await db.FeatureTypes.Where(t => t.Code == "fracture_line").Select(t => t.Id).SingleAsync();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"ftown-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"ftout-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"ftview-{suffix}@t.local");
    }

    [Fact]
    public async Task Crud_validation_visibility_and_map_work_end_to_end()
    {
        // ---- auth and role gates
        using (var anonymous = factory.CreateClient())
        {
            (await anonymous.GetAsync("/api/v1/features")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        (await viewer.PostAsJsonAsync("/api/v1/features", Body("Viewer feature", sinkholeTypeId)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // ---- subtyped kinds are rejected on the generic create route
        var kindGuard = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "cave",
            name = "Not through here",
            featureTypeId = sinkholeTypeId,
            visibility = "private",
        });
        kindGuard.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await kindGuard.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>()
            .ShouldBe("feature.kind_not_generic");

        // ---- geometry validation: class outside the kind's accepted set, and malformed
        var classMismatch = await owner.PostAsJsonAsync(
            "/api/v1/features", Body("Bad class", sinkholeTypeId, geometry: Line()));
        classMismatch.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await classMismatch.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>()
            .ShouldBe("feature.geometry_invalid");

        var malformed = await owner.PostAsJsonAsync(
            "/api/v1/features",
            Body("Broken", sinkholeTypeId, geometry: new { type = "Point", coordinates = new[] { 25.0 } }));
        malformed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await malformed.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>()
            .ShouldBe("feature.geometry_invalid");

        // ---- unknown feature type
        var unknownType = await owner.PostAsJsonAsync("/api/v1/features", Body("No type", 999_999));
        unknownType.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unknownType.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>()
            .ShouldBe("feature.type_required");

        // ---- schema-validated properties: the sinkhole kind's schema requires depth_m >= 0
        var badProperties = await owner.PostAsJsonAsync(
            "/api/v1/features", Body("Too deep", sinkholeTypeId, properties: new { depth_m = -3 }));
        badProperties.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await badProperties.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>()
            .ShouldBe("feature.properties_invalid");

        // ---- create a private point and an authenticated line
        var marker = Guid.NewGuid().ToString("N")[..8];
        var privateId = await CreateAsync(owner, Body(
            $"Dolina Priv {marker}", sinkholeTypeId, "private", properties: new { depth_m = 4.5, diameter_m = 10 }));
        var publicId = await CreateAsync(owner, Body(
            $"Falia Publica {marker}", fractureTypeId, "authenticated", geometry: Line(), properties: new { depth = 4 }));

        // ---- read: geometry + typed/schemaless jsonb passthrough
        var dto = (await owner.GetFromJsonAsync<JsonObject>($"/api/v1/features/{publicId}"))!;
        dto["kind"]!.GetValue<string>().ShouldBe("generic");
        dto["feature"]!["featureTypeCode"]!.GetValue<string>().ShouldBe("fracture_line");
        dto["feature"]!["geometry"]!["type"]!.GetValue<string>().ShouldBe("LineString");
        dto["feature"]!["properties"]!["depth"]!.GetValue<int>().ShouldBe(4);

        var privateDto = (await owner.GetFromJsonAsync<JsonObject>($"/api/v1/features/{privateId}"))!;
        privateDto["feature"]!["featureTypeCode"]!.GetValue<string>().ShouldBe("sinkhole");
        privateDto["feature"]!["properties"]!["depth_m"]!.GetValue<double>().ShouldBe(4.5);

        // ---- visibility: outsider sees the authenticated feature but not the private one
        (await outsider.GetAsync($"/api/v1/features/{privateId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.GetAsync($"/api/v1/features/{publicId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var outsiderList = (await outsider.GetFromJsonAsync<JsonObject>(
            $"/api/v1/features?search=Dolina Priv {marker}"))!;
        outsiderList["items"]!.AsArray().Count.ShouldBe(0);

        // ---- invalid list filters are named errors, not silent misses
        var badKind = await owner.GetAsync("/api/v1/features?kind=bogus");
        badKind.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await badKind.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>()
            .ShouldBe("feature.kind_invalid");

        // ---- outsider cannot update or delete; owner can update
        (await outsider.PutWithIfMatchAsync($"/api/v1/features/{privateId}", Body("Hijack", sinkholeTypeId)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.DeleteAsync($"/api/v1/features/{publicId}"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var renamed = Body($"Dolina Redenumita {marker}", sinkholeTypeId, "private", properties: new { depth_m = 5 });
        (await owner.PutWithIfMatchAsync($"/api/v1/features/{privateId}", renamed))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.GetFromJsonAsync<JsonObject>($"/api/v1/features/{privateId}"))!
            ["feature"]!["name"]!.GetValue<string>().ShouldBe($"Dolina Redenumita {marker}");

        // ---- schema validation guards the update path too
        var badUpdate = await owner.PutWithIfMatchAsync(
            $"/api/v1/features/{privateId}",
            Body("Bad props", sinkholeTypeId, "private", properties: new { depth_m = "deep" }));
        badUpdate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await badUpdate.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>()
            .ShouldBe("feature.properties_invalid");

        // ---- map endpoint: bbox + type filter + visibility
        var bbox = "25.3,45.4,25.6,45.7";
        var ownerMap = (await owner.GetFromJsonAsync<JsonObject>(
            $"/api/v1/map/features?bbox={bbox}"))!["features"]!.AsArray();
        ownerMap.Select(f => f!["properties"]!["id"]!.GetValue<Guid>()).ShouldContain(privateId);
        ownerMap.Select(f => f!["properties"]!["id"]!.GetValue<Guid>()).ShouldContain(publicId);
        ownerMap.Single(f => f!["properties"]!["id"]!.GetValue<Guid>() == privateId)!
            ["properties"]!["kind"]!.GetValue<string>().ShouldBe("generic");

        var outsiderMap = (await outsider.GetFromJsonAsync<JsonObject>(
            $"/api/v1/map/features?bbox={bbox}"))!["features"]!.AsArray();
        outsiderMap.Select(f => f!["properties"]!["id"]!.GetValue<Guid>()).ShouldNotContain(privateId);
        outsiderMap.Select(f => f!["properties"]!["id"]!.GetValue<Guid>()).ShouldContain(publicId);

        var typeFiltered = (await owner.GetFromJsonAsync<JsonObject>(
            $"/api/v1/map/features?bbox={bbox}&featureTypeId={fractureTypeId}"))!["features"]!.AsArray();
        typeFiltered.Select(f => f!["properties"]!["id"]!.GetValue<Guid>()).ShouldNotContain(privateId);
        typeFiltered.Select(f => f!["properties"]!["id"]!.GetValue<Guid>()).ShouldContain(publicId);

        // ---- delete is soft but total for read paths
        (await owner.DeleteAsync($"/api/v1/features/{privateId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.GetAsync($"/api/v1/features/{privateId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Multi_part_geometry_round_trips_through_create_update_and_map()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        // Imported multi-part geodata (a GPX track is a MultiLineString) must create, read,
        // edit and render like a simple feature. A multi-part line fits a Line-kind type.
        var multiLine = new
        {
            type = "MultiLineString",
            coordinates = new[]
            {
                new[] { new[] { 25.44, 45.52 }, new[] { 25.45, 45.53 } },
                new[] { new[] { 25.46, 45.54 }, new[] { 25.47, 45.55 } },
            },
        };
        var id = await CreateAsync(owner, Body($"Multi Track {marker}", fractureTypeId, "authenticated", geometry: multiLine));

        var dto = (await owner.GetFromJsonAsync<JsonObject>($"/api/v1/features/{id}"))!;
        dto["feature"]!["geometry"]!["type"]!.GetValue<string>().ShouldBe("MultiLineString");
        dto["feature"]!["geometry"]!["coordinates"]!.AsArray().Count.ShouldBe(2);

        // Editing the geometry (here, a third part) and saving it back is the modify+save
        // path the map edit tools drive — it must round-trip, not 400 as "geometry_invalid".
        var edited = new
        {
            type = "MultiLineString",
            coordinates = new[]
            {
                new[] { new[] { 25.44, 45.52 }, new[] { 25.45, 45.53 } },
                new[] { new[] { 25.46, 45.54 }, new[] { 25.47, 45.55 } },
                new[] { new[] { 25.48, 45.56 }, new[] { 25.49, 45.57 } },
            },
        };
        (await owner.PutWithIfMatchAsync($"/api/v1/features/{id}",
            Body($"Multi Track {marker}", fractureTypeId, "authenticated", geometry: edited)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var updated = (await owner.GetFromJsonAsync<JsonObject>($"/api/v1/features/{id}"))!;
        updated["feature"]!["geometry"]!["coordinates"]!.AsArray().Count.ShouldBe(3);

        // The map endpoint emits the multi-part geometry unchanged.
        var mapFeature = (await owner.GetFromJsonAsync<JsonObject>(
            "/api/v1/map/features?bbox=25.3,45.4,25.6,45.7"))!["features"]!.AsArray()
            .Single(f => f!["properties"]!["id"]!.GetValue<Guid>() == id)!;
        mapFeature["geometry"]!["type"]!.GetValue<string>().ShouldBe("MultiLineString");

        // A multipoint fits a Point-kind type.
        var multiPointId = await CreateAsync(owner, Body($"Multi Points {marker}", sinkholeTypeId, "private", geometry: new
        {
            type = "MultiPoint",
            coordinates = new[] { new[] { 25.44, 45.52 }, new[] { 25.45, 45.53 } },
        }));
        (await owner.GetFromJsonAsync<JsonObject>($"/api/v1/features/{multiPointId}"))!
            ["feature"]!["geometry"]!["type"]!.GetValue<string>().ShouldBe("MultiPoint");

        // But a multipolygon still does not fit a Line-kind type — class checks hold for Multi*.
        var classMismatch = await owner.PostAsJsonAsync("/api/v1/features", Body(
            $"Bad multi {marker}", fractureTypeId, geometry: new
            {
                type = "MultiPolygon",
                coordinates = new[]
                {
                    new[] { new[] { new[] { 0.0, 0.0 }, new[] { 4.0, 0.0 }, new[] { 4.0, 4.0 }, new[] { 0.0, 4.0 }, new[] { 0.0, 0.0 } } },
                },
            }));
        classMismatch.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await classMismatch.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>()
            .ShouldBe("feature.geometry_invalid");
    }

    [Fact]
    public async Task Resolver_answers_any_feature_id_with_its_typed_envelope()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        // A cave with one entrance, built through the typed endpoints.
        var caveResponse = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Envelope Cave {marker}",
            caveTypeId,
            visibility = "authenticated",
            explorationStatus = "unknown",
            isShowCave = false,
            locationProtected = false,
        });
        caveResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await caveResponse.Content.ReadAsStringAsync());
        var caveId = (await caveResponse.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();

        var lon = 25.4432;
        var lat = 45.5121;
        var entranceResponse = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            name = $"Main entrance {marker}",
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            positionQuality = "gps",
        });
        entranceResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await entranceResponse.Content.ReadAsStringAsync());
        var entranceDto = (await entranceResponse.Content.ReadFromJsonAsync<JsonObject>())!;
        // The entrance DTO keeps its cave pointer — the value is the cave FEATURE id.
        entranceDto["caveId"]!.GetValue<Guid>().ShouldBe(caveId);
        var entranceId = entranceDto["id"]!.GetValue<Guid>();

        // Cave id → cave envelope: common view plus the cave part, nothing else.
        var caveEnvelope = (await owner.GetFromJsonAsync<JsonObject>($"/api/v1/features/{caveId}"))!;
        caveEnvelope["kind"]!.GetValue<string>().ShouldBe("cave");
        caveEnvelope["feature"]!["id"]!.GetValue<Guid>().ShouldBe(caveId);
        caveEnvelope["feature"]!["name"]!.GetValue<string>().ShouldBe($"Envelope Cave {marker}");
        caveEnvelope["cave"]!["caveTypeId"]!.GetValue<long>().ShouldBe(caveTypeId);
        caveEnvelope["cave"]!["entranceCount"]!.GetValue<int>().ShouldBe(1);
        caveEnvelope["entrance"].ShouldBeNull();
        caveEnvelope["centerline"].ShouldBeNull();
        // The cave feature's geometry is the main entrance's representative point.
        caveEnvelope["feature"]!["geometry"]!["coordinates"]![0]!.GetValue<double>().ShouldBe(lon);
        caveEnvelope["feature"]!["geometry"]!["coordinates"]![1]!.GetValue<double>().ShouldBe(lat);

        // Entrance id → entrance envelope pointing back at its cave.
        var entranceEnvelope = (await owner.GetFromJsonAsync<JsonObject>($"/api/v1/features/{entranceId}"))!;
        entranceEnvelope["kind"]!.GetValue<string>().ShouldBe("caveEntrance");
        entranceEnvelope["entrance"]!["caveFeatureId"]!.GetValue<Guid>().ShouldBe(caveId);
        entranceEnvelope["cave"].ShouldBeNull();
        entranceEnvelope["centerline"].ShouldBeNull();

        // Generic id → all subtype parts null.
        var genericId = await CreateAsync(owner, Body($"Envelope Generic {marker}", sinkholeTypeId, "authenticated"));
        var genericEnvelope = (await owner.GetFromJsonAsync<JsonObject>($"/api/v1/features/{genericId}"))!;
        genericEnvelope["kind"]!.GetValue<string>().ShouldBe("generic");
        genericEnvelope["cave"].ShouldBeNull();
        genericEnvelope["entrance"].ShouldBeNull();
        genericEnvelope["centerline"].ShouldBeNull();

        // Unknown ids are not disclosed differently from unreadable ones.
        var missing = await owner.GetAsync($"/api/v1/features/{Guid.NewGuid()}");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await missing.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>()
            .ShouldBe("feature.not_found");
    }

    private static object Body(
        string name, long featureTypeId, string visibility = "private",
        object? geometry = null, object? properties = null) => new
    {
        kind = "generic",
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
        var response = await client.PostAsJsonAsync("/api/v1/features", body);
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
