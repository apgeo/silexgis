// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The entrance layer's clusters as a client consumes them: each names the lattice cell it summed
/// over, the zoom that switches clusters to points is the installation's setting, and both are what
/// a client needs to list a cluster's members without copying the server's arithmetic.
/// </summary>
public sealed class MapClusterConfigurationTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly PostgresFixture postgres;
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private long caveTypeId;
    private long entranceTypeId;

    public MapClusterConfigurationTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Map:ClusterMaxZoom"] = "9",
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"clu-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"clu-view-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"clu-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"clu-view-{suffix}@t.local");
    }

    [Fact]
    public async Task The_configured_cluster_zoom_is_published_and_decides_clusters_or_points()
    {
        await CreateCaveWithEntranceAsync("Cluster Config", 10.31, 44.71, locationProtected: false);
        (await viewer.GetFromJsonAsync<JsonObject>("/api/v1/map/config"))!
            ["clusterMaxZoom"]!.GetValue<int>().ShouldBe(9);

        const string bbox = "10.0,44.5,10.6,44.9";
        IsClusters(await EntrancesAsync(viewer, bbox, 8)).ShouldBeTrue();
        // Zoom 10 was a cluster zoom under the shipped default of 11; this installation says 9.
        IsClusters(await EntrancesAsync(viewer, bbox, 9)).ShouldBeFalse();
        IsClusters(await EntrancesAsync(viewer, bbox, 10)).ShouldBeFalse();
    }

    /// <summary>
    /// A cluster's members are exactly what the points branch answers for its cell at the published
    /// zoom — the protected entrance among them included, at the snapped point that decided both.
    /// </summary>
    [Fact]
    public async Task A_cluster_names_its_cell_and_the_cell_lists_exactly_its_members()
    {
        await CreateCaveWithEntranceAsync("Cluster Open A", 11.302, 45.402, locationProtected: false);
        await CreateCaveWithEntranceAsync("Cluster Open B", 11.318, 45.415, locationProtected: false);
        await CreateCaveWithEntranceAsync("Cluster Guarded", 11.311, 45.409, locationProtected: true);

        const int zoom = 6;
        var clusters = (await EntrancesAsync(viewer, "10.5,44.8,12.0,46.0", zoom))["features"]!.AsArray();
        clusters.Count.ShouldBe(1);
        var properties = clusters[0]!["properties"]!;
        properties["count"]!.GetValue<int>().ShouldBe(3);

        var cell = properties["cell"]!.AsArray().Select(v => v!.GetValue<double>()).ToArray();
        cell.Length.ShouldBe(4);
        var size = 360d / Math.Pow(2, zoom) / 4d;
        (cell[2] - cell[0]).ShouldBe(size, 1e-9);
        (cell[3] - cell[1]).ShouldBe(size, 1e-9);
        // The global lattice: a cell's edges sit on half-multiples of its size, whoever asks.
        var westEdge = (cell[0] / size) + 0.5;
        westEdge.ShouldBe(Math.Round(westEdge), 1e-9);

        var cellBbox = string.Join(',', cell.Select(v => v.ToString("R", Invariant)));
        var members = (await EntrancesAsync(viewer, cellBbox, 9))["features"]!.AsArray();
        members.Count.ShouldBe(3);
        members.Count(m => m!["properties"]!["approximate"]!.GetValue<bool>()).ShouldBe(1);
    }

    [Fact]
    public void A_cluster_zoom_outside_the_map_zoom_range_refuses_to_start()
    {
        using var misconfigured = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Map:ClusterMaxZoom"] = "30",
        });
        var refusal = Should.Throw<OptionsValidationException>(() => misconfigured.CreateClient());
        refusal.Message.ShouldContain("Map:ClusterMaxZoom");
    }

    private static bool IsClusters(JsonObject body)
    {
        var features = body["features"]!.AsArray();
        features.Count.ShouldBeGreaterThan(0);
        return features.All(f => f!["properties"]!["cluster"]?.GetValue<bool>() == true);
    }

    private static async Task<JsonObject> EntrancesAsync(HttpClient client, string bbox, int zoom)
    {
        var response = await client.GetAsync($"/api/v1/map/cave-entrances?bbox={bbox}&zoom={zoom}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonNode.Parse(payload)!.AsObject();
    }

    private async Task CreateCaveWithEntranceAsync(string name, double lon, double lat, bool locationProtected)
    {
        var cave = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"{name} {Guid.NewGuid():N}"[..36],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await cave.Content.ReadAsStringAsync();
        cave.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var caveId = JsonNode.Parse(payload)!["id"]!.GetValue<Guid>();

        var entrance = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            positionQuality = "Gps",
        });
        entrance.StatusCode.ShouldBe(HttpStatusCode.Created, await entrance.Content.ReadAsStringAsync());
    }

    public Task DisposeAsync()
    {
        owner.Dispose();
        viewer.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
