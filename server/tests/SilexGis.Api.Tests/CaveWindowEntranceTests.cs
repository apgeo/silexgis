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
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The cave registry cut to the map's window lists a cave when any entrance the reader is shown is
/// in the window — not only its main one — and decides that on the points the reader is shown.
/// </summary>
/// <remarks>
/// <para>
/// The map draws every entrance of a cave, so a cave whose main entrance lies outside the view and
/// whose second entrance is inside is on the screen; a list beside it that left the cave out would
/// say the map showed something it did not. The registry's windowed exports share the rule, so a
/// file asked for from the list holds the caves the list shows, and the list's total is a count of
/// the same set.
/// </para>
/// <para>
/// The protected case is a probe like the ones the window-membership rule exists to defeat: moving
/// one window edge across the TRUE position of a protected cave's second entrance must change
/// nothing for a reader who may not place it, while the owner, who may, is the control whose answer
/// does change there. A rule that tested the stored point would pass the owner's half and fail the
/// reader's.
/// </para>
/// <para>
/// Each test seeds its own caves in a patch of ground of its own, so the windows never see each
/// other's rows.
/// </para>
/// </remarks>
public sealed class CaveWindowEntranceTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    /// <summary>The shipped location-protection grid.</summary>
    private const double GridMeters = 5000d;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private Guid viewerId;
    private long caveTypeId;
    private long entranceTypeId;

    public CaveWindowEntranceTests(PostgresFixture postgres)
    {
        factory = new SilexGisApiFactory(postgres.ConnectionString);
    }

    private static double Cell => LocationProtection.CellDegrees(GridMeters);

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"cwe-own-{suffix}@t.local");
        viewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cwe-view-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"cwe-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"cwe-view-{suffix}@t.local");
    }

    [Fact]
    public async Task A_cave_whose_main_entrance_is_outside_and_second_entrance_inside_is_listed_and_exported()
    {
        // Main entrance well west of the window, second entrance inside it; a third cave entirely
        // outside is the control that the window still narrows.
        var (caveId, _) = await CreateCaveAsync(owner, "Window Two Mouths", 30.10, 40.50, locationProtected: false);
        await AddEntranceAsync(caveId, 30.62, 40.52);
        var (outsideId, _) = await CreateCaveAsync(owner, "Window Elsewhere", 30.05, 40.45, locationProtected: false);

        var window = Window(30.50, 40.40, 30.80, 40.70);
        foreach (var reader in new[] { owner, viewer })
        {
            var page = await ListAsync(reader, window);
            var ids = page["items"]!.AsArray().Select(i => i!["id"]!.GetValue<Guid>()).ToList();
            ids.ShouldContain(caveId);
            ids.ShouldNotContain(outsideId);
            // The total counts the set the page lists, so the two agree.
            page["totalItems"]!.GetValue<int>().ShouldBe(ids.Count);

            // The file carries names, not ids; the list's names are what it must hold.
            var names = page["items"]!.AsArray().Select(i => i!["name"]!.GetValue<string>()).ToList();
            (await ExportedNamesAsync(reader, window)).ShouldBe(names, ignoreOrder: true);
        }

        // Its own point alone still decides for a window around the main entrance.
        (await ListedIdsAsync(viewer, Window(30.00, 40.40, 30.20, 40.60))).ShouldContain(caveId);
        // And a window holding neither entrance lists nothing of it.
        (await ListedIdsAsync(viewer, Window(30.30, 40.40, 30.45, 40.60))).ShouldNotContain(caveId);
    }

    [Fact]
    public async Task An_entrance_the_reader_may_not_read_never_brings_its_cave_into_the_window()
    {
        var (caveId, _) = await CreateCaveAsync(owner, "Window Hidden Mouth", 31.10, 41.50, locationProtected: false);
        var hiddenEntrance = await AddEntranceAsync(caveId, 31.62, 41.52);
        await DenyReadAsync(viewerId, hiddenEntrance);

        // The fixture: the reader reads the cave, and the map does not draw that entrance for them
        // while it does for the owner — the entrance is one the reader is not shown.
        (await viewer.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var window = Window(31.50, 41.40, 31.80, 41.70);
        (await DrawnEntrancesAsync(viewer, window)).ShouldNotContain(hiddenEntrance);
        (await DrawnEntrancesAsync(owner, window)).ShouldContain(hiddenEntrance);

        (await ListedIdsAsync(owner, window)).ShouldContain(caveId, "the owner reads the entrance, so it counts for them");
        (await ListedIdsAsync(viewer, window)).ShouldNotContain(caveId);
        (await ExportedNamesAsync(viewer, window)).ShouldNotContain(await NameOfAsync(caveId));
    }

    /// <summary>
    /// A protected cave's second entrance, three tenths of a cell west of the lattice point it snaps
    /// to: an edge moved across its true position stays on one side of the snapped point, so the
    /// reader's answer must not move while the owner's does.
    /// </summary>
    [Fact]
    public async Task A_protected_second_entrance_decides_membership_by_its_snapped_point_never_its_true_one()
    {
        var snappedLon = Math.Round(32.5 / Cell, MidpointRounding.AwayFromZero) * Cell;
        var snappedLat = Math.Round(42.5 / Cell, MidpointRounding.AwayFromZero) * Cell;
        var lon = snappedLon - (0.3 * Cell);
        var lat = snappedLat + (0.1 * Cell);
        var step = 0.05 * Cell;

        // The main entrance is far outside every window below, so only the second can decide.
        var (caveId, _) = await CreateCaveAsync(owner, "Window Guarded Mouth", snappedLon - 2.0, snappedLat, locationProtected: true);
        await AddEntranceAsync(caveId, lon, lat);

        var westInside = Window(lon - step, snappedLat - 0.4, snappedLon + 0.4, snappedLat + 0.4);
        var westOutside = Window(lon + step, snappedLat - 0.4, snappedLon + 0.4, snappedLat + 0.4);
        var eastInside = Window(snappedLon - 0.4, snappedLat - 0.4, lon + step, snappedLat + 0.4);
        var eastOutside = Window(snappedLon - 0.4, snappedLat - 0.4, lon - step, snappedLat + 0.4);

        // The snapped point is inside both west windows and outside both east ones.
        (await ListedIdsAsync(viewer, westInside)).ShouldContain(caveId);
        (await ListedIdsAsync(viewer, westOutside)).ShouldContain(caveId, "the west edge crossed the true position and the answer moved");
        (await ListedIdsAsync(viewer, eastInside)).ShouldNotContain(caveId, "the east edge crossed the true position and the answer moved");
        (await ListedIdsAsync(viewer, eastOutside)).ShouldNotContain(caveId);
        var name = await NameOfAsync(caveId);
        (await ExportedNamesAsync(viewer, westOutside)).ShouldContain(name);
        (await ExportedNamesAsync(viewer, eastInside)).ShouldNotContain(name);

        // The control: the owner places the entrance exactly, so for them the window is exact.
        (await ListedIdsAsync(owner, westInside)).ShouldContain(caveId);
        (await ListedIdsAsync(owner, westOutside)).ShouldNotContain(caveId);
        (await ListedIdsAsync(owner, eastInside)).ShouldContain(caveId);
        (await ListedIdsAsync(owner, eastOutside)).ShouldNotContain(caveId);
    }

    private static async Task<JsonObject> ListAsync(HttpClient client, string bbox)
    {
        var response = await client.GetAsync($"/api/v1/caves?bbox={bbox}&pageSize=500");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonNode.Parse(payload)!.AsObject();
    }

    private static async Task<List<Guid>> ListedIdsAsync(HttpClient client, string bbox) =>
        [.. (await ListAsync(client, bbox))["items"]!.AsArray().Select(i => i!["id"]!.GetValue<Guid>())];

    /// <summary>The caves a windowed GeoJSON export holds, by name — the file carries no ids.</summary>
    private static async Task<List<string>> ExportedNamesAsync(HttpClient client, string bbox)
    {
        var response = await client.GetAsync($"/api/v1/export/caves?format=geojson&bbox={bbox}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        var features = JsonNode.Parse(payload)!["features"]!.AsArray();
        return [.. features.Select(f => f!["properties"]!["name"]!.GetValue<string>())];
    }

    private async Task<string> NameOfAsync(Guid caveId) =>
        (await owner.GetFromJsonAsync<JsonObject>($"/api/v1/caves/{caveId}"))!["name"]!.GetValue<string>();

    /// <summary>The entrances the map's points branch draws in a window, by id.</summary>
    private static async Task<List<Guid>> DrawnEntrancesAsync(HttpClient client, string bbox)
    {
        var response = await client.GetAsync($"/api/v1/map/cave-entrances?bbox={bbox}&zoom=14");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return [.. JsonNode.Parse(payload)!["features"]!.AsArray()
            .Select(f => f!["properties"]!["id"]!.GetValue<Guid>())];
    }

    private async Task DenyReadAsync(Guid subjectId, Guid featureId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = subjectId,
            Effect = AccessEffect.Deny,
            Domain = AccessDomain.Features,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.Object,
            ScopeFeatureId = featureId,
        });
        await db.SaveChangesAsync();
    }

    private static string Window(double west, double south, double east, double north) =>
        string.Join(',', new[] { west, south, east, north }.Select(v => v.ToString("R", Invariant)));

    private static string Unique(string name)
    {
        var candidate = $"{name} {Guid.NewGuid():N}";
        return candidate.Length <= 40 ? candidate : candidate[..40];
    }

    private async Task<(Guid CaveId, Guid EntranceId)> CreateCaveAsync(
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
        var caveId = JsonNode.Parse(payload)!["id"]!.GetValue<Guid>();
        var entranceId = await AddEntranceAsync(caveId, lon, lat, isMain: true);
        return (caveId, entranceId);
    }

    private async Task<Guid> AddEntranceAsync(Guid caveId, double lon, double lat, bool isMain = false)
    {
        var entrance = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            positionQuality = "Gps",
        });
        var payload = await entrance.Content.ReadAsStringAsync();
        entrance.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonNode.Parse(payload)!["id"]!.GetValue<Guid>();
    }

    public ValueTask DisposeAsync()
    {
        owner.Dispose();
        viewer.Dispose();
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
