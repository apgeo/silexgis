// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Geofile pipeline end-to-end: multipart upload → queued OGR import job → bbox map
/// layer → re-export; plus export endpoints with visibility filtering and location
/// obfuscation, and geofile/job access rules.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GeofileTests : IAsyncLifetime, IDisposable
{
    private const string WorldBbox = "-180,-90,180,90";

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient editor = null!;
    private HttpClient outsider = null!; // Editor, unrelated user
    private HttpClient viewer = null!;
    private Guid editorId;

    public GeofileTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        editorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"gf-editor-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"gf-out-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"gf-view-{suffix}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"gf-editor-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"gf-out-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"gf-view-{suffix}@t.local");
    }

    [Fact]
    public async Task Gpx_upload_imports_features_and_serves_them_on_the_map()
    {
        const string gpx = """
            <?xml version="1.0" encoding="UTF-8"?>
            <gpx version="1.1" creator="test" xmlns="http://www.topografix.com/GPX/1/1">
              <wpt lat="45.51" lon="25.41"><name>Spring A</name><ele>812</ele></wpt>
              <wpt lat="45.52" lon="25.42"><name>Sink B</name></wpt>
              <trk><name>Approach</name><trkseg>
                <trkpt lat="45.50" lon="25.40"/><trkpt lat="45.505" lon="25.405"/><trkpt lat="45.51" lon="25.41"/>
              </trkseg></trk>
            </gpx>
            """;

        var geofile = await UploadAsync(editor, "approach.gpx", Encoding.UTF8.GetBytes(gpx));
        geofile.GetProperty("format").GetString().ShouldBe("gpx");
        var id = geofile.GetProperty("id").GetGuid();

        var status = await WaitForImportAsync(editor, id);
        status.GetProperty("importStatus").GetString().ShouldBe("imported");
        status.GetProperty("featureCount").GetInt32().ShouldBe(3); // 2 waypoints + 1 track

        var detail = await GetJsonAsync(editor, $"/api/v1/geofiles/{id}");
        detail.GetProperty("bbox").ValueKind.ShouldBe(JsonValueKind.Object);

        // Map layer: rows come back as GeoJSON with source attributes.
        var collection = await GetJsonAsync(editor, $"/api/v1/map/geofiles/{id}/features?bbox={WorldBbox}");
        var features = collection.GetProperty("features").EnumerateArray().ToList();
        features.Count.ShouldBe(3);
        features.Count(f => f.GetProperty("geometry").GetProperty("type").GetString() == "Point").ShouldBe(2);
        features.SelectMany(f => f.GetProperty("properties").EnumerateObject())
            .Any(p => p.Name == "name" && p.Value.GetString() == "Spring A").ShouldBeTrue();

        // The requester can poll the job; others get 404.
        long jobId;
        using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            jobId = await db.ProcessingJobs
                .Where(j => j.Kind == ProcessingJobKinds.GeofileImport && j.RequestedBy == editorId)
                .OrderByDescending(j => j.Id)
                .Select(j => j.Id)
                .FirstAsync();
        }

        (await editor.GetAsync($"/api/v1/jobs/{jobId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await outsider.GetAsync($"/api/v1/jobs/{jobId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Shapefile_zip_export_reimports_losslessly()
    {
        // Native surface features → zipped-shapefile export → import as a geofile.
        var typeId = await FeatureTypeIdAsync("generic");
        await CreateFeatureAsync("Shp Point", typeId, new { type = "Point", coordinates = new[] { 25.71, 45.71 } });
        await CreateFeatureAsync("Shp Line", typeId, new
        {
            type = "LineString",
            coordinates = new[] { new[] { 25.72, 45.72 }, new[] { 25.73, 45.73 } },
        });

        var export = await editor.GetAsync("/api/v1/export/surface-features?format=shapefile&search=Shp");
        export.StatusCode.ShouldBe(HttpStatusCode.OK);
        export.Content.Headers.ContentType!.MediaType.ShouldBe("application/zip");
        var zip = await export.Content.ReadAsByteArrayAsync();
        zip.Length.ShouldBeGreaterThan(0);

        var geofile = await UploadAsync(editor, "roundtrip.zip", zip);
        var id = geofile.GetProperty("id").GetGuid();
        var status = await WaitForImportAsync(editor, id);
        status.GetProperty("importStatus").GetString().ShouldBe("imported", status.ToString());
        status.GetProperty("featureCount").GetInt32().ShouldBe(2);

        var collection = await GetJsonAsync(editor, $"/api/v1/map/geofiles/{id}/features?bbox={WorldBbox}");
        var names = collection.GetProperty("features").EnumerateArray()
            .Select(f => f.GetProperty("properties").GetProperty("name").GetString())
            .ToList();
        names.ShouldBe(["Shp Point", "Shp Line"], ignoreOrder: true);
    }

    [Fact]
    public async Task Cave_exports_obfuscate_protected_locations_and_filter_visibility()
    {
        var caveTypeId = await CaveTypeIdAsync();
        var entranceTypeId = await EntranceTypeIdAsync();

        var openCave = await CreateCaveAsync("Export Open Cave", caveTypeId, locationProtected: false);
        var protectedCave = await CreateCaveAsync("Export Protected Cave", caveTypeId, locationProtected: true);
        var privateCave = await CreateCaveAsync("Export Private Cave", caveTypeId, locationProtected: false, visibility: "private");

        const double exactLon = 25.44721;
        const double exactLat = 45.53127;
        foreach (var caveId in new[] { openCave, protectedCave, privateCave })
        {
            await CreateEntranceAsync(caveId, entranceTypeId, exactLon, exactLat);
        }

        // Viewer GeoJSON export: private cave absent, protected cave snapped to the grid.
        var response = await viewer.GetAsync("/api/v1/export/caves?format=geojson&search=Export");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var geojson = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var features = geojson.GetProperty("features").EnumerateArray().ToList();

        var names = features.Select(f => f.GetProperty("properties").GetProperty("name").GetString()).ToList();
        names.ShouldContain("Export Open Cave");
        names.ShouldContain("Export Protected Cave");
        names.ShouldNotContain("Export Private Cave");

        var cell = LocationProtection.CellDegrees(5000);
        foreach (var feature in features)
        {
            var props = feature.GetProperty("properties");
            var coords = feature.GetProperty("geometry").GetProperty("coordinates");
            var lon = coords[0].GetDouble();
            var lat = coords[1].GetDouble();

            if (props.GetProperty("name").GetString() == "Export Protected Cave")
            {
                props.GetProperty("approximate").GetString().ShouldBe("yes");
                (lon / cell).ShouldBe(Math.Round(lon / cell), 1e-6, "obfuscated lon must sit on the grid");
                (lat / cell).ShouldBe(Math.Round(lat / cell), 1e-6, "obfuscated lat must sit on the grid");
                lon.ShouldNotBe(exactLon);
            }
            else
            {
                lon.ShouldBe(exactLon, 1e-9);
                lat.ShouldBe(exactLat, 1e-9);
            }
        }

        // Owner (editor) sees exact coordinates for their own protected cave.
        var ownerExport = JsonDocument.Parse(await (await editor.GetAsync(
            "/api/v1/export/caves?format=geojson&search=Export Protected")).Content.ReadAsStringAsync()).RootElement;
        var ownerCoords = ownerExport.GetProperty("features")[0].GetProperty("geometry").GetProperty("coordinates");
        ownerCoords[0].GetDouble().ShouldBe(exactLon, 1e-9);

        // CSV and GPX flavors materialize with the right shapes.
        var csv = await viewer.GetAsync("/api/v1/export/caves?format=csv&search=Export");
        csv.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        (await csv.Content.ReadAsStringAsync()).ShouldContain("Export Open Cave");

        var gpxResponse = await viewer.GetAsync("/api/v1/export/caves?format=gpx&search=Export");
        var gpxDoc = XDocument.Parse(await gpxResponse.Content.ReadAsStringAsync());
        gpxDoc.Descendants().Count(e => e.Name.LocalName == "wpt").ShouldBe(2);

        (await viewer.GetAsync("/api/v1/export/caves?format=dwg"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Geofiles_enforce_roles_visibility_and_non_disclosure()
    {
        const string wkt = "POINT (25.9 45.9)\nLINESTRING (25.9 45.9, 25.91 45.91)";
        var geofile = await UploadAsync(editor, "notes.wkt", Encoding.UTF8.GetBytes(wkt));
        var id = geofile.GetProperty("id").GetGuid();
        (await WaitForImportAsync(editor, id)).GetProperty("featureCount").GetInt32().ShouldBe(2);

        // Viewer role cannot upload at all.
        using (var form = BuildForm("x.wkt", Encoding.UTF8.GetBytes("POINT (1 1)")))
        {
            (await viewer.PostAsync("/api/v1/geofiles", form)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        // Private by default: another user sees 404 everywhere, and the list is filtered.
        foreach (var url in new[]
        {
            $"/api/v1/geofiles/{id}",
            $"/api/v1/geofiles/{id}/status",
            $"/api/v1/map/geofiles/{id}/features?bbox={WorldBbox}",
            $"/api/v1/geofiles/{id}/export?format=geojson",
        })
        {
            (await outsider.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.NotFound, url);
        }

        var outsiderList = await GetJsonAsync(outsider, "/api/v1/geofiles/");
        outsiderList.GetProperty("items").EnumerateArray()
            .Any(g => g.GetProperty("id").GetGuid() == id).ShouldBeFalse();

        // Metadata update + widened visibility open it up for others.
        var update = await editor.PutAsJsonAsync($"/api/v1/geofiles/{id}", new
        {
            name = "Shared Notes",
            description = "now shared",
            style = new { stroke = "#ff0000" },
            teamId = (Guid?)null,
            visibility = "authenticated",
        });
        update.StatusCode.ShouldBe(HttpStatusCode.OK);

        var visible = await GetJsonAsync(outsider, $"/api/v1/geofiles/{id}");
        visible.GetProperty("name").GetString().ShouldBe("Shared Notes");
        visible.GetProperty("style").GetProperty("stroke").GetString().ShouldBe("#ff0000");

        // Re-export of the imported rows round-trips as GeoJSON.
        var export = await outsider.GetAsync($"/api/v1/geofiles/{id}/export?format=geojson");
        export.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonDocument.Parse(await export.Content.ReadAsStringAsync())
            .RootElement.GetProperty("features").GetArrayLength().ShouldBe(2);

        // Others cannot modify or delete; the owner can delete (features + file go too).
        (await outsider.DeleteAsync($"/api/v1/geofiles/{id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await editor.DeleteAsync($"/api/v1/geofiles/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await editor.GetAsync($"/api/v1/geofiles/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.GeofileFeatures.CountAsync(f => f.GeofileId == id)).ShouldBe(0);
    }

    [Fact]
    public async Task Broken_upload_fails_with_recorded_error()
    {
        var geofile = await UploadAsync(editor, "broken.geojson", Encoding.UTF8.GetBytes("this is not geojson"));
        var id = geofile.GetProperty("id").GetGuid();

        var status = await WaitForImportAsync(editor, id, expectFailure: true);
        status.GetProperty("importStatus").GetString().ShouldBe("failed");
        status.GetProperty("importError").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    // ---- helpers ----

    private static MultipartFormDataContent BuildForm(string fileName, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/octet-stream");
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    private async Task<JsonElement> UploadAsync(HttpClient client, string fileName, byte[] bytes)
    {
        using var form = BuildForm(fileName, bytes);
        var response = await client.PostAsync("/api/v1/geofiles", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    /// <summary>Polls the status endpoint until the background import settles.</summary>
    private async Task<JsonElement> WaitForImportAsync(HttpClient client, Guid id, bool expectFailure = false)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            var status = await GetJsonAsync(client, $"/api/v1/geofiles/{id}/status");
            var value = status.GetProperty("importStatus").GetString();
            if (value is "imported" or "failed")
            {
                if (!expectFailure)
                {
                    value.ShouldBe("imported", status.ToString());
                }

                return status;
            }

            DateTimeOffset.UtcNow.ShouldBeLessThan(deadline, "import job did not finish in time");
            await Task.Delay(250);
        }
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{url} → {payload}");
        return JsonDocument.Parse(payload).RootElement;
    }

    private async Task<long> FeatureTypeIdAsync(string code)
    {
        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.FeatureTypes.Where(t => t.Code == code).Select(t => t.Id).SingleAsync();
    }

    private async Task<long> CaveTypeIdAsync()
    {
        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync();
    }

    private async Task<long> EntranceTypeIdAsync()
    {
        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.EntranceTypes.Where(t => t.Code == "natural").Select(t => t.Id).SingleAsync();
    }

    private async Task CreateFeatureAsync(string name, long featureTypeId, object geometry)
    {
        var response = await editor.PostAsJsonAsync("/api/v1/surface-features", new
        {
            name,
            featureTypeId,
            geometry,
            visibility = "authenticated",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> CreateCaveAsync(
        string name, long caveTypeId, bool locationProtected, string visibility = "authenticated")
    {
        var response = await editor.PostAsJsonAsync("/api/v1/caves", new
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

    private async Task CreateEntranceAsync(Guid caveId, long entranceTypeId, double lon, double lat)
    {
        var response = await editor.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            positionQuality = "Gps",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        try
        {
            if (Directory.Exists(filesRoot))
            {
                Directory.Delete(filesRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temp files; best effort.
        }
    }
}
