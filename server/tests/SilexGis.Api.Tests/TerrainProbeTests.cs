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
using SilexGis.Domain.Features;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The two ways of asking what the ground is, and why they are two.
///
/// <para>
/// One route answers about coordinates the caller supplied; the other answers about features. The
/// test this file exists for is the second one's negative: <b>the same mixed set of features, asked
/// for by two callers, comes back shorter for the one who may not place them</b> — and shorter is
/// asserted on the count as well as on the contents, because a route that returned every row with
/// the height blanked out would pass a check on the contents and still have disclosed, per
/// identifier, that there is a protected cave at the far end of it.
/// </para>
/// <para>
/// The positive half is asserted in the same test, and so is the fact that the withheld feature is
/// perfectly readable by the caller who does not get it. Without both, a route that answered nobody
/// would pass, and a route that was merely hiding the cave outright would look like protection.
/// </para>
/// </summary>
public sealed class TerrainProbeTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const int LongitudeLatitude = 4326;

    /// <summary>The north-west corner of the raster this class builds, and its pixel size.</summary>
    private const double West = 25.0;
    private const double North = 46.1;
    private const double Pixel = 0.001;

    /// <summary>
    /// The place a height is known at by construction: the middle of the pixel three across and
    /// five down, whose value this class writes as 100 + (5 × 16) + 3.
    /// </summary>
    private const double KnownLongitude = West + (3.5 * Pixel);
    private const double KnownLatitude = North - (5.5 * Pixel);
    private const double KnownElevationM = 100 + (5 * 16) + 3;

    private readonly SilexGisApiFactory factory;
    private readonly string buildRoot;

    private HttpClient owner = null!;
    private HttpClient insider = null!;
    private HttpClient terrainReader = null!;
    private HttpClient placer = null!;
    private HttpClient anonymous = null!;

    private Guid placerId;
    private Guid buildId;
    private long caveTypeId;
    private long entranceTypeId;
    private long sinkholeTypeId;

    public TerrainProbeTests(PostgresFixture postgres)
    {
        buildRoot = Path.Combine(Path.GetTempPath(), $"silexgis-tprobe-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Terrain:BuildRoot"] = buildRoot },
            JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // An Editor authors the caves. The seeded ruleset every account joins says nothing about
        // terrain, so this one holds real cave rights and genuinely no terrain right — which is
        // what the refusal test needs rather than an account assumed to hold none.
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tp-own-{suffix}@t.local");

        // A second Editor, holding the terrain right as well. It exists so that the reading rule
        // can be shown from both sides: it authors the cave nobody else may see, so it is entitled
        // to it by ownership rather than by any grant, and it may read elevation, so nothing about
        // the terrain right is what separates its answer from the one below.
        var insiderId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Editor, $"tp-in-{suffix}@t.local");

        // Two Viewers. A Viewer reads past visibility nowhere and holds no grant anywhere, so the
        // only difference between them is the one this file is about: one is handed the right to
        // place the protected cave and the other never is.
        var readerId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Viewer, $"tp-read-{suffix}@t.local");
        placerId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Viewer, $"tp-place-{suffix}@t.local");

        await GrantTerrainReadAsync(insiderId);
        await GrantTerrainReadAsync(readerId);
        await GrantTerrainReadAsync(placerId);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
            sinkholeTypeId = await db.FeatureTypes
                .Where(t => t.Code == FeatureTypeSeeds.Sinkhole).Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"tp-own-{suffix}@t.local");
        insider = await AuthHelper.BearerClientAsync(factory, $"tp-in-{suffix}@t.local");
        terrainReader = await AuthHelper.BearerClientAsync(factory, $"tp-read-{suffix}@t.local");
        placer = await AuthHelper.BearerClientAsync(factory, $"tp-place-{suffix}@t.local");
        anonymous = factory.CreateClient();

        buildId = await SeedActiveBuildAsync();
        WriteRaster(buildId, "ground.tif");
    }

    /// <summary>
    /// The free-form route is answered by the terrain right and by nothing else, which is exactly
    /// what makes it safe to hold that right: this caller has no cave rights whatever and the answer
    /// is a property of the elevation this installation holds, not of anything on the ground.
    /// </summary>
    [Fact]
    public async Task A_caller_holding_only_the_terrain_right_is_told_the_ground_height()
    {
        var body = await ProbeAsync(terrainReader, KnownLongitude, KnownLatitude);

        body.GetProperty("hasTerrain").GetBoolean().ShouldBeTrue();

        var sample = body.GetProperty("samples").EnumerateArray().Single();
        sample.GetProperty("outcome").GetString().ShouldBe("sampled");
        sample.GetProperty("elevationM").GetDouble().ShouldBe(KnownElevationM, 0.001);
        sample.GetProperty("longitude").GetDouble().ShouldBe(KnownLongitude, 1e-9);
    }

    /// <summary>
    /// Ground nobody has built elevation over is said to be unknown, and is never zero: a surface at
    /// sea level is a plausible reading and a passage drawn against one looks like a real result.
    /// </summary>
    [Fact]
    public async Task Ground_outside_every_raster_is_unknown_rather_than_sea_level()
    {
        var body = await ProbeAsync(terrainReader, 20.0, 40.0);

        var sample = body.GetProperty("samples").EnumerateArray().Single();
        sample.GetProperty("outcome").GetString().ShouldBe("outsideCoverage");
        sample.GetProperty("elevationM").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>
    /// The judged test. Two callers, one set of identifiers, and the difference between the answers
    /// is exactly the cave one of them may not place.
    /// </summary>
    [Fact]
    public async Task The_batch_probe_is_shorter_for_a_caller_who_may_not_place_what_it_names()
    {
        var open = await CaveWithEntranceAsync(KnownLongitude, KnownLatitude, locationProtected: false);
        var closed = await CaveWithEntranceAsync(
            West + (4.5 * Pixel), KnownLatitude, locationProtected: true);
        var sinkhole = await SinkholeAsync(West + (5.5 * Pixel), KnownLatitude);

        // The right to place the protected cave, and only for one of the two Viewers.
        await GrantExactViewAsync(closed.CaveId);

        var asked = new[] { open.EntranceId, closed.EntranceId, sinkhole };

        // Visibility is not what is doing the work below. The caller who will not be given a row for
        // this cave can read the cave itself perfectly well; what they cannot do is place it.
        var readable = await terrainReader.GetAsync($"/api/v1/caves/{closed.CaveId}");
        readable.StatusCode.ShouldBe(HttpStatusCode.OK, await readable.Content.ReadAsStringAsync());

        var withheld = await ProbeFeaturesAsync(terrainReader, asked);
        var whole = await ProbeFeaturesAsync(placer, asked);

        // The count, asserted on its own. A row present with no height would satisfy every
        // assertion about contents below and would still have said, per identifier, that something
        // is there and is being kept back.
        withheld.Count.ShouldBe(2);
        whole.Count.ShouldBe(3);
        withheld.Count.ShouldBeLessThan(whole.Count);

        withheld.Keys.ShouldNotContain(closed.EntranceId);
        withheld.Keys.ShouldContain(open.EntranceId);
        withheld.Keys.ShouldContain(sinkhole);

        whole.Keys.ShouldContain(closed.EntranceId);

        // And the rows that are there are real answers rather than empty placeholders.
        withheld[open.EntranceId]!.Value.ShouldBe(KnownElevationM, 0.001);
        whole[closed.EntranceId]!.Value.ShouldBe(100 + (5 * 16) + 4, 0.001);
        whole[sinkhole]!.Value.ShouldBe(100 + (5 * 16) + 5, 0.001);
    }

    /// <summary>
    /// Naming a feature that is not there, or is not one this caller may read at all, is answered
    /// the same way as naming one they may not place: by its absence. Anything else would make the
    /// route a way of testing identifiers.
    /// </summary>
    [Fact]
    public async Task A_feature_this_caller_cannot_read_is_absent_rather_than_refused()
    {
        // Authored by the account that also holds the terrain right, so that the very same row can
        // be shown coming back for somebody. Nothing here is location-protected: what is being
        // proved is the reading rule on its own.
        var hidden = await CaveWithEntranceAsync(
            KnownLongitude,
            KnownLatitude,
            locationProtected: false,
            visibility: "private",
            author: insider);
        var open = await CaveWithEntranceAsync(KnownLongitude, KnownLatitude, locationProtected: false);

        var withheld = await ProbeFeaturesAsync(placer, [hidden.EntranceId, open.EntranceId]);
        withheld.Count.ShouldBe(1);
        withheld.Keys.ShouldContain(open.EntranceId);
        withheld.Keys.ShouldNotContain(hidden.EntranceId);

        // The positive half, and the reason it has to be here: without it this test passes just as
        // well when the entrance came back for nobody — because its geometry never landed, because
        // it was written under the wrong parent, because its kind is not one this route reads. The
        // author is entitled to it and gets it, with a real height, so the only thing separating
        // the two answers is who is asking.
        var whole = await ProbeFeaturesAsync(insider, [hidden.EntranceId, open.EntranceId]);
        whole.Count.ShouldBe(2);
        whole.Keys.ShouldContain(hidden.EntranceId);
        whole[hidden.EntranceId]!.Value.ShouldBe(KnownElevationM, 0.001);

        withheld.Count.ShouldBeLessThan(whole.Count);

        // A pure invention is absent too, and is not told apart from the above.
        var invented = await ProbeFeaturesAsync(terrainReader, [Guid.NewGuid()]);
        invented.ShouldBeEmpty();
    }

    /// <summary>
    /// Holding every right over caves earns nothing here. Elevation is an installation-level asset
    /// and the right to read it is held over the installation, so an account that may author caves
    /// and holds no terrain right is refused on both routes.
    /// </summary>
    [Fact]
    public async Task Cave_rights_without_the_terrain_right_are_refused_on_both_routes()
    {
        var open = await CaveWithEntranceAsync(KnownLongitude, KnownLatitude, locationProtected: false);

        // The positive half: this account really does hold cave rights over the very feature it is
        // about to be refused elevation for.
        var readable = await owner.GetAsync($"/api/v1/caves/{open.CaveId}");
        readable.StatusCode.ShouldBe(HttpStatusCode.OK, await readable.Content.ReadAsStringAsync());

        var free = await owner.PostAsJsonAsync(
            "/api/v1/terrain/probe",
            new { points = new[] { new { longitude = KnownLongitude, latitude = KnownLatitude } } });
        free.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Code(free)).ShouldBe("access.forbidden");

        var batch = await owner.PostAsJsonAsync(
            "/api/v1/terrain/probe/features", new { featureIds = new[] { open.EntranceId } });
        batch.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Code(batch)).ShouldBe("access.forbidden");
    }

    [Fact]
    public async Task Neither_route_answers_a_visitor_who_is_not_signed_in()
    {
        var free = await anonymous.PostAsJsonAsync(
            "/api/v1/terrain/probe",
            new { points = new[] { new { longitude = KnownLongitude, latitude = KnownLatitude } } });
        free.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var batch = await anonymous.PostAsJsonAsync(
            "/api/v1/terrain/probe/features", new { featureIds = new[] { Guid.NewGuid() } });
        batch.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The bound on how many places one request may name is what stops a single request holding the
    /// elevation reader, which serves one caller at a time, for as long as it likes.
    /// </summary>
    [Fact]
    public async Task A_request_naming_nothing_or_far_too_much_is_refused_before_any_file_is_opened()
    {
        var empty = await terrainReader.PostAsJsonAsync(
            "/api/v1/terrain/probe", new { points = Array.Empty<object>() });
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var toomany = await terrainReader.PostAsJsonAsync(
            "/api/v1/terrain/probe",
            new
            {
                points = Enumerable.Range(0, 501)
                    .Select(_ => new { longitude = KnownLongitude, latitude = KnownLatitude })
                    .ToArray(),
            });
        toomany.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var offEarth = await terrainReader.PostAsJsonAsync(
            "/api/v1/terrain/probe",
            new { points = new[] { new { longitude = 400d, latitude = 12d } } });
        offEarth.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var noFeatures = await terrainReader.PostAsJsonAsync(
            "/api/v1/terrain/probe/features", new { featureIds = Array.Empty<Guid>() });
        noFeatures.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
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
        insider?.Dispose();
        terrainReader?.Dispose();
        placer?.Dispose();
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

    private static async Task<JsonElement> ProbeAsync(
        HttpClient client, double longitude, double latitude)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/terrain/probe", new { points = new[] { new { longitude, latitude } } });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>The rows that came back, keyed by feature. What is absent is the point.</summary>
    private static async Task<Dictionary<Guid, double?>> ProbeFeaturesAsync(
        HttpClient client, IReadOnlyList<Guid> featureIds)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/terrain/probe/features", new { featureIds });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);

        return JsonDocument.Parse(body).RootElement
            .GetProperty("samples")
            .EnumerateArray()
            .ToDictionary(
                s => s.GetProperty("featureId").GetGuid(),
                s => s.GetProperty("elevationM").ValueKind == JsonValueKind.Null
                    ? (double?)null
                    : s.GetProperty("elevationM").GetDouble());
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

    /// <summary>Read and exact placement on one cave, for one of the two Viewers.</summary>
    private async Task GrantExactViewAsync(Guid caveId)
    {
        var grant = await owner.PutAsJsonAsync($"/api/v1/objects/feature/{caveId}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = placerId,
                    effect = "allow",
                    actions = "read, viewExactLocation",
                    scopeKind = "object",
                },
            },
        });
        grant.StatusCode.ShouldBe(HttpStatusCode.OK, await grant.Content.ReadAsStringAsync());
    }

    private async Task<(Guid CaveId, Guid EntranceId)> CaveWithEntranceAsync(
        double longitude,
        double latitude,
        bool locationProtected,
        string visibility = "authenticated",
        HttpClient? author = null)
    {
        var writer = author ?? owner;
        var caveResponse = await writer.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Probe Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var cavePayload = await caveResponse.Content.ReadAsStringAsync();
        caveResponse.StatusCode.ShouldBe(HttpStatusCode.Created, cavePayload);
        var caveId = JsonDocument.Parse(cavePayload).RootElement.GetProperty("id").GetGuid();

        var entranceResponse = await writer.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { longitude, latitude } },
            altitude = 900m,
            positionQuality = "Gps",
        });
        var entrancePayload = await entranceResponse.Content.ReadAsStringAsync();
        entranceResponse.StatusCode.ShouldBe(HttpStatusCode.Created, entrancePayload);
        var entranceId = JsonDocument.Parse(entrancePayload).RootElement.GetProperty("id").GetGuid();

        return (caveId, entranceId);
    }

    /// <summary>
    /// A depression drawn as a point. It is here because the batch route reads two kinds and a test
    /// over entrances alone would not notice the second one falling out of the query.
    /// </summary>
    private async Task<Guid> SinkholeAsync(double longitude, double latitude)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Probe Doline {Guid.NewGuid():N}"[..30],
            featureTypeId = sinkholeTypeId,
            geometry = new { type = "Point", coordinates = new[] { longitude, latitude } },
            locationProtected = false,
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
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
                new Coordinate(25.0, 46.0),
                new Coordinate(25.1, 46.0),
                new Coordinate(25.1, 46.1),
                new Coordinate(25.0, 46.1),
                new Coordinate(25.0, 46.0),
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
    /// A prepared raster in the form the preparation step leaves behind, so what the routes read is
    /// what they read in production. Its values count upwards across the grid, which is what makes
    /// every height asserted above known by construction rather than read back out of the file.
    /// </summary>
    private void WriteRaster(Guid build, string name)
    {
        GdalBase.ConfigureAll();
        var directory = Path.Combine(buildRoot, build.ToString("N"), "prepared");
        Directory.CreateDirectory(directory);

        using var dataset = Gdal.GetDriverByName("GTiff")
            .Create(Path.Combine(directory, name), 16, 16, 1, DataType.GDT_Float32, null);

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
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = 100f + i;
        }

        band.WriteRaster(0, 0, 16, 16, pixels, 16, 16, 0, 0);
        dataset.FlushCache();
    }
}
