// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The density grid: what it counts, what it normalises by, and the one thing it refuses.
///
/// <para>
/// The refusal is the reason this file is long. A count per cell is a position statement whose
/// precision is the cell, so a caller who may not see a cave's coordinate could otherwise recover
/// it by asking for a finer and finer grid and watching which cell the count moved to. The floor
/// under the cell size is what closes that, and it is asserted from both ends — that a cell at the
/// floor is served and counts the protected cave, and that a cell below it is refused rather than
/// widened.
/// </para>
/// <para>
/// The fixtures sit off the coast of Sardinia, well away from every other class in this suite,
/// because the grid counts every entrance in the window and a neighbour's fixture would be counted
/// too.
/// </para>
/// </summary>
public sealed class MapDensityTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    /// <summary>This installation's shipped location-protection grid, and therefore the floor.</summary>
    private const double GridMeters = 5000d;

    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private long caveTypeId;
    private long entranceTypeId;
    private long karstAreaTypeId;

    public MapDensityTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"dens-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"dens-view-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
            karstAreaTypeId = await db.FeatureTypes
                .Where(t => t.Code == "karst_area").Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"dens-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"dens-view-{suffix}@t.local");
    }

    [Fact]
    public async Task The_grid_counts_the_entrances_in_the_window_into_the_cells_they_fall_in()
    {
        // Two entrances a few tens of metres apart, and a third some twenty kilometres away: at a
        // five-kilometre cell that is two cells holding two and one.
        await CreateCaveWithEntranceAsync(owner, "Dens A", 8.2000, 40.3000);
        await CreateCaveWithEntranceAsync(owner, "Dens B", 8.2005, 40.3005);
        await CreateCaveWithEntranceAsync(owner, "Dens C", 8.4000, 40.4500);

        var body = await GridAsync(owner, "8.0,40.0,8.5,40.5", GridMeters);

        body["featureCount"]!.GetValue<int>().ShouldBe(3);
        body["cellMetres"]!.GetValue<double>().ShouldBe(GridMeters);
        body["minimumCellMetres"]!.GetValue<double>().ShouldBe(GridMeters);
        body["protectionGridMetres"]!.GetValue<double>().ShouldBe(GridMeters);
        body["studyAreaKm2"]!.ShouldBeNull();

        var occupied = Occupied(body);
        occupied.Count.ShouldBe(2);
        occupied.Select(c => c["count"]!.GetValue<int>()).Order().ShouldBe([1, 2]);

        // Empty cells are part of the answer — a surface with holes cannot be told from a window
        // that was never asked about.
        body["cellCount"]!.GetValue<int>().ShouldBeGreaterThan(occupied.Count);

        // Each cell is one cell wide, and its ground area is measured rather than assumed to be
        // the cell size squared: at 40 degrees north a five-kilometre-tall cell is about
        // 3.8 km wide, so the square-cell assumption would be out by a fifth.
        var cellDegrees = LocationProtection.CellDegrees(GridMeters);
        var first = occupied[0];
        (first["east"]!.GetValue<double>() - first["west"]!.GetValue<double>())
            .ShouldBe(cellDegrees, 1e-9);
        first["areaKm2"]!.GetValue<double>().ShouldBeInRange(14d, 21d);

        // And the density is the count over that measured area, not over a nominal 25 km².
        var two = occupied.Single(c => c["count"]!.GetValue<int>() == 2);
        two["densityPerKm2"]!.GetValue<double>()
            .ShouldBe(2d / two["areaKm2"]!.GetValue<double>(), 1e-9);
        two["studyAreaFraction"]!.ShouldBeNull();
    }

    [Fact]
    public async Task A_protected_cave_cannot_be_located_by_asking_for_a_finer_cell()
    {
        const double lon = 9.1000;
        const double lat = 41.1000;
        const string bbox = "9.0,41.0,9.2,41.2";

        var caveId = await CreateCaveWithEntranceAsync(
            owner, "Dens Guarded", lon, lat, locationProtected: true);

        // The unreadable state, built explicitly: a Viewer with no grant anywhere. They can read
        // the cave — so what follows is the placement rule and not a visibility one — and the
        // entrance they are shown is snapped, not exact.
        (await viewer.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var entrances = (await viewer.GetFromJsonAsync<JsonArray>($"/api/v1/caves/{caveId}/entrances"))!;
        entrances.Count.ShouldBe(1);
        entrances[0]!["approximateLocation"]!.GetValue<bool>().ShouldBeTrue();
        entrances[0]!["geom"]!["coordinates"]![0]!.GetValue<double>().ShouldNotBe(lon);

        // Shrinking the cell is refused, and refused with a code a client can branch on rather
        // than with the one every failed rule in the request shares.
        foreach (var tooFine in new[] { 1d, 250d, 4_999.999d })
        {
            var refused = await viewer.GetAsync(
                $"/api/v1/map/density?bbox={bbox}&cellMetres={tooFine.ToString(Invariant)}");
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await refused.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>()
                .ShouldBe("density.cell_below_protection_grid");
        }

        // The positive half, for the same caller: at the floor the cave is counted. A refusal
        // that had simply hidden the cave would pass every assertion above and fail here.
        var atFloor = await GridAsync(viewer, bbox, GridMeters);
        atFloor["featureCount"]!.GetValue<int>().ShouldBe(1);
        var viewerCells = Occupied(atFloor);
        viewerCells.Count.ShouldBe(1);

        // And it is the same cell the cave's owner is shown, who sees the entrance exactly. That
        // is the whole property: at every permitted cell size the two answers are identical, so
        // the surface says nothing about who may place what.
        var ownerCells = Occupied(await GridAsync(owner, bbox, GridMeters));
        ownerCells.Count.ShouldBe(1);
        ownerCells[0]["west"]!.GetValue<double>()
            .ShouldBe(viewerCells[0]["west"]!.GetValue<double>(), 1e-12);
        ownerCells[0]["south"]!.GetValue<double>()
            .ShouldBe(viewerCells[0]["south"]!.GetValue<double>(), 1e-12);

        // A coarser cell is served — the floor is a floor, not a fixed size.
        (await GridAsync(viewer, bbox, GridMeters * 4)) ["featureCount"]!.GetValue<int>().ShouldBe(1);
    }

    [Fact]
    public async Task A_study_area_is_the_denominator_and_only_for_a_caller_who_may_place_it()
    {
        const string bbox = "7.0,39.0,7.5,39.5";
        await CreateCaveWithEntranceAsync(owner, "Dens Inside", 7.2000, 39.2000);

        var openAreaId = await CreateKarstAreaAsync("Dens Open Area", 7.10, 39.10, 7.30, 39.30);
        var guardedAreaId = await CreateKarstAreaAsync(
            "Dens Guarded Area", 7.10, 39.10, 7.30, 39.30, locationProtected: true);

        var normalised = await GridAsync(owner, bbox, GridMeters, openAreaId);
        normalised["studyAreaId"]!.GetValue<Guid>().ShouldBe(openAreaId);

        // The outline is 0.2 by 0.2 degrees at 39 north: about 17.3 by 22.2 km, so roughly
        // 385 km². The tolerance is wide because it is checking the order of magnitude — that the
        // denominator is ground and not degrees — not the spheroid's fourth digit.
        normalised["studyAreaKm2"]!.GetValue<double>().ShouldBeInRange(300d, 450d);

        // Only cells that touch the outline are returned, and each reports how much of it is
        // inside, so a cell half outside the karst is divided by the half that is karst.
        var cells = normalised["cells"]!.AsArray();
        cells.Count.ShouldBeGreaterThan(0);
        foreach (var cell in cells)
        {
            var fraction = cell!["studyAreaFraction"]!.GetValue<double>();
            fraction.ShouldBeGreaterThan(0d);
            fraction.ShouldBeLessThanOrEqualTo(1.000001d);
        }

        var counted = Occupied(normalised).Single();
        counted["densityPerKm2"]!.GetValue<double>().ShouldBeGreaterThan(
            counted["count"]!.GetValue<int>() / counted["areaKm2"]!.GetValue<double>() - 1e-9);

        // A Viewer may use the open outline as a denominator — the positive half — and is told the
        // protected one does not exist, in the same words as an outline that never did. Dividing
        // cell by cell would otherwise draw its edges for somebody who may not be shown where it is.
        (await GridAsync(viewer, bbox, GridMeters, openAreaId))["studyAreaId"]!
            .GetValue<Guid>().ShouldBe(openAreaId);
        (await GridAsync(owner, bbox, GridMeters, guardedAreaId))["studyAreaId"]!
            .GetValue<Guid>().ShouldBe(guardedAreaId);

        await ExpectAsync(viewer, $"?bbox={bbox}&areaId={guardedAreaId}",
            HttpStatusCode.NotFound, "feature.not_found");
        await ExpectAsync(viewer, $"?bbox={bbox}&areaId={Guid.NewGuid()}",
            HttpStatusCode.NotFound, "feature.not_found");
    }

    [Fact]
    public async Task The_route_refuses_a_window_it_cannot_answer_and_a_denominator_with_no_area()
    {
        var caveId = await CreateCaveWithEntranceAsync(owner, "Dens Point Area", 8.6, 40.6);

        await ExpectAsync(owner, "?bbox=not-a-box", HttpStatusCode.BadRequest, "map.invalid_bbox");
        await ExpectAsync(owner, "?bbox=8.0,40.0", HttpStatusCode.BadRequest, "map.invalid_bbox");

        // The whole world at the finest permitted cell is millions of cells; it is refused from
        // the arithmetic, before a row is read.
        await ExpectAsync(owner, "?bbox=-180,-85,180,85&cellMetres=5000",
            HttpStatusCode.BadRequest, "density.too_many_cells");

        // A cave is a point. A point encloses no ground, so it cannot be a denominator.
        await ExpectAsync(owner, $"?bbox=8.5,40.5,8.7,40.7&areaId={caveId}",
            HttpStatusCode.BadRequest, "density.study_area_not_an_outline");

        // Arithmetic that is wrong at any setting stays with the validator.
        await ExpectAsync(owner, "?bbox=8.5,40.5,8.7,40.7&cellMetres=0",
            HttpStatusCode.BadRequest, "validation.failed");
        await ExpectAsync(owner, "?bbox=8.5,40.5,8.7,40.7&cellMetres=99999999",
            HttpStatusCode.BadRequest, "validation.failed");

        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/map/density?bbox=8.5,40.5,8.7,40.7"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_cave_the_caller_cannot_read_is_in_no_cell_at_all()
    {
        const string bbox = "10.0,42.0,10.2,42.2";

        // Private to its owner: a Viewer holds no grant on it and is in no group that reaches it.
        await CreateCaveWithEntranceAsync(owner, "Dens Private", 10.1, 42.1, visibility: "private");
        await CreateCaveWithEntranceAsync(owner, "Dens Shared", 10.11, 42.11);

        // The positive half in the same test: both are counted for the owner, so the difference
        // below is the visibility filter and not a query that finds nothing here.
        (await GridAsync(owner, bbox, GridMeters))["featureCount"]!.GetValue<int>().ShouldBe(2);
        (await GridAsync(viewer, bbox, GridMeters))["featureCount"]!.GetValue<int>().ShouldBe(1);
    }

    [Fact]
    public async Task A_protected_cave_cannot_be_located_by_shrinking_the_window_either()
    {
        // The floor stops the caller shrinking the *cell*. It says nothing at all about the
        // *window*, and a count taken over the raw window rather than over the enumerated cells
        // would let the same caller bisect a coordinate with a bbox: ask about a sliver of one
        // cell, watch whether the count is one or nought, and move the sliver. So the count window
        // is the grid's own extent, and a cell answers the same number wherever the caller looked
        // from.
        const double lon = 9.6123;
        const double lat = 41.6234;

        await CreateCaveWithEntranceAsync(owner, "Dens Bisect", lon, lat, locationProtected: true);

        var wide = Occupied(await GridAsync(viewer, "9.4,41.4,9.8,41.8", GridMeters)).Single();
        var wideCount = wide["count"]!.GetValue<int>();
        wideCount.ShouldBe(1);

        // Four windows, each far narrower than a cell, walked across the cell the cave is in. If
        // the count tracked the window the cave would appear in one of them and not the others,
        // and its position would follow from which.
        var cellDegrees = LocationProtection.CellDegrees(GridMeters);
        var centreX = wide["west"]!.GetValue<double>() + (cellDegrees / 2d);
        var centreY = wide["south"]!.GetValue<double>() + (cellDegrees / 2d);

        foreach (var offset in new[] { -0.35d, -0.1d, 0.1d, 0.35d })
        {
            var x = centreX + (offset * cellDegrees);
            var y = centreY + (offset * cellDegrees);
            var slivered = await GridAsync(
                viewer,
                string.Create(
                    Invariant, $"{x - 0.0001:0.######},{y - 0.0001:0.######},{x + 0.0001:0.######},{y + 0.0001:0.######}"),
                GridMeters);

            var cell = Occupied(slivered).SingleOrDefault();
            cell.ShouldNotBeNull(
                $"a sliver at offset {offset} lost the cave, which is the count following the window");
            cell["count"]!.GetValue<int>().ShouldBe(wideCount);

            // And it is the same cell, not a cell the window carved out for itself.
            cell["west"]!.GetValue<double>().ShouldBe(wide["west"]!.GetValue<double>(), 1e-12);
            cell["south"]!.GetValue<double>().ShouldBe(wide["south"]!.GetValue<double>(), 1e-12);
        }
    }

    [Fact]
    public async Task A_study_area_decides_which_caves_are_counted_and_not_only_what_to_divide_by()
    {
        const string bbox = "6.0,38.0,6.6,38.6";

        // One entrance inside the outline, one outside it but in the same cell as the outline's
        // corner. Counting both against the sliver of that cell which is inside the karst would
        // report a density many times the true one — the numerator and the denominator have to
        // describe the same ground.
        var areaId = await CreateKarstAreaAsync("Dens Clip Area", 6.10, 38.10, 6.30, 38.30);
        await CreateCaveWithEntranceAsync(owner, "Dens In Karst", 6.2000, 38.2000);
        await CreateCaveWithEntranceAsync(owner, "Dens Out Of Karst", 6.3100, 38.3100);

        // Without the outline both are in the window, so the difference below is the outline and
        // not a fixture that never landed.
        (await GridAsync(owner, bbox, GridMeters))["featureCount"]!.GetValue<int>().ShouldBe(2);

        var clipped = await GridAsync(owner, bbox, GridMeters, areaId);
        clipped["featureCount"]!.GetValue<int>().ShouldBe(1);

        // And no cell reports a density wilder than the count it holds over the whole cell scaled
        // by how much of that cell is karst — which is what an uncounted outsider would produce.
        foreach (var cell in Occupied(clipped))
        {
            var fraction = cell["studyAreaFraction"]!.GetValue<double>();
            var overWholeCell = cell["count"]!.GetValue<int>() / cell["areaKm2"]!.GetValue<double>();
            cell["densityPerKm2"]!.GetValue<double>()
                .ShouldBe(overWholeCell / fraction, overWholeCell * 0.01);
        }
    }

    [Fact]
    public async Task A_window_outside_the_world_is_refused_before_anything_is_sized_from_it()
    {
        // Four finite numbers parse. They are still not a window, and everything downstream
        // measures work per unit of it: at a five-kilometre cell this box is more cells than a
        // 64-bit count holds, so a guard doing whole-number arithmetic wraps to a negative number,
        // decides the request is small, and runs it.
        await ExpectAsync(owner, "?bbox=-70000000,-70000000,70000000,70000000&cellMetres=5000",
            HttpStatusCode.BadRequest, "map.invalid_bbox");

        // The ordinary ways of being outside the world, each refused with the same code.
        await ExpectAsync(owner, "?bbox=-181,40,-179,41", HttpStatusCode.BadRequest, "map.invalid_bbox");
        await ExpectAsync(owner, "?bbox=8,-91,9,-89", HttpStatusCode.BadRequest, "map.invalid_bbox");
        await ExpectAsync(owner, "?bbox=9,41,8,40", HttpStatusCode.BadRequest, "map.invalid_bbox");

        // The positive half: a window that is inside the world is still served, so the check is a
        // check and not a wall.
        (await GridAsync(owner, "8.0,40.0,8.2,40.2", GridMeters))
            ["cellCount"]!.GetValue<int>().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task The_kernel_surface_spreads_a_count_into_the_cells_around_it()
    {
        const string bbox = "5.0,37.0,5.4,37.4";
        await CreateCaveWithEntranceAsync(owner, "Dens Kernel", 5.2000, 37.2000);

        var body = await GridAsync(owner, bbox, GridMeters);

        body["bandwidthMetres"]!.GetValue<double>().ShouldBe(GridMeters * 2d);
        body["minimumBandwidthMetres"]!.GetValue<double>().ShouldBe(GridMeters);

        var cells = body["cells"]!.AsArray();
        var empty = cells.Where(c => c!["count"]!.GetValue<int>() == 0).ToList();

        // The discrete count and the smoothed surface answer different questions, and a cell with
        // nothing in it is where they visibly differ: nought caves, and a clearly non-zero
        // neighbourhood. A "kernel" that had copied the counts across would be nought here.
        empty.Any(c => c!["kernelDensityPerKm2"]!.GetValue<double>() > 0d).ShouldBeTrue();

        // The peak is at the cell that holds the cave.
        var peak = cells.Max(c => c!["kernelDensityPerKm2"]!.GetValue<double>());
        var occupied = Occupied(body).Single();
        occupied["kernelDensityPerKm2"]!.GetValue<double>().ShouldBe(peak, peak * 1e-9);

        // A kernel narrower than the cell is refused: smoothing cannot recover what the binning
        // removed, and a narrow one would draw each cell as a peak that looks like a cave.
        await ExpectAsync(owner, $"?bbox={bbox}&cellMetres=5000&bandwidthMetres=100",
            HttpStatusCode.BadRequest, "density.bandwidth_below_cell");
    }

    /// <summary>A name no other run of this class can collide with, inside the length the API takes.</summary>
    private static string Unique(string name)
    {
        var candidate = $"{name} {Guid.NewGuid():N}";
        return candidate.Length <= 40 ? candidate : candidate[..40];
    }

    private static readonly System.Globalization.CultureInfo Invariant =
        System.Globalization.CultureInfo.InvariantCulture;

    private static List<JsonNode> Occupied(JsonObject body) =>
        [.. body["cells"]!.AsArray()
            .Select(c => c!)
            .Where(c => c["count"]!.GetValue<int>() > 0)];

    private async Task<JsonObject> GridAsync(
        HttpClient client, string bbox, double cellMetres, Guid? areaId = null)
    {
        var url = $"/api/v1/map/density?bbox={bbox}&cellMetres={cellMetres.ToString(Invariant)}"
            + (areaId is null ? string.Empty : $"&areaId={areaId}");
        var response = await client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    private async Task ExpectAsync(
        HttpClient client, string query, HttpStatusCode status, string code)
    {
        var response = await client.GetAsync($"/api/v1/map/density{query}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, payload);
        System.Text.Json.JsonDocument.Parse(payload).RootElement
            .GetProperty("code").GetString().ShouldBe(code);
    }

    private async Task<Guid> CreateCaveWithEntranceAsync(
        HttpClient client,
        string name,
        double lon,
        double lat,
        bool locationProtected = false,
        string visibility = "authenticated")
    {
        var cave = await client.PostAsJsonAsync("/api/v1/caves", new
        {
            name = Unique(name),
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await cave.Content.ReadAsStringAsync();
        cave.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var caveId = System.Text.Json.JsonDocument.Parse(payload).RootElement
            .GetProperty("id").GetGuid();

        var entrance = await client.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            positionQuality = "Gps",
        });
        entrance.StatusCode.ShouldBe(
            HttpStatusCode.Created, await entrance.Content.ReadAsStringAsync());

        return caveId;
    }

    private async Task<Guid> CreateKarstAreaAsync(
        string name, double west, double south, double east, double north,
        bool locationProtected = false)
    {
        var ring = new[]
        {
            new[] { west, south }, new[] { east, south }, new[] { east, north },
            new[] { west, north }, new[] { west, south },
        };
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = Unique(name),
            featureTypeId = karstAreaTypeId,
            geometry = new { type = "Polygon", coordinates = new[] { ring } },
            visibility = "authenticated",
            locationProtected,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return System.Text.Json.JsonDocument.Parse(payload).RootElement
            .GetProperty("id").GetGuid();
    }

    public Task DisposeAsync()
    {
        owner.Dispose();
        viewer.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
