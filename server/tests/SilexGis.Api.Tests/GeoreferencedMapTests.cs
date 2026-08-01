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
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Georeferenced raster maps end-to-end: GeoTIFF upload → background COG normalization →
/// signed delivery URL with range requests; visibility, the protected-cave omission rule
/// and the exact-location grant that lifts it.
/// </summary>
[Collection(PostgresCollection.Name)]
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

        // The owner still sees it; others don't — not in lists, not by id.
        (await owner.GetAsync($"/api/v1/georeferenced-maps/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await outsider.GetAsync($"/api/v1/georeferenced-maps/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await MapIdsOfCaveAsync(outsider, caveFeatureId)).ShouldNotContain(id);

        // An explicit ViewExactLocation grant on the cave is exactly what the omission
        // waits for: with it, the raster becomes readable and listable again.
        await ReplaceFeatureAclAsync(
            owner, caveFeatureId, [(outsiderId, ObjectPermission.Read | ObjectPermission.ViewExactLocation)]);

        (await outsider.GetAsync($"/api/v1/georeferenced-maps/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await MapIdsOfCaveAsync(outsider, caveFeatureId)).ShouldContain(id);

        // Revoking the grant hides it again — the grant is the only thing holding it open.
        await ReplaceFeatureAclAsync(owner, caveFeatureId, []);
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

    // ---- helpers ----

    /// <summary>A small georeferenced GeoTIFF (EPSG:4326) generated in-process.</summary>
    private static byte[] MakeGeoTiff(double lonMin, double latMax, double pixelDegrees, int size)
    {
        GdalBase.ConfigureAll();
        var path = Path.Combine(Path.GetTempPath(), $"silexgis-fixture-{Guid.NewGuid():N}.tif");
        try
        {
            var driver = Gdal.GetDriverByName("GTiff");
            using (var dataset = driver.Create(path, size, size, 1, DataType.GDT_Byte, null))
            {
                dataset.SetGeoTransform([lonMin, pixelDegrees, 0, latMax, 0, -pixelDegrees]);
                var srs = new SpatialReference("");
                srs.ImportFromEPSG(4326);
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

    /// <summary>A TIFF with pixels but no georeferencing.</summary>
    private static byte[] MakePlainTiff()
    {
        GdalBase.ConfigureAll();
        var path = Path.Combine(Path.GetTempPath(), $"silexgis-fixture-{Guid.NewGuid():N}.tif");
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

    /// <summary>Replaces the grants on a feature (the one ACL route name of the feature world).</summary>
    private static async Task ReplaceFeatureAclAsync(
        HttpClient client, Guid featureId, (Guid SubjectId, ObjectPermission Permissions)[] entries)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/acl", new
        {
            entries = entries.Select(e => new
            {
                subjectKind = "user",
                subjectId = e.SubjectId,
                permissions = e.Permissions.ToString().Replace(" ", string.Empty),
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
