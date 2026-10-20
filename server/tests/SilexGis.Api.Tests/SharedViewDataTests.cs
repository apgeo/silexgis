// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Map;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a shared map view's link draws: its background, and the public cave entrances and surface
/// features it was saved with switched on — for nobody in particular.
/// </summary>
/// <remarks>
/// <para>
/// The link holder is the least-privileged reader there is: they read only rows at the widest
/// visibility level, and no protected row at all — not snapped, not counted, in no window,
/// whatever the installation grants every account. These tests hold each half of that, windows
/// whose edges cross a protected cave's true and snapped positions, and the three ways a link
/// stops drawing data: it is revoked, the overlay was off, or the installation switched data on
/// shared views off.
/// </para>
/// <para>
/// Every place here is well away from the demonstration data, so a window holds only what the
/// test put in it.
/// </para>
/// </remarks>
public sealed class SharedViewDataTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    /// <summary>The shipped location-protection grid.</summary>
    private const double GridMeters = 5000d;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly PostgresFixture postgres;
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;
    private HttpClient member = null!;
    private HttpClient anonymous = null!;
    private long caveTypeId;
    private long entranceTypeId;
    private long sinkholeTypeId;

    public SharedViewDataTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
        factory = new SilexGisApiFactory(postgres.ConnectionString);
    }

    private static double Cell => LocationProtection.CellDegrees(GridMeters);

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"svd-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"svd-mem-{suffix}@t.local");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
            sinkholeTypeId = await db.FeatureTypes.Where(t => t.Code == "sinkhole").Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"svd-own-{suffix}@t.local");
        member = await AuthHelper.BearerClientAsync(factory, $"svd-mem-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    [Fact]
    public async Task A_link_draws_public_entrances_and_features_and_nothing_narrower()
    {
        var publicCave = await CreateCaveAsync("Link Public", 10.101, 60.101, "public", locationProtected: false);
        await CreateCaveAsync("Link Accounts", 10.121, 60.101, "authenticated", locationProtected: false);
        await CreateCaveAsync("Link Private", 10.141, 60.101, "private", locationProtected: false);
        await CreatePointFeatureAsync("Link Public Sinkhole", 10.111, 60.111, "public", locationProtected: false);
        await CreatePointFeatureAsync("Link Accounts Sinkhole", 10.131, 60.111, "authenticated", locationProtected: false);

        var token = await ShareAsync(await CreateViewAsync(entrances: true, features: true, 10.1, 60.1));
        const string window = "10.0,60.0,10.2,60.2";

        var view = await GetJsonAsync(anonymous, $"/api/v1/shared/views/{token}");
        view["overlays"]!["entrances"]!.GetValue<bool>().ShouldBeTrue();
        view["overlays"]!["features"]!.GetValue<bool>().ShouldBeTrue();
        view["background"].ShouldNotBeNull();

        // Points: the public cave's entrance, exactly where it is, by its public name — and no
        // handle to ask for it anywhere else.
        var points = (await GetJsonAsync(anonymous, $"/api/v1/shared/views/{token}/cave-entrances?bbox={window}&zoom=14"))["features"]!.AsArray();
        var point = points.ShouldHaveSingleItem()!;
        point["geometry"]!["coordinates"]![0]!.GetValue<double>().ShouldBe(10.101, 1e-9);
        point["properties"]!.AsObject().ContainsKey("approximate").ShouldBeFalse();
        point["properties"]!["caveName"]!.GetValue<string>().ShouldBe(publicCave.Name);
        point["properties"]!.AsObject().ContainsKey("id").ShouldBeFalse();
        point["properties"]!.AsObject().ContainsKey("caveId").ShouldBeFalse();

        // Clusters count the same one entrance, and nothing narrower — at its own position: nothing
        // a link draws is snapped, because nothing it draws is protected.
        var clusters = (await GetJsonAsync(anonymous, $"/api/v1/shared/views/{token}/cave-entrances?bbox={window}&zoom=6"))["features"]!.AsArray();
        var cluster = clusters.ShouldHaveSingleItem()!;
        cluster["properties"]!["count"]!.GetValue<int>().ShouldBe(1);
        cluster["geometry"]!["coordinates"]![0]!.GetValue<double>().ShouldBe(10.101, 1e-9);
        cluster["geometry"]!["coordinates"]![1]!.GetValue<double>().ShouldBe(60.101, 1e-9);

        var features = (await GetJsonAsync(anonymous, $"/api/v1/shared/views/{token}/features?bbox={window}"))["features"]!.AsArray();
        features.ShouldHaveSingleItem()!["geometry"]!["coordinates"]![0]!.GetValue<double>().ShouldBe(10.111, 1e-9);

        // The rows the link leaves out are there, and every account reads them.
        (await CountAsync(member, $"/api/v1/map/cave-entrances?bbox={window}&zoom=14")).ShouldBe(2);
    }

    /// <summary>
    /// A protected public cave is left off a link entirely — even where the installation gives every
    /// account exact view of it — at the cluster zoom and the point zoom, in every window: one round
    /// its true position, one round the protection cell it would snap to, and windows whose edges
    /// cross either. A public cave nothing protects, inside the same protection cell, is drawn in
    /// each window that holds it, and the cluster that holds both counts one.
    /// </summary>
    [Theory]
    [InlineData(6, 11.5, 61.2)]
    [InlineData(14, 12.5, 61.6)]
    public async Task A_protected_public_cave_is_left_off_a_link_at_every_zoom_and_in_every_window(
        int zoom, double nearLon, double nearLat)
    {
        var snappedLon = Math.Round(nearLon / Cell, MidpointRounding.AwayFromZero) * Cell;
        var snappedLat = Math.Round(nearLat / Cell, MidpointRounding.AwayFromZero) * Cell;
        var lon = snappedLon - (0.3 * Cell);
        var lat = snappedLat + (0.1 * Cell);
        var openLon = snappedLon + (0.3 * Cell);
        var openLat = snappedLat - (0.2 * Cell);
        var step = 0.05 * Cell;
        var guarded = await CreateCaveAsync("Link Guarded", lon, lat, "public", locationProtected: true);
        await GrantEveryAccountExactViewAsync(guarded.Id);
        var open = await CreateCaveAsync("Link Open Neighbour", openLon, openLat, "public", locationProtected: false);

        var token = await ShareAsync(await CreateViewAsync(entrances: true, features: false, snappedLon, snappedLat, zoom: 10));
        string Url(string bbox) => $"/api/v1/shared/views/{token}/cave-entrances?bbox={bbox}&zoom={zoom}";

        var windows = new (double West, double South, double East, double North)[]
        {
            (snappedLon - 0.4, snappedLat - 0.4, snappedLon + 0.4, snappedLat + 0.4),
            (lon - step, lat - step, lon + step, lat + step),
            (snappedLon - step, snappedLat - step, snappedLon + step, snappedLat + step),
            (lon - step, snappedLat - 0.4, snappedLon + 0.4, snappedLat + 0.4),
            (lon + step, snappedLat - 0.4, snappedLon + 0.4, snappedLat + 0.4),
            (snappedLon - 0.4, snappedLat - 0.4, lon + step, snappedLat + 0.4),
            (snappedLon - 0.4, snappedLat - 0.4, lon - step, snappedLat + 0.4),
            (snappedLon - 0.4, snappedLat - 0.4, snappedLon + step, snappedLat + 0.4),
            (snappedLon - step, snappedLat - 0.4, snappedLon + 0.4, snappedLat + 0.4),
        };
        foreach (var (west, south, east, north) in windows)
        {
            var bbox = Window(west, south, east, north);
            var holdsOpen = openLon >= west && openLon <= east && openLat >= south && openLat <= north;
            (await CountAsync(anonymous, Url(bbox))).ShouldBe(holdsOpen ? 1 : 0, $"window {bbox}");

            if (zoom >= 11)
            {
                // Every point drawn is the open cave's, exactly where it is: nothing at the guarded
                // cave's position and nothing at the cell it would snap to.
                foreach (var drawn in (await GetJsonAsync(anonymous, Url(bbox)))["features"]!.AsArray())
                {
                    drawn!["geometry"]!["coordinates"]![0]!.GetValue<double>().ShouldBe(openLon, 1e-9);
                    drawn["geometry"]!["coordinates"]![1]!.GetValue<double>().ShouldBe(openLat, 1e-9);
                    drawn["properties"]!["caveName"]!.GetValue<string>().ShouldBe(open.Name);
                    drawn["properties"]!.AsObject().ContainsKey("approximate").ShouldBeFalse();
                }
            }
        }

        // The control: the guarded cave is there, and every account — by the installation's grant —
        // places it exactly. The link draws it nowhere.
        var all = Window(snappedLon - 0.4, snappedLat - 0.4, snappedLon + 0.4, snappedLat + 0.4);
        (await CountAsync(member, $"/api/v1/map/cave-entrances?bbox={all}&zoom={zoom}")).ShouldBe(2);
        (await CountAsync(member, $"/api/v1/map/cave-entrances?bbox={Window(lon - step, lat - step, lon + step, lat + step)}&zoom=14")).ShouldBe(1);
    }

    /// <summary>
    /// A public cave that is not flagged itself but lies inside a protected area is protected all the
    /// same, and is left off a link as a flagged one is; so is the protected area's outline.
    /// </summary>
    [Fact]
    public async Task A_public_cave_inside_a_protected_area_is_left_off_a_link()
    {
        double[][] ring = [[21.05, 68.05], [21.25, 68.05], [21.25, 68.25], [21.05, 68.25], [21.05, 68.05]];
        // A window of its own: another test of this class places a public cave at 19.1, 68.1, and
        // the tests of a class share one database, so a window there would count it whenever that test ran first.
        var area = await CreateFeatureAsync("Link Guarded Karst", new { type = "Polygon", coordinates = new[] { ring } },
            "karst_area", "public", locationProtected: true);
        await CreateCaveAsync("Link Cave In Guarded Karst", 21.101, 68.101, "public", locationProtected: false, parentId: area);
        await CreateCaveAsync("Link Cave Outside Karst", 21.281, 68.101, "public", locationProtected: false);
        // A readable feature in the same window, so that an outline missing from the link's features is
        // shown to be missing because it is protected and not because the route draws nothing there.
        await CreatePointFeatureAsync("Link Sinkhole Outside Karst", 21.281, 68.201, "public", locationProtected: false);

        var token = await ShareAsync(await CreateViewAsync(entrances: true, features: true, 21.15, 68.15, zoom: 11));
        const string window = "21.0,68.0,21.3,68.3";

        foreach (var zoom in new[] { 9, 14 })
        {
            (await CountAsync(anonymous, $"/api/v1/shared/views/{token}/cave-entrances?bbox={window}&zoom={zoom}"))
                .ShouldBe(1, $"zoom {zoom}");
        }

        var point = (await GetJsonAsync(anonymous, $"/api/v1/shared/views/{token}/cave-entrances?bbox={window}&zoom=14"))["features"]!
            .AsArray().ShouldHaveSingleItem()!;
        point["geometry"]!["coordinates"]![0]!.GetValue<double>().ShouldBe(21.281, 1e-9);
        var feature = (await GetJsonAsync(anonymous, $"/api/v1/shared/views/{token}/features?bbox={window}"))["features"]!
            .AsArray().ShouldHaveSingleItem()!;
        feature["geometry"]!["type"]!.GetValue<string>().ShouldBe("Point");
        feature["geometry"]!["coordinates"]![0]!.GetValue<double>().ShouldBe(21.281, 1e-9);
        feature["geometry"]!["coordinates"]![1]!.GetValue<double>().ShouldBe(68.201, 1e-9);

        // The controls: every account reads both caves, the one in the protected area snapped, and the
        // area's outline is drawn for its owner in the same window.
        (await CountAsync(member, $"/api/v1/map/cave-entrances?bbox={window}&zoom=14")).ShouldBe(2);
        (await GetJsonAsync(owner, $"/api/v1/map/features?bbox={window}"))["features"]!.AsArray()
            .Select(f => f!["properties"]!["id"]!.GetValue<Guid>()).ShouldContain(area);
    }

    [Fact]
    public async Task Protected_public_features_are_left_off_a_link_points_and_outlines_alike()
    {
        double[][] ring = [[13.10, 62.10], [13.12, 62.10], [13.12, 62.12], [13.10, 62.12], [13.10, 62.10]];
        await CreateFeatureAsync("Link Guarded Area", new { type = "Polygon", coordinates = new[] { ring } },
            "karst_area", "public", locationProtected: true);
        var snappedLon = Math.Round(13.15 / Cell, MidpointRounding.AwayFromZero) * Cell;
        var snappedLat = Math.Round(62.15 / Cell, MidpointRounding.AwayFromZero) * Cell;
        await CreatePointFeatureAsync("Link Guarded Point", snappedLon - (0.2 * Cell), snappedLat + (0.2 * Cell), "public", locationProtected: true);
        await CreatePointFeatureAsync("Link Open Point", 13.251, 62.251, "public", locationProtected: false);

        var token = await ShareAsync(await CreateViewAsync(entrances: false, features: true, 13.2, 62.2, zoom: 10));
        var drawn = (await GetJsonAsync(anonymous, $"/api/v1/shared/views/{token}/features?bbox=13.0,62.0,13.4,62.4"))["features"]!.AsArray();

        // Only the open point, exactly where it is; nothing at the guarded point's cell.
        var point = drawn.ShouldHaveSingleItem()!;
        point["geometry"]!["coordinates"]![0]!.GetValue<double>().ShouldBe(13.251, 1e-9);
        point["properties"]!.AsObject().ContainsKey("approximate").ShouldBeFalse();
        (await GetJsonAsync(anonymous, $"/api/v1/shared/views/{token}/features?bbox={Window(snappedLon - 0.01, snappedLat - 0.01, snappedLon + 0.01, snappedLat + 0.01)}"))["features"]!
            .AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Revoking_the_link_stops_its_data_at_once()
    {
        await CreateCaveAsync("Link Revoked", 14.101, 63.101, "public", locationProtected: false);
        var viewId = await CreateViewAsync(entrances: true, features: true, 14.1, 63.1);
        var token = await ShareAsync(viewId);
        const string window = "14.0,63.0,14.2,63.2";
        (await anonymous.GetAsync($"/api/v1/shared/views/{token}/cave-entrances?bbox={window}&zoom=14"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await owner.DeleteAsync($"/api/v1/map-views/{viewId}/share")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await ShouldBeNotFoundAsync(anonymous, $"/api/v1/shared/views/{token}/cave-entrances?bbox={window}&zoom=14");
        await ShouldBeNotFoundAsync(anonymous, $"/api/v1/shared/views/{token}/features?bbox={window}");
        await ShouldBeNotFoundAsync(anonymous, $"/api/v1/shared/views/{token}");
        // An invented token is answered exactly alike.
        await ShouldBeNotFoundAsync(anonymous, $"/api/v1/shared/views/{Guid.NewGuid()}/cave-entrances?bbox={window}&zoom=14");
    }

    [Fact]
    public async Task An_overlay_the_view_had_off_is_not_drawn()
    {
        var token = await ShareAsync(await CreateViewAsync(entrances: true, features: false, 14.1, 63.1));

        var view = await GetJsonAsync(anonymous, $"/api/v1/shared/views/{token}");
        view["overlays"]!["features"]!.GetValue<bool>().ShouldBeFalse();
        await ShouldBeNotFoundAsync(anonymous, $"/api/v1/shared/views/{token}/features?bbox=14.0,63.0,14.2,63.2");
        (await anonymous.GetAsync($"/api/v1/shared/views/{token}/cave-entrances?bbox=14.0,63.0,14.2,63.2&zoom=14"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_window_off_the_globe_is_refused()
    {
        var token = await ShareAsync(await CreateViewAsync(entrances: true, features: true, 25.5, 45.5));

        var response = await anonymous.GetAsync($"/api/v1/shared/views/{token}/cave-entrances?bbox=26,45,25,46&zoom=14");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["code"]!.GetValue<string>().ShouldBe("map.invalid_bbox");
    }

    /// <summary>
    /// A view saved over a basemap that needs an access key opens over the installation's default
    /// keyless basemap instead, and the key — which is in the keyed source's address — is in no
    /// part of anything the link is sent.
    /// </summary>
    [Fact]
    public async Task A_keyed_background_falls_back_and_its_key_never_reaches_the_link()
    {
        const string secret = "SECRET-TILE-KEY-7f3a";
        var keyedId = await AddLayerAsync(new MapLayer
        {
            Name = $"Keyed {Guid.NewGuid():N}"[..30],
            UrlTemplate = $"https://keyed.example/{{z}}/{{x}}/{{y}}.png?key={secret}",
            ApiKeyName = "TestKey",
            IsBase = true,
            Enabled = true,
            Attribution = "© Keyed",
        });

        var token = await ShareAsync(await CreateViewAsync(entrances: false, features: false, 10.1, 60.1, baseLayerId: keyedId));
        var response = await anonymous.GetAsync($"/api/v1/shared/views/{token}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);

        payload.ShouldNotContain(secret);
        string defaultName;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            defaultName = await db.MapLayers
                .Where(l => l.IsDefault && l.IsBase && l.Enabled && l.ApiKeyName == null)
                .OrderBy(l => l.SortOrder)
                .Select(l => l.Name)
                .FirstAsync();
        }

        JsonNode.Parse(payload)!["background"]!["name"]!.GetValue<string>().ShouldBe(defaultName);
    }

    [Fact]
    public async Task A_map_service_background_reaches_the_link_with_its_options()
    {
        var entry = MapLayerCatalog.Read(
            """<silexgis-map-layers version="1"><layer name="Service" kind="wms" url="https://maps.example.gov/wms" layers="topo" attribution="© Agency" /></silexgis-map-layers>""")
            .Layers.Single();
        var serviceId = await AddLayerAsync(new MapLayer
        {
            Name = $"Service {Guid.NewGuid():N}"[..30],
            UrlTemplate = entry.UrlTemplate,
            LayerKind = MapLayerKind.Wms,
            Options = MapLayerOptionsJson.Write(entry),
            IsBase = true,
            Enabled = true,
            Attribution = entry.Attribution,
            // A row added by hand is off links until it says otherwise, as one from the file would be.
            OnSharedLinks = true,
        });

        var token = await ShareAsync(await CreateViewAsync(entrances: false, features: false, 10.1, 60.1, baseLayerId: serviceId));
        var background = (await GetJsonAsync(anonymous, $"/api/v1/shared/views/{token}"))["background"]!;

        background["layerKind"]!.GetValue<string>().ShouldBe("wms");
        background["urlTemplate"]!.GetValue<string>().ShouldBe("https://maps.example.gov/wms");
        background["wms"]!["layers"]!.GetValue<string>().ShouldBe("topo");
        background["wms"]!["crs"]!.GetValue<string>().ShouldBe("EPSG:3857");
    }

    [Fact]
    public async Task With_data_on_shared_views_switched_off_a_link_draws_its_background_only()
    {
        using var off = new SilexGisApiFactory(
            postgres.ConnectionString, new Dictionary<string, string?> { ["Map:SharedViewData"] = "false" });
        await CreateCaveAsync("Link Switched Off", 15.101, 64.101, "public", locationProtected: false);
        var token = await ShareAsync(await CreateViewAsync(entrances: true, features: true, 15.1, 64.1));
        using var stranger = off.CreateClient();

        var view = await GetJsonAsync(stranger, $"/api/v1/shared/views/{token}");
        view["overlays"]!["entrances"]!.GetValue<bool>().ShouldBeFalse();
        view["overlays"]!["features"]!.GetValue<bool>().ShouldBeFalse();
        view["background"].ShouldNotBeNull();
        await ShouldBeNotFoundAsync(stranger, $"/api/v1/shared/views/{token}/cave-entrances?bbox=15.0,64.0,15.2,64.2&zoom=14");
        await ShouldBeNotFoundAsync(stranger, $"/api/v1/shared/views/{token}/features?bbox=15.0,64.0,15.2,64.2");
    }

    /// <summary>
    /// A link answers only inside the view it shares: a public cave a few view-widths away is not
    /// on it at any window or zoom, a window wholly outside is answered with nothing in the usual
    /// shape, and a request further out than the reach allows is answered at the reach's lowest
    /// zoom — so the link is never a read of the installation's whole public register.
    /// </summary>
    [Fact]
    public async Task A_link_answers_only_inside_the_view_it_shares()
    {
        await CreateCaveAsync("Link Near", 16.101, 65.101, "public", locationProtected: false);
        await CreateCaveAsync("Link Far", 19.101, 65.101, "public", locationProtected: false);
        await CreatePointFeatureAsync("Link Near Sinkhole", 16.111, 65.111, "public", locationProtected: false);
        await CreatePointFeatureAsync("Link Far Sinkhole", 19.111, 65.111, "public", locationProtected: false);
        var token = await ShareAsync(await CreateViewAsync(entrances: true, features: true, 16.1, 65.1, zoom: 12));

        // A window over both, a whole-world window, and the coarsest zoom anybody can ask: the
        // near cave only, every time.
        const string both = "15.9,64.9,19.3,65.3";
        const string world = "-180,-90,180,90";
        foreach (var url in new[]
        {
            $"/api/v1/shared/views/{token}/cave-entrances?bbox={both}&zoom=14",
            $"/api/v1/shared/views/{token}/cave-entrances?bbox={both}&zoom=6",
            $"/api/v1/shared/views/{token}/cave-entrances?bbox={world}&zoom=0",
            $"/api/v1/shared/views/{token}/cave-entrances?bbox={world}&zoom=14",
        })
        {
            (await CountAsync(anonymous, url)).ShouldBe(1, url);
        }

        (await GetJsonAsync(anonymous, $"/api/v1/shared/views/{token}/features?bbox={both}"))["features"]!.AsArray()
            .ShouldHaveSingleItem()!["geometry"]!["coordinates"]![0]!.GetValue<double>().ShouldBe(16.111, 1e-9);
        (await GetJsonAsync(anonymous, $"/api/v1/shared/views/{token}/features?bbox={world}"))["features"]!.AsArray()
            .Count.ShouldBe(1);

        // Asked about the far cave alone: the usual shape, with nothing in it.
        const string far = "19.0,65.0,19.2,65.2";
        var nothing = await GetJsonAsync(anonymous, $"/api/v1/shared/views/{token}/cave-entrances?bbox={far}&zoom=14");
        nothing["type"]!.GetValue<string>().ShouldBe("FeatureCollection");
        nothing["features"]!.AsArray().ShouldBeEmpty();
        (await GetJsonAsync(anonymous, $"/api/v1/shared/views/{token}/features?bbox={far}"))["features"]!.AsArray()
            .ShouldBeEmpty();

        // A zoom further out than the reach allows is answered at the reach's lowest, which is
        // still a cluster zoom here: the near cave's cluster names a cell of that zoom's lattice.
        var cluster = (await GetJsonAsync(anonymous, $"/api/v1/shared/views/{token}/cave-entrances?bbox={both}&zoom=2"))["features"]!
            .AsArray().ShouldHaveSingleItem()!;
        var cell = cluster["properties"]!["cell"]!.AsArray();
        (cell[2]!.GetValue<double>() - cell[0]!.GetValue<double>()).ShouldBe(360d / Math.Pow(2, 10) / 4d, 1e-9);

        // Every account still sees both, on the workspace map.
        (await CountAsync(member, $"/api/v1/map/cave-entrances?bbox={both}&zoom=14")).ShouldBe(2);

        // The page is told the reach, so it can hold its map inside it.
        var reach = (await GetJsonAsync(anonymous, $"/api/v1/shared/views/{token}"))["reach"]!;
        reach["minZoom"]!.GetValue<int>().ShouldBe(10);
        reach["west"]!.GetValue<double>().ShouldBeLessThan(16.1);
        reach["east"]!.GetValue<double>().ShouldBeGreaterThan(16.1);
        reach["east"]!.GetValue<double>().ShouldBeLessThan(19.1, "the far cave must be outside the view's reach");
    }

    /// <summary>
    /// Only rows that are themselves public are drawn on a link — an entrance by its cave's own
    /// level, a feature by its own. A private cave and a private sinkhole filed inside a public
    /// karst area are readable by every account through the area (the ordinary cascade), and are
    /// not on the link; nor is an entrance marked public whose cave is private.
    /// </summary>
    [Fact]
    public async Task A_private_row_inside_a_public_container_is_not_drawn_on_a_link()
    {
        double[][] ring = [[17.05, 66.05], [17.25, 66.05], [17.25, 66.25], [17.05, 66.25], [17.05, 66.05]];
        var area = await CreateFeatureAsync("Link Public Area", new { type = "Polygon", coordinates = new[] { ring } },
            "karst_area", "public", locationProtected: false);
        await CreateCaveAsync("Link Private In Area", 17.101, 66.101, "private", locationProtected: false, parentId: area);
        await CreateFeatureAsync("Link Private Sinkhole In Area", new { type = "Point", coordinates = new[] { 17.111, 66.111 } },
            "sinkhole", "private", locationProtected: false, parentId: area);
        var privateCave = await CreateCaveAsync("Link Private Own Door", 17.121, 66.121, "private", locationProtected: false);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.Features.Where(f => f.Id == privateCave.EntranceId)
                .ExecuteUpdateAsync(u => u.SetProperty(f => f.Visibility, Visibility.Public));
        }

        var token = await ShareAsync(await CreateViewAsync(entrances: true, features: true, 17.15, 66.15, zoom: 11));
        const string window = "17.0,66.0,17.3,66.3";

        (await CountAsync(anonymous, $"/api/v1/shared/views/{token}/cave-entrances?bbox={window}&zoom=14")).ShouldBe(0);
        (await CountAsync(anonymous, $"/api/v1/shared/views/{token}/cave-entrances?bbox={window}&zoom=9")).ShouldBe(0);

        // The public area itself is drawn; the private sinkhole inside it is not.
        var drawn = (await GetJsonAsync(anonymous, $"/api/v1/shared/views/{token}/features?bbox={window}"))["features"]!.AsArray();
        drawn.ShouldHaveSingleItem()!["geometry"]!["type"]!.GetValue<string>().ShouldBe("Polygon");

        // The control: every account reads both entrances, one through the area and one by its own level.
        (await CountAsync(member, $"/api/v1/map/cave-entrances?bbox={window}&zoom=14")).ShouldBe(2);
    }

    /// <summary>A view saved narrowed to a tag is narrowed to it on its link too, on all three statements.</summary>
    [Fact]
    public async Task A_link_is_narrowed_by_the_views_tag_filter()
    {
        var tagName = $"Link Tag {Guid.NewGuid():N}"[..20];
        var tagged = await CreateCaveAsync("Link Tagged", 18.101, 67.101, "public", locationProtected: false);
        await CreateCaveAsync("Link Untagged", 18.121, 67.101, "public", locationProtected: false);
        var taggedSinkhole = await CreatePointFeatureAsync("Link Tagged Sinkhole", 18.111, 67.111, "public", locationProtected: false);
        await CreatePointFeatureAsync("Link Untagged Sinkhole", 18.131, 67.111, "public", locationProtected: false);
        string? slug = null;
        foreach (var id in new[] { tagged.EntranceId, taggedSinkhole })
        {
            var response = await owner.PostAsJsonAsync("/api/v1/taggings/", new { tagName, entityType = "feature", entityId = id });
            var payload = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
            slug = JsonNode.Parse(payload)!["tag"]!["slug"]!.GetValue<string>();
        }

        var token = await ShareAsync(await CreateViewAsync(entrances: true, features: true, 18.1, 67.1, tagFilter: slug));
        const string window = "18.0,67.0,18.2,67.2";

        var points = (await GetJsonAsync(anonymous, $"/api/v1/shared/views/{token}/cave-entrances?bbox={window}&zoom=14"))["features"]!.AsArray();
        points.ShouldHaveSingleItem()!["properties"]!["caveName"]!.GetValue<string>().ShouldBe(tagged.Name);
        (await CountAsync(anonymous, $"/api/v1/shared/views/{token}/cave-entrances?bbox={window}&zoom=10")).ShouldBe(1);
        (await GetJsonAsync(anonymous, $"/api/v1/shared/views/{token}/features?bbox={window}"))["features"]!.AsArray()
            .ShouldHaveSingleItem()!["geometry"]!["coordinates"]![0]!.GetValue<double>().ShouldBe(18.111, 1e-9);
    }

    /// <summary>
    /// A view whose filter holds a place of its own is refused when it is saved; one that is in the
    /// table all the same — written by hand, or before the refusal existed — is drawn through its
    /// link with no data at all, and nothing of its filter reaches the link.
    /// </summary>
    [Fact]
    public async Task A_link_to_a_view_whose_filter_holds_a_place_draws_no_data()
    {
        await CreateCaveAsync("Link Placed", 19.101, 68.101, "public", locationProtected: false);
        await CreatePointFeatureAsync("Link Placed Sinkhole", 19.111, 68.111, "public", locationProtected: false);
        var viewId = await CreateViewAsync(entrances: true, features: true, 19.1, 68.1);
        var token = await ShareAsync(viewId);
        const string window = "19.0,68.0,19.2,68.2";

        // The control: as saved, the link draws both.
        (await CountAsync(anonymous, $"/api/v1/shared/views/{token}/cave-entrances?bbox={window}&zoom=14")).ShouldBe(1);
        (await CountAsync(anonymous, $"/api/v1/shared/views/{token}/features?bbox={window}")).ShouldBe(1);

        // Two kilometres around a typed point beside the cave, written into the stored document.
        var placed = new FilterDocument
        {
            Scope =
            [
                new WorldScope("feature", new ConditionNode(
                    FeatureFilterFields.Position,
                    FilterOp.Within,
                    [new NumberValue(2000), new PointValue(19.1013, 68.1017)])),
            ],
        };
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var view = await db.MapViews.SingleAsync(v => v.Id == viewId);
            var config = JsonNode.Parse(view.Config)!.AsObject();
            config["filterDocument"] = JsonNode.Parse(FilterDocument.Serialize(placed));
            view.Config = config.ToJsonString();
            await db.SaveChangesAsync();
        }

        var shared = await GetJsonAsync(anonymous, $"/api/v1/shared/views/{token}");
        shared["overlays"]!["entrances"]!.GetValue<bool>().ShouldBeFalse();
        shared["overlays"]!["features"]!.GetValue<bool>().ShouldBeFalse();
        shared["reach"].ShouldBeNull();
        shared.ToJsonString().ShouldNotContain("19.1013");
        shared.ToJsonString().ShouldNotContain("68.1017");
        await ShouldBeNotFoundAsync(anonymous, $"/api/v1/shared/views/{token}/cave-entrances?bbox={window}&zoom=14");
        await ShouldBeNotFoundAsync(anonymous, $"/api/v1/shared/views/{token}/cave-entrances?bbox={window}&zoom=8");
        await ShouldBeNotFoundAsync(anonymous, $"/api/v1/shared/views/{token}/features?bbox={window}");
    }

    /// <summary>
    /// A source switched on for this installation's own accounts under terms that do not reach the
    /// public — the four Google entries and the non-commercial Sentinel-2 composite — is never a
    /// link's background, whatever the view was saved over and whether or not it is switched on.
    /// </summary>
    [Theory]
    [InlineData("Google Satellite", "google.com")]
    [InlineData("Google Road", "google.com")]
    [InlineData("Sentinel-2 cloudless", "eox.at")]
    public async Task A_restricted_terms_background_never_reaches_a_link(string layerName, string host)
    {
        long restrictedId;
        string defaultName;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var restricted = await db.MapLayers.SingleAsync(l => l.Name == layerName);
            restricted.Enabled = true;
            await db.SaveChangesAsync();
            restrictedId = restricted.Id;
            defaultName = await db.MapLayers
                .Where(l => l.IsDefault && l.IsBase && l.Enabled && l.ApiKeyName == null)
                .OrderBy(l => l.SortOrder)
                .Select(l => l.Name)
                .FirstAsync();
        }

        var token = await ShareAsync(await CreateViewAsync(entrances: false, features: false, 10.1, 60.1, baseLayerId: restrictedId));
        var response = await anonymous.GetAsync($"/api/v1/shared/views/{token}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);

        payload.ShouldNotContain(host, Case.Insensitive);
        JsonNode.Parse(payload)!["background"]!["name"]!.GetValue<string>().ShouldBe(defaultName);
    }

    private sealed record CaveRef(Guid Id, string Name, Guid EntranceId);

    private async Task<CaveRef> CreateCaveAsync(
        string name, double lon, double lat, string visibility, bool locationProtected, Guid? parentId = null)
    {
        var unique = Unique(name);
        var cave = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = unique,
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
            parentId,
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
        var entrancePayload = await entrance.Content.ReadAsStringAsync();
        entrance.StatusCode.ShouldBe(HttpStatusCode.Created, entrancePayload);
        return new CaveRef(caveId, unique, JsonNode.Parse(entrancePayload)!["id"]!.GetValue<Guid>());
    }

    private Task<Guid> CreatePointFeatureAsync(string name, double lon, double lat, string visibility, bool locationProtected) =>
        CreateFeatureAsync(name, new { type = "Point", coordinates = new[] { lon, lat } }, "sinkhole", visibility, locationProtected);

    private async Task<Guid> CreateFeatureAsync(
        string name, object geometry, string typeCode, string visibility, bool locationProtected, Guid? parentId = null)
    {
        long typeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            typeId = typeCode == "sinkhole"
                ? sinkholeTypeId
                : await db.FeatureTypes.Where(t => t.Code == typeCode).Select(t => t.Id).SingleAsync();
        }

        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = Unique(name),
            featureTypeId = typeId,
            geometry,
            visibility,
            locationProtected,
            parents = parentId is { } parent ? new[] { new { parentId = parent, isPrimary = true } } : null,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonNode.Parse(payload)!["id"]!.GetValue<Guid>();
    }

    /// <summary>A shared view saved with its camera over the place a test put its rows.</summary>
    private async Task<Guid> CreateViewAsync(
        bool entrances,
        bool features,
        double centerLon,
        double centerLat,
        double zoom = 12,
        long? baseLayerId = null,
        string? tagFilter = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/map-views/", new
        {
            name = Unique("Shared"),
            description = (string?)null,
            config = new
            {
                configVersion = 1,
                zoom,
                center = new[] { centerLon, centerLat },
                baseLayerId,
                entrancesVisible = entrances,
                surfaceFeaturesVisible = features,
                photosVisible = true,
                centerlinesVisible = true,
                tagFilter,
            },
            isHome = false,
            cavingGroupId = (Guid?)null,
            visibility = "private",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonNode.Parse(payload)!["id"]!.GetValue<Guid>();
    }

    private async Task<Guid> ShareAsync(Guid viewId)
    {
        var response = await owner.PostAsync($"/api/v1/map-views/{viewId}/share", null);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonNode.Parse(payload)!["shareToken"]!.GetValue<Guid>();
    }

    /// <summary>Gives every account exact view of a protected cave, as an installation can.</summary>
    private async Task GrantEveryAccountExactViewAsync(Guid caveId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var allUsers = await db.PermissionGroups
            .Where(g => g.Slug == SeededPermissionGroups.AllUsersSlug)
            .Select(g => g.Id)
            .SingleAsync();
        db.AccessEntries.Add(new AccessEntry
        {
            PermissionGroupId = allUsers,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Features,
            Actions = AccessAction.Read | AccessAction.ViewExactLocation,
            ScopeKind = AccessScopeKind.Subtree,
            ScopeFeatureId = caveId,
        });
        await db.SaveChangesAsync();
    }

    private async Task<long> AddLayerAsync(MapLayer layer)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        layer.SortOrder = 100_000;
        db.MapLayers.Add(layer);
        await db.SaveChangesAsync();
        return layer.Id;
    }

    private static async Task<JsonNode> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonNode.Parse(payload)!;
    }

    private static async Task ShouldBeNotFoundAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, url);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["code"]!.GetValue<string>().ShouldBe("map_view.not_found");
    }

    /// <summary>Entrances in an answer: a cluster contributes its count, a point one.</summary>
    private static async Task<int> CountAsync(HttpClient client, string url) =>
        (await GetJsonAsync(client, url))["features"]!.AsArray().Sum(f =>
            f!["properties"]!["cluster"]?.GetValue<bool>() == true
                ? f["properties"]!["count"]!.GetValue<int>()
                : 1);

    private static string Window(double west, double south, double east, double north) =>
        string.Join(',', new[] { west, south, east, north }.Select(v => v.ToString("R", Invariant)));

    private static string Unique(string name)
    {
        var candidate = $"{name} {Guid.NewGuid():N}";
        return candidate.Length <= 40 ? candidate : candidate[..40];
    }

    public ValueTask DisposeAsync()
    {
        owner.Dispose();
        member.Dispose();
        anonymous.Dispose();
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
