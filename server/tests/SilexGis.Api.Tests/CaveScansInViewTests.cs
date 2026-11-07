// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
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
using SilexGis.Domain.Filters;
using SilexGis.Domain.Scans;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Scans;

namespace SilexGis.Api.Tests;

/// <summary>
/// The placed point clouds of the caves in a view: which scan answers for a cave, which caves are
/// in the answer at all, in what order, and how many — and that a scan which was tiled but never
/// placed, or placed but never tiled, is in neither the items nor the count.
/// </summary>
/// <remarks>
/// <para>
/// Every test works in a box of its own, drawn somewhere on open ocean at random, because the
/// database is the one every class shares and a box is a query over all of it.
/// </para>
/// <para>
/// Scans are sent through the upload route and tiled by a canned worker on the far side of the
/// hand-off directory, as the tile delivery tests do it; they are then placed by writing the
/// anchor onto the row, which is what a placement does to it however it is asked for.
/// </para>
/// </remarks>
public sealed class CaveScansInViewTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    /// <summary>Half the side of a test's box, in degrees: a little over two kilometres.</summary>
    private const double HalfBox = 0.02;

    private const int CappedCaves = 2;
    private const int CappedCavesCeiling = 3;
    private const int CappedMinZoom = 16;
    private const long CappedBytes = 4096;

    private readonly SilexGisApiFactory factory;
    private readonly SilexGisApiFactory capped;
    private readonly string connectionString;
    private readonly Dictionary<string, string?> settings;
    private readonly string filesRoot;
    private readonly string handoffRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];
    private CannedScanWorker worker = null!;

    private string ownerEmail = null!;
    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private HttpClient cappedOwner = null!;
    private Guid readerId;
    private long caveTypeId;
    private long karstAreaTypeId;

    /// <summary>When set, the canned worker refuses to tile, leaving a scan whose header was read and which has no archive.</summary>
    private bool tileRefused;

    public CaveScansInViewTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-csv-{Guid.NewGuid():N}");
        handoffRoot = Path.Combine(filesRoot, "handoff");
        settings = new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            ["Scans:ProcessingEnabled"] = "true",
            ["Scans:HandoffRoot"] = handoffRoot,
            ["Scans:PickupSeconds"] = "1",
            ["Scans:JobTimeoutSeconds"] = "5",
        };

        // No background worker in either host: each test runs the jobs it queued itself.
        factory = new SilexGisApiFactory(postgres.ConnectionString, settings, JobWorkers.RemoveFrom);
        capped = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>(settings)
            {
                ["Map:ScansInViewMaxCaves"] = CappedCaves.ToString(CultureInfo.InvariantCulture),
                ["Map:ScansInViewMaxCavesLimit"] = CappedCavesCeiling.ToString(CultureInfo.InvariantCulture),
                ["Map:ScansInViewMinZoom"] = CappedMinZoom.ToString(CultureInfo.InvariantCulture),
                ["Map:ScansInViewMaxBytes"] = CappedBytes.ToString(CultureInfo.InvariantCulture),
            },
            JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        ownerEmail = $"csv-own-{suffix}@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, ownerEmail);
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"csv-read-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, ownerEmail);
        reader = await AuthHelper.BearerClientAsync(factory, $"csv-read-{suffix}@t.local");
        cappedOwner = await AuthHelper.BearerClientAsync(capped, ownerEmail);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            karstAreaTypeId = await db.FeatureTypes.Where(t => t.Code == "karst_area").Select(t => t.Id).SingleAsync();
        }

        var handoff = factory.Services.GetRequiredService<ScanWorkerHandoff>();
        handoff.PollInterval = TimeSpan.FromMilliseconds(50);
        handoff.WithdrawalWait = TimeSpan.FromSeconds(1);

        CannedScanWorker.Announce(handoffRoot, PointCloudEstimateStep.StepId, PointCloudDownsampleStep.StepId, Py3dtilesTileStep.StepId);
        worker = new CannedScanWorker(handoffRoot) { Answer = Tiling };
    }

    [Fact]
    public async Task Placed_tiled_scans_in_the_box_answer_nearest_first_and_their_address_opens_the_tileset()
    {
        var spot = Spot.Somewhere();

        // Created far-to-near on purpose, so that an answer in creation order would be wrong.
        var outside = await CreateCaveWithScanAsync(spot.North(0.05));
        var far = await CreateCaveWithScanAsync(spot.North(0.012), heightM: 1420);
        var near = await CreateCaveWithScanAsync(spot.North(0.001), heightM: 980);
        // Tiled and never placed: there are tiles, and nothing to set them down on.
        var unplaced = await CreateCaveWithScanAsync(spot.North(-0.002), place: false);
        // Placed, and its tiling refused: there is a position, and nothing to draw at it.
        var untiled = await CreateCaveWithScanAsync(spot.North(-0.003), tiled: false);

        var answer = await ScansAsync(owner, spot);

        answer.Total.ShouldBe(2);
        answer.CaveIds.ShouldBe([near.CaveId, far.CaveId]);
        answer.CaveIds.ShouldNotContain(outside.CaveId);
        answer.CaveIds.ShouldNotContain(unplaced.CaveId);
        answer.CaveIds.ShouldNotContain(untiled.CaveId);

        var first = answer.Items[0];
        first.GetProperty("surveyModelId").GetGuid().ShouldBe(near.ModelId);
        first.GetProperty("caveName").GetString().ShouldBe(near.CaveName);
        first.GetProperty("anchorLongitude").GetDouble().ShouldBe(spot.Longitude, tolerance: 1e-9);
        first.GetProperty("anchorLatitude").GetDouble().ShouldBe(spot.Latitude + 0.001, tolerance: 1e-9);
        first.GetProperty("anchorHeightM").GetDouble().ShouldBe(980);
        first.GetProperty("pointCount").GetInt64().ShouldBe(3, "the header of the synthetic cloud");
        first.GetProperty("tiledPointCount").GetInt64().ShouldBe(3, "the full cloud was tiled, so the tiles hold every point");
        first.GetProperty("sizeBytes").GetInt64().ShouldBe(TilesZip.LongLength, "the archive's size is what a scene budgets by");
        answer.Items[1].GetProperty("anchorHeightM").GetDouble().ShouldBe(1420);

        // The address is the scan's own tile route, signed, and it opens the root tileset without
        // any further credential — a tiles reader cannot send a header.
        var address = first.GetProperty("tilesetUrl").GetString().ShouldNotBeNull();
        address.ShouldStartWith($"/api/v1/survey-models/{near.ModelId}/tiles/tileset.json?token=");
        var root = await factory.CreateClient().GetAsync(address);
        root.StatusCode.ShouldBe(HttpStatusCode.OK, await root.Content.ReadAsStringAsync());
        (await root.Content.ReadAsByteArrayAsync()).ShouldBe(Tileset);
    }

    [Fact]
    public async Task The_current_scan_answers_for_a_cave_and_an_older_one_only_when_the_current_is_not_placed_or_tiled()
    {
        var spot = Spot.Somewhere();
        var cave = await CreateCaveWithScanAsync(spot.North(0.001));
        var older = cave.ModelId;
        // A second scan of the same cave, tiled and placed, takes the current mark.
        var newer = await AddScanAsync(cave.CaveId, spot.North(0.001));
        (await owner.PutAsync($"/api/v1/survey-models/{newer}/current", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ScansAsync(owner, spot)).ModelIds.ShouldBe([newer]);

        // The current scan loses its placement: the older, still-placed one stands in for it.
        await UnplaceAsync(newer);
        (await ScansAsync(owner, spot)).ModelIds.ShouldBe([older]);

        // A current scan placed outside the box is not answered by an older one inside it.
        await PlaceAsync(newer, spot.North(0.2), 1000);
        var moved = await ScansAsync(owner, spot);
        moved.Items.ShouldBeEmpty();
        moved.Total.ShouldBe(0);
    }

    /// <summary>
    /// The placement is the cave's exact position, so a reader without the exact-location right is
    /// told nothing about a protected cave's scan — not even that there is one — while the scan's
    /// owner, and a reader once granted, see it. The positive half is asserted in the same test
    /// so that the empty answer is the rule and not a query that finds nothing.
    /// </summary>
    [Fact]
    public async Task A_location_protected_cave_s_scan_is_absent_and_uncounted_until_its_exact_location_is_granted()
    {
        var spot = Spot.Somewhere();
        var open = await CreateCaveWithScanAsync(spot.North(0.004));
        var closed = await CreateCaveWithScanAsync(spot.North(0.001), locationProtected: true);

        // The reader may read the protected cave itself; what is closed to them is where it is.
        (await reader.GetAsync($"/api/v1/caves/{closed.CaveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var withheld = await ScansAsync(reader, spot);
        withheld.CaveIds.ShouldBe([open.CaveId]);
        withheld.Total.ShouldBe(1);

        (await ScansAsync(owner, spot)).CaveIds.ShouldBe([closed.CaveId, open.CaveId]);

        await GrantExactViewAsync(closed.CaveId);

        var granted = await ScansAsync(reader, spot);
        granted.CaveIds.ShouldBe([closed.CaveId, open.CaveId]);
        granted.Total.ShouldBe(2);
    }

    [Fact]
    public async Task Protection_inherited_from_an_area_withholds_the_scans_of_the_caves_inside_it()
    {
        var spot = Spot.Somewhere();
        var areaId = await CreateProtectedAreaAsync();
        var inside = await CreateCaveWithScanAsync(spot.North(0.001), parentId: areaId);

        (await ScansAsync(owner, spot)).CaveIds.ShouldBe([inside.CaveId]);
        (await ScansAsync(reader, spot)).Total.ShouldBe(0);

        // A grant on the cave is not a grant on the root above it.
        await GrantExactViewAsync(inside.CaveId);
        (await ScansAsync(reader, spot)).Total.ShouldBe(0);

        await GrantExactViewAsync(areaId);
        (await ScansAsync(reader, spot)).CaveIds.ShouldBe([inside.CaveId]);
    }

    [Fact]
    public async Task A_private_cave_and_a_deleted_one_are_absent_and_uncounted()
    {
        var spot = Spot.Somewhere();
        var shared = await CreateCaveWithScanAsync(spot.North(0.001));
        var hidden = await CreateCaveWithScanAsync(spot.North(0.002), visibility: "private");
        var doomed = await CreateCaveWithScanAsync(spot.North(0.003));

        (await ScansAsync(owner, spot)).CaveIds.ShouldBe([shared.CaveId, hidden.CaveId, doomed.CaveId]);

        var outsider = await ScansAsync(reader, spot);
        outsider.CaveIds.ShouldBe([shared.CaveId, doomed.CaveId]);
        outsider.Total.ShouldBe(2);

        (await owner.DeleteAsync($"/api/v1/caves/{doomed.CaveId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterwards = await ScansAsync(owner, spot);
        afterwards.CaveIds.ShouldBe([shared.CaveId, hidden.CaveId]);
        afterwards.Total.ShouldBe(2);
    }

    [Fact]
    public async Task The_answer_is_capped_at_the_configured_count_or_the_caller_s_up_to_the_ceiling_and_the_total_counts_all()
    {
        var spot = Spot.Somewhere();
        var caves = new List<Guid>();
        for (var i = 1; i <= CappedCavesCeiling + 1; i++)
        {
            caves.Add((await CreateCaveWithScanAsync(spot.North(0.001 * i))).CaveId);
        }

        var byDefault = await ScansAsync(cappedOwner, spot);
        byDefault.Items.Count.ShouldBe(CappedCaves);
        byDefault.Total.ShouldBe(CappedCavesCeiling + 1);
        byDefault.CaveIds.ShouldBe(caves.Take(CappedCaves));

        var raised = await ScansAsync(cappedOwner, spot, maxCaves: CappedCavesCeiling + 1);
        raised.Items.Count.ShouldBe(CappedCavesCeiling, "a count above the ceiling is answered as the ceiling");
        raised.Total.ShouldBe(CappedCavesCeiling + 1);

        var lowered = await ScansAsync(cappedOwner, spot, maxCaves: 1);
        lowered.CaveIds.ShouldBe([caves[0]]);
        lowered.Total.ShouldBe(CappedCavesCeiling + 1);

        (await ScansAsync(cappedOwner, spot, maxCaves: 0)).Items.Count.ShouldBe(1, "never fewer than one cave");

        var shipped = await owner.GetFromJsonAsync<JsonElement>("/api/v1/map/config");
        shipped.GetProperty("scansInViewMinZoom").GetInt32().ShouldBe(14);
        shipped.GetProperty("scansInViewMaxCaves").GetInt32().ShouldBe(6);
        shipped.GetProperty("scansInViewMaxBytes").GetInt64().ShouldBe(256L * 1024 * 1024);
        shipped.GetProperty("scansInViewMaxCavesLimit").GetInt32().ShouldBe(30);
        shipped.GetProperty("scansInViewMaxBytesLimit").GetInt64().ShouldBe(2048L * 1024 * 1024);

        var configured = await cappedOwner.GetFromJsonAsync<JsonElement>("/api/v1/map/config");
        configured.GetProperty("scansInViewMinZoom").GetInt32().ShouldBe(CappedMinZoom);
        configured.GetProperty("scansInViewMaxCaves").GetInt32().ShouldBe(CappedCaves);
        configured.GetProperty("scansInViewMaxBytes").GetInt64().ShouldBe(CappedBytes);
        configured.GetProperty("scansInViewMaxCavesLimit").GetInt32().ShouldBe(CappedCavesCeiling);
        configured.GetProperty("scansInViewMaxBytesLimit").GetInt64().ShouldBe(2048L * 1024 * 1024);
    }

    [Fact]
    public async Task A_default_configured_above_its_ceiling_is_held_to_the_ceiling()
    {
        using var overreaching = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?>(settings)
            {
                ["Map:ScansInViewMaxCaves"] = "9",
                ["Map:ScansInViewMaxCavesLimit"] = "1",
                ["Map:ScansInViewMaxBytes"] = "4096",
                ["Map:ScansInViewMaxBytesLimit"] = "1024",
            },
            JobWorkers.RemoveFrom);
        using var client = await AuthHelper.BearerClientAsync(overreaching, ownerEmail);

        var config = await client.GetFromJsonAsync<JsonElement>("/api/v1/map/config");
        config.GetProperty("scansInViewMaxCaves").GetInt32().ShouldBe(1);
        config.GetProperty("scansInViewMaxCavesLimit").GetInt32().ShouldBe(1);
        config.GetProperty("scansInViewMaxBytes").GetInt64().ShouldBe(1024);
        config.GetProperty("scansInViewMaxBytesLimit").GetInt64().ShouldBe(1024);

        var spot = Spot.Somewhere();
        var near = await CreateCaveWithScanAsync(spot.North(0.001));
        _ = await CreateCaveWithScanAsync(spot.North(0.006));
        var answer = await ScansAsync(client, spot, maxCaves: 5);
        answer.CaveIds.ShouldBe([near.CaveId]);
        answer.Total.ShouldBe(2);
    }

    [Fact]
    public async Task Without_a_session_or_with_a_bad_box_the_listing_refuses()
    {
        var spot = Spot.Somewhere();
        (await factory.CreateClient().GetAsync($"/api/v1/map/cave-scans?bbox={spot.Box}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        foreach (var malformed in new[] { "not-a-box", "8.0,40.0", "9,41,8,40", "-181,40,-179,41" })
        {
            var refused = await owner.GetAsync($"/api/v1/map/cave-scans?bbox={malformed}");
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await refused.Content.ReadAsStringAsync()).ShouldContain("map.invalid_bbox");
        }

        var notANumber = await owner.GetAsync($"/api/v1/map/cave-scans?bbox={spot.Box}&maxCaves=many");
        notANumber.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await notANumber.Content.ReadAsStringAsync()).ShouldContain("request.binding_failed");
    }

    [Fact]
    public async Task A_filter_keeps_the_scans_of_the_caves_it_keeps_and_counts_only_those()
    {
        var spot = Spot.Somewhere();
        var kept = await CreateCaveWithScanAsync(spot.North(0.001));
        var other = await CreateCaveWithScanAsync(spot.North(0.002));

        (await ScansAsync(owner, spot)).Total.ShouldBe(2);

        var token = FilterToken.Encode(new FilterDocument
        {
            Scope =
            [
                new WorldScope("feature", new AllOfNode(
                [
                    new ConditionNode(FeatureFilterFields.Kind, FilterOp.Equals, [new IdValue("cave")]),
                    new ConditionNode(FeatureFilterFields.Name, FilterOp.Contains, [new TextValue(kept.CaveName)]),
                ])),
            ],
        });
        var filtered = await ScansAsync(owner, spot, extra: $"&filter={token}");
        filtered.CaveIds.ShouldBe([kept.CaveId]);
        filtered.CaveIds.ShouldNotContain(other.CaveId);
        filtered.Total.ShouldBe(1);

        var malformed = await owner.GetAsync($"/api/v1/map/cave-scans?bbox={spot.Box}&filter=eyJ2ZXJzaW9uIjox");
        malformed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await malformed.Content.ReadAsStringAsync()).ShouldContain("filter.malformed");
        var missing = await owner.GetAsync($"/api/v1/map/cave-scans?bbox={spot.Box}&savedFilterId={Guid.NewGuid()}");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await missing.Content.ReadAsStringAsync()).ShouldContain("saved_filter.not_found");
    }

    // ---- what the canned worker answers with ----

    private static readonly byte[] Tileset = Encoding.UTF8.GetBytes(
        """{"asset":{"version":"1.0"},"geometricError":4.0,"root":{"boundingVolume":{"box":[0,0,0,2,0,0,0,2,0,0,0,2]},"geometricError":1.0,"refine":"ADD","content":{"uri":"r.pnts"}}}""");

    private static readonly byte[] Pnts = [.. Encoding.ASCII.GetBytes("pnts"), 1, 0, 0, 0, 200, 0, 0, 0, .. Enumerable.Range(0, 188).Select(i => (byte)(i * 7))];

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

    /// <summary>Estimates a cloud small enough to tile whole, then tiles it — unless told to refuse.</summary>
    private Task Tiling(string directory, JsonElement request)
    {
        var step = request.GetProperty("step").GetString();
        switch (step)
        {
            case PointCloudEstimateStep.StepId:
            {
                var data = new
                {
                    pointCount = 3,
                    hasRgb = false,
                    hasIntensity = true,
                    full = new { points = 3, estimatedTileBytes = 510, estimatedSeconds = 1 },
                    voxels = new[] { new { voxelCm = 5.0, points = 2, estimatedTileBytes = 85, estimatedSeconds = 1 } },
                };
                var output = CannedScanWorker.Output(directory, "estimate", "estimate.json", JsonSerializer.SerializeToUtf8Bytes(data));
                CannedScanWorker.Finish(directory, "ok", [output], data);
                break;
            }

            case Py3dtilesTileStep.StepId when tileRefused:
                CannedScanWorker.Finish(directory, "failed", [], reason: "the test's tiler is switched off");
                break;

            case Py3dtilesTileStep.StepId:
            {
                var output = CannedScanWorker.Output(directory, "pointCloudTiles", "tiles.zip", TilesZip);
                CannedScanWorker.Finish(
                    directory, "ok", [output],
                    new { pointCount = 3, originShift = new[] { 0.5, 0.5, 0.5 }, tileCount = 1, tileBytes = Pnts.Length, archiveBytes = TilesZip.LongLength, intensity = true, rgb = false },
                    tool: "py3dtiles");
                break;
            }

            default:
                CannedScanWorker.Finish(directory, "refused", [], reason: $"step '{step}' is not one this worker runs");
                break;
        }

        return Task.CompletedTask;
    }

    // ---- helpers ----

    /// <summary>The middle of one test's box, and positions measured from it.</summary>
    private sealed record Spot(double Longitude, double Latitude)
    {
        /// <summary>A point on open ocean nobody else is using.</summary>
        public static Spot Somewhere() => new(
            Math.Round(-170d + (Random.Shared.NextDouble() * 60d), 6),
            Math.Round(-60d + (Random.Shared.NextDouble() * 30d), 6));

        /// <summary>The same meridian, this many degrees of latitude further north.</summary>
        public Spot North(double degrees) => new(Longitude, Latitude + degrees);

        public string Box => string.Join(
            ',',
            new[] { Longitude - HalfBox, Latitude - HalfBox, Longitude + HalfBox, Latitude + HalfBox }
                .Select(n => n.ToString("R", CultureInfo.InvariantCulture)));
    }

    private sealed record ScanCave(Guid CaveId, string CaveName, Guid ModelId);

    private sealed record Answer(IReadOnlyList<JsonElement> Items, int Total)
    {
        public IReadOnlyList<Guid> CaveIds => [.. Items.Select(i => i.GetProperty("caveId").GetGuid())];

        public IReadOnlyList<Guid> ModelIds => [.. Items.Select(i => i.GetProperty("surveyModelId").GetGuid())];
    }

    private static async Task<Answer> ScansAsync(HttpClient client, Spot spot, int? maxCaves = null, string extra = "")
    {
        var count = maxCaves is null ? string.Empty : $"&maxCaves={maxCaves}";
        var response = await client.GetAsync($"/api/v1/map/cave-scans?bbox={spot.Box}{count}{extra}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new Answer([.. body.GetProperty("items").EnumerateArray()], body.GetProperty("total").GetInt32());
    }

    /// <summary>A cave with one scan, tiled and placed at the given position unless told otherwise.</summary>
    private async Task<ScanCave> CreateCaveWithScanAsync(
        Spot at,
        double heightM = 1000,
        string visibility = "authenticated",
        bool locationProtected = false,
        Guid? parentId = null,
        bool tiled = true,
        bool place = true)
    {
        var name = $"Scan Cave {Guid.NewGuid():N}"[..30];
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
            parentId,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var caveId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var modelId = await AddScanAsync(caveId, place ? at : null, heightM, tiled);
        return new ScanCave(caveId, name, modelId);
    }

    /// <summary>A scan sent for the cave and run through the canned worker; placed when a position is given.</summary>
    private async Task<Guid> AddScanAsync(Guid caveId, Spot? at, double heightM = 1000, bool tiled = true)
    {
        var uploadId = await SendAsync(owner, $"Scan {suffix}", caveId, ("scan.laz", SyntheticLas.Las(points: 3, compressed: true)));
        if (tiled)
        {
            await RunPipelineAsync(uploadId);
        }
        else
        {
            tileRefused = true;
            try
            {
                // The run fails at tiling, as a worker's failure fails it; the header's facts stand.
                await Should.ThrowAsync<InvalidOperationException>(RunPipelineAsync(uploadId));
            }
            finally
            {
                tileRefused = false;
            }
        }

        var upload = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{uploadId}");
        var modelId = upload.GetProperty("pointCloud").GetProperty("modelId").GetGuid();
        if (at is not null)
        {
            await PlaceAsync(modelId, at, heightM);
        }

        return modelId;
    }

    /// <summary>Sets the scan down at a position: what a placement writes onto the row.</summary>
    private async Task PlaceAsync(Guid modelId, Spot at, double heightM)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.SingleAsync(m => m.Id == modelId);
        model.Anchor = new Point(at.Longitude, at.Latitude) { SRID = 4326 };
        model.AnchorHeightM = heightM;
        await db.SaveChangesAsync();
    }

    private async Task UnplaceAsync(Guid modelId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.SurveyModels.Where(m => m.Id == modelId).ExecuteUpdateAsync(s => s
            .SetProperty(m => m.Anchor, (Point?)null)
            .SetProperty(m => m.AnchorHeightM, (double?)null));
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

    /// <summary>Grants the reader Read + ViewExactLocation on one feature, whatever its kind.</summary>
    private async Task GrantExactViewAsync(Guid featureId)
    {
        var grant = await owner.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = readerId,
                    effect = "allow",
                    actions = "read, viewExactLocation",
                    scopeKind = "object",
                },
            },
        });
        grant.StatusCode.ShouldBe(HttpStatusCode.OK, await grant.Content.ReadAsStringAsync());
    }

    /// <summary>A location-protected karst area: a protection root that is not a cave.</summary>
    private async Task<Guid> CreateProtectedAreaAsync()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Protected Karst {Guid.NewGuid():N}"[..30],
            featureTypeId = karstAreaTypeId,
            geometry = (object?)null,
            description = (string?)null,
            locationProtected = true,
            visibility = "authenticated",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public async ValueTask DisposeAsync()
    {
        await worker.DisposeAsync();
        Dispose();
    }

    public void Dispose()
    {
        factory.Dispose();
        capped.Dispose();
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
}
