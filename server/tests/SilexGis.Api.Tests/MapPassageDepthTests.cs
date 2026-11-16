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
/// The depth-coloured passage layer: which caves are drawn, in what colour, and for whom.
///
/// <para>
/// The layer draws cave centreline work, so a protected cave drawn to a reader who may not place it
/// would hand them its position. The negative tests therefore hold a caller who may read the
/// protected cave and holds every terrain right, and show the cave missing from the answer and from
/// its count while an open cave beside it is present.
/// </para>
/// </summary>
public sealed class MapPassageDepthTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
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

    public MapPassageDepthTests(PostgresFixture postgres)
    {
        buildRoot = Path.Combine(TestScratch.Root, $"silexgis-pdepth-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Terrain:BuildRoot"] = buildRoot },
            JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"pd-own-{suffix}@t.local");
        await GrantTerrainReadAsync(ownerId);
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"pd-none-{suffix}@t.local");

        // A genuine Viewer holding the terrain right and nothing over any cave.
        var viewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"pd-read-{suffix}@t.local");
        await GrantTerrainReadAsync(viewerId);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"pd-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"pd-read-{suffix}@t.local");
        groundless = await AuthHelper.BearerClientAsync(factory, $"pd-none-{suffix}@t.local");
        anonymous = factory.CreateClient();

        buildId = await SeedActiveBuildAsync();
        WriteRaster(buildId);
    }

    private const string Window = "25.0,46.08,25.02,46.1";

    [Fact]
    public async Task An_open_cave_in_the_window_is_drawn_in_the_class_of_its_cover()
    {
        // Ground at 1050 m over a passage at 900 m: 150 m of rock, well inside one class. Cover of
        // exactly 100 m sits on a class edge, where a reading a hair under it would flip the class.
        WriteRaster(buildId, (_, _) => 1050f);
        var cave = await CaveWithPassageAsync(locationProtected: false);

        var body = await ReadAsync(viewer, Window);

        body.GetProperty("hasTerrain").GetBoolean().ShouldBeTrue();
        body.GetProperty("classEdgesM").EnumerateArray().Select(e => e.GetDouble())
            .ShouldBe(PassageDepth.ClassEdgesM);
        body.GetProperty("caveCount").GetInt32().ShouldBeGreaterThanOrEqualTo(1);

        // The class from 100 up to 200.
        var lines = LinesOf(body, cave);
        lines.ShouldNotBeEmpty();
        lines.ShouldAllBe(l => l.GetProperty("depthClass").GetInt32() == PassageDepth.ClassOf(150));
        lines[0].GetProperty("paths")[0].GetArrayLength().ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Ground_that_changes_along_the_passage_changes_the_class_along_it()
    {
        WriteRaster(buildId, (x, _) => x < 7 ? 905f : 1050f);
        var cave = await CaveWithPassageAsync(locationProtected: false);

        var classes = LinesOf(await ReadAsync(viewer, Window), cave)
            .Select(l => l.GetProperty("depthClass").GetInt32()).Distinct().Order().ToArray();

        classes.ShouldContain(PassageDepth.ClassOf(5));
        classes.ShouldContain(PassageDepth.ClassOf(150));
    }

    /// <summary>
    /// The disclosure the layer could make: lines, a count or a truncation flag shaped by a cave the
    /// reader may not place. The reader holds every terrain right and may read the protected cave.
    /// </summary>
    [Fact]
    public async Task A_cave_the_reader_may_not_place_is_neither_drawn_nor_counted_while_an_open_one_is()
    {
        var open = await CaveWithPassageAsync(locationProtected: false);
        var hidden = await CaveWithPassageAsync(locationProtected: true);

        var asViewer = await ReadAsync(viewer, Window);
        LinesOf(asViewer, open).ShouldNotBeEmpty();
        LinesOf(asViewer, hidden).ShouldBeEmpty();
        asViewer.GetRawText().ShouldNotContain(hidden.ToString());

        // The protected cave is genuinely readable by this caller, so the placement rule hides it.
        (await viewer.GetAsync($"/api/v1/caves/{hidden}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var asOwner = await ReadAsync(owner, Window);
        LinesOf(asOwner, hidden).ShouldNotBeEmpty();

        // The count is over the placeable set only: the owner's includes the cave the viewer's omits.
        asOwner.GetProperty("caveCount").GetInt32()
            .ShouldBe(asViewer.GetProperty("caveCount").GetInt32() + 1);
    }

    [Fact]
    public async Task A_plan_drawing_with_no_altitudes_is_not_drawn_at_nought()
    {
        var cave = await CaveWithPassageAsync(locationProtected: false, withAltitudes: false);

        LinesOf(await ReadAsync(viewer, Window), cave).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_window_away_from_the_passage_draws_nothing()
    {
        var cave = await CaveWithPassageAsync(locationProtected: false);

        var body = await ReadAsync(viewer, "26.0,47.0,26.1,47.1");

        LinesOf(body, cave).ShouldBeEmpty();
        body.GetProperty("lines").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task With_no_terrain_built_the_answer_says_so_and_carries_no_lines()
    {
        await CaveWithPassageAsync(locationProtected: false);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.TerrainBuilds.Where(b => b.Id == buildId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsActive, false));
        }

        var body = await ReadAsync(viewer, Window);

        body.GetProperty("hasTerrain").GetBoolean().ShouldBeFalse();
        body.GetProperty("lines").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_caller_without_the_terrain_right_is_refused_and_an_anonymous_one_is_unauthorised()
    {
        await CaveWithPassageAsync(locationProtected: false);

        var forbidden = await groundless.GetAsync($"/api/v1/map/passage-depth?bbox={Window}");
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Code(forbidden)).ShouldBe("access.forbidden");
        (await forbidden.Content.ReadAsStringAsync()).ShouldNotContain("lines");

        (await anonymous.GetAsync($"/api/v1/map/passage-depth?bbox={Window}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await owner.GetAsync($"/api/v1/map/passage-depth?bbox={Window}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?bbox=")]
    [InlineData("?bbox=1,2,3")]
    [InlineData("?bbox=x,y,z,w")]
    public async Task A_missing_or_malformed_window_is_refused(string query)
    {
        var response = await owner.GetAsync($"/api/v1/map/passage-depth{query}");
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

    private static JsonElement[] LinesOf(JsonElement body, Guid cave) =>
        [.. body.GetProperty("lines").EnumerateArray().Where(l => l.GetProperty("caveId").GetGuid() == cave)];

    private static async Task<JsonElement> ReadAsync(HttpClient client, string bbox)
    {
        var response = await client.GetAsync($"/api/v1/map/passage-depth?bbox={bbox}");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
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
        bool withAltitudes = true)
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
            geom = new { type = "Point", coordinates = new[] { CoveredWestLongitude, PassageLatitude } },
            altitude = 950m,
            positionQuality = "Gps",
        });
        entranceResponse.StatusCode.ShouldBe(
            HttpStatusCode.Created, await entranceResponse.Content.ReadAsStringAsync());

        var content = new ByteArrayContent(Passage(eastLongitude, withAltitudes));
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
    private static byte[] Passage(double eastLongitude, bool withAltitudes)
    {
        var points = new List<string>();
        for (var i = 0; i <= 20; i++)
        {
            var longitude = CoveredWestLongitude
                + ((eastLongitude - CoveredWestLongitude) * i / 20d);
            points.Add(withAltitudes
                ? $"[{F(longitude)},{F(PassageLatitude)},{F(PassageAltitudeM)}]"
                : $"[{F(longitude)},{F(PassageLatitude)}]");
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
