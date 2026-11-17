// SPDX-License-Identifier: AGPL-3.0-or-later
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
/// The area statistics split by mapped geology: each lithology's outcrop inside the outline, the
/// entrances standing on it, and the outcrop-normalised density over karstifiable ground.
///
/// <para>
/// The split is spatial, unlike every declared figure beside it, so the property this file exists
/// to pin is that it runs over the placeable set only: an entrance the caller may not place is
/// counted on no ground at all, and a unit outline they may not place does not shape the split.
/// Each test sits in its own box in the southern Indian Ocean, because entrances are found by
/// their position and a neighbour test's entrance inside the same outline would be counted.
/// </para>
/// </summary>
public sealed class AreaLithologyStatisticsTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private long caveTypeId;
    private long entranceTypeId;
    private long karstAreaTypeId;
    private long unitTypeId;

    public AreaLithologyStatisticsTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"lith-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"lith-view-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
            karstAreaTypeId = await db.FeatureTypes
                .Where(t => t.Code == FeatureTypeSeeds.KarstArea).Select(t => t.Id).SingleAsync();
            unitTypeId = await db.FeatureTypes
                .Where(t => t.Code == FeatureTypeSeeds.GeologicalUnit).Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"lith-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"lith-view-{suffix}@t.local");
    }

    [Fact]
    public async Task Each_class_reports_its_outcrop_inside_the_outline_and_the_entrances_on_it()
    {
        // Outline 70.0–70.4 E. Limestone over the west half (as two overlapping units, which must
        // be merged rather than counted twice), sandstone over 70.2–70.3 and reaching far outside
        // the outline (which must be clipped), and 70.3–70.4 unmapped.
        var areaId = await AreaAsync(70.0, -49.4, 70.4, -49.0);
        await UnitAsync("limestone", 69.9, -49.4, 70.15, -49.0);
        await UnitAsync("limestone", 70.1, -49.4, 70.2, -49.0);
        await UnitAsync("sandstone", 70.2, -49.6, 70.3, -48.8);

        await CaveAsync(70.05, -49.2);
        await CaveAsync(70.15, -49.1);
        await CaveAsync(70.25, -49.2);
        await CaveAsync(70.35, -49.2);
        // Outside the outline, on the sandstone's clipped-off part: on no ground of this area.
        await CaveAsync(70.25, -49.5);

        var body = await StatisticsAsync(owner, areaId);
        var lith = body["lithology"]!;
        lith["basis"]!.GetValue<string>().ShouldBe("placeable");

        var classes = lith["classes"]!.AsArray();
        classes.Select(c => c!["code"]!.GetValue<string>()).ShouldBe(["limestone", "sandstone"]);

        var limestone = classes[0]!;
        limestone["karstifiable"]!.GetValue<bool>().ShouldBeTrue();
        limestone["recognised"]!.GetValue<bool>().ShouldBeTrue();
        limestone["areaShare"]!.GetValue<double>().ShouldBe(0.5, 0.01);
        limestone["entranceCount"]!.GetValue<int>().ShouldBe(2);
        limestone["entrancesPerKm2"]!.GetValue<double>()
            .ShouldBe(2d / limestone["areaKm2"]!.GetValue<double>(), 1e-9);

        var sandstone = classes[1]!;
        sandstone["karstifiable"]!.GetValue<bool>().ShouldBeFalse();
        sandstone["areaShare"]!.GetValue<double>().ShouldBe(0.25, 0.01);
        sandstone["entranceCount"]!.GetValue<int>().ShouldBe(1);

        lith["unmappedEntranceCount"]!.GetValue<int>().ShouldBe(1);
        (lith["unmappedAreaKm2"]!.GetValue<double>() / body["areaKm2"]!.GetValue<double>())
            .ShouldBe(0.25, 0.01);
        lith["karstifiableEntranceCount"]!.GetValue<int>().ShouldBe(2);
        lith["karstifiableAreaKm2"]!.GetValue<double>()
            .ShouldBe(limestone["areaKm2"]!.GetValue<double>(), 1e-9);
        lith["karstifiableEntrancesPerKm2"]!.GetValue<double>()
            .ShouldBe(limestone["entrancesPerKm2"]!.GetValue<double>(), 1e-9);
    }

    [Fact]
    public async Task An_entrance_the_caller_may_not_place_is_counted_on_no_ground()
    {
        var areaId = await AreaAsync(72.0, -49.4, 72.4, -49.0);
        await UnitAsync("limestone", 72.0, -49.4, 72.4, -49.0);
        await CaveAsync(72.1, -49.2);
        await CaveAsync(72.3, -49.2, locationProtected: true);

        // Positive half: the owner may place both, so the query does find the protected one.
        var seenByOwner = (await StatisticsAsync(owner, areaId))["lithology"]!;
        seenByOwner["karstifiableEntranceCount"]!.GetValue<int>().ShouldBe(2);

        // The Viewer holds no exact-location grant: the protected entrance is withheld before any
        // count, so it is neither on the limestone nor moved onto the unmapped ground.
        var seenByViewer = (await StatisticsAsync(viewer, areaId))["lithology"]!;
        seenByViewer["karstifiableEntranceCount"]!.GetValue<int>().ShouldBe(1);
        seenByViewer["unmappedEntranceCount"]!.GetValue<int>().ShouldBe(0);
        seenByViewer["classes"]![0]!["entranceCount"]!.GetValue<int>().ShouldBe(1);
    }

    [Fact]
    public async Task A_unit_outline_the_caller_may_not_place_does_not_shape_the_split()
    {
        var areaId = await AreaAsync(74.0, -49.4, 74.4, -49.0);
        await UnitAsync("limestone", 74.0, -49.4, 74.2, -49.0);
        await UnitAsync("gypsum_anhydrite", 74.2, -49.4, 74.4, -49.0, locationProtected: true);
        await CaveAsync(74.3, -49.2);

        // Positive half: the owner sees the gypsum and the entrance on it.
        var seenByOwner = (await StatisticsAsync(owner, areaId))["lithology"]!;
        seenByOwner["classes"]!.AsArray().Select(c => c!["code"]!.GetValue<string>())
            .ShouldBe(["gypsum_anhydrite", "limestone"], ignoreOrder: true);
        seenByOwner["karstifiableEntranceCount"]!.GetValue<int>().ShouldBe(1);

        // The Viewer reads the unit but may not place its outline: the split is as if it were not
        // drawn, and the entrance stands on unmapped ground.
        var seenByViewer = (await StatisticsAsync(viewer, areaId))["lithology"]!;
        seenByViewer["classes"]!.AsArray().Select(c => c!["code"]!.GetValue<string>())
            .ShouldBe(["limestone"]);
        seenByViewer["karstifiableEntranceCount"]!.GetValue<int>().ShouldBe(0);
        seenByViewer["unmappedEntranceCount"]!.GetValue<int>().ShouldBe(1);
    }

    [Fact]
    public async Task An_area_without_mapped_geology_reports_its_whole_outline_as_unmapped()
    {
        var areaId = await AreaAsync(76.0, -49.4, 76.4, -49.0);
        await CaveAsync(76.1, -49.2);

        var body = await StatisticsAsync(owner, areaId);
        var lith = body["lithology"]!;
        lith["classes"]!.AsArray().Count.ShouldBe(0);
        lith["unmappedAreaKm2"]!.GetValue<double>().ShouldBe(body["areaKm2"]!.GetValue<double>(), 1e-6);
        lith["unmappedEntranceCount"]!.GetValue<int>().ShouldBe(1);
        lith["karstifiableEntrancesPerKm2"].ShouldBeNull();
    }

    [Fact]
    public async Task A_lithology_outside_the_vocabulary_is_refused_on_entry()
    {
        var response = await PostUnitAsync("chalk", 78.0, -49.4, 78.2, -49.0, locationProtected: false);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("code").GetString().ShouldBe("feature.properties_invalid");
    }

    [Fact]
    public async Task The_spreadsheet_carries_the_split_with_its_basis()
    {
        var areaId = await AreaAsync(80.0, -49.4, 80.4, -49.0);
        await UnitAsync("dolomite", 80.0, -49.4, 80.4, -49.0);
        await CaveAsync(80.2, -49.2);

        var csv = await owner.GetStringAsync($"/api/v1/features/{areaId}/karst-statistics.csv");
        csv.ShouldContain("lithology_basis,placeable\n");
        csv.ShouldContain("lithology.dolomite.entrance_count,1\n");
        csv.ShouldContain("karstifiable_entrance_count,1\n");
    }

    /// <summary>
    /// The map draws a unit by its rock class, so the class and the vocabulary's karstifiable flag
    /// ride on the unit's map row; a unit nobody classified says so with nulls. A protected unit
    /// stays out of the window of a reader who may not place it, class and all, exactly as before.
    /// </summary>
    [Fact]
    public async Task The_map_row_of_a_unit_carries_its_class_and_whether_it_is_karstifiable()
    {
        var limestone = await UnitAsync("limestone", 82.0, -49.4, 82.1, -49.0);
        var sandstone = await UnitAsync("sandstone", 82.1, -49.4, 82.2, -49.0);
        var unclassified = await UnitAsync(null, 82.2, -49.4, 82.3, -49.0);
        var hidden = await UnitAsync("gypsum_anhydrite", 82.3, -49.4, 82.4, -49.0, locationProtected: true);

        const string url = "/api/v1/map/features?bbox=81.9,-49.5,82.5,-48.9&kinds=generic";
        var rows = await MapRowsAsync(viewer, url);

        var lime = rows[limestone]["properties"]!;
        lime["lithology"]!.GetValue<string>().ShouldBe("limestone");
        lime["karstifiable"]!.GetValue<bool>().ShouldBeTrue();

        var sand = rows[sandstone]["properties"]!;
        sand["lithology"]!.GetValue<string>().ShouldBe("sandstone");
        sand["karstifiable"]!.GetValue<bool>().ShouldBeFalse();

        var none = rows[unclassified]["properties"]!.AsObject();
        none.ContainsKey("lithology").ShouldBeTrue();
        none["lithology"].ShouldBeNull();
        none["karstifiable"].ShouldBeNull();

        rows.ContainsKey(hidden).ShouldBeFalse();
        var ownerRows = await MapRowsAsync(owner, url);
        ownerRows[hidden]["properties"]!["lithology"]!.GetValue<string>().ShouldBe("gypsum_anhydrite");
    }

    private static async Task<Dictionary<Guid, JsonObject>> MapRowsAsync(HttpClient client, string url)
    {
        var collection = (await client.GetFromJsonAsync<JsonObject>(url))!;
        return collection["features"]!.AsArray()
            .Select(f => f!.AsObject())
            .ToDictionary(f => f["properties"]!["id"]!.GetValue<Guid>());
    }

    private async Task<JsonObject> StatisticsAsync(HttpClient client, Guid areaId)
    {
        var response = await client.GetAsync($"/api/v1/features/{areaId}/karst-statistics");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    private static double[][] Ring(double west, double south, double east, double north) =>
    [
        [west, south], [east, south], [east, north], [west, north], [west, south],
    ];

    private async Task<Guid> AreaAsync(double west, double south, double east, double north)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Lith area {Guid.NewGuid():N}"[..30],
            featureTypeId = karstAreaTypeId,
            geometry = new { type = "Polygon", coordinates = new[] { Ring(west, south, east, north) } },
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> UnitAsync(
        string? lithology, double west, double south, double east, double north, bool locationProtected = false)
    {
        var response = await PostUnitAsync(lithology, west, south, east, north, locationProtected);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> PostUnitAsync(
        string? lithology, double west, double south, double east, double north, bool locationProtected)
    {
        var properties = new Dictionary<string, string>
        {
            ["formation"] = "Invented Formation",
            ["age"] = "Upper Jurassic",
        };
        if (lithology is not null)
        {
            properties["lithology"] = lithology;
        }

        return owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Lith unit {Guid.NewGuid():N}"[..30],
            featureTypeId = unitTypeId,
            geometry = new { type = "Polygon", coordinates = new[] { Ring(west, south, east, north) } },
            properties,
            visibility = "authenticated",
            locationProtected,
        });
    }

    private async Task CaveAsync(double lon, double lat, bool locationProtected = false)
    {
        var cave = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Lith cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await cave.Content.ReadAsStringAsync();
        cave.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var caveId = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var entrance = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            positionQuality = "Gps",
        });
        entrance.StatusCode.ShouldBe(HttpStatusCode.Created, await entrance.Content.ReadAsStringAsync());
    }

    public ValueTask DisposeAsync()
    {
        owner.Dispose();
        viewer.Dispose();
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
