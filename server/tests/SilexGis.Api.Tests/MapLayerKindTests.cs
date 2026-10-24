// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Map;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The catalogue's kinds, from the file to the map: the seeder stores each entry's kind and typed
/// options — a GeoTIFF's coordinate system resolved by the installation's own registry — refuses an
/// entry it cannot draw in a line the administrator reads, by its code, and the layer list
/// publishes what the maps can draw and nothing else.
/// </summary>
public sealed class MapLayerKindTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];
    private HttpClient reader = null!;

    public MapLayerKindTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"mlk-rdr-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"mlk-rdr-{suffix}@t.local");
    }

    [Fact]
    public async Task A_catalogue_of_every_kind_is_stored_typed_and_published_as_the_maps_draw_it()
    {
        var catalogue = $$"""
            <silexgis-map-layers version="1">
              <layer name="WMS {{suffix}}" kind="wms" url="https://maps.example.gov/wms" layers="topo" attribution="© Agency" />
              <layer name="WMTS {{suffix}}" kind="wmts" base="false"
                     url="https://tiles.example.gov/wmts/{TileMatrix}/{TileRow}/{TileCol}.png"
                     layer="topo" matrixSet="google3857" maxZoom="17" />
              <layer name="Stereo {{suffix}}" kind="cog" base="false" url="https://data.example.gov/lidar.tif"
                     projection="EPSG:3844" bands="1" nodata="0" min="1" max="255" />
              <layer name="Ortho {{suffix}}" kind="cog" url="https://data.example.gov/ortho.tif" projection="EPSG:3857" />
              <layer name="Off {{suffix}}" url="https://t.example/{z}/{x}/{y}.png" enabled="false" />
              <layer name="Styled {{suffix}}" kind="vector" format="mvt" url="https://tiles.example.org/planet"
                     style="https://tiles.example.org/styles/liberty" labelField="name:{lang}" scene3d="wms {{suffix}}"
                     attribution="© OpenMapTiles © OpenStreetMap contributors" />
              <layer name="Unlit {{suffix}}" kind="vector" format="pmtiles" url="https://files.example.org/a.pmtiles"
                     maxZoom="15" scene3d="Off {{suffix}}" />
              <layer name="Boundary {{suffix}}" kind="vector" format="geojson" base="false" url="https://files.example.org/natura.geojson" />
              <layer name="Unplaced {{suffix}}" kind="cog" url="https://data.example.gov/a.tif" />
              <layer name="Undefined {{suffix}}" kind="cog" url="https://data.example.gov/b.tif" projection="EPSG:999999" />
              <layer name="Formless {{suffix}}" kind="vector" url="https://tiles.example.org/planet" />
            </silexgis-map-layers>
            """;

        var crs = factory.Services.GetRequiredService<ICrsRegistry>();
        var report = await SeedAsync(catalogue);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var wms = await db.MapLayers.AsNoTracking().SingleAsync(l => l.Name == $"WMS {suffix}");
            wms.LayerKind.ShouldBe(MapLayerKind.Wms);
            wms.Options.ShouldNotBeNull();

            // The definition is resolved on start and stored with the entry, so that every view
            // that places the file is handed it with the layer.
            var stereo = await db.MapLayers.AsNoTracking().SingleAsync(l => l.Name == $"Stereo {suffix}");
            stereo.LayerKind.ShouldBe(MapLayerKind.Cog);
            stereo.Options!.ShouldContain("+towgs84");

            (await db.MapLayers.AnyAsync(l =>
                    l.Name == $"Unplaced {suffix}" || l.Name == $"Undefined {suffix}" || l.Name == $"Formless {suffix}"))
                .ShouldBeFalse();
        }

        // Each refusal is said by name, and by its code.
        report.Problems.ShouldContain(p =>
            p.Code == MapLayerCatalogCodes.CogProjectionMissing && p.Message.Contains($"'Unplaced {suffix}'"));
        report.Problems.ShouldContain(p =>
            p.Code == MapLayerCatalogCodes.CogProjectionUnknown && p.Message.Contains($"'Undefined {suffix}'"));
        report.Problems.ShouldContain(p =>
            p.Code == MapLayerCatalogCodes.VectorFormatInvalid && p.Message.Contains($"'Formless {suffix}'"));
        report.Problems.Count.ShouldBe(3, string.Join("; ", report.Problems.Select(p => p.Message)));

        var published = await PublishedAsync();
        var service = published[$"WMS {suffix}"];
        service["layerKind"]!.GetValue<string>().ShouldBe("wms");
        service["urlTemplate"]!.GetValue<string>().ShouldBe("https://maps.example.gov/wms");
        service["wms"]!["layers"]!.GetValue<string>().ShouldBe("topo");
        service["wms"]!["version"]!.GetValue<string>().ShouldBe("1.3.0");
        service["wms"]!["transparent"]!.GetValue<bool>().ShouldBeFalse();
        service["wmts"].ShouldBeNull();
        service["cog"].ShouldBeNull();
        service["vector"].ShouldBeNull();
        // A map service is never copied into a document.
        service["inDocuments"]!.GetValue<bool>().ShouldBeFalse();

        var tiles = published[$"WMTS {suffix}"];
        tiles["layerKind"]!.GetValue<string>().ShouldBe("wmts");
        tiles["wmts"]!["matrixSet"]!.GetValue<string>().ShouldBe("google3857");
        tiles["wmts"]!["crs"]!.GetValue<string>().ShouldBe("EPSG:3857");
        tiles["wms"].ShouldBeNull();

        var lidar = published[$"Stereo {suffix}"];
        lidar["layerKind"]!.GetValue<string>().ShouldBe("cog");
        lidar["urlTemplate"]!.GetValue<string>().ShouldBe("https://data.example.gov/lidar.tif");
        lidar["cog"]!["bands"]!.AsArray().Select(b => b!.GetValue<int>()).ShouldBe([1]);
        lidar["cog"]!["nodata"]!.GetValue<double>().ShouldBe(0);
        lidar["cog"]!["min"]!.GetValue<double>().ShouldBe(1);
        lidar["cog"]!["max"]!.GetValue<double>().ShouldBe(255);
        lidar["cog"]!["projection"]!.GetValue<string>().ShouldBe("EPSG:3844");
        // The installation's own definition, datum shift included — what puts the file where the
        // database and the coordinate formats put Romanian positions.
        lidar["cog"]!["projectionDefinition"]!.GetValue<string>().ShouldBe(crs.Proj4(3844));
        lidar["vector"].ShouldBeNull();
        lidar["inDocuments"]!.GetValue<bool>().ShouldBeFalse();

        var ortho = published[$"Ortho {suffix}"]["cog"]!;
        ortho["bands"].ShouldBeNull();
        ortho["min"].ShouldBeNull();
        ortho["projection"]!.GetValue<string>().ShouldBe("EPSG:3857");
        ortho["projectionDefinition"].ShouldBeNull();

        var styled = published[$"Styled {suffix}"];
        styled["layerKind"]!.GetValue<string>().ShouldBe("vector");
        styled["urlTemplate"]!.GetValue<string>().ShouldBe("https://tiles.example.org/planet");
        styled["maxZoom"]!.GetValue<int>().ShouldBe(MapLayerKinds.VectorDefaultMaxZoom);
        styled["vector"]!["format"]!.GetValue<string>().ShouldBe("mvt");
        styled["vector"]!["style"]!.GetValue<string>().ShouldBe("https://tiles.example.org/styles/liberty");
        styled["vector"]!["styleSource"].ShouldBeNull();
        styled["vector"]!["labelField"]!.GetValue<string>().ShouldBe("name:{lang}");
        // Named as the published layer it is, however the file spelt it.
        styled["vector"]!["scene3dStandIn"]!.GetValue<string>().ShouldBe($"WMS {suffix}");
        styled["cog"].ShouldBeNull();

        // A stand-in that is not published is not sent: the 3D scene would look for it in vain.
        published.Keys.ShouldNotContain($"Off {suffix}");
        var unlit = published[$"Unlit {suffix}"];
        unlit["vector"]!["format"]!.GetValue<string>().ShouldBe("pmtiles");
        unlit["vector"]!["scene3dStandIn"].ShouldBeNull();
        unlit["maxZoom"]!.GetValue<int>().ShouldBe(15);

        published[$"Boundary {suffix}"]["vector"]!["format"]!.GetValue<string>().ShouldBe("geojson");
        published[$"Boundary {suffix}"]["isBase"]!.GetValue<bool>().ShouldBeFalse();

        // Applying the same file again changes nothing: the definition is resolved the same way.
        var again = await SeedAsync(catalogue);
        again.Added.ShouldBe(0);
        again.Updated.ShouldBe(0);
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
                new MapLayer { Name = $"Hand xyz {suffix}", UrlTemplate = "https://t.example/{z}/{x}/{y}.png", IsBase = true, SortOrder = 9003 },
                // A file in a system nobody defined would be drawn somewhere else, not refused.
                new MapLayer
                {
                    Name = $"Hand undefined cog {suffix}", UrlTemplate = "https://d.example/a.tif", LayerKind = MapLayerKind.Cog, IsBase = true, SortOrder = 9004,
                    Options = """{"bands":null,"nodata":null,"min":null,"max":null,"projection":"EPSG:3844","projectionDefinition":null}""",
                },
                new MapLayer
                {
                    Name = $"Hand numbered vector {suffix}", UrlTemplate = "https://v.example/tiles", LayerKind = MapLayerKind.Vector, IsBase = true, SortOrder = 9005,
                    Options = """{"format":0,"style":null,"styleSource":null,"labelField":null,"scene3dStandIn":null}""",
                },
                new MapLayer
                {
                    Name = $"Hand cog {suffix}", UrlTemplate = "https://d.example/b.tif", LayerKind = MapLayerKind.Cog, IsBase = true, SortOrder = 9006,
                    Options = """{"bands":[1],"nodata":null,"min":null,"max":null,"projection":"EPSG:4326","projectionDefinition":null}""",
                });
            await db.SaveChangesAsync();
        }

        var published = await PublishedAsync();
        published.Keys.ShouldContain($"Hand xyz {suffix}");
        published.Keys.ShouldNotContain($"Hand vector {suffix}");
        published.Keys.ShouldNotContain($"Hand broken wms {suffix}");
        published.Keys.ShouldNotContain($"Hand bare wmts {suffix}");
        published.Keys.ShouldNotContain($"Hand undefined cog {suffix}");
        published.Keys.ShouldNotContain($"Hand numbered vector {suffix}");
        published.Keys.ShouldContain($"Hand cog {suffix}");
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
        // Edited into a GeoTIFF that does not say where it is, which the catalogue refuses.
        var asUnplacedFile = $$"""<silexgis-map-layers version="1"><layer name="{{name}}" kind="cog" url="https://t.example/a.tif" /></silexgis-map-layers>""";

        await SeedAsync(asTiles);
        (await PublishedAsync()).Keys.ShouldContain(name);

        var report = await SeedAsync(asUnplacedFile);
        (await PublishedAsync()).Keys.ShouldNotContain(name, "a source the catalogue now refuses went on drawing");
        report.Problems.ShouldContain(p =>
            p.Code == MapLayerCatalogCodes.SwitchedOffAsRefused && p.Message.Contains($"'{name}'") && p.Message.Contains("switched off"));

        await SeedAsync(asTiles);
        (await PublishedAsync()).Keys.ShouldContain(name);
    }

    /// <summary>
    /// The catalogue this application ships, read as a start reads it — through the installation's
    /// own coordinate-system registry, which must know every system a shipped GeoTIFF names.
    /// </summary>
    [Fact]
    public async Task The_shipped_catalogue_reads_without_complaint_through_the_installations_own_registry()
    {
        var root = factory.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
        var path = factory.Services.GetRequiredService<IOptions<MapLayerOptions>>().Value.ResolvedCatalogPath(root);

        var report = await SeedAsync(await File.ReadAllTextAsync(path));

        report.Problems.ShouldBeEmpty(string.Join("; ", report.Problems.Select(p => $"[{p.Code}] {p.Message}")));
        var published = await PublishedAsync();
        published["OpenFreeMap Liberty"]["vector"]!["scene3dStandIn"]!.GetValue<string>().ShouldBe("OpenStreetMap");
        published.Keys.ShouldNotContain("Sentinel-2 scene, Apuseni 2024 (COG example)");
    }

    private async Task<MapLayerSeedReport> SeedAsync(string catalogue)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await MapLayerSeeder.SeedAsync(
            catalogue, new Dictionary<string, string>(), scope.ServiceProvider.GetRequiredService<ICrsRegistry>(), db);
    }

    private async Task<Dictionary<string, JsonNode>> PublishedAsync()
    {
        var response = await reader.GetAsync("/api/v1/map-layers");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonNode.Parse(payload)!.AsArray()
            .ToDictionary(l => l!["name"]!.GetValue<string>(), l => l!);
    }

    public ValueTask DisposeAsync()
    {
        reader.Dispose();
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
