// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Cave centerlines end-to-end: GPX/GeoJSON upload → MultiLineStringZ with a
/// PostGIS-computed geodesic length; the bbox map layer; and the location-protection
/// rule — centerlines of a protected cave are withheld entirely unless the caller may
/// view the exact location.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CenterlineTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private Guid readerId;
    private long caveTypeId;

    public CenterlineTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"ctl-own-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ctl-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"ctl-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"ctl-read-{suffix}@t.local");
    }

    [Fact]
    public async Task Gpx_centerline_lifecycle_with_elevation_length_and_map_layer()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);

        using var form = BuildForm("main-gallery.gpx", GpxTrack());
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var centerline = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement;

        centerline.GetProperty("name").GetString().ShouldBe("main-gallery");
        centerline.GetProperty("source").GetString().ShouldBe("uploaded");
        centerline.GetProperty("geom").GetProperty("type").GetString().ShouldBe("MultiLineString");

        // GPX elevations survive into the GeoJSON positions ([lon, lat, ele]).
        var firstPosition = centerline.GetProperty("geom").GetProperty("coordinates")[0][0];
        firstPosition.GetArrayLength().ShouldBe(3);
        firstPosition[2].GetDouble().ShouldBe(800, 0.01);

        // ~2×135 m of track, measured on the spheroid by PostGIS.
        var length = centerline.GetProperty("lengthM").GetDecimal();
        length.ShouldBeGreaterThan(200);
        length.ShouldBeLessThan(350);

        // Any cave reader sees the list and the bbox map layer.
        var list = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/centerlines");
        list.GetArrayLength().ShouldBe(1);
        var mapFeatures = await MapFeaturesOfAsync(reader, caveId);
        mapFeatures.Count.ShouldBe(1);
        mapFeatures[0].GetProperty("properties").GetProperty("name").GetString().ShouldBe("main-gallery");

        // The reader may not delete; the owner may.
        var id = centerline.GetProperty("id").GetGuid();
        (await reader.DeleteAsync($"/api/v1/cave-centerlines/{id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.DeleteAsync($"/api/v1/cave-centerlines/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/centerlines"))
            .GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Centerlines_of_protected_caves_are_withheld_without_exact_location()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: true);
        using var form = BuildForm("secret.geojson", GeoJsonLine());
        (await owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", form))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // Empty list and no map feature for the plain reader; the owner sees both.
        (await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/centerlines"))
            .GetArrayLength().ShouldBe(0);
        (await MapFeaturesOfAsync(reader, caveId)).Count.ShouldBe(0);
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/centerlines"))
            .GetArrayLength().ShouldBe(1);
        (await MapFeaturesOfAsync(owner, caveId)).Count.ShouldBe(1);

        // An explicit ViewExactLocation ACL grant flips them visible for the reader.
        var grant = await owner.PutAsJsonAsync($"/api/v1/objects/cave/{caveId}/acl", new
        {
            entries = new[] { new { subjectKind = "user", subjectId = readerId, permissions = "read, viewExactLocation" } },
        });
        grant.StatusCode.ShouldBe(HttpStatusCode.OK, await grant.Content.ReadAsStringAsync());

        (await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/centerlines"))
            .GetArrayLength().ShouldBe(1);
        (await MapFeaturesOfAsync(reader, caveId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Bad_uploads_are_rejected_and_private_caves_not_disclosed()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);

        using (var wrongExtension = BuildForm("track.csv", "a,b\n1,2"u8.ToArray()))
        {
            (await owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", wrongExtension))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var garbage = BuildForm("broken.geojson", "not json at all"u8.ToArray()))
        {
            (await owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", garbage))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var pointsOnly = BuildForm("points.geojson", Encoding.UTF8.GetBytes(
            """{"type":"FeatureCollection","features":[{"type":"Feature","geometry":{"type":"Point","coordinates":[25.5,45.5]},"properties":{}}]}""")))
        {
            var response = await owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", pointsOnly);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).ShouldContain("centerline.no_lines");
        }

        // Existence non-disclosure on a private cave.
        var privateCaveId = await CreateCaveAsync(visibility: "private", locationProtected: false);
        (await reader.GetAsync($"/api/v1/caves/{privateCaveId}/centerlines"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var intrusion = BuildForm("intrusion.gpx", GpxTrack());
        (await reader.PostAsync($"/api/v1/caves/{privateCaveId}/centerlines", intrusion))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- helpers ----

    /// <summary>Bbox map features of one cave — keeps tests independent of other rows in the shared DB.</summary>
    private static async Task<List<JsonElement>> MapFeaturesOfAsync(HttpClient client, Guid caveId)
    {
        var map = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/map/cave-centerlines?bbox=25.4,45.4,25.6,45.6");
        return [.. map.GetProperty("features").EnumerateArray()
            .Where(f => f.GetProperty("properties").GetProperty("caveId").GetGuid() == caveId)];
    }

    private async Task<Guid> CreateCaveAsync(string visibility, bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Centerline Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>A three-point GPX track with elevations near 25.5E 45.5N (~270 m long).</summary>
    private static byte[] GpxTrack() => Encoding.UTF8.GetBytes(
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <gpx version="1.1" creator="silexgis-test" xmlns="http://www.topografix.com/GPX/1/1">
          <trk><name>main gallery</name><trkseg>
            <trkpt lat="45.500" lon="25.500"><ele>800</ele></trkpt>
            <trkpt lat="45.501" lon="25.501"><ele>810</ele></trkpt>
            <trkpt lat="45.502" lon="25.502"><ele>820</ele></trkpt>
          </trkseg></trk>
        </gpx>
        """);

    private static byte[] GeoJsonLine() => Encoding.UTF8.GetBytes(
        """{"type":"FeatureCollection","features":[{"type":"Feature","geometry":{"type":"LineString","coordinates":[[25.5,45.5],[25.51,45.51]]},"properties":{}}]}""");

    private static MultipartFormDataContent BuildForm(string fileName, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/octet-stream");
        return new MultipartFormDataContent { { content, "file", fileName } };
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
