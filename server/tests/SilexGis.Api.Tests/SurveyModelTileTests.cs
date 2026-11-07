// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Features.SurveyUploads;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Scans;

namespace SilexGis.Api.Tests;

/// <summary>
/// A scan's tiles delivered one archive entry at a time by the signed address its upload hands
/// out: the root tileset and each tile with their length, checksum and a session-long cache; the
/// address given to everybody who may read the upload, exact position or not; and every way of
/// asking wrongly — no token, a tampered or lapsed one, one minted for another scan's archive, a
/// name that walks out of the archive or is not a tile's, an entry that is not there, a model
/// that is not a tiled scan — answered by the one not-found.
/// </summary>
/// <remarks>
/// The cloud is a synthetic header with no points and the tiles are bytes the test wrote; the
/// worker is a canned one on the far side of the hand-off directory.
/// </remarks>
public sealed class SurveyModelTileTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
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

    /// <summary>When set, the canned worker refuses to tile, leaving a scan whose header was read and which has no archive.</summary>
    private bool tileRefused;

    public SurveyModelTileTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-smt-{Guid.NewGuid():N}");
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
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"smt-own-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"smt-read-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"smt-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"smt-read-{suffix}@t.local");
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
    public async Task The_address_on_the_scan_streams_the_root_tileset_and_each_tile_as_cacheable_bytes()
    {
        var scan = await TiledScanAsync(owner);

        // The root, fetched by a client carrying nothing: the token in the address is the whole
        // credential, as it is for the tile reader that will follow it.
        var root = await nobody.GetAsync(scan.TilesetUrl);
        root.StatusCode.ShouldBe(HttpStatusCode.OK, await root.Content.ReadAsStringAsync());
        root.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        root.Content.Headers.ContentLength.ShouldBe(Tileset.LongLength);
        (await root.Content.ReadAsByteArrayAsync()).ShouldBe(Tileset);
        ShouldBeKeptForAYear(root);
        root.Headers.ETag.ShouldNotBeNull();
        root.Headers.ETag!.IsWeak.ShouldBeFalse();

        // A tile, by the address a reader derives from the root's: the same token, another entry.
        var tile = await nobody.GetAsync(scan.EntryUrl("r.pnts"));
        tile.StatusCode.ShouldBe(HttpStatusCode.OK, await tile.Content.ReadAsStringAsync());
        tile.Content.Headers.ContentType!.MediaType.ShouldBe("application/octet-stream");
        tile.Content.Headers.ContentLength.ShouldBe(Pnts.LongLength);
        (await tile.Content.ReadAsByteArrayAsync()).ShouldBe(Pnts);
        tile.Headers.ETag.ShouldNotBe(root.Headers.ETag, "two entries are two sets of bytes");

        // A tile in a folder, as a tiler that splits a large cloud writes them.
        var nested = await nobody.GetAsync(scan.EntryUrl("tiles/r0.pnts"));
        nested.StatusCode.ShouldBe(HttpStatusCode.OK, await nested.Content.ReadAsStringAsync());
        (await nested.Content.ReadAsByteArrayAsync()).ShouldBe(Nested);

        // A browser holding the bytes asks whether they changed; they never do.
        using var again = new HttpRequestMessage(HttpMethod.Get, scan.TilesetUrl);
        again.Headers.IfNoneMatch.Add(root.Headers.ETag);
        var unchanged = await nobody.SendAsync(again);
        unchanged.StatusCode.ShouldBe(HttpStatusCode.NotModified);
        unchanged.Headers.ETag.ShouldBe(root.Headers.ETag);
        ShouldBeKeptForAYear(unchanged);
    }

    [Fact]
    public async Task A_reader_who_may_see_the_upload_but_not_the_cave_s_position_is_handed_the_address_and_the_tiles_open()
    {
        // A protected cave the reader may read, write and place exactly: they send the scan. A
        // cave's survey files are withheld whole from everybody without the exact-location right,
        // except that the one who sent an upload keeps sight of it — so the reader who may see
        // the upload but not the position is its uploader, after that right is taken from them.
        var caveId = await CreateCaveAsync(locationProtected: true);
        await GrantAsync(caveId, "read, write, viewExactLocation");
        var scan = await TiledScanAsync(reader, caveId);

        await GrantAsync(caveId, "read");
        var upload = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{scan.UploadId}");
        var cloud = upload.GetProperty("pointCloud");
        // The position is withheld from this answer; the tiles are in the cloud's own frame, so
        // their address is not.
        cloud.GetProperty("originShift").ValueKind.ShouldBe(JsonValueKind.Null);
        var address = cloud.GetProperty("tilesetUrl").GetString();
        address.ShouldNotBeNullOrEmpty();
        address.ShouldStartWith($"/api/v1/survey-models/{scan.ModelId}/tiles/tileset.json?token=");

        var root = await nobody.GetAsync(address);
        root.StatusCode.ShouldBe(HttpStatusCode.OK, await root.Content.ReadAsStringAsync());
        (await root.Content.ReadAsByteArrayAsync()).ShouldBe(Tileset);

        // With their reading of the cave taken away, there is no answer to carry an address.
        await GrantAsync(caveId, actions: null);
        (await reader.GetAsync($"/api/v1/survey-uploads/{scan.UploadId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_token_that_does_not_verify_or_was_minted_for_another_archive_is_not_found()
    {
        var scan = await TiledScanAsync(owner);
        var other = await TiledScanAsync(owner);

        // The token of the other scan's address, placed on this one's: minted over another file.
        var borrowed = $"/api/v1/survey-models/{scan.ModelId}/tiles/tileset.json?token={other.Token}";
        await ShouldBeNotFoundAsync(borrowed);

        // No token, an unreadable one, a readable one that has lapsed.
        await ShouldBeNotFoundAsync($"/api/v1/survey-models/{scan.ModelId}/tiles/tileset.json");
        await ShouldBeNotFoundAsync($"/api/v1/survey-models/{scan.ModelId}/tiles/tileset.json?token=not-a-token");
        await ShouldBeNotFoundAsync($"/api/v1/survey-models/{scan.ModelId}/tiles/tileset.json?token={Uri.EscapeDataString(scan.Token + "x")}");
        await ShouldBeNotFoundAsync($"/api/v1/survey-models/{scan.ModelId}/tiles/tileset.json?token={Uri.EscapeDataString(await LapsedTokenAsync(scan.ModelId))}");

        // The same path with its own token opens, so the refusals above were the token's.
        (await nobody.GetAsync(scan.TilesetUrl)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_name_that_is_not_a_tile_s_or_walks_out_of_the_archive_is_not_found_whatever_the_archive_holds()
    {
        var scan = await TiledScanAsync(owner);

        // In the archive, but not a tile: the tiler's stray text file is not served.
        await ShouldBeNotFoundAsync(scan.EntryUrl("notes.txt"));
        // Not in the archive at all.
        await ShouldBeNotFoundAsync(scan.EntryUrl("r9.pnts"));
        await ShouldBeNotFoundAsync(scan.EntryUrl("tiles/missing.json"));
        // Built to walk out of the archive, or spelled so a looser match might find the root.
        // The server folds ".." segments before routing, as a browser does before sending: one
        // that climbs above the tiles leaves the route altogether, and one that stays within it
        // is the root by another spelling — the same entry, under the same token — so what
        // reaches the archive is never a "..". The encodings below survive that folding and are
        // refused by the name rule.
        await ShouldBeNotFoundAsync(scan.EntryUrl("%2e%2e/tileset.json"));
        var folded = await nobody.GetAsync(scan.EntryUrl("tiles/%2e%2e/tileset.json"));
        folded.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await folded.Content.ReadAsByteArrayAsync()).ShouldBe(Tileset);
        await ShouldBeNotFoundAsync(scan.EntryUrl("%2e%2e%5ctileset.json"));
        await ShouldBeNotFoundAsync(scan.EntryUrl("tiles%2f%2e%2e%2ftileset.json"));
        await ShouldBeNotFoundAsync(scan.EntryUrl("%2ftileset.json"));
        await ShouldBeNotFoundAsync(scan.EntryUrl("TILESET.JSON"));

        // The name beside them opens, so the refusals above were the name's.
        (await nobody.GetAsync(scan.EntryUrl("r.pnts"))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_model_that_is_not_a_tiled_scan_is_not_found_even_with_a_token_that_verifies()
    {
        var scan = await TiledScanAsync(owner);

        // A model that does not exist, with this scan's own token.
        await ShouldBeNotFoundAsync($"/api/v1/survey-models/{Guid.NewGuid()}/tiles/tileset.json?token={scan.Token}");

        // The scan before it has an archive: its token cannot exist, and the route says only not found.
        var untiled = await UntiledScanAsync(owner);
        await ShouldBeNotFoundAsync($"/api/v1/survey-models/{untiled.ModelId}/tiles/tileset.json?token={scan.Token}");
        untiled.TilesetUrl.ShouldBeNull("an upload not yet tiled hands out no address");

        // A model that is not a point cloud, whose converted file is this very archive: the token
        // verifies against it — the same token opens the archive through the file route — and the
        // tile route still answers not found, so the refusal is the model's kind and nothing else.
        Guid linePlotId;
        Guid archiveId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var tiled = await db.SurveyModels.AsNoTracking().SingleAsync(m => m.Id == scan.ModelId);
            archiveId = tiled.ConvertedFileId!.Value;
            var linePlot = new SurveyModel
            {
                CaveFeatureId = tiled.CaveFeatureId,
                Name = "a line plot over the scan's archive",
                FileId = tiled.FileId,
                ConvertedFileId = archiveId,
                Format = SurveyModelFormat.Survex3d,
                Status = SurveyModelStatus.Ready,
            };
            db.SurveyModels.Add(linePlot);
            await db.SaveChangesAsync();
            linePlotId = linePlot.Id;
        }

        (await nobody.GetAsync($"/api/v1/files/{archiveId}/content?token={scan.Token}")).StatusCode
            .ShouldBe(HttpStatusCode.OK, "the token is good for the archive these two models share");
        await ShouldBeNotFoundAsync($"/api/v1/survey-models/{linePlotId}/tiles/tileset.json?token={scan.Token}");
    }

    // ---- what the canned worker answers with ----

    private static readonly byte[] Tileset = Encoding.UTF8.GetBytes(
        """{"asset":{"version":"1.0"},"geometricError":4.0,"root":{"boundingVolume":{"box":[0,0,0,2,0,0,0,2,0,0,0,2]},"geometricError":1.0,"refine":"ADD","content":{"uri":"r.pnts"},"children":[{"boundingVolume":{"box":[0,0,0,1,0,0,0,1,0,0,0,1]},"geometricError":0.0,"content":{"uri":"tiles/r0.pnts"}}]}}""");

    private static readonly byte[] Pnts = [.. Encoding.ASCII.GetBytes("pnts"), 1, 0, 0, 0, 200, 0, 0, 0, .. Enumerable.Range(0, 188).Select(i => (byte)(i * 7))];
    private static readonly byte[] Nested = [.. Encoding.ASCII.GetBytes("pnts"), 1, 0, 0, 0, 40, 0, 0, 0, .. Enumerable.Range(0, 28).Select(i => (byte)(255 - i))];
    private static readonly byte[] Notes = Encoding.UTF8.GetBytes("a file the tiler left beside its tiles");

    private static readonly byte[] TilesZip = MakeZip();

    private static byte[] MakeZip()
    {
        using var stream = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in new[] { ("tileset.json", Tileset), ("r.pnts", Pnts), ("tiles/r0.pnts", Nested), ("notes.txt", Notes) })
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

            case Py3dtilesTileStep.StepId when tileRefused:
                CannedScanWorker.Finish(directory, "failed", [], reason: "the test's tiler is switched off");
                break;

            case Py3dtilesTileStep.StepId:
            {
                var output = CannedScanWorker.Output(directory, "pointCloudTiles", "tiles.zip", TilesZip);
                CannedScanWorker.Finish(
                    directory, "ok", [output],
                    new { pointCount = 30, originShift = new[] { 0.5, 0.5, 0.5 }, tileCount = 2, tileBytes = Pnts.Length + Nested.Length, archiveBytes = TilesZip.LongLength, intensity = true, rgb = false },
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

    private sealed record Scan(Guid UploadId, Guid ModelId, string? TilesetUrl)
    {
        public string Token => TilesetUrl![(TilesetUrl!.IndexOf("token=", StringComparison.Ordinal) + "token=".Length)..];

        /// <summary>The address a reader derives for an entry: the root's, with the entry's name in place of the root's.</summary>
        public string EntryUrl(string name) => TilesetUrl!.Replace("/tiles/tileset.json?", $"/tiles/{name}?", StringComparison.Ordinal);
    }

    /// <summary>A point cloud sent, its header read and its tiles stored by the canned worker.</summary>
    private async Task<Scan> TiledScanAsync(HttpClient client, Guid? caveId = null)
    {
        caveId ??= await CreateCaveAsync();
        var uploadId = await SendAsync(client, $"Scan {suffix}", caveId.Value, ("scan.laz", SyntheticLas.Las(points: 3, compressed: true)));
        await RunPipelineAsync(uploadId);
        return await ReadAsync(client, uploadId);
    }

    /// <summary>A point cloud whose header was read but whose tiling the worker refused: a scan with no archive.</summary>
    private async Task<Scan> UntiledScanAsync(HttpClient client)
    {
        tileRefused = true;
        try
        {
            var caveId = await CreateCaveAsync();
            var uploadId = await SendAsync(client, $"Scan {suffix}", caveId, ("scan.laz", SyntheticLas.Las(points: 3, compressed: true)));
            // The run fails at tiling, as a worker's failure fails it; the header's facts stand.
            await Should.ThrowAsync<InvalidOperationException>(RunPipelineAsync(uploadId));
            var scan = await ReadAsync(client, uploadId);
            scan.ModelId.ShouldNotBe(Guid.Empty);
            return scan;
        }
        finally
        {
            tileRefused = false;
        }
    }

    private static async Task<Scan> ReadAsync(HttpClient client, Guid uploadId)
    {
        var upload = await client.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{uploadId}");
        var cloud = upload.GetProperty("pointCloud");
        return new Scan(
            uploadId,
            cloud.GetProperty("modelId").GetGuid(),
            cloud.TryGetProperty("tilesetUrl", out var url) && url.ValueKind == JsonValueKind.String ? url.GetString() : null);
    }

    /// <summary>
    /// The caching the route promises, read as the directives rather than as text: the client
    /// parses the header and prints its directives back in its own order.
    /// </summary>
    private static void ShouldBeKeptForAYear(HttpResponseMessage response)
    {
        var cache = response.Headers.CacheControl.ShouldNotBeNull();
        cache.Private.ShouldBeTrue("the address carries a token minted for one caller");
        cache.Public.ShouldBeFalse();
        cache.NoCache.ShouldBeFalse();
        cache.NoStore.ShouldBeFalse();
        cache.MaxAge.ShouldBe(TimeSpan.FromSeconds(31536000));
        cache.Extensions.ShouldContain(e => e.Name == "immutable" && e.Value == null);
    }

    private async Task ShouldBeNotFoundAsync(string address)
    {
        var response = await nobody.GetAsync(address);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, address);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotBeNullOrEmpty(address);
        using var problem = JsonDocument.Parse(body);
        if (problem.RootElement.TryGetProperty("code", out var code))
        {
            // The route answered: it says what every refusal of it says.
            code.GetString().ShouldBe("survey_model.not_found", address);
        }
        else
        {
            // The server folded a ".." segment away before routing, so no route matched and the
            // framework's own not-found answered — a problem with no code. Either way the caller
            // learns nothing about the archive.
            problem.RootElement.GetProperty("status").GetInt32().ShouldBe(404, $"{address}: {body}");
        }
    }

    /// <summary>
    /// A token minted over this scan's archive, for its whole bytes, that expired a minute ago —
    /// written the way the delivery token service writes its own, with the expiry in the past.
    /// </summary>
    private async Task<string> LapsedTokenAsync(Guid modelId)
    {
        Guid archiveId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            archiveId = (await db.SurveyModels.AsNoTracking().SingleAsync(m => m.Id == modelId)).ConvertedFileId!.Value;
        }

        var protector = factory.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("SilexGis.FileAccess")
            .ToTimeLimitedDataProtector();
        return protector.Protect($"{archiveId:N}.f.{readerId:N}", DateTimeOffset.UtcNow.AddMinutes(-1));
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
            name = $"Tile Cave {Guid.NewGuid():N}"[..28],
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
