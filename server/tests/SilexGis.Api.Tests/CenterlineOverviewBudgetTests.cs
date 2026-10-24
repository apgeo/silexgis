// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The centreline overlay's overview answers stay below the sizes at which drawing them would need
/// more than the stored skeleton: a background parse, more levels of detail, or a footprint drawn
/// instead of the lines at low zoom.
/// </summary>
/// <remarks>
/// <para>
/// None of that is built, on purpose: measured on real surveys the overview answer is under a
/// thousand line pieces and well under a hundred kilobytes, parsed in about ten milliseconds, so
/// there is nothing for it to fix. It is to be built when the overview answer grows past the sizes
/// below. Waiting for "a cave that misbehaves" is a promise nobody checks; this test is the check.
/// When it fails, the overlay has outgrown what it does now, and that is the signal to build the
/// next step rather than to raise a number here.
/// </para>
/// <para>
/// The surveys are invented, generated here: a branching cave network with the fan of short wall
/// shots off every station that real exports carry — those shots are most of every real file, and
/// the overview drops them. One large system, as big as the largest surveys this was measured on,
/// and a karst area holding many ordinary ones, the way a club's map fills up over the years.
/// </para>
/// </remarks>
public sealed class CenterlineOverviewBudgetTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    /// <summary>
    /// Line pieces one cave's overview skeleton may hold. Past it, a single survey is large enough
    /// that turning it into map features takes noticeably longer than a frame on an ordinary
    /// computer, and longer still on a phone in the field — the point at which a background parse
    /// or a coarser level for low zooms starts to earn its cost. The largest real survey measured
    /// came to under nine hundred.
    /// </summary>
    private const int SkeletonPiecesTrigger = 5_000;

    /// <summary>
    /// Line pieces a karst-area view at zooms 12 to 14 may hold, all caves together. Past it, the
    /// many-caves case — the likeliest way the overlay grows — needs a footprint drawn per cave at
    /// those zooms instead of every cave's lines.
    /// </summary>
    private const int AreaViewPiecesTrigger = 20_000;

    /// <summary>
    /// Bytes the same area view may weigh. Past about a megabyte the transfer and the parse, not
    /// the drawing, are what a member waits for when the overlay is switched on.
    /// </summary>
    private const int AreaViewBytesTrigger = 1_000_000;

    /// <summary>The large system: survey legs, and wall shots fanned off every station.</summary>
    private const int LargeSystemLegs = 6_000;

    private const int LargeSystemSplays = 15;

    /// <summary>The karst area: how many ordinary surveyed caves, each of how many legs.</summary>
    private const int AreaCaves = 30;

    private const int AreaCaveLegs = 400;

    private const int AreaCaveSplays = 8;

    private const double Lon0 = 12.40;
    private const double Lat0 = 41.90;

    /// <summary>
    /// The karst area sits a degree east of the large system, so neither test's view holds the
    /// other's caves whichever runs first.
    /// </summary>
    private const double AreaLon0 = 13.40;

    private readonly SilexGisApiFactory factory;
    private readonly ITestOutputHelper output;
    private readonly string filesRoot;
    private HttpClient owner = null!;
    private long caveTypeId;

    public CenterlineOverviewBudgetTests(PostgresFixture postgres, ITestOutputHelper output)
    {
        this.output = output;
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async ValueTask InitializeAsync()
    {
        var email = $"budget-{Guid.NewGuid():N}@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, email);
    }

    [Fact]
    public async Task The_overview_of_the_largest_system_stays_under_the_skeleton_trigger()
    {
        var caveId = await UploadAsync(Survey(Lon0, Lat0, LargeSystemLegs, LargeSystemSplays, seed: 1));
        var raw = LargeSystemLegs * (1 + LargeSystemSplays);

        foreach (var zoom in new[] { 12, 13, 14, 15, 16, 17 })
        {
            var (answer, bytes) = await OverlayAsync(Lon0 - 0.3, Lat0 - 0.3, Lon0 + 0.3, Lat0 + 0.3, zoom);
            var cave = answer.Features.Single(f => f.CaveId == caveId);
            output.WriteLine(Invariant($"large system, zoom {zoom}: {cave.Paths} pieces of {raw} shot, {bytes / 1024} KB"));

            answer.Detail.ShouldBeFalse($"zoom {zoom} is an overview zoom and is answered with the skeleton");
            cave.Paths.ShouldBeLessThan(
                SkeletonPiecesTrigger,
                $"one cave's overview skeleton at zoom {zoom} passed the size at which phase C of the overlay is due");
        }
    }

    [Fact]
    public async Task A_karst_area_full_of_surveyed_caves_stays_under_the_area_view_triggers()
    {
        var uploaded = new HashSet<Guid>();
        for (var i = 0; i < AreaCaves; i++)
        {
            uploaded.Add(await UploadAsync(Survey(
                AreaLon0 + (0.012 * (i % 6)), Lat0 + (0.012 * (i / 6)), AreaCaveLegs, AreaCaveSplays, seed: 100 + i)));
        }

        foreach (var zoom in new[] { 12, 13, 14 })
        {
            var (answer, bytes) = await OverlayAsync(AreaLon0 - 0.02, Lat0 - 0.02, AreaLon0 + 0.1, Lat0 + 0.08, zoom);
            var pieces = answer.Features.Sum(f => f.Paths);
            output.WriteLine(Invariant($"area view, zoom {zoom}: {answer.Features.Count} caves, {pieces} pieces, {bytes / 1024} KB"));

            answer.Features.Select(f => f.CaveId).ToHashSet().SetEquals(uploaded)
                .ShouldBeTrue("the view holds every surveyed cave of the area and nothing else");
            pieces.ShouldBeLessThan(
                AreaViewPiecesTrigger,
                $"the area view at zoom {zoom} passed the piece count at which phase C of the overlay is due");
            bytes.ShouldBeLessThan(
                AreaViewBytesTrigger,
                $"the area view at zoom {zoom} passed the size at which phase C of the overlay is due");
        }
    }

    // ---------- helpers ----------

    private sealed record Served(Guid CaveId, int Paths);

    private sealed record Overlay(IReadOnlyList<Served> Features, bool Detail);

    private async Task<(Overlay Answer, long Bytes)> OverlayAsync(
        double west, double south, double east, double north, int zoom)
    {
        var response = await owner.GetAsync(Invariant(
            $"/api/v1/map/cave-centerlines?bbox={west},{south},{east},{north}&zoom={zoom}"));
        var body = await response.Content.ReadAsByteArrayAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, Encoding.UTF8.GetString(body));

        var root = JsonDocument.Parse(body).RootElement;
        var features = root.GetProperty("features").EnumerateArray()
            .Select(f => f.GetProperty("properties"))
            .Select(p => new Served(p.GetProperty("caveId").GetGuid(), p.GetProperty("paths").GetInt32()))
            .ToList();
        return (new Overlay(features, root.GetProperty("detail").GetBoolean()), body.Length);
    }

    private async Task<Guid> UploadAsync(byte[] survey)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Budget Cave {Guid.NewGuid():N}"[..28],
            caveTypeId,
            visibility = "authenticated",
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var caveId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var content = new ByteArrayContent(survey);
        content.Headers.ContentType = new("application/octet-stream");
        using var form = new MultipartFormDataContent { { content, "file", "invented.geojson" } };
        var uploaded = await owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", form);
        uploaded.StatusCode.ShouldBe(HttpStatusCode.Created, await uploaded.Content.ReadAsStringAsync());
        return caveId;
    }

    /// <summary>
    /// An invented survey shaped like a real export: a meandering main passage with side passages
    /// branching off it — some branching again — one shot per leg, and a fan of short wall shots
    /// off every station, each written as its own line.
    /// </summary>
    private static byte[] Survey(double lon0, double lat0, int legs, int splays, int seed)
    {
        var random = new Random(seed);
        var text = new StringBuilder(legs * (1 + splays) * 140);
        text.Append("{\"type\":\"FeatureCollection\",\"features\":[");
        var first = true;
        void Shot((double X, double Y, double Z) from, (double X, double Y, double Z) to)
        {
            if (!first)
            {
                text.Append(',');
            }

            first = false;
            text.Append(Invariant(
                $"{{\"type\":\"Feature\",\"properties\":{{}},\"geometry\":{{\"type\":\"LineString\",\"coordinates\":[[{from.X:F7},{from.Y:F7},{from.Z:F2}],[{to.X:F7},{to.Y:F7},{to.Z:F2}]]}}}}"));
        }

        // Stations a passage can still branch from, with the heading the passage had there.
        var stations = new List<((double X, double Y, double Z) At, double Heading)> { ((lon0, lat0, 900), 0) };
        var drawn = 0;
        while (drawn < legs)
        {
            // Mostly carry on from the newest station; now and then start a side passage from an
            // older one, which is what gives a real survey its junctions.
            var (at, heading) = random.NextDouble() < 0.12
                ? stations[random.Next(stations.Count)]
                : stations[^1];
            if (random.NextDouble() < 0.12)
            {
                heading += (random.NextDouble() < 0.5 ? -1 : 1) * (0.8 + random.NextDouble());
            }

            var run = 3 + random.Next(25);
            for (var i = 0; i < run && drawn < legs; i++, drawn++)
            {
                heading += (random.NextDouble() - 0.5) * 0.6;
                var next = (
                    at.X + (Math.Cos(heading) * 0.00006),
                    at.Y + (Math.Sin(heading) * 0.00004),
                    at.Z - (random.NextDouble() * 1.5) + 0.5);
                Shot(at, next);
                for (var k = 0; k < splays; k++)
                {
                    var angle = random.NextDouble() * Math.PI * 2;
                    var reach = 0.00001 + (random.NextDouble() * 0.00003);
                    Shot(next, (next.Item1 + (Math.Cos(angle) * reach), next.Item2 + (Math.Sin(angle) * reach), next.Item3 + (random.NextDouble() * 4) - 2));
                }

                at = next;
                stations.Add((at, heading));
            }
        }

        text.Append("]}");
        return Encoding.UTF8.GetBytes(text.ToString());
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
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
