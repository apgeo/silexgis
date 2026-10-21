// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Features.Filters;
using SilexGis.Api.Features.SavedFilters;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Filters;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The filter's spatial conditions, attacked the way a distance or an outline is attacked: by
/// moving the radius or an edge across a protected row's true position and watching the answer.
/// </summary>
/// <remarks>
/// <para>
/// Every protected entrance here sits three tenths of a protection cell west of the lattice point
/// it snaps to, and every question is placed so that the true position and the snapped point fall
/// on opposite sides of it. A build that measured the stored position answers those questions
/// differently for a reader who may not place the entrance — which is what each test asserts never
/// happens — while the owner, who places it exactly, is the control in the same test.
/// </para>
/// <para>
/// Each test works in a spot of its own, far from anything else in the shared database, so a
/// question asked over a few kilometres sees only the rows the test put there.
/// </para>
/// </remarks>
public sealed class FilterSpatialConditionTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const double GridMeters = 5000d;

    private static readonly JsonSerializerOptions Wire = BuildWire();

    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private Guid ownerId;
    private Guid clubId;
    private long caveTypeId;
    private long entranceTypeId;
    private string tag = null!;

    public FilterSpatialConditionTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    private static double Cell => LocationProtection.CellDegrees(GridMeters);

    public async ValueTask InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        // The owner places their own protected rows exactly; the viewer holds no grant of exact view
        // on anything, and the seeded editors' group is not theirs.
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"sp-own-{tag}@t.local");
        var viewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"sp-view-{tag}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"sp-own-{tag}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"sp-view-{tag}@t.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();

        // A club of the two of them and nothing else — no grant comes with it — so a saved filter
        // can be shared from the owner to the viewer.
        var club = new CavingGroup { Name = $"Spatial club {tag}", Slug = $"spatial-club-{tag}" };
        db.CavingGroups.Add(club);
        await db.SaveChangesAsync();
        clubId = club.Id;
        await RosterHelper.AddMemberAsync(db, clubId, ownerId);
        await RosterHelper.AddMemberAsync(db, clubId, viewerId);
        entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
    }

    [Fact]
    public async Task A_radius_crossing_a_protected_entrance_true_position_changes_nothing_for_a_reader_who_may_not_place_it()
    {
        var spot = Spot.Pick();
        var (caveId, entranceId) = await CaveWithEntranceAsync("Guarded", spot.TrueLon, spot.TrueLat, locationProtected: true);
        await AssertSnappedForViewerAsync(caveId, spot.TrueLon);

        // The anchor is west of the true position, so the true position is nearer to it than the
        // snapped point is: a radius between the two distances is the probe.
        var anchor = new PointValue(spot.TrueLon - (0.5 * Cell), spot.TrueLat);
        var toTrue = Geodesy.DistanceMeters(anchor.Longitude, anchor.Latitude, spot.TrueLon, spot.TrueLat);
        var toShown = Geodesy.DistanceMeters(anchor.Longitude, anchor.Latitude, spot.SnappedLon, spot.SnappedLat);
        toShown.ShouldBeGreaterThan(toTrue + 500, "the fixture must put the two distances well apart");

        var justInside = toTrue + 100;
        var justOutside = toTrue - 100;
        var pastShown = toShown + 100;

        // Bisection: the viewer's answer does not move as the radius crosses the true position.
        var viewerInside = await IdsAsync(viewer, Within(justInside, anchor));
        var viewerOutside = await IdsAsync(viewer, Within(justOutside, anchor));
        viewerInside.ShouldNotContain(entranceId, "the radius reached only the true position, not the shown one");
        viewerOutside.ShouldNotContain(entranceId);
        viewerInside.ShouldNotContain(caveId, "a cave answers by its entrance's shown position, never its true one");

        // ...and moves exactly when it crosses the point the viewer is shown, cave and all.
        var viewerPast = await IdsAsync(viewer, Within(pastShown, anchor));
        viewerPast.ShouldContain(entranceId);
        viewerPast.ShouldContain(caveId);

        // The control: the owner places it exactly, so for them the radius is exact.
        (await IdsAsync(owner, Within(justInside, anchor))).ShouldContain(entranceId);
        (await IdsAsync(owner, Within(justOutside, anchor))).ShouldNotContain(entranceId);
    }

    [Fact]
    public async Task Not_within_never_measures_a_protected_entrance_by_its_true_position()
    {
        var spot = Spot.Pick();
        var (_, entranceId) = await CaveWithEntranceAsync("Negated", spot.TrueLon, spot.TrueLat, locationProtected: true);

        var anchor = new PointValue(spot.TrueLon - (0.5 * Cell), spot.TrueLat);
        var toTrue = Geodesy.DistanceMeters(anchor.Longitude, anchor.Latitude, spot.TrueLon, spot.TrueLat);

        // Bounded to the test's own spot, then negated: "near here, but not within r".
        FilterNode NearButNot(double radius) => new AllOfNode(
        [
            Within(20_000, anchor),
            new NotNode(Within(radius, anchor)),
        ]);

        var viewerA = await IdsAsync(viewer, NearButNot(toTrue + 100));
        var viewerB = await IdsAsync(viewer, NearButNot(toTrue - 100));
        viewerA.Contains(entranceId).ShouldBe(viewerB.Contains(entranceId),
            "the negated radius crossed the true position and the answer moved");
        viewerA.ShouldContain(entranceId, "by its shown point the entrance is outside both radii");

        (await IdsAsync(owner, NearButNot(toTrue + 100))).ShouldNotContain(entranceId);
        (await IdsAsync(owner, NearButNot(toTrue - 100))).ShouldContain(entranceId);
    }

    [Fact]
    public async Task An_outline_edge_crossing_a_protected_entrance_true_position_changes_nothing_for_a_reader_who_may_not_place_it()
    {
        var spot = Spot.Pick();
        var (_, entranceId) = await CaveWithEntranceAsync("Outlined", spot.TrueLon, spot.TrueLat, locationProtected: true);

        // Around the true position only — the snapped point is three tenths of a cell east of it.
        var aroundTrue = Square(spot.TrueLon, spot.TrueLat, 0.1 * Cell);
        // Around the snapped point only.
        var aroundShown = Square(spot.SnappedLon, spot.SnappedLat, 0.1 * Cell);

        (await IdsAsync(viewer, Inside(aroundTrue))).ShouldNotContain(entranceId);
        (await IdsAsync(viewer, Inside(aroundShown))).ShouldContain(entranceId);
        (await IdsAsync(viewer, new AllOfNode([Within(20_000, new PointValue(spot.TrueLon, spot.TrueLat)), new NotNode(Inside(aroundTrue))])))
            .ShouldContain(entranceId, "the negated outline is decided on the shown point too");

        (await IdsAsync(owner, Inside(aroundTrue))).ShouldContain(entranceId);
        (await IdsAsync(owner, Inside(aroundShown))).ShouldNotContain(entranceId);
    }

    [Fact]
    public async Task A_protected_outline_a_reader_may_not_place_answers_no_spatial_question_at_all()
    {
        var spot = Spot.Pick();
        var doline = await GenericAsync("Doline", new
        {
            type = "Polygon",
            coordinates = new[] { Ring(spot.TrueLon, spot.TrueLat, 0.05 * Cell) },
        });
        var centre = new PointValue(spot.TrueLon, spot.TrueLat);
        var everywhere = Square(spot.TrueLon, spot.TrueLat, 2 * Cell);

        (await IdsAsync(viewer, Within(20_000, centre))).ShouldNotContain(doline);
        (await IdsAsync(viewer, Inside(everywhere))).ShouldNotContain(doline);
        // Negated, it is in: the answer is the same whatever the stored outline is.
        (await IdsAsync(viewer, new NotNode(Within(20_000, centre)), nameContains: tag)).ShouldContain(doline);

        (await IdsAsync(owner, Within(20_000, centre))).ShouldContain(doline);
        (await IdsAsync(owner, Inside(everywhere))).ShouldContain(doline);
    }

    [Fact]
    public async Task An_object_anchor_the_reader_may_not_place_is_refused_exactly_like_a_missing_one()
    {
        var spot = Spot.Pick();
        var (guardedCave, guarded) = await CaveWithEntranceAsync("Anchor guarded", spot.TrueLon, spot.TrueLat, locationProtected: true);
        var (_, open) = await CaveWithEntranceAsync("Anchor open", spot.TrueLon + Cell, spot.TrueLat, locationProtected: false);
        // Readable but not placeable — the state the refusal is about, not merely an unreadable row.
        await AssertSnappedForViewerAsync(guardedCave, spot.TrueLon);

        var unplaceable = await RefusalAsync(viewer, Within(1000, new ObjectValue("feature", guarded)));
        var missing = await RefusalAsync(viewer, Within(1000, new ObjectValue("feature", Guid.NewGuid())));
        unplaceable.Code.ShouldBe(SpatialConditions.AnchorNotFoundCode);
        unplaceable.ShouldBe(missing, "a withheld anchor must read exactly like one that does not exist");

        // An area the reader may not place is the same refusal.
        (await RefusalAsync(viewer, Inside(new ObjectValue("feature", guarded)))).Code
            .ShouldBe(SpatialConditions.AnchorNotFoundCode);

        // The control: an anchor the viewer places exactly works for them, and the guarded one
        // works for its owner — from it, the open entrance a cell away is in reach.
        (await IdsAsync(viewer, Within(1000, new ObjectValue("feature", open)))).ShouldContain(open);
        (await IdsAsync(owner, Within(10_000, new ObjectValue("feature", guarded)))).ShouldContain(open);
    }

    [Fact]
    public async Task Another_saved_filter_is_run_under_the_runner_access_and_nested_only_once()
    {
        var spot = Spot.Pick();
        var (_, open) = await CaveWithEntranceAsync("Near result", spot.TrueLon, spot.TrueLat, locationProtected: false);

        // A private filter of the owner's: the area around this test's entrance, by an object anchor.
        var inner = await SaveAsync(owner, $"Inner {tag}", Within(100, new ObjectValue("feature", open)));
        var outer = Within(500, new SavedFilterValue(inner));

        (await IdsAsync(owner, outer)).ShouldContain(open);
        (await RefusalAsync(viewer, outer)).ShouldBe(
            await RefusalAsync(viewer, Within(500, new SavedFilterValue(Guid.NewGuid()))),
            "a saved filter the runner may not read reads exactly like one that does not exist");
        (await RefusalAsync(viewer, outer)).Code.ShouldBe(SpatialConditions.AnchorNotFoundCode);

        // A filter measuring from a saved filter may be saved, but measuring from it is refused.
        var middle = await SaveAsync(owner, $"Middle {tag}", outer);
        (await RefusalAsync(owner, Within(500, new SavedFilterValue(middle)))).Code
            .ShouldBe(SpatialConditions.AnchorNestedCode);
    }

    [Fact]
    public async Task A_saved_filter_refuses_a_point_or_an_outline_and_keeps_an_object()
    {
        var spot = Spot.Pick();
        var point = await owner.PostAsJsonAsync(
            "/api/v1/saved-filters",
            new SavedFilterWriteRequest($"Point {tag}", null, About(Within(500, new PointValue(spot.TrueLon, spot.TrueLat))), null),
            Wire);
        point.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await point.Content.ReadAsStringAsync()).ShouldContain(SavedFilterRules.AnchorRefusedCode);

        var outline = await owner.PostAsJsonAsync(
            "/api/v1/saved-filters",
            new SavedFilterWriteRequest($"Outline {tag}", null, About(new NotNode(Inside(Square(spot.TrueLon, spot.TrueLat, Cell)))), null),
            Wire);
        outline.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await outline.Content.ReadAsStringAsync()).ShouldContain(SavedFilterRules.AnchorRefusedCode);

        _ = await SaveAsync(owner, $"Object {tag}", Within(500, new ObjectValue("feature", Guid.NewGuid())));
    }

    [Fact]
    public async Task A_radius_or_an_outline_out_of_bounds_is_refused_before_anything_runs()
    {
        var spot = Spot.Pick();
        var anchor = new PointValue(spot.TrueLon, spot.TrueLat);
        (await RefusalAsync(viewer, Within(0, anchor))).Code.ShouldBe("filter.invalid");
        (await RefusalAsync(viewer, Within(SpatialConditions.MaxRadiusMeters + 1, anchor))).Code.ShouldBe("filter.invalid");
        (await RefusalAsync(viewer, Inside(new PolygonValue([[[0, 0], [1, 1], [1, 0], [0, 1], [0, 0]]])))).Code
            .ShouldBe("filter.invalid");
        (await RefusalAsync(viewer, Within(500, new ObjectValue("tripLog", Guid.NewGuid())))).Code
            .ShouldBe("filter.invalid");
    }

    [Fact]
    public async Task Within_a_cave_measures_a_protected_candidate_by_its_shown_point_never_its_true_one()
    {
        var spot = Spot.Pick();
        var (caveId, entranceId) = await CaveWithEntranceAsync("Near a cave", spot.TrueLon, spot.TrueLat, locationProtected: true);
        await AssertSnappedForViewerAsync(caveId, spot.TrueLon);
        var anchorPoint = new PointValue(spot.TrueLon - (0.5 * Cell), spot.TrueLat);
        var (anchorCave, _) = await CaveWithEntranceAsync("Cave anchor", anchorPoint.Longitude, anchorPoint.Latitude, locationProtected: false);

        // A cave is measured from by its entrances, through its own arm of the statement.
        await AssertMeasuredByShownPointAsync(
            spot, entranceId, anchorPoint, r => Within(r, new ObjectValue("feature", anchorCave)));
    }

    [Fact]
    public async Task Within_another_saved_filter_measures_a_protected_candidate_by_its_shown_point_never_its_true_one()
    {
        var spot = Spot.Pick();
        var (caveId, entranceId) = await CaveWithEntranceAsync("Near a result", spot.TrueLon, spot.TrueLat, locationProtected: true);
        await AssertSnappedForViewerAsync(caveId, spot.TrueLon);
        var anchorPoint = new PointValue(spot.TrueLon - (0.5 * Cell), spot.TrueLat);
        var (_, anchorEntrance) = await CaveWithEntranceAsync("Result anchor", anchorPoint.Longitude, anchorPoint.Latitude, locationProtected: false);

        // Its result is the anchor entrance and its cave, and nothing near the protected one.
        var inner = await SharedAsync($"Anchor set {tag}", Within(1, new ObjectValue("feature", anchorEntrance)));

        await AssertMeasuredByShownPointAsync(
            spot, entranceId, anchorPoint, r => Within(r, new SavedFilterValue(inner)));
    }

    [Fact]
    public async Task Another_saved_filter_is_measured_from_only_by_the_rows_the_runner_may_place()
    {
        var spot = Spot.Pick();
        var member = $"Set member {tag}";
        // The filter's result: an open entrance far west, and a protected one the runner may read
        // but not place. Shared, so the runner reads the very filter its author does.
        var (guardedCave, guarded) = await CaveWithEntranceAsync("Set guarded", spot.TrueLon, spot.TrueLat, locationProtected: true, entranceName: member);
        var (_, far) = await CaveWithEntranceAsync("Set far", spot.TrueLon - (3 * Cell), spot.TrueLat, locationProtected: false, entranceName: member);
        // A candidate of nobody's protection, half a cell west of the protected entrance's true position.
        var probe = new PointValue(spot.TrueLon - (0.5 * Cell), spot.TrueLat);
        var (_, candidate) = await CaveWithEntranceAsync("Set candidate", probe.Longitude, probe.Latitude, locationProtected: false);
        var inner = await SharedAsync(
            $"Members {tag}",
            new ConditionNode(FeatureFilterFields.Name, FilterOp.Contains, [new TextValue(member)]));

        // The withheld state, asserted rather than assumed: the viewer reads the protected entrance,
        // finds it in the filter's result, and is shown it only on the grid.
        await AssertSnappedForViewerAsync(guardedCave, spot.TrueLon);
        (await IdsAsync(viewer, new ConditionNode(FeatureFilterFields.Name, FilterOp.Contains, [new TextValue(member)])))
            .ShouldBe([guarded, far], ignoreOrder: true);

        var toTrue = Geodesy.DistanceMeters(probe.Longitude, probe.Latitude, spot.TrueLon, spot.TrueLat);
        var toShown = Geodesy.DistanceMeters(probe.Longitude, probe.Latitude, spot.SnappedLon, spot.SnappedLat);
        var toFar = Geodesy.DistanceMeters(probe.Longitude, probe.Latitude, spot.TrueLon - (3 * Cell), spot.TrueLat);
        toFar.ShouldBeGreaterThan(toShown + 1000, "the fixture must keep the open member well beyond both");

        // Neither the true position nor the shown point of the protected member is measured from:
        // the candidate is reached only once the radius reaches the open member.
        (await IdsAsync(viewer, Within(toTrue + 300, new SavedFilterValue(inner)))).ShouldNotContain(candidate);
        (await IdsAsync(viewer, Within(toShown + 300, new SavedFilterValue(inner)))).ShouldNotContain(candidate);
        (await IdsAsync(viewer, Within(toFar + 300, new SavedFilterValue(inner)))).ShouldContain(candidate);

        // Negated, the same: "not within" never measures from the protected member either.
        FilterNode NearButNot(double radius) => new AllOfNode(
        [
            Within(2_000, probe),
            new NotNode(Within(radius, new SavedFilterValue(inner))),
        ]);
        (await IdsAsync(viewer, NearButNot(toTrue + 300))).ShouldContain(candidate);

        // The control: the author places the protected member exactly, so for them it is measured
        // from — which also shows the filter runs under each runner's own access, not its author's.
        (await IdsAsync(owner, Within(toTrue + 300, new SavedFilterValue(inner)))).ShouldContain(candidate);
        (await IdsAsync(owner, NearButNot(toTrue + 300))).ShouldNotContain(candidate);
    }

    [Fact]
    public async Task A_saved_filter_measured_from_is_bounded_by_the_rows_its_runner_may_place()
    {
        var spot = Spot.Pick();
        var member = $"Many {tag}";
        var count = SpatialConditions.MaxAnchorRows + 1;
        var ids = new List<Guid>(count);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var typeId = await db.FeatureTypes.Where(t => t.Code == "sinkhole").Select(t => t.Id).SingleAsync();
            for (var i = 0; i < count; i++)
            {
                var id = Guid.NewGuid();
                ids.Add(id);
                db.Features.Add(new Feature
                {
                    Id = id,
                    Name = $"{member} {i}",
                    Kind = FeatureKind.Generic,
                    FeatureTypeId = typeId,
                    OwnerUserId = ownerId,
                    Visibility = Visibility.Authenticated,
                    AncestorIds = [id],
                    Geom = new NetTopologySuite.Geometries.Point(spot.TrueLon + (i * 1e-5), spot.TrueLat) { SRID = 4326 },
                });
                db.FeatureAncestors.Add(new FeatureAncestor { FeatureId = id, AncestorId = id });
            }

            await db.SaveChangesAsync();
        }

        var inner = await SharedAsync(
            $"Many members {tag}",
            new ConditionNode(FeatureFilterFields.Name, FilterOp.Contains, [new TextValue(member)]));
        var fromInner = Within(100, new SavedFilterValue(inner));

        (await RefusalAsync(viewer, fromInner)).Code.ShouldBe(SpatialConditions.AnchorTooLargeCode);

        // One of them protected: the viewer may now place exactly the limit and is answered, while
        // the owner, who places all of them, is still refused — the count is of placeable rows.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var guarded = ids.Take(1).ToArray();
            await db.Features.Where(f => guarded.Contains(f.Id)).ExecuteUpdateAsync(u => u
                .SetProperty(f => f.LocationProtected, true)
                .SetProperty(f => f.IsProtectedEffective, true));
        }

        (await IdsAsync(viewer, fromInner)).ShouldNotBeEmpty();
        (await RefusalAsync(owner, fromInner)).Code.ShouldBe(SpatialConditions.AnchorTooLargeCode);
    }

    [Fact]
    public async Task Inside_a_named_area_decides_a_protected_entrance_by_its_shown_point_never_its_true_one()
    {
        var spot = Spot.Pick();
        var (caveId, entranceId) = await CaveWithEntranceAsync("In an area", spot.TrueLon, spot.TrueLat, locationProtected: true);
        await AssertSnappedForViewerAsync(caveId, spot.TrueLon);

        // Two open areas the viewer places exactly: one around the true position only, one around
        // the shown point only — the snapped point is three tenths of a cell east of the true one.
        var aroundTrue = await GenericAsync("Area true", new
        {
            type = "Polygon",
            coordinates = new[] { Ring(spot.TrueLon, spot.TrueLat, 0.1 * Cell) },
        }, locationProtected: false);
        var aroundShown = await GenericAsync("Area shown", new
        {
            type = "Polygon",
            coordinates = new[] { Ring(spot.SnappedLon, spot.SnappedLat, 0.1 * Cell) },
        }, locationProtected: false);

        (await IdsAsync(viewer, Inside(new ObjectValue("feature", aroundTrue)))).ShouldNotContain(entranceId);
        (await IdsAsync(viewer, Inside(new ObjectValue("feature", aroundShown)))).ShouldContain(entranceId);
        (await IdsAsync(viewer, Inside(new ObjectValue("feature", aroundShown)))).ShouldContain(caveId);
        var bounded = Within(20_000, new PointValue(spot.TrueLon, spot.TrueLat));
        (await IdsAsync(viewer, new AllOfNode([bounded, new NotNode(Inside(new ObjectValue("feature", aroundTrue)))])))
            .ShouldContain(entranceId, "the negated area is decided on the shown point too");
        (await IdsAsync(viewer, new AllOfNode([bounded, new NotNode(Inside(new ObjectValue("feature", aroundShown)))])))
            .ShouldNotContain(entranceId);

        // The control: the owner places the entrance exactly.
        (await IdsAsync(owner, Inside(new ObjectValue("feature", aroundTrue)))).ShouldContain(entranceId);
        (await IdsAsync(owner, Inside(new ObjectValue("feature", aroundShown)))).ShouldNotContain(entranceId);
        (await IdsAsync(owner, new AllOfNode([bounded, new NotNode(Inside(new ObjectValue("feature", aroundTrue)))])))
            .ShouldNotContain(entranceId);
    }

    [Fact]
    public async Task A_named_area_the_reader_may_not_place_is_refused_like_a_missing_one_and_a_point_is_not_an_area()
    {
        var spot = Spot.Pick();
        var guardedArea = await GenericAsync("Guarded area", new
        {
            type = "Polygon",
            coordinates = new[] { Ring(spot.TrueLon, spot.TrueLat, 0.2 * Cell) },
        });
        var openPoint = await GenericAsync("Open point", new
        {
            type = "Point",
            coordinates = new[] { spot.TrueLon, spot.TrueLat },
        }, locationProtected: false);
        var guardedPoint = await GenericAsync("Guarded point", new
        {
            type = "Point",
            coordinates = new[] { spot.TrueLon, spot.TrueLat },
        });

        // Readable to the viewer — it is listed for them by name — and not placeable.
        (await IdsAsync(viewer, new ConditionNode(FeatureFilterFields.Name, FilterOp.Contains, [new TextValue($"Guarded area {tag}")])))
            .ShouldContain(guardedArea);

        var missing = await RefusalAsync(viewer, Inside(new ObjectValue("feature", Guid.NewGuid())));
        missing.Code.ShouldBe(SpatialConditions.AnchorNotFoundCode);
        (await RefusalAsync(viewer, Inside(new ObjectValue("feature", guardedArea)))).ShouldBe(missing);
        // Placement is asked before shape: a point the viewer may not place is "missing", not "a point".
        (await RefusalAsync(viewer, Inside(new ObjectValue("feature", guardedPoint)))).ShouldBe(missing);

        var notAnArea = await RefusalAsync(viewer, Inside(new ObjectValue("feature", openPoint)));
        notAnArea.Code.ShouldBe(SpatialConditions.AnchorNotAnAreaCode);
        notAnArea.Code.ShouldBe(StudyArea.NotAnOutlineCode("filter"));

        // The control: the owner places the guarded area and is answered — the outline covers
        // itself and the point drawn at its centre.
        (await IdsAsync(owner, Inside(new ObjectValue("feature", guardedArea)))).ShouldContain(guardedPoint);
    }

    [Fact]
    public async Task The_prefilter_reach_holds_every_point_the_database_measures_within_the_radius()
    {
        // The reach is an upper bound on what ST_DWithin admits on the spheroid; were it shorter by
        // more than half a protection cell, a protected point would be kept or dropped by its true
        // position. Asked of PostGIS itself, at the largest radius, where the shortfall would be.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var connection = db.Database.GetDbConnection();

        foreach (var (lon, lat) in new[] { (25d, 0d), (25d, 46d), (25d, -70d), (25d, 85d) })
        {
            var reach = SpatialReach.Around(
                new NetTopologySuite.Geometries.Envelope(lon, lon, lat, lat), SpatialConditions.MaxRadiusMeters);
            var edge = (await connection.QueryAsync<EdgePoint>(
                """
                SELECT ST_X(p::geometry) AS x, ST_Y(p::geometry) AS y
                FROM generate_series(0, 355, 5) AS az,
                     LATERAL ST_Project(ST_SetSRID(ST_MakePoint(@lon, @lat), 4326)::geography, @r, radians(az)) AS p
                """,
                new { lon, lat, r = SpatialConditions.MaxRadiusMeters })).ToList();

            edge.Count.ShouldBe(72);
            edge.ShouldAllBe(
                p => reach.Contains(p.X, p.Y),
                $"the reach around {lon}, {lat} falls short of the radius the database measures");
        }
    }

    // ---------- the surfaces that read a filter ----------

    [Fact]
    public async Task The_cave_list_and_its_export_decide_a_protected_cave_by_its_shown_point()
    {
        var spot = Spot.Pick();
        var (caveId, _) = await CaveWithEntranceAsync("Listed", spot.TrueLon, spot.TrueLat, locationProtected: true);
        await AssertSnappedForViewerAsync(caveId, spot.TrueLon);
        var listed = $"Listed {tag}";

        var anchor = new PointValue(spot.TrueLon - (0.5 * Cell), spot.TrueLat);
        var toTrue = Geodesy.DistanceMeters(anchor.Longitude, anchor.Latitude, spot.TrueLon, spot.TrueLat);
        var toShown = Geodesy.DistanceMeters(anchor.Longitude, anchor.Latitude, spot.SnappedLon, spot.SnappedLat);
        string Token(double radius) => FilterToken.Encode(About(Within(radius, anchor)));

        (await CaveNamesAsync(viewer, Token(toTrue + 100))).ShouldNotContain(listed);
        (await CaveNamesAsync(viewer, Token(toShown + 100))).ShouldContain(listed);
        (await ExportedNamesAsync(viewer, Token(toTrue + 100))).ShouldNotContain(listed);
        (await ExportedNamesAsync(viewer, Token(toShown + 100))).ShouldContain(listed);
        var negated = FilterToken.Encode(About(new AllOfNode([Within(20_000, anchor), new NotNode(Within(toTrue + 100, anchor))])));
        (await CaveNamesAsync(viewer, negated)).ShouldContain(listed);

        // The control: the owner places it exactly.
        (await CaveNamesAsync(owner, Token(toTrue + 100))).ShouldContain(listed);
        (await ExportedNamesAsync(owner, Token(toTrue + 100))).ShouldContain(listed);
        (await CaveNamesAsync(owner, negated)).ShouldNotContain(listed);
    }

    [Fact]
    public async Task The_cave_list_and_its_export_refuse_an_anchor_the_reader_may_not_place_like_a_missing_one()
    {
        var spot = Spot.Pick();
        var (guardedCave, guarded) = await CaveWithEntranceAsync("Surface guarded", spot.TrueLon, spot.TrueLat, locationProtected: true);
        await AssertSnappedForViewerAsync(guardedCave, spot.TrueLon);

        string Token(Guid id) => FilterToken.Encode(About(Within(1000, new ObjectValue("feature", id))));
        foreach (var route in new[] { "/api/v1/caves?filter=", "/api/v1/export/caves?format=geojson&filter=" })
        {
            var missing = await SurfaceRefusalAsync(viewer, route + Token(Guid.NewGuid()));
            missing.Code.ShouldBe(SpatialConditions.AnchorNotFoundCode, route);
            (await SurfaceRefusalAsync(viewer, route + Token(guarded))).ShouldBe(missing, route);
            (await SurfaceRefusalAsync(viewer, route + Token(guardedCave))).ShouldBe(missing, route);

            // The control: the owner measures from it.
            (await owner.GetAsync(route + Token(guarded))).StatusCode.ShouldBe(HttpStatusCode.OK, route);
        }
    }

    [Fact]
    public async Task The_cave_list_ranks_nearest_first_only_the_caves_the_reader_may_place()
    {
        var spot = Spot.Pick();
        // As for the filter's own query: the probe sits at the protected cave's true position, so a
        // list ranking it by its true position puts it first and one ranking by its shown point
        // puts it before the open cave six tenths of a cell away.
        var (guardedCave, guarded) = await CaveWithEntranceAsync("List guarded", spot.TrueLon, spot.TrueLat, locationProtected: true);
        _ = await CaveWithEntranceAsync("List near", spot.TrueLon - (0.6 * Cell), spot.TrueLat, locationProtected: false);
        _ = await CaveWithEntranceAsync("List far", spot.TrueLon - (1.2 * Cell), spot.TrueLat, locationProtected: false);
        await AssertSnappedForViewerAsync(guardedCave, spot.TrueLon);
        string[] Named(params string[] names) => [.. names.Select(n => $"List {n} {tag}")];

        var bounded = Within(20_000, new PointValue(spot.TrueLon, spot.TrueLat));
        string Nearest(FilterAnchor? anchor) => FilterToken.Encode(About(bounded) with { Sort = SortKey.Proximity, Anchor = anchor });
        async Task<List<string?>> ListedAsync(HttpClient client, FilterAnchor anchor)
        {
            var response = await client.GetAsync($"/api/v1/caves?sort=proximity&filter={Nearest(anchor)}&pageSize=100");
            var payload = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
            return [.. JsonNode.Parse(payload)!["items"]!.AsArray().Select(i => i!["name"]?.GetValue<string>())];
        }

        var atGuarded = new PointAnchor(spot.TrueLon, spot.TrueLat);
        var farWest = new PointAnchor(spot.TrueLon - (3 * Cell), spot.TrueLat);
        (await ListedAsync(viewer, atGuarded)).ShouldBe(Named("near", "far", "guarded"));
        (await ListedAsync(viewer, farWest)).ShouldBe(Named("far", "near", "guarded"));
        (await ListedAsync(viewer, new ObjectAnchor("feature", (await IdsAsync(viewer, new ConditionNode(
            FeatureFilterFields.Name, FilterOp.Contains, [new TextValue($"List far {tag}")]))).Single())))
            .ShouldBe(Named("far", "near", "guarded"));

        // The control: the owner places the guarded cave, so for them it is nearest to its own spot.
        (await ListedAsync(owner, atGuarded)).ShouldBe(Named("guarded", "near", "far"));
        (await ListedAsync(owner, farWest)).ShouldBe(Named("far", "near", "guarded"));

        // Nothing to measure from, and an anchor the viewer may not place, are refused; the second
        // exactly like an object that does not exist.
        var bare = await SurfaceRefusalAsync(viewer, $"/api/v1/caves?sort=proximity&filter={Nearest(null)}");
        bare.Code.ShouldBe("filter.invalid");
        (await SurfaceRefusalAsync(viewer, "/api/v1/caves?sort=proximity")).Code.ShouldBe("filter.invalid");
        var missing = await SurfaceRefusalAsync(
            viewer, $"/api/v1/caves?sort=proximity&filter={Nearest(new ObjectAnchor("feature", Guid.NewGuid()))}");
        missing.Code.ShouldBe(SpatialConditions.AnchorNotFoundCode);
        (await SurfaceRefusalAsync(
            viewer, $"/api/v1/caves?sort=proximity&filter={Nearest(new ObjectAnchor("feature", guarded))}")).ShouldBe(missing);
        (await SurfaceRefusalAsync(
            viewer, $"/api/v1/caves?sort=proximity&filter={Nearest(new ObjectAnchor("feature", guardedCave))}")).ShouldBe(missing);
    }

    /// <summary>
    /// The radius attack on one anchor: a radius crossing only the protected entrance's true
    /// position changes nothing for the viewer, one crossing its shown point does, the negated form
    /// is decided on the shown point, and the owner — who places it — is the control.
    /// </summary>
    private async Task AssertMeasuredByShownPointAsync(
        Spot spot, Guid entranceId, PointValue anchorPoint, Func<double, FilterNode> within)
    {
        var toTrue = Geodesy.DistanceMeters(anchorPoint.Longitude, anchorPoint.Latitude, spot.TrueLon, spot.TrueLat);
        var toShown = Geodesy.DistanceMeters(anchorPoint.Longitude, anchorPoint.Latitude, spot.SnappedLon, spot.SnappedLat);
        toShown.ShouldBeGreaterThan(toTrue + 500, "the fixture must put the two distances well apart");

        (await IdsAsync(viewer, within(toTrue + 100))).ShouldNotContain(entranceId, "the radius reached only the true position");
        (await IdsAsync(viewer, within(toTrue - 100))).ShouldNotContain(entranceId);
        (await IdsAsync(viewer, within(toShown + 100))).ShouldContain(entranceId);

        FilterNode NearButNot(double radius) => new AllOfNode(
        [
            Within(20_000, anchorPoint),
            new NotNode(within(radius)),
        ]);
        (await IdsAsync(viewer, NearButNot(toTrue + 100))).ShouldContain(entranceId, "negated, it is decided on the shown point");
        (await IdsAsync(viewer, NearButNot(toTrue - 100))).ShouldContain(entranceId);
        (await IdsAsync(viewer, NearButNot(toShown + 100))).ShouldNotContain(entranceId);

        (await IdsAsync(owner, within(toTrue + 100))).ShouldContain(entranceId);
        (await IdsAsync(owner, within(toTrue - 100))).ShouldNotContain(entranceId);
        (await IdsAsync(owner, NearButNot(toTrue + 100))).ShouldNotContain(entranceId);
    }

    private sealed class EdgePoint
    {
        public double X { get; set; }

        public double Y { get; set; }
    }

    private static async Task<List<string?>> CaveNamesAsync(HttpClient client, string token)
    {
        var response = await client.GetAsync($"/api/v1/caves?filter={token}&pageSize=100");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return [.. JsonNode.Parse(payload)!["items"]!.AsArray().Select(i => i!["name"]?.GetValue<string>())];
    }

    private static async Task<List<string?>> ExportedNamesAsync(HttpClient client, string token)
    {
        var response = await client.GetAsync($"/api/v1/export/caves?format=geojson&filter={token}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return [.. JsonNode.Parse(payload)!["features"]!.AsArray().Select(f => f!["properties"]!["name"]?.GetValue<string>())];
    }

    private static async Task<Refusal> SurfaceRefusalAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, payload);
        var body = JsonNode.Parse(payload)!;
        return new Refusal((int)response.StatusCode, body["code"]?.GetValue<string>(), body["detail"]?.GetValue<string>());
    }

    // ---------- the proximity sort ----------

    [Fact]
    public async Task Nearest_first_ranks_only_rows_the_reader_may_place_and_never_moves_one_by_its_true_or_shown_position()
    {
        var spot = Spot.Pick();
        // The probe sits at the protected entrance's true position. Its shown point is about a third
        // of a cell from there, so a build ranking by the shown point would put it before the open
        // entrance at six tenths of a cell; one ranking by the true position would put it first.
        var (guardedCave, guarded) = await CaveWithEntranceAsync("Rank guarded", spot.TrueLon, spot.TrueLat, locationProtected: true);
        var (nearCave, near) = await CaveWithEntranceAsync("Rank near", spot.TrueLon - (0.6 * Cell), spot.TrueLat, locationProtected: false);
        var (farCave, far) = await CaveWithEntranceAsync("Rank far", spot.TrueLon - (1.2 * Cell), spot.TrueLat, locationProtected: false);

        var here = new PointValue(spot.TrueLon, spot.TrueLat);
        var bounded = Within(20_000, here);
        var atGuarded = new PointAnchor(spot.TrueLon, spot.TrueLat);
        var farWest = new PointAnchor(spot.TrueLon - (3 * Cell), spot.TrueLat);

        var fromGuarded = await OrderedAsync(viewer, bounded, atGuarded);
        var fromWest = await OrderedAsync(viewer, bounded, farWest);

        fromGuarded.Count.ShouldBe(6);
        // A cave is ranked with its entrance, so each pair shares a distance and is told apart by
        // the fallback order alone.
        fromGuarded.Take(2).Select(h => h.Id).ShouldBe([near, nearCave], ignoreOrder: true);
        fromGuarded.Skip(2).Take(2).Select(h => h.Id).ShouldBe([far, farCave], ignoreOrder: true);
        fromWest.Take(2).Select(h => h.Id).ShouldBe([far, farCave], ignoreOrder: true);
        fromWest.Skip(2).Take(2).Select(h => h.Id).ShouldBe([near, nearCave], ignoreOrder: true);

        // The rows the viewer may not place come after every ranked row, from both anchors, in the
        // same order — wherever the anchor is, they do not move.
        fromGuarded.Skip(4).Select(h => h.Id).ShouldBe([.. fromWest.Skip(4).Select(h => h.Id)]);
        fromGuarded.Skip(4).Select(h => h.Id).ShouldBe([guarded, guardedCave], ignoreOrder: true);
        fromGuarded.Take(4).ShouldAllBe(h => h.Placeable);
        fromGuarded.Skip(4).ShouldAllBe(h => !h.Placeable);

        // The control: the owner places it exactly, so for them it is nearest from its own spot.
        var ownerFromGuarded = await OrderedAsync(owner, bounded, atGuarded);
        ownerFromGuarded.Take(2).Select(h => h.Id).ShouldBe([guarded, guardedCave], ignoreOrder: true);
        (await OrderedAsync(owner, bounded, farWest)).Skip(4).Select(h => h.Id)
            .ShouldBe([guarded, guardedCave], ignoreOrder: true);
    }

    [Fact]
    public async Task Nearest_first_from_an_object_the_reader_may_not_place_is_refused_exactly_like_a_missing_one()
    {
        var spot = Spot.Pick();
        var (guardedCave, guarded) = await CaveWithEntranceAsync("From guarded", spot.TrueLon, spot.TrueLat, locationProtected: true);
        var (openCave, open) = await CaveWithEntranceAsync("From open", spot.TrueLon - (0.6 * Cell), spot.TrueLat, locationProtected: false);
        var bounded = Within(20_000, new PointValue(spot.TrueLon, spot.TrueLat));
        await AssertSnappedForViewerAsync(guardedCave, spot.TrueLon);

        var unplaceable = await RefusalAsync(viewer, Sorted(bounded, new ObjectAnchor("feature", guarded)));
        var missing = await RefusalAsync(viewer, Sorted(bounded, new ObjectAnchor("feature", Guid.NewGuid())));
        unplaceable.Code.ShouldBe(SpatialConditions.AnchorNotFoundCode);
        unplaceable.ShouldBe(missing, "a withheld anchor must read exactly like one that does not exist");
        // A cave is placed by its entrances, and the viewer may place none of this one's.
        (await RefusalAsync(viewer, Sorted(bounded, new ObjectAnchor("feature", guardedCave)))).ShouldBe(missing);

        // The control: objects the reader may place work, a cave by its entrances, and the owner
        // measures from their own guarded cave.
        (await OrderedAsync(viewer, bounded, new ObjectAnchor("feature", openCave))).Take(2).Select(h => h.Id)
            .ShouldBe([open, openCave], ignoreOrder: true);
        (await OrderedAsync(viewer, bounded, new ObjectAnchor("feature", open))).Take(2).Select(h => h.Id)
            .ShouldBe([open, openCave], ignoreOrder: true);
        (await OrderedAsync(owner, bounded, new ObjectAnchor("feature", guardedCave))).Take(2).Select(h => h.Id)
            .ShouldBe([guarded, guardedCave], ignoreOrder: true);
    }

    [Fact]
    public async Task Nearest_first_with_nothing_to_measure_from_is_refused_and_a_saved_filter_may_ask_for_it_without_one()
    {
        var spot = Spot.Pick();
        var bounded = Within(20_000, new PointValue(spot.TrueLon, spot.TrueLat));

        var bare = await RefusalAsync(viewer, About(bounded) with { Sort = SortKey.Proximity });
        bare.Code.ShouldBe("filter.invalid");
        (await RefusalAsync(viewer, Sorted(bounded, new ObjectAnchor("tripLog", Guid.NewGuid())))).Code
            .ShouldBe("filter.invalid");
        (await RefusalAsync(viewer, Sorted(bounded, new PointAnchor(200, 0)))).Code.ShouldBe("filter.invalid");

        // Stored, the order is kept and the place is not: whoever runs it supplies one.
        var stored = await owner.PostAsJsonAsync(
            "/api/v1/saved-filters",
            new SavedFilterWriteRequest(
                $"Nearest {tag}", null, About(Within(500, new ObjectValue("feature", Guid.NewGuid()))) with { Sort = SortKey.Proximity }, null),
            Wire);
        stored.StatusCode.ShouldBe(HttpStatusCode.Created, await stored.Content.ReadAsStringAsync());
        var anchored = await owner.PostAsJsonAsync(
            "/api/v1/saved-filters",
            new SavedFilterWriteRequest($"Nearest here {tag}", null, Sorted(bounded, new PointAnchor(spot.TrueLon, spot.TrueLat)), null),
            Wire);
        anchored.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await anchored.Content.ReadAsStringAsync()).ShouldContain(SavedFilterRules.AnchorRefusedCode);
    }

    // ---------- helpers ----------

    /// <summary>A spot of this test's own, with a true position three tenths of a cell off its lattice point.</summary>
    private sealed record Spot(double TrueLon, double TrueLat, double SnappedLon, double SnappedLat)
    {
        public static Spot Pick()
        {
            // Spread over a wide, otherwise unused stretch of the globe so tests never meet.
            var nearLon = 100 + (Random.Shared.NextDouble() * 60);
            var nearLat = -45 + (Random.Shared.NextDouble() * 20);
            var snappedLon = Math.Round(nearLon / Cell, MidpointRounding.AwayFromZero) * Cell;
            var snappedLat = Math.Round(nearLat / Cell, MidpointRounding.AwayFromZero) * Cell;
            return new Spot(snappedLon - (0.3 * Cell), snappedLat + (0.1 * Cell), snappedLon, snappedLat);
        }
    }

    private static ConditionNode Within(double radius, FilterValue anchor) =>
        new(FeatureFilterFields.Position, FilterOp.Within, [new NumberValue(radius), anchor]);

    private static ConditionNode Inside(FilterValue outline) =>
        new(FeatureFilterFields.Position, FilterOp.Inside, [outline]);

    private static PolygonValue Square(double lon, double lat, double half) => new([Ring(lon, lat, half)]);

    private static double[][] Ring(double lon, double lat, double half) =>
    [
        [lon - half, lat - half],
        [lon + half, lat - half],
        [lon + half, lat + half],
        [lon - half, lat + half],
        [lon - half, lat - half],
    ];

    /// <summary>
    /// A page of nearest first is ranked among the rows inside the reach of the page's nearest
    /// readable, placeable rows only, when enough of the question's own rows lie inside it, and from
    /// every row otherwise. Read page by page — the first pages from inside the reach, the last,
    /// which holds the rows the reader may not place, from the whole ranking — the order must be
    /// the one a single page holding every row gives.
    /// </summary>
    [Fact]
    public async Task Nearest_first_page_by_page_is_the_order_of_one_page_holding_every_row()
    {
        var spot = Spot.Pick();
        _ = await CaveWithEntranceAsync("Paged guarded", spot.TrueLon, spot.TrueLat, locationProtected: true);
        for (var i = 1; i <= 5; i++)
        {
            _ = await CaveWithEntranceAsync($"Paged open {i}", spot.TrueLon - (0.4 * i * Cell), spot.TrueLat, locationProtected: false);
        }

        var bounded = Within(20_000, new PointValue(spot.TrueLon, spot.TrueLat));
        var anchor = new PointAnchor(spot.TrueLon, spot.TrueLat);

        // The reach the first pages are taken from exists for the viewer here — so the pages below do
        // read from inside it rather than all falling back to the whole ranking.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var viewerId = await db.Users.Where(u => u.Email == $"sp-view-{tag}@t.local").Select(u => u.Id).SingleAsync();
            var ctx = await AccessContextResolver.ResolveAsync(db, viewerId);
            (await ProximityOrderSql.ReachOfNearestAsync(
                    db, ctx, new NetTopologySuite.Geometries.Point(anchor.Longitude, anchor.Latitude) { SRID = 4326 }, 2, default))
                .ShouldNotBeNull("the viewer must read and place two rows near the anchor");
        }

        foreach (var client in new[] { viewer, owner })
        {
            var whole = await OrderedAsync(client, bounded, anchor);
            whole.Count.ShouldBe(12);
            var paged = new List<Ranked>();
            for (var page = 1; page <= 6; page++)
            {
                var response = await client.PostAsJsonAsync(
                    "/api/v1/filters/query",
                    new FilterQueryRequest(Sorted(bounded, anchor), Page: page, PageSize: 2),
                    Wire);
                var payload = await response.Content.ReadAsStringAsync();
                response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
                paged.AddRange(JsonNode.Parse(payload)!["worlds"]![0]!["hits"]!.AsArray()
                    .Select(h => new Ranked(h!["id"]!.GetValue<Guid>(), h["placeable"]!.GetValue<bool>())));
            }

            paged.ShouldBe(whole);
        }

        // For the viewer the guarded pair is unplaceable and comes last, so the last page could not
        // be answered from inside any reach; for the owner it is nearest and comes first.
        (await OrderedAsync(viewer, bounded, anchor)).Skip(10).ShouldAllBe(h => !h.Placeable);
        (await OrderedAsync(owner, bounded, anchor)).ShouldAllBe(h => h.Placeable);
    }

    private static FilterDocument About(FilterNode where) => new() { Scope = [new WorldScope("feature", where)] };

    private static async Task<HashSet<Guid>> IdsAsync(HttpClient client, FilterNode where, string? nameContains = null)
    {
        var tree = nameContains is null
            ? where
            : new AllOfNode([where, new ConditionNode(FeatureFilterFields.Name, FilterOp.Contains, [new TextValue(nameContains)])]);
        var response = await client.PostAsJsonAsync(
            "/api/v1/filters/query", new FilterQueryRequest(About(tree), PageSize: 100), Wire);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        var body = JsonNode.Parse(payload)!;
        return [.. body["worlds"]![0]!["hits"]!.AsArray().Select(h => h!["id"]!.GetValue<Guid>())];
    }

    private static FilterDocument Sorted(FilterNode where, FilterAnchor anchor) =>
        About(where) with { Sort = SortKey.Proximity, Anchor = anchor };

    private sealed record Ranked(Guid Id, bool Placeable);

    private static async Task<List<Ranked>> OrderedAsync(HttpClient client, FilterNode where, FilterAnchor anchor)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/filters/query", new FilterQueryRequest(Sorted(where, anchor), PageSize: 100), Wire);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return
        [
            .. JsonNode.Parse(payload)!["worlds"]![0]!["hits"]!.AsArray()
                .Select(h => new Ranked(h!["id"]!.GetValue<Guid>(), h["placeable"]!.GetValue<bool>())),
        ];
    }

    private sealed record Refusal(int Status, string? Code, string? Detail);

    private static Task<Refusal> RefusalAsync(HttpClient client, FilterNode where) => RefusalAsync(client, About(where));

    private static async Task<Refusal> RefusalAsync(HttpClient client, FilterDocument document)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/filters/query", new FilterQueryRequest(document), Wire);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, payload);
        var body = JsonNode.Parse(payload)!;
        return new Refusal((int)response.StatusCode, body["code"]?.GetValue<string>(), body["detail"]?.GetValue<string>());
    }

    private Task<Guid> SharedAsync(string name, FilterNode where) => SaveAsync(owner, name, where, clubId);

    private static async Task<Guid> SaveAsync(HttpClient client, string name, FilterNode where, Guid? club = null)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/saved-filters",
            new SavedFilterWriteRequest(
                name, null, About(where), club, club is null ? Visibility.Private : Visibility.CavingGroup),
            Wire);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonNode.Parse(payload)!["id"]!.GetValue<Guid>();
    }

    private async Task<(Guid CaveId, Guid EntranceId)> CaveWithEntranceAsync(
        string name, double lon, double lat, bool locationProtected, string? entranceName = null)
    {
        var cave = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"{name} {tag}",
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
            name = entranceName,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            positionQuality = "Gps",
        });
        var entrancePayload = await entrance.Content.ReadAsStringAsync();
        entrance.StatusCode.ShouldBe(HttpStatusCode.Created, entrancePayload);
        return (caveId, JsonNode.Parse(entrancePayload)!["id"]!.GetValue<Guid>());
    }

    private async Task<Guid> GenericAsync(string name, object geometry, bool locationProtected = true)
    {
        long typeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            typeId = await db.FeatureTypes.Where(t => t.Code == "sinkhole").Select(t => t.Id).SingleAsync();
        }

        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"{name} {tag}",
            featureTypeId = typeId,
            geometry,
            visibility = "authenticated",
            locationProtected,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonNode.Parse(payload)!["id"]!.GetValue<Guid>();
    }

    private async Task AssertSnappedForViewerAsync(Guid caveId, double trueLon)
    {
        var entrances = (await viewer.GetFromJsonAsync<JsonArray>($"/api/v1/caves/{caveId}/entrances"))!;
        entrances.Count.ShouldBe(1);
        entrances[0]!["approximateLocation"]!.GetValue<bool>().ShouldBeTrue();
        entrances[0]!["geom"]!["coordinates"]![0]!.GetValue<double>().ShouldNotBe(trueLon);
    }

    private static JsonSerializerOptions BuildWire()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
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
