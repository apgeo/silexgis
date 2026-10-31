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
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A cave's descent profile. It reads no elevation model and carries no position, but an altitude
/// profile is the cave's own shape, so a cave the caller may read and not place answers as absent.
/// </summary>
public sealed class CaveDepthProfileTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private HttpClient anonymous = null!;
    private long caveTypeId;

    public CaveDepthProfileTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"dp-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"dp-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"dp-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"dp-read-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    [Fact]
    public async Task The_profile_runs_from_the_highest_point_down_and_names_no_position()
    {
        var cave = await CaveAsync(locationProtected: false);
        await UploadDescentAsync(cave, 1150, 900);

        var response = await viewer.GetAsync($"/api/v1/caves/{cave}/depth-profile");
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, raw);
        var body = JsonDocument.Parse(raw).RootElement;

        body.GetProperty("hasAltitudes").GetBoolean().ShouldBeTrue();
        body.GetProperty("highestM").GetDouble().ShouldBe(1150, 1e-3);
        body.GetProperty("lowestM").GetDouble().ShouldBe(900, 1e-3);
        body.GetProperty("deepestM").GetDouble().ShouldBe(250, 1e-3);

        var samples = body.GetProperty("samples").EnumerateArray().ToList();
        samples.Count.ShouldBeGreaterThan(2);
        samples[0].GetProperty("depthM").GetDouble().ShouldBe(0, 1e-3);
        samples[^1].GetProperty("depthM").GetDouble().ShouldBe(250, 1e-3);
        samples.Select(s => s.GetProperty("distanceAlongM").GetDouble())
            .ShouldBe(samples.Select(s => s.GetProperty("distanceAlongM").GetDouble()).Order());

        raw.ShouldNotContain("ongitude");
        raw.ShouldNotContain("atitude");
    }

    [Fact]
    public async Task A_cave_with_no_line_work_has_an_empty_profile_not_a_flat_one()
    {
        var cave = await CaveAsync(locationProtected: false);

        var body = await ReadAsync(viewer, cave);
        body.GetProperty("hasAltitudes").GetBoolean().ShouldBeFalse();
        body.GetProperty("samples").GetArrayLength().ShouldBe(0);
        body.GetProperty("highestM").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_cave_the_reader_may_not_place_is_answered_as_absent_and_the_author_is_not_refused()
    {
        var hidden = await CaveAsync(locationProtected: true);
        await UploadDescentAsync(hidden, 1150, 900);

        (await viewer.GetAsync($"/api/v1/caves/{hidden}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var refused = await viewer.GetAsync($"/api/v1/caves/{hidden}/depth-profile");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await refused.Content.ReadAsStringAsync()).ShouldNotContain("samples");

        (await viewer.GetAsync($"/api/v1/caves/{Guid.CreateVersion7()}/depth-profile"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await ReadAsync(owner, hidden)).GetProperty("samples").GetArrayLength().ShouldBeGreaterThan(0);

        (await anonymous.GetAsync($"/api/v1/caves/{hidden}/depth-profile"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await owner.GetAsync($"/api/v1/caves/{Guid.Empty}/depth-profile"))
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
        var response = await client.GetAsync($"/api/v1/caves/{caveId}/depth-profile");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<Guid> CaveAsync(bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"DepthProf {Guid.NewGuid():N}"[..20],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

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
