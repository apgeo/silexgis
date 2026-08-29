// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The measured shape of a closed depression, against outlines whose answers are known before
/// the query runs.
///
/// <para>
/// Both fixtures were laid out as metric shapes in the working system and converted into stored
/// degrees, so the expected area, perimeter, axis lengths and bearing are the numbers they were
/// built from rather than numbers read back off an earlier run. That is what makes them able to
/// fail: a query measuring in degrees does not miss these by a few percent, it misses the area
/// by ten orders of magnitude.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PolygonMorphometryTests : IAsyncLifetime, IDisposable
{
    /// <summary>
    /// A 200 m by 50 m rectangle whose long side bears 060°, centred near 46.05 N 26.35 E.
    /// Area 10 000 m², perimeter 500 m, circularity 4π·10000/500² = 0.502655, elongation 4.
    /// </summary>
    private const string RectangleWkt =
        "POLYGON((26.352721194 46.051096658,26.354949494 46.052009284,26.354621809 46.052397158,"
        + "26.352393499 46.051484525,26.352721194 46.051096658))";

    /// <summary>
    /// A regular twelve-sided polygon of circumradius 100 m about the same centre — the
    /// circle-ish doline. Area 12·½·R²·sin 30° = 30 000 m², perimeter 24·R·sin 15° = 621.166 m,
    /// so circularity is 4π·30000/621.166² = 0.977049 and the smallest containing rectangle is
    /// square: 2·R·cos 15° = 193.185 m on both axes, elongation 1.
    /// </summary>
    private const string DodecagonWkt =
        "POLYGON((26.354964064 46.051754214,26.354785652 46.052203221,26.354308694 46.052529961,"
        + "26.353660992 46.052646881,26.353016103 46.052522651,26.352546831 46.05219056,"
        + "26.352378917 46.051739595,26.352557347 46.051290592,26.353034304 46.050963859,"
        + "26.353681988 46.050846943,26.35432686 46.050971169,26.354796132 46.051303252,"
        + "26.354964064 46.051754214))";

    private readonly SilexGisApiFactory factory;
    private readonly int workingSrid = new SpatialOptions().WorkingSrid;

    private HttpClient owner = null!;
    private Guid ownerId;
    private long sinkholeTypeId;

    public PolygonMorphometryTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"mmown-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            sinkholeTypeId = await db.FeatureTypes
                .Where(t => t.Code == "sinkhole").Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"mmown-{suffix}@t.local");
    }

    [Fact]
    public async Task A_doline_may_be_drawn_as_an_outline_and_not_only_dropped_as_a_marker()
    {
        // The kind shipped accepting points alone. Both halves are asserted so that a widening
        // which quietly replaced one class with the other would still be caught.
        var marker = await owner.PostAsJsonAsync("/api/v1/features", Body(
            "Marker doline", new { type = "Point", coordinates = new[] { 26.3536, 46.0517 } }));
        marker.StatusCode.ShouldBe(HttpStatusCode.Created, await marker.Content.ReadAsStringAsync());

        var rim = await owner.PostAsJsonAsync("/api/v1/features", Body("Rim doline", Rectangle()));
        rim.StatusCode.ShouldBe(HttpStatusCode.Created, await rim.Content.ReadAsStringAsync());

        // A line is still not a doline, so the class check is doing something rather than
        // having been switched off.
        var line = await owner.PostAsJsonAsync("/api/v1/features", Body("Not a doline", new
        {
            type = "LineString",
            coordinates = new[] { new[] { 26.352, 46.051 }, new[] { 26.354, 46.052 } },
        }));
        line.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await line.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>()
            .ShouldBe("feature.geometry_invalid");
    }

    [Fact]
    public async Task The_classical_parameters_match_the_shapes_they_were_built_from()
    {
        var rectangleId = await CreateAsync(Body("Elongated doline", Rectangle()));
        var dodecagonId = await CreateAsync(Body("Round doline", Dodecagon()));

        var rectangle = await MeasureAsync(rectangleId, Admin());
        rectangle.ShouldNotBeNull();
        rectangle.GeometryValid.ShouldBeTrue();
        rectangle.AreaM2.ShouldNotBeNull().ShouldBe(10_000d, 1d);
        rectangle.PerimeterM.ShouldNotBeNull().ShouldBe(500d, 0.1d);
        rectangle.Circularity.ShouldNotBeNull().ShouldBe(0.502655d, 0.0005d);
        rectangle.LongAxisM.ShouldNotBeNull().ShouldBe(200d, 0.1d);
        rectangle.ShortAxisM.ShouldNotBeNull().ShouldBe(50d, 0.1d);
        rectangle.Elongation.ShouldNotBeNull().ShouldBe(4d, 0.01d);

        // The rectangle was laid out with its long side bearing 060°. An azimuth read off the
        // stored degrees would be out by roughly the latitude's cosine — about twelve degrees
        // here — so this is the assertion that the bearing is measured on the projected shape.
        rectangle.LongAxisAzimuthDegrees.ShouldNotBeNull().ShouldBe(60d, 0.1d);

        // Centroid comes back in the stored system, and it is the middle of the area rather
        // than the middle of the coordinate range.
        rectangle.CentroidLongitude.ShouldNotBeNull().ShouldBe(26.35367d, 0.0001d);
        rectangle.CentroidLatitude.ShouldNotBeNull().ShouldBe(46.05175d, 0.0001d);

        var round = await MeasureAsync(dodecagonId, Admin());
        round.ShouldNotBeNull();
        round.AreaM2.ShouldNotBeNull().ShouldBe(30_000d, 1d);
        round.PerimeterM.ShouldNotBeNull().ShouldBe(621.166d, 0.1d);
        round.Circularity.ShouldNotBeNull().ShouldBe(0.977049d, 0.0005d);
        round.LongAxisM.ShouldNotBeNull().ShouldBe(193.185d, 0.1d);
        round.Elongation.ShouldNotBeNull().ShouldBe(1d, 0.01d);

        // The two shapes are ordered by size in the bulk table, and only they are in it.
        var table = await MeasureAreaAsync(Admin());
        var mine = table.Where(r => r.FeatureId == rectangleId || r.FeatureId == dodecagonId).ToList();
        mine.Count.ShouldBe(2);
        mine[0].FeatureId.ShouldBe(dodecagonId);
    }

    [Fact]
    public async Task Measuring_in_the_stored_system_instead_of_the_working_one_answers_nonsense()
    {
        var rectangleId = await CreateAsync(Body("Degree doline", Rectangle()));

        var metric = await MeasureAsync(rectangleId, Admin());
        metric.ShouldNotBeNull();
        metric.AreaM2.ShouldNotBeNull().ShouldBe(10_000d, 1d);

        // The same fixture, the same query, measured in the system the geometry is stored in.
        // Degrees are not metres and their square is not a square metre: the area comes back as
        // about a millionth, and the axes as thousandths. This is here so that a working system
        // silently falling back to the stored one — a wrong setting, a transform quietly
        // dropped — cannot leave every other assertion in this file still passing.
        var degrees = await MeasureAsync(rectangleId, Admin(), srid: 4326);
        degrees.ShouldNotBeNull();
        degrees.AreaM2.ShouldNotBeNull().ShouldBeLessThan(1d);
        degrees.LongAxisM.ShouldNotBeNull().ShouldBeLessThan(1d);
        Should.Throw<ShouldAssertException>(
            () => degrees.AreaM2.ShouldNotBeNull().ShouldBe(10_000d, 1d));
    }

    [Fact]
    public async Task A_reader_who_may_not_place_a_doline_is_told_nothing_about_its_shape()
    {
        // A stranger with no grants at all: the visibility of each feature is the whole of what
        // decides this, so the pair differs in exactly one thing — whether the rim is guarded.
        var open = await CreateAsync(Body("Open doline", Rectangle(), visibility: "public"));
        var guarded = await CreateAsync(
            Body("Guarded doline", Rectangle(), visibility: "public", locationProtected: true));

        var stranger = new AccessContext(Guid.CreateVersion7(), isFullAdmin: false, [], []);

        // The positive half, in the same test: the open one is measured for that same stranger,
        // so the silence below is the protection rule and not a query that finds nothing.
        (await MeasureAsync(open, stranger)).ShouldNotBeNull();

        // A shape is a position. Its centroid says where the doline is outright, and its
        // outline and bearing place it as surely once the ground is known, so nothing is
        // returned — not an approximate area, not a shape without a centroid.
        (await MeasureAsync(guarded, stranger)).ShouldBeNull();

        // And the guarded one is genuinely readable by that stranger, which is what makes this
        // a placement rule rather than a visibility one.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var connection = db.Database.GetDbConnection();
        var (visibleSql, _, parameters) = SilexGis.Infrastructure.Permissions.AccessSql
            .FeatureLayerFragments(stranger, "f");
        parameters.Add("probe_id", guarded);
        var readable = await connection.QuerySingleAsync<bool>(new CommandDefinition(
            $"SELECT EXISTS (SELECT 1 FROM features f WHERE f.id = @probe_id AND {visibleSql})",
            parameters));
        readable.ShouldBeTrue();
    }

    [Fact]
    public async Task A_shape_that_crosses_itself_is_reported_as_unmeasurable_rather_than_as_empty()
    {
        // A bow tie has an area function that answers, and it answers zero — which would read as
        // a doline of no size and no shape rather than as a defect in the drawing. Saving one is
        // already refused, so the only way a database holds such a ring is around the write path
        // (a restored dump, a hand-run repair), and it is written that way here on purpose: the
        // guard exists for the row nobody meant to create.
        var featureId = await CreateAsync(Body("Bow tie", Rectangle()));

        await using (var setup = factory.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE features
                SET geom = ST_GeomFromText(
                    'POLYGON((26.3520 46.0510, 26.3540 46.0530, 26.3540 46.0510,
                              26.3520 46.0530, 26.3520 46.0510))', 4326)
                WHERE id = {0}
                """,
                featureId);
        }

        var row = await MeasureAsync(featureId, Admin());
        row.ShouldNotBeNull();
        row.GeometryValid.ShouldBeFalse();
        row.AreaM2.ShouldBeNull();
        row.Circularity.ShouldBeNull();
        row.CentroidLongitude.ShouldBeNull();
    }

    [Fact]
    public async Task A_ring_that_crosses_itself_is_refused_before_it_is_ever_stored()
    {
        // The other half of the guard above: the drawing is rejected on the way in, so a
        // measurable database does not fill with shapes that have no shape.
        var response = await owner.PostAsJsonAsync("/api/v1/features", Body("Bad rim", new
        {
            type = "Polygon",
            coordinates = new[]
            {
                new[]
                {
                    new[] { 26.3520, 46.0510 }, new[] { 26.3540, 46.0530 },
                    new[] { 26.3540, 46.0510 }, new[] { 26.3520, 46.0530 },
                    new[] { 26.3520, 46.0510 },
                },
            },
        }));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>()
            .ShouldBe("feature.geometry_invalid");
    }

    private AccessContext Admin() => new(ownerId, isFullAdmin: true, [], []);

    private async Task<PolygonMorphometryRow?> MeasureAsync(
        Guid featureId, AccessContext ctx, int? srid = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var (sql, parameters) = PolygonMorphometrySql.BuildForFeature(ctx, featureId, srid ?? workingSrid);
        return await db.Database.GetDbConnection()
            .QuerySingleOrDefaultAsync<PolygonMorphometryRow>(new CommandDefinition(sql, parameters));
    }

    private async Task<IReadOnlyList<PolygonMorphometryRow>> MeasureAreaAsync(AccessContext ctx)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var (sql, parameters) = PolygonMorphometrySql.BuildForArea(
            ctx, 26.34, 46.04, 26.37, 46.06, sinkholeTypeId, limit: 50, workingSrid);
        var rows = await db.Database.GetDbConnection()
            .QueryAsync<PolygonMorphometryRow>(new CommandDefinition(sql, parameters));
        return [.. rows];
    }

    private static object Rectangle() => WktPolygon(RectangleWkt);

    private static object Dodecagon() => WktPolygon(DodecagonWkt);

    /// <summary>The fixture outlines are written as WKT and handed over as GeoJSON.</summary>
    private static object WktPolygon(string wkt)
    {
        var inner = wkt["POLYGON((".Length..^2];
        var ring = inner.Split(',')
            .Select(pair => pair.Trim().Split(' '))
            .Select(parts => new[]
            {
                double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
            })
            .ToArray();
        return new { type = "Polygon", coordinates = new[] { ring } };
    }

    private object Body(
        string name, object geometry, string visibility = "private", bool locationProtected = false) => new
    {
        kind = "generic",
        name,
        featureTypeId = sinkholeTypeId,
        geometry,
        visibility,
        locationProtected,
    };

    private async Task<Guid> CreateAsync(object body)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
    }

    public Task DisposeAsync()
    {
        owner.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
