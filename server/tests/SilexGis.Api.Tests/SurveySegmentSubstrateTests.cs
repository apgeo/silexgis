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
using SilexGis.Infrastructure.Surveys;
using Therion.Blender;
using Therion.Blender.Parsing;

namespace SilexGis.Api.Tests;

/// <summary>
/// The substrate every cave survey statistic is computed over: one row shape, produced either
/// from the legs of a parsed survey file or — for a cave whose line work never went through a
/// survey program — from a shape-based reduction of its centerline.
///
/// <para>
/// What these tests are for is that the two producers agree on <i>how</i> they measure (metres
/// and true degrees, asked of the spheroid, so no projected system is chosen) while never
/// pretending to be each other: every row says which body of line work it came out of, wall
/// shots and surface legs leave by the file's own flag, a duplicated leg stays as a row and
/// leaves the length sum, and a centerline drawn without altitudes refuses the vertical numbers
/// rather than reporting a cave flat at sea level.
/// </para>
/// </summary>
public sealed class SurveySegmentSubstrateTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private Guid ownerId;
    private Guid viewerId;
    private long caveTypeId;

    public SurveySegmentSubstrateTests(PostgresFixture postgres)
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
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"seg-own-{suffix}@t.local");
        viewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"seg-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"seg-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"seg-read-{suffix}@t.local");
    }

    [Fact]
    public async Task Survey_legs_are_measured_in_metres_and_true_degrees_and_the_files_flags_decide_what_counts()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        var modelId = await UploadLocalLoxAsync(caveId, FlaggedSurvey());
        await RunQueuedGraphJobAsync(modelId);

        var set = await SegmentsAsync(ownerId, caveId);

        set.Basis.ShouldBe(SurveySegmentBasis.SurveyFlags);
        set.HasZ.ShouldBeTrue();

        // Five legs went in; the wall shot and the surface leg leave because the file says what
        // they are, and the duplicated leg stays because it is real passage.
        set.Segments.Count.ShouldBe(3);
        set.Segments.ShouldAllBe(s => s.Basis == SurveySegmentBasis.SurveyFlags);
        set.Segments.ShouldAllBe(s => s.ShotId != null);
        set.Segments.ShouldAllBe(s => s.PathIndex == null);

        var names = set.Segments
            .Select(s => $"{s.FromStationName}-{s.ToStationName}")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        names.ShouldBe(["A-B", "B-C", "B-E"], ignoreOrder: true);

        // A hundred metres due east and ten metres up. Degrees of longitude are not metres and a
        // bearing read off raw coordinate differences would be out by the cosine of the latitude
        // here — around ten degrees — so these two numbers are what proves the measuring is being
        // done on the spheroid and not on the stored coordinates.
        var east = set.Segments.Single(s => s.ToStationName == "B");
        east.PlanLengthM.ShouldBeInRange(99, 101);
        east.AzimuthDegrees.ShouldNotBeNull().ShouldBeInRange(89.5, 90.5);
        east.DeltaZM.ShouldNotBeNull().ShouldBeInRange(9.9, 10.1);
        east.DipDegrees.ShouldNotBeNull().ShouldBeInRange(5.4, 6.0);
        east.SlopeLengthM.ShouldNotBeNull().ShouldBeInRange(100.2, 100.8);
        east.IsDuplicate.ShouldBeFalse();

        // The file states its own leg length exactly, and projecting the ends into longitude and
        // latitude does not preserve it — so it is carried rather than recomputed away.
        east.FileLengthM.ShouldNotBeNull().ShouldBeInRange(100.2, 100.8);

        // A hundred metres due north and level: azimuth zero, no dip, not a wrapped 360.
        var north = set.Segments.Single(s => s.ToStationName == "C");
        north.AzimuthDegrees.ShouldNotBeNull().ShouldBeInRange(-0.5, 0.5);
        north.DipDegrees.ShouldNotBeNull().ShouldBeInRange(-0.1, 0.1);

        // Midpoint altitudes, asserted as the difference between two of them so the check does not
        // depend on what height the upload declared its zero point to sit at: A is at the floor,
        // B and C are ten metres above it, so the two midpoints are five metres apart.
        (north.MidZM!.Value - east.MidZM!.Value).ShouldBe(5, tolerance: 0.1);

        // The duplicated leg is a row, and it is not in the length. Both halves matter: dropping
        // the row would lose passage the topology needs, and keeping it in the sum would count a
        // hundred metres of passage twice.
        var duplicate = set.Segments.Single(s => s.ToStationName == "E");
        duplicate.IsDuplicate.ShouldBeTrue();
        duplicate.PlanLengthM.ShouldBeInRange(99, 101);

        var counted = SurveySegments.SlopeLengthSumM(set.Segments);
        var countedTwice = set.Segments.Sum(s => s.SlopeLengthM ?? s.PlanLengthM);
        counted.ShouldBeInRange(199, 202);
        countedTwice.ShouldBeInRange(299, 303);
    }

    [Fact]
    public async Task A_cave_with_only_a_drawn_centerline_is_measured_from_the_reduction_and_says_so()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        await UploadCenterlineAsync(caveId, "traverse.geojson", SplayedTraverse(withAltitudes: true));

        var set = await SegmentsAsync(ownerId, caveId);

        // No survey file was ever parsed for this cave, so the answer is the documented
        // approximation — and it is labelled as one on the set and on every row, which is the only
        // thing that stops this being compared with a flag-based rose as if they were the same
        // measurement.
        set.Basis.ShouldBe(SurveySegmentBasis.SkeletonHeuristic);
        set.Segments.ShouldAllBe(s => s.Basis == SurveySegmentBasis.SkeletonHeuristic);
        set.Segments.ShouldAllBe(s => s.ShotId == null && s.FromStationName == null);
        set.Segments.ShouldAllBe(s => !s.IsDuplicate);

        // The fan of two loose lines at the far station is read as wall shots and goes; the
        // traverse survives as one polyline of two pieces, in order.
        set.Segments.Count.ShouldBe(2);
        set.Segments.ShouldAllBe(s => s.PathIndex == 0);
        set.Segments.Select(s => s.SegmentIndex).ShouldBe([0, 1]);

        set.HasZ.ShouldBeTrue();
        var first = set.Segments[0];
        first.PlanLengthM.ShouldBeInRange(76, 80);
        first.AzimuthDegrees.ShouldNotBeNull().ShouldBeInRange(89.5, 90.5);
        first.DeltaZM.ShouldNotBeNull().ShouldBeInRange(9.9, 10.1);
        first.DipDegrees.ShouldNotBeNull().ShouldBeInRange(6.9, 7.7);
        first.MidZM.ShouldNotBeNull().ShouldBeInRange(1004.9, 1005.1);
        set.Segments[1].MidZM.ShouldNotBeNull().ShouldBeInRange(1014.9, 1015.1);

        SurveySegments.PlanLengthSumM(set.Segments).ShouldBeInRange(153, 160);
    }

    [Fact]
    public async Task A_centerline_drawn_without_altitudes_refuses_the_vertical_numbers_rather_than_reporting_zero()
    {
        var flat = await CreateCaveAsync(locationProtected: false);
        await UploadCenterlineAsync(flat, "plan.geojson", SplayedTraverse(withAltitudes: false));

        var set = await SegmentsAsync(ownerId, flat);

        // The reduction fills a missing altitude with zero so that what it produces can be stored
        // and drawn. Reading that back as fact would report a plan drawing as a perfectly flat
        // cave at sea level, with total confidence and no way for a reader to tell.
        set.Basis.ShouldBe(SurveySegmentBasis.SkeletonHeuristic);
        set.HasZ.ShouldBeFalse();
        set.Segments.ShouldAllBe(s => !s.HasZ);
        set.Segments.ShouldAllBe(s => s.DeltaZM == null && s.SlopeLengthM == null);
        set.Segments.ShouldAllBe(s => s.DipDegrees == null && s.MidZM == null);

        // What survives without altitudes still survives: the plan measurements are unaffected.
        set.Segments.Count.ShouldBe(2);
        set.Segments[0].PlanLengthM.ShouldBeInRange(76, 80);
        set.Segments[0].AzimuthDegrees.ShouldNotBeNull().ShouldBeInRange(89.5, 90.5);

        // And the length a plan-only cave can honestly claim is its plan length, not nothing.
        SurveySegments.SlopeLengthSumM(set.Segments)
            .ShouldBe(SurveySegments.PlanLengthSumM(set.Segments), tolerance: 0.001);
    }

    [Fact]
    public async Task A_parsed_survey_answers_in_place_of_the_reduction_when_the_cave_has_both()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        await UploadCenterlineAsync(caveId, "drawn.geojson", SplayedTraverse(withAltitudes: true));

        var drawn = await SegmentsAsync(ownerId, caveId);
        drawn.Basis.ShouldBe(SurveySegmentBasis.SkeletonHeuristic);

        // Reading a survey file gives the cave a second centerline, extracted from the legs. From
        // that point the flags exist, so the guess must stop answering — otherwise the same cave
        // reports one distribution today and a different one tomorrow with nothing to say why.
        var modelId = await UploadLocalLoxAsync(caveId, FlaggedSurvey());
        await RunQueuedGraphJobAsync(modelId);

        var parsed = await SegmentsAsync(ownerId, caveId);
        parsed.Basis.ShouldBe(SurveySegmentBasis.SurveyFlags);
        parsed.Segments.ShouldAllBe(s => s.ShotId != null);
    }

    [Fact]
    public async Task A_cave_holding_two_uploaded_surveys_is_measured_over_one_of_them_and_says_which()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);

        var first = await UploadLocalLoxAsync(caveId, FlaggedSurvey());
        await RunQueuedGraphJobAsync(first);

        var once = await SegmentsAsync(ownerId, caveId);
        once.Segments.Count.ShouldBe(3);
        once.SurveyModelId.ShouldBe(first);
        var lengthOfOne = SurveySegments.SlopeLengthSumM(once.Segments);

        // Uploads are immutable, so a corrected re-export of the same cave is a second model beside
        // the first rather than a replacement of it, and one cave held in both of the two formats
        // that are read is another. Each leaves its own complete set of legs against the one cave.
        var second = await UploadLocalLoxAsync(caveId, FlaggedSurvey());
        await RunQueuedGraphJobAsync(second);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.SurveyModels.CountAsync(m => m.CaveFeatureId == caveId))
                .ShouldBe(2, "the fixture must actually leave two models on the cave");
            (await db.SurveyShots.CountAsync(s => s.SurveyModelId == first || s.SurveyModelId == second))
                .ShouldBe(10, "and two complete sets of legs beneath them");
        }

        // Measuring over both would report this cave as twice the passage it is — which halves
        // every ratio of a length against an extent, and raises a disagreement against a declared
        // length that is exactly right.
        var again = await SegmentsAsync(ownerId, caveId);
        again.Basis.ShouldBe(SurveySegmentBasis.SurveyFlags);
        again.Segments.Count.ShouldBe(3);
        SurveySegments.SlopeLengthSumM(again.Segments).ShouldBe(lengthOfOne, tolerance: 0.001);

        // And which of the two answered is not left to be guessed: the cave's current shape was
        // read out of the first upload, so the first upload is what measures it.
        again.SurveyModelId.ShouldBe(first);
        again.Segments.ShouldAllBe(s => s.SurveyModelId != null);
    }

    [Fact]
    public async Task A_cave_the_caller_may_not_read_at_all_has_no_segments_from_either_producer()
    {
        // Private rather than location-protected: this is the read-visibility arm, a different
        // refusal from the exact-position one below. Both are spliced into both producers, and
        // each needs its own negative case or half the guard is untested.
        var caveId = await CreatePrivateCaveAsync();
        var modelId = await UploadLocalLoxAsync(caveId, FlaggedSurvey());
        await RunQueuedGraphJobAsync(modelId);
        await UploadCenterlineAsync(caveId, "drawn.geojson", SplayedTraverse(withAltitudes: true));

        // The Viewer cannot even read the cave.
        (await viewer.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var withheld = await SegmentsAsync(viewerId, caveId);
        withheld.Basis.ShouldBe(SurveySegmentBasis.Unavailable);
        withheld.Segments.ShouldBeEmpty();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var ctx = await RosterHelper.AccessContextOfAsync(db, viewerId);
        (await SurveySegmentSql.ForCaveAsync(db, ctx, caveId, CancellationToken.None)).ShouldBeEmpty();
        (await CenterlineSegmentSql.ForCaveAsync(db, ctx, caveId, CancellationToken.None)).ShouldBeEmpty();

        // The owner reaches both producers over the same rows, so the pair above is a refusal and
        // not an artefact of two queries that return nothing to anybody.
        var ownerCtx = await RosterHelper.AccessContextOfAsync(db, ownerId);
        (await SurveySegmentSql.ForCaveAsync(db, ownerCtx, caveId, CancellationToken.None))
            .ShouldNotBeEmpty();
        (await CenterlineSegmentSql.ForCaveAsync(db, ownerCtx, caveId, CancellationToken.None))
            .ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_centerline_whose_every_altitude_is_zero_is_read_as_carrying_none()
    {
        // The stored column is always three-dimensional — the upload path writes a zero where a
        // plan drawing said nothing — so the dimension of the geometry cannot answer whether there
        // are altitudes, and the values have to. This is the case where reading the values is
        // knowingly wrong: a genuine survey every station of which sits at exactly zero elevation
        // is refused its vertical numbers. For such a cave those numbers are all zero, so nothing
        // is lost; the other mistake — reporting a plan drawing as a flat cave at sea level, with
        // nothing to say it was a guess — is the one worth avoiding, and this pins which way the
        // trade was made.
        var atZero = await CreateCaveAsync(locationProtected: false);
        await UploadCenterlineAsync(atZero, "sealevel.geojson", TraverseAtAltitude(0));

        var set = await SegmentsAsync(ownerId, atZero);
        set.Basis.ShouldBe(SurveySegmentBasis.SkeletonHeuristic);
        set.HasZ.ShouldBeFalse();
        set.Segments.ShouldNotBeEmpty();
        set.Segments.ShouldAllBe(s => s.DipDegrees == null && s.MidZM == null);

        // The same traverse at any other single altitude is genuinely level and is reported as
        // such, so what is refused above is the zero and not a cave that happens not to climb.
        var raised = await CreateCaveAsync(locationProtected: false);
        await UploadCenterlineAsync(raised, "raised.geojson", TraverseAtAltitude(700));

        var level = await SegmentsAsync(ownerId, raised);
        level.HasZ.ShouldBeTrue();
        level.Segments.ShouldNotBeEmpty();
        level.Segments.ShouldAllBe(s => s.DipDegrees == 0);
        level.Segments.ShouldAllBe(s => s.MidZM == 700);
    }

    [Fact]
    public async Task A_viewer_who_may_read_a_cave_but_not_place_it_gets_no_segments_of_it()
    {
        var caveId = await CreateCaveAsync(locationProtected: true);
        var modelId = await UploadLocalLoxAsync(caveId, FlaggedSurvey());
        await RunQueuedGraphJobAsync(modelId);
        await UploadCenterlineAsync(caveId, "drawn.geojson", SplayedTraverse(withAltitudes: true));

        // The positive half, in the same test: the rows exist and the cave's owner measures them.
        var mine = await SegmentsAsync(ownerId, caveId);
        mine.Basis.ShouldBe(SurveySegmentBasis.SurveyFlags);
        mine.Segments.Count.ShouldBe(3);

        // A genuine Viewer with no grant of any kind. The cave is readable to them — the seeded
        // Viewer role reads authenticated features — and its legs are its exact position, so the
        // substrate has nothing for them from either producer.
        (await viewer.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var withheld = await SegmentsAsync(viewerId, caveId);
        withheld.Basis.ShouldBe(SurveySegmentBasis.Unavailable);
        withheld.Segments.ShouldBeEmpty();

        // Both producers refuse, not just the one that happened to answer first.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var ctx = await RosterHelper.AccessContextOfAsync(db, viewerId);
        (await SurveySegmentSql.ForCaveAsync(db, ctx, caveId, CancellationToken.None)).ShouldBeEmpty();
        (await CenterlineSegmentSql.ForCaveAsync(db, ctx, caveId, CancellationToken.None)).ShouldBeEmpty();

        // And a cave that does not exist answers the same way, so an empty set says nothing about
        // which caves are being kept from them.
        var absent = await SegmentsAsync(viewerId, Guid.NewGuid());
        absent.Basis.ShouldBe(SurveySegmentBasis.Unavailable);
    }

    private async Task<SurveySegmentSet> SegmentsAsync(Guid userId, Guid caveFeatureId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var ctx = await RosterHelper.AccessContextOfAsync(db, userId);
        return await SurveySegmentSource.ForCaveAsync(db, ctx, caveFeatureId, CancellationToken.None);
    }

    /// <summary>
    /// A survey whose flags carry every case the substrate has to tell apart: passage, a wall
    /// shot, a leg walked above ground, and a leg the surveyor marked as passage already measured
    /// on another trip. Positions are plain metres from the declared origin, so the expected
    /// bearings and lengths are known before the test runs: A-B is a hundred metres due east
    /// rising ten, B-C a hundred metres due north and level.
    /// </summary>
    private static byte[] FlaggedSurvey()
    {
        const uint loxSurfaceBit = 1;
        const uint loxDuplicateBit = 2;
        const uint loxSplayBit = 16;

        (uint Id, string Name, double X, double Y, double Z)[] stations =
        [
            (1, "A", 0, 0, 0),
            (2, "B", 100, 0, 10),
            (3, "C", 100, 100, 10),
            (4, "P", 100, 20, 0),
            (5, "S", 0, -50, 0),
            (6, "E", 200, 0, 10),
        ];

        (uint From, uint To, uint RawFlags)[] shots =
        [
            (1, 2, 0),
            (2, 3, 0),
            (2, 4, loxSplayBit),
            (1, 5, loxSurfaceBit),
            (2, 6, loxDuplicateBit),
        ];

        var model = new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox,
            Stations =
            [
                .. stations.Select(s => new CaveStation
                {
                    Id = s.Id,
                    Name = s.Name,
                    Position = new CaveVector3(s.X, s.Y, s.Z),
                }),
            ],
            Shots =
            [
                .. shots.Select(s => new CaveShot
                {
                    FromStationId = s.From,
                    ToStationId = s.To,
                    RawFlags = s.RawFlags,
                }),
            ],
        };

        return LoxWriter.Write(model);
    }

    /// <summary>
    /// A drawn centerline of the kind that carries no flags at all: a three-station traverse
    /// running due east and climbing, plus two loose lines off its far station — the shape a fan
    /// of wall shots makes, and the only evidence available when the file says nothing.
    /// </summary>
    private static byte[] SplayedTraverse(bool withAltitudes)
    {
        string Point(double lon, double lat, double z) => withAltitudes
            ? FormattableString.Invariant($"[{lon:R},{lat:R},{z:R}]")
            : FormattableString.Invariant($"[{lon:R},{lat:R}]");

        static string Line(params string[] points) =>
            "{\"type\":\"Feature\",\"properties\":{},\"geometry\":"
            + "{\"type\":\"LineString\",\"coordinates\":[" + string.Join(",", points) + "]}}";

        var a = Point(25.500, 45.500, 1000);
        var b = Point(25.501, 45.500, 1010);
        var c = Point(25.502, 45.500, 1020);
        var q = Point(25.5025, 45.5005, 1020);
        var r = Point(25.5025, 45.4995, 1020);

        return Encoding.UTF8.GetBytes(
            "{\"type\":\"FeatureCollection\",\"features\":["
            + string.Join(",", Line(a, b, c), Line(c, q), Line(c, r))
            + "]}");
    }

    private Task<Guid> CreateCaveAsync(bool locationProtected) =>
        CreateCaveAsync("authenticated", locationProtected);

    /// <summary>A cave nobody but its owner may read at all — the read-visibility refusal.</summary>
    private Task<Guid> CreatePrivateCaveAsync() => CreateCaveAsync("private", locationProtected: false);

    private async Task<Guid> CreateCaveAsync(string visibility, bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Segment Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// The same traverse as <see cref="SplayedTraverse"/>, every vertex of it at one stated
    /// altitude — so a fixture can put a genuine three-dimensional centerline at exactly zero.
    /// </summary>
    private static byte[] TraverseAtAltitude(double altitude)
    {
        string Point(double lon, double lat) =>
            FormattableString.Invariant($"[{lon:R},{lat:R},{altitude:R}]");

        static string Line(params string[] points) =>
            "{\"type\":\"Feature\",\"properties\":{},\"geometry\":"
            + "{\"type\":\"LineString\",\"coordinates\":[" + string.Join(",", points) + "]}}";

        var a = Point(25.500, 45.500);
        var b = Point(25.501, 45.500);
        var c = Point(25.502, 45.500);
        var q = Point(25.5025, 45.5005);
        var r = Point(25.5025, 45.4995);

        return Encoding.UTF8.GetBytes(
            "{\"type\":\"FeatureCollection\",\"features\":["
            + string.Join(",", Line(a, b, c), Line(c, q), Line(c, r))
            + "]}");
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
        form.Add(new StringContent("25.2"), "originLongitude");
        form.Add(new StringContent("45.5"), "originLatitude");
        form.Add(new StringContent("1200"), "originHeightM");

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
