// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MaxRev.Gdal.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using OSGeo.GDAL;
using OSGeo.OSR;
using Shouldly;
using SilexGis.Api.Features.Caves;
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
/// Which caves have passage beneath a doline, a sinkhole or any outline, and who is allowed to be told.
///
/// <para>
/// The answer is a list of caves placed against a position, so a protected cave named in it would be
/// placed by it. The negative test therefore holds a caller who may read a protected cave and holds
/// every terrain right, and shows the cave missing from the list while an open cave beside it is
/// present — so what is absent is absent for the placement rule and not for a query that finds
/// nothing. The same is asserted of a protected feature asked about.
/// </para>
/// <para>
/// The arithmetic is pinned against a raster of one repeated height: each expected thickness is that
/// height less the altitude this file wrote into the survey.
/// </para>
/// </summary>
public sealed class FeaturePassagesTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const int LongitudeLatitude = 4326;
    private const double West = 25.0;
    private const double North = 46.1;
    private const double Pixel = 0.001;
    private const double GroundM = 1000;
    private const double PassageLatitude = 46.09;
    private const double PassageAltitudeM = 900;
    private const double CoveredWestLongitude = 25.002;
    private const double CoveredEastLongitude = 25.012;

    private readonly SilexGisApiFactory factory;
    private readonly string buildRoot;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private HttpClient groundless = null!;
    private HttpClient anonymous = null!;

    private Guid buildId;
    private long caveTypeId;
    private long entranceTypeId;
    private long sinkholeTypeId;

    public FeaturePassagesTests(PostgresFixture postgres)
    {
        buildRoot = Path.Combine(TestScratch.Root, $"silexgis-fpass-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Terrain:BuildRoot"] = buildRoot },
            JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"fp-own-{suffix}@t.local");
        await GrantTerrainReadAsync(ownerId);
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"fp-none-{suffix}@t.local");

        // A genuine Viewer holding the terrain right and nothing over any cave.
        var viewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"fp-read-{suffix}@t.local");
        await GrantTerrainReadAsync(viewerId);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
            sinkholeTypeId = await db.FeatureTypes.Where(t => t.Code == "sinkhole").Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"fp-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"fp-read-{suffix}@t.local");
        groundless = await AuthHelper.BearerClientAsync(factory, $"fp-none-{suffix}@t.local");
        anonymous = factory.CreateClient();

        buildId = await SeedActiveBuildAsync();
        WriteRaster(buildId);
    }

    [Fact]
    public async Task An_outline_over_a_passage_lists_the_cave_with_no_offset_and_the_rock_overhead()
    {
        var cave = await CaveWithPassageAsync(locationProtected: false);
        var outline = await CreateFeatureAsync(Rectangle(25.0045, 25.0055, 46.0899, 46.0901));

        var body = await ReadAsync(viewer, outline, 0);

        body.GetProperty("isPolygon").GetBoolean().ShouldBeTrue();
        body.GetProperty("hasTerrain").GetBoolean().ShouldBeTrue();
        var row = Find(body, cave);
        row.GetProperty("lateralOffsetM").GetDouble().ShouldBe(0, 1e-6);
        row.GetProperty("hasAltitudes").GetBoolean().ShouldBeTrue();
        row.GetProperty("groundAltitudeM").GetDouble().ShouldBe(GroundM, 1e-6);
        row.GetProperty("overburdenM").GetDouble().ShouldBe(GroundM - PassageAltitudeM, 1e-6);
        row.GetProperty("passageAltitudeM").GetDouble().ShouldBe(PassageAltitudeM, 1e-6);
    }

    [Fact]
    public async Task A_point_sinkhole_beside_a_passage_lists_it_only_within_the_reach_and_states_the_offset()
    {
        var cave = await CaveWithPassageAsync(locationProtected: false);

        // 0.0005 degrees of latitude north of the passage: about 55.7 m.
        var sinkhole = await CreateFeatureAsync(new { type = "Point", coordinates = new[] { 25.007, 46.0905 } });

        var near = await ReadAsync(viewer, sinkhole, 100);
        near.GetProperty("isPolygon").GetBoolean().ShouldBeFalse();
        Find(near, cave).GetProperty("lateralOffsetM").GetDouble().ShouldBe(55.7, 1.5);

        // The same cave is not in the list once the reach is shorter than the offset.
        Passages(await ReadAsync(viewer, sinkhole, 20)).ShouldNotContain(p => p.GetProperty("caveId").GetGuid() == cave);
    }

    /// <summary>
    /// The disclosure the route could make: a list of caves near a place. The caller holds every
    /// terrain right and may read the protected cave, and is shown the open one beside it.
    /// </summary>
    [Fact]
    public async Task A_cave_the_reader_may_not_place_is_not_listed_while_an_open_one_beside_it_is()
    {
        var open = await CaveWithPassageAsync(locationProtected: false);
        var hidden = await CaveWithPassageAsync(locationProtected: true);
        var outline = await CreateFeatureAsync(Rectangle(25.0045, 25.0055, 46.0899, 46.0901));

        var asViewer = await ReadAsync(viewer, outline, 0);
        Find(asViewer, open);
        Passages(asViewer).ShouldNotContain(p => p.GetProperty("caveId").GetGuid() == hidden);

        // The protected cave is genuinely readable by this caller, so the placement rule is what hides it.
        (await viewer.GetAsync($"/api/v1/caves/{hidden}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        asViewer.GetRawText().ShouldNotContain(hidden.ToString());

        // And the one who may place it is told about both.
        var asOwner = await ReadAsync(owner, outline, 0);
        Find(asOwner, open);
        Find(asOwner, hidden);
    }

    [Fact]
    public async Task A_feature_the_reader_may_not_place_is_refused_as_though_it_did_not_exist()
    {
        await CaveWithPassageAsync(locationProtected: false);
        var open = await CreateFeatureAsync(Rectangle(25.0045, 25.0055, 46.0899, 46.0901));
        var guarded = await CreateFeatureAsync(Rectangle(25.0045, 25.0055, 46.0899, 46.0901), locationProtected: true);

        (await viewer.GetAsync($"/api/v1/features/{open}/passages-beneath")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.GetAsync($"/api/v1/features/{guarded}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var refused = await viewer.GetAsync($"/api/v1/features/{guarded}/passages-beneath");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(refused)).ShouldBe("feature.not_found");

        var absent = await viewer.GetAsync($"/api/v1/features/{Guid.CreateVersion7()}/passages-beneath");
        absent.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(absent)).ShouldBe("feature.not_found");

        // A cave is not a surface feature of this kind.
        var cave = await CaveWithPassageAsync(locationProtected: false);
        (await owner.GetAsync($"/api/v1/features/{cave}/passages-beneath")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await ReadAsync(owner, guarded, 0)).GetProperty("featureId").GetGuid().ShouldBe(guarded);
    }

    /// <summary>
    /// With no terrain built the list is still the list: the caves are found by their lines and not
    /// by the ground, so the answer must say there is no terrain and leave the heights empty rather
    /// than report the thickness of rock as nought — and must not drop the cave for it.
    /// </summary>
    [Fact]
    public async Task With_no_terrain_built_the_cave_is_listed_with_no_ground_and_the_answer_says_so()
    {
        var cave = await CaveWithPassageAsync(locationProtected: false);
        var outline = await CreateFeatureAsync(Rectangle(25.0045, 25.0055, 46.0899, 46.0901));

        // The control: with the terrain in place the same request carries a ground height.
        var withTerrain = await ReadAsync(viewer, outline, 0);
        withTerrain.GetProperty("hasTerrain").GetBoolean().ShouldBeTrue();
        Find(withTerrain, cave).GetProperty("groundAltitudeM").GetDouble().ShouldBe(GroundM, 1e-6);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.TerrainBuilds.Where(b => b.Id == buildId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsActive, false));
        }

        var body = await ReadAsync(viewer, outline, 0);
        body.GetProperty("hasTerrain").GetBoolean().ShouldBeFalse();
        var row = Find(body, cave);
        row.GetProperty("lateralOffsetM").GetDouble().ShouldBe(0, 1e-6);
        row.GetProperty("hasAltitudes").GetBoolean().ShouldBeTrue();
        row.GetProperty("passageAltitudeM").GetDouble().ShouldBe(PassageAltitudeM, 1e-6);
        row.GetProperty("outcome").GetString().ShouldBe("outsideCoverage");
        row.GetProperty("groundAltitudeM").ValueKind.ShouldBe(JsonValueKind.Null);
        row.GetProperty("overburdenM").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>
    /// A list of twenty-five caves is complete and a twenty-sixth makes it a cut one. The flag is
    /// asserted on both sides of that edge, in a place of its own (the other tests of this class
    /// leave caves in the shared database at the passage latitude, some 550 m away).
    /// </summary>
    [Fact]
    public async Task A_list_of_more_caves_than_the_cap_is_cut_and_says_so_and_a_full_one_is_not()
    {
        const double isolatedLatitude = 46.095;
        var outline = await CreateFeatureAsync(Rectangle(25.0045, 25.0055, 46.0949, 46.0951));

        var caves = new List<Guid>();
        for (var i = 0; i < FeaturePassagesLimits.MaxCaves; i++)
        {
            caves.Add(await CaveWithPassageAsync(locationProtected: false, latitude: isolatedLatitude));
        }

        var full = await ReadAsync(viewer, outline, 0);
        full.GetProperty("truncated").GetBoolean().ShouldBeFalse();
        Passages(full).Length.ShouldBe(FeaturePassagesLimits.MaxCaves);

        caves.Add(await CaveWithPassageAsync(locationProtected: false, latitude: isolatedLatitude));

        var cut = await ReadAsync(viewer, outline, 0);
        cut.GetProperty("truncated").GetBoolean().ShouldBeTrue();
        var listed = Passages(cut);
        listed.Length.ShouldBe(FeaturePassagesLimits.MaxCaves);
        listed.Select(p => p.GetProperty("caveId").GetGuid()).ShouldBeSubsetOf(caves);
        listed.Select(p => p.GetProperty("caveId").GetGuid()).Distinct().Count().ShouldBe(listed.Length);
    }

    [Fact]
    public async Task A_caller_without_the_terrain_right_is_refused_and_an_anonymous_one_is_unauthorised()
    {
        await CaveWithPassageAsync(locationProtected: false);
        var outline = await CreateFeatureAsync(Rectangle(25.0045, 25.0055, 46.0899, 46.0901));

        var forbidden = await groundless.GetAsync($"/api/v1/features/{outline}/passages-beneath");
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Code(forbidden)).ShouldBe("access.forbidden");
        (await forbidden.Content.ReadAsStringAsync()).ShouldNotContain("groundAltitudeM");

        (await anonymous.GetAsync($"/api/v1/features/{outline}/passages-beneath"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await owner.GetAsync($"/api/v1/features/{outline}/passages-beneath")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000", "")]
    [InlineData("", "?distanceM=-1")]
    [InlineData("", "?distanceM=501")]
    public async Task A_malformed_request_is_refused_before_the_access_check(string id, string query)
    {
        var target = id.Length > 0 ? Guid.Parse(id) : Guid.CreateVersion7();
        var response = await owner.GetAsync($"/api/v1/features/{target}/passages-beneath{query}");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    public async ValueTask DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.TerrainBuilds.Where(b => b.Id == buildId).ExecuteDeleteAsync();
        await factory.DisposeAsync();
        Dispose();
    }

    public void Dispose()
    {
        owner?.Dispose();
        viewer?.Dispose();
        groundless?.Dispose();
        anonymous?.Dispose();
        try
        {
            if (Directory.Exists(buildRoot))
            {
                Directory.Delete(buildRoot, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Litter, not a failing test.
        }
    }

    private static async Task<string?> Code(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static string F(double value) => value.ToString("0.#######", CultureInfo.InvariantCulture);

    private static JsonElement[] Passages(JsonElement body) => [.. body.GetProperty("passages").EnumerateArray()];

    private static JsonElement Find(JsonElement body, Guid cave) =>
        Passages(body).Single(p => p.GetProperty("caveId").GetGuid() == cave);

    private static async Task<JsonElement> ReadAsync(HttpClient client, Guid featureId, double distanceM)
    {
        var response = await client.GetAsync($"/api/v1/features/{featureId}/passages-beneath?distanceM={F(distanceM)}");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static object Rectangle(double west, double east, double south, double north) => new
    {
        type = "Polygon",
        coordinates = new[]
        {
            new[]
            {
                new[] { west, south }, new[] { east, south }, new[] { east, north },
                new[] { west, north }, new[] { west, south },
            },
        },
    };

    private async Task<Guid> CreateFeatureAsync(object geometry, bool locationProtected = false)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Doline {Guid.NewGuid():N}"[..14],
            featureTypeId = sinkholeTypeId,
            geometry,
            visibility = "authenticated",
            locationProtected,
        });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// A rule naming one person directly, written straight into storage: the authoring surface
    /// refuses rules handing out more than their author holds, and no fixture account here holds
    /// anything over terrain to hand out.
    /// </summary>
    private async Task GrantTerrainReadAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Domain = AccessDomain.Terrain,
            Actions = AccessAction.Read,
            Effect = AccessEffect.Allow,
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> CaveWithPassageAsync(
        bool locationProtected,
        string visibility = "authenticated",
        double eastLongitude = CoveredEastLongitude,
        bool withAltitudes = true,
        double latitude = PassageLatitude)
    {
        var caveResponse = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Overburden {Guid.NewGuid():N}"[..24],
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var cavePayload = await caveResponse.Content.ReadAsStringAsync();
        caveResponse.StatusCode.ShouldBe(HttpStatusCode.Created, cavePayload);
        var caveId = JsonDocument.Parse(cavePayload).RootElement.GetProperty("id").GetGuid();

        var entranceResponse = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { CoveredWestLongitude, latitude } },
            altitude = 950m,
            positionQuality = "Gps",
        });
        entranceResponse.StatusCode.ShouldBe(
            HttpStatusCode.Created, await entranceResponse.Content.ReadAsStringAsync());

        var content = new ByteArrayContent(Passage(eastLongitude, withAltitudes, latitude));
        content.Headers.ContentType = new("application/geo+json");
        using var form = new MultipartFormDataContent { { content, "file", "Centerline.geojson" } };
        var upload = await owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", form);
        upload.StatusCode.ShouldBe(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());

        return caveId;
    }

    /// <summary>
    /// A level run of passage at one altitude, so that every expected thickness is one subtraction.
    /// Without altitudes it is the same drawing recorded as a plan, which is legal and is what a
    /// survey with no third coordinate looks like on disk.
    /// </summary>
    private static byte[] Passage(double eastLongitude, bool withAltitudes, double latitude)
    {
        var points = new List<string>();
        for (var i = 0; i <= 20; i++)
        {
            var longitude = CoveredWestLongitude
                + ((eastLongitude - CoveredWestLongitude) * i / 20d);
            points.Add(withAltitudes
                ? $"[{F(longitude)},{F(latitude)},{F(PassageAltitudeM)}]"
                : $"[{F(longitude)},{F(latitude)}]");
        }

        var body = "{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\","
            + "\"properties\":{},\"geometry\":{\"type\":\"LineString\",\"coordinates\":["
            + string.Join(",", points) + "]}}]}";
        return Encoding.UTF8.GetBytes(body);
    }

    private async Task<Guid> SeedActiveBuildAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // At most one build in the whole installation is the active one, held by a unique index
        // rather than by a handler, and the database container is shared between test classes.
        await db.TerrainBuilds.Where(b => b.IsActive)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsActive, false));

        var build = new TerrainBuild
        {
            Extent = new GeometryFactory(new PrecisionModel(), LongitudeLatitude).CreatePolygon(
            [
                new Coordinate(West, North - (16 * Pixel)),
                new Coordinate(West + (16 * Pixel), North - (16 * Pixel)),
                new Coordinate(West + (16 * Pixel), North),
                new Coordinate(West, North),
                new Coordinate(West, North - (16 * Pixel)),
            ]),
            RequestedMaxDepth = 13,
            Status = TerrainBuildStatus.Queued,
            Phase = TerrainBuildPhase.Pending,
            HeightDatum = TerrainHeightDatum.Orthometric,
            GeoidHeightM = 0,
            IsActive = true,
        };

        db.TerrainBuilds.Add(build);
        await db.SaveChangesAsync();
        return build.Id;
    }

    /// <summary>
    /// A prepared raster in the form the preparation step leaves behind. Every pixel carries the
    /// same height, which bilinear interpolation reproduces exactly at any point inside the
    /// footprint — so no expected thickness in this file depends on where a station happened to land
    /// between two pixels.
    /// </summary>
    private void WriteRaster(Guid build, Func<int, int, float>? height = null)
    {
        GdalBase.ConfigureAll();
        var directory = Path.Combine(buildRoot, build.ToString("N"), "prepared");
        Directory.CreateDirectory(directory);

        using var dataset = Gdal.GetDriverByName("GTiff")
            .Create(Path.Combine(directory, "ground.tif"), 16, 16, 1, DataType.GDT_Float32, null);

        dataset.SetGeoTransform([West, Pixel, 0, North, 0, -Pixel]);

        using (var reference = new SpatialReference(""))
        {
            reference.ImportFromEPSG(LongitudeLatitude);
            reference.ExportToWkt(out var wkt, null);
            dataset.SetProjection(wkt);
        }

        var band = dataset.GetRasterBand(1);
        band.SetNoDataValue(TerrainRasterPreparation.VoidValue);

        var pixels = new float[16 * 16];
        Array.Fill(pixels, (float)GroundM);
        if (height is not null)
        {
            for (var y = 0; y < 16; y++)
            {
                for (var x = 0; x < 16; x++)
                {
                    pixels[(y * 16) + x] = height(x, y);
                }
            }
        }

        band.WriteRaster(0, 0, 16, 16, pixels, 16, 16, 0, 0);
        dataset.FlushCache();
    }
}
