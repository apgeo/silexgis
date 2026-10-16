// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Map services in the catalogue, from the file to the map: the seeder stores a WMS or WMTS entry's
/// kind and typed options, refuses a kind this version cannot draw in a line the administrator
/// reads, and the layer list publishes what the map can draw and nothing else.
/// </summary>
public sealed class MapLayerKindTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];
    private HttpClient reader = null!;

    public MapLayerKindTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"mlk-rdr-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"mlk-rdr-{suffix}@t.local");
    }

    [Fact]
    public async Task A_catalogue_of_services_is_stored_typed_and_published_as_the_map_draws_it()
    {
        var catalogue = $$"""
            <silexgis-map-layers version="1">
              <layer name="WMS {{suffix}}" kind="wms" url="https://maps.example.gov/wms" layers="topo" attribution="© Agency" />
              <layer name="WMTS {{suffix}}" kind="wmts" base="false"
                     url="https://tiles.example.gov/wmts/{TileMatrix}/{TileRow}/{TileCol}.png"
                     layer="topo" matrixSet="google3857" maxZoom="17" />
              <layer name="Vector {{suffix}}" kind="vector" url="https://tiles.example.gov/v/{z}/{x}/{y}.pbf" />
              <layer name="Cog {{suffix}}" kind="cog" url="https://data.example.gov/dem.tif" />
            </silexgis-map-layers>
            """;

        MapLayerSeedReport report;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            report = await MapLayerSeeder.SeedAsync(catalogue, new Dictionary<string, string>(), db);

            var wms = await db.MapLayers.AsNoTracking().SingleAsync(l => l.Name == $"WMS {suffix}");
            wms.LayerKind.ShouldBe(MapLayerKind.Wms);
            wms.Options.ShouldNotBeNull();
            (await db.MapLayers.AnyAsync(l => l.Name == $"Vector {suffix}" || l.Name == $"Cog {suffix}")).ShouldBeFalse();
        }

        // The two kinds that are not drawn yet are refused visibly, each by name.
        report.Problems.ShouldContain(p => p.Contains($"'Vector {suffix}'") && p.Contains("not supported yet"));
        report.Problems.ShouldContain(p => p.Contains($"'Cog {suffix}'") && p.Contains("not supported yet"));

        var published = await PublishedAsync();
        var service = published[$"WMS {suffix}"];
        service["layerKind"]!.GetValue<string>().ShouldBe("wms");
        service["urlTemplate"]!.GetValue<string>().ShouldBe("https://maps.example.gov/wms");
        service["wms"]!["layers"]!.GetValue<string>().ShouldBe("topo");
        service["wms"]!["version"]!.GetValue<string>().ShouldBe("1.3.0");
        service["wms"]!["transparent"]!.GetValue<bool>().ShouldBeFalse();
        service["wmts"].ShouldBeNull();
        // A map service is never copied into a document.
        service["inDocuments"]!.GetValue<bool>().ShouldBeFalse();

        var tiles = published[$"WMTS {suffix}"];
        tiles["layerKind"]!.GetValue<string>().ShouldBe("wmts");
        tiles["wmts"]!["matrixSet"]!.GetValue<string>().ShouldBe("google3857");
        tiles["wmts"]!["crs"]!.GetValue<string>().ShouldBe("EPSG:3857");
        tiles["wms"].ShouldBeNull();
    }

    /// <summary>
    /// A row put into the table some other way than the catalogue is published only if the map
    /// can draw it: a kind with nothing to draw it, or a service whose stored options do not read
    /// back as its kind's (the column holds JSON, so a wrong shape rather than a broken text), would
    /// be a control that does nothing.
    /// </summary>
    [Fact]
    public async Task A_row_the_map_cannot_draw_is_not_published()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.MapLayers.AddRange(
                new MapLayer { Name = $"Hand vector {suffix}", UrlTemplate = "https://v.example/{z}/{x}/{y}.pbf", LayerKind = MapLayerKind.Vector, IsBase = true, SortOrder = 9000 },
                new MapLayer { Name = $"Hand broken wms {suffix}", UrlTemplate = "https://maps.example.gov/wms", LayerKind = MapLayerKind.Wms, Options = "{\"nonsense\":true}", IsBase = true, SortOrder = 9001 },
                new MapLayer { Name = $"Hand bare wmts {suffix}", UrlTemplate = "https://tiles.example.gov/wmts", LayerKind = MapLayerKind.Wmts, IsBase = true, SortOrder = 9002 },
                new MapLayer { Name = $"Hand xyz {suffix}", UrlTemplate = "https://t.example/{z}/{x}/{y}.png", IsBase = true, SortOrder = 9003 });
            await db.SaveChangesAsync();
        }

        var published = await PublishedAsync();
        published.Keys.ShouldContain($"Hand xyz {suffix}");
        published.Keys.ShouldNotContain($"Hand vector {suffix}");
        published.Keys.ShouldNotContain($"Hand broken wms {suffix}");
        published.Keys.ShouldNotContain($"Hand bare wmts {suffix}");
    }

    /// <summary>
    /// An entry edited into a shape this version refuses switches off the source it seeded on an
    /// earlier start, rather than leaving it drawing as it was while the report says "skipped";
    /// putting the entry right switches it back on.
    /// </summary>
    [Fact]
    public async Task A_source_whose_entry_is_now_refused_stops_drawing_until_it_is_put_right()
    {
        var name = $"Changed {suffix}";
        var asTiles = $$"""<silexgis-map-layers version="1"><layer name="{{name}}" url="https://t.example/{z}/{x}/{y}.png" /></silexgis-map-layers>""";
        var asVector = $$"""<silexgis-map-layers version="1"><layer name="{{name}}" kind="vector" url="https://t.example/{z}/{x}/{y}.pbf" /></silexgis-map-layers>""";

        await SeedAsync(asTiles);
        (await PublishedAsync()).Keys.ShouldContain(name);

        var report = await SeedAsync(asVector);
        (await PublishedAsync()).Keys.ShouldNotContain(name, "a source the catalogue now refuses went on drawing");
        report.Problems.ShouldContain(p => p.Contains($"'{name}'") && p.Contains("switched off"));

        await SeedAsync(asTiles);
        (await PublishedAsync()).Keys.ShouldContain(name);
    }

    private async Task<MapLayerSeedReport> SeedAsync(string catalogue)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await MapLayerSeeder.SeedAsync(catalogue, new Dictionary<string, string>(), db);
    }

    private async Task<Dictionary<string, JsonNode>> PublishedAsync()
    {
        var response = await reader.GetAsync("/api/v1/map-layers");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonNode.Parse(payload)!.AsArray()
            .ToDictionary(l => l!["name"]!.GetValue<string>(), l => l!);
    }

    public Task DisposeAsync()
    {
        reader.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
