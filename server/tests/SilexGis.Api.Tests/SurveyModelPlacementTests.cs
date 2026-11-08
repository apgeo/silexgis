// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Features.SurveyUploads;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Scans;

namespace SilexGis.Api.Tests;

/// <summary>
/// Placing a scan: by hand on a position and a bearing, corrected, taken away, and listed on the
/// map turned the way it was placed — with the tiles, their archive and their address untouched
/// throughout; from the file's own coordinate system when the file names one; refused for a model
/// that is not a point cloud; and gated the way every write to a survey model is — not found for a
/// caller the cave's position is closed to, forbidden for one who may see it and not write it.
/// </summary>
/// <remarks>
/// The cloud is a synthetic header with no points and the tiles are bytes the test wrote; the
/// worker is a canned one on the far side of the hand-off directory.
/// </remarks>
public sealed class SurveyModelPlacementTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string handoffRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];
    private CannedScanWorker worker = null!;

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private HttpClient nobody = null!;
    private Guid readerId;
    private long caveTypeId;

    /// <summary>What the canned worker says it subtracted before tiling: the tiles' zero point, in the file's frame.</summary>
    private double[] shift = [0.5, 0.5, 0.5];

    /// <summary>Something to do on the far side of the hand-off while the tiling step is "running": a person acting during a long run.</summary>
    private Func<Task>? whileTiling;

    /// <summary>UTM zone 35N, and a zero point east of its central meridian.</summary>
    private const int Utm35N = 32635;
    private const double Easting = 600_000;
    private const double Northing = 5_040_000;

    /// <summary>
    /// UTM zone 35N as well-known text, with no authority named and no GeoTIFF keys beside it —
    /// how a LAS 1.4 writer records the system, and the only way the 1.4 point formats may.
    /// </summary>
    private const string Utm35NWkt =
        """PROJCS["WGS 84 / UTM zone 35N",GEOGCS["WGS 84",DATUM["WGS_1984",SPHEROID["WGS 84",6378137,298.257223563]],PRIMEM["Greenwich",0],UNIT["degree",0.0174532925199433]],PROJECTION["Transverse_Mercator"],PARAMETER["latitude_of_origin",0],PARAMETER["central_meridian",27],PARAMETER["scale_factor",0.9996],PARAMETER["false_easting",500000],PARAMETER["false_northing",0],UNIT["metre",1],AXIS["Easting",EAST],AXIS["Northing",NORTH]]""";

    public SurveyModelPlacementTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-smp-{Guid.NewGuid():N}");
        handoffRoot = Path.Combine(filesRoot, "handoff");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            ["Scans:ProcessingEnabled"] = "true",
            ["Scans:HandoffRoot"] = handoffRoot,
            ["Scans:PickupSeconds"] = "1",
            ["Scans:JobTimeoutSeconds"] = "5",
        }, JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"smp-own-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"smp-read-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"smp-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"smp-read-{suffix}@t.local");
        nobody = factory.CreateClient();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();

        var handoff = factory.Services.GetRequiredService<ScanWorkerHandoff>();
        handoff.PollInterval = TimeSpan.FromMilliseconds(50);
        handoff.WithdrawalWait = TimeSpan.FromSeconds(1);

        CannedScanWorker.Announce(handoffRoot, PointCloudEstimateStep.StepId, PointCloudDownsampleStep.StepId, Py3dtilesTileStep.StepId);
        worker = new CannedScanWorker(handoffRoot) { Answer = Tiling };
    }

    public async ValueTask DisposeAsync()
    {
        await worker.DisposeAsync();
        Dispose();
    }

    public void Dispose()
    {
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            try
            {
                Directory.Delete(filesRoot, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task A_scan_placed_by_hand_is_listed_on_the_map_turned_as_placed_and_its_tiles_are_not_touched()
    {
        // A spot of this test's own, since the map listing is read over a shared database.
        var lon = 20 + Random.Shared.NextDouble() * 5;
        var lat = 40 + Random.Shared.NextDouble() * 5;
        var scan = await TiledScanAsync(owner);
        var archiveBefore = await ArchiveIdAsync(scan.ModelId);
        var jobsBefore = await JobCountAsync();

        var placed = await owner.PutAsJsonAsync($"/api/v1/survey-models/{scan.ModelId}/placement",
            new { longitude = lon, latitude = lat, heightM = 980, headingDeg = -45 });
        placed.StatusCode.ShouldBe(HttpStatusCode.OK, await placed.Content.ReadAsStringAsync());
        var dto = await placed.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("anchorLongitude").GetDouble().ShouldBe(lon, tolerance: 1e-9);
        dto.GetProperty("anchorLatitude").GetDouble().ShouldBe(lat, tolerance: 1e-9);
        dto.GetProperty("anchorHeightM").GetDouble().ShouldBe(980);
        dto.GetProperty("placementHeadingDeg").GetDouble().ShouldBe(315, "a bearing is stored in the compass round");
        dto.GetProperty("placementSource").GetString().ShouldBe("manual");
        // The model itself names its tiles, so the scan can be opened from the cave's model list
        // to pick the points it is placed by; the address opens the same tiles the card's does.
        var modelTiles = dto.GetProperty("tilesetUrl").GetString()!;
        modelTiles.ShouldStartWith($"/api/v1/survey-models/{scan.ModelId}/tiles/tileset.json?token=");
        (await nobody.GetAsync(modelTiles)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // On the map, turned as placed, at the same address the card hands out.
        var listed = await ScansAtAsync(owner, lon, lat);
        listed.GetProperty("total").GetInt32().ShouldBe(1);
        var item = listed.GetProperty("items")[0];
        item.GetProperty("surveyModelId").GetGuid().ShouldBe(scan.ModelId);
        item.GetProperty("headingDeg").GetDouble().ShouldBe(315);
        item.GetProperty("pitchDeg").GetDouble().ShouldBe(0, "a scan placed by hand is level");
        item.GetProperty("rollDeg").GetDouble().ShouldBe(0);
        item.GetProperty("anchorHeightM").GetDouble().ShouldBe(980);
        item.GetProperty("tilesetUrl").GetString().ShouldStartWith($"/api/v1/survey-models/{scan.ModelId}/tiles/tileset.json?token=");

        // Corrected: a changed row, nothing queued, the same archive.
        var corrected = await owner.PutAsJsonAsync($"/api/v1/survey-models/{scan.ModelId}/placement",
            new { longitude = lon, latitude = lat, heightM = 990, headingDeg = 10.5 });
        corrected.StatusCode.ShouldBe(HttpStatusCode.OK, await corrected.Content.ReadAsStringAsync());
        (await corrected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("placementHeadingDeg").GetDouble().ShouldBe(10.5);
        (await ScansAtAsync(owner, lon, lat)).GetProperty("items")[0].GetProperty("headingDeg").GetDouble().ShouldBe(10.5);

        (await ArchiveIdAsync(scan.ModelId)).ShouldBe(archiveBefore, "placing a scan never tiles it again");
        (await JobCountAsync()).ShouldBe(jobsBefore, "placing a scan queues no work");
        var root = await nobody.GetAsync(scan.TilesetUrl);
        root.StatusCode.ShouldBe(HttpStatusCode.OK, "the address handed out before the placement still opens");

        // Taken away: viewable in its own frame still, off the map.
        var unplaced = await owner.DeleteAsync($"/api/v1/survey-models/{scan.ModelId}/placement");
        unplaced.StatusCode.ShouldBe(HttpStatusCode.OK, await unplaced.Content.ReadAsStringAsync());
        var gone = await unplaced.Content.ReadFromJsonAsync<JsonElement>();
        gone.GetProperty("anchorLongitude").ValueKind.ShouldBe(JsonValueKind.Null);
        gone.GetProperty("placementHeadingDeg").ValueKind.ShouldBe(JsonValueKind.Null);
        gone.GetProperty("placementSource").ValueKind.ShouldBe(JsonValueKind.Null);
        (await ScansAtAsync(owner, lon, lat)).GetProperty("total").GetInt32().ShouldBe(0);
        (await nobody.GetAsync(scan.TilesetUrl)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_placement_off_the_world_or_outside_the_height_band_is_refused_as_validation()
    {
        var scan = await TiledScanAsync(owner);
        foreach (var body in new object[]
        {
            new { longitude = 181, latitude = 45, heightM = 900, headingDeg = 0 },
            new { longitude = 25, latitude = -90.5, heightM = 900, headingDeg = 0 },
            new { longitude = 25, latitude = 45, heightM = 9500, headingDeg = 0 },
            new { longitude = 25, latitude = 45, heightM = -600, headingDeg = 0 },
        })
        {
            var refused = await owner.PutAsJsonAsync($"/api/v1/survey-models/{scan.ModelId}/placement", body);
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, JsonSerializer.Serialize(body));
        }

        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-models/{scan.ModelId}"))
            .GetProperty("anchorLongitude").ValueKind.ShouldBe(JsonValueKind.Null, "nothing refused was written");
    }

    [Fact]
    public async Task Placing_needs_write_on_the_cave_with_its_exact_location_and_the_placement_is_read_under_the_same_right()
    {
        // A protected cave the reader may read, write and place exactly: they place the scan.
        var caveId = await CreateCaveAsync(locationProtected: true);
        await GrantAsync(caveId, "read, write, viewExactLocation");
        var scan = await TiledScanAsync(owner, caveId);
        var body = new { longitude = 25.4472, latitude = 45.5312, heightM = 952, headingDeg = 90 };

        (await nobody.PutAsJsonAsync($"/api/v1/survey-models/{scan.ModelId}/placement", body))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await nobody.DeleteAsync($"/api/v1/survey-models/{scan.ModelId}/placement"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var placed = await reader.PutAsJsonAsync($"/api/v1/survey-models/{scan.ModelId}/placement", body);
        placed.StatusCode.ShouldBe(HttpStatusCode.OK, await placed.Content.ReadAsStringAsync());
        (await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-models/{scan.ModelId}"))
            .GetProperty("placementHeadingDeg").GetDouble().ShouldBe(90);

        // May see the cave and its position, may not write it: forbidden, and still reads the placement.
        await GrantAsync(caveId, "read, viewExactLocation");
        var forbidden = await reader.PutAsJsonAsync($"/api/v1/survey-models/{scan.ModelId}/placement", body with { headingDeg = 180 });
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await forbidden.Content.ReadAsStringAsync());
        (await reader.DeleteAsync($"/api/v1/survey-models/{scan.ModelId}/placement")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-models/{scan.ModelId}"))
            .GetProperty("placementHeadingDeg").GetDouble().ShouldBe(90, "a refused write changes nothing");

        // May read and write the cave, but its position is closed to them: the model is not
        // theirs to find, and the placement — the position by another name — is told to nobody
        // without the right, however much else they may do to the cave.
        await GrantAsync(caveId, "read, write");
        var closed = await reader.PutAsJsonAsync($"/api/v1/survey-models/{scan.ModelId}/placement", body);
        closed.StatusCode.ShouldBe(HttpStatusCode.NotFound, await closed.Content.ReadAsStringAsync());
        (await reader.DeleteAsync($"/api/v1/survey-models/{scan.ModelId}/placement")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/survey-models/{scan.ModelId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-models")).GetArrayLength().ShouldBe(0);
        (await ScansAtAsync(reader, 25.4472, 45.5312)).GetProperty("total").GetInt32().ShouldBe(0);

        // The owner, who holds every right, reads what was placed.
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-models/{scan.ModelId}"))
            .GetProperty("placementHeadingDeg").GetDouble().ShouldBe(90);
        (await ScansAtAsync(owner, 25.4472, 45.5312)).GetProperty("items").EnumerateArray()
            .ShouldContain(i => i.GetProperty("surveyModelId").GetGuid() == scan.ModelId);
    }

    [Fact]
    public async Task A_model_that_is_not_a_point_cloud_cannot_be_placed_this_way()
    {
        var caveId = await CreateCaveAsync();
        var scan = await TiledScanAsync(owner, caveId);
        Guid linePlotId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var fileId = (await db.SurveyModels.AsNoTracking().SingleAsync(m => m.Id == scan.ModelId)).FileId;
            var linePlot = new SurveyModel
            {
                CaveFeatureId = caveId,
                Name = "a line plot",
                FileId = fileId,
                Format = SurveyModelFormat.Survex3d,
                Status = SurveyModelStatus.Ready,
            };
            db.SurveyModels.Add(linePlot);
            await db.SaveChangesAsync();
            linePlotId = linePlot.Id;
        }

        var body = new { longitude = 25.4472, latitude = 45.5312, heightM = 952, headingDeg = 0 };
        var refused = await owner.PutAsJsonAsync($"/api/v1/survey-models/{linePlotId}/placement", body);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync());
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("survey_model.not_placeable");
        var refusedToo = await owner.DeleteAsync($"/api/v1/survey-models/{linePlotId}/placement");
        refusedToo.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // The scan beside it is placed, as a scan is.
        (await owner.PutAsJsonAsync($"/api/v1/survey-models/{scan.ModelId}/placement", body)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_file_that_names_its_grid_is_placed_from_it_when_tiled_facing_grid_north()
    {
        shift = [Easting, Northing, 930];
        try
        {
            var scan = await TiledScanAsync(owner, epsg: Utm35N);
            var expected = new ProjCoordinateProjector().ToWgs84(Utm35N, Easting, Northing).ShouldNotBeNull();

            var dto = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-models/{scan.ModelId}");
            dto.GetProperty("anchorLongitude").GetDouble().ShouldBe(expected.Longitude, tolerance: 1e-9);
            dto.GetProperty("anchorLatitude").GetDouble().ShouldBe(expected.Latitude, tolerance: 1e-9);
            dto.GetProperty("anchorHeightM").GetDouble().ShouldBe(930, "the zero point's own Z is the zero plane's altitude");
            dto.GetProperty("placementHeadingDeg").GetDouble().ShouldBe(expected.ConvergenceDeg, tolerance: 1e-9);
            dto.GetProperty("placementSource").GetString().ShouldBe("fileCrs");

            var listed = await ScansAtAsync(owner, expected.Longitude, expected.Latitude);
            listed.GetProperty("items").EnumerateArray()
                .ShouldContain(i => i.GetProperty("surveyModelId").GetGuid() == scan.ModelId);

            // A file without a system is left for somebody to place.
            var local = await TiledScanAsync(owner);
            (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-models/{local.ModelId}"))
                .GetProperty("placementSource").ValueKind.ShouldBe(JsonValueKind.Null);
        }
        finally
        {
            shift = [0.5, 0.5, 0.5];
        }
    }

    [Fact]
    public async Task A_file_that_spells_its_grid_out_as_text_with_no_code_is_placed_from_it_when_tiled()
    {
        shift = [Easting, Northing, 930];
        try
        {
            var scan = await TiledScanAsync(owner, wkt: Utm35NWkt);
            var expected = new ProjCoordinateProjector().ToWgs84(Utm35N, Easting, Northing).ShouldNotBeNull();

            var dto = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-models/{scan.ModelId}");
            dto.GetProperty("anchorLongitude").GetDouble().ShouldBe(expected.Longitude, tolerance: 1e-9);
            dto.GetProperty("anchorLatitude").GetDouble().ShouldBe(expected.Latitude, tolerance: 1e-9);
            dto.GetProperty("anchorHeightM").GetDouble().ShouldBe(930);
            dto.GetProperty("placementHeadingDeg").GetDouble().ShouldBe(expected.ConvergenceDeg, tolerance: 1e-9);
            dto.GetProperty("placementSource").GetString().ShouldBe("fileCrs");
            (await SourceEpsgAsync(scan.ModelId)).ShouldBe(Utm35N, "the text was recognised as the register's zone");

            var listed = await ScansAtAsync(owner, expected.Longitude, expected.Latitude);
            listed.GetProperty("items").EnumerateArray()
                .ShouldContain(i => i.GetProperty("surveyModelId").GetGuid() == scan.ModelId);
        }
        finally
        {
            shift = [0.5, 0.5, 0.5];
        }
    }

    [Fact]
    public async Task A_placement_made_by_hand_while_a_file_with_a_grid_is_being_tiled_is_kept()
    {
        shift = [Easting, Northing, 930];
        var caveId = await CreateCaveAsync();
        var uploadId = await SendAsync(owner, $"Scan {suffix}", caveId, ("scan.laz", SyntheticLas.Las(points: 3, compressed: true, epsg: Utm35N)));
        Guid modelId = default;
        var placedWhileTiling = default(HttpStatusCode);
        whileTiling = async () =>
        {
            // The worker is tiling; meanwhile somebody places the scan on an entrance by hand.
            var upload = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{uploadId}");
            modelId = upload.GetProperty("pointCloud").GetProperty("modelId").GetGuid();
            var placed = await owner.PutAsJsonAsync($"/api/v1/survey-models/{modelId}/placement",
                new { longitude = 25.4472, latitude = 45.5312, heightM = 952, headingDeg = 300 });
            placedWhileTiling = placed.StatusCode;
        };
        try
        {
            await RunPipelineAsync(uploadId);
        }
        finally
        {
            whileTiling = null;
            shift = [0.5, 0.5, 0.5];
        }

        placedWhileTiling.ShouldBe(HttpStatusCode.OK, "a scan is placeable before it has tiles");
        modelId.ShouldNotBe(Guid.Empty);

        // The run ended with tiles and a file that could place the scan; the hand's placement stands.
        var dto = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-models/{modelId}");
        (await ArchiveIdAsync(modelId)).ShouldNotBeNull("the run tiled the scan");
        dto.GetProperty("anchorLongitude").GetDouble().ShouldBe(25.4472, tolerance: 1e-9);
        dto.GetProperty("anchorLatitude").GetDouble().ShouldBe(45.5312, tolerance: 1e-9);
        dto.GetProperty("anchorHeightM").GetDouble().ShouldBe(952);
        dto.GetProperty("placementHeadingDeg").GetDouble().ShouldBe(300);
        dto.GetProperty("placementSource").GetString().ShouldBe("manual");
    }

    // ---- what the canned worker answers with ----

    /// <summary>Where the invented survey truly has the scan: what a fit is to find again.</summary>
    private static readonly ScanPlacement Truth = new(25.4472, 45.5312, 952, 40, ScanPlacementSource.Manual);

    /// <summary>Four points picked in the scan, spread over a passage some thirty metres long.</summary>
    private static readonly (string Station, double X, double Y, double Z)[] Picks =
    [
        ("st.1", 1, 2, 0.5),
        ("st.2", 12, -3, 1.0),
        ("st.3", 25, 6, -2.0),
        ("st.4", 31, 15, 0.0),
    ];

    [Fact]
    public async Task A_scan_placed_by_control_points_is_fitted_to_its_stations_with_residuals_and_the_wrong_pair_is_flagged()
    {
        var caveId = await CreateCaveAsync();
        var scan = await TiledScanAsync(owner, caveId);
        // The fourth station is drawn two metres east of where the scan has it: a pair put on
        // the wrong station, which the fit is to flag and the other three are to stay close to.
        await LinePlotAsync(caveId, scan.ModelId, wrongBy: 2);
        var jobsBefore = await JobCountAsync();

        var placed = await owner.PutAsJsonAsync($"/api/v1/survey-models/{scan.ModelId}/placement/control-points", new
        {
            pairs = Picks.Select(p => new { scanX = p.X, scanY = p.Y, scanZ = p.Z, station = p.Station }),
        });
        placed.StatusCode.ShouldBe(HttpStatusCode.OK, await placed.Content.ReadAsStringAsync());
        var dto = await placed.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("placementSource").GetString().ShouldBe("controlPoints");
        dto.GetProperty("placementHeadingDeg").GetDouble().ShouldBe(40, tolerance: 3);
        dto.GetProperty("placementPitchDeg").ValueKind.ShouldBe(JsonValueKind.Null, "no tilt was asked for");
        var fit = dto.GetProperty("placementControlPoints");
        var pairs = fit.GetProperty("pairs").EnumerateArray().ToList();
        pairs.Count.ShouldBe(4);
        pairs.Select(p => p.GetProperty("station").GetString()).ShouldBe(["st.1", "st.2", "st.3", "st.4"]);
        pairs[3].GetProperty("flagged").GetBoolean().ShouldBeTrue();
        pairs[3].GetProperty("distanceM").GetDouble().ShouldBeGreaterThan(pairs[0].GetProperty("distanceM").GetDouble());
        fit.GetProperty("warnM").GetDouble().ShouldBe(ScanControlPoints.DefaultWarnM);
        fit.GetProperty("rmsM").GetDouble().ShouldBeGreaterThan(0);
        (await JobCountAsync()).ShouldBe(jobsBefore, "re-placing a scan never tiles it again");

        // The placement and its pairs are what the model reads back.
        var read = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-models/{scan.ModelId}");
        read.GetProperty("placementControlPoints").GetProperty("pairs").GetArrayLength().ShouldBe(4);

        // A placement by hand replaces it, and the pairs no longer describe it.
        var byHand = await owner.PutAsJsonAsync($"/api/v1/survey-models/{scan.ModelId}/placement",
            new { longitude = 25.4472, latitude = 45.5312, heightM = 952, headingDeg = 10 });
        byHand.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await byHand.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("placementControlPoints").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Exact_pairs_place_the_scan_where_the_survey_has_it()
    {
        var caveId = await CreateCaveAsync();
        var scan = await TiledScanAsync(owner, caveId);
        await LinePlotAsync(caveId, scan.ModelId, wrongBy: 0);

        var placed = await owner.PutAsJsonAsync($"/api/v1/survey-models/{scan.ModelId}/placement/control-points", new
        {
            pairs = Picks.Select(p => new { scanX = p.X, scanY = p.Y, scanZ = p.Z, station = p.Station }),
            fitTilt = true,
        });
        placed.StatusCode.ShouldBe(HttpStatusCode.OK, await placed.Content.ReadAsStringAsync());
        var dto = await placed.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("anchorLongitude").GetDouble().ShouldBe(Truth.Longitude, tolerance: 1e-7);
        dto.GetProperty("anchorLatitude").GetDouble().ShouldBe(Truth.Latitude, tolerance: 1e-7);
        dto.GetProperty("anchorHeightM").GetDouble().ShouldBe(Truth.HeightM, tolerance: 0.01);
        dto.GetProperty("placementHeadingDeg").GetDouble().ShouldBe(Truth.HeadingDeg, tolerance: 0.01);
        var fit = dto.GetProperty("placementControlPoints");
        fit.GetProperty("rmsM").GetDouble().ShouldBeLessThan(0.01);
        fit.GetProperty("tiltFitted").GetBoolean().ShouldBeTrue();
        fit.GetProperty("pairs").EnumerateArray().ShouldAllBe(p => !p.GetProperty("flagged").GetBoolean());
    }

    [Fact]
    public async Task Control_points_that_cannot_place_the_scan_are_refused_with_a_code()
    {
        var caveId = await CreateCaveAsync();
        var scan = await TiledScanAsync(owner, caveId);
        var url = $"/api/v1/survey-models/{scan.ModelId}/placement/control-points";
        var two = Picks.Take(2).Select(p => new { scanX = p.X, scanY = p.Y, scanZ = p.Z, station = p.Station }).ToArray();

        // No line plot yet: no stations to place by.
        await RefusedAsync(await owner.PutAsJsonAsync(url, new { pairs = two }), HttpStatusCode.Conflict, "survey_model.control_points_no_line_plot");

        await LinePlotAsync(caveId, scan.ModelId, wrongBy: 0);
        await RefusedAsync(await owner.PutAsJsonAsync(url, new { pairs = two.Take(1) }), HttpStatusCode.BadRequest, "validation.failed");
        await RefusedAsync(await owner.PutAsJsonAsync(url, new { pairs = two, fitTilt = true }), HttpStatusCode.BadRequest, "validation.failed");
        // A null element is a malformed request, not a server fault.
        await RefusedAsync(
            await owner.PutAsync(url, new StringContent("{\"pairs\":[null,null]}", System.Text.Encoding.UTF8, "application/json")),
            HttpStatusCode.BadRequest,
            "validation.failed");
        await RefusedAsync(
            await owner.PutAsJsonAsync(url, new { pairs = new[] { two[0], two[1] with { station = "no.such" } } }),
            HttpStatusCode.BadRequest,
            "survey_model.control_points_station_not_found");
        await RefusedAsync(
            await owner.PutAsJsonAsync(url, new { pairs = new[] { two[0], two[0] with { station = "st.2" } } }),
            HttpStatusCode.BadRequest,
            ScanControlPoints.ScanPointsTooClose);

        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-models/{scan.ModelId}"))
            .GetProperty("placementSource").ValueKind.ShouldBe(JsonValueKind.Null, "nothing refused placed the scan");
    }

    [Fact]
    public async Task Placing_by_control_points_needs_write_on_the_cave_with_its_exact_location()
    {
        var caveId = await CreateCaveAsync(locationProtected: true);
        var scan = await TiledScanAsync(owner, caveId);
        await LinePlotAsync(caveId, scan.ModelId, wrongBy: 0);
        var url = $"/api/v1/survey-models/{scan.ModelId}/placement/control-points";
        var body = new { pairs = Picks.Select(p => new { scanX = p.X, scanY = p.Y, scanZ = p.Z, station = p.Station }) };

        (await nobody.PutAsJsonAsync(url, body)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Read, write and the exact location: the reader places it and reads the pairs back.
        await GrantAsync(caveId, "read, write, viewExactLocation");
        var placed = await reader.PutAsJsonAsync(url, body);
        placed.StatusCode.ShouldBe(HttpStatusCode.OK, await placed.Content.ReadAsStringAsync());
        (await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-models/{scan.ModelId}"))
            .GetProperty("placementControlPoints").GetProperty("pairs").GetArrayLength().ShouldBe(4);

        // The position open, no write: forbidden.
        await GrantAsync(caveId, "read, viewExactLocation");
        (await reader.PutAsJsonAsync(url, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Write, but the position closed: the model and its pairs are nobody's to find.
        await GrantAsync(caveId, "read, write");
        (await reader.PutAsJsonAsync(url, body)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/survey-models/{scan.ModelId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The owner still reads what the reader placed.
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-models/{scan.ModelId}"))
            .GetProperty("placementSource").GetString().ShouldBe("controlPoints");
    }

    [Fact]
    public async Task A_model_that_is_not_a_point_cloud_cannot_be_placed_by_control_points()
    {
        var caveId = await CreateCaveAsync();
        var scan = await TiledScanAsync(owner, caveId);
        var linePlotId = await LinePlotAsync(caveId, scan.ModelId, wrongBy: 0);
        var refused = await owner.PutAsJsonAsync($"/api/v1/survey-models/{linePlotId}/placement/control-points", new
        {
            pairs = Picks.Select(p => new { scanX = p.X, scanY = p.Y, scanZ = p.Z, station = p.Station }),
        });
        await RefusedAsync(refused, HttpStatusCode.Conflict, "survey_model.not_placeable");
    }

    private static async Task RefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, body);
        JsonDocument.Parse(body).RootElement.GetProperty("code").GetString().ShouldBe(code, body);
    }

    /// <summary>
    /// The cave's current line plot, written straight into the tables: its stations stand where
    /// <see cref="Truth"/> puts the picked points, the last one moved east by <paramref name="wrongBy"/> metres.
    /// </summary>
    private async Task<Guid> LinePlotAsync(Guid caveId, Guid scanId, double wrongBy)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var fileId = (await db.SurveyModels.AsNoTracking().SingleAsync(m => m.Id == scanId)).FileId;
        var linePlot = new SurveyModel
        {
            CaveFeatureId = caveId,
            Name = "the survey",
            FileId = fileId,
            Format = SurveyModelFormat.Survex3d,
            Status = SurveyModelStatus.Ready,
            IsCurrent = true,
        };
        db.SurveyModels.Add(linePlot);
        for (var i = 0; i < Picks.Length; i++)
        {
            var (station, x, y, z) = Picks[i];
            var (lon, lat, h) = Truth.PositionOf(x, y, z);
            if (i == Picks.Length - 1 && wrongBy != 0)
            {
                (lon, lat, h) = new SilexGis.Domain.Geo.LocalTangentFrame(lon, lat, h).GeodeticAt(wrongBy, 0, 0);
            }

            db.SurveyStations.Add(new SurveyStation
            {
                SurveyModelId = linePlot.Id,
                Name = station,
                Position = new Point(new CoordinateZ(lon, lat, h)) { SRID = 4326 },
                Flags = SurveyStationFlags.Underground,
            });
        }

        await db.SaveChangesAsync();
        return linePlot.Id;
    }

    private static readonly byte[] Tileset = Encoding.UTF8.GetBytes(
        """{"asset":{"version":"1.0"},"geometricError":4.0,"root":{"boundingVolume":{"box":[0,0,0,2,0,0,0,2,0,0,0,2]},"geometricError":1.0,"refine":"ADD","content":{"uri":"r.pnts"}}}""");

    private static readonly byte[] Pnts = [.. Encoding.ASCII.GetBytes("pnts"), 1, 0, 0, 0, 40, 0, 0, 0, .. Enumerable.Range(0, 28).Select(i => (byte)(i * 7))];

    private static readonly byte[] TilesZip = MakeZip();

    private static byte[] MakeZip()
    {
        using var stream = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in new[] { ("tileset.json", Tileset), ("r.pnts", Pnts) })
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(bytes);
            }
        }

        return stream.ToArray();
    }

    /// <summary>Estimates a cloud small enough to tile whole, then tiles it about <see cref="shift"/>.</summary>
    private async Task Tiling(string directory, JsonElement request)
    {
        var step = request.GetProperty("step").GetString();
        switch (step)
        {
            case PointCloudEstimateStep.StepId:
            {
                var data = new
                {
                    pointCount = 30,
                    hasRgb = false,
                    hasIntensity = true,
                    full = new { points = 30, estimatedTileBytes = 510, estimatedSeconds = 17 },
                    voxels = new[] { new { voxelCm = 5.0, points = 5, estimatedTileBytes = 85, estimatedSeconds = 17 } },
                };
                var output = CannedScanWorker.Output(directory, "estimate", "estimate.json", JsonSerializer.SerializeToUtf8Bytes(data));
                CannedScanWorker.Finish(directory, "ok", [output], data);
                break;
            }

            case Py3dtilesTileStep.StepId:
            {
                if (whileTiling is { } meanwhile)
                {
                    await meanwhile();
                }

                var output = CannedScanWorker.Output(directory, "pointCloudTiles", "tiles.zip", TilesZip);
                CannedScanWorker.Finish(
                    directory, "ok", [output],
                    new { pointCount = 30, originShift = shift, tileCount = 1, tileBytes = Pnts.Length, archiveBytes = TilesZip.LongLength, intensity = true, rgb = false },
                    tool: "py3dtiles");
                break;
            }

            default:
                CannedScanWorker.Finish(directory, "refused", [], reason: $"step '{step}' is not one this worker runs");
                break;
        }
    }

    // ---- helpers ----

    private sealed record Scan(Guid UploadId, Guid ModelId, string TilesetUrl);

    /// <summary>A point cloud sent, its header read and its tiles stored by the canned worker.</summary>
    private async Task<Scan> TiledScanAsync(HttpClient client, Guid? caveId = null, int? epsg = null, string? wkt = null)
    {
        caveId ??= await CreateCaveAsync();
        var uploadId = await SendAsync(client, $"Scan {suffix}", caveId.Value, ("scan.laz", SyntheticLas.Las(points: 3, compressed: true, epsg: epsg, wkt: wkt)));
        await RunPipelineAsync(uploadId);
        var upload = await client.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{uploadId}");
        var cloud = upload.GetProperty("pointCloud");
        return new Scan(uploadId, cloud.GetProperty("modelId").GetGuid(), cloud.GetProperty("tilesetUrl").GetString()!);
    }

    /// <summary>The placed scans in a small box about a point, as the caller sees them.</summary>
    private static async Task<JsonElement> ScansAtAsync(HttpClient client, double lon, double lat)
    {
        var bbox = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{lon - 0.01},{lat - 0.01},{lon + 0.01},{lat + 0.01}");
        var response = await client.GetAsync($"/api/v1/map/cave-scans?bbox={bbox}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<Guid?> ArchiveIdAsync(Guid modelId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (await db.SurveyModels.AsNoTracking().SingleAsync(m => m.Id == modelId)).ConvertedFileId;
    }

    private async Task<int?> SourceEpsgAsync(Guid modelId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (await db.SurveyModels.AsNoTracking().SingleAsync(m => m.Id == modelId)).SourceEpsg;
    }

    private async Task<int> JobCountAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ProcessingJobs.AsNoTracking().CountAsync(j => j.Kind == ProcessingJobKinds.PointCloudPipeline);
    }

    private static object File_(string name, long size) => new { name, size, role = "archive" };

    private static async Task<Guid> SendAsync(HttpClient client, string title, Guid caveId, (string Name, byte[] Bytes) file)
    {
        var response = await client.PostAsJsonAsync("/api/v1/survey-uploads", new
        {
            source = "pointcloud",
            cave = new { id = caveId },
            client = new { name = "silexgis tests", version = "1" },
            surveyTitle = title,
            files = new[] { File_(file.Name, file.Bytes.LongLength) },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var content = new ByteArrayContent(file.Bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var put = await client.PutAsync($"/api/v1/survey-uploads/{id}/files/{Uri.EscapeDataString(file.Name)}", content);
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        var complete = await client.PostAsync($"/api/v1/survey-uploads/{id}/complete", null);
        complete.StatusCode.ShouldBe(HttpStatusCode.Accepted, await complete.Content.ReadAsStringAsync());
        return id;
    }

    /// <summary>Runs the processing job the upload's completion queued, exactly once.</summary>
    private async Task RunPipelineAsync(Guid uploadId)
    {
        long jobId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var queued = await db.ProcessingJobs.AsNoTracking()
                .Where(j => j.Kind == ProcessingJobKinds.PointCloudPipeline && j.Status == ProcessingJobStatus.Queued)
                .ToListAsync();
            jobId = queued.Single(j =>
                JsonSerializer.Deserialize<SurveyUploadPipelinePayload>(j.Payload, JsonSerializerOptions.Web)!.SurveyUploadId == uploadId).Id;
        }

        await QueuedJob.RunAsync(factory.Services, jobId);
    }

    private async Task<Guid> CreateCaveAsync(bool locationProtected = false)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Place Cave {Guid.NewGuid():N}"[..28],
            caveTypeId,
            visibility = "private",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>The reader's entries on the cave, replacing whatever they had; null leaves them none.</summary>
    private async Task GrantAsync(Guid featureId, string? actions)
    {
        var grant = await owner.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/access", new
        {
            entries = actions is null
                ? Array.Empty<object>()
                : [new { subjectKind = "user", subjectId = readerId, effect = "allow", actions, scopeKind = "object" }],
        });
        grant.StatusCode.ShouldBe(HttpStatusCode.OK, await grant.Content.ReadAsStringAsync());
    }
}
