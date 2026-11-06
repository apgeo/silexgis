// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MaxRev.Gdal.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OSGeo.GDAL;
using OSGeo.OSR;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Georeferenced raster maps end-to-end: GeoTIFF upload → background COG normalization →
/// signed delivery URL with range requests; visibility, the protected-cave omission rule
/// and the exact-location grant that lifts it.
/// </summary>
[Collection(RasterScratchCollection.Name)]
public sealed class GeoreferencedMapTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private HttpClient outsider = null!;
    private Guid outsiderId;
    private long caveTypeId;

    public GeoreferencedMapTests(PostgresFixture postgres)
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
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"grm-own-{suffix}@t.local");
        outsiderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"grm-out-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"grm-own-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"grm-out-{suffix}@t.local");
    }

    [Fact]
    public void Bundled_gdal_exposes_the_cog_driver()
    {
        // The whole in-process COG plan rests on this driver being present.
        RasterCogService.CogDriverAvailable.ShouldBeTrue();
    }

    [Fact]
    public async Task Geotiff_upload_is_normalized_to_cog_with_footprint_and_streams()
    {
        var tiff = MakeGeoTiff(lonMin: 25.40, latMax: 45.60, pixelDegrees: 0.001, size: 64);
        var map = await UploadAsync(owner, "geologic-sheet.tif", tiff);
        map.GetProperty("status").GetString().ShouldBe("uploaded");
        var id = map.GetProperty("id").GetGuid();

        var ready = await WaitForProcessingAsync(owner, id);
        ready.GetProperty("status").GetString().ShouldBe("ready", ready.ToString());

        // Footprint lands in the right corner of the world.
        var bbox = ready.GetProperty("bbox");
        bbox.GetProperty("type").GetString().ShouldBe("Polygon");
        var ring = bbox.GetProperty("coordinates")[0];
        ring[0][0].GetDouble().ShouldBe(25.40, 0.01);
        ring[2][1].GetDouble().ShouldBe(45.60, 0.01);

        // The delivery URL streams anonymously and honors Range (geotiff.js needs both).
        var cogUrl = ready.GetProperty("cogUrl").GetString()!;
        using var anonymous = factory.CreateClient();
        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, cogUrl);
        rangeRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1);
        var partial = await anonymous.SendAsync(rangeRequest);
        partial.StatusCode.ShouldBe(HttpStatusCode.PartialContent);
        var magic = await partial.Content.ReadAsByteArrayAsync();
        magic.ShouldBe("II"u8.ToArray()); // little-endian TIFF marker

        // The stored file is a real COG (GDAL reports the layout).
        string storagePath;
        using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var fileId = ready.GetProperty("fileId").GetGuid();
            storagePath = await db.StoredFiles.Where(f => f.Id == fileId)
                .Select(f => f.StoragePath).SingleAsync();
        }

        GdalBase.ConfigureAll();
        using var dataset = Gdal.Open(Path.Combine(filesRoot, storagePath), Access.GA_ReadOnly);
        dataset.GetMetadataItem("LAYOUT", "IMAGE_STRUCTURE").ShouldBe("COG");
    }

    /// <summary>
    /// A cave-linked raster IS the cave's location — it cannot be degraded, only withheld.
    /// The rule is the ordinary exact-location rule, so an explicit ViewExactLocation grant
    /// on the cave restores the raster exactly like it restores the coordinates.
    /// </summary>
    [Fact]
    public async Task Rasters_linked_to_protected_caves_are_omitted_until_exact_location_is_granted()
    {
        var tiff = MakeGeoTiff(25.50, 45.50, 0.001, 32);
        var map = await UploadAsync(owner, "cave-plan.tif", tiff);
        var id = map.GetProperty("id").GetGuid();
        (await WaitForProcessingAsync(owner, id)).GetProperty("status").GetString().ShouldBe("ready");

        // Make it visible to all authenticated users, then link it to a protected cave.
        var caveResponse = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Raster Protected Cave {Guid.NewGuid():N}"[..40],
            caveTypeId,
            visibility = "authenticated",
            locationProtected = true,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        caveResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var caveFeatureId = (await caveResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var update = await owner.PutAsJsonAsync($"/api/v1/georeferenced-maps/{id}", new
        {
            name = "Cave plan overlay",
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
        var updated = await update.Content.ReadAsStringAsync();
        update.StatusCode.ShouldBe(HttpStatusCode.OK, updated);
        JsonDocument.Parse(updated).RootElement.GetProperty("caveFeatureId").GetGuid().ShouldBe(caveFeatureId);

        // The owner still sees it; others don't — not in lists, not by id. The owner's read hands
        // out the version the update answered, so a re-read refreshes what the next save carries.
        var ownRead = await owner.GetAsync($"/api/v1/georeferenced-maps/{id}");
        ownRead.StatusCode.ShouldBe(HttpStatusCode.OK);
        ownRead.Headers.ETag.ShouldNotBeNull("a versioned map's read hands out its version");
        ownRead.Headers.ETag!.Tag.ShouldBe(update.Headers.ETag!.Tag);
        (await outsider.GetAsync($"/api/v1/georeferenced-maps/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await MapIdsOfCaveAsync(outsider, caveFeatureId)).ShouldNotContain(id);

        // An explicit ViewExactLocation grant on the cave is exactly what the omission
        // waits for: with it, the raster becomes readable and listable again.
        await ReplaceFeatureAccessRulesAsync(
            owner, caveFeatureId, [(outsiderId, AccessAction.Read | AccessAction.ViewExactLocation)]);

        (await outsider.GetAsync($"/api/v1/georeferenced-maps/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await MapIdsOfCaveAsync(outsider, caveFeatureId)).ShouldContain(id);

        // Revoking the grant hides it again — the grant is the only thing holding it open.
        await ReplaceFeatureAccessRulesAsync(owner, caveFeatureId, []);
        (await outsider.GetAsync($"/api/v1/georeferenced-maps/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await MapIdsOfCaveAsync(outsider, caveFeatureId)).ShouldNotContain(id);
    }

    [Fact]
    public async Task Bad_rasters_fail_with_recorded_error_and_wrong_extensions_are_rejected()
    {
        using (var form = BuildForm("not-a-map.txt", "hello"u8.ToArray()))
        {
            (await owner.PostAsync("/api/v1/georeferenced-maps/", form))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        // A TIFF without georeferencing must fail processing, not crash the worker.
        var map = await UploadAsync(owner, "plain-image.tif", MakePlainTiff());
        var id = map.GetProperty("id").GetGuid();
        var failed = await WaitForProcessingAsync(owner, id, expectFailure: true);
        failed.GetProperty("status").GetString().ShouldBe("failed");
        failed.GetProperty("processingError").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// A few lines of the raster library's "virtual raster" XML saved as <c>.tif</c> used to be
    /// converted as what they are: a map whose pixels are read from whatever file the document
    /// names, published back to the uploader. The file it names here is a real georeferenced
    /// raster the test writes outside the store, so the only reason to refuse is what the upload is.
    /// </summary>
    [Fact]
    public async Task A_virtual_raster_uploaded_as_a_geotiff_fails_rather_than_reading_the_file_it_names()
    {
        var serverOnly = Path.Combine(TestScratch.Root, $"silexgis-server-only-{Guid.NewGuid():N}.tif");
        await File.WriteAllBytesAsync(serverOnly, MakeGeoTiff(25.0, 46.06, 0.004, 16));
        try
        {
            var document = $"""
                <VRTDataset rasterXSize="16" rasterYSize="16">
                  <SRS>EPSG:4326</SRS>
                  <GeoTransform>25.0, 0.004, 0, 46.06, 0, -0.004</GeoTransform>
                  <VRTRasterBand dataType="Byte" band="1">
                    <SimpleSource>
                      <SourceFilename relativeToVRT="0">{serverOnly}</SourceFilename>
                      <SourceBand>1</SourceBand>
                    </SimpleSource>
                  </VRTRasterBand>
                </VRTDataset>
                """;

            var map = await UploadAsync(owner, "map.tif", System.Text.Encoding.UTF8.GetBytes(document));
            var failed = await WaitForProcessingAsync(owner, map.GetProperty("id").GetGuid(), expectFailure: true);
            failed.GetProperty("status").GetString().ShouldBe("failed");
            failed.GetProperty("processingError").GetString().ShouldNotBeNullOrWhiteSpace();
        }
        finally
        {
            File.Delete(serverOnly);
        }
    }

    // ---- helpers ----

    /// <summary>A small georeferenced GeoTIFF (EPSG:4326) generated in-process.</summary>
    private static byte[] MakeGeoTiff(double lonMin, double latMax, double pixelDegrees, int size) =>
        MakeGeoTiff([lonMin, pixelDegrees, 0, latMax, 0, -pixelDegrees], size, 4326);

    /// <summary>A small GeoTIFF on any coordinate system and placement, generated in-process.</summary>
    private static byte[] MakeGeoTiff(double[] geoTransform, int size, int epsg)
    {
        GdalBase.ConfigureAll();
        var path = Path.Combine(TestScratch.Root, $"silexgis-fixture-{Guid.NewGuid():N}.tif");
        try
        {
            var driver = Gdal.GetDriverByName("GTiff");
            using (var dataset = driver.Create(path, size, size, 1, DataType.GDT_Byte, null))
            {
                dataset.SetGeoTransform(geoTransform);
                var srs = new SpatialReference("");
                srs.ImportFromEPSG(epsg);
                srs.ExportToWkt(out var wkt, null);
                dataset.SetProjection(wkt);

                var pixels = new byte[size * size];
                Random.Shared.NextBytes(pixels);
                dataset.GetRasterBand(1).WriteRaster(0, 0, size, size, pixels, size, size, 0, 0);
                dataset.FlushCache();
            }

            return File.ReadAllBytes(path);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>
    /// The map update refuses what would save a band nobody can be admitted to — club visibility
    /// with no club named — and a zoom range whose top is below its bottom, while a sound update
    /// of the same map still saves.
    /// </summary>
    [Fact]
    public async Task Update_refuses_club_visibility_without_a_club_and_an_inverted_zoom_range()
    {
        var map = await UploadAsync(owner, "club-map.tif", MakePlainTiff());
        var id = map.GetProperty("id").GetGuid();
        await WaitForProcessingAsync(owner, id, expectFailure: true);

        object Body(string visibility, int? minZoom, int? maxZoom) => new
        {
            name = "Club map",
            description = (string?)null,
            mapKind = "topographic",
            minZoom,
            maxZoom,
            attribution = (string?)null,
            defaultOpacity = 0.8,
            caveFeatureId = (Guid?)null,
            cavingGroupId = (Guid?)null,
            visibility,
        };

        var noClub = await owner.PutAsJsonAsync($"/api/v1/georeferenced-maps/{id}", Body("cavingGroup", null, null));
        noClub.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await noClub.Content.ReadAsStringAsync());

        var inverted = await owner.PutAsJsonAsync($"/api/v1/georeferenced-maps/{id}", Body("authenticated", 12, 8));
        inverted.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await inverted.Content.ReadAsStringAsync());

        var sound = await owner.PutAsJsonAsync($"/api/v1/georeferenced-maps/{id}", Body("authenticated", 8, 12));
        sound.StatusCode.ShouldBe(HttpStatusCode.OK, await sound.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The upload names the map after its file. A name longer than the column used to fail the save
    /// after the bytes were stored, answering 500; it is now cut to fit, and a file name longer than
    /// the stored-file row holds is refused before anything is written.
    /// </summary>
    [Fact]
    public async Task A_long_file_name_is_cut_to_fit_and_an_overlong_one_is_refused_before_storing()
    {
        var longStem = new string('m', 230);
        var map = await UploadAsync(owner, longStem + ".tif", MakePlainTiff());
        map.GetProperty("name").GetString().ShouldBe(longStem[..200]);
        await WaitForProcessingAsync(owner, map.GetProperty("id").GetGuid(), expectFailure: true);

        var filesBefore = Directory.GetFiles(filesRoot, "*", SearchOption.AllDirectories).Length;
        using var form = BuildForm(new string('o', 260) + ".tif", MakePlainTiff());
        var refused = await owner.PostAsync("/api/v1/georeferenced-maps/", form);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("georeferenced_map.file_name_too_long");
        Directory.GetFiles(filesRoot, "*", SearchOption.AllDirectories).Length.ShouldBe(filesBefore);
    }

    /// <summary>
    /// A tag on a map names it by kind and id, with no foreign key, so only the delete can remove
    /// it. The delete used to remove the catalogue row alone, leaving the tag counting towards its
    /// tag and reported by the integrity check as an orphan.
    /// </summary>
    [Fact]
    public async Task Deleting_a_map_removes_the_tags_on_it()
    {
        var map = await UploadAsync(owner, "tagged-map.tif", MakePlainTiff());
        var id = map.GetProperty("id").GetGuid();
        await WaitForProcessingAsync(owner, id, expectFailure: true);

        long tagId;
        using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var slug = $"grm-tag-{Guid.NewGuid():N}";
            var label = new Tag { Name = slug, Slug = slug };
            db.Tags.Add(label);
            await db.SaveChangesAsync();
            tagId = label.Id;
            db.Taggings.Add(new Tagging { TagId = tagId, EntityType = AttachedEntityType.GeoreferencedMap, EntityId = id });
            await db.SaveChangesAsync();
            (await db.Taggings.CountAsync(t => t.TagId == tagId)).ShouldBe(1);
        }

        (await owner.DeleteAsync($"/api/v1/georeferenced-maps/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Taggings.CountAsync(t => t.TagId == tagId)).ShouldBe(0);
            (await db.GeoreferencedMaps.CountAsync(m => m.Id == id)).ShouldBe(0);
        }
    }

    /// <summary>
    /// Every refusal the map's write routes make, each beside the allowed request it differs from:
    /// no credentials (401), a caller who reads the map but may not change it (403, while the owner
    /// saves), a stale version (412, while the current one saves), a club the owner belongs to no
    /// part of (403 with the binding code), a cave that does not exist (400), an empty file (400,
    /// while a real one is taken), and a delete followed by a read (404).
    /// </summary>
    [Fact]
    public async Task The_map_write_routes_refuse_each_wrong_request_and_take_the_right_one()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"grm-view-{suffix}@t.local");
        using var viewer = await AuthHelper.BearerClientAsync(factory, $"grm-view-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Manager, $"grm-mgr-{suffix}@t.local");
        using var manager = await AuthHelper.BearerClientAsync(factory, $"grm-mgr-{suffix}@t.local");
        using var anonymous = factory.CreateClient();

        // Empty upload refused; a real one taken.
        using (var empty = BuildForm("empty.tif", []))
        {
            var refusedEmpty = await owner.PostAsync("/api/v1/georeferenced-maps", empty);
            refusedEmpty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await refusedEmpty.Content.ReadAsStringAsync()).ShouldContain("georeferenced_map.size_invalid");
        }

        var map = await UploadAsync(owner, "guarded-map.tif", MakePlainTiff());
        var id = map.GetProperty("id").GetGuid();
        await WaitForProcessingAsync(owner, id, expectFailure: true);
        var url = $"/api/v1/georeferenced-maps/{id}";

        object Body(string visibility, Guid? caveFeatureId = null, Guid? cavingGroupId = null) => new
        {
            name = "Guarded map",
            description = (string?)null,
            mapKind = "topographic",
            minZoom = (int?)null,
            maxZoom = (int?)null,
            attribution = (string?)null,
            defaultOpacity = 0.8,
            caveFeatureId,
            cavingGroupId,
            visibility,
        };

        (await anonymous.PutAsJsonAsync(url, Body("authenticated"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync(url)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Opened to every signed-in account: the viewer reads it and still may not change it.
        (await owner.PutAsJsonAsync(url, Body("authenticated"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.PutAsJsonAsync(url, Body("authenticated"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.DeleteAsync(url)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A version that is not the current one is refused; the current one saves.
        var current = await owner.GetAsync(url);
        current.Headers.ETag.ShouldNotBeNull();
        using (var stale = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(Body("authenticated")) })
        {
            stale.Headers.TryAddWithoutValidation("If-Match", "\"999999\"");
            (await owner.SendAsync(stale)).StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        }

        using (var fresh = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(Body("authenticated")) })
        {
            fresh.Headers.TryAddWithoutValidation("If-Match", current.Headers.ETag!.ToString());
            var saved = await owner.SendAsync(fresh);
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        // A club the owner is no part of.
        var club = await manager.PostAsJsonAsync("/api/v1/caving-groups/", new
        {
            name = $"Map Club {suffix}",
            type = "cavingClub",
            description = (string?)null,
            website = (string?)null,
        });
        club.StatusCode.ShouldBe(HttpStatusCode.Created, await club.Content.ReadAsStringAsync());
        var clubId = (await club.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var foreign = await owner.PutAsJsonAsync(url, Body("cavingGroup", cavingGroupId: clubId));
        foreign.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await foreign.Content.ReadAsStringAsync()).ShouldContain(CavingGroupBindingRules.ForbiddenCode);

        var noCave = await owner.PutAsJsonAsync(url, Body("authenticated", caveFeatureId: Guid.NewGuid()));
        noCave.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await noCave.Content.ReadAsStringAsync()).ShouldContain("georeferenced_map.cave_not_found");

        (await owner.DeleteAsync(url)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The footprint a map is filed under is the ground its pixels cover, on all four sides: exactly
    /// the raster's own rectangle when it is already in longitude and latitude, and, for a sheet in
    /// the national projected system, the box round its whole boundary carried into degrees — which
    /// this test works out itself, point by point along every edge.
    /// </summary>
    [Fact]
    public async Task The_footprint_is_the_ground_the_raster_covers_on_every_side()
    {
        var degrees = await FootprintAsync(MakeGeoTiff(lonMin: 25.40, latMax: 45.60, pixelDegrees: 0.001, size: 64));
        degrees.West.ShouldBe(25.40, 1e-6);
        degrees.East.ShouldBe(25.40 + (64 * 0.001), 1e-6);
        degrees.North.ShouldBe(45.60, 1e-6);
        degrees.South.ShouldBe(45.60 - (64 * 0.001), 1e-6);

        // A sheet in Stereo 70, 64 pixels of 100 m from a corner near Brașov.
        double[] stereo = [545_000, 100, 0, 455_000, 0, -100];
        var projected = await FootprintAsync(MakeGeoTiff(stereo, 64, 3844));
        var expected = BoundaryInDegrees(stereo, 64, 3844);
        projected.West.ShouldBe(expected.West, 1e-6);
        projected.East.ShouldBe(expected.East, 1e-6);
        projected.South.ShouldBe(expected.South, 1e-6);
        projected.North.ShouldBe(expected.North, 1e-6);

        // Not a degree rectangle any more: the sheet's corners land at four different longitudes.
        (expected.East - expected.West).ShouldBeGreaterThan(0.05);
    }

    private async Task<(double West, double South, double East, double North)> FootprintAsync(byte[] tiff)
    {
        var map = await UploadAsync(owner, "footprint.tif", tiff);
        var ready = await WaitForProcessingAsync(owner, map.GetProperty("id").GetGuid());
        ready.GetProperty("status").GetString().ShouldBe("ready", ready.ToString());

        var ring = ready.GetProperty("bbox").GetProperty("coordinates")[0].EnumerateArray()
            .Select(p => (X: p[0].GetDouble(), Y: p[1].GetDouble())).ToList();
        return (ring.Min(p => p.X), ring.Min(p => p.Y), ring.Max(p => p.X), ring.Max(p => p.Y));
    }

    /// <summary>
    /// The box round a raster's boundary in longitude and latitude, from a dense walk along every
    /// edge — far denser than anything the server samples, so a server box drawn round fewer points
    /// or round the corners alone lies inside this one by the bulge it missed.
    /// </summary>
    private static (double West, double South, double East, double North) BoundaryInDegrees(
        double[] gt, int size, int epsg)
    {
        GdalBase.ConfigureAll();
        using var source = new SpatialReference("");
        source.ImportFromEPSG(epsg);
        source.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        using var target = new SpatialReference("");
        target.ImportFromEPSG(4326);
        target.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        using var transform = new CoordinateTransformation(source, target);

        double west = double.MaxValue, south = double.MaxValue, east = double.MinValue, north = double.MinValue;
        var point = new double[3];
        const int steps = 1024;
        for (var i = 0; i <= steps; i++)
        {
            var along = (double)i / steps * size;
            foreach (var (px, py) in new[] { (along, 0d), (size, along), (along, (double)size), (0d, along) })
            {
                transform.TransformPoint(point, gt[0] + (px * gt[1]) + (py * gt[2]), gt[3] + (px * gt[4]) + (py * gt[5]), 0);
                west = Math.Min(west, point[0]);
                east = Math.Max(east, point[0]);
                south = Math.Min(south, point[1]);
                north = Math.Max(north, point[1]);
            }
        }

        return (west, south, east, north);
    }

    /// <summary>A TIFF with pixels but no georeferencing.</summary>
    private static byte[] MakePlainTiff()
    {
        GdalBase.ConfigureAll();
        var path = Path.Combine(TestScratch.Root, $"silexgis-fixture-{Guid.NewGuid():N}.tif");
        try
        {
            var driver = Gdal.GetDriverByName("GTiff");
            using (var dataset = driver.Create(path, 16, 16, 1, DataType.GDT_Byte, null))
            {
                dataset.GetRasterBand(1).WriteRaster(0, 0, 16, 16, new byte[256], 16, 16, 0, 0);
                dataset.FlushCache();
            }

            return File.ReadAllBytes(path);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static MultipartFormDataContent BuildForm(string fileName, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("image/tiff");
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    private async Task<JsonElement> UploadAsync(HttpClient client, string fileName, byte[] bytes)
    {
        using var form = BuildForm(fileName, bytes);
        var response = await client.PostAsync("/api/v1/georeferenced-maps/", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    /// <summary>
    /// Catalog ids for one cave, so the assertion never depends on how many rasters the
    /// shared database happens to hold or on which page they land.
    /// </summary>
    private static async Task<List<Guid>> MapIdsOfCaveAsync(HttpClient client, Guid caveFeatureId)
    {
        var response = await client.GetAsync($"/api/v1/georeferenced-maps/?caveFeatureId={caveFeatureId}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return [.. JsonDocument.Parse(payload).RootElement.GetProperty("items").EnumerateArray()
            .Select(m => m.GetProperty("id").GetGuid())];
    }

    /// <summary>
    /// Replaces the direct access rules on a feature (the one route name of the feature world).
    /// </summary>
    private static async Task ReplaceFeatureAccessRulesAsync(
        HttpClient client, Guid featureId, (Guid SubjectId, AccessAction Actions)[] entries)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/access", new
        {
            entries = entries.Select(e => new
            {
                subjectKind = "user",
                subjectId = e.SubjectId,
                effect = "allow",
                actions = e.Actions.ToString().Replace(" ", string.Empty),
                scopeKind = "object",
            }).ToArray(),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<JsonElement> WaitForProcessingAsync(HttpClient client, Guid id, bool expectFailure = false)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            var response = await client.GetAsync($"/api/v1/georeferenced-maps/{id}");
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var map = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            var status = map.GetProperty("status").GetString();
            if (status is "ready" or "failed")
            {
                if (!expectFailure)
                {
                    status.ShouldBe("ready", map.ToString());
                }

                return map;
            }

            DateTimeOffset.UtcNow.ShouldBeLessThan(deadline, "raster job did not finish in time");
            await Task.Delay(250);
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
