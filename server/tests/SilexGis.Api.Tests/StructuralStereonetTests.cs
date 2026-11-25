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
using SilexGis.Domain.Features;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Structural readings on a stereonet — around a cave and under an area — and the passage trends
/// set against their strikes by Watson's U². Every reading is gated one at a time: one the caller
/// may not place exactly is in no pole, no count and no statistic; a cave the caller may not place
/// is refused whole as "no such cave"; an area the caller may not read is refused as "no such
/// feature".
///
/// <para>
/// The fixtures sit in a box of their own (the Southern Alps of New Zealand) that no other class
/// seeds, and each test uses a latitude of its own inside it, so the reach around one test's cave
/// holds only what that test made.
/// </para>
/// </summary>
public sealed class StructuralStereonetTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const double BaseLon = 171.50;

    /// <summary>The passage trend every fixture cave runs at.</summary>
    private const double PassageBearing = 65d;

    /// <summary>A dip direction whose right-hand-rule strike (155) runs across the passage.</summary>
    private const double AcrossDipDirection = 245d;

    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private HttpClient anonymous = null!;
    private long readingTypeId;
    private long caveTypeId;
    private long karstAreaTypeId;

    public StructuralStereonetTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"sst-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"sst-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            readingTypeId = await db.FeatureTypes
                .Where(t => t.Code == FeatureTypeSeeds.StructuralMeasurement).Select(t => t.Id).SingleAsync();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).FirstAsync();
            karstAreaTypeId = await db.FeatureTypes
                .Where(t => t.Code == FeatureTypeSeeds.KarstArea).Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"sst-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"sst-read-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    [Fact]
    public async Task Readings_around_a_cave_plot_as_poles_and_their_strikes_are_tested_against_the_passage()
    {
        const double lat = -42.10;
        var cave = await CaveWithPassageAsync(lat);
        for (var i = 0; i < 6; i++)
        {
            await ReadingAsync(lat + (i * 0.0002), "joint", AcrossDipDirection - 2d + i, 60d);
        }

        await ReadingAsync(lat - 0.0002, "bedding", 10d, 15d);

        var net = await ReadJsonAsync(owner, $"/api/v1/caves/{cave}/stereonet?MeasurementType=joint");
        net.GetProperty("readingCount").GetInt32().ShouldBe(6);
        net.GetProperty("radiusMetres").GetDouble().ShouldBe(1_000d);
        var pole = net.GetProperty("poles").EnumerateArray()
            .Single(p => Math.Abs(p.GetProperty("dipDirectionDegrees").GetDouble() - AcrossDipDirection) < 1e-9);
        pole.GetProperty("trendDegrees").GetDouble().ShouldBe(65d, 1e-9);
        pole.GetProperty("plungeDegrees").GetDouble().ShouldBe(30d, 1e-9);
        var expected = Stereonet.Project(new LineOrientation(65d, 30d));
        pole.GetProperty("x").GetDouble().ShouldBe(expected.X, 1e-9);
        pole.GetProperty("y").GetDouble().ShouldBe(expected.Y, 1e-9);
        pole.GetProperty("measurementType").GetString().ShouldBe("joint");
        var density = net.GetProperty("density");
        density.GetProperty("size").GetInt32().ShouldBe(Stereonet.DefaultGridSize);
        density.GetProperty("maxSigma").GetDouble().ShouldBeGreaterThan(3d);

        // Every kind when none is named.
        var all = await ReadJsonAsync(owner, $"/api/v1/caves/{cave}/stereonet");
        all.GetProperty("readingCount").GetInt32().ShouldBe(7);

        // The strikes run across the passage: the test tells the two apart.
        var comparison = await ReadJsonAsync(
            owner, $"/api/v1/caves/{cave}/structure-comparison?MeasurementType=joint");
        var strikes = comparison.GetProperty("measuredStrikes");
        strikes.GetProperty("measurementType").GetString().ShouldBe("joint");
        strikes.GetProperty("readingCount").GetInt32().ShouldBe(6);
        strikes.GetProperty("passageLegCount").GetInt32().ShouldBe(11);
        strikes.GetProperty("minimumSampleSize").GetInt32().ShouldBe(WatsonU2.MinimumSampleSize);
        var watson = strikes.GetProperty("watson");
        watson.GetProperty("firstCount").GetInt32().ShouldBe(11);
        watson.GetProperty("secondCount").GetInt32().ShouldBe(6);
        watson.GetProperty("pValue").GetDouble().ShouldBeLessThan(0.01);
        AxialGap(strikes.GetProperty("strikes").GetProperty("byCount").GetProperty("meanAxisDegrees").GetDouble(), 155d)
            .ShouldBeLessThan(1d);
    }

    [Fact]
    public async Task A_reading_the_caller_may_not_place_is_in_no_pole_count_or_statistic()
    {
        const double lat = -42.16;
        var cave = await CaveWithPassageAsync(lat);
        for (var i = 0; i < 5; i++)
        {
            await ReadingAsync(lat + (i * 0.0002), "joint", AcrossDipDirection, 60d);
        }

        var hidden = await ReadingAsync(lat + 0.0012, "joint", 20d, 35d, locationProtected: true);

        // The owner may place the protected reading: it is there for them.
        var ownerNet = await ReadJsonAsync(owner, $"/api/v1/caves/{cave}/stereonet");
        ownerNet.GetProperty("readingCount").GetInt32().ShouldBe(6);
        ownerNet.GetProperty("poles").EnumerateArray()
            .Any(p => p.GetProperty("featureId").GetGuid() == hidden).ShouldBeTrue();
        (await ReadJsonAsync(owner, $"/api/v1/caves/{cave}/structure-comparison"))
            .GetProperty("measuredStrikes").GetProperty("readingCount").GetInt32().ShouldBe(6);

        // The viewer may read it but not place it: it is in nothing they are shown, and what they
        // are shown is what they would be shown were it never recorded.
        (await viewer.GetAsync($"/api/v1/features/{hidden}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var viewerNet = await ReadRawAsync(viewer, $"/api/v1/caves/{cave}/stereonet");
        var viewerComparison = (await ReadJsonAsync(viewer, $"/api/v1/caves/{cave}/structure-comparison"))
            .GetProperty("measuredStrikes").GetRawText();
        JsonDocument.Parse(viewerNet).RootElement.GetProperty("readingCount").GetInt32().ShouldBe(5);
        viewerNet.ShouldNotContain(hidden.ToString());

        (await owner.DeleteAsync($"/api/v1/features/{hidden}")).IsSuccessStatusCode.ShouldBeTrue();
        (await ReadRawAsync(owner, $"/api/v1/caves/{cave}/stereonet")).ShouldBe(viewerNet);
        (await ReadJsonAsync(owner, $"/api/v1/caves/{cave}/structure-comparison"))
            .GetProperty("measuredStrikes").GetRawText().ShouldBe(viewerComparison);
    }

    [Fact]
    public async Task A_cave_the_caller_may_not_place_is_refused_whole()
    {
        const double lat = -42.19;
        var cave = await CaveWithPassageAsync(lat, locationProtected: true);
        await ReadingAsync(lat, "bedding", 100d, 20d);

        // The owner may place the cave and is answered. (The reach is gathered only from the cave
        // and its unprotected parts, the rule the trace comparison keeps, so a protected cave whose
        // only geometry is its inherited-protected centreline gathers from nothing; what matters
        // here is that the answer is given, not what it holds.)
        (await owner.GetAsync($"/api/v1/caves/{cave}/stereonet")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await viewer.GetAsync($"/api/v1/caves/{cave}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var refused = await viewer.GetAsync($"/api/v1/caves/{cave}/stereonet");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(refused)).ShouldBe("cave.not_found");
    }

    [Fact]
    public async Task An_area_plots_the_readings_under_it_and_is_refused_to_a_caller_who_may_not_read_it()
    {
        const double lat = -42.31;
        var open = await AreaAsync(lat, "authenticated");
        var closed = await AreaAsync(lat + 0.02, "private");
        await ReadingAsync(lat + 0.001, "bedding", 300d, 25d, parentId: open);
        await ReadingAsync(lat + 0.002, "cleavage", 120d, 80d, parentId: open);
        await ReadingAsync(lat + 0.021, "bedding", 300d, 25d, parentId: closed);

        var net = await ReadJsonAsync(viewer, $"/api/v1/features/{open}/stereonet");
        net.GetProperty("readingCount").GetInt32().ShouldBe(2);
        net.GetProperty("radiusMetres").ValueKind.ShouldBe(JsonValueKind.Null);
        (await ReadJsonAsync(viewer, $"/api/v1/features/{open}/stereonet?MeasurementType=cleavage"))
            .GetProperty("readingCount").GetInt32().ShouldBe(1);

        (await ReadJsonAsync(owner, $"/api/v1/features/{closed}/stereonet"))
            .GetProperty("readingCount").GetInt32().ShouldBe(1);
        var refused = await viewer.GetAsync($"/api/v1/features/{closed}/stereonet");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(refused)).ShouldBe("feature.not_found");
    }

    /// <summary>
    /// The area reach gates each reading on its own, as the cave reach does: a protected reading
    /// under a readable, unprotected area is in neither the area's stereonet nor the strikes a cave
    /// is compared against through that area, for a reader who may not place it — and is in both
    /// for the owner, who may.
    /// </summary>
    [Fact]
    public async Task A_reading_under_an_area_the_caller_may_not_place_is_in_no_pole_count_or_statistic()
    {
        const double lat = -42.34;
        var area = await AreaAsync(lat, "authenticated");
        var cave = await CaveWithPassageAsync(lat + 0.003);
        for (var i = 0; i < 5; i++)
        {
            await ReadingAsync(lat + 0.001 + (i * 0.0002), "joint", AcrossDipDirection, 60d, parentId: area);
        }

        var hidden = await ReadingAsync(
            lat + 0.0025, "joint", 20d, 35d, locationProtected: true, parentId: area);

        var netUrl = $"/api/v1/features/{area}/stereonet";
        var comparisonUrl = $"/api/v1/caves/{cave}/structure-comparison?AreaId={area}";

        var ownerNet = await ReadJsonAsync(owner, netUrl);
        ownerNet.GetProperty("readingCount").GetInt32().ShouldBe(6);
        ownerNet.GetProperty("poles").EnumerateArray()
            .Any(p => p.GetProperty("featureId").GetGuid() == hidden).ShouldBeTrue();
        (await ReadJsonAsync(owner, comparisonUrl))
            .GetProperty("measuredStrikes").GetProperty("readingCount").GetInt32().ShouldBe(6);

        // Readable, not placeable: missing from every figure, and the answers are exactly those
        // the owner gets once the reading is gone.
        (await viewer.GetAsync($"/api/v1/features/{hidden}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var viewerNet = await ReadRawAsync(viewer, netUrl);
        JsonDocument.Parse(viewerNet).RootElement.GetProperty("readingCount").GetInt32().ShouldBe(5);
        viewerNet.ShouldNotContain(hidden.ToString());
        var viewerStrikes = (await ReadJsonAsync(viewer, comparisonUrl)).GetProperty("measuredStrikes");
        viewerStrikes.GetProperty("readingCount").GetInt32().ShouldBe(5);
        var viewerStrikesRaw = viewerStrikes.GetRawText();

        (await owner.DeleteAsync($"/api/v1/features/{hidden}")).IsSuccessStatusCode.ShouldBeTrue();
        (await ReadRawAsync(owner, netUrl)).ShouldBe(viewerNet);
        (await ReadJsonAsync(owner, comparisonUrl))
            .GetProperty("measuredStrikes").GetRawText().ShouldBe(viewerStrikesRaw);
    }

    [Fact]
    public async Task Too_few_readings_give_counts_and_no_test()
    {
        const double lat = -42.25;
        var cave = await CaveWithPassageAsync(lat);
        await ReadingAsync(lat, "joint", AcrossDipDirection, 60d);

        // A flat plane has no strike and is left out of the strikes, though it is a pole.
        await ReadingAsync(lat + 0.0002, "bedding", 0d, 0d);

        (await ReadJsonAsync(owner, $"/api/v1/caves/{cave}/stereonet"))
            .GetProperty("readingCount").GetInt32().ShouldBe(2);
        var strikes = (await ReadJsonAsync(owner, $"/api/v1/caves/{cave}/structure-comparison"))
            .GetProperty("measuredStrikes");
        strikes.GetProperty("readingCount").GetInt32().ShouldBe(1);
        strikes.GetProperty("watson").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task An_unknown_kind_or_reach_is_refused_and_an_anonymous_caller_is_turned_away()
    {
        var cave = Guid.NewGuid();
        (await owner.GetAsync($"/api/v1/caves/{cave}/stereonet?MeasurementType=schistosity"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.GetAsync($"/api/v1/caves/{cave}/stereonet?RadiusMetres=10"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.GetAsync($"/api/v1/features/{cave}/stereonet?MeasurementType=schistosity"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.GetAsync($"/api/v1/caves/{cave}/structure-comparison?MeasurementType=schistosity"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await anonymous.GetAsync($"/api/v1/caves/{cave}/stereonet")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/api/v1/features/{cave}/stereonet")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        owner?.Dispose();
        viewer?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();
    }

    private static double AxialGap(double a, double b)
    {
        var gap = Math.Abs(OrientationStatistics.Fold(a) - OrientationStatistics.Fold(b));
        return gap > 90d ? 180d - gap : gap;
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement.GetProperty("code").GetString() ?? string.Empty;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpClient client, string url) =>
        JsonDocument.Parse(await ReadRawAsync(client, url)).RootElement.Clone();

    private static async Task<string> ReadRawAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return body;
    }

    /// <summary>A cave with a drawn centreline of eleven 40 m legs dead straight at <see cref="PassageBearing"/>.</summary>
    private async Task<Guid> CaveWithPassageAsync(double latitude, bool locationProtected = false)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Sst Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var cave = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var ordinates = new List<string>();
        var (lon, lat) = (BaseLon, latitude);
        for (var i = 0; i < 12; i++)
        {
            ordinates.Add($"[{F(lon)},{F(lat)},{F(800d + i)}]");
            (lon, lat) = Step(lon, lat, PassageBearing, 40d);
        }

        var body = "{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\","
            + "\"properties\":{},\"geometry\":{\"type\":\"LineString\",\"coordinates\":["
            + string.Join(",", ordinates) + "]}}]}";
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        content.Headers.ContentType = new("application/geo+json");
        using var form = new MultipartFormDataContent { { content, "file", "Centerline.geojson" } };
        var upload = await owner.PostAsync($"/api/v1/caves/{cave}/centerlines", form);
        upload.StatusCode.ShouldBe(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());

        return cave;
    }

    private async Task<Guid> ReadingAsync(
        double latitude,
        string type,
        double dipDirection,
        double dip,
        bool locationProtected = false,
        Guid? parentId = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Sst {Guid.NewGuid():N}"[..20],
            featureTypeId = readingTypeId,
            geometry = new { type = "Point", coordinates = new[] { BaseLon + 0.0005, latitude } },
            properties = new Dictionary<string, object>
            {
                [StructuralMeasurement.TypeKey] = type,
                [StructuralMeasurement.DipDirectionKey] = dipDirection,
                [StructuralMeasurement.DipKey] = dip,
            },
            parents = parentId is null ? null : new[] { new { parentId = parentId.Value, isPrimary = true } },
            locationProtected,
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> AreaAsync(double south, string visibility)
    {
        const double west = BaseLon - 0.05;
        const double east = BaseLon + 0.05;
        var north = south + 0.01;
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Sst Area {Guid.NewGuid():N}"[..30],
            featureTypeId = karstAreaTypeId,
            geometry = new
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
            },
            locationProtected = false,
            visibility,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static (double Lon, double Lat) Step(double lon, double lat, double bearingDegrees, double metres)
    {
        var radians = bearingDegrees * Math.PI / 180d;
        var north = Math.Cos(radians) * metres;
        var east = Math.Sin(radians) * metres;
        return (lon + (east / (111_320d * Math.Cos(lat * Math.PI / 180d))), lat + (north / 110_540d));
    }

    private static string F(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);
}
