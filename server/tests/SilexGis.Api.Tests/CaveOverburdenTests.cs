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
/// How much rock is over a passage, and who is allowed to be told.
///
/// <para>
/// The test this file exists for is the negative one. An overburden profile is not a statistic about
/// a cave — it is the cave's own shape and position expressed as a curve, with the coordinate of
/// every reading beside it, so a caller who may read a protected cave but may not place it must get
/// <b>nothing</b>: not a curve of nulls, not an empty array, not a length. The assertion is on the
/// whole response. And the caller it is asserted for holds every terrain right in the installation,
/// because the danger being guarded against is exactly the belief that terrain data is what this
/// route is made of.
/// </para>
/// <para>
/// The positive half is asserted for that same caller in the same test, and the protected cave is
/// shown to be perfectly readable by them, so what refuses them is the placement rule and not
/// visibility, and not a route that answers nobody.
/// </para>
/// <para>
/// The arithmetic is pinned against a raster of one repeated height, which bilinear interpolation
/// reproduces exactly anywhere inside it. Every expected thickness below is therefore that height
/// minus an altitude this file wrote into the survey, and not a number read back out of the answer.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CaveOverburdenTests : IAsyncLifetime, IDisposable
{
    private const int LongitudeLatitude = 4326;

    /// <summary>The raster: sixteen pixels square, one repeated height, north-west corner here.</summary>
    private const double West = 25.0;
    private const double North = 46.1;
    private const double Pixel = 0.001;
    private const double GroundM = 1000;

    /// <summary>Where the passage is written, and at what altitude — both known by construction.</summary>
    private const double PassageLatitude = 46.09;
    private const double PassageAltitudeM = 900;
    private const double CoveredWestLongitude = 25.002;
    private const double CoveredEastLongitude = 25.012;

    /// <summary>The undulation used to prove the datum correction is applied, and applied once.</summary>
    private const double UndulationM = 39.39;

    private readonly SilexGisApiFactory factory;
    private readonly string buildRoot;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private HttpClient groundless = null!;
    private HttpClient anonymous = null!;

    private Guid buildId;
    private long caveTypeId;
    private long entranceTypeId;

    public CaveOverburdenTests(PostgresFixture postgres)
    {
        buildRoot = Path.Combine(Path.GetTempPath(), $"silexgis-overb-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Terrain:BuildRoot"] = buildRoot },
            JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // The author of every cave below and the caller every positive assertion is made for. It
        // holds the terrain right because a profile is made of ground heights and reading those is
        // gated in its own right; what it holds over caves is only what authoring them gives.
        var ownerId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Editor, $"ob-own-{suffix}@t.local");
        await GrantTerrainReadAsync(ownerId);

        // The same kind of account with the terrain right left off. The caves below are readable by
        // it and are not location-protected, so the only thing between it and a profile is the
        // right to read elevation at all.
        _ = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Editor, $"ob-none-{suffix}@t.local");

        // A genuine Viewer: it reads past visibility nowhere and holds no grant over any cave. The
        // one right it is given is the terrain right, at the widest scope there is, so that the
        // refusal below cannot be explained by it lacking anything to do with elevation.
        var viewerId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Viewer, $"ob-read-{suffix}@t.local");
        await GrantTerrainReadAsync(viewerId);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"ob-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"ob-read-{suffix}@t.local");
        groundless = await AuthHelper.BearerClientAsync(factory, $"ob-none-{suffix}@t.local");
        anonymous = factory.CreateClient();

        buildId = await SeedActiveBuildAsync();
        WriteRaster(buildId);
    }

    /// <summary>
    /// The one that decides whether this route is safe. Everything the caller could hold that is not
    /// the right to place this cave, they hold.
    /// </summary>
    [Fact]
    public async Task A_reader_who_may_not_place_the_cave_gets_no_profile_at_all()
    {
        var open = await CaveWithPassageAsync(locationProtected: false);
        var hidden = await CaveWithPassageAsync(locationProtected: true);

        // The positive half, for this same caller. Without it a route that answered nobody passes.
        var seen = await ReadAsync(viewer, open);
        seen.GetProperty("samples").GetArrayLength().ShouldBeGreaterThan(1);

        // And the protected cave is genuinely readable by them, so it is the placement rule that
        // refuses what follows and not visibility.
        (await viewer.GetAsync($"/api/v1/caves/{hidden}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Nothing comes back. Not a body with nulls in it — no body of this shape at all.
        var refused = await viewer.GetAsync($"/api/v1/caves/{hidden}/overburden");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(refused)).ShouldBe("cave.not_found");
        (await refused.Content.ReadAsStringAsync()).ShouldNotContain("samples");

        // A cave that was never created answers identically, so the refusal itself says nothing
        // about whether there is a cave there to be kept from them.
        var absent = await viewer.GetAsync($"/api/v1/caves/{Guid.CreateVersion7()}/overburden");
        absent.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(absent)).ShouldBe("cave.not_found");

        // The owner, who may place it, gets the profile — so the passage really is there to profile.
        (await ReadAsync(owner, hidden)).GetProperty("samples").GetArrayLength().ShouldBeGreaterThan(1);
    }

    /// <summary>
    /// A cave the caller may not read at all is the same answer, so the two refusals cannot be told
    /// apart either.
    /// </summary>
    [Fact]
    public async Task A_cave_the_caller_may_not_read_is_refused_the_same_way()
    {
        var mine = await CaveWithPassageAsync(locationProtected: false, visibility: "private");

        (await ReadAsync(owner, mine)).GetProperty("samples").GetArrayLength().ShouldBeGreaterThan(1);

        var refused = await viewer.GetAsync($"/api/v1/caves/{mine}/overburden");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(refused)).ShouldBe("cave.not_found");
    }

    /// <summary>
    /// The other half of the gate. A profile's readings carry ground heights read out of the
    /// installation's prepared rasters — the same numbers, from the same files, that the free-form
    /// elevation probe answers with — so an installation that withholds the right to read elevation
    /// must have it withheld here as well. Otherwise a cave page is a way round the probe's refusal.
    /// </summary>
    [Fact]
    public async Task A_caller_who_may_place_the_cave_but_holds_no_terrain_right_gets_no_ground_heights()
    {
        var open = await CaveWithPassageAsync(locationProtected: false);

        // The cave itself is readable by this caller, and it is not protected, so nothing about the
        // cave is what refuses below.
        (await groundless.GetAsync($"/api/v1/caves/{open}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // And the elevation probe refuses it too, which is the right this route is being held to.
        var probe = await groundless.PostAsJsonAsync("/api/v1/terrain/probe", new
        {
            points = new[] { new { longitude = CoveredWestLongitude, latitude = PassageLatitude } },
        });
        probe.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var refused = await groundless.GetAsync($"/api/v1/caves/{open}/overburden");
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Code(refused)).ShouldBe("access.forbidden");

        // No readings at all, rather than readings with the ground left out of them.
        (await refused.Content.ReadAsStringAsync()).ShouldNotContain("groundAltitudeM");

        // The same cave, asked for by a caller holding the terrain right, does answer — so what
        // refused above is that right and not the cave, and not a route that answers nobody.
        (await ReadAsync(owner, open)).GetProperty("samples").GetArrayLength().ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused_before_any_cave_is_looked_up()
    {
        var open = await CaveWithPassageAsync(locationProtected: false);

        var response = await anonymous.GetAsync($"/api/v1/caves/{open}/overburden");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The arithmetic, over ground whose height is the same everywhere and a passage written at one
    /// altitude: every thickness is the difference between two numbers this file chose.
    /// </summary>
    [Fact]
    public async Task The_thickness_over_a_passage_is_the_ground_less_the_passage_altitude()
    {
        var cave = await CaveWithPassageAsync(locationProtected: false);

        var body = await ReadAsync(owner, cave);

        body.GetProperty("hasTerrain").GetBoolean().ShouldBeTrue();
        body.GetProperty("hasAltitudes").GetBoolean().ShouldBeTrue();
        body.GetProperty("basis").GetString().ShouldBe("skeletonHeuristic");
        body.GetProperty("isApproximation").GetBoolean().ShouldBeTrue();

        var samples = body.GetProperty("samples").EnumerateArray().ToArray();
        samples.Length.ShouldBeGreaterThan(1);
        body.GetProperty("coveredSampleCount").GetInt32().ShouldBe(samples.Length);

        foreach (var sample in samples)
        {
            sample.GetProperty("outcome").GetString().ShouldBe("sampled");
            sample.GetProperty("groundAltitudeM").GetDouble().ShouldBe(GroundM, 1e-6);
            sample.GetProperty("overburdenM").GetDouble().ShouldBe(GroundM - PassageAltitudeM, 1e-6);
        }

        body.GetProperty("minOverburdenM").GetDouble().ShouldBe(GroundM - PassageAltitudeM, 1e-6);
        body.GetProperty("maxOverburdenM").GetDouble().ShouldBe(GroundM - PassageAltitudeM, 1e-6);
        body.GetProperty("meanOverburdenM").GetDouble().ShouldBe(GroundM - PassageAltitudeM, 1e-6);

        // The axis runs from nothing to the length of the passage, in order.
        var distances = samples.Select(s => s.GetProperty("distanceAlongM").GetDouble()).ToArray();
        distances[0].ShouldBe(0);
        distances.ShouldBeInOrder();
        distances[^1].ShouldBe(body.GetProperty("passageLengthM").GetDouble(), 1e-6);
    }

    /// <summary>
    /// Ground the elevation data does not reach leaves a hole in the curve, and the summary is taken
    /// over what was read and over nothing else.
    /// </summary>
    /// <remarks>
    /// The failure this guards against reads perfectly: an unread place reported as nought draws the
    /// passage arriving at the surface, which is plausible, alarming and invented, and it would drag
    /// every average towards the surface in proportion to how little of the cave is covered.
    /// </remarks>
    [Fact]
    public async Task Passage_the_elevation_data_does_not_reach_is_a_gap_and_never_nought()
    {
        // The passage starts inside the raster and runs out past its eastern edge at 25.016.
        var cave = await CaveWithPassageAsync(
            locationProtected: false, eastLongitude: 25.030);

        var body = await ReadAsync(owner, cave);
        var samples = body.GetProperty("samples").EnumerateArray().ToArray();

        var covered = body.GetProperty("coveredSampleCount").GetInt32();
        covered.ShouldBeGreaterThan(0);
        covered.ShouldBeLessThan(samples.Length);

        var uncovered = samples.Where(s => s.GetProperty("outcome").GetString() != "sampled").ToArray();
        uncovered.ShouldNotBeEmpty();
        foreach (var sample in uncovered)
        {
            sample.GetProperty("outcome").GetString().ShouldBe("outsideCoverage");
            sample.GetProperty("groundAltitudeM").ValueKind.ShouldBe(JsonValueKind.Null);
            sample.GetProperty("overburdenM").ValueKind.ShouldBe(JsonValueKind.Null);

            // The passage is still there and still placed — it is the ground that is unknown.
            sample.GetProperty("passageAltitudeM").GetDouble().ShouldBe(PassageAltitudeM, 1e-6);
        }

        // Averaged over the readings that exist, so the mean is unmoved by how much is missing.
        body.GetProperty("meanOverburdenM").GetDouble().ShouldBe(GroundM - PassageAltitudeM, 1e-6);
    }

    /// <summary>
    /// The correction between an ellipsoidal elevation model and an orthometric survey is applied,
    /// and applied exactly once. Applied twice or not at all, every thickness below still looks like
    /// a reasonable altitude, which is why it is asserted against a number worked out by hand.
    /// </summary>
    [Fact]
    public async Task An_ellipsoidal_elevation_model_is_reconciled_with_the_survey_once()
    {
        var cave = await CaveWithPassageAsync(locationProtected: false);

        try
        {
            await SetDatumAsync(TerrainHeightDatum.Ellipsoidal, UndulationM);

            var body = await ReadAsync(owner, cave);
            var expected = GroundM - UndulationM - PassageAltitudeM;

            foreach (var sample in body.GetProperty("samples").EnumerateArray())
            {
                sample.GetProperty("overburdenM").GetDouble().ShouldBe(expected, 1e-6);
            }
        }
        finally
        {
            await SetDatumAsync(TerrainHeightDatum.Orthometric, 0);
        }
    }

    /// <summary>
    /// A survey recorded as a plan drawing has no third coordinate, so there is nothing to subtract a
    /// ground height from. The answer says so rather than drawing the cave lying on the surface.
    /// </summary>
    [Fact]
    public async Task A_plan_drawing_with_no_altitudes_yields_no_curve_rather_than_one_at_nought()
    {
        var cave = await CaveWithPassageAsync(locationProtected: false, withAltitudes: false);

        var body = await ReadAsync(owner, cave);

        body.GetProperty("hasAltitudes").GetBoolean().ShouldBeFalse();
        body.GetProperty("samples").GetArrayLength().ShouldBe(0);
        body.GetProperty("coveredSampleCount").GetInt32().ShouldBe(0);
        body.GetProperty("meanOverburdenM").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_request_naming_no_cave_is_refused_before_the_access_check()
    {
        var response = await owner.GetAsync($"/api/v1/caves/{Guid.Empty}/overburden");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.TerrainBuilds.Where(b => b.Id == buildId).ExecuteDeleteAsync();
        await factory.DisposeAsync();
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
        return JsonDocument.Parse(body).RootElement.TryGetProperty("code", out var code)
            ? code.GetString()
            : null;
    }

    private static string F(double value) => value.ToString("0.#######", CultureInfo.InvariantCulture);

    private static async Task<JsonElement> ReadAsync(HttpClient client, Guid caveId)
    {
        var response = await client.GetAsync($"/api/v1/caves/{caveId}/overburden");
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

    private async Task SetDatumAsync(TerrainHeightDatum datum, double geoidHeightM)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.TerrainBuilds.Where(b => b.Id == buildId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.HeightDatum, datum)
                .SetProperty(b => b.GeoidHeightM, geoidHeightM));
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
    private void WriteRaster(Guid build)
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

        band.WriteRaster(0, 0, 16, 16, pixels, 16, 16, 0, 0);
        dataset.FlushCache();
    }
}
