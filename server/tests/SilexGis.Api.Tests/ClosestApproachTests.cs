// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Api.Features.Caves;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// How close two caves come to each other, and who is allowed to be told.
///
/// <para>
/// The fixture caves are two straight pieces of passage running north, one directly north of the
/// other, at altitudes a hundred and fifty metres apart. Their closest approach is therefore
/// between two known ends, and its horizontal and vertical parts are separately known before any
/// query runs — which is what makes the measurement able to fail rather than merely able to
/// return.
/// </para>
/// </summary>
public sealed class ClosestApproachTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const double Longitude = 24.0;

    /// <summary>Where the southern cave's passage starts, and where it ends.</summary>
    private const double SouthStartLatitude = 46.0;
    private const double SouthEndLatitude = 46.001;

    /// <summary>Where the northern cave's passage starts — the gap is the closest approach.</summary>
    private const double NorthStartLatitude = 46.003;
    private const double NorthEndLatitude = 46.004;

    /// <summary>
    /// The two-thousandth of a degree of latitude between the two nearest ends, in metres.
    /// A degree of latitude is about 111 132 m at this latitude and the working system's grid
    /// stretches that by well under a part in a thousand, so 222 m is the figure to a metre.
    /// </summary>
    private const double ExpectedHorizontalM = 222.3;

    private const double SouthAltitude = 100d;
    private const double NorthAltitude = -50d;
    private const double ExpectedVerticalM = 150d;

    /// <summary>Pythagoras over the two above: sqrt(222.3² + 150²).</summary>
    private const double ExpectedDistanceM = 268.2;

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly int workingSrid = new SpatialOptions().WorkingSrid;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private HttpClient anonymous = null!;
    private Guid ownerId;
    private long caveTypeId;

    public ClosestApproachTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            // The queue lives in the container every test class shares; a drain started here would
            // claim work another class queued and fail it against storage this host does not have.
            JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"ca-own-{suffix}@t.local");

        // A Viewer, deliberately: the seeded editors group reads past visibility everywhere by
        // design, so a negative assertion made with an editor would pass for the wrong reason.
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ca-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"ca-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"ca-read-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    /// <summary>
    /// The test this whole feature stands or falls on. Three-dimensional distance is cartesian: it
    /// adds the three ordinates as it finds them, so over stored longitude, latitude and metres of
    /// altitude it returns a number that is mostly the height difference and is not a distance at
    /// all. The same pair is measured twice — once through the working system, once with the
    /// transform deliberately made a no-op — and the second must fail every assertion the first
    /// passes, or nothing here would notice degrees being used as metres.
    /// </summary>
    [Fact]
    public async Task The_distance_is_measured_in_metres_and_a_variant_measured_in_degrees_fails_it()
    {
        var (south, north) = await CreatePairAsync();

        var metric = await MeasureAsync(Admin(), south, north);
        metric.ShouldNotBeNull();
        metric.HasAltitudes.ShouldBeTrue();
        metric.HorizontalDistanceM.ShouldNotBeNull().ShouldBe(ExpectedHorizontalM, 2d);
        metric.VerticalDistanceM.ShouldNotBeNull().ShouldBe(ExpectedVerticalM, 0.01d);
        metric.DistanceM.ShouldNotBeNull().ShouldBe(ExpectedDistanceM, 2d);

        // Due north, and read off the spheroid rather than off the projected grid.
        metric.BearingDegrees.ShouldNotBeNull().ShouldBe(0d, 0.5d);

        var degrees = await MeasureAsync(Admin(), south, north, srid: 4326);
        degrees.ShouldNotBeNull();

        // Two thousandths of a degree, reported as though it were a length. This is the whole
        // failure: it is not a few percent out, it is three orders of magnitude out, and it is the
        // number every figure on this feature would carry if the projection were dropped.
        degrees.HorizontalDistanceM.ShouldNotBeNull().ShouldBeLessThan(1d);
        Should.Throw<ShouldAssertException>(() =>
            degrees.HorizontalDistanceM!.Value.ShouldBe(ExpectedHorizontalM, 2d));

        // And the distance collapses onto the height difference, because the horizontal ordinates
        // contribute almost nothing once they are degrees being added to metres.
        degrees.DistanceM.ShouldNotBeNull().ShouldBe(ExpectedVerticalM, 0.1d);
        Should.Throw<ShouldAssertException>(() =>
            degrees.DistanceM!.Value.ShouldBe(ExpectedDistanceM, 2d));
    }

    /// <summary>
    /// The case the whole gate exists for. A viewer who may read a protected cave, and is shown its
    /// record when they ask for it, still gets no distance from it to anything — because a distance
    /// and a bearing from a cave they can already place would put the protected one on the map to
    /// the metre. The open pair is measured for the same viewer in the same test, so a gate that
    /// had simply stopped answering would not pass this.
    /// </summary>
    [Fact]
    public async Task A_viewer_who_may_read_a_cave_but_not_place_it_is_given_no_distance_at_all()
    {
        var (south, north) = await CreatePairAsync();
        var guarded = await CreateCaveAsync(locationProtected: true);
        await UploadCenterlineAsync(guarded, Passage(46.006, 46.007, 20d));

        // The viewer can measure the open pair, so the machinery is running.
        var open = await viewer.GetAsync($"/api/v1/caves/{south}/closest-approach/{north}");
        open.StatusCode.ShouldBe(HttpStatusCode.OK, await open.Content.ReadAsStringAsync());
        (await open.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("distanceM").GetDouble().ShouldBe(ExpectedDistanceM, 2d);

        // And the viewer can read the guarded cave's own record — which is what makes this the
        // readable-but-not-placeable case rather than an unreadable one.
        (await viewer.GetAsync($"/api/v1/caves/{guarded}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // But no distance to it, in either direction, and no distance that is merely rounded or
        // snapped: the route answers as though the cave did not exist.
        foreach (var url in new[]
        {
            $"/api/v1/caves/{north}/closest-approach/{guarded}",
            $"/api/v1/caves/{guarded}/closest-approach/{north}",
        })
        {
            var refused = await viewer.GetAsync(url);
            refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await refused.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code").GetString().ShouldBe("cave.not_found");
        }

        // A cave that never existed answers identically, which is the point of spelling the
        // refusal this way.
        var absent = await viewer.GetAsync($"/api/v1/caves/{north}/closest-approach/{Guid.NewGuid()}");
        absent.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The owner, who may place it, gets the measurement — so the refusal above is a rule about
        // the caller and not the guarded cave quietly having no line work.
        var allowed = await owner.GetAsync($"/api/v1/caves/{north}/closest-approach/{guarded}");
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await allowed.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("distanceM").GetDouble().ShouldBeGreaterThan(0d);
    }

    /// <summary>
    /// The rest of the matrix: a cave the caller may not read at all, on either side and on both.
    /// </summary>
    [Fact]
    public async Task A_cave_the_caller_may_not_read_is_no_cave_on_either_side_of_the_pair()
    {
        var open = await CreateCaveAsync();
        await UploadCenterlineAsync(open, Passage(SouthStartLatitude, SouthEndLatitude, SouthAltitude));

        var hiddenA = await CreateCaveAsync(visibility: "private");
        await UploadCenterlineAsync(hiddenA, Passage(NorthStartLatitude, NorthEndLatitude, NorthAltitude));
        var hiddenB = await CreateCaveAsync(visibility: "private");
        await UploadCenterlineAsync(hiddenB, Passage(46.006, 46.007, 10d));

        // One side unreadable, from either end; then neither side readable.
        foreach (var (a, b) in new[]
        {
            (open, hiddenA),
            (hiddenA, open),
            (hiddenA, hiddenB),
        })
        {
            var refused = await viewer.GetAsync($"/api/v1/caves/{a}/closest-approach/{b}");
            refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await refused.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code").GetString().ShouldBe("cave.not_found");
        }

        // The owner reads all three, so the caves and their line work exist and the refusals above
        // are about who is asking.
        var measured = await owner.GetAsync($"/api/v1/caves/{hiddenA}/closest-approach/{hiddenB}");
        measured.StatusCode.ShouldBe(HttpStatusCode.OK, await measured.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Two absences that are not the same absence, and neither of which is a number.
    /// </summary>
    [Fact]
    public async Task A_cave_with_no_line_work_and_one_drawn_in_plan_each_say_why_there_is_no_distance()
    {
        var (south, north) = await CreatePairAsync();

        var bare = await CreateCaveAsync();
        var noLineWork = await Read(owner.GetAsync($"/api/v1/caves/{south}/closest-approach/{bare}"));
        noLineWork.GetProperty("absence").GetString().ShouldBe("noLineWork");
        noLineWork.GetProperty("distanceM").ValueKind.ShouldBe(JsonValueKind.Null);
        noLineWork.GetProperty("from").ValueKind.ShouldBe(JsonValueKind.Null);

        // A survey drawn in plan is stored with a third ordinate of zero so that it can be held
        // and drawn at all, so the database calls it three-dimensional. Measuring between it and a
        // real survey would report a cave a hundred metres up as being level with one below it.
        var drawn = await CreateCaveAsync();
        await UploadCenterlineAsync(drawn, Passage(46.006, 46.007, altitude: null));

        var plan = await Read(owner.GetAsync($"/api/v1/caves/{north}/closest-approach/{drawn}"));
        plan.GetProperty("absence").GetString().ShouldBe("noAltitudes");
        plan.GetProperty("distanceM").ValueKind.ShouldBe(JsonValueKind.Null);
        plan.GetProperty("verticalDistanceM").ValueKind.ShouldBe(JsonValueKind.Null);

        // Both caves are named on both answers: an absence still says which pair it is about.
        plan.GetProperty("caveAName").ValueKind.ShouldNotBe(JsonValueKind.Null);
        plan.GetProperty("caveBName").ValueKind.ShouldNotBe(JsonValueKind.Null);
    }

    /// <summary>
    /// The table over an area: nearest first, and built only over pairs the caller may place, so
    /// it cannot be used to ask the per-pair question about a cave the per-pair route refuses.
    /// </summary>
    /// <remarks>
    /// Its caves sit in a band of latitude no other test here uses, and the box is drawn round
    /// that band. Every test in this class shares one database, so a box wide enough to be
    /// convenient would list caves another test made and turn an ordering assertion into a race.
    /// </remarks>
    [Fact]
    public async Task The_table_lists_the_nearest_pairs_and_only_ones_the_caller_may_place()
    {
        const double band = 46.02;
        const string box = "west=23.9&south=46.015&east=24.1&north=46.035&maxDistanceM=2000";

        var south = await CreateCaveAsync();
        await UploadCenterlineAsync(south, Passage(band, band + 0.001, SouthAltitude));
        var north = await CreateCaveAsync();
        await UploadCenterlineAsync(north, Passage(band + 0.003, band + 0.004, NorthAltitude));

        // Closer to the northern cave than the southern one is, so the ordering has something to
        // get wrong.
        var near = await CreateCaveAsync();
        await UploadCenterlineAsync(near, Passage(band + 0.0045, band + 0.0055, NorthAltitude));

        var guarded = await CreateCaveAsync(locationProtected: true);
        await UploadCenterlineAsync(guarded, Passage(band + 0.0042, band + 0.0043, NorthAltitude));

        var forOwner = await Read(owner.GetAsync($"/api/v1/caves/closest-approaches?{box}"));
        var ownerPairs = forOwner.GetProperty("pairs");

        // Four caves make six pairs, and the owner may place all of them.
        ownerPairs.GetArrayLength().ShouldBe(6);
        CavesIn(ownerPairs).ShouldContain(guarded);

        var forViewer = await Read(viewer.GetAsync($"/api/v1/caves/closest-approaches?{box}"));
        var viewerPairs = forViewer.GetProperty("pairs");

        // The guarded cave is in no pair at all for the viewer, on either side of one — and the
        // three pairs among the open caves are all there, so this is a protection rule rather than
        // a table that quietly stopped being built.
        CavesIn(viewerPairs).ShouldNotContain(guarded);
        viewerPairs.GetArrayLength().ShouldBe(3);
        CavesIn(viewerPairs).ShouldContain(north);

        // Nearest first.
        var distances = viewerPairs.EnumerateArray()
            .Select(p => p.GetProperty("distanceM").GetDouble())
            .ToList();
        distances.ShouldBe(distances.OrderBy(d => d).ToList());

        // The nearest pair is the northern cave and the one just beyond it — fifty-odd metres
        // apart and level with each other — not the southern one two hundred metres away.
        var nearest = viewerPairs[0];
        new[] { nearest.GetProperty("caveAId").GetGuid(), nearest.GetProperty("caveBId").GetGuid() }
            .ShouldBe([north, near], ignoreOrder: true);
        nearest.GetProperty("distanceM").GetDouble().ShouldBe(55.6d, 1d);
        nearest.GetProperty("verticalDistanceM").GetDouble().ShouldBe(0d, 0.01d);
        _ = south;

        // A threshold below every gap in the fixture empties the table rather than shortening it,
        // which is what says the threshold is applied to the distance and not to the row count.
        var tight = await Read(viewer.GetAsync(
            "/api/v1/caves/closest-approaches?west=23.9&south=46.015&east=24.1&north=46.035&maxDistanceM=10"));
        tight.GetProperty("pairs").GetArrayLength().ShouldBe(0);
        tight.GetProperty("maxDistanceM").GetDouble().ShouldBe(10d);
    }

    /// <summary>
    /// What the pairing costs per pair, asserted against the plan rather than against a clock.
    ///
    /// <para>
    /// The area query compares every admitted cave with every other one, so whatever its join
    /// evaluates runs a number of times that grows with the square of how many caves the box
    /// holds. A cast of a survey to the geography type on that path is therefore the one shape
    /// that cannot be allowed: a survey is a multi-line string of tens of thousands of components,
    /// the cast detoasts and traverses the whole of it, and no index can help a comparison between
    /// two rows of a materialised intermediate. It would pass every test in this class — the
    /// fixtures hold three or four caves — and fall over on a real karst region.
    /// </para>
    /// <para>
    /// So the plan itself is read, and every condition and filter in it is required to be free of
    /// the geography type. The measurement is still metre-accurate: it is made in the working
    /// system, which is what every other figure on this answer is measured in.
    /// </para>
    /// </summary>
    [Fact]
    public async Task No_condition_on_the_pairing_path_casts_a_survey_to_the_geography_type()
    {
        await CreatePairAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var (sql, parameters) = ClosestApproachSql.BuildForArea(
            Admin(),
            23.9,
            45.9,
            24.1,
            46.1,
            ClosestApproachLimits.DefaultDistanceM,
            ClosestApproachLimits.DefaultRows,
            ClosestApproachLimits.MaxCavesPaired,
            workingSrid);

        var planLines = await db.Database.GetDbConnection().QueryAsync<string>(
            new CommandDefinition($"EXPLAIN (ANALYZE, BUFFERS)\n{sql}", parameters));
        var plan = string.Join("\n", planLines);

        var predicates = plan.Split('\n')
            .Where(line => line.Contains("Cond:", StringComparison.Ordinal)
                || line.Contains("Filter:", StringComparison.Ordinal))
            .ToList();

        // A plan with no conditions in it at all would pass the assertion below for the wrong
        // reason, so the plan is first required to have narrowed anything.
        predicates.ShouldNotBeEmpty("the plan states no conditions at all, so it cannot be read");

        predicates
            .Where(line => line.Contains("geography", StringComparison.OrdinalIgnoreCase))
            .ShouldBeEmpty(
                "no condition on the closest-approach path may cast to the geography type: the "
                + "pairing evaluates its condition once per pair of caves, over whole surveys, "
                + "with no index able to answer it");

        // And the two halves that replaced it are both there: the bounding-box pre-filter, which
        // is answered from the geometry header, and the metric test on what survived it.
        predicates.ShouldContain(
            line => line.Contains("st_expand", StringComparison.OrdinalIgnoreCase),
            "the pairing must pre-filter on expanded bounding boxes");
        predicates.ShouldContain(
            line => line.Contains("st_dwithin", StringComparison.OrdinalIgnoreCase),
            "the pairing must still make a metre-accurate test on what the boxes admitted");

        // The ceiling on how many caves may be paired is in the plan, not merely in a constant:
        // it is the only thing that bounds a request whose box is the whole world.
        plan.ShouldContain("Limit", Case.Sensitive,
            "the candidate ceiling must appear as a limit in the plan");
    }

    [Fact]
    public async Task The_routes_are_closed_to_a_caller_with_no_account_and_to_a_nonsensical_pair()
    {
        var (south, north) = await CreatePairAsync();

        (await anonymous.GetAsync($"/api/v1/caves/{south}/closest-approach/{north}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/v1/caves/closest-approaches?west=23.9&south=45.9&east=24.1&north=46.1"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // A cave against itself is nought metres from itself, which is true and is not a question.
        (await owner.GetAsync($"/api/v1/caves/{south}/closest-approach/{south}"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // A box that is not a box, and a threshold past the ceiling.
        (await owner.GetAsync("/api/v1/caves/closest-approaches?west=24.1&south=45.9&east=23.9&north=46.1"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.GetAsync(
            "/api/v1/caves/closest-approaches?west=23.9&south=45.9&east=24.1&north=46.1&maxDistanceM=999999"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static IReadOnlyList<Guid> CavesIn(JsonElement pairs) =>
    [
        .. pairs.EnumerateArray().SelectMany(p => new[]
        {
            p.GetProperty("caveAId").GetGuid(),
            p.GetProperty("caveBId").GetGuid(),
        }),
    ];

    private static async Task<JsonElement> Read(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private AccessContext Admin() => new(ownerId, isFullAdmin: true, [], []);

    private async Task<(Guid South, Guid North)> CreatePairAsync()
    {
        var south = await CreateCaveAsync();
        await UploadCenterlineAsync(south, Passage(SouthStartLatitude, SouthEndLatitude, SouthAltitude));
        var north = await CreateCaveAsync();
        await UploadCenterlineAsync(north, Passage(NorthStartLatitude, NorthEndLatitude, NorthAltitude));
        return (south, north);
    }

    private async Task<ClosestApproachRow?> MeasureAsync(
        AccessContext ctx, Guid a, Guid b, int? srid = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var (sql, parameters) = ClosestApproachSql.BuildForPair(ctx, a, b, srid ?? workingSrid);
        return await db.Database.GetDbConnection()
            .QuerySingleOrDefaultAsync<ClosestApproachRow>(new CommandDefinition(sql, parameters));
    }

    /// <summary>
    /// A straight piece of passage running north along one meridian, as the drawn line work an
    /// upload accepts. A null altitude writes two-dimensional coordinates — a plan drawing, which
    /// is what the storage format then gives a third ordinate of zero.
    /// </summary>
    private static byte[] Passage(double fromLatitude, double toLatitude, double? altitude)
    {
        string Point(double latitude) => altitude is { } z
            ? FormattableString.Invariant($"[{Longitude:R},{latitude:R},{z:R}]")
            : FormattableString.Invariant($"[{Longitude:R},{latitude:R}]");

        return Encoding.UTF8.GetBytes(
            "{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\",\"properties\":{},"
            + "\"geometry\":{\"type\":\"LineString\",\"coordinates\":["
            + Point(fromLatitude) + "," + Point(toLatitude) + "]}}]}");
    }

    private async Task<Guid> CreateCaveAsync(
        bool locationProtected = false, string visibility = "authenticated")
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Approach {Guid.NewGuid():N}"[..28],
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task UploadCenterlineAsync(Guid caveId, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/octet-stream");
        using var form = new MultipartFormDataContent { { content, "file", "passage.geojson" } };
        var response = await owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", form);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    public Task DisposeAsync()
    {
        owner.Dispose();
        viewer.Dispose();
        anonymous.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
