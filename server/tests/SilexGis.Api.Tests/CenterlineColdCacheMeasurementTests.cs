// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Opt-in, reports only: how the centreline overlay behaves the first time it is asked for after
/// the database has forgotten the line work. A measurement, not a gate — without its variable it
/// does nothing and passes, so a green result from it says nothing about the overlay.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> Every other timing of the centreline overlay is taken warm: the test that
/// times it has just written the rows, so their pages are in shared buffers. A real installation
/// serves the first map of the morning from disk. A centreline is a multi-megabyte geometry kept
/// out of line (TOAST), so the cold read is where any surprise would be.
/// </para>
/// <para>
/// <b>What "cold" means here, honestly.</b> The PostgreSQL buffer cache is emptied for this class's
/// own database with <c>pg_buffercache_evict</c> (PostgreSQL 17), which touches no other test
/// class's pages and needs no container restart — restarting the one container every class in the
/// run shares would break all of them. The operating system's page cache is NOT emptied: that
/// needs root on the host and would slow every process on the machine. So the cold number is
/// "nothing in shared buffers, file pages possibly still in the kernel's cache", which is a lower
/// bound on a true cold start from disk. On the NVMe-backed machines this runs on the difference
/// is the device's read latency, not a change of shape.
/// </para>
/// <para>
/// <b>How to run it.</b> Set <c>SILEXGIS_MEASURE_CENTERLINE_COLD_CACHE=1</c> and run this class
/// alone with a detailed console logger; the numbers are written to the test output. Without the
/// variable the test does nothing and says so, so an ordinary run pays nothing for it. The only
/// assertion beyond "it answered" is a generous sanity budget.
/// </para>
/// </remarks>
public sealed class CenterlineColdCacheMeasurementTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string OptInVariable = "SILEXGIS_MEASURE_CENTERLINE_COLD_CACHE";

    /// <summary>Caves seeded, each with one uploaded survey.</summary>
    private const int Caves = 12;

    /// <summary>Survey stations per cave; each carries a fan of wall shots as real exports do.</summary>
    private const int StationsPerCave = 4000;

    private const int SplaysPerStation = 9;

    /// <summary>A cold first answer slower than this is broken, not slow.</summary>
    private const long SanityBudgetMs = 30_000;

    private const double Lon0 = 12.10;
    private const double Lat0 = 41.70;

    private readonly SilexGisApiFactory factory;
    private readonly ITestOutputHelper output;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private long caveTypeId;

    public CenterlineColdCacheMeasurementTests(PostgresFixture postgres, ITestOutputHelper output)
    {
        this.output = output;
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    private static bool Requested =>
        Environment.GetEnvironmentVariable(OptInVariable) == "1";

    public async ValueTask InitializeAsync()
    {
        if (!Requested)
        {
            return;
        }

        var email = $"cold-{Guid.NewGuid():N}@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, email);
    }

    [Fact]
    public async Task Opt_in_reports_only_first_centerline_overlay_after_the_buffer_cache_is_emptied()
    {
        if (!Requested)
        {
            output.WriteLine($"Not requested: set {OptInVariable}=1 to measure the cold centreline overlay.");
            return;
        }

        var seed = Stopwatch.StartNew();
        long uploadedBytes = 0;
        for (var i = 0; i < Caves; i++)
        {
            var caveId = await CreateCaveAsync();
            var survey = Survey(Lon0 + (0.03 * (i % 4)), Lat0 + (0.03 * (i / 4)), seed: i);
            uploadedBytes += survey.Length;
            using var form = Form($"cold-{i}.geojson", survey);
            var response = await owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", form);
            response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        }

        seed.Stop();
        var shots = Caves * StationsPerCave * (1 + SplaysPerStation);
        output.WriteLine(Invariant(
            $"seeded {Caves} centrelines, {shots} shots, {uploadedBytes / 1024 / 1024} MB of GeoJSON in {seed.Elapsed.TotalSeconds:F1} s"));
        output.WriteLine($"stored geometry: {await StoredGeometrySizeAsync()}");

        // The overview every map opens with (stored skeletons), and full detail over one cave.
        var overview = Invariant($"/api/v1/map/cave-centerlines?bbox={Lon0 - 0.05},{Lat0 - 0.05},{Lon0 + 0.15},{Lat0 + 0.12}&zoom=13");
        var detail = Invariant($"/api/v1/map/cave-centerlines?bbox={Lon0 - 0.005},{Lat0 - 0.005},{Lon0 + 0.02},{Lat0 + 0.02}&zoom=18");

        foreach (var (name, url) in new[] { ("overview z13 (skeletons)", overview), ("detail z18 (one cave)", detail) })
        {
            var cold = new List<long>();
            var warm = new List<long>();
            for (var round = 0; round < 3; round++)
            {
                var evicted = await EvictBufferCacheAsync();
                var (coldMs, coldBytes) = await TimeAsync(url);
                var (warmMs, _) = await TimeAsync(url);
                cold.Add(coldMs);
                warm.Add(warmMs);
                output.WriteLine(Invariant(
                    $"{name} round {round + 1}: evicted {evicted} buffers; cold {coldMs} ms, warm {warmMs} ms, {coldBytes / 1024} KB answer"));
                coldMs.ShouldBeLessThan(SanityBudgetMs, $"{name}: the cold answer took longer than the sanity budget");
            }

            output.WriteLine(Invariant(
                $"{name}: median cold {Median(cold)} ms, median warm {Median(warm)} ms"));
        }
    }

    private async Task<(long Ms, long Bytes)> TimeAsync(string url)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await owner.GetAsync(url);
        var body = await response.Content.ReadAsByteArrayAsync();
        stopwatch.Stop();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, url);
        return (stopwatch.ElapsedMilliseconds, body.Length);
    }

    /// <summary>
    /// Drops every shared buffer holding a page of this class's database. Pinned or dirty buffers
    /// are skipped by the function and reported as not evicted, so a checkpoint goes first.
    /// </summary>
    private async Task<int> EvictBufferCacheAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS pg_buffercache");
        // Named as text: Dapper takes a statement with no whitespace for a procedure name.
        await connection.ExecuteAsync("CHECKPOINT", commandType: System.Data.CommandType.Text);
        return await connection.QuerySingleAsync<int>("""
            SELECT count(*) FILTER (WHERE pg_buffercache_evict(bufferid))::int
            FROM pg_buffercache
            WHERE reldatabase = (SELECT oid FROM pg_database WHERE datname = current_database())
            """);
    }

    private async Task<string> StoredGeometrySizeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Database.GetDbConnection().QuerySingleAsync<string>($"""
            SELECT pg_size_pretty(sum(pg_column_size(geom))::bigint)
            FROM features WHERE kind = {(short)FeatureKind.Centerline} AND deleted_at IS NULL
            """);
    }

    private static long Median(List<long> values)
    {
        var sorted = values.Order().ToList();
        return sorted[sorted.Count / 2];
    }

    /// <summary>
    /// A synthetic survey shaped like a real export: a meandering traverse, one shot per leg, and a
    /// fan of short wall shots off every station, each written as its own line.
    /// </summary>
    private static byte[] Survey(double lon0, double lat0, int seed)
    {
        var random = new Random(seed);
        var text = new StringBuilder(StationsPerCave * (1 + SplaysPerStation) * 140);
        text.Append("{\"type\":\"FeatureCollection\",\"features\":[");
        var first = true;
        void Shot(double x1, double y1, double z1, double x2, double y2, double z2)
        {
            if (!first)
            {
                text.Append(',');
            }

            first = false;
            text.Append(Invariant(
                $"{{\"type\":\"Feature\",\"properties\":{{}},\"geometry\":{{\"type\":\"LineString\",\"coordinates\":[[{x1:F7},{y1:F7},{z1:F2}],[{x2:F7},{y2:F7},{z2:F2}]]}}}}"));
        }

        double x = lon0, y = lat0, z = 900, heading = 0;
        for (var s = 0; s < StationsPerCave; s++)
        {
            heading += (random.NextDouble() - 0.5) * 0.8;
            var nx = x + (Math.Cos(heading) * 0.00006);
            var ny = y + (Math.Sin(heading) * 0.00004);
            var nz = z - (random.NextDouble() * 1.5);
            Shot(x, y, z, nx, ny, nz);
            for (var k = 0; k < SplaysPerStation; k++)
            {
                var angle = random.NextDouble() * Math.PI * 2;
                var reach = 0.00001 + (random.NextDouble() * 0.00003);
                Shot(nx, ny, nz, nx + (Math.Cos(angle) * reach), ny + (Math.Sin(angle) * reach), nz + (random.NextDouble() * 4) - 2);
            }

            (x, y, z) = (nx, ny, nz);
        }

        text.Append("]}");
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private async Task<Guid> CreateCaveAsync()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Cold Cave {Guid.NewGuid():N}"[..28],
            caveTypeId,
            visibility = "authenticated",
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static MultipartFormDataContent Form(string fileName, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/octet-stream");
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);

    public ValueTask DisposeAsync()
    {
        owner?.Dispose();
        Dispose();
        return ValueTask.CompletedTask;
    }

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
