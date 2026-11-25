// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The markers cave entrances are drawn with: chosen by an administrator, published to every
/// signed-in reader with the map's configuration, and to a shared view's page with its overlays.
/// </summary>
/// <remarks>
/// The class owns its database, so the one row the section writes is never seen by another class;
/// within the class the tests that read the defaults run against a factory whose section was never
/// saved, and the test that saves puts the defaults back before it ends.
/// </remarks>
public sealed class MapSymbolSettingsTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Route = "/api/v1/admin/settings/map-symbols";

    private readonly SilexGisApiFactory factory;
    private readonly SilexGisApiFactory configured;

    private HttpClient admin = null!;
    private HttpClient editor = null!;
    private HttpClient viewer = null!;
    private HttpClient anonymous = null!;
    private HttpClient configuredViewer = null!;

    public MapSymbolSettingsTests(PostgresFixture postgres)
    {
        factory = new SilexGisApiFactory(postgres.ConnectionString);
        // A deployment can set the markers by configuration; a mistyped one falls back to the default
        // rather than reaching a browser.
        configured = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["MapSymbols:EntranceSymbol"] = "qgis_poi_cave.svg",
            ["MapSymbols:ApproximateEntranceSymbol"] = "../not/a/symbol.svg",
        });
    }

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"sym-adm-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"sym-ed-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"sym-view-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"sym-adm-{suffix}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"sym-ed-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"sym-view-{suffix}@t.local");
        anonymous = factory.CreateClient();
        configuredViewer = await AuthHelper.BearerClientAsync(configured, $"sym-view-{suffix}@t.local");
    }

    private async Task<(string Entrance, string Approximate)> MapConfigSymbolsAsync(HttpClient client)
    {
        var config = await client.GetFromJsonAsync<JsonElement>("/api/v1/map/config");
        return (config.GetProperty("entranceSymbol").GetString()!, config.GetProperty("approximateEntranceSymbol").GetString()!);
    }

    [Fact]
    public async Task An_installation_that_chose_nothing_draws_entrances_with_the_cave_entrance_shape()
    {
        (await MapConfigSymbolsAsync(viewer)).ShouldBe(("shape:cave", "shape:cave:d46b08"));

        var settings = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/settings");
        var section = settings.GetProperty("mapSymbols");
        section.GetProperty("entranceSymbol").GetString().ShouldBe("shape:cave");
        section.GetProperty("approximateEntranceSymbol").GetString().ShouldBe("shape:cave:d46b08");
    }

    [Fact]
    public async Task An_administrator_chooses_the_markers_and_every_reader_is_told_them()
    {
        try
        {
            var saved = await admin.PutAsJsonAsync(Route, new
            {
                entranceSymbol = "osm_cave.svg",
                approximateEntranceSymbol = "shape:triangle:aa5500",
            });
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
            var echoed = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mapSymbols");
            echoed.GetProperty("entranceSymbol").GetString().ShouldBe("osm_cave.svg");

            (await MapConfigSymbolsAsync(viewer)).ShouldBe(("osm_cave.svg", "shape:triangle:aa5500"));
        }
        finally
        {
            // Saved back to the defaults for this factory's cache, and the row removed, so the
            // configured factory beside it reads its configuration rather than this row.
            (await admin.PutAsJsonAsync(Route, new
            {
                entranceSymbol = "shape:cave",
                approximateEntranceSymbol = "shape:cave:d46b08",
            })).StatusCode.ShouldBe(HttpStatusCode.OK);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.AppSettings.Where(row => row.Key == AppSettingSections.MapSymbols).ExecuteDeleteAsync();
        }
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("https://elsewhere.example/pin.svg")]
    [InlineData("/feature_symbols/pit.png")]
    [InlineData("shape:hexagon")]
    [InlineData("shape:cave:D46B08")]
    [InlineData("pit.gif")]
    [InlineData("")]
    public async Task A_reference_that_is_neither_a_bundled_file_nor_a_drawn_shape_is_refused(string symbol)
    {
        (await admin.PutAsJsonAsync(Route, new { entranceSymbol = symbol, approximateEntranceSymbol = "shape:cave" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutAsJsonAsync(Route, new { entranceSymbol = "shape:cave", approximateEntranceSymbol = symbol }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await MapConfigSymbolsAsync(viewer)).ShouldBe(("shape:cave", "shape:cave:d46b08"));
    }

    [Fact]
    public async Task Only_a_settings_administrator_may_choose_them_and_only_a_signed_in_reader_is_told()
    {
        var body = new { entranceSymbol = "shape:star", approximateEntranceSymbol = "shape:star:d46b08" };
        (await anonymous.PutAsJsonAsync(Route, body)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await editor.PutAsJsonAsync(Route, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.PutAsJsonAsync(Route, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await anonymous.GetAsync("/api/v1/map/config")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await MapConfigSymbolsAsync(viewer)).ShouldBe(("shape:cave", "shape:cave:d46b08"));
    }

    [Fact]
    public async Task A_deployment_sets_them_by_configuration_and_a_mistyped_one_falls_back_to_the_default()
    {
        (await MapConfigSymbolsAsync(configuredViewer)).ShouldBe(("qgis_poi_cave.svg", "shape:cave:d46b08"));
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        admin?.Dispose();
        editor?.Dispose();
        viewer?.Dispose();
        anonymous?.Dispose();
        configuredViewer?.Dispose();
        factory.Dispose();
        configured.Dispose();
    }
}
