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
/// visibility level, and they place nothing exactly — a protected point is drawn snapped, a
/// protected outline not at all, whatever the installation grants every account. These tests
/// hold each half of that, the window-membership oracle against the link's own routes, and the
/// three ways a link stops drawing data: it is revoked, the overlay was off, or the installation
/// switched data on shared views off.
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

    public async Task InitializeAsync()
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
        point["properties"]!["approximate"]!.GetValue<bool>().ShouldBeFalse();
        point["properties"]!["caveName"]!.GetValue<string>().ShouldBe(publicCave.Name);
        point["properties"]!.AsObject().ContainsKey("id").ShouldBeFalse();
        point["properties"]!.AsObject().ContainsKey("caveId").ShouldBeFalse();

        // Clusters count the same one entrance, and nothing narrower — at its own position, since
        // nothing protects it: only a protected row is snapped for a link.
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
    /// A protected public cave is drawn snapped through a link — even where the installation gives
    /// every account exact view of it — and the link's window decides on the snapped point, so an
    /// edge moved over the true position inside one protection cell changes nothing.
    /// </summary>
    [Theory]
    [InlineData(6, 11.5, 61.2)]
    [InlineData(14, 12.5, 61.6)]
    public async Task A_protected_public_cave_is_snapped_through_a_link_and_its_window_edge_tells_nothing(
        int zoom, double nearLon, double nearLat)
    {
        var snappedLon = Math.Round(nearLon / Cell, MidpointRounding.AwayFromZero) * Cell;
        var snappedLat = Math.Round(nearLat / Cell, MidpointRounding.AwayFromZero) * Cell;
        var lon = snappedLon - (0.3 * Cell);
        var lat = snappedLat + (0.1 * Cell);
        var step = 0.05 * Cell;
        var cave = await CreateCaveAsync("Link Guarded", lon, lat, "public", locationProtected: true);
        await GrantEveryAccountExactViewAsync(cave.Id);

        var token = await ShareAsync(await CreateViewAsync(entrances: true, features: false, snappedLon, snappedLat, zoom: 10));
        string Url(string bbox) => $"/api/v1/shared/views/{token}/cave-entrances?bbox={bbox}&zoom={zoom}";

        var westInside = Window(lon - step, snappedLat - 0.4, snappedLon + 0.4, snappedLat + 0.4);
        var westOutside = Window(lon + step, snappedLat - 0.4, snappedLon + 0.4, snappedLat + 0.4);
        var eastInside = Window(snappedLon - 0.4, snappedLat - 0.4, lon + step, snappedLat + 0.4);
        var eastOutside = Window(snappedLon - 0.4, snappedLat - 0.4, lon - step, snappedLat + 0.4);

        (await CountAsync(anonymous, Url(westInside))).ShouldBe(1);
        (await CountAsync(anonymous, Url(westOutside))).ShouldBe(1, "the west edge crossed the true position and the answer moved");
        (await CountAsync(anonymous, Url(eastInside))).ShouldBe(0, "the east edge crossed the true position and the answer moved");
        (await CountAsync(anonymous, Url(eastOutside))).ShouldBe(0);

        // The control: an account placing it exactly — here every account, by the installation's
        // grant — sees the window change at the true position. The link never does.
        (await CountAsync(member, $"/api/v1/map/cave-entrances?bbox={westOutside}&zoom={zoom}")).ShouldBe(0);

        if (zoom >= 11)
        {
            var drawn = (await GetJsonAsync(anonymous, Url(westInside)))["features"]!.AsArray().ShouldHaveSingleItem()!;
            drawn["properties"]!["approximate"]!.GetValue<bool>().ShouldBeTrue();
            drawn["geometry"]!["coordinates"]![0]!.GetValue<double>().ShouldBe(snappedLon, 1e-9);
            drawn["geometry"]!["coordinates"]![1]!.GetValue<double>().ShouldBe(snappedLat, 1e-9);
        }
    }

    [Fact]
    public async Task A_protected_public_outline_is_not_drawn_and_a_protected_public_point_is_snapped()
    {
        double[][] ring = [[13.10, 62.10], [13.12, 62.10], [13.12, 62.12], [13.10, 62.12], [13.10, 62.10]];
        await CreateFeatureAsync("Link Guarded Area", new { type = "Polygon", coordinates = new[] { ring } },
            "karst_area", "public", locationProtected: true);
        var snappedLon = Math.Round(13.15 / Cell, MidpointRounding.AwayFromZero) * Cell;
        var snappedLat = Math.Round(62.15 / Cell, MidpointRounding.AwayFromZero) * Cell;
        await CreatePointFeatureAsync("Link Guarded Point", snappedLon - (0.2 * Cell), snappedLat + (0.2 * Cell), "public", locationProtected: true);

        var token = await ShareAsync(await CreateViewAsync(entrances: false, features: true, 13.2, 62.2, zoom: 10));
        var drawn = (await GetJsonAsync(anonymous, $"/api/v1/shared/views/{token}/features?bbox=13.0,62.0,13.4,62.4"))["features"]!.AsArray();

        var point = drawn.ShouldHaveSingleItem()!;
        point["properties"]!["approximate"]!.GetValue<bool>().ShouldBeTrue();
        point["geometry"]!["coordinates"]![0]!.GetValue<double>().ShouldBe(snappedLon, 1e-9);
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

    public Task DisposeAsync()
    {
        owner.Dispose();
        member.Dispose();
        anonymous.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
