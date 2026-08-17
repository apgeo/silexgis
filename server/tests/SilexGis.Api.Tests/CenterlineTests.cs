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
/// PostGIS-computed geodesic length; the default-centerline rule (the first one uploaded
/// is the cave's shape, promotion goes through PUT); the bbox map layer; and the
/// location-protection rule — a centerline is withheld entirely from callers who may not
/// view the exact location of every protected feature above it, whether the protection
/// root is the cave itself or an area containing it.
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
    private long karstAreaTypeId;

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
            karstAreaTypeId = await db.FeatureTypes
                .Where(t => t.Code == "karst_area").Select(t => t.Id).SingleAsync();
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
        // The centerline is a feature in its own right, anchored to the cave's feature id.
        centerline.GetProperty("caveId").GetGuid().ShouldBe(caveId);
        centerline.GetProperty("id").GetGuid().ShouldNotBe(caveId);
        // Nothing else claimed the cave's shape yet, so the upload takes it.
        centerline.GetProperty("isDefault").GetBoolean().ShouldBeTrue();

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
        var mapFeatures = await MapFeaturesOfAsync(reader, caveId, "25.4,45.4,25.6,45.6");
        mapFeatures.Count.ShouldBe(1);
        mapFeatures[0].GetProperty("properties").GetProperty("name").GetString().ShouldBe("main-gallery");

        // The reader may not delete; the owner may.
        var id = centerline.GetProperty("id").GetGuid();
        (await reader.DeleteAsync($"/api/v1/centerlines/{id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.DeleteAsync($"/api/v1/centerlines/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/centerlines"))
            .GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task The_first_centerline_is_the_default_and_a_put_promotes_another()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);
        var first = await UploadAsync(caveId, "first.geojson", GeoJsonLine(25.5, 45.5));
        var second = await UploadAsync(caveId, "second.geojson", GeoJsonLine(25.52, 45.52));

        first.GetProperty("isDefault").GetBoolean().ShouldBeTrue();
        second.GetProperty("isDefault").GetBoolean().ShouldBeFalse();
        var firstId = first.GetProperty("id").GetGuid();
        var secondId = second.GetProperty("id").GetGuid();

        // A reader may see them but not rename or promote them.
        (await reader.PutAsJsonAsync($"/api/v1/centerlines/{secondId}", Update("Nope", isDefault: false)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A survey model that is not this cave's cannot be attached.
        var wrongModel = await owner.PutAsJsonAsync(
            $"/api/v1/centerlines/{secondId}",
            new { name = "Resurvey", description = (string?)null, surveyModelId = Guid.NewGuid(), isDefault = false });
        wrongModel.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await wrongModel.Content.ReadAsStringAsync()).ShouldContain("centerline.survey_model_invalid");

        // Promotion moves the flag; it is never held by two rows of one cave.
        var promoted = await owner.PutAsJsonAsync(
            $"/api/v1/centerlines/{secondId}", Update("Resurvey 2025", isDefault: true));
        promoted.StatusCode.ShouldBe(HttpStatusCode.OK, await promoted.Content.ReadAsStringAsync());
        var promotedDto = await promoted.Content.ReadFromJsonAsync<JsonElement>();
        promotedDto.GetProperty("isDefault").GetBoolean().ShouldBeTrue();
        promotedDto.GetProperty("name").GetString().ShouldBe("Resurvey 2025");

        var listed = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/centerlines");
        DefaultFlagOf(listed, firstId).ShouldBeFalse();
        DefaultFlagOf(listed, secondId).ShouldBeTrue();

        // Promoting BACK to the earlier survey must work too. One default per cave is a
        // partial unique index checked per statement, so if the demotion and the promotion
        // are written in id order rather than in intent order, this direction — and only
        // this direction, since ids are time-ordered — collides on the index.
        var promotedBack = await owner.PutAsJsonAsync(
            $"/api/v1/centerlines/{firstId}", Update("Original survey", isDefault: true));
        promotedBack.StatusCode.ShouldBe(HttpStatusCode.OK, await promotedBack.Content.ReadAsStringAsync());

        listed = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/centerlines");
        DefaultFlagOf(listed, firstId).ShouldBeTrue();
        DefaultFlagOf(listed, secondId).ShouldBeFalse();

        // The flag is only ever moved, never cleared: a cave with centerlines always has a shape.
        var cleared = await owner.PutAsJsonAsync(
            $"/api/v1/centerlines/{firstId}", Update("Original survey", isDefault: false));
        cleared.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await cleared.Content.ReadAsStringAsync()).ShouldContain("centerline.default_required");
    }

    [Fact]
    public async Task Deleting_the_default_centerline_hands_the_flag_to_the_survivor()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);
        var first = await UploadAsync(caveId, "first.geojson", GeoJsonLine(25.54, 45.54));
        var second = await UploadAsync(caveId, "second.geojson", GeoJsonLine(25.56, 45.56));
        var firstId = first.GetProperty("id").GetGuid();
        var secondId = second.GetProperty("id").GetGuid();

        (await owner.DeleteAsync($"/api/v1/centerlines/{firstId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var remaining = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/centerlines");
        remaining.GetArrayLength().ShouldBe(1);
        remaining[0].GetProperty("id").GetGuid().ShouldBe(secondId);
        remaining[0].GetProperty("isDefault").GetBoolean().ShouldBeTrue();

        // The deleted row is gone from every read path, and deleting it twice is not found.
        (await owner.DeleteAsync($"/api/v1/centerlines/{firstId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Centerlines_of_protected_caves_are_withheld_without_exact_location()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: true);
        // The file name becomes the centerline's name, so make it searchable and unique.
        var secretName = $"secret-{Guid.NewGuid():N}"[..20];
        var created = await UploadAsync(caveId, $"{secretName}.geojson", GeoJsonLine(25.5, 45.5));
        var centerlineId = created.GetProperty("id").GetGuid();

        // Empty list and no map feature for the plain reader; the owner sees both.
        (await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/centerlines"))
            .GetArrayLength().ShouldBe(0);
        (await MapFeaturesOfAsync(reader, caveId, "25.4,45.4,25.6,45.6")).Count.ShouldBe(0);
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/centerlines"))
            .GetArrayLength().ShouldBe(1);
        (await MapFeaturesOfAsync(owner, caveId, "25.4,45.4,25.6,45.6")).Count.ShouldBe(1);

        // A withheld centerline is not disclosed by the write paths either: 404, never 403.
        (await reader.DeleteAsync($"/api/v1/centerlines/{centerlineId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.PutAsJsonAsync($"/api/v1/centerlines/{centerlineId}", Update("Nope", isDefault: false)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Search must not name it either: a hit would disclose that a cave whose position
        // is guarded has a survey at all, which every other read path spends code to hide.
        (await SearchFindsCenterlineAsync(reader, secretName, centerlineId)).ShouldBeFalse();
        (await SearchFindsCenterlineAsync(owner, secretName, centerlineId)).ShouldBeTrue();

        // An explicit ViewExactLocation grant on the protected root flips them visible.
        await GrantExactViewAsync(caveId);

        (await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/centerlines"))
            .GetArrayLength().ShouldBe(1);
        (await MapFeaturesOfAsync(reader, caveId, "25.4,45.4,25.6,45.6")).Count.ShouldBe(1);
        (await SearchFindsCenterlineAsync(reader, secretName, centerlineId)).ShouldBeTrue();
    }

    private static async Task<bool> SearchFindsCenterlineAsync(HttpClient client, string term, Guid centerlineId)
    {
        var results = await client.GetFromJsonAsync<JsonElement>($"/api/v1/search?q={term}");
        return results.GetProperty("features").EnumerateArray()
            .Any(f => f.GetProperty("id").GetGuid() == centerlineId);
    }

    [Fact]
    public async Task A_protected_parent_area_withholds_the_centerlines_of_the_caves_inside_it()
    {
        // Protection is a property of the ancestry, not of the cave row: an unprotected cave
        // inside a protected karst area is protected, and only a grant on the AREA — the
        // actual protection root — can lift it.
        var areaId = await CreateProtectedAreaAsync();
        var caveId = await CreateCaveAsync(
            visibility: "authenticated", locationProtected: false, parentId: areaId);
        const string bbox = "26.59,46.59,26.61,46.61";
        var created = await UploadAsync(caveId, "inside-area.geojson", GeoJsonLine(26.600, 46.600));
        var centerlineId = created.GetProperty("id").GetGuid();

        // The cave itself is readable and its protection is inherited, not stored on the row.
        var cave = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}");
        cave.GetProperty("locationProtected").GetBoolean().ShouldBeFalse();
        cave.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();

        (await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/centerlines"))
            .GetArrayLength().ShouldBe(0);
        (await MapFeaturesOfAsync(reader, caveId, bbox)).Count.ShouldBe(0);
        (await reader.DeleteAsync($"/api/v1/centerlines/{centerlineId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A grant on the cave is not a grant on the root above it, so it reveals nothing.
        await GrantExactViewAsync(caveId);
        (await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/centerlines"))
            .GetArrayLength().ShouldBe(0);
        (await MapFeaturesOfAsync(reader, caveId, bbox)).Count.ShouldBe(0);

        // The grant on the area does.
        await GrantExactViewAsync(areaId);
        (await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/centerlines"))
            .GetArrayLength().ShouldBe(1);
        (await MapFeaturesOfAsync(reader, caveId, bbox)).Count.ShouldBe(1);

        // The row owner never lost sight of their own cave under a protected area.
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/centerlines"))
            .GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Deleting_a_cave_takes_its_centerlines_out_of_every_read_path()
    {
        // Feature deletion is soft and stamps the whole containment subtree; a centerline is
        // a child of its cave, so it must vanish with it rather than linger on the overlay.
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);
        const string bbox = "26.69,46.69,26.71,46.71";
        var created = await UploadAsync(caveId, "doomed.geojson", GeoJsonLine(26.700, 46.700));
        var centerlineId = created.GetProperty("id").GetGuid();
        (await MapFeaturesOfAsync(owner, caveId, bbox)).Count.ShouldBe(1);

        (await owner.DeleteAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await owner.GetAsync($"/api/v1/caves/{caveId}/centerlines")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.DeleteAsync($"/api/v1/centerlines/{centerlineId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await MapFeaturesOfAsync(owner, caveId, bbox)).Count.ShouldBe(0);
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
    public async Task The_overlay_is_flat_unless_altitudes_are_asked_for_and_carries_them_when_they_are()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);
        using var form = BuildForm("with-altitudes.geojson", SplayedSurvey(26.800, 46.800));
        (await owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", form))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        const string bbox = "26.79,46.79,26.81,46.81";

        // The 2D map does not ask, and gets exactly what it always got: two ordinates per
        // position and no altitude reporting anywhere in the payload.
        var flat = await CenterlineResponseAsync(reader, bbox, zoom: 19);
        var flatFeature = FeatureOf(flat, caveId);
        PositionsOf(flatFeature).ShouldAllBe(p => p.GetArrayLength() == 2);
        flatFeature.GetProperty("properties").TryGetProperty("hasZ", out _).ShouldBeFalse();
        flat.GetProperty("flatCount").GetInt32().ShouldBe(0);

        // Opting in keeps the survey's altitudes: the fixture climbs 700 m → 704 m across the
        // traverse, so the values are the surveyed ones rather than a zero fill.
        var withZ = await CenterlineResponseAsync(reader, bbox, zoom: 19, z: true);
        var withZFeature = FeatureOf(withZ, caveId);
        var positions = PositionsOf(withZFeature);
        positions.ShouldAllBe(p => p.GetArrayLength() == 3);
        var altitudes = positions.Select(p => p[2].GetDouble()).ToList();
        altitudes.ShouldAllBe(a => a >= 700 && a <= 704);
        altitudes.ShouldContain(a => a > 700);
        withZFeature.GetProperty("properties").GetProperty("hasZ").GetBoolean().ShouldBeTrue();
        withZ.GetProperty("flatCount").GetInt32().ShouldBe(0);

        // Same request, same components either way: opting in must not change what is drawn.
        withZFeature.GetProperty("properties").GetProperty("paths").GetInt32()
            .ShouldBe(flatFeature.GetProperty("properties").GetProperty("paths").GetInt32());
    }

    [Fact]
    public async Task A_viewport_that_cuts_the_survey_still_returns_surveyed_altitudes_and_no_nan()
    {
        // The 2D clip is a box clip, which invents vertices on the boundary; fed 3D input it
        // would write NaN into their altitudes and the response would stop being valid JSON.
        // The altitude-preserving branch takes whole components instead, so every altitude here
        // is one that was surveyed — and the response has to parse at all, which it only does if
        // no NaN reached it (GetFromJsonAsync throws otherwise).
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);
        using var form = BuildForm("cut.geojson", SplayedSurvey(26.900, 46.900));
        (await owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", form))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // The traverse runs 26.900 → 26.904; this stops short of its far end, so components
        // cross the viewport edge and the whole-geometry fast path cannot be taken.
        var cut = await CenterlineResponseAsync(reader, "26.8995,46.8995,26.9025,46.9015", zoom: 19, z: true);

        var feature = FeatureOf(cut, caveId);
        feature.GetProperty("properties").GetProperty("hasZ").GetBoolean().ShouldBeTrue();
        // 20 components go in; the last traverse shot lies wholly east of the viewport and is the
        // only one dropped. Anything else would mean the filter never ran, or that it cut a shot.
        feature.GetProperty("properties").GetProperty("paths").GetInt32().ShouldBe(19);
        var altitudes = PositionsOf(feature).Select(p => p[2].GetDouble()).ToList();
        altitudes.ShouldNotBeEmpty();
        altitudes.ShouldAllBe(a => double.IsFinite(a) && a >= 700 && a <= 704);
        cut.GetProperty("flatCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task The_reported_top_altitude_describes_the_whole_survey_not_the_part_in_view()
    {
        // A client drawing a survey against a globe has to know where the cave meets the ground,
        // and the served geometry cannot be asked: at detail zoom it is cut to the viewport, so
        // its own highest position changes every time the viewer pans. A figure read off the
        // payload would therefore slide the whole cave up and down as it was panned across.
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);
        using var form = BuildForm("anchored.geojson", SplayedSurvey(26.910, 46.910));
        (await owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", form))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // The traverse climbs 700 m → 704 m across four shots; with all of it in view the reported
        // top is the top of the survey.
        var whole = FeatureOf(
            await CenterlineResponseAsync(reader, "26.90,46.90,26.92,46.92", zoom: 19, z: true), caveId);
        whole.GetProperty("properties").GetProperty("topAltitudeM").GetDouble().ShouldBe(704, 0.001);

        // The same viewport shape the clipping test uses: it stops short of the traverse's far end,
        // so the highest station is not in the response at all. The reported top must not follow.
        var cut = FeatureOf(
            await CenterlineResponseAsync(reader, "26.9095,46.9095,26.9125,46.9115", zoom: 19, z: true), caveId);
        PositionsOf(cut).Select(p => p[2].GetDouble()).Max().ShouldBeLessThan(704);
        cut.GetProperty("properties").GetProperty("topAltitudeM").GetDouble().ShouldBe(704, 0.001);

        // Nothing to anchor against is reported as nothing rather than as a sea-level anchor: a row
        // served from the flat stored skeleton carries no altitudes at all, and neither does a
        // request that never asked for them.
        FeatureOf(await CenterlineResponseAsync(reader, "26.90,46.90,26.92,46.92", zoom: 14, z: true), caveId)
            .GetProperty("properties").TryGetProperty("topAltitudeM", out _).ShouldBeFalse();
        FeatureOf(await CenterlineResponseAsync(reader, "26.90,46.90,26.92,46.92", zoom: 19), caveId)
            .GetProperty("properties").TryGetProperty("topAltitudeM", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_row_that_can_only_be_served_flat_is_still_served_and_says_so()
    {
        // The stored display skeleton is a 2D column, so there is no altitude to give at overview
        // zooms — nor when a detail request falls back to the skeleton on the path budget. The
        // row is served anyway (blanking the overlay would be worse) and reports what it carries.
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);
        using var form = BuildForm("skeletal.geojson", SplayedSurvey(26.950, 46.950));
        (await owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", form))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        const string bbox = "26.94,46.94,26.96,46.96";

        var overview = await CenterlineResponseAsync(reader, bbox, zoom: 14, z: true);
        var skeleton = FeatureOf(overview, caveId);
        skeleton.GetProperty("properties").GetProperty("hasZ").GetBoolean().ShouldBeFalse();
        PositionsOf(skeleton).ShouldAllBe(p => p.GetArrayLength() == 2);
        overview.GetProperty("flatCount").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        // Reported as flat, not as withheld: the caller can still draw it.
        overview.GetProperty("withheldCount").GetInt32().ShouldBe(0);

        var degraded = await CenterlineResponseAsync(reader, bbox, zoom: 19, maxPaths: 10, z: true);
        var fallback = FeatureOf(degraded, caveId);
        fallback.GetProperty("properties").GetProperty("detail").GetBoolean().ShouldBeFalse();
        fallback.GetProperty("properties").GetProperty("hasZ").GetBoolean().ShouldBeFalse();
        degraded.GetProperty("flatCount").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Asking_for_altitudes_does_not_lift_the_exact_location_gate()
    {
        // A centerline IS the cave's exact position, so a protected cave must stay absent and
        // uncounted on the altitude-preserving branch exactly as it is on the flat one — a new
        // query parameter is the classic way for a protection rule to be routed around.
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: true);
        using var form = BuildForm("guarded.geojson", SplayedSurvey(26.970, 46.970));
        (await owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", form))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        const string bbox = "26.96,46.96,26.98,46.98";

        foreach (var zoom in new[] { 8, 14, 19 })
        {
            var response = await CenterlineResponseAsync(reader, bbox, zoom, z: true);
            response.GetProperty("features").EnumerateArray()
                .ShouldNotContain(f => f.GetProperty("properties").GetProperty("caveId").GetGuid() == caveId);
            response.GetProperty("withheldCount").GetInt32().ShouldBe(0);
            response.GetProperty("flatCount").GetInt32().ShouldBe(0);
        }

        // The grant is what reveals it, on this branch too — and then with its altitudes.
        await GrantExactViewAsync(caveId);
        var granted = await CenterlineResponseAsync(reader, bbox, zoom: 19, z: true);
        FeatureOf(granted, caveId).GetProperty("properties").GetProperty("hasZ").GetBoolean().ShouldBeTrue();
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

    [Fact]
    public async Task Map_config_publishes_no_elevation_model_for_an_installation_that_has_none()
    {
        // "An installation that has none" is a state this test has to put the installation into,
        // not one it may assume. Which pyramid a scene draws is a property of the whole
        // installation rather than of any one caller — one table, one chosen row — and the
        // classes that cover choosing one leave their choice standing when they finish. Sharing
        // a database with them, this test would otherwise assert against whatever was chosen
        // last, and pass or fail on the order the classes happened to run in.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.TerrainBuilds.ExecuteDeleteAsync();
        }

        // The shipped state, asserted over real HTTP. An installation that has not baked a tile
        // pyramid publishes null rather than an empty object, and the 3D view then draws the
        // smooth reference ellipsoid — which needs no elevation server, no download and no
        // pre-baking. This is the guard on "if an operator does nothing, nothing changes".
        var config = await reader.GetFromJsonAsync<JsonElement>("/api/v1/map/config");

        config.GetProperty("terrain").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // ---- helpers ----

    /// <summary>The whole centerline map response, so the foreign members can be asserted too.</summary>
    private static async Task<JsonElement> CenterlineResponseAsync(
        HttpClient client, string bbox, int zoom, int? detailZoom = null, int? maxPaths = null, bool? z = null)
    {
        var query = $"?bbox={bbox}&zoom={zoom}"
            + (detailZoom is null ? string.Empty : $"&detailZoom={detailZoom}")
            + (maxPaths is null ? string.Empty : $"&maxPaths={maxPaths}")
            + (z is null ? string.Empty : $"&z={(z.Value ? "true" : "false")}");
        return await client.GetFromJsonAsync<JsonElement>($"/api/v1/map/cave-centerlines{query}");
    }

    /// <summary>
    /// Every position of one feature's geometry, whichever line shape PostGIS produced — a
    /// single surviving component comes back as a LineString, several as a MultiLineString.
    /// </summary>
    private static List<JsonElement> PositionsOf(JsonElement feature)
    {
        var geometry = feature.GetProperty("geometry");
        var coordinates = geometry.GetProperty("coordinates");
        return geometry.GetProperty("type").GetString() == "LineString"
            ? [.. coordinates.EnumerateArray()]
            : [.. coordinates.EnumerateArray().SelectMany(line => line.EnumerateArray())];
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

    /// <summary>The default flag of one listed centerline, found by id rather than position.</summary>
    private static bool DefaultFlagOf(JsonElement list, Guid centerlineId)
    {
        var row = list.EnumerateArray().SingleOrDefault(x => x.GetProperty("id").GetGuid() == centerlineId);
        row.ValueKind.ShouldBe(JsonValueKind.Object, $"centerline {centerlineId} missing from the list");
        return row.GetProperty("isDefault").GetBoolean();
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
    private static async Task<List<JsonElement>> MapFeaturesOfAsync(HttpClient client, Guid caveId, string bbox)
    {
        var map = await client.GetFromJsonAsync<JsonElement>($"/api/v1/map/cave-centerlines?bbox={bbox}");
        return [.. map.GetProperty("features").EnumerateArray()
            .Where(f => f.GetProperty("properties").GetProperty("caveId").GetGuid() == caveId)];
    }

    /// <summary>Uploads one centerline and returns the created DTO.</summary>
    private async Task<JsonElement> UploadAsync(Guid caveId, string fileName, byte[] bytes)
    {
        using var form = BuildForm(fileName, bytes);
        var response = await owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", form);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static object Update(string name, bool isDefault) =>
        new { name, description = (string?)null, surveyModelId = (Guid?)null, isDefault };

    /// <summary>Grants the reader Read + ViewExactLocation on one feature, whatever its kind.</summary>
    private async Task GrantExactViewAsync(Guid featureId)
    {
        var grant = await owner.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = readerId,
                    effect = "allow",
                    actions = "read, viewExactLocation",
                    scopeKind = "object",
                },
            },
        });
        grant.StatusCode.ShouldBe(HttpStatusCode.OK, await grant.Content.ReadAsStringAsync());
    }

    /// <summary>A location-protected karst area: a protection root that is not a cave.</summary>
    private async Task<Guid> CreateProtectedAreaAsync()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Protected Karst {Guid.NewGuid():N}"[..30],
            featureTypeId = karstAreaTypeId,
            geometry = (object?)null,
            description = (string?)null,
            locationProtected = true,
            visibility = "authenticated",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCaveAsync(string visibility, bool locationProtected, Guid? parentId = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Centerline Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
            parentId,
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

    /// <summary>A single-shot GeoJSON line starting at the given position.</summary>
    private static byte[] GeoJsonLine(double lon, double lat) => Collection(
    [
        LineFeature(
            FormattableString.Invariant($"[{lon:R},{lat:R}]"),
            FormattableString.Invariant($"[{lon + 0.001:R},{lat + 0.001:R}]")),
    ]);

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
