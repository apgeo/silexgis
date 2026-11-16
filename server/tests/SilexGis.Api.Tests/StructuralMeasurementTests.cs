// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Structural readings — bedding, joints, fault planes — as features: entered by strike or by dip
/// direction and stored by dip direction, refused when their orientation is missing or
/// contradictory, and drawn on the map turned to their orientation. A reading linked to a protected
/// cave inherits the cave's protection, and a reader who may not place it exactly sees it only on
/// the protection grid.
///
/// <para>
/// The fixtures sit in a box of their own (northern Honshu) that no other class seeds, so a map
/// window drawn around them holds only what this class made.
/// </para>
/// </summary>
public sealed class StructuralMeasurementTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const double BaseLon = 140.62;
    private const double BaseLat = 40.71;

    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private HttpClient anonymous = null!;
    private long readingTypeId;
    private long caveTypeId;

    public StructuralMeasurementTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"smt-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"smt-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            readingTypeId = await db.FeatureTypes
                .Where(t => t.Code == FeatureTypeSeeds.StructuralMeasurement).Select(t => t.Id).SingleAsync();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"smt-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"smt-read-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    [Fact]
    public async Task A_strike_entered_by_the_right_hand_rule_is_stored_as_its_dip_direction()
    {
        var id = await ReadingAsync(
            BaseLon, BaseLat, new { measurement_type = "bedding", strike = 30, dip = 40, note = "outcrop" });

        var stored = await PropertiesAsync(owner, id);
        stored.TryGetProperty("strike", out _).ShouldBeFalse();
        stored.GetProperty("dip_direction").GetDouble().ShouldBe(120);
        stored.GetProperty("dip").GetDouble().ShouldBe(40);
        stored.GetProperty("note").GetString().ShouldBe("outcrop");

        // The same conversion holds on a full update, not only at creation.
        var update = await owner.PutWithIfMatchAsync($"/api/v1/features/{id}", new
        {
            name = "Smt bedding",
            featureTypeId = readingTypeId,
            geometry = new { type = "Point", coordinates = new[] { BaseLon, BaseLat } },
            properties = new { measurement_type = "joint", strike = 350, dip = 75 },
            locationProtected = false,
            visibility = "authenticated",
        });
        update.StatusCode.ShouldBe(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());

        var updated = await PropertiesAsync(owner, id);
        updated.TryGetProperty("strike", out _).ShouldBeFalse();
        updated.GetProperty("dip_direction").GetDouble().ShouldBe(80);
        updated.GetProperty("measurement_type").GetString().ShouldBe("joint");
    }

    [Theory]
    [InlineData("""{"measurement_type":"bedding","dip_direction":120}""")]
    [InlineData("""{"measurement_type":"bedding","dip_direction":120,"dip":95}""")]
    [InlineData("""{"measurement_type":"bedding","dip_direction":360.5,"dip":10}""")]
    [InlineData("""{"measurement_type":"lineation","dip_direction":120,"dip":10}""")]
    [InlineData("""{"measurement_type":"bedding","strike":30,"dip_direction":300,"dip":40}""")]
    [InlineData("""{"measurement_type":"bedding","strike":"north","dip":40}""")]
    public async Task A_reading_without_a_whole_consistent_orientation_is_refused(string properties)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = "Smt refused",
            featureTypeId = readingTypeId,
            geometry = new { type = "Point", coordinates = new[] { BaseLon + 0.05, BaseLat } },
            properties = JsonDocument.Parse(properties).RootElement,
            locationProtected = false,
            visibility = "authenticated",
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        JsonDocument.Parse(body).RootElement.GetProperty("code").GetString().ShouldBe("feature.properties_invalid");
    }

    [Fact]
    public async Task A_full_turn_dip_direction_is_stored_as_north()
    {
        var id = await ReadingAsync(
            BaseLon + 0.06, BaseLat, new { measurement_type = "fault", dip_direction = 360, dip = 55 });
        (await PropertiesAsync(owner, id)).GetProperty("dip_direction").GetDouble().ShouldBe(0);
    }

    [Fact]
    public async Task The_map_turns_a_reading_to_its_orientation_and_shows_one_in_a_protected_cave_only_approximately()
    {
        var lon = BaseLon + 0.1;
        var lat = BaseLat + 0.1;

        // An open outcrop reading, and one taken inside a protected cave and linked to it.
        var open = await ReadingAsync(lon, lat, new { measurement_type = "bedding", dip_direction = 200, dip = 25 });
        var cave = await CaveAsync(locationProtected: true);
        var underground = await ReadingAsync(
            lon + 0.003, lat + 0.002, new { measurement_type = "joint", dip_direction = 45, dip = 80 }, parentId: cave);

        var bbox = string.Create(CultureInfo.InvariantCulture, $"{lon - 0.2},{lat - 0.2},{lon + 0.2},{lat + 0.2}");

        // The owner may place both exactly: true positions, and each turned to its own plane.
        var ownMap = await MapAsync(owner, bbox);
        var ownOpen = ownMap[open];
        ownOpen["approximate"]!.GetValue<bool>().ShouldBeFalse();
        ownOpen["dipDirection"]!.GetValue<double>().ShouldBe(200);
        ownOpen["dip"]!.GetValue<double>().ShouldBe(25);
        ownOpen["typeCode"]!.GetValue<string>().ShouldBe(FeatureTypeSeeds.StructuralMeasurement);
        var ownUnder = ownMap[underground];
        ownUnder["protected"]!.GetValue<bool>().ShouldBeTrue();
        ownUnder["approximate"]!.GetValue<bool>().ShouldBeFalse();
        IsAt(ownUnder, lon + 0.003, lat + 0.002).ShouldBeTrue();

        // The viewer reads both rows but holds no exact-view grant on the cave. The open reading is
        // where it is; the underground one inherited the cave's protection and is moved onto the
        // protection grid. Its orientation still rides along: it says how a plane lies, not where.
        var readMap = await MapAsync(viewer, bbox);
        var readOpen = readMap[open];
        readOpen["approximate"]!.GetValue<bool>().ShouldBeFalse();
        IsAt(readOpen, lon, lat).ShouldBeTrue();
        var readUnder = readMap[underground];
        readUnder["protected"]!.GetValue<bool>().ShouldBeTrue();
        readUnder["approximate"]!.GetValue<bool>().ShouldBeTrue();
        IsAt(readUnder, lon + 0.003, lat + 0.002).ShouldBeFalse();
        readUnder["dipDirection"]!.GetValue<double>().ShouldBe(45);

        // And the map answers nobody who is not signed in.
        (await anonymous.GetAsync($"/api/v1/map/features?bbox={bbox}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        viewer?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();
    }

    private async Task<Guid> ReadingAsync(double lon, double lat, object properties, Guid? parentId = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Smt {Guid.NewGuid():N}"[..20],
            featureTypeId = readingTypeId,
            geometry = new { type = "Point", coordinates = new[] { lon, lat } },
            properties,
            parents = parentId is null ? null : new[] { new { parentId = parentId.Value, isPrimary = true } },
            locationProtected = false,
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CaveAsync(bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Smt Cave {Guid.NewGuid():N}"[..24],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> PropertiesAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/v1/features/{id}");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("feature").GetProperty("properties").Clone();
    }

    /// <summary>The map window's features by id, each as its properties plus its geometry.</summary>
    private static async Task<Dictionary<Guid, JsonObject>> MapAsync(HttpClient client, string bbox)
    {
        var collection = (await client.GetFromJsonAsync<JsonObject>($"/api/v1/map/features?bbox={bbox}&kinds=generic"))!;
        return collection["features"]!.AsArray()
            .Select(f => f!.AsObject())
            .ToDictionary(
                f => f["properties"]!["id"]!.GetValue<Guid>(),
                f =>
                {
                    var properties = f["properties"]!.AsObject().DeepClone().AsObject();
                    properties["geometry"] = f["geometry"]?.DeepClone();
                    return properties;
                });
    }

    private static bool IsAt(JsonObject feature, double lon, double lat)
    {
        var coordinates = feature["geometry"]!["coordinates"]!.AsArray();
        return Math.Abs(coordinates[0]!.GetValue<double>() - lon) < 1e-9
            && Math.Abs(coordinates[1]!.GetValue<double>() - lat) < 1e-9;
    }
}
