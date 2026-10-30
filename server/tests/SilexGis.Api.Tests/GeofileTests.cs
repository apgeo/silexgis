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
public sealed class GeofileTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string WorldBbox = "-180,-90,180,90";

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient editor = null!;
    private HttpClient outsider = null!; // Viewer (regular user), unrelated — Editors read everything now
    private HttpClient viewer = null!;
    private Guid editorId;
    private Guid viewerId;

    /// <summary>
    /// Token woven into every name this instance seeds. The whole suite shares one
    /// database, so search-scoped export assertions must not be able to see another
    /// class's rows (xUnit builds one instance per test, so the token is per test).
    /// </summary>
    private string tag = null!;

    public GeofileTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
        });
    }

    public async ValueTask InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        editorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"gf-editor-{tag}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"gf-out-{tag}@t.local");
        viewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"gf-view-{tag}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"gf-editor-{tag}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"gf-out-{tag}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"gf-view-{tag}@t.local");
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
        // Native features → zipped-shapefile export → import as a geofile.
        var pointName = $"Shp Point {tag}";
        var lineName = $"Shp Line {tag}";
        var genericTypeId = await FeatureTypeIdAsync("generic");
        await CreateFeatureAsync(pointName, genericTypeId, new { type = "Point", coordinates = new[] { 25.71, 45.71 } });
        await CreateFeatureAsync(lineName, genericTypeId, new
        {
            type = "LineString",
            coordinates = new[] { new[] { 25.72, 45.72 }, new[] { 25.73, 45.73 } },
        });

        var export = await editor.GetAsync($"/api/v1/export/features?format=shapefile&search={tag}");
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
        names.ShouldBe([pointName, lineName], ignoreOrder: true);
    }

    [Fact]
    public async Task Cave_exports_obfuscate_protected_locations_and_filter_visibility()
    {
        var caveTypeId = await CaveTypeIdAsync();
        var entranceTypeId = await EntranceTypeIdAsync();

        var openName = $"Export Open Cave {tag}";
        var protectedName = $"Export Protected Cave {tag}";
        var privateName = $"Export Private Cave {tag}";

        var openCave = await CreateCaveAsync(openName, caveTypeId, locationProtected: false);
        var protectedCave = await CreateCaveAsync(protectedName, caveTypeId, locationProtected: true);
        var privateCave = await CreateCaveAsync(privateName, caveTypeId, locationProtected: false, visibility: "private");

        const double exactLon = 25.44721;
        const double exactLat = 45.53127;
        foreach (var caveId in new[] { openCave, protectedCave, privateCave })
        {
            await CreateEntranceAsync(caveId, entranceTypeId, exactLon, exactLat);
        }

        // Viewer GeoJSON export: private cave absent, protected cave snapped to the grid.
        var response = await viewer.GetAsync($"/api/v1/export/caves?format=geojson&search={tag}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var geojson = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var features = geojson.GetProperty("features").EnumerateArray().ToList();

        var names = features.Select(f => f.GetProperty("properties").GetProperty("name").GetString()).ToList();
        names.ShouldBe([openName, protectedName], ignoreOrder: true);

        var cell = LocationProtection.CellDegrees(5000);
        foreach (var feature in features)
        {
            var props = feature.GetProperty("properties");
            var coords = feature.GetProperty("geometry").GetProperty("coordinates");
            var lon = coords[0].GetDouble();
            var lat = coords[1].GetDouble();

            if (props.GetProperty("name").GetString() == protectedName)
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
            $"/api/v1/export/caves?format=geojson&search={tag}")).Content.ReadAsStringAsync()).RootElement;
        var ownerProtected = ownerExport.GetProperty("features").EnumerateArray()
            .Single(f => f.GetProperty("properties").GetProperty("name").GetString() == protectedName);
        ownerProtected.GetProperty("geometry").GetProperty("coordinates")[0].GetDouble().ShouldBe(exactLon, 1e-9);

        // CSV and GPX flavors materialize with the right shapes.
        var csv = await viewer.GetAsync($"/api/v1/export/caves?format=csv&search={tag}");
        csv.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        (await csv.Content.ReadAsStringAsync()).ShouldContain(openName);

        var gpxResponse = await viewer.GetAsync($"/api/v1/export/caves?format=gpx&search={tag}");
        var gpxDoc = XDocument.Parse(await gpxResponse.Content.ReadAsStringAsync());
        gpxDoc.Descendants().Count(e => e.Name.LocalName == "wpt").ShouldBe(2);

        (await viewer.GetAsync("/api/v1/export/caves?format=dwg"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// A bbox export finds a protected cave where it is published, not where it is. The file
    /// prints the snapped point for a caller who may not place the cave, so the honest box test
    /// is against that same point: membership then moves only in whole grid steps, and a
    /// caller-drawn box cannot be bisected against the true position — which is what a box over
    /// the stored coordinate allowed, one edge-crossing at a time. Both directions are pinned,
    /// and then reversed for the owner, for whom the true point governs.
    /// </summary>
    [Fact]
    public async Task A_bbox_export_finds_a_protected_cave_where_it_is_published_not_where_it_is()
    {
        // Off the lattice by 0.4 of a cell on each axis, so a small box can hold exactly one of
        // the two points. 0.4 rounds down: the published point is at the lattice indices.
        var cell = LocationProtection.CellDegrees(5000);
        var lonIndex = Math.Round(17.0 / cell);
        var latIndex = Math.Round(44.0 / cell);
        var lonTrue = (lonIndex + 0.4) * cell;
        var latTrue = (latIndex + 0.4) * cell;

        var name = $"Export Grid Cave {tag}";
        var caveId = await CreateCaveAsync(name, await CaveTypeIdAsync(), locationProtected: true);
        await CreateEntranceAsync(caveId, await EntranceTypeIdAsync(), lonTrue, latTrue);

        string BoxAround(double lon, double lat) => FormattableString.Invariant(
            $"{lon - 0.15 * cell},{lat - 0.15 * cell},{lon + 0.15 * cell},{lat + 0.15 * cell}");

        (await ExportedNamesAsync(viewer, BoxAround(lonTrue, latTrue)))
            .ShouldNotContain(name, "membership followed the true position of a cave the caller may not place");
        (await ExportedNamesAsync(viewer, BoxAround(lonIndex * cell, latIndex * cell)))
            .ShouldContain(name);

        // The owner places the cave exactly, so for them the same two boxes reverse — the rule
        // is about placement, not about boxes.
        (await ExportedNamesAsync(editor, BoxAround(lonTrue, latTrue))).ShouldContain(name);
        (await ExportedNamesAsync(editor, BoxAround(lonIndex * cell, latIndex * cell)))
            .ShouldNotContain(name);
    }

    private async Task<List<string?>> ExportedNamesAsync(HttpClient client, string bbox)
    {
        var response = await client.GetAsync(
            $"/api/v1/export/caves?format=geojson&search={tag}&bbox={bbox}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return [.. body.GetProperty("features").EnumerateArray()
            .Select(f => f.GetProperty("properties").GetProperty("name").GetString())];
    }

    /// <summary>
    /// The feature export applies the same protection rule as the map: a protected point
    /// leaves snapped and flagged, and protected geometry that cannot be snapped without
    /// disclosing shape or extent does not leave at all.
    /// </summary>
    [Fact]
    public async Task Feature_exports_snap_protected_points_and_drop_protected_extended_geometry()
    {
        var openName = $"Export Open Point {tag}";
        var protectedPointName = $"Export Protected Point {tag}";
        var protectedLineName = $"Export Protected Line {tag}";

        var pointTypeId = await FeatureTypeIdAsync("sinkhole");
        var lineTypeId = await FeatureTypeIdAsync("fracture_line");

        const double exactLon = 25.61233;
        const double exactLat = 45.61377;
        await CreateFeatureAsync(openName, pointTypeId, new { type = "Point", coordinates = new[] { exactLon, exactLat } });
        await CreateFeatureAsync(
            protectedPointName,
            pointTypeId,
            new { type = "Point", coordinates = new[] { exactLon, exactLat } },
            locationProtected: true);
        await CreateFeatureAsync(
            protectedLineName,
            lineTypeId,
            new
            {
                type = "LineString",
                coordinates = new[] { new[] { 25.62, 45.62 }, new[] { 25.63, 45.63 } },
            },
            locationProtected: true);

        var viewerExport = JsonDocument.Parse(await (await viewer.GetAsync(
            $"/api/v1/export/features?format=geojson&search={tag}")).Content.ReadAsStringAsync()).RootElement;
        var rows = viewerExport.GetProperty("features").EnumerateArray().ToList();

        // The protected line is absent entirely — it is never degraded, only dropped.
        rows.Select(f => f.GetProperty("properties").GetProperty("name").GetString())
            .ShouldBe([openName, protectedPointName], ignoreOrder: true);

        var cell = LocationProtection.CellDegrees(5000);
        var snapped = rows.Single(f => f.GetProperty("properties").GetProperty("name").GetString() == protectedPointName);
        snapped.GetProperty("properties").GetProperty("approximate").GetString().ShouldBe("yes");
        var snappedLon = snapped.GetProperty("geometry").GetProperty("coordinates")[0].GetDouble();
        snappedLon.ShouldNotBe(exactLon);
        (snappedLon / cell).ShouldBe(Math.Round(snappedLon / cell), 1e-6, "obfuscated lon must sit on the grid");

        var open = rows.Single(f => f.GetProperty("properties").GetProperty("name").GetString() == openName);
        open.GetProperty("geometry").GetProperty("coordinates")[0].GetDouble().ShouldBe(exactLon, 1e-9);

        // The owner keeps every row, exactly.
        var ownerExport = JsonDocument.Parse(await (await editor.GetAsync(
            $"/api/v1/export/features?format=geojson&search={tag}")).Content.ReadAsStringAsync()).RootElement;
        var ownerRows = ownerExport.GetProperty("features").EnumerateArray().ToList();
        ownerRows.Select(f => f.GetProperty("properties").GetProperty("name").GetString())
            .ShouldBe([openName, protectedPointName, protectedLineName], ignoreOrder: true);
        ownerRows
            .Single(f => f.GetProperty("properties").GetProperty("name").GetString() == protectedLineName)
            .GetProperty("geometry").GetProperty("type").GetString().ShouldBe("LineString");
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
            cavingGroupId = (Guid?)null,
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

    /// <summary>
    /// The vector exports name the record each row came from, so a file can be joined back to the
    /// installation it left. Read in every format that has a column to put it in, and beside a
    /// row whose position was snapped, since that is the row somebody would most want to find
    /// again.
    /// </summary>
    [Fact]
    public async Task Cave_and_feature_exports_carry_the_id_of_the_record_in_every_format_with_columns()
    {
        var caveTypeId = await CaveTypeIdAsync();
        var entranceTypeId = await EntranceTypeIdAsync();
        var openCave = await CreateCaveAsync($"Id Open Cave {tag}", caveTypeId, locationProtected: false);
        var protectedCave = await CreateCaveAsync($"Id Protected Cave {tag}", caveTypeId, locationProtected: true);
        foreach (var caveId in new[] { openCave, protectedCave })
        {
            await CreateEntranceAsync(caveId, entranceTypeId, 25.44721, 45.53127);
        }

        var sinkholeTypeId = await FeatureTypeIdAsync("sinkhole");
        var point = await CreateFeatureAsync(
            $"Id Point {tag}", sinkholeTypeId, new { type = "Point", coordinates = new[] { 25.61233, 45.61377 } });

        var cavesJson = JsonDocument.Parse(await (await viewer.GetAsync(
            $"/api/v1/export/caves?format=geojson&search={tag}")).Content.ReadAsStringAsync()).RootElement;
        cavesJson.GetProperty("features").EnumerateArray()
            .Select(f => f.GetProperty("properties").GetProperty("id").GetString())
            .ShouldBe([openCave.ToString(), protectedCave.ToString()], ignoreOrder: true);

        var featuresJson = JsonDocument.Parse(await (await viewer.GetAsync(
            $"/api/v1/export/features?format=geojson&search={tag}")).Content.ReadAsStringAsync()).RootElement;
        featuresJson.GetProperty("features").EnumerateArray()
            .Single(f => f.GetProperty("properties").GetProperty("name").GetString() == $"Id Point {tag}")
            .GetProperty("properties").GetProperty("id").GetString().ShouldBe(point.ToString());

        // Spreadsheets get the same column, and it is the first one: the one a join is made on.
        var csv = await (await viewer.GetAsync($"/api/v1/export/caves?format=csv&search={tag}"))
            .Content.ReadAsStringAsync();
        csv.Split('\n')[0].ShouldContain("id");
        csv.ShouldContain(openCave.ToString());
        csv.ShouldContain(protectedCave.ToString());

        // A zipped shapefile keeps it through a round trip: the id comes back as an attribute of
        // the imported rows.
        var zip = await (await viewer.GetAsync($"/api/v1/export/features?format=shapefile&search={tag}"))
            .Content.ReadAsByteArrayAsync();
        var geofile = await UploadAsync(editor, "ids.zip", zip);
        var geofileId = geofile.GetProperty("id").GetGuid();
        (await WaitForImportAsync(editor, geofileId)).GetProperty("importStatus").GetString().ShouldBe("imported");
        // Read back through the geofile's own export: the map layer stamps its own row id over a
        // property of that name, so it cannot show what the file held.
        var imported = await GetJsonAsync(editor, $"/api/v1/geofiles/{geofileId}/export?format=geojson");
        imported.GetProperty("features").EnumerateArray()
            .Select(f => f.GetProperty("properties").GetProperty("id").GetString())
            .ShouldContain(point.ToString());
    }

    /// <summary>
    /// A file outlives every permission check that produced it, so each vector export is
    /// recorded: who took it, how many rows, and exactly which rows left at the protection grid
    /// and which were kept out of the file. Paired with the rows that did not need a treatment,
    /// which must not be listed, and with a refusal, which must leave no record of a file.
    /// </summary>
    [Fact]
    public async Task Each_vector_export_leaves_an_audit_row_naming_the_rows_it_snapped_or_withheld()
    {
        var caveTypeId = await CaveTypeIdAsync();
        var entranceTypeId = await EntranceTypeIdAsync();
        var openCave = await CreateCaveAsync($"Audit Open Cave {tag}", caveTypeId, locationProtected: false);
        var protectedCave = await CreateCaveAsync($"Audit Protected Cave {tag}", caveTypeId, locationProtected: true);
        const double exactLon = 25.44721;
        const double exactLat = 45.53127;
        foreach (var caveId in new[] { openCave, protectedCave })
        {
            await CreateEntranceAsync(caveId, entranceTypeId, exactLon, exactLat);
        }

        var sinkholeTypeId = await FeatureTypeIdAsync("sinkhole");
        var lineTypeId = await FeatureTypeIdAsync("fracture_line");
        var openPoint = await CreateFeatureAsync(
            $"Audit Open Point {tag}", sinkholeTypeId, new { type = "Point", coordinates = new[] { 25.61233, 45.61377 } });
        var protectedPoint = await CreateFeatureAsync(
            $"Audit Protected Point {tag}",
            sinkholeTypeId,
            new { type = "Point", coordinates = new[] { 25.61233, 45.61377 } },
            locationProtected: true);
        var protectedLine = await CreateFeatureAsync(
            $"Audit Protected Line {tag}",
            lineTypeId,
            new { type = "LineString", coordinates = new[] { new[] { 25.62, 45.62 }, new[] { 25.63, 45.63 } } },
            locationProtected: true);

        (await ExportAuditRowsAsync()).ShouldBeEmpty("nothing has been exported yet");

        // Caves.
        (await viewer.GetAsync($"/api/v1/export/caves?format=geojson&search={tag}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var caves = (await ExportAuditRowsAsync()).Single();
        caves.GetProperty("layer").GetString().ShouldBe("caves");
        caves.GetProperty("format").GetString().ShouldBe("geojson");
        caves.GetProperty("exportedCount").GetInt32().ShouldBe(2);
        caves.GetProperty("snappedIds").EnumerateArray().Select(i => i.GetString())
            .ShouldBe([protectedCave.ToString()]);
        caves.GetProperty("withheldCount").GetInt32().ShouldBe(0);

        // Features: the snapped point and the withheld line are each named, the open one is not.
        // The search matches the two caves too (a cave is a feature), so the protected cave is
        // snapped here as well and there are four rows in the file.
        (await viewer.GetAsync($"/api/v1/export/features?format=csv&search={tag}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var features = (await ExportAuditRowsAsync()).Single(r => r.GetProperty("layer").GetString() == "features");
        features.GetProperty("format").GetString().ShouldBe("csv");
        features.GetProperty("exportedCount").GetInt32().ShouldBe(4);
        features.GetProperty("snappedIds").EnumerateArray().Select(i => i.GetString())
            .ShouldBe([protectedCave.ToString(), protectedPoint.ToString()], ignoreOrder: true);
        features.GetProperty("withheldIds").EnumerateArray().Select(i => i.GetString())
            .ShouldBe([protectedLine.ToString()]);
        features.ToString().ShouldNotContain(openPoint.ToString());
        features.ToString().ShouldNotContain(openCave.ToString());

        // The owner places everything exactly, so nothing of theirs is snapped or withheld.
        (await editor.GetAsync($"/api/v1/export/features?format=geojson&search={tag}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var ownerRow = (await ExportAuditRowsAsync(editorId)).Single();
        ownerRow.GetProperty("exportedCount").GetInt32().ShouldBe(5);
        ownerRow.GetProperty("snappedCount").GetInt32().ShouldBe(0);
        ownerRow.GetProperty("withheldCount").GetInt32().ShouldBe(0);

        // The record holds identifiers and counts, never a position.
        foreach (var row in await ExportAuditRowsAsync())
        {
            var text = row.ToString();
            text.ShouldNotContain(exactLon.ToString(System.Globalization.CultureInfo.InvariantCulture));
            text.ShouldNotContain(exactLat.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // Geofiles, as a list and one at a time.
        var geofile = await UploadAsync(editor, "audit.wkt", Encoding.UTF8.GetBytes("POINT (25.9 45.9)\nPOINT (25.91 45.91)"));
        var geofileId = geofile.GetProperty("id").GetGuid();
        (await WaitForImportAsync(editor, geofileId)).GetProperty("featureCount").GetInt32().ShouldBe(2);

        // Private to its owner: the viewer is refused, and a refusal is not an export.
        var before = (await ExportAuditRowsAsync()).Count;
        (await viewer.GetAsync($"/api/v1/geofiles/{geofileId}/export?format=geojson"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await viewer.GetAsync("/api/v1/export/caves?format=dwg")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ExportAuditRowsAsync()).Count.ShouldBe(before);
        using (var anonymous = factory.CreateClient())
        {
            (await anonymous.GetAsync($"/api/v1/geofiles/{geofileId}/export?format=geojson"))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await anonymous.GetAsync("/api/v1/export/caves?format=geojson"))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        (await ExportAuditRowsAsync()).Count.ShouldBe(before);

        (await editor.PutAsJsonAsync($"/api/v1/geofiles/{geofileId}", new
        {
            name = $"Audit Geofile {tag}",
            description = (string?)null,
            style = new { stroke = "#ff0000" },
            cavingGroupId = (Guid?)null,
            visibility = "authenticated",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await viewer.GetAsync($"/api/v1/geofiles/export?format=geojson&search={tag}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = (await ExportAuditRowsAsync()).Single(r => r.GetProperty("layer").GetString() == "geofiles");
        list.GetProperty("exportedCount").GetInt32().ShouldBe(2);
        list.GetProperty("geofileIds").EnumerateArray().Select(i => i.GetString())
            .ShouldBe([geofileId.ToString()]);

        (await viewer.GetAsync($"/api/v1/geofiles/{geofileId}/export?format=geojson"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var single = (await ExportAuditRowsAsync()).Single(r => r.GetProperty("layer").GetString() == "geofile");
        single.GetProperty("exportedCount").GetInt32().ShouldBe(2);
        single.GetProperty("geofileIds").EnumerateArray().Select(i => i.GetString())
            .ShouldBe([geofileId.ToString()]);
    }

    /// <summary>The detail of every vector-export audit row a user wrote, oldest first.</summary>
    private async Task<List<JsonElement>> ExportAuditRowsAsync(Guid? userId = null)
    {
        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var user = userId ?? viewerId;
        var changes = await db.AuditEntries.AsNoTracking()
            .Where(e => e.Action == AuditActions.Exported && e.EntityType == "VectorExport" && e.UserId == user)
            .OrderBy(e => e.Id)
            .Select(e => e.Changes)
            .ToListAsync();
        return [.. changes.Select(c => JsonDocument.Parse(c!).RootElement.Clone())];
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

    /// <summary>Creates a generic feature — the kind the surface palette became.</summary>
    private async Task<Guid> CreateFeatureAsync(
        string name, long featureTypeId, object geometry, bool locationProtected = false)
    {
        var response = await editor.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name,
            featureTypeId,
            geometry,
            locationProtected,
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
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

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

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
