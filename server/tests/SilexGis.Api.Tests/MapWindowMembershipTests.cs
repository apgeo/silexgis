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
using SilexGis.Domain.Filters;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A map window never decides membership on a position the caller may not see.
///
/// <para>
/// The entrance layer hands a caller who may read a protected cave, but not place it, a point
/// snapped to the protection grid. That is worth nothing if the window that selects the rows is
/// tested against the stored point: the caller moves one edge of the bbox in small steps and
/// watches the count (or the presence of the point) change at the exact moment the edge crosses
/// the true position — a bisection that recovers the coordinate far below the grid, without a
/// coordinate ever being returned. Every test here builds that probe: two windows whose moving
/// edge sits just either side of the true position, inside one protection cell, and asserts the
/// two answers are the same for the snapped caller.
/// </para>
/// <para>
/// The owner, who may place the entrance exactly, is the control: their answers DO change at the
/// true position. Without that half, a fix that simply moved everybody onto the snapped point —
/// or dropped protected rows from the window altogether — would pass.
/// </para>
/// <para>
/// Each test seeds its own cave at its own place, far from the others, because the cluster
/// branch counts every entrance in the window.
/// </para>
/// </summary>
public sealed class MapWindowMembershipTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    /// <summary>The shipped location-protection grid.</summary>
    private const double GridMeters = 5000d;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly SilexGisApiFactory factory;

    private readonly PostgresFixture postgres;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private HttpClient granted = null!;
    private Guid grantedId;
    private long caveTypeId;
    private long entranceTypeId;

    public MapWindowMembershipTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
        factory = new SilexGisApiFactory(postgres.ConnectionString);
    }

    private static double Cell => LocationProtection.CellDegrees(GridMeters);

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"win-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"win-view-{suffix}@t.local");
        grantedId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"win-grant-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"win-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"win-view-{suffix}@t.local");
        granted = await AuthHelper.BearerClientAsync(factory, $"win-grant-{suffix}@t.local");
    }

    /// <summary>
    /// Zoom 6 answers with clusters, zoom 14 with individual points: both branches decide which
    /// rows are in the window, and both used to decide it on the stored geometry.
    /// </summary>
    [Theory]
    [InlineData(6, 13.5, 47.2)]
    [InlineData(14, 16.5, 48.6)]
    public async Task A_window_edge_crossing_a_protected_entrance_inside_one_protection_cell_changes_nothing(
        int zoom, double nearLon, double nearLat)
    {
        // The true position sits three tenths of a cell west of the lattice point it snaps to, so
        // an edge can pass over it while staying on one side of the snapped point.
        var snappedLon = Math.Round(nearLon / Cell, MidpointRounding.AwayFromZero) * Cell;
        var snappedLat = Math.Round(nearLat / Cell, MidpointRounding.AwayFromZero) * Cell;
        var lon = snappedLon - (0.3 * Cell);
        var lat = snappedLat + (0.1 * Cell);
        var step = 0.05 * Cell;

        var caveId = await CreateCaveWithEntranceAsync(owner, "Window Guarded", lon, lat, locationProtected: true);
        await AssertSnappedForViewerAsync(caveId, lon);

        // Moving the WEST edge over the true position: the snapped point stays inside both.
        var westInside = Window(lon - step, snappedLat - 0.4, snappedLon + 0.4, snappedLat + 0.4);
        var westOutside = Window(lon + step, snappedLat - 0.4, snappedLon + 0.4, snappedLat + 0.4);

        var viewerWestInside = await CountAsync(viewer, westInside, zoom);
        var viewerWestOutside = await CountAsync(viewer, westOutside, zoom);
        viewerWestInside.ShouldBe(viewerWestOutside, "the west edge crossed the true position and the answer moved");
        viewerWestInside.ShouldBe(1, "the snapped point is inside both windows, so the entrance is counted in both");

        // Moving the EAST edge over the true position: the snapped point is outside both.
        var eastInside = Window(snappedLon - 0.4, snappedLat - 0.4, lon + step, snappedLat + 0.4);
        var eastOutside = Window(snappedLon - 0.4, snappedLat - 0.4, lon - step, snappedLat + 0.4);

        var viewerEastInside = await CountAsync(viewer, eastInside, zoom);
        var viewerEastOutside = await CountAsync(viewer, eastOutside, zoom);
        viewerEastInside.ShouldBe(viewerEastOutside, "the east edge crossed the true position and the answer moved");
        viewerEastInside.ShouldBe(0, "the snapped point is outside both windows");

        // The control: the owner places the entrance exactly, so for them the window is exact.
        (await CountAsync(owner, westInside, zoom)).ShouldBe(1);
        (await CountAsync(owner, westOutside, zoom)).ShouldBe(0);
        (await CountAsync(owner, eastInside, zoom)).ShouldBe(1);
        (await CountAsync(owner, eastOutside, zoom)).ShouldBe(0);
    }

    /// <summary>
    /// The points branch emits the same snapped point the window was decided on, so a caller who
    /// sees the entrance sees it inside the window they asked about.
    /// </summary>
    [Fact]
    public async Task A_snapped_entrance_returned_by_the_points_branch_lies_inside_the_window()
    {
        var snappedLon = Math.Round(19.5 / Cell, MidpointRounding.AwayFromZero) * Cell;
        var snappedLat = Math.Round(49.5 / Cell, MidpointRounding.AwayFromZero) * Cell;
        var lon = snappedLon + (0.35 * Cell);
        var lat = snappedLat - (0.2 * Cell);
        await CreateCaveWithEntranceAsync(owner, "Window Points", lon, lat, locationProtected: true);

        var west = snappedLon - 0.1;
        var east = snappedLon + (0.1 * Cell);
        var body = await EntrancesAsync(viewer, Window(west, snappedLat - 0.1, east, snappedLat + 0.1), 14);
        var features = body["features"]!.AsArray();
        features.Count.ShouldBe(1);
        var point = features[0]!;
        point["properties"]!["approximate"]!.GetValue<bool>().ShouldBeTrue();
        var x = point["geometry"]!["coordinates"]![0]!.GetValue<double>();
        x.ShouldBe(snappedLon, 1e-9);
        x.ShouldBeInRange(west, east);
    }

    /// <summary>
    /// The listing routes that take a bbox answer with a page and a total, and the total is as
    /// much a probe as a cluster count. Each is driven with the same pair of windows.
    /// </summary>
    [Theory]
    [InlineData("map-features", 21.5, 50.5)]
    [InlineData("features", 22.5, 51.5)]
    [InlineData("caves", 23.5, 52.5)]
    public async Task A_listing_bbox_edge_crossing_a_protected_point_changes_nothing(
        string route, double nearLon, double nearLat)
    {
        var snappedLon = Math.Round(nearLon / Cell, MidpointRounding.AwayFromZero) * Cell;
        var snappedLat = Math.Round(nearLat / Cell, MidpointRounding.AwayFromZero) * Cell;
        var lon = snappedLon - (0.3 * Cell);
        var lat = snappedLat + (0.1 * Cell);
        var step = 0.05 * Cell;

        if (route == "caves")
        {
            await CreateCaveWithEntranceAsync(owner, "Window Cave", lon, lat, locationProtected: true);
        }
        else
        {
            await CreateGenericAsync("Window Sinkhole", new { type = "Point", coordinates = new[] { lon, lat } });
        }

        var westInside = Window(lon - step, snappedLat - 0.4, snappedLon + 0.4, snappedLat + 0.4);
        var westOutside = Window(lon + step, snappedLat - 0.4, snappedLon + 0.4, snappedLat + 0.4);
        var eastInside = Window(snappedLon - 0.4, snappedLat - 0.4, lon + step, snappedLat + 0.4);
        var eastOutside = Window(snappedLon - 0.4, snappedLat - 0.4, lon - step, snappedLat + 0.4);

        (await ListedAsync(viewer, route, westInside)).ShouldBe(1);
        (await ListedAsync(viewer, route, westOutside)).ShouldBe(1, "the west edge crossed the true position and the answer moved");
        (await ListedAsync(viewer, route, eastInside)).ShouldBe(0, "the east edge crossed the true position and the answer moved");
        (await ListedAsync(viewer, route, eastOutside)).ShouldBe(0);

        (await ListedAsync(owner, route, westInside)).ShouldBe(1);
        (await ListedAsync(owner, route, westOutside)).ShouldBe(0);
        (await ListedAsync(owner, route, eastInside)).ShouldBe(1);
        (await ListedAsync(owner, route, eastOutside)).ShouldBe(0);
    }

    /// <summary>
    /// A protected outline has no shown position for a caller who may not place it — its geometry
    /// is withheld — so it is in no window for them. It used to be listed, with a null geometry,
    /// whenever its stored extent touched the window, which let a caller trace that extent edge
    /// by edge.
    /// </summary>
    [Theory]
    [InlineData("map-features", 24.10)]
    [InlineData("features", 25.10)]
    public async Task A_protected_outline_is_in_no_window_for_a_caller_who_may_not_place_it(string route, double west)
    {
        double[][] ring =
        [
            [west, 53.10], [west + 0.02, 53.10], [west + 0.02, 53.12], [west, 53.12], [west, 53.10],
        ];
        var id = await CreateGenericAsync(
            "Window Outline", new { type = "Polygon", coordinates = new[] { ring } }, "karst_area");
        (await viewer.GetAsync($"/api/v1/features/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var touching = Window(west + 0.015, 53.0, west + 0.4, 53.5);
        (await ListedAsync(viewer, route, touching)).ShouldBe(0);
        (await ListedAsync(owner, route, touching)).ShouldBe(1);
        (await ListedAsync(owner, route, Window(west + 0.025, 53.0, west + 0.4, 53.5))).ShouldBe(0);
    }

    /// <summary>
    /// Exact placement by a grant, not by ownership, is what makes a window exact for a caller:
    /// somebody holding ViewExactLocation on the protected cave sees the entrance come and go at
    /// its true position, exactly as the owner does, while the ordinary reader beside them does
    /// not. Without this half a rule that only recognised the owner arm would pass.
    /// </summary>
    [Theory]
    [InlineData(6, 13.5, 55.2)]
    [InlineData(14, 16.5, 56.6)]
    public async Task A_caller_granted_exact_view_who_is_not_the_owner_gets_the_exact_window(
        int zoom, double nearLon, double nearLat)
    {
        var snappedLon = Math.Round(nearLon / Cell, MidpointRounding.AwayFromZero) * Cell;
        var snappedLat = Math.Round(nearLat / Cell, MidpointRounding.AwayFromZero) * Cell;
        var lon = snappedLon - (0.3 * Cell);
        var lat = snappedLat + (0.1 * Cell);
        var step = 0.05 * Cell;

        var caveId = await CreateCaveWithEntranceAsync(owner, "Window Granted", lon, lat, locationProtected: true);
        var grant = await owner.PutAsJsonAsync($"/api/v1/objects/feature/{caveId}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = grantedId,
                    effect = "allow",
                    actions = "read, viewExactLocation",
                    scopeKind = "object",
                },
            },
        });
        grant.StatusCode.ShouldBe(HttpStatusCode.OK, await grant.Content.ReadAsStringAsync());

        var westInside = Window(lon - step, snappedLat - 0.4, snappedLon + 0.4, snappedLat + 0.4);
        var westOutside = Window(lon + step, snappedLat - 0.4, snappedLon + 0.4, snappedLat + 0.4);

        (await CountAsync(granted, westInside, zoom)).ShouldBe(1);
        (await CountAsync(granted, westOutside, zoom)).ShouldBe(0, "a granted caller places the entrance exactly");
        (await CountAsync(viewer, westInside, zoom)).ShouldBe(1);
        (await CountAsync(viewer, westOutside, zoom)).ShouldBe(1, "the ordinary reader is still decided on the snapped point");
    }

    /// <summary>
    /// <c>/features?bbox</c> with no <c>kind</c> asks the membership rule about every kind at
    /// once, which is a different statement from the one-kind form the other cases drive.
    /// </summary>
    [Fact]
    public async Task A_listing_with_no_kind_decides_a_protected_point_on_its_shown_position()
    {
        var snappedLon = Math.Round(26.5 / Cell, MidpointRounding.AwayFromZero) * Cell;
        var snappedLat = Math.Round(54.5 / Cell, MidpointRounding.AwayFromZero) * Cell;
        var lon = snappedLon - (0.3 * Cell);
        var lat = snappedLat + (0.1 * Cell);
        var step = 0.05 * Cell;
        await CreateGenericAsync("Window Anykind", new { type = "Point", coordinates = new[] { lon, lat } });

        var westInside = Window(lon - step, snappedLat - 0.4, snappedLon + 0.4, snappedLat + 0.4);
        var westOutside = Window(lon + step, snappedLat - 0.4, snappedLon + 0.4, snappedLat + 0.4);

        (await ListedAsync(viewer, "features-any-kind", westInside)).ShouldBe(1);
        (await ListedAsync(viewer, "features-any-kind", westOutside)).ShouldBe(1, "the west edge crossed the true position and the answer moved");
        (await ListedAsync(owner, "features-any-kind", westInside)).ShouldBe(1);
        (await ListedAsync(owner, "features-any-kind", westOutside)).ShouldBe(0);
    }

    /// <summary>
    /// A hidden row cannot take a slot of a capped answer. With the cap at one, a window holding
    /// a protected entrance whose shown point lies outside it and an ordinary entrance inside it
    /// answers with the ordinary entrance — even though the protected one is older and so sorts
    /// first by id, which is the order the cap is applied in.
    /// </summary>
    [Fact]
    public async Task A_hidden_entrance_cannot_take_the_only_slot_of_a_capped_answer()
    {
        using var capped = new SilexGisApiFactory(
            postgres.ConnectionString, new Dictionary<string, string?> { ["Map:MaxPoints"] = "1" });
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(capped, GlobalRoles.Editor, $"win-cap-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(capped, GlobalRoles.Viewer, $"win-cap-view-{suffix}@t.local");
        using var cappedOwner = await AuthHelper.BearerClientAsync(capped, $"win-cap-own-{suffix}@t.local");
        using var cappedViewer = await AuthHelper.BearerClientAsync(capped, $"win-cap-view-{suffix}@t.local");

        var snappedLon = Math.Round(17.5 / Cell, MidpointRounding.AwayFromZero) * Cell;
        var snappedLat = Math.Round(57.5 / Cell, MidpointRounding.AwayFromZero) * Cell;
        var hiddenLon = snappedLon - (0.3 * Cell);
        var hiddenLat = snappedLat + (0.1 * Cell);

        // Created first, so its id sorts first: the slot it would take is the only one there is.
        await CreateCaveWithEntranceAsync(cappedOwner, "Window Hidden", hiddenLon, hiddenLat, locationProtected: true);
        var visibleCave = await CreateCaveWithEntranceAsync(
            cappedOwner, "Window Visible", hiddenLon + (0.1 * Cell), hiddenLat, locationProtected: false);

        // The window holds both true positions and not the protected entrance's snapped point.
        var window = Window(hiddenLon - (0.05 * Cell), snappedLat - 0.1, snappedLon - (0.1 * Cell), snappedLat + 0.1);
        var viewerFeatures = (await EntrancesAsync(cappedViewer, window, 14))["features"]!.AsArray();
        viewerFeatures.Count.ShouldBe(1, "the cap still admits one row");
        viewerFeatures[0]!["properties"]!["caveId"]!.GetValue<Guid>().ShouldBe(visibleCave);

        // The control: the owner places both, and the older one does take the slot.
        var ownerFeatures = (await EntrancesAsync(cappedOwner, window, 14))["features"]!.AsArray();
        ownerFeatures.Count.ShouldBe(1);
        ownerFeatures[0]!["properties"]!["caveId"]!.GetValue<Guid>().ShouldNotBe(visibleCave);
    }

    /// <summary>
    /// A window the wrong way round, or off the globe, is refused by both layers that decide
    /// membership per row, on both branches of the entrance layer. Read instead, it answered
    /// unprotected rows and no protected ones on the points branch and nothing at all on the
    /// cluster branch — two answers to one question.
    /// </summary>
    [Theory]
    [InlineData("/api/v1/map/cave-entrances?zoom=6&bbox=26,45,25,46")]
    [InlineData("/api/v1/map/cave-entrances?zoom=14&bbox=26,45,25,46")]
    [InlineData("/api/v1/map/cave-entrances?zoom=14&bbox=25,46,26,45")]
    [InlineData("/api/v1/map/cave-entrances?zoom=6&bbox=-200,-10,10,10")]
    [InlineData("/api/v1/map/features?bbox=26,45,25,46")]
    [InlineData("/api/v1/map/features?bbox=0,-95,10,10")]
    public async Task A_window_the_wrong_way_round_or_off_the_globe_is_refused(string url)
    {
        var response = await viewer.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, payload);
        JsonNode.Parse(payload)!["code"]!.GetValue<string>().ShouldBe("map.invalid_bbox");
    }

    /// <summary>The feature list narrowed to generic features, spoken the one way it takes a kind.</summary>
    private static readonly string GenericKindOnly = FilterToken.Encode(new FilterDocument
    {
        Scope =
        [
            new WorldScope("feature", new ConditionNode(
                FeatureFilterFields.Kind, FilterOp.Equals, [new IdValue("generic")])),
        ],
    });

    private async Task<int> ListedAsync(HttpClient client, string route, string bbox)
    {
        var url = route switch
        {
            "map-features" => $"/api/v1/map/features?bbox={bbox}&kinds=generic",
            "features" => $"/api/v1/features?bbox={bbox}&filter={GenericKindOnly}",
            "features-any-kind" => $"/api/v1/features?bbox={bbox}",
            "caves" => $"/api/v1/caves?bbox={bbox}",
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };
        var response = await client.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        var body = JsonNode.Parse(payload)!;
        return route == "map-features"
            ? body["features"]!.AsArray().Count
            : body["totalItems"]!.GetValue<int>();
    }

    private async Task<Guid> CreateGenericAsync(string name, object geometry, string typeCode = "sinkhole")
    {
        long typeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            typeId = await db.FeatureTypes.Where(t => t.Code == typeCode).Select(t => t.Id).SingleAsync();
        }

        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = Unique(name),
            featureTypeId = typeId,
            geometry,
            visibility = "authenticated",
            locationProtected = true,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonNode.Parse(payload)!["id"]!.GetValue<Guid>();
    }

    private static string Window(double west, double south, double east, double north) =>
        string.Join(',', new[] { west, south, east, north }.Select(v => v.ToString("R", Invariant)));

    private async Task AssertSnappedForViewerAsync(Guid caveId, double trueLon)
    {
        (await viewer.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var entrances = (await viewer.GetFromJsonAsync<JsonArray>($"/api/v1/caves/{caveId}/entrances"))!;
        entrances.Count.ShouldBe(1);
        entrances[0]!["approximateLocation"]!.GetValue<bool>().ShouldBeTrue();
        entrances[0]!["geom"]!["coordinates"]![0]!.GetValue<double>().ShouldNotBe(trueLon);
    }

    private static async Task<JsonObject> EntrancesAsync(HttpClient client, string bbox, int zoom)
    {
        var response = await client.GetAsync($"/api/v1/map/cave-entrances?bbox={bbox}&zoom={zoom}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonNode.Parse(payload)!.AsObject();
    }

    /// <summary>Entrances in the window: a cluster contributes its count, a point one.</summary>
    private static async Task<int> CountAsync(HttpClient client, string bbox, int zoom)
    {
        var body = await EntrancesAsync(client, bbox, zoom);
        return body["features"]!.AsArray().Sum(f =>
            f!["properties"]!["cluster"]?.GetValue<bool>() == true
                ? f["properties"]!["count"]!.GetValue<int>()
                : 1);
    }

    private static string Unique(string name)
    {
        var candidate = $"{name} {Guid.NewGuid():N}";
        return candidate.Length <= 40 ? candidate : candidate[..40];
    }

    private async Task<Guid> CreateCaveWithEntranceAsync(
        HttpClient client, string name, double lon, double lat, bool locationProtected)
    {
        var cave = await client.PostAsJsonAsync("/api/v1/caves", new
        {
            name = Unique(name),
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await cave.Content.ReadAsStringAsync();
        cave.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var caveId = System.Text.Json.JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var entrance = await client.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            positionQuality = "Gps",
        });
        entrance.StatusCode.ShouldBe(HttpStatusCode.Created, await entrance.Content.ReadAsStringAsync());
        return caveId;
    }

    public ValueTask DisposeAsync()
    {
        owner.Dispose();
        viewer.Dispose();
        granted.Dispose();
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
