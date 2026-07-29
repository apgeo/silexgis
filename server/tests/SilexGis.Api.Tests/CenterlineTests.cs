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
            // Real surveys reach the low-zoom size gate at tens of thousands of components;
            // dial it down so a test fixture can cross it. Only requests below the gate zoom
            // (12, left at its default) are affected, which the other tests here stay above.
            ["Map:CenterlineGatePaths"] = "2",
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

    [Fact]
    public async Task The_map_overlay_serves_a_splay_free_skeleton_until_the_detail_zoom()
    {
        // A survey export is mostly splays — short wall shots off each station. They cost one
        // canvas path each and are invisible at overview zooms, so those zooms get the skeleton.
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);
        using var form = BuildForm("splayed.geojson", SplayedSurvey(26.100, 46.100));
        (await owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", form))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        var bbox = "26.09,46.09,26.11,46.11";

        // Overview: one sewn polyline in place of the traverse plus its 16 wall shots.
        var overview = await CenterlineResponseAsync(reader, bbox, zoom: 14);
        overview.GetProperty("detail").GetBoolean().ShouldBeFalse();
        var skeleton = FeatureOf(overview, caveId);
        skeleton.GetProperty("properties").GetProperty("paths").GetInt32().ShouldBe(1);
        skeleton.GetProperty("geometry").GetProperty("coordinates").GetArrayLength().ShouldBe(1);

        // Close in, the wall shots come back.
        var detail = await CenterlineResponseAsync(reader, bbox, zoom: 19);
        detail.GetProperty("detail").GetBoolean().ShouldBeTrue();
        var full = FeatureOf(detail, caveId);
        full.GetProperty("properties").GetProperty("paths").GetInt32().ShouldBe(20);

        // The skeleton is display geometry: what the cave detail page serves is untouched, and
        // still carries altitudes.
        var stored = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/centerlines");
        stored.GetArrayLength().ShouldBe(1);
        stored[0].GetProperty("geom").GetProperty("coordinates").GetArrayLength().ShouldBe(20);
    }

    [Fact]
    public async Task The_path_budget_withholds_whole_centerlines_and_says_how_many()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);
        using var form = BuildForm("budgeted.geojson", SplayedSurvey(26.200, 46.200));
        (await owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", form))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        var bbox = "26.19,46.19,26.21,46.21";

        // A budget too small even for the skeleton: nothing is drawn, and the client is told
        // there is something to zoom in for rather than shown a silently empty map.
        var starved = await CenterlineResponseAsync(reader, bbox, zoom: 19, maxPaths: 0);
        starved.GetProperty("features").EnumerateArray()
            .ShouldNotContain(f => f.GetProperty("properties").GetProperty("caveId").GetGuid() == caveId);
        starved.GetProperty("withheldCount").GetInt32().ShouldBeGreaterThanOrEqualTo(1);

        // The same request with room for it draws it and withholds nothing.
        var afforded = await CenterlineResponseAsync(reader, bbox, zoom: 19, maxPaths: 500);
        FeatureOf(afforded, caveId).GetProperty("properties").GetProperty("paths").GetInt32().ShouldBe(20);
        afforded.GetProperty("withheldCount").GetInt32().ShouldBe(0);

        // Between the two: a budget that cannot take the wall shots but can take the skeleton
        // serves the skeleton, rather than blanking an overlay that worked one zoom out.
        var degraded = await CenterlineResponseAsync(reader, bbox, zoom: 19, maxPaths: 10);
        var fallback = FeatureOf(degraded, caveId);
        fallback.GetProperty("properties").GetProperty("paths").GetInt32().ShouldBe(1);
        fallback.GetProperty("properties").GetProperty("detail").GetBoolean().ShouldBeFalse();
        degraded.GetProperty("detail").GetBoolean().ShouldBeFalse();
        degraded.GetProperty("withheldCount").GetInt32().ShouldBe(0);

        // A viewer may also pull detail in earlier than the installation default.
        var early = await CenterlineResponseAsync(reader, bbox, zoom: 14, detailZoom: 13, maxPaths: 500);
        early.GetProperty("detail").GetBoolean().ShouldBeTrue();
        FeatureOf(early, caveId).GetProperty("properties").GetProperty("paths").GetInt32().ShouldBe(20);
    }

    [Fact]
    public async Task Below_the_gate_zoom_an_oversized_centerline_is_withheld_on_its_stored_count_alone()
    {
        // The gate reads the stored component count, so an oversized geometry is never fetched
        // just to discover how big it is. Configured down to 2 components for this test run.
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);
        using var form = BuildForm("wide.geojson", DisconnectedLines(26.300, 46.300, count: 4));
        (await owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", form))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        var bbox = "26.29,46.29,26.32,46.32";

        var zoomedOut = await CenterlineResponseAsync(reader, bbox, zoom: 8);
        zoomedOut.GetProperty("features").EnumerateArray()
            .ShouldNotContain(f => f.GetProperty("properties").GetProperty("caveId").GetGuid() == caveId);
        zoomedOut.GetProperty("withheldCount").GetInt32().ShouldBeGreaterThanOrEqualTo(1);

        // Above the gate zoom the same centerline is served.
        FeatureOf(await CenterlineResponseAsync(reader, bbox, zoom: 14), caveId)
            .GetProperty("properties").GetProperty("paths").GetInt32().ShouldBe(4);
    }

    [Fact]
    public async Task A_protected_caves_centerline_is_not_even_counted_as_withheld()
    {
        // Withholding for size invites "zoom in to see more". Withholding for location
        // protection must not: the count would disclose that a hidden cave is there at all.
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: true);
        using var form = BuildForm("secret.geojson", DisconnectedLines(26.400, 46.400, count: 4));
        (await owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", form))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        var bbox = "26.39,46.39,26.42,46.42";

        foreach (var zoom in new[] { 8, 14, 19 })
        {
            var response = await CenterlineResponseAsync(reader, bbox, zoom);
            response.GetProperty("features").EnumerateArray()
                .ShouldNotContain(f => f.GetProperty("properties").GetProperty("caveId").GetGuid() == caveId);
            response.GetProperty("withheldCount").GetInt32().ShouldBe(0);
        }

        // The owner still sees it, at both representations.
        FeatureOf(await CenterlineResponseAsync(owner, bbox, zoom: 14), caveId)
            .GetProperty("properties").GetProperty("paths").GetInt32().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_kml_export_uploads_and_survives_its_zero_length_shots()
    {
        // Both real survey exports on hand are KML and both contain a few zero-length shots (a
        // station written twice). That combination used to fail the upload outright: the
        // degenerate component made the whole geometry invalid and the reader dropped it.
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);
        using var form = BuildForm("survey.kml", KmlSurveyWithDegenerateShot(26.500, 46.500));
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        var centerline = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement;
        centerline.GetProperty("geom").GetProperty("type").GetString().ShouldBe("MultiLineString");
        // Three shots went in, one of them zero-length; only the two real ones are stored.
        centerline.GetProperty("geom").GetProperty("coordinates").GetArrayLength().ShouldBe(2);
        centerline.GetProperty("lengthM").GetDecimal().ShouldBeGreaterThan(0);

        FeatureOf(await CenterlineResponseAsync(reader, "26.49,46.49,26.51,46.51", zoom: 14), caveId)
            .GetProperty("properties").GetProperty("paths").GetInt32().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Map_config_publishes_the_installations_rendering_limits()
    {
        var config = await reader.GetFromJsonAsync<JsonElement>("/api/v1/map/config");

        config.GetProperty("centerlineDetailZoom").GetInt32().ShouldBe(18);
        config.GetProperty("centerlineGateZoom").GetInt32().ShouldBe(12);
        config.GetProperty("centerlineMaxPaths").GetInt32().ShouldBeGreaterThan(0);
        config.GetProperty("centerlineMaxPathsLimit").GetInt32()
            .ShouldBeGreaterThanOrEqualTo(config.GetProperty("centerlineMaxPaths").GetInt32());
        config.GetProperty("clusterMaxZoom").GetInt32().ShouldBe(11);
    }

    // ---- helpers ----

    /// <summary>The whole centerline map response, so the foreign members can be asserted too.</summary>
    private static async Task<JsonElement> CenterlineResponseAsync(
        HttpClient client, string bbox, int zoom, int? detailZoom = null, int? maxPaths = null)
    {
        var query = $"?bbox={bbox}&zoom={zoom}"
            + (detailZoom is null ? string.Empty : $"&detailZoom={detailZoom}")
            + (maxPaths is null ? string.Empty : $"&maxPaths={maxPaths}");
        return await client.GetFromJsonAsync<JsonElement>($"/api/v1/map/cave-centerlines{query}");
    }

    /// <summary>The single feature belonging to one cave; fails the test when it is missing.</summary>
    private static JsonElement FeatureOf(JsonElement response, Guid caveId)
    {
        var features = response.GetProperty("features").EnumerateArray()
            .Where(f => f.GetProperty("properties").GetProperty("caveId").GetGuid() == caveId)
            .ToList();
        features.Count.ShouldBe(1, $"expected exactly one feature for cave {caveId}");
        return features[0];
    }

    /// <summary>
    /// A traverse of four shots with a fan of eight wall shots at each of two interior stations —
    /// 20 components, of which the skeleton should keep one sewn polyline. Written one shot per
    /// component, the way survey tools export.
    /// </summary>
    private static byte[] SplayedSurvey(double lon0, double lat0)
    {
        var shots = new List<string>();
        string Position(double lon, double lat, double z) => FormattableString.Invariant($"[{lon:R},{lat:R},{z:R}]");
        string Station(int i) => Position(lon0 + (0.001 * i), lat0, 700 + i);

        for (var i = 0; i < 4; i++)
        {
            shots.Add(LineFeature(Station(i), Station(i + 1)));
        }

        foreach (var station in new[] { 1, 2 })
        {
            for (var j = 1; j <= 8; j++)
            {
                shots.Add(LineFeature(
                    Station(station),
                    Position(lon0 + (0.001 * station) + (0.0001 * j), lat0 + (0.0001 * j), 700 + station)));
            }
        }

        return Collection(shots);
    }

    /// <summary>Separate short lines that share no station, so none of them can be pruned.</summary>
    private static byte[] DisconnectedLines(double lon0, double lat0, int count)
    {
        var lines = Enumerable.Range(0, count).Select(i => LineFeature(
            FormattableString.Invariant($"[{lon0 + (0.005 * i):R},{lat0:R}]"),
            FormattableString.Invariant($"[{lon0 + (0.005 * i) + 0.001:R},{lat0 + 0.001:R}]")));
        return Collection([.. lines]);
    }

    /// <summary>One shot as a GeoJSON LineString feature, from two already-formatted positions.</summary>
    private static string LineFeature(string from, string to) =>
        "{\"type\":\"Feature\",\"geometry\":{\"type\":\"LineString\",\"coordinates\":["
        + from + "," + to + "]},\"properties\":{}}";

    private static byte[] Collection(IReadOnlyList<string> features) => Encoding.UTF8.GetBytes(
        "{\"type\":\"FeatureCollection\",\"features\":[" + string.Join(",", features) + "]}");

    /// <summary>A Therion-shaped KML: one placemark, a MultiGeometry of shots, one zero-length.</summary>
    private static byte[] KmlSurveyWithDegenerateShot(double lon0, double lat0)
    {
        string Line(string coordinates) => $"<LineString><coordinates>{coordinates}</coordinates></LineString>";
        var a = FormattableString.Invariant($"{lon0:R},{lat0:R},700");
        var b = FormattableString.Invariant($"{lon0 + 0.001:R},{lat0 + 0.001:R},710");
        var c = FormattableString.Invariant($"{lon0 + 0.002:R},{lat0 + 0.002:R},720");
        return Encoding.UTF8.GetBytes(
            $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <kml xmlns="http://www.opengis.net/kml/2.2"><Document><Placemark><name>survey</name>
            <MultiGeometry>{Line($"{a} {b}")}{Line($"{b} {c}")}{Line($"{c} {c}")}</MultiGeometry>
            </Placemark></Document></kml>
            """);
    }

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
