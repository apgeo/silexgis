// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The point-pattern statistics end to end: what they measure, what they say they measured it over,
/// and who is in the set.
///
/// <para>
/// Both ends of each statistic are asserted. A nearest-neighbour index checked only against a
/// clustered fixture would also be satisfied by a handler that returned a small constant, so a
/// spread-out fixture is measured in the same test and has to come out on the other side of one.
/// </para>
/// <para>
/// The fixtures sit over the Black Sea coast, away from every other class in this suite, because
/// the statistics read every entrance in the window and a neighbour's fixture would be read too.
/// Each test uses a window of its own for the same reason.
/// </para>
/// </summary>
public sealed class MapPointPatternTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private long caveTypeId;
    private long entranceTypeId;
    private long karstAreaTypeId;

    public MapPointPatternTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"pp-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"pp-view-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
            karstAreaTypeId = await db.FeatureTypes
                .Where(t => t.Code == "karst_area").Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"pp-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"pp-view-{suffix}@t.local");
    }

    [Fact]
    public async Task A_clustered_window_indexes_below_one_and_a_spread_one_does_not()
    {
        // Twelve entrances in two tight knots, in a window some forty kilometres across.
        const string clusteredBbox = "27.0,43.0,27.5,43.4";
        for (var i = 0; i < 6; i++)
        {
            await CreateCaveWithEntranceAsync(owner, "Pp Knot A", 27.100 + (0.002 * i), 43.100 + (0.001 * i));
            await CreateCaveWithEntranceAsync(owner, "Pp Knot B", 27.400 + (0.002 * i), 43.330 + (0.001 * i));
        }

        var clustered = await PatternAsync(owner, clusteredBbox, "&simulations=19&steps=6&seed=3");

        clustered["featureCount"]!.GetValue<int>().ShouldBe(12);
        clustered["studyAreaKm2"]!.GetValue<double>().ShouldBeGreaterThan(1_000d);
        clustered["studyAreaId"]!.ShouldBeNull();

        var clarkEvans = clustered["clarkEvans"]!;
        clarkEvans["index"]!.GetValue<double>().ShouldBeLessThan(0.5);
        clarkEvans["zScore"]!.GetValue<double>().ShouldBeLessThan(-3d);
        clarkEvans["pValue"]!.GetValue<double>().ShouldBeLessThan(0.01);
        clarkEvans["meanNearestNeighbourM"]!.GetValue<double>()
            .ShouldBeLessThan(clarkEvans["expectedMeanM"]!.GetValue<double>());

        // The other end, in its own window: the same twelve caves spread over a lattice are not
        // clustered, and the index has to say so rather than always answering "clustered".
        const string spreadBbox = "28.0,43.0,28.5,43.4";
        for (var i = 0; i < 12; i++)
        {
            await CreateCaveWithEntranceAsync(
                owner, "Pp Spread", 28.08 + (0.11 * (i % 4)), 43.08 + (0.11 * (i / 4)));
        }

        var spread = await PatternAsync(owner, spreadBbox, "&simulations=19&steps=6&seed=3");

        spread["featureCount"]!.GetValue<int>().ShouldBe(12);
        spread["clarkEvans"]!["index"]!.GetValue<double>().ShouldBeGreaterThan(1.1);
    }

    [Fact]
    public async Task The_envelope_states_its_seed_and_reproduces_from_it()
    {
        const string bbox = "29.0,43.0,29.4,43.3";
        for (var i = 0; i < 8; i++)
        {
            await CreateCaveWithEntranceAsync(owner, "Pp Band", 29.05 + (0.04 * i), 43.05 + (0.03 * i));
        }

        var first = await PatternAsync(owner, bbox, "&simulations=19&steps=5&seed=11");
        var again = await PatternAsync(owner, bbox, "&simulations=19&steps=5&seed=11");
        var other = await PatternAsync(owner, bbox, "&simulations=19&steps=5&seed=12");

        var ripley = first["ripley"]!;
        ripley["seed"]!.GetValue<int>().ShouldBe(11);
        ripley["simulations"]!.GetValue<int>().ShouldBe(19);
        ripley["steps"]!.AsArray().Count.ShouldBe(5);
        ripley["maxRadiusM"]!.GetValue<double>().ShouldBeGreaterThan(0d);

        // Radii climb, the band brackets the curve's own comparison line, and the whole answer is
        // the same answer next time it is asked for.
        for (var i = 0; i < 5; i++)
        {
            var step = ripley["steps"]![i]!;
            var repeat = again["ripley"]!["steps"]![i]!;
            step["radiusM"]!.GetValue<double>().ShouldBeGreaterThan(0d);
            step["lowerL"]!.GetValue<double>().ShouldBeLessThanOrEqualTo(step["upperL"]!.GetValue<double>());
            repeat["lowerL"]!.GetValue<double>().ShouldBe(step["lowerL"]!.GetValue<double>());
            repeat["upperL"]!.GetValue<double>().ShouldBe(step["upperL"]!.GetValue<double>());
            repeat["observedL"]!.GetValue<double>().ShouldBe(step["observedL"]!.GetValue<double>());
        }

        // A different seed is a different draw — otherwise the seed is a decoration and the band
        // could as well have been hard-coded.
        Enumerable.Range(0, 5)
            .Any(i => Math.Abs(other["ripley"]!["steps"]![i]!["upperL"]!.GetValue<double>()
                - ripley["steps"]![i]!["upperL"]!.GetValue<double>()) > 1e-9)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task An_entrance_the_caller_cannot_place_is_in_neither_statistic()
    {
        const string bbox = "26.0,43.0,26.4,43.3";

        for (var i = 0; i < 6; i++)
        {
            await CreateCaveWithEntranceAsync(owner, "Pp Open", 26.05 + (0.05 * i), 43.05 + (0.04 * i));
        }

        // The unplaceable state, built explicitly: a Viewer holding no grant anywhere, and a cave
        // that is protected rather than hidden — they can read it, they cannot place it.
        var protectedId = await CreateCaveWithEntranceAsync(
            owner, "Pp Guarded", 26.20, 43.15, locationProtected: true);
        (await viewer.GetAsync($"/api/v1/caves/{protectedId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The positive half in the same test: for the owner, who may place every one of them, the
        // protected cave is in the set. A handler that simply found nothing would pass the negative
        // assertion below and fail this one.
        (await PatternAsync(owner, bbox, "&simulations=9&steps=4"))["featureCount"]!
            .GetValue<int>().ShouldBe(7);

        // And for the Viewer it is absent rather than approximated: a spacing measured from a
        // coordinate rounded onto the protection grid would measure the rounding.
        var forViewer = await PatternAsync(viewer, bbox, "&simulations=9&steps=4");
        forViewer["featureCount"]!.GetValue<int>().ShouldBe(6);
        forViewer["clarkEvans"]!.ShouldNotBeNull();
    }

    [Fact]
    public async Task Too_few_placeable_entrances_answer_with_the_count_and_no_numbers()
    {
        const string bbox = "24.0,43.0,24.3,43.2";
        await CreateCaveWithEntranceAsync(owner, "Pp Lonely", 24.1, 43.1);
        await CreateCaveWithEntranceAsync(owner, "Pp Lonelier", 24.2, 43.15);

        var body = await PatternAsync(owner, bbox, string.Empty);

        // Not an error: two caves is what there was, and the answer says so and stops.
        body["featureCount"]!.GetValue<int>().ShouldBe(2);
        body["minimumFeatureCount"]!.GetValue<int>().ShouldBeGreaterThan(2);
        body["studyAreaKm2"]!.GetValue<double>().ShouldBeGreaterThan(0d);
        body["clarkEvans"]!.ShouldBeNull();
        body["ripley"]!.ShouldBeNull();
    }

    [Fact]
    public async Task A_named_outline_becomes_the_window_and_the_set()
    {
        const string bbox = "23.0,43.0,23.6,43.4";
        for (var i = 0; i < 6; i++)
        {
            await CreateCaveWithEntranceAsync(owner, "Pp In", 23.10 + (0.02 * i), 43.10 + (0.01 * i));
        }

        await CreateCaveWithEntranceAsync(owner, "Pp Out", 23.50, 43.35);

        var areaId = await CreateKarstAreaAsync("Pp Outline", 23.05, 43.05, 23.30, 43.25);

        var whole = await PatternAsync(owner, bbox, "&simulations=9&steps=4");
        var inside = await PatternAsync(owner, bbox, $"&simulations=9&steps=4&areaId={areaId}");

        whole["featureCount"]!.GetValue<int>().ShouldBe(7);
        inside["featureCount"]!.GetValue<int>().ShouldBe(6);
        inside["studyAreaId"]!.GetValue<Guid>().ShouldBe(areaId);

        // The outline is the denominator too, so the ground shrinks with the set — which is the
        // whole point of naming one.
        inside["studyAreaKm2"]!.GetValue<double>()
            .ShouldBeLessThan(whole["studyAreaKm2"]!.GetValue<double>());
    }

    [Fact]
    public async Task The_route_is_closed_to_a_caller_with_no_session_and_refuses_what_it_cannot_answer()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/map/point-pattern?bbox=22.0,43.0,22.2,43.2"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        await ExpectAsync(owner, "?bbox=nonsense", HttpStatusCode.BadRequest, "map.invalid_bbox");
        await ExpectAsync(
            owner, "?bbox=22.0,43.0,22.2,43.2&steps=999", HttpStatusCode.BadRequest, "validation.failed");
        await ExpectAsync(
            owner,
            "?bbox=22.0,43.0,22.2,43.2&simulations=100000",
            HttpStatusCode.BadRequest,
            "validation.failed");
        await ExpectAsync(
            owner,
            $"?bbox=22.0,43.0,22.2,43.2&areaId={Guid.NewGuid()}",
            HttpStatusCode.NotFound,
            "feature.not_found");
    }

    [Fact]
    public async Task Entrances_strung_along_a_line_show_that_direction_on_the_alignment_rose()
    {
        // Spacing statistics cannot see a direction: a row of entrances along a fault and a round
        // huddle of the same entrances are the same answer to both of them. The bearings between
        // pairs are what tells those apart, and they are binned into the same sectors a cave's
        // passage trends are, so the same rose draws both.
        const string bbox = "24.0,44.0,24.6,44.6";
        for (var i = 0; i < 10; i++)
        {
            // Due east of one another, a few kilometres apart: every joining line runs 090.
            await CreateCaveWithEntranceAsync(owner, $"Pp Line {i}", 24.10 + (0.04 * i), 44.30);
        }

        // The minimum separation is stated rather than left to its default, which is the
        // location-protection grid: adjacent entrances here are about three kilometres apart, and
        // the default would drop every adjacent pair as too close to have a reliable direction.
        var body = await PatternAsync(
            owner, bbox,
            "&simulations=0&maxPairSeparationMetres=60000&minPairSeparationMetres=100");
        var alignment = body["alignment"]!;

        alignment["pairCount"]!.GetValue<int>().ShouldBe(10 * 9 / 2);

        var mean = alignment["rose"]!["byLength"]!["meanAxisDegrees"]!.GetValue<double>();
        mean.ShouldBe(90d, 2d);

        // Concentrated about that axis and not merely averaging to it — a rose spread evenly over
        // every sector averages to something too, and would pass a mean assertion alone.
        alignment["rose"]!["byLength"]!["resultantLength"]!.GetValue<double>()
            .ShouldBeGreaterThan(0.95d);

        // The bins are the rose's own, so the client draws this through the diagram it already has
        // rather than through a second one built for it.
        alignment["rose"]!["bins"]!.AsArray().Count.ShouldBe(18);

        // And the separation range really excludes: a ceiling below every gap leaves no pair, so
        // no rose at all rather than a rose of nothing.
        var narrow = await PatternAsync(
            owner, bbox, "&simulations=0&maxPairSeparationMetres=10&minPairSeparationMetres=1");
        narrow["alignment"]!.ShouldBeNull();
    }

    [Fact]
    public async Task A_curve_asked_for_without_simulations_reports_no_band_rather_than_a_flat_one()
    {
        const string bbox = "23.0,45.0,23.6,45.6";
        for (var i = 0; i < 12; i++)
        {
            await CreateCaveWithEntranceAsync(owner, $"Pp NoBand {i}", 23.05 + (0.045 * i), 45.05 + (0.04 * i));
        }

        var body = await PatternAsync(owner, bbox, "&simulations=0");
        var ripley = body["ripley"]!;

        // Nought is a legitimate request — the shape of the curve without the cost of testing it.
        // What it must not do is come back with the band collapsed onto the observed curve, which
        // reads downstream as "exactly what chance would give": the opposite of what happened.
        ripley["simulations"]!.GetValue<int>().ShouldBe(0);
        var steps = ripley["steps"]!.AsArray();
        foreach (var step in steps)
        {
            step!["lowerL"]!.ShouldBeNull();
            step["upperL"]!.ShouldBeNull();
            step["observedL"]!.GetValue<double>().ShouldBeGreaterThanOrEqualTo(0d);
        }

        // The curve itself is real: nought at the tightest radii, because no pair is that close,
        // and clearly positive by the widest. Without this the assertions above would also pass
        // for an endpoint that returned a row of zeroes.
        steps[^1]!["observedL"]!.GetValue<double>().ShouldBeGreaterThan(0d);

        // The positive half: asked for a band, it reports the number that ran and a real one.
        var banded = await PatternAsync(owner, bbox, "&simulations=9");
        banded["ripley"]!["simulations"]!.GetValue<int>().ShouldBe(9);
        banded["ripley"]!["steps"]!.AsArray()[0]!["lowerL"]!.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_window_outside_the_world_is_refused_before_its_edges_are_measured()
    {
        // Four finite numbers parse, and this is still not a window. The perimeter is segmentised
        // before anything else happens, so an unchecked box of this size asks the database for
        // tens of billions of vertices and is answered with an out-of-memory rather than a result.
        await ExpectAsync(owner, "?bbox=-70000000,-70000000,70000000,70000000",
            HttpStatusCode.BadRequest, "map.invalid_bbox");
        await ExpectAsync(owner, "?bbox=24,44,23,43", HttpStatusCode.BadRequest, "map.invalid_bbox");
    }

    private async Task<JsonObject> PatternAsync(HttpClient client, string bbox, string extra)
    {
        var response = await client.GetAsync($"/api/v1/map/point-pattern?bbox={bbox}{extra}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    private async Task ExpectAsync(
        HttpClient client, string query, HttpStatusCode status, string code)
    {
        var response = await client.GetAsync($"/api/v1/map/point-pattern{query}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, payload);
        System.Text.Json.JsonDocument.Parse(payload).RootElement
            .GetProperty("code").GetString().ShouldBe(code);
    }

    /// <summary>A name no other run of this class can collide with, inside the length the API takes.</summary>
    private static string Unique(string name)
    {
        var candidate = $"{name} {Guid.NewGuid():N}";
        return candidate.Length <= 40 ? candidate : candidate[..40];
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
        string name, double west, double south, double east, double north)
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
