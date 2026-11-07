// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MaxRev.Gdal.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OSGeo.GDAL;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Map sheets uploaded as portable documents: drawn by the server, published at once when the
/// document carries its own place, otherwise kept unplaced until somebody places them; placed by
/// control points through the raster library's own transformations, tried out first in a preview,
/// and drawn again with another page or other layers.
/// </summary>
[Collection(RasterScratchCollection.Name)]
public sealed class GeoreferencedMapSheetTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private HttpClient outsider = null!;
    private long caveTypeId;

    public GeoreferencedMapSheetTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"gms-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"gms-out-{suffix}@t.local");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"gms-own-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"gms-out-{suffix}@t.local");
    }

    /// <summary>
    /// A document with its own georeferencing in a national grid is placed with no help: drawn,
    /// warped into web-mercator and published as a compressed cloud-optimised GeoTIFF with a
    /// transparent background, its footprint where the document says.
    /// </summary>
    [Fact]
    public async Task A_georeferenced_document_is_published_where_it_says_it_is()
    {
        // Stereo 70 (EPSG:3844), 400 × 300 pixels of 0.5 m from (500000, 500000): central Romania.
        var map = await UploadAsync(owner, "survey-sheet.pdf", PdfSheetFixtures.GeoPdf(3844, 500000, 500000, 0.5));
        map.GetProperty("status").GetString().ShouldBe("uploaded");
        map.GetProperty("mapKind").GetString().ShouldBe("caveMap");
        var id = map.GetProperty("id").GetGuid();

        var ready = await WaitAsync(owner, id, "ready");
        ready.GetProperty("placementSource").GetString().ShouldBe("embedded");
        ready.GetProperty("hasEmbeddedPlacement").GetBoolean().ShouldBeTrue();
        var placement = ready.GetProperty("placement");
        placement.GetProperty("crs").GetString().ShouldBe("EPSG:3844");
        placement.GetProperty("gcps").GetArrayLength().ShouldBe(4);
        ready.GetProperty("sourcePageCount").GetInt32().ShouldBe(1);
        ready.GetProperty("cogUrl").GetString().ShouldNotBeNull();
        ready.GetProperty("previewUrl").GetString().ShouldNotBeNull();

        // Stereo 70 (500000, 500000) is the projection's origin, at 25°E 46°N; the sheet is 200 m
        // by 150 m, so its footprint sits a fraction of a kilometre south-east of that.
        var ring = ready.GetProperty("bbox").GetProperty("coordinates")[0];
        ring[0][0].GetDouble().ShouldBe(25.0, 0.01);
        ring[2][1].GetDouble().ShouldBe(46.0, 0.01);

        using var cog = OpenStored(ready.GetProperty("fileId").GetGuid());
        cog.GetMetadataItem("LAYOUT", "IMAGE_STRUCTURE").ShouldBe("COG");
        cog.GetMetadataItem("COMPRESSION", "IMAGE_STRUCTURE").ShouldBe("WEBP");
        cog.RasterCount.ShouldBe(4);
        cog.GetProjection().ShouldContain("Pseudo-Mercator");
    }

    /// <summary>
    /// A document with no place of its own is drawn and kept unplaced, with a picture for the
    /// editor and nothing on the map; a placement by its four corners publishes it, and taking
    /// the placement away unpublishes it again.
    /// </summary>
    [Fact]
    public async Task An_unplaced_sheet_is_placed_by_its_corners_and_unplaced_again()
    {
        var map = await UploadAsync(owner, "plain-sheet.pdf", PdfSheetFixtures.PlainPdf());
        var id = map.GetProperty("id").GetGuid();
        var unplaced = await WaitAsync(owner, id, "unplaced");
        unplaced.GetProperty("cogUrl").ValueKind.ShouldBe(JsonValueKind.Null);
        unplaced.GetProperty("placement").ValueKind.ShouldBe(JsonValueKind.Null);
        unplaced.GetProperty("renderWidth").GetInt32().ShouldBeGreaterThan(0);

        using var anonymous = factory.CreateClient();
        var preview = await anonymous.GetAsync(unplaced.GetProperty("previewUrl").GetString());
        preview.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await preview.Content.ReadAsByteArrayAsync()).AsSpan(8, 4).ToArray().ShouldBe("WEBP"u8.ToArray());

        var placed = await PlaceAsync(owner, id, Corners("homography", 24.000, 45.502, 24.004, 45.500));
        placed.StatusCode.ShouldBe(HttpStatusCode.OK, await placed.Content.ReadAsStringAsync());

        var ready = await WaitAsync(owner, id, "ready");
        ready.GetProperty("placementSource").GetString().ShouldBe("manual");
        ready.GetProperty("placementRmsMetres").GetDouble().ShouldBeLessThan(0.01);
        var ring = ready.GetProperty("bbox").GetProperty("coordinates")[0];
        ring[0][0].GetDouble().ShouldBe(24.000, 0.0002);
        ring[2][0].GetDouble().ShouldBe(24.004, 0.0002);
        ring[0][1].GetDouble().ShouldBe(45.500, 0.0002);
        ring[2][1].GetDouble().ShouldBe(45.502, 0.0002);

        var removed = await owner.DeleteAsync($"/api/v1/georeferenced-maps/{id}/placement");
        removed.StatusCode.ShouldBe(HttpStatusCode.OK, await removed.Content.ReadAsStringAsync());
        var again = await GetAsync(owner, id);
        again.GetProperty("status").GetString().ShouldBe("unplaced");
        again.GetProperty("cogUrl").ValueKind.ShouldBe(JsonValueKind.Null);
        again.GetProperty("bbox").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>Two points scale and turn the sheet; a third agrees with them and is measured.</summary>
    [Fact]
    public async Task Two_points_place_a_sheet_by_scale_and_turn()
    {
        var id = (await UploadAsync(owner, "two-point.pdf", PdfSheetFixtures.PlainPdf())).GetProperty("id").GetGuid();
        await WaitAsync(owner, id, "unplaced");

        var body = new
        {
            version = 1,
            transformation = "similarity",
            crs = "EPSG:3844",
            gcps = new object[]
            {
                new { u = 0.0, v = 0.0, x = 500000.0, y = 500150.0, label = "a" },
                new { u = 1.0, v = 1.0, x = 500200.0, y = 500000.0, label = "b" },
                new { u = 1.0, v = 0.0, x = 500200.0, y = 500150.0, label = "c" },
            },
        };
        var placed = await PlaceAsync(owner, id, body);
        placed.StatusCode.ShouldBe(HttpStatusCode.OK, await placed.Content.ReadAsStringAsync());
        var ready = await WaitAsync(owner, id, "ready");
        ready.GetProperty("placementRmsMetres").GetDouble().ShouldBeLessThan(0.5);
    }

    /// <summary>
    /// A placement that cannot be fitted is refused before anything is queued, each with the code
    /// a client translates; and a GeoTIFF map, which has no drawing to place, refuses placement.
    /// </summary>
    [Fact]
    public async Task Placements_that_cannot_be_fitted_are_refused_with_their_codes()
    {
        var id = (await UploadAsync(owner, "refusals.pdf", PdfSheetFixtures.PlainPdf())).GetProperty("id").GetGuid();
        await WaitAsync(owner, id, "unplaced");

        async Task Refused(object body, string code)
        {
            var response = await PlaceAsync(owner, id, body);
            var payload = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, payload);
            payload.ShouldContain(code);
        }

        await Refused(Corners("affine", 24, 45.5, 24.1, 45.4, take: 2), "georeferenced_map.placement_too_few_points");
        await Refused(Corners("warp-it", 24, 45.5, 24.1, 45.4), "georeferenced_map.placement_transformation_unknown");
        await Refused(Corners("homography", 24, 45.5, 24.1, 45.4, crs: "EPSG:999999"), "georeferenced_map.placement_crs_unknown");
        await Refused(new
        {
            version = 1,
            transformation = "affine",
            crs = "EPSG:4326",
            gcps = new object[]
            {
                new { u = 0.0, v = 0.0, x = 24.0, y = 45.5 },
                new { u = 0.5, v = 0.5, x = 24.1, y = 45.4 },
                new { u = 1.0, v = 1.0, x = 24.2, y = 45.3 },
            },
        }, "georeferenced_map.placement_points_degenerate");
        await Refused(new
        {
            version = 1,
            transformation = "affine",
            crs = "EPSG:4326",
            gcps = new object[]
            {
                new { u = 0.0, v = 0.0, x = 24.0, y = 45.5 },
                new { u = 3.0, v = 0.0, x = 24.1, y = 45.5 },
                new { u = 0.0, v = 1.0, x = 24.0, y = 45.4 },
            },
        }, "georeferenced_map.placement_point_outside_picture");

        // Nothing was queued by a refusal: the sheet is still waiting to be placed.
        (await GetAsync(owner, id)).GetProperty("status").GetString().ShouldBe("unplaced");

        var tiff = await UploadAsync(owner, "not-a-sheet.tif", RasterScratchTiff());
        var notASheet = await PlaceAsync(owner, tiff.GetProperty("id").GetGuid(), Corners("homography", 24, 45.5, 24.1, 45.4));
        notASheet.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await notASheet.Content.ReadAsStringAsync()).ShouldContain("georeferenced_map.not_a_sheet");
    }

    /// <summary>
    /// A preview answers a picture of the placed sheet and where it goes, keeps nothing, and is
    /// answered only to a signed-in caller who may read the map.
    /// </summary>
    [Fact]
    public async Task A_preview_shows_a_placement_without_keeping_it()
    {
        var id = (await UploadAsync(owner, "preview.pdf", PdfSheetFixtures.PlainPdf())).GetProperty("id").GetGuid();
        await WaitAsync(owner, id, "unplaced");

        var response = await owner.PostAsJsonAsync(
            $"/api/v1/georeferenced-maps/{id}/placement/preview", Corners("affine", 24.000, 45.502, 24.004, 45.500));
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        var answer = JsonDocument.Parse(payload).RootElement;
        answer.GetProperty("image").GetString()!.ShouldStartWith("data:image/png;base64,");
        var extent = answer.GetProperty("extent4326");
        extent[0].GetDouble().ShouldBe(24.000, 0.0005);
        extent[3].GetDouble().ShouldBe(45.502, 0.0005);
        answer.GetProperty("rmsMetres").GetDouble().ShouldBeLessThan(0.01);
        answer.GetProperty("residuals").GetArrayLength().ShouldBe(4);

        (await GetAsync(owner, id)).GetProperty("status").GetString().ShouldBe("unplaced");

        // Only a signed-in caller who may read the map is answered; the protected-cave case, where
        // a reader is still refused, has a test of its own.
        using var anonymous = factory.CreateClient();
        var nobody = await anonymous.PostAsJsonAsync(
            $"/api/v1/georeferenced-maps/{id}/placement/preview", Corners("affine", 24.000, 45.502, 24.004, 45.500));
        nobody.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// A sheet's layers are read and can be chosen; another page can be drawn; a page or a layer
    /// the document does not have is refused.
    /// </summary>
    [Fact]
    public async Task Layers_and_pages_can_be_chosen_and_unknown_ones_are_refused()
    {
        var layered = (await UploadAsync(owner, "layered.pdf", PdfSheetFixtures.LayeredPdf())).GetProperty("id").GetGuid();
        var first = await WaitAsync(owner, layered, "unplaced");
        first.GetProperty("sourceLayers").EnumerateArray().Select(l => l.GetString()).ShouldBe(["Walls", "Labels"]);
        first.GetProperty("renderLayers").ValueKind.ShouldBe(JsonValueKind.Null);

        var unknown = await owner.PostAsJsonAsync($"/api/v1/georeferenced-maps/{layered}/render", new { layers = new[] { "Grid" } });
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadAsStringAsync()).ShouldContain("georeferenced_map.layer_unknown");

        var labels = await owner.PostAsJsonAsync($"/api/v1/georeferenced-maps/{layered}/render", new { layers = new[] { "Labels" } });
        labels.StatusCode.ShouldBe(HttpStatusCode.OK, await labels.Content.ReadAsStringAsync());
        var redrawn = await WaitAsync(owner, layered, "unplaced");
        redrawn.GetProperty("renderLayers").EnumerateArray().Select(l => l.GetString()).ShouldBe(["Labels"]);

        var twoPages = (await UploadAsync(owner, "two-pages.pdf", PdfSheetFixtures.TwoPagePdf())).GetProperty("id").GetGuid();
        (await WaitAsync(owner, twoPages, "unplaced")).GetProperty("sourcePageCount").GetInt32().ShouldBe(2);

        var tooFar = await owner.PostAsJsonAsync($"/api/v1/georeferenced-maps/{twoPages}/render", new { page = 3 });
        tooFar.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooFar.Content.ReadAsStringAsync()).ShouldContain("georeferenced_map.page_out_of_range");

        var second = await owner.PostAsJsonAsync($"/api/v1/georeferenced-maps/{twoPages}/render", new { page = 2, dpi = 100 });
        second.StatusCode.ShouldBe(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        var page2 = await WaitAsync(owner, twoPages, "unplaced");
        page2.GetProperty("sourcePage").GetInt32().ShouldBe(2);
        page2.GetProperty("renderDpi").GetDouble().ShouldBe(100);

        var tooFine = await owner.PostAsJsonAsync($"/api/v1/georeferenced-maps/{twoPages}/render", new { dpi = 5000 });
        tooFine.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooFine.Content.ReadAsStringAsync()).ShouldContain("georeferenced_map.dpi_out_of_range");
    }

    /// <summary>
    /// A document the drawing library complains about while still drawing it whole is drawn, with
    /// the complaint kept as a warning — not failed.
    /// </summary>
    [Fact]
    public async Task A_page_drawn_with_complaints_is_kept_with_a_warning()
    {
        var id = (await UploadAsync(owner, "complaining.pdf", PdfSheetFixtures.WarningPdf())).GetProperty("id").GetGuid();
        var drawn = await WaitAsync(owner, id, "unplaced");
        drawn.GetProperty("renderWarnings").GetString()!.ShouldContain("BDC");
    }

    /// <summary>
    /// A placement goes out as a IIIF Georeference Annotation and comes back in as the same
    /// placement, in longitude and latitude whatever system it was typed in.
    /// </summary>
    [Fact]
    public async Task A_placement_travels_out_and_back_as_a_georeference_annotation()
    {
        var id = (await UploadAsync(owner, "annotated.pdf", PdfSheetFixtures.PlainPdf())).GetProperty("id").GetGuid();
        await WaitAsync(owner, id, "unplaced");
        (await owner.GetAsync($"/api/v1/georeferenced-maps/{id}/placement/annotation")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var stereo = new
        {
            version = 1,
            transformation = "affine",
            crs = "EPSG:3844",
            gcps = new object[]
            {
                new { u = 0.0, v = 0.0, x = 500000.0, y = 500150.0 },
                new { u = 1.0, v = 0.0, x = 500200.0, y = 500150.0 },
                new { u = 0.0, v = 1.0, x = 500000.0, y = 500000.0 },
            },
        };
        (await PlaceAsync(owner, id, stereo)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var first = await WaitAsync(owner, id, "ready");

        var exported = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/georeferenced-maps/{id}/placement/annotation");
        exported.GetProperty("motivation").GetString().ShouldBe("georeferencing");
        var features = exported.GetProperty("body").GetProperty("features");
        features.GetArrayLength().ShouldBe(3);
        features[0].GetProperty("geometry").GetProperty("coordinates")[0].GetDouble().ShouldBe(25.0, 0.01);

        var imported = await owner.PutAsJsonAsync($"/api/v1/georeferenced-maps/{id}/placement/annotation", exported);
        imported.StatusCode.ShouldBe(HttpStatusCode.OK, await imported.Content.ReadAsStringAsync());
        var again = await WaitAsync(owner, id, "ready");
        again.GetProperty("placement").GetProperty("crs").GetString().ShouldBe("EPSG:4326");
        var before = first.GetProperty("bbox").GetProperty("coordinates")[0][0];
        var after = again.GetProperty("bbox").GetProperty("coordinates")[0][0];
        after[0].GetDouble().ShouldBe(before[0].GetDouble(), 1e-5);
        after[1].GetDouble().ShouldBe(before[1].GetDouble(), 1e-5);

        var broken = await owner.PutAsJsonAsync($"/api/v1/georeferenced-maps/{id}/placement/annotation", new { body = new { } });
        broken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await broken.Content.ReadAsStringAsync()).ShouldContain("georeferenced_map.annotation_invalid");
    }

    /// <summary>A file named .pdf that is not one is refused at the door.</summary>
    [Fact]
    public async Task A_file_that_only_claims_to_be_a_portable_document_is_refused()
    {
        using var form = Form("fake.pdf", "this is not a portable document"u8.ToArray());
        var response = await owner.PostAsync("/api/v1/georeferenced-maps/", form);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("georeferenced_map.format_unsupported");
    }

    /// <summary>
    /// A sheet tied to a protected cave is the cave's position. To a caller who may read the map
    /// but not place the cave it does not exist — not in the list, not by id, not as a preview,
    /// and its drawing cannot be changed.
    /// </summary>
    [Fact]
    public async Task A_sheet_of_a_protected_cave_is_withheld_from_callers_without_exact_view()
    {
        var map = await UploadAsync(owner, "cave-sheet.pdf", PdfSheetFixtures.GeoPdf(3844, 500000, 500000, 0.5));
        var id = map.GetProperty("id").GetGuid();
        await WaitAsync(owner, id, "ready");

        var cave = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Sheet Protected Cave {Guid.NewGuid():N}"[..40],
            caveTypeId,
            visibility = "authenticated",
            locationProtected = true,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        cave.StatusCode.ShouldBe(HttpStatusCode.Created);
        var caveFeatureId = (await cave.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var update = await owner.PutAsJsonAsync($"/api/v1/georeferenced-maps/{id}", new
        {
            name = "Cave sheet",
            description = (string?)null,
            mapKind = "caveMap",
            minZoom = (int?)null,
            maxZoom = (int?)null,
            attribution = (string?)null,
            defaultOpacity = 0.8,
            caveFeatureId,
            cavingGroupId = (Guid?)null,
            visibility = "authenticated",
        });
        update.StatusCode.ShouldBe(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());

        (await outsider.GetAsync($"/api/v1/georeferenced-maps/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var list = await outsider.GetFromJsonAsync<JsonElement>($"/api/v1/georeferenced-maps/?caveFeatureId={caveFeatureId}");
        list.GetProperty("items").GetArrayLength().ShouldBe(0);
        (await outsider.PostAsJsonAsync(
            $"/api/v1/georeferenced-maps/{id}/placement/preview", Corners("affine", 24, 45.5, 24.1, 45.4)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.PostAsJsonAsync($"/api/v1/georeferenced-maps/{id}/render", new { dpi = 100 }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The owner, who may place the cave, still reads it.
        (await owner.GetAsync($"/api/v1/georeferenced-maps/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- helpers ----

    /// <summary>A placement by the four corners of the picture, north-west first, clockwise.</summary>
    private static object Corners(
        string transformation, double west, double north, double east, double south, int take = 4, string crs = "EPSG:4326")
    {
        var corners = new object[]
        {
            new { u = 0.0, v = 0.0, x = west, y = north, label = "NW" },
            new { u = 1.0, v = 0.0, x = east, y = north, label = "NE" },
            new { u = 1.0, v = 1.0, x = east, y = south, label = "SE" },
            new { u = 0.0, v = 1.0, x = west, y = south, label = "SW" },
        };
        return new { version = 1, transformation, crs, gcps = corners.Take(take).ToArray() };
    }

    private static Task<HttpResponseMessage> PlaceAsync(HttpClient client, Guid id, object body) =>
        client.PutAsJsonAsync($"/api/v1/georeferenced-maps/{id}/placement", body);

    private static MultipartFormDataContent Form(string fileName, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(fileName.EndsWith(".pdf", StringComparison.Ordinal) ? "application/pdf" : "image/tiff");
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    private static async Task<JsonElement> UploadAsync(HttpClient client, string fileName, byte[] bytes)
    {
        using var form = Form(fileName, bytes);
        var response = await client.PostAsync("/api/v1/georeferenced-maps/", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/v1/georeferenced-maps/{id}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    /// <summary>Waits until the map's jobs have settled and asserts where they settled.</summary>
    private static async Task<JsonElement> WaitAsync(HttpClient client, Guid id, string expected)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (true)
        {
            var map = await GetAsync(client, id);
            var status = map.GetProperty("status").GetString();
            if (status is "ready" or "failed" or "unplaced")
            {
                status.ShouldBe(expected, map.ToString());
                return map;
            }

            DateTimeOffset.UtcNow.ShouldBeLessThan(deadline, "the sheet's job did not finish in time");
            await Task.Delay(250);
        }
    }

    private Dataset OpenStored(Guid fileId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var storagePath = db.StoredFiles.Where(f => f.Id == fileId).Select(f => f.StoragePath).Single();
        GdalBase.ConfigureAll();
        return Gdal.Open(Path.Combine(filesRoot, storagePath), Access.GA_ReadOnly);
    }

    /// <summary>A tiny GeoTIFF with a place, for the one test that needs a map that is not a sheet.</summary>
    private static byte[] RasterScratchTiff()
    {
        GdalBase.ConfigureAll();
        var path = Path.Combine(TestScratch.Root, $"silexgis-fixture-{Guid.NewGuid():N}.tif");
        try
        {
            using (var dataset = Gdal.GetDriverByName("GTiff").Create(path, 16, 16, 1, DataType.GDT_Byte, null))
            {
                dataset.SetGeoTransform([24, 0.001, 0, 45.5, 0, -0.001]);
                dataset.SetProjection("GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433],AUTHORITY[\"EPSG\",\"4326\"]]");
            }

            return File.ReadAllBytes(path);
        }
        finally
        {
            File.Delete(path);
        }
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
