// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MaxRev.Gdal.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using OSGeo.GDAL;
using OSGeo.OSR;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Entrances against the ground they sit on, read from computed steepness, facing and position
/// pictures. The pictures are generated rasters whose value depends on the pixel column alone, so
/// every expected class is known by construction:
/// <list type="bullet">
/// <item>columns 0-7 slope 10 degrees facing 90 (east), position level;</item>
/// <item>columns 8-13 slope 40 degrees facing 270 (west), position a rise;</item>
/// <item>columns 14-15 flat (slope 0) with the bearing zero the picture writes for flat ground.</item>
/// </list>
/// </summary>
public sealed class AreaTerrainStatisticsTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const double West = 25.0;
    private const double North = 46.1;
    private const double Pixel = 0.001;
    private const double Latitude = 46.09;

    private readonly SilexGisApiFactory factory;
    private readonly string buildRoot;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private HttpClient groundless = null!;
    private HttpClient anonymous = null!;

    private Guid buildId;
    private long caveTypeId;
    private long entranceTypeId;
    private long karstAreaTypeId;

    public AreaTerrainStatisticsTests(PostgresFixture postgres)
    {
        buildRoot = Path.Combine(TestScratch.Root, $"silexgis-terstat-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Terrain:BuildRoot"] = buildRoot },
            JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"ts-own-{suffix}@t.local");
        var viewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ts-read-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ts-none-{suffix}@t.local");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            foreach (var id in new[] { ownerId, viewerId })
            {
                db.AccessEntries.Add(new AccessEntry
                {
                    SubjectKind = AccessSubjectKind.User,
                    SubjectId = id,
                    Domain = AccessDomain.Terrain,
                    Actions = AccessAction.Read,
                    Effect = AccessEffect.Allow,
                    ScopeKind = AccessScopeKind.All,
                });
            }

            await db.SaveChangesAsync();

            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
            karstAreaTypeId = await db.FeatureTypes.Where(t => t.Code == "karst_area").Select(t => t.Id).FirstAsync();

            await db.TerrainBuilds.Where(b => b.IsActive)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsActive, false));
            var build = new TerrainBuild
            {
                Extent = Rectangle(),
                RequestedMaxDepth = 13,
                Status = TerrainBuildStatus.Queued,
                Phase = TerrainBuildPhase.Pending,
                HeightDatum = TerrainHeightDatum.Orthometric,
                GeoidHeightM = 0,
                IsActive = true,
            };
            db.TerrainBuilds.Add(build);
            await db.SaveChangesAsync();
            buildId = build.Id;
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"ts-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"ts-read-{suffix}@t.local");
        groundless = await AuthHelper.BearerClientAsync(factory, $"ts-none-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    [Fact]
    public async Task Entrances_are_counted_in_the_class_of_the_ground_they_sit_on_beside_the_ground_itself()
    {
        await SeedPicturesAsync();
        var area = await CreateAreaAsync();
        await CaveAtAsync(area, 25.002);
        await CaveAtAsync(area, 25.004);
        await CaveAtAsync(area, 25.011);

        var body = await ReadAsync(viewer, area);

        body.GetProperty("entrances").GetInt32().ShouldBe(3);
        body.GetProperty("backgroundPoints").GetInt32().ShouldBeGreaterThan(1500);

        var slope = Measure(body, "slope");
        slope.GetProperty("available").GetBoolean().ShouldBeTrue();
        slope.GetProperty("entrancesRead").GetInt32().ShouldBe(3);
        Row(slope, "5to15").GetProperty("entrances").GetInt32().ShouldBe(2);
        Row(slope, "30to45").GetProperty("entrances").GetInt32().ShouldBe(1);
        Row(slope, "lt5").GetProperty("entrances").GetInt32().ShouldBe(0);

        // Columns 0-7 of 16 are 10 degrees, so about half the ground is in that class and the two
        // entrances there are over-represented by about 4/3: (2/3) / (1/2).
        Row(slope, "5to15").GetProperty("backgroundShare").GetDouble().ShouldBe(0.5, 0.03);
        Row(slope, "5to15").GetProperty("entranceRatio").GetDouble().ShouldBe(4d / 3d, 0.12);

        var position = Measure(body, "position");
        Row(position, "level").GetProperty("entrances").GetInt32().ShouldBe(2);
        Row(position, "rise").GetProperty("entrances").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Facing_counts_only_where_the_ground_slopes()
    {
        await SeedPicturesAsync();
        var area = await CreateAreaAsync();
        await CaveAtAsync(area, 25.002);
        await CaveAtAsync(area, 25.0145);

        var aspect = Measure(await ReadAsync(viewer, area), "aspect");

        aspect.GetProperty("available").GetBoolean().ShouldBeTrue();
        Row(aspect, "e").GetProperty("entrances").GetInt32().ShouldBe(1);

        // The entrance on the flat columns is not counted under north, and neither is any flat ground.
        aspect.GetProperty("entrancesRead").GetInt32().ShouldBe(1);
        Row(aspect, "n").GetProperty("entrances").GetInt32().ShouldBe(0);
        Row(aspect, "n").GetProperty("background").GetInt32().ShouldBe(0);
        Row(aspect, "w").GetProperty("background").GetInt32().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_measure_with_no_computed_picture_says_so_instead_of_drawing_an_empty_histogram()
    {
        var area = await CreateAreaAsync();
        await CaveAtAsync(area, 25.002);

        var body = await ReadAsync(viewer, area);

        body.GetProperty("hasTerrain").GetBoolean().ShouldBeTrue();
        foreach (var name in new[] { "slope", "aspect", "position" })
        {
            var measure = Measure(body, name);
            measure.GetProperty("available").GetBoolean().ShouldBeFalse();
            measure.GetProperty("reason").GetString().ShouldBe("no_layer");
            measure.GetProperty("classes").GetArrayLength().ShouldBe(0);
        }

        // Steepness computed only as a percentage cannot be classed in degrees; facing then has no
        // picture to tell flat ground from sloping ground.
        await SeedLayerAsync(TerrainDerivative.Slope, _ => 50f, TerrainSlopeUnit.Percent);
        await SeedLayerAsync(TerrainDerivative.Aspect, _ => 90f);

        body = await ReadAsync(viewer, area);
        Measure(body, "slope").GetProperty("reason").GetString().ShouldBe("slope_unit");
        Measure(body, "aspect").GetProperty("reason").GetString().ShouldBe("slope_unit");
    }

    /// <summary>
    /// An entrance the reader may not place sits on ground of a class no other entrance is in. The
    /// report for the reader must be byte-for-byte what it was before the cave existed; the author,
    /// who may place it, sees it counted.
    /// </summary>
    [Fact]
    public async Task A_protected_entrance_moves_no_count_share_or_ratio_for_a_reader_who_may_not_place_it()
    {
        await SeedPicturesAsync();
        var area = await CreateAreaAsync();
        await CaveAtAsync(area, 25.002);
        var before = (await ReadAsync(viewer, area)).GetRawText();

        var hidden = await CaveAtAsync(area, 25.0145, locationProtected: true);
        (await viewer.GetAsync($"/api/v1/caves/{hidden}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ReadAsync(viewer, area)).GetRawText().ShouldBe(before);

        var author = await ReadAsync(owner, area);
        author.GetProperty("entrances").GetInt32().ShouldBe(2);
        Row(Measure(author, "slope"), "lt5").GetProperty("entrances").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task The_route_needs_the_terrain_right_an_area_the_reader_can_see_and_a_signed_in_caller()
    {
        var area = await CreateAreaAsync();

        var refused = await groundless.GetAsync($"/api/v1/features/{area}/terrain-statistics");
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("access.forbidden");

        (await viewer.GetAsync($"/api/v1/features/{area}/terrain-statistics")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.GetAsync($"/api/v1/features/{Guid.CreateVersion7()}/terrain-statistics"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync($"/api/v1/features/{area}/terrain-statistics"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    public async ValueTask DisposeAsync()
    {
        await factory.DisposeAsync();
        Dispose();
    }

    public void Dispose()
    {
        owner?.Dispose();
        viewer?.Dispose();
        groundless?.Dispose();
        anonymous?.Dispose();
    }

    private static JsonElement Measure(JsonElement body, string name) =>
        body.GetProperty("measures").EnumerateArray().Single(m => m.GetProperty("measure").GetString() == name);

    private static JsonElement Row(JsonElement measure, string code) =>
        measure.GetProperty("classes").EnumerateArray().Single(c => c.GetProperty("code").GetString() == code);

    private static async Task<JsonElement> ReadAsync(HttpClient client, Guid area)
    {
        var response = await client.GetAsync($"/api/v1/features/{area}/terrain-statistics");
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, raw);
        return JsonDocument.Parse(raw).RootElement.Clone();
    }

    private static Polygon Rectangle() =>
        new GeometryFactory(new PrecisionModel(), 4326).CreatePolygon(
        [
            new Coordinate(West, North - (16 * Pixel)),
            new Coordinate(West + (16 * Pixel), North - (16 * Pixel)),
            new Coordinate(West + (16 * Pixel), North),
            new Coordinate(West, North),
            new Coordinate(West, North - (16 * Pixel)),
        ]);

    private async Task SeedPicturesAsync()
    {
        await SeedLayerAsync(TerrainDerivative.Slope, c => c < 8 ? 10f : c < 14 ? 40f : 0f);
        await SeedLayerAsync(TerrainDerivative.Aspect, c => c < 8 ? 90f : c < 14 ? 270f : 0f);
        await SeedLayerAsync(TerrainDerivative.PositionIndex, c => c < 8 ? 0f : c < 14 ? 5f : 0f);
    }

    /// <summary>
    /// A computed picture of the active build, held in a real raster whose value is a function of
    /// the pixel column, registered as ready and current the way the job that draws it would.
    /// </summary>
    private async Task SeedLayerAsync(
        TerrainDerivative derivative, Func<int, float> byColumn, TerrainSlopeUnit unit = TerrainSlopeUnit.Degrees)
    {
        var settings = TerrainDerivativeRegistry.Normalise(
            new TerrainDerivativeSettings { Derivative = derivative, SlopeUnit = unit });

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var layer = new TerrainDerivativeLayer
        {
            TerrainBuildId = buildId,
            Derivative = derivative,
            Settings = TerrainDerivativeRegistry.Describe(settings),
            SettingsHash = TerrainDerivativeRegistry.Fingerprint(settings),
            Name = derivative.ToString(),
            Status = TerrainDerivativeStatus.Ready,
            Version = 1,
            MethodRevision = TerrainDerivativeRegistry.MethodRevision(derivative),
            ComputedAt = DateTimeOffset.UtcNow,
        };
        db.TerrainDerivativeLayers.Add(layer);

        var directory = Path.Combine(buildRoot, buildId.ToString("N"), "derivatives", layer.Id.ToString("N"), "v1");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "tile.tif");
        WriteColumns(file, byColumn);

        db.TerrainDerivativeRasters.Add(new TerrainDerivativeRaster
        {
            TerrainDerivativeLayerId = layer.Id,
            SourcePath = Path.Combine(buildRoot, buildId.ToString("N"), "prepared", "ground.tif"),
            Path = file,
            Width = 16,
            Height = 16,
            PixelSizeDegrees = Pixel,
            Footprint = Rectangle(),
            SizeBytes = new FileInfo(file).Length,
            ComputedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static void WriteColumns(string path, Func<int, float> byColumn)
    {
        GdalBase.ConfigureAll();
        using var dataset = Gdal.GetDriverByName("GTiff").Create(path, 16, 16, 1, DataType.GDT_Float32, null);
        dataset.SetGeoTransform([West, Pixel, 0, North, 0, -Pixel]);
        using (var reference = new SpatialReference(""))
        {
            reference.ImportFromEPSG(4326);
            reference.ExportToWkt(out var wkt, null);
            dataset.SetProjection(wkt);
        }

        var band = dataset.GetRasterBand(1);
        band.SetNoDataValue(-9999);
        var pixels = new float[16 * 16];
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                pixels[(y * 16) + x] = byColumn(x);
            }
        }

        band.WriteRaster(0, 0, 16, 16, pixels, 16, 16, 0, 0);
        dataset.FlushCache();
    }

    private async Task<Guid> CreateAreaAsync()
    {
        var box = Rectangle().EnvelopeInternal;
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"TerStat Area {Guid.NewGuid():N}"[..36],
            featureTypeId = karstAreaTypeId,
            geometry = new
            {
                type = "Polygon",
                coordinates = new[]
                {
                    new[]
                    {
                        new[] { box.MinX, box.MinY }, new[] { box.MaxX, box.MinY },
                        new[] { box.MaxX, box.MaxY }, new[] { box.MinX, box.MaxY },
                        new[] { box.MinX, box.MinY },
                    },
                },
            },
            locationProtected = false,
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CaveAtAsync(Guid area, double longitude, bool locationProtected = false)
    {
        var caveResponse = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"TerStat {Guid.NewGuid():N}"[..20],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            parentId = area,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await caveResponse.Content.ReadAsStringAsync();
        caveResponse.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var caveId = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var entrance = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { longitude, Latitude } },
            positionQuality = "Gps",
        });
        entrance.StatusCode.ShouldBe(HttpStatusCode.Created, await entrance.Content.ReadAsStringAsync());
        return caveId;
    }
}
