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
/// Recorded entrance altitudes against the ground under them, and who is allowed to be told.
///
/// <para>
/// The protection test that matters is that the report is the same with and without a protected
/// entrance whose altitude would otherwise top the ranking. A genuine Viewer holds the terrain right
/// and no grant over caves, so the protected cave is one they may read and may not place: every
/// count and the whole order must be what they would see had that cave never existed.
/// </para>
/// <para>
/// The ground is a raster of one repeated height, which bilinear interpolation reproduces exactly,
/// so every expected difference is an altitude this file wrote minus that height.
/// </para>
/// </summary>
public sealed class EntranceAltitudeTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const int LongitudeLatitude = 4326;
    private const double West = 25.0;
    private const double North = 46.1;
    private const double Pixel = 0.001;
    private const double GroundM = 1000;
    private const double EntranceLatitude = 46.09;

    private readonly SilexGisApiFactory factory;
    private readonly string buildRoot;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private HttpClient groundless = null!;
    private HttpClient anonymous = null!;

    private Guid buildId;
    private Guid viewerUserId;
    private long caveTypeId;
    private long entranceTypeId;
    private long karstAreaTypeId;
    private int placed;

    public EntranceAltitudeTests(PostgresFixture postgres)
    {
        buildRoot = Path.Combine(TestScratch.Root, $"silexgis-entalt-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Terrain:BuildRoot"] = buildRoot },
            JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var ownerId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Editor, $"ea-own-{suffix}@t.local");
        await GrantTerrainReadAsync(ownerId);

        // Reads caves and holds the terrain right, and nothing else: no write, no placement of a
        // protected cave. What refuses it below can therefore only be the rule being tested.
        var viewerId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Viewer, $"ea-read-{suffix}@t.local");
        await GrantTerrainReadAsync(viewerId);
        viewerUserId = viewerId;

        _ = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Editor, $"ea-none-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
            karstAreaTypeId = await db.FeatureTypes
                .Where(t => t.Code == "karst_area").Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"ea-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"ea-read-{suffix}@t.local");
        groundless = await AuthHelper.BearerClientAsync(factory, $"ea-none-{suffix}@t.local");
        anonymous = factory.CreateClient();

        buildId = await SeedActiveBuildAsync();
        WriteRaster(buildId);
    }

    [Fact]
    public async Task The_report_ranks_by_the_widest_gap_and_leaves_out_what_could_not_be_compared()
    {
        var area = await CreateAreaAsync();
        var agrees = await CaveWithEntranceAsync(area, 1005m);
        var high = await CaveWithEntranceAsync(area, 1100m);
        var low = await CaveWithEntranceAsync(area, 950m);
        var blank = await CaveWithEntranceAsync(area, null);

        var body = await ReadAreaAsync(viewer, area);

        body.GetProperty("hasTerrain").GetBoolean().ShouldBeTrue();
        body.GetProperty("entranceCount").GetInt32().ShouldBe(4);
        body.GetProperty("comparedCount").GetInt32().ShouldBe(3);
        body.GetProperty("disagreeingCount").GetInt32().ShouldBe(2);
        body.GetProperty("notRecordedCount").GetInt32().ShouldBe(1);

        var rows = body.GetProperty("entrances").EnumerateArray().ToArray();
        rows.Select(r => r.GetProperty("caveId").GetGuid()).ShouldBe([high, low, agrees, blank]);

        rows[0].GetProperty("differenceM").GetDouble().ShouldBe(100, 1e-6);
        rows[0].GetProperty("modelledAltitudeM").GetDouble().ShouldBe(GroundM, 1e-6);
        rows[0].GetProperty("verdict").GetString().ShouldBe("disagrees");
        rows[1].GetProperty("differenceM").GetDouble().ShouldBe(-50, 1e-6);
        rows[2].GetProperty("verdict").GetString().ShouldBe("agrees");
        rows[3].GetProperty("verdict").GetString().ShouldBe("notRecorded");
        rows[3].GetProperty("fillableFromTerrain").GetBoolean().ShouldBeTrue();
        rows[0].GetProperty("fillableFromTerrain").GetBoolean().ShouldBeFalse();

        // The report names entrances and places none of them.
        var text = body.GetRawText();
        text.ShouldNotContain("longitude");
        text.ShouldNotContain("geom");
    }

    /// <summary>
    /// The one that decides whether this is safe. The protected entrance would head the ranking were
    /// it counted, and it is not counted, not ranked and not subtracted from anything.
    /// </summary>
    [Fact]
    public async Task A_protected_entrance_the_reader_may_not_place_changes_nothing_they_are_told()
    {
        var area = await CreateAreaAsync();
        await CaveWithEntranceAsync(area, 1030m);
        await CaveWithEntranceAsync(area, 990m);

        var before = (await ReadAreaAsync(viewer, area)).GetRawText();
        var ownerBefore = await ReadAreaAsync(owner, area);

        var hidden = await CaveWithEntranceAsync(area, 2500m, locationProtected: true);

        // The positive half for the same caller: the protected cave is readable to them.
        (await viewer.GetAsync($"/api/v1/caves/{hidden}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var after = await ReadAreaAsync(viewer, area);
        after.GetRawText().ShouldBe(before);
        after.GetRawText().ShouldNotContain(hidden.ToString());

        // The author may place it, so for them the same cave is the top of the ranking: the
        // difference above is the rule and not an entrance that was never counted.
        var byOwner = await ReadAreaAsync(owner, area);
        byOwner.GetProperty("entranceCount").GetInt32()
            .ShouldBe(ownerBefore.GetProperty("entranceCount").GetInt32() + 1);
        byOwner.GetProperty("entrances")[0].GetProperty("caveId").GetGuid().ShouldBe(hidden);
        byOwner.GetProperty("entrances")[0].GetProperty("differenceM").GetDouble().ShouldBe(1500, 1e-6);
    }

    [Fact]
    public async Task One_cave_is_reported_whole_or_refused_as_absent()
    {
        var area = await CreateAreaAsync();
        var open = await CaveWithEntranceAsync(area, 1100m);
        var hidden = await CaveWithEntranceAsync(area, 1100m, locationProtected: true);

        var seen = await ReadJsonAsync(viewer, $"/api/v1/caves/{open}/altitude-disagreement");
        seen.GetProperty("entranceCount").GetInt32().ShouldBe(1);
        seen.GetProperty("entrances")[0].GetProperty("differenceM").GetDouble().ShouldBe(100, 1e-6);

        (await viewer.GetAsync($"/api/v1/caves/{hidden}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var refused = await viewer.GetAsync($"/api/v1/caves/{hidden}/altitude-disagreement");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(refused)).ShouldBe("cave.not_found");
        (await refused.Content.ReadAsStringAsync()).ShouldNotContain("entrances");

        var absent = await viewer.GetAsync(
            $"/api/v1/caves/{Guid.CreateVersion7()}/altitude-disagreement");
        absent.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(absent)).ShouldBe("cave.not_found");

        (await ReadJsonAsync(owner, $"/api/v1/caves/{hidden}/altitude-disagreement"))
            .GetProperty("entranceCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Reading_the_report_needs_the_terrain_right_and_a_signed_in_caller()
    {
        var area = await CreateAreaAsync();
        var cave = await CaveWithEntranceAsync(area, 1100m);

        // Readable and unprotected for this caller, so only the missing right is in the way.
        (await groundless.GetAsync($"/api/v1/caves/{cave}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var refusedArea = await groundless.GetAsync($"/api/v1/features/{area}/altitude-disagreement");
        refusedArea.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Code(refusedArea)).ShouldBe("access.forbidden");
        (await groundless.GetAsync($"/api/v1/caves/{cave}/altitude-disagreement"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await ReadAreaAsync(owner, area)).GetProperty("entranceCount").GetInt32().ShouldBe(1);

        (await anonymous.GetAsync($"/api/v1/features/{area}/altitude-disagreement"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/api/v1/caves/{cave}/altitude-disagreement"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await owner.GetAsync($"/api/v1/features/{Guid.Empty}/altitude-disagreement"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_area_nobody_may_read_is_answered_like_one_that_does_not_exist()
    {
        var closed = await CreateAreaAsync(visibility: "private");

        var refused = await viewer.GetAsync($"/api/v1/features/{closed}/altitude-disagreement");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await viewer.GetAsync($"/api/v1/features/{Guid.CreateVersion7()}/altitude-disagreement"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/features/{closed}/altitude-disagreement"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_area_report_does_not_name_a_cave_the_reader_cannot_read_through_its_entrance()
    {
        var area = await CreateAreaAsync();
        var cave = await CaveWithEntranceAsync(area, null);
        var entranceId = await EntranceIdAsync(cave);

        // The cave is denied to this reader at the object level while its entrance stays
        // readable: the case a grant made on the entrance alone produces.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.AccessEntries.Add(new AccessEntry
            {
                SubjectKind = AccessSubjectKind.User,
                SubjectId = viewerUserId,
                Effect = AccessEffect.Deny,
                Domain = AccessDomain.Features,
                Actions = AccessAction.Read,
                ScopeKind = AccessScopeKind.Object,
                ScopeFeatureId = cave,
            });
            await db.SaveChangesAsync();
        }

        (await viewer.GetAsync($"/api/v1/caves/{cave}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var seen = await (await viewer.GetAsync($"/api/v1/features/{area}/altitude-disagreement"))
            .Content.ReadAsStringAsync();
        seen.ShouldNotContain(cave.ToString());

        var owned = await (await owner.GetAsync($"/api/v1/features/{area}/altitude-disagreement"))
            .Content.ReadAsStringAsync();
        owned.ShouldContain(cave.ToString());
    }

    [Fact]
    public async Task Filling_writes_only_an_empty_altitude_and_never_replaces_a_recorded_one()
    {
        var area = await CreateAreaAsync();
        var blank = await CaveWithEntranceAsync(area, null);
        var recorded = await CaveWithEntranceAsync(area, 1100m);

        var blankEntrance = await EntranceIdAsync(blank);
        var recordedEntrance = await EntranceIdAsync(recorded);

        var filled = await owner.PostAsync($"/api/v1/cave-entrances/{blankEntrance}/fill-altitude", null);
        var payload = await filled.Content.ReadAsStringAsync();
        filled.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        JsonDocument.Parse(payload).RootElement.GetProperty("altitude").GetDecimal().ShouldBe(1000m);

        // The value reached the cave's record, and the report now compares it.
        var report = await ReadJsonAsync(owner, $"/api/v1/caves/{blank}/altitude-disagreement");
        report.GetProperty("entrances")[0].GetProperty("recordedAltitudeM").GetDecimal().ShouldBe(1000m);
        report.GetProperty("entrances")[0].GetProperty("verdict").GetString().ShouldBe("agrees");

        // A second fill finds it recorded, and so does a fill on an altitude somebody typed.
        foreach (var entrance in new[] { blankEntrance, recordedEntrance })
        {
            var refused = await owner.PostAsync($"/api/v1/cave-entrances/{entrance}/fill-altitude", null);
            refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await Code(refused)).ShouldBe("entrance.altitude_recorded");
        }

        var untouched = await ReadJsonAsync(owner, $"/api/v1/caves/{recorded}/altitude-disagreement");
        untouched.GetProperty("entrances")[0].GetProperty("recordedAltitudeM").GetDecimal().ShouldBe(1100m);
    }

    [Fact]
    public async Task Filling_is_refused_without_write_without_the_right_and_for_a_withheld_entrance()
    {
        var area = await CreateAreaAsync();
        var blank = await CaveWithEntranceAsync(area, null);
        var hidden = await CaveWithEntranceAsync(area, null, locationProtected: true);
        var blankEntrance = await EntranceIdAsync(blank);
        var hiddenEntrance = await EntranceIdAsync(hidden);

        // Reads the cave and holds the terrain right, but may not change the cave.
        (await viewer.GetAsync($"/api/v1/caves/{blank}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.PostAsync($"/api/v1/cave-entrances/{blankEntrance}/fill-altitude", null))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Readable but not placeable: answered exactly as an entrance that does not exist.
        (await viewer.GetAsync($"/api/v1/caves/{hidden}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var withheld = await viewer.PostAsync($"/api/v1/cave-entrances/{hiddenEntrance}/fill-altitude", null);
        withheld.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(withheld)).ShouldBe("entrance.not_found");
        var absent = await viewer.PostAsync($"/api/v1/cave-entrances/{Guid.CreateVersion7()}/fill-altitude", null);
        absent.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(absent)).ShouldBe("entrance.not_found");

        (await groundless.PostAsync($"/api/v1/cave-entrances/{blankEntrance}/fill-altitude", null))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await anonymous.PostAsync($"/api/v1/cave-entrances/{blankEntrance}/fill-altitude", null))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await owner.PostAsync($"/api/v1/cave-entrances/{Guid.Empty}/fill-altitude", null))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // None of the refusals wrote anything, and the author can still fill the withheld one.
        (await owner.PostAsync($"/api/v1/cave-entrances/{blankEntrance}/fill-altitude", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.PostAsync($"/api/v1/cave-entrances/{hiddenEntrance}/fill-altitude", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ground_the_elevation_data_does_not_reach_is_not_compared_and_cannot_be_filled()
    {
        var area = await CreateAreaAsync();
        var outside = await CaveWithEntranceAsync(area, 1100m, longitude: 25.5);
        var blankOutside = await CaveWithEntranceAsync(area, null, longitude: 25.5);

        var body = await ReadAreaAsync(owner, area);
        body.GetProperty("comparedCount").GetInt32().ShouldBe(0);
        body.GetProperty("notModelledCount").GetInt32().ShouldBe(1);
        var row = body.GetProperty("entrances").EnumerateArray()
            .Single(r => r.GetProperty("caveId").GetGuid() == outside);
        row.GetProperty("verdict").GetString().ShouldBe("notModelled");
        row.GetProperty("differenceM").ValueKind.ShouldBe(JsonValueKind.Null);

        var refused = await owner.PostAsync(
            $"/api/v1/cave-entrances/{await EntranceIdAsync(blankOutside)}/fill-altitude", null);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Code(refused)).ShouldBe("entrance.altitude_unavailable");
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
        return JsonDocument.Parse(body).RootElement.TryGetProperty("code", out var code)
            ? code.GetString()
            : null;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static Task<JsonElement> ReadAreaAsync(HttpClient client, Guid area) =>
        ReadJsonAsync(client, $"/api/v1/features/{area}/altitude-disagreement");

    private async Task<Guid> EntranceIdAsync(Guid caveId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.CaveEntrances.Where(e => e.CaveFeatureId == caveId).Select(e => e.Id).SingleAsync();
    }

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

    private async Task<Guid> CreateAreaAsync(string visibility = "authenticated")
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"EntAlt Area {Guid.NewGuid():N}"[..36],
            featureTypeId = karstAreaTypeId,
            geometry = new
            {
                type = "Polygon",
                coordinates = new[]
                {
                    new[]
                    {
                        new[] { 24.9, 46.0 }, new[] { 25.6, 46.0 },
                        new[] { 25.6, 46.2 }, new[] { 24.9, 46.2 },
                        new[] { 24.9, 46.0 },
                    },
                },
            },
            locationProtected = false,
            visibility,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CaveWithEntranceAsync(
        Guid area, decimal? altitude, bool locationProtected = false, double? longitude = null)
    {
        var caveResponse = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"EntAlt {Guid.NewGuid():N}"[..20],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            parentId = area,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var cavePayload = await caveResponse.Content.ReadAsStringAsync();
        caveResponse.StatusCode.ShouldBe(HttpStatusCode.Created, cavePayload);
        var caveId = JsonDocument.Parse(cavePayload).RootElement.GetProperty("id").GetGuid();

        // Every entrance sits on its own pixel column inside the raster unless told otherwise.
        var x = longitude ?? (West + 0.001 + (0.0007 * placed++ % 0.014));
        var entranceResponse = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { x, EntranceLatitude } },
            altitude,
            positionQuality = "Gps",
        });
        entranceResponse.StatusCode.ShouldBe(
            HttpStatusCode.Created, await entranceResponse.Content.ReadAsStringAsync());
        return caveId;
    }

    private async Task<Guid> SeedActiveBuildAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

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
