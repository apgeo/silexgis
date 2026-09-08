// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using Therion.Blender;
using Therion.Blender.Parsing;

namespace SilexGis.Api.Tests;

/// <summary>
/// The read surfaces that answer what a cave's survey measures the cave to be — how long it is,
/// which way it runs, and what shape its network of passages has.
///
/// <para>
/// The load-bearing test in here is the three-way one. Everything this work rests on is the claim
/// that the substrate had to be the survey file's own per-leg flags — that neither of the two
/// things already stored would do, because the full survey geometry is mostly wall shots and the
/// stored skeleton is flat. That claim is asserted here rather than merely written down: one cave's
/// line work is measured through the flags, through the shape-based reduction, and raw; the first
/// two agree, and the third describes a different cave.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CaveSurveyStatisticsTests : IAsyncLifetime, IDisposable
{
    // A traverse of twelve fifty-metre legs running one way and climbing two metres a leg, with a
    // fan of six wall shots at every one of its thirteen stations. The proportions are the point:
    // by count the wall shots outnumber the passage six to one, and by length they outweigh it
    // nearly two to one, which is the shape a real survey export has and the reason a rose taken
    // over all of it measures the surveyor's habits instead of the cave.
    private const int TraverseLegs = 12;
    private const double LegMetres = 50d;
    private const double RisePerLegMetres = 2d;
    private const double SplayMetres = 15d;

    // Deliberately not a cardinal bearing, and deliberately not on a sector edge. A traverse
    // running due east measures 89.9999-something degrees rather than ninety, because a geodesic
    // between two points at the same latitude bulges towards the pole and its starting bearing is a
    // shade short of east — so it lands in the sector below the one anybody reading the fixture
    // would expect. That is real behaviour and not worth designing around; a fixture sitting on a
    // sector edge is what is worth avoiding.
    private const double TraverseBearing = 65d;

    // None of these shares the traverse's sector, so the raw distribution is separable from it.
    private static readonly double[] SplayBearings = [0, 25, 100, 130, 155, 175];

    // Metres per degree at the fixture's latitude, so the drawn centerline and the surveyed one
    // describe the same passage and their lengths are comparable.
    private const double OriginLongitude = 25.5;
    private const double OriginLatitude = 45.5;
    private const double MetresPerDegreeLatitude = 111132d;
    private const double MetresPerDegreeLongitude = 78025d;

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private HttpClient anonymous = null!;
    private long caveTypeId;

    public CaveSurveyStatisticsTests(PostgresFixture postgres)
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
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"cst-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cst-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"cst-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"cst-read-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    /// <summary>
    /// The test the substrate decision is worth: one body of line work, measured three ways.
    /// </summary>
    [Fact]
    public async Task One_caves_passage_measured_through_the_flags_and_through_the_reduction_agrees_and_measured_raw_does_not()
    {
        var surveyed = await CreateCaveAsync();
        var modelId = await UploadLocalLoxAsync(surveyed, SplayedSurveyFile());
        await RunQueuedGraphJobAsync(modelId);

        var drawn = await CreateCaveAsync();
        await UploadCenterlineAsync(drawn, "drawn.geojson", SplayedCenterlineFile());

        var byFlags = await OrientationAsync(owner, surveyed);
        var byReduction = await OrientationAsync(owner, drawn);

        // Each says which body of line work answered, and they are not the same one. That field is
        // the whole reason the two roses below may be put on one screen at all.
        byFlags.GetProperty("basis").GetString().ShouldBe("surveyFlags");
        byFlags.GetProperty("isApproximation").GetBoolean().ShouldBeFalse();
        byReduction.GetProperty("basis").GetString().ShouldBe("skeletonHeuristic");
        byReduction.GetProperty("isApproximation").GetBoolean().ShouldBeTrue();

        // Only the twelve passage legs survive either filter — the file's flags in one case, the
        // shape of the fans in the other — and both find all twelve.
        byFlags.GetProperty("segmentCount").GetInt32().ShouldBe(TraverseLegs);
        byReduction.GetProperty("segmentCount").GetInt32().ShouldBe(TraverseLegs);

        var flaggedLength = byFlags.GetProperty("totalLengthM").GetDouble();
        var reducedLength = byReduction.GetProperty("totalLengthM").GetDouble();

        // The reduction is a documented approximation, measured on real exports as retaining
        // 92–95% of the surveyed length because a dead end's last leg leaves with the fan at its
        // tip. This fixture puts a fan at every station including both ends, so nothing is lost to
        // that — and the band is asserted rather than equality, because the tolerance is the
        // contract and a future change that starts losing passage must fail here.
        (reducedLength / flaggedLength).ShouldBeInRange(0.92, 1.05);

        // Both describe the same cave: one run of passage, all of it in one sector.
        PassageFraction(byFlags).ShouldBeGreaterThan(0.99);
        PassageFraction(byReduction).ShouldBeGreaterThan(0.99);
        byFlags.GetProperty("byLength").GetProperty("meanAxisDegrees").GetDouble()
            .ShouldBeInRange(63, 67);
        byReduction.GetProperty("byLength").GetProperty("meanAxisDegrees").GetDouble()
            .ShouldBeInRange(63, 67);

        // And now the third way: the same cave's line work with nothing filtered out of it, read
        // back off the stored survey legs — every wall shot included, which is what the full
        // survey geometry is and what a rose drawn over it would measure.
        var raw = await RawOrientationOfStoredLegsAsync(modelId);

        raw.SampleCount.ShouldBe(TraverseLegs + ((TraverseLegs + 1) * SplayBearings.Length));

        var rawPassageSector = raw.Bins.Single(b => b.FromDegrees == 60).LengthFraction;
        rawPassageSector.ShouldBeLessThan(0.5);

        // Materially different, not marginally: the passage's own sector holds essentially all of
        // one distribution and a third of the other, and the wall shots put mass in sectors the
        // cave has no passage in at all.
        (PassageFraction(byFlags) - rawPassageSector).ShouldBeGreaterThan(0.4);
        raw.Bins.Single(b => b.FromDegrees == 0).LengthFraction.ShouldBeGreaterThan(0.08);
        raw.Bins.Single(b => b.FromDegrees == 20).LengthFraction.ShouldBeGreaterThan(0.08);

        // The concentration statistic tells the same story in one number: passage that all runs one
        // way is a resultant near one, and the raw line work is a fan.
        byFlags.GetProperty("byLength").GetProperty("resultantLength").GetDouble()
            .ShouldBeGreaterThan(0.99);
        raw.ByLength.ResultantLength.ShouldBeLessThan(0.5);
    }

    [Fact]
    public async Task A_surveyed_cave_reports_its_measured_shape_and_how_steeply_it_runs()
    {
        var caveId = await CreateCaveAsync();
        var modelId = await UploadLocalLoxAsync(caveId, SplayedSurveyFile());
        await RunQueuedGraphJobAsync(modelId);

        var stats = await StatisticsAsync(owner, caveId);
        stats.GetProperty("basis").GetString().ShouldBe("surveyFlags");

        // Which uploaded file was measured travels with the answer. A cave may hold several, only
        // one of them measures it, and two answers about one cave describe the same passage only
        // if this is the same in both.
        stats.GetProperty("surveyModelId").GetGuid().ShouldBe(modelId);

        // Both routes of the pair answer the altitude question in the same place, so a reader
        // deciding whether to draw the vertical half of a page looks in one place either way.
        stats.GetProperty("hasAltitudes").GetBoolean().ShouldBeTrue();

        var indices = stats.GetProperty("indices");
        indices.GetProperty("segmentCount").GetInt32().ShouldBe(TraverseLegs);
        indices.GetProperty("hasAltitudes").GetBoolean().ShouldBeTrue();

        // Twelve legs of fifty metres rising two: 600.5 m of passage, 600 m of it across the map,
        // 24 m of vertical range, and a single straight run so it covers all the ground it walks.
        indices.GetProperty("totalLengthM").GetDouble().ShouldBeInRange(596, 606);
        indices.GetProperty("planLengthM").GetDouble().ShouldBeInRange(595, 605);
        indices.GetProperty("verticalExtentM").GetDouble().ShouldBeInRange(23.5, 24.5);
        indices.GetProperty("horizontality").GetDouble().ShouldBeInRange(0.99, 1.0);
        indices.GetProperty("verticality").GetDouble().ShouldBeInRange(0.035, 0.045);
        indices.GetProperty("linearity").GetDouble().ShouldBeInRange(0.98, 1.0);
        indices.GetProperty("lengthToDepthRatio").GetDouble().ShouldBeInRange(24, 26);

        // The legs are a network of named stations, not an ordered path, so no sinuosity is
        // claimed for them rather than a wrong one being invented.
        indices.GetProperty("pathCount").GetInt32().ShouldBe(0);
        stats.GetProperty("paths").GetArrayLength().ShouldBe(0);

        var orientation = await OrientationAsync(owner, caveId);
        orientation.GetProperty("hasAltitudes").GetBoolean().ShouldBeTrue();
        orientation.GetProperty("surveyModelId").GetGuid().ShouldBe(modelId);
        orientation.GetProperty("bins").GetArrayLength().ShouldBe(18);

        // Fifty metres along and two up is a shade under two and a third degrees, every leg the
        // same, and every leg climbing — so the mean and the mean steepness are the same number.
        var dip = orientation.GetProperty("dip");
        dip.GetProperty("meanDipDegrees").GetDouble().ShouldBeInRange(2.1, 2.5);
        dip.GetProperty("meanAbsoluteDipDegrees").GetDouble().ShouldBeInRange(2.1, 2.5);
        // Every leg of this fixture climbs, so both extremes are climbs and both are positive.
        // That is only a readable answer because the fields are the least and the greatest
        // inclination; a "steepest descent" of +2.3 degrees for a cave that never descends would
        // be wrong on its face.
        dip.GetProperty("minimumDipDegrees").GetDouble().ShouldBeInRange(2.1, 2.5);
        dip.GetProperty("maximumDipDegrees").GetDouble().ShouldBeInRange(2.1, 2.5);
        dip.GetProperty("bins").GetArrayLength().ShouldBe(18);
    }

    [Fact]
    public async Task What_the_survey_measures_is_set_beside_what_the_record_claims_and_the_gap_is_named()
    {
        // A length nobody has revised since the cave was half explored, and a depth that is right.
        // A registry's typed-in morphometry is routinely older than the survey it is compared
        // against, so saying the pair disagrees is the feature — not deciding which is wrong.
        var caveId = await CreateCaveAsync(surveyedLength: 2500m, depth: 24m);
        var modelId = await UploadLocalLoxAsync(caveId, SplayedSurveyFile());
        await RunQueuedGraphJobAsync(modelId);

        var stats = await StatisticsAsync(owner, caveId);

        var length = stats.GetProperty("length");
        length.GetProperty("declaredM").GetDecimal().ShouldBe(2500m);
        length.GetProperty("computedM").GetDouble().ShouldBeInRange(596, 606);
        length.GetProperty("differenceM").GetDouble().ShouldBeGreaterThan(1800);
        length.GetProperty("agreement").GetString().ShouldBe("disagrees");

        var depth = stats.GetProperty("depth");
        depth.GetProperty("agreement").GetString().ShouldBe("agrees");

        stats.GetProperty("declaredDisagrees").GetBoolean().ShouldBeTrue();

        // The positive half: a record that matches its survey is not flagged, so the flag means
        // something when it appears.
        var agreeing = await CreateCaveAsync(surveyedLength: 600m, depth: 24m);
        var agreeingModel = await UploadLocalLoxAsync(agreeing, SplayedSurveyFile());
        await RunQueuedGraphJobAsync(agreeingModel);

        var matched = await StatisticsAsync(owner, agreeing);
        matched.GetProperty("length").GetProperty("agreement").GetString().ShouldBe("agrees");
        matched.GetProperty("declaredDisagrees").GetBoolean().ShouldBeFalse();

        // And a record with nothing typed in is not a disagreement either.
        var blank = await CreateCaveAsync();
        var blankModel = await UploadLocalLoxAsync(blank, SplayedSurveyFile());
        await RunQueuedGraphJobAsync(blankModel);

        var empty = await StatisticsAsync(owner, blank);
        empty.GetProperty("length").GetProperty("agreement").GetString().ShouldBe("notDeclared");
        empty.GetProperty("declaredDisagrees").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Line_work_drawn_without_altitudes_refuses_the_vertical_answers_rather_than_reporting_a_level_cave()
    {
        var caveId = await CreateCaveAsync(depth: 40m);
        await UploadCenterlineAsync(caveId, "plan.geojson", SplayedCenterlineFile(withAltitudes: false));

        var orientation = await OrientationAsync(owner, caveId);
        orientation.GetProperty("basis").GetString().ShouldBe("skeletonHeuristic");
        orientation.GetProperty("hasAltitudes").GetBoolean().ShouldBeFalse();

        // The reduction substitutes zero for a missing altitude so its output can be stored and
        // drawn. Summarising that would report a plan drawing as a cave level everywhere, with
        // total confidence and nothing in the answer to say otherwise.
        orientation.GetProperty("dip").ValueKind.ShouldBe(JsonValueKind.Null);

        // What survives without altitudes still survives, and says so.
        orientation.GetProperty("segmentCount").GetInt32().ShouldBe(TraverseLegs);
        PassageFraction(orientation).ShouldBeGreaterThan(0.99);

        var stats = await StatisticsAsync(owner, caveId);
        stats.GetProperty("hasAltitudes").GetBoolean().ShouldBeFalse();
        var indices = stats.GetProperty("indices");
        indices.GetProperty("hasAltitudes").GetBoolean().ShouldBeFalse();
        indices.GetProperty("verticalExtentM").ValueKind.ShouldBe(JsonValueKind.Null);
        indices.GetProperty("verticality").ValueKind.ShouldBe(JsonValueKind.Null);
        indices.GetProperty("planLengthM").GetDouble().ShouldBeInRange(590, 610);

        // A declared depth with nothing to compare it against is uncomparable, which is not the
        // same answer as agreement — and is not reported as a disagreement either.
        stats.GetProperty("depth").GetProperty("agreement").GetString().ShouldBe("notComputed");
        stats.GetProperty("declaredDisagrees").GetBoolean().ShouldBeFalse();

        // The drawn line work is one path, so this one does have a sinuosity: a straight run.
        indices.GetProperty("pathCount").GetInt32().ShouldBe(1);
        indices.GetProperty("sinuosity").GetDouble().ShouldBeInRange(0.99, 1.02);
        stats.GetProperty("paths").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task A_cave_with_no_line_work_at_all_says_so_rather_than_reporting_a_cave_of_no_length()
    {
        var caveId = await CreateCaveAsync(surveyedLength: 900m);

        var stats = await StatisticsAsync(owner, caveId);
        stats.GetProperty("basis").GetString().ShouldBe("unavailable");
        stats.GetProperty("indices").GetProperty("segmentCount").GetInt32().ShouldBe(0);

        // An unmeasured cave has an unknown length, not a length of zero, so its declared 900 m is
        // uncomparable rather than wrong by 900 m.
        stats.GetProperty("length").GetProperty("agreement").GetString().ShouldBe("notComputed");
        stats.GetProperty("declaredDisagrees").GetBoolean().ShouldBeFalse();

        var orientation = await OrientationAsync(owner, caveId);
        orientation.GetProperty("basis").GetString().ShouldBe("unavailable");
        orientation.GetProperty("segmentCount").GetInt32().ShouldBe(0);
        orientation.GetProperty("dip").ValueKind.ShouldBe(JsonValueKind.Null);

        // The rose still has all its sectors, so a reader drawing it has nothing to special-case.
        orientation.GetProperty("bins").GetArrayLength().ShouldBe(18);
    }

    [Fact]
    public async Task A_viewer_who_may_read_a_cave_but_not_place_it_is_told_there_is_no_such_cave()
    {
        var caveId = await CreateCaveAsync(locationProtected: true);
        var modelId = await UploadLocalLoxAsync(caveId, SplayedSurveyFile());
        await RunQueuedGraphJobAsync(modelId);

        // The positive half, in the same test: the numbers exist and the cave's owner reads them.
        var mine = await StatisticsAsync(owner, caveId);
        mine.GetProperty("basis").GetString().ShouldBe("surveyFlags");
        (await OrientationAsync(owner, caveId)).GetProperty("segmentCount").GetInt32()
            .ShouldBe(TraverseLegs);

        // A genuine Viewer with no grant of any kind, who may read the cave — the seeded Viewer
        // role reads authenticated features — but may not place it.
        (await viewer.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Not a 403. A refusal that distinguishes "you may not" from "there is nothing here" tells
        // somebody being kept from a hidden cave both that it exists and that it has been surveyed,
        // which is exactly what withholding its position is for.
        var stats = await viewer.GetAsync($"/api/v1/caves/{caveId}/statistics");
        stats.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(stats)).ShouldBe("cave.not_found");

        var orientation = await viewer.GetAsync($"/api/v1/caves/{caveId}/orientation");
        orientation.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(orientation)).ShouldBe("cave.not_found");

        // And a cave that was never created answers identically, so the refusal says nothing about
        // which caves are being kept from them.
        var absent = await viewer.GetAsync($"/api/v1/caves/{Guid.NewGuid()}/statistics");
        absent.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(absent)).ShouldBe("cave.not_found");

        // The shape of the network is behind the same door as the length and the bearings. It is
        // only a set of counts, but it was measured from a drawing of where the passages are, and
        // answering it to somebody kept from the position would say the cave exists and has been
        // surveyed.
        var mineTopology = await TopologyAsync(owner, caveId);
        mineTopology.GetProperty("nodeCount").GetInt32().ShouldBe(TraverseLegs + 1);

        var topology = await viewer.GetAsync($"/api/v1/caves/{caveId}/topology");
        topology.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(topology)).ShouldBe("cave.not_found");

        var absentTopology = await viewer.GetAsync($"/api/v1/caves/{Guid.NewGuid()}/topology");
        absentTopology.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(absentTopology)).ShouldBe("cave.not_found");
    }

    [Fact]
    public async Task A_cave_the_caller_may_not_read_answers_neither_route()
    {
        // A different refusal from the one above: there the Viewer could read the cave and only its
        // position was closed, here the cave itself is. Both routes filter on both, and each arm
        // needs a case of its own or half of the filtering is never exercised negatively.
        var caveId = await CreateCaveAsync(visibility: "private");
        var modelId = await UploadLocalLoxAsync(caveId, SplayedSurveyFile());
        await RunQueuedGraphJobAsync(modelId);

        // The owner's answer, so a route that answered nobody could not pass this.
        (await StatisticsAsync(owner, caveId)).GetProperty("basis").GetString().ShouldBe("surveyFlags");

        (await viewer.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var stats = await viewer.GetAsync($"/api/v1/caves/{caveId}/statistics");
        stats.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(stats)).ShouldBe("cave.not_found");

        var orientation = await viewer.GetAsync($"/api/v1/caves/{caveId}/orientation");
        orientation.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(orientation)).ShouldBe("cave.not_found");

        (await TopologyAsync(owner, caveId)).GetProperty("branchCount").GetInt32().ShouldBe(1);

        var topology = await viewer.GetAsync($"/api/v1/caves/{caveId}/topology");
        topology.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(topology)).ShouldBe("cave.not_found");
    }

    [Fact]
    public async Task Neither_route_answers_without_a_token_and_neither_accepts_a_cave_id_that_is_no_cave()
    {
        var caveId = await CreateCaveAsync();

        (await anonymous.GetAsync($"/api/v1/caves/{caveId}/statistics"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/api/v1/caves/{caveId}/orientation"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The route constraint accepts the all-zero guid as a well-formed one and no cave is ever
        // it, so it is refused as a malformed request rather than answered as a cave nobody may
        // see — the two failures are different and arrive looking different.
        var empty = await owner.GetAsync($"/api/v1/caves/{Guid.Empty}/orientation");
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Code(empty)).ShouldBe("validation.failed");

        var emptyStats = await owner.GetAsync($"/api/v1/caves/{Guid.Empty}/statistics");
        emptyStats.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Code(emptyStats)).ShouldBe("validation.failed");

        (await anonymous.GetAsync($"/api/v1/caves/{caveId}/topology"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var emptyTopology = await owner.GetAsync($"/api/v1/caves/{Guid.Empty}/topology");
        emptyTopology.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Code(emptyTopology)).ShouldBe("validation.failed");

        // A cave the caller may see whose network has never been measured says so in its own
        // words. Reporting it as a missing cave would say the cave had gone, and reporting a row
        // of zeroes would say it had been surveyed and found to have nothing.
        var unmeasured = await owner.GetAsync($"/api/v1/caves/{caveId}/topology");
        unmeasured.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(unmeasured)).ShouldBe("cave_topology.not_measured");
    }

    /// <summary>
    /// The figures are measured when the file is read, and they are read back with the two counts
    /// that say how much of the file became network. A reading that lost legs still produces
    /// entirely reasonable-looking figures, so the counts travel with them rather than being
    /// available separately to whoever thinks to ask.
    /// </summary>
    [Fact]
    public async Task The_measured_network_is_read_back_with_what_the_reading_dropped_and_merged()
    {
        var caveId = await CreateCaveAsync();
        var modelId = await UploadLocalLoxAsync(caveId, SplayedSurveyFile());
        await RunQueuedGraphJobAsync(modelId);

        var topology = await TopologyAsync(owner, caveId);

        topology.GetProperty("surveyModelId").GetGuid().ShouldBe(modelId);

        // The fixture is one traverse of twelve legs with wall shots hanging off every station.
        // The wall shots are not passage, so the network is the traverse and nothing else: one
        // branch between two dead ends, and no loop anywhere.
        topology.GetProperty("nodeCount").GetInt32().ShouldBe(TraverseLegs + 1);
        topology.GetProperty("edgeCount").GetInt32().ShouldBe(TraverseLegs);
        topology.GetProperty("componentCount").GetInt32().ShouldBe(1);
        topology.GetProperty("reducedNodeCount").GetInt32().ShouldBe(2);
        topology.GetProperty("reducedEdgeCount").GetInt32().ShouldBe(1);
        topology.GetProperty("cyclomaticNumber").GetInt32().ShouldBe(0);
        topology.GetProperty("branchCount").GetInt32().ShouldBe(1);
        topology.GetProperty("extremityCount").GetInt32().ShouldBe(2);
        topology.GetProperty("junctionCount").GetInt32().ShouldBe(0);

        // Present and a number, not merely present: null here would mean nobody had looked, which
        // is exactly the state these two counts exist to distinguish from "nothing was lost".
        topology.GetProperty("droppedShotCount").GetInt32().ShouldBe(0);
        topology.GetProperty("mergedStationCount").GetInt32().ShouldBe(0);
    }

    /// <summary>
    /// Reading the file again replaces the figures rather than leaving the previous reading's
    /// beside the new stations. A stored figure that outlives the reading it was computed from is
    /// worse than no figure, because nothing about it looks stale.
    /// </summary>
    [Fact]
    public async Task Reading_the_file_again_replaces_the_measured_network_rather_than_adding_to_it()
    {
        var caveId = await CreateCaveAsync();
        var modelId = await UploadLocalLoxAsync(caveId, SplayedSurveyFile());
        await RunQueuedGraphJobAsync(modelId);

        var first = (await TopologyAsync(owner, caveId)).GetProperty("computedAt").GetDateTime();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
                .Single(h => h.Kind == ProcessingJobKinds.SurveyGraph);
            var model = await db.SurveyModels.SingleAsync(m => m.Id == modelId);
            model.Status = SurveyModelStatus.Pending;
            await db.SaveChangesAsync();

            var job = (await db.ProcessingJobs
                    .Where(j => j.Kind == ProcessingJobKinds.SurveyGraph)
                    .ToListAsync())
                .Single(j => JsonSerializer
                    .Deserialize<SurveyGraphPayload>(j.Payload, JsonSerializerOptions.Web)
                    ?.SurveyModelId == modelId);

            await handler.ExecuteAsync(job, CancellationToken.None);

            (await db.SurveyTopologies.CountAsync(t => t.SurveyModelId == modelId)).ShouldBe(1);
        }

        var second = await TopologyAsync(owner, caveId);
        second.GetProperty("computedAt").GetDateTime().ShouldBeGreaterThanOrEqualTo(first);
        second.GetProperty("branchCount").GetInt32().ShouldBe(1);
    }

    private async Task<JsonElement> StatisticsAsync(HttpClient client, Guid caveId)
    {
        var response = await client.GetAsync($"/api/v1/caves/{caveId}/statistics");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> OrientationAsync(HttpClient client, Guid caveId)
    {
        var response = await client.GetAsync($"/api/v1/caves/{caveId}/orientation");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> TopologyAsync(HttpClient client, Guid caveId)
    {
        var response = await client.GetAsync($"/api/v1/caves/{caveId}/topology");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string?> Code(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    /// <summary>How much of the passage runs in the sector the fixture's traverse runs in.</summary>
    private static double PassageFraction(JsonElement orientation) =>
        orientation.GetProperty("bins").EnumerateArray()
            .Single(b => b.GetProperty("fromDegrees").GetDouble() == 60)
            .GetProperty("lengthFraction").GetDouble();

    /// <summary>
    /// The third way of measuring the same cave: every stored leg of the survey, wall shots
    /// included, which is what the full survey geometry holds and what a rose taken over it would
    /// be. Read out of the database rather than recomputed from the fixture's intentions, so this
    /// is a measurement of what was stored and not a restatement of what was uploaded.
    /// </summary>
    private async Task<OrientationSummary> RawOrientationOfStoredLegsAsync(Guid surveyModelId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var shots = await db.SurveyShots.AsNoTracking()
            .Where(s => s.SurveyModelId == surveyModelId)
            .ToListAsync();

        return OrientationStatistics.Summarize(shots.Select(s =>
        {
            var from = s.Geom.GetCoordinateN(0);
            var to = s.Geom.GetCoordinateN(1);

            // A bearing taken from raw coordinate differences is wrong by the cosine of the
            // latitude — around ten degrees here — so the eastward component is scaled before the
            // angle is taken. Good enough over legs of tens of metres, and this is the reference
            // side of the comparison rather than the answer under test.
            var meanLatitude = (from.Y + to.Y) / 2d * Math.PI / 180d;
            var east = (to.X - from.X) * Math.Cos(meanLatitude);
            var north = to.Y - from.Y;

            return new OrientationSample(Math.Atan2(east, north) * 180d / Math.PI, s.LengthM);
        }));
    }

    /// <summary>
    /// The fixture survey: a straight climbing traverse with a fan of flagged wall shots at every
    /// station. Positions are plain metres from the declared origin, so the lengths and bearings
    /// the test expects are known before it runs.
    /// </summary>
    private static byte[] SplayedSurveyFile()
    {
        const uint loxSplayBit = 16;

        var stations = new List<CaveStation>();
        var shots = new List<CaveShot>();
        uint nextId = 1;

        var stationIds = new uint[TraverseLegs + 1];
        for (var i = 0; i <= TraverseLegs; i++)
        {
            stationIds[i] = nextId++;
            stations.Add(new CaveStation
            {
                Id = stationIds[i],
                Name = $"T{i}",
                Position = new CaveVector3(
                    i * LegMetres * Math.Sin(TraverseBearing * Math.PI / 180d),
                    i * LegMetres * Math.Cos(TraverseBearing * Math.PI / 180d),
                    i * RisePerLegMetres),
            });

            if (i > 0)
            {
                shots.Add(new CaveShot
                {
                    FromStationId = stationIds[i - 1],
                    ToStationId = stationIds[i],
                    RawFlags = 0,
                });
            }
        }

        for (var i = 0; i <= TraverseLegs; i++)
        {
            foreach (var bearing in SplayBearings)
            {
                var radians = bearing * Math.PI / 180d;
                var id = nextId++;
                stations.Add(new CaveStation
                {
                    Id = id,
                    Name = $"W{i}_{bearing:F0}",
                    Position = new CaveVector3(
                        (i * LegMetres * Math.Sin(TraverseBearing * Math.PI / 180d))
                            + (SplayMetres * Math.Sin(radians)),
                        (i * LegMetres * Math.Cos(TraverseBearing * Math.PI / 180d))
                            + (SplayMetres * Math.Cos(radians)),
                        i * RisePerLegMetres),
                });
                shots.Add(new CaveShot
                {
                    FromStationId = stationIds[i],
                    ToStationId = id,
                    RawFlags = loxSplayBit,
                });
            }
        }

        return LoxWriter.Write(new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox,
            Stations = stations,
            Shots = shots,
        });
    }

    /// <summary>
    /// The same passage as a drawing that carries no flags at all: the traverse as one line, and
    /// every wall shot as a loose line of its own. The only evidence of what those loose lines are
    /// is their shape, which is what the reduction reads.
    /// </summary>
    private static byte[] SplayedCenterlineFile(bool withAltitudes = true)
    {
        string Point(double lon, double lat, double z) => withAltitudes
            ? FormattableString.Invariant($"[{lon:R},{lat:R},{z:R}]")
            : FormattableString.Invariant($"[{lon:R},{lat:R}]");

        static string Line(params string[] points) =>
            "{\"type\":\"Feature\",\"properties\":{},\"geometry\":"
            + "{\"type\":\"LineString\",\"coordinates\":[" + string.Join(",", points) + "]}}";

        var heading = TraverseBearing * Math.PI / 180d;
        double Longitude(int leg) =>
            OriginLongitude + (leg * LegMetres * Math.Sin(heading) / MetresPerDegreeLongitude);
        double Latitude(int leg) =>
            OriginLatitude + (leg * LegMetres * Math.Cos(heading) / MetresPerDegreeLatitude);
        double Altitude(int leg) => 1000d + (leg * RisePerLegMetres);

        var traverse = new List<string>();
        var splays = new List<string>();

        for (var i = 0; i <= TraverseLegs; i++)
        {
            var stationLon = Longitude(i);
            var stationLat = Latitude(i);
            traverse.Add(Point(stationLon, stationLat, Altitude(i)));

            foreach (var bearing in SplayBearings)
            {
                var radians = bearing * Math.PI / 180d;
                splays.Add(Line(
                    Point(stationLon, stationLat, Altitude(i)),
                    Point(
                        stationLon + (SplayMetres * Math.Sin(radians) / MetresPerDegreeLongitude),
                        stationLat + (SplayMetres * Math.Cos(radians) / MetresPerDegreeLatitude),
                        Altitude(i))));
            }
        }

        return Encoding.UTF8.GetBytes(
            "{\"type\":\"FeatureCollection\",\"features\":["
            + string.Join(",", [Line([.. traverse]), .. splays])
            + "]}");
    }

    private async Task<Guid> CreateCaveAsync(
        bool locationProtected = false,
        decimal? surveyedLength = null,
        decimal? depth = null,
        string visibility = "authenticated")
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Stats Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
            surveyedLength,
            depth,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task UploadCenterlineAsync(Guid caveId, string fileName, byte[] bytes)
    {
        using var form = BuildForm(fileName, bytes);
        var response = await owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", form);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Uploads a survey in plain metres with the position of its zero point declared.</summary>
    private async Task<Guid> UploadLocalLoxAsync(Guid caveId, byte[] bytes)
    {
        using var form = BuildForm("Local.lox", bytes);
        form.Add(new StringContent(
            OriginLongitude.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            "originLongitude");
        form.Add(new StringContent(
            OriginLatitude.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            "originLatitude");
        form.Add(new StringContent("1000"), "originHeightM");

        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static MultipartFormDataContent BuildForm(string fileName, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/octet-stream");
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    /// <summary>Runs what one upload queued, the way the background worker would.</summary>
    private async Task RunQueuedGraphJobAsync(Guid modelId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.SurveyGraph);

        var queued = await db.ProcessingJobs
            .Where(j => j.Kind == ProcessingJobKinds.SurveyGraph && j.Status == ProcessingJobStatus.Queued)
            .ToListAsync();

        // This model's job and no other: the queue is shared with every class in the collection,
        // and another class's file lives under a directory this host does not have.
        var mine = queued.Where(j =>
            JsonSerializer.Deserialize<SurveyGraphPayload>(j.Payload, JsonSerializerOptions.Web)
                ?.SurveyModelId == modelId).ToList();
        mine.ShouldHaveSingleItem();

        foreach (var job in mine)
        {
            job.Status = ProcessingJobStatus.Succeeded;
            await handler.ExecuteAsync(job, CancellationToken.None);
        }

        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
