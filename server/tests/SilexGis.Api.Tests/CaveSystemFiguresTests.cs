// SPDX-License-Identifier: AGPL-3.0-or-later
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
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Depth and height taken across a cave's entrances and survey, and the springs they are set against.
///
/// <para>
/// Every figure is an altitude, so the protection tests are the point: a cave the caller may read but
/// not place answers as absent, and a spring the caller may not place draws no resurgence level
/// and leaves no trace of having been left out.
/// </para>
/// </summary>
public sealed class CaveSystemFiguresTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private HttpClient anonymous = null!;
    private long caveTypeId;
    private long springCaveTypeId;
    private long entranceTypeId;
    private long karstAreaTypeId;

    public CaveSystemFiguresTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"sf-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"sf-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).FirstAsync();
            springCaveTypeId = await db.CaveTypes
                .Where(t => t.Code == CaveTypeSeeds.SpringCave).Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
            karstAreaTypeId = await db.FeatureTypes
                .Where(t => t.Code == "karst_area").Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"sf-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"sf-read-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    [Fact]
    public async Task Depth_height_and_potential_depth_come_from_the_entrances_the_survey_and_the_area_springs()
    {
        var area = await CreateAreaAsync();
        await CaveWithEntrancesAsync(area, [800m], springCaveTypeId);
        var cave = await CaveWithEntrancesAsync(area, [1200m, 1100m]);
        await UploadDescentAsync(cave, 1150, 900);

        var body = await ReadAsync(viewer, cave);
        var figures = body.GetProperty("figures");

        body.GetProperty("hasAltitudes").GetBoolean().ShouldBeTrue();
        figures.GetProperty("entranceCount").GetInt32().ShouldBe(2);
        figures.GetProperty("highestEntranceM").GetDouble().ShouldBe(1200, 1e-6);
        figures.GetProperty("lowestEntranceM").GetDouble().ShouldBe(1100, 1e-6);
        figures.GetProperty("entranceSpreadM").GetDouble().ShouldBe(100, 1e-6);
        figures.GetProperty("totalDepthM").GetDouble().ShouldBe(300, 1e-6);
        figures.GetProperty("heightAboveLowestEntranceM").GetDouble().ShouldBe(50, 1e-6);
        figures.GetProperty("resurgenceLevelM").GetDouble().ShouldBe(800, 1e-6);
        figures.GetProperty("potentialDepthM").GetDouble().ShouldBe(400, 1e-6);
        body.GetProperty("potentialDepth").GetProperty("agreement").GetString().ShouldBe("notDeclared");
    }

    [Fact]
    public async Task A_cave_without_survey_altitudes_has_entrance_figures_and_no_invented_depth()
    {
        var cave = await CaveWithEntrancesAsync(null, [1200m]);

        var figures = (await ReadAsync(owner, cave)).GetProperty("figures");
        figures.GetProperty("highestEntranceM").GetDouble().ShouldBe(1200, 1e-6);
        figures.GetProperty("totalDepthM").ValueKind.ShouldBe(JsonValueKind.Null);
        figures.GetProperty("heightAboveLowestEntranceM").ValueKind.ShouldBe(JsonValueKind.Null);
        figures.GetProperty("potentialDepthM").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>
    /// A spring the reader may read and not place contributes no level. The potential depth is the
    /// same as if it had never been written, so it cannot reveal that it exists.
    /// </summary>
    [Fact]
    public async Task A_protected_spring_the_reader_may_not_place_moves_no_figure()
    {
        var area = await CreateAreaAsync();
        await CaveWithEntrancesAsync(area, [800m], springCaveTypeId);
        var cave = await CaveWithEntrancesAsync(area, [1200m]);
        var before = (await ReadAsync(viewer, cave)).GetRawText();

        var hidden = await CaveWithEntrancesAsync(area, [600m], springCaveTypeId, locationProtected: true);
        (await viewer.GetAsync($"/api/v1/caves/{hidden}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ReadAsync(viewer, cave)).GetRawText().ShouldBe(before);

        // The author may place it, and for them the level drops: it is the rule that held it back.
        (await ReadAsync(owner, cave)).GetProperty("figures").GetProperty("potentialDepthM")
            .GetDouble().ShouldBe(600, 1e-6);
    }

    [Fact]
    public async Task A_cave_the_reader_may_not_place_is_answered_as_absent()
    {
        var hidden = await CaveWithEntrancesAsync(null, [1200m], locationProtected: true);

        (await viewer.GetAsync($"/api/v1/caves/{hidden}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var refused = await viewer.GetAsync($"/api/v1/caves/{hidden}/system-figures");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await refused.Content.ReadAsStringAsync()).ShouldNotContain("figures");

        var absent = await viewer.GetAsync($"/api/v1/caves/{Guid.CreateVersion7()}/system-figures");
        absent.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await ReadAsync(owner, hidden)).GetProperty("figures")
            .GetProperty("highestEntranceM").GetDouble().ShouldBe(1200, 1e-6);

        (await anonymous.GetAsync($"/api/v1/caves/{hidden}/system-figures"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await owner.GetAsync($"/api/v1/caves/{Guid.Empty}/system-figures"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    public async ValueTask DisposeAsync()
    {
        await factory.DisposeAsync();
        Dispose();
    }

    public void Dispose()
    {
        owner?.Dispose();
        viewer?.Dispose();
        anonymous?.Dispose();
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, Guid caveId)
    {
        var response = await client.GetAsync($"/api/v1/caves/{caveId}/system-figures");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<Guid> CreateAreaAsync()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"SysFig Area {Guid.NewGuid():N}"[..36],
            featureTypeId = karstAreaTypeId,
            geometry = new
            {
                type = "Polygon",
                coordinates = new[]
                {
                    new[]
                    {
                        new[] { 25.40, 45.50 }, new[] { 25.55, 45.50 },
                        new[] { 25.55, 45.62 }, new[] { 25.40, 45.62 },
                        new[] { 25.40, 45.50 },
                    },
                },
            },
            locationProtected = false,
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CaveWithEntrancesAsync(
        Guid? area, decimal[] altitudes, long? typeId = null, bool locationProtected = false)
    {
        var caveResponse = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"SysFig {Guid.NewGuid():N}"[..20],
            caveTypeId = typeId ?? caveTypeId,
            visibility = "authenticated",
            locationProtected,
            parentId = area,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await caveResponse.Content.ReadAsStringAsync();
        caveResponse.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var cave = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        for (var i = 0; i < altitudes.Length; i++)
        {
            var response = await owner.PostAsJsonAsync($"/api/v1/caves/{cave}/entrances", new
            {
                entranceTypeId,
                isMain = i == 0,
                geom = new { type = "Point", coordinates = new[] { 25.45 + (i * 0.001), 45.55 } },
                altitude = altitudes[i],
                positionQuality = "Gps",
            });
            response.StatusCode.ShouldBe(
                HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        }

        return cave;
    }

    /// <summary>A passage that steps down in altitude, so highest and lowest are known by construction.</summary>
    private async Task UploadDescentAsync(Guid caveId, double topM, double bottomM)
    {
        var points = Enumerable.Range(0, 11).Select(i =>
        {
            var z = topM + ((bottomM - topM) * i / 10d);
            return string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"[{25.450 + (i * 0.0005):0.#####},45.55,{z:0.###}]");
        });
        var body = "{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\","
            + "\"properties\":{},\"geometry\":{\"type\":\"LineString\",\"coordinates\":["
            + string.Join(",", points) + "]}}]}";

        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        content.Headers.ContentType = new("application/geo+json");
        using var form = new MultipartFormDataContent { { content, "file", "Centerline.geojson" } };
        var response = await owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", form);
        response.StatusCode.ShouldBe(
            HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }
}
