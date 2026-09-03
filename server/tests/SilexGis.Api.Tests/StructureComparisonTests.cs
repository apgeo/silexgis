// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A cave's passage trends against the fracture traces mapped around it.
///
/// <para>
/// Two things are pinned here that a one-sided test would miss. The first is that the divergence
/// figure reaches both ends: a cave whose passage runs along the faults must score near nothing and
/// a cave whose passage runs across them must score most of the way up, because a measure wired up
/// wrongly still returns small numbers for roses that agree. The second is that a trace whose
/// position is closed to the reader is in no rose, no count and no divergence — a trace's bearing
/// places it as surely as a coordinate does once its shape is known, and there is no snapped form
/// of a bearing.
/// </para>
///
/// <para>
/// Each fixture sits at its own latitude, far enough from the others that nothing another test
/// seeded is within reach of it. Otherwise a suite that ran in a different order would be measuring
/// a different neighbourhood.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StructureComparisonTests : IAsyncLifetime, IDisposable
{
    /// <summary>
    /// The bearing every fixture's passage runs at. Chosen in the middle of a ten-degree sector so
    /// that the small difference between a bearing on the spheroid and one worked out on the flat
    /// cannot move it into the neighbouring sector and change what the histogram says.
    /// </summary>
    private const double PassageBearing = 65d;

    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private HttpClient anonymous = null!;
    private long caveTypeId;
    private long fractureTypeId;
    private long karstAreaTypeId;
    private long sinkholeTypeId;
    private long entranceTypeId;
    private long wallTypeId;
    private Guid viewerUserId;

    public StructureComparisonTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"str-own-{suffix}@t.local");
        viewerUserId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Viewer, $"str-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).FirstAsync();
            fractureTypeId = await db.FeatureTypes
                .Where(t => t.Code == FeatureTypeSeeds.FractureLine).Select(t => t.Id).FirstAsync();
            karstAreaTypeId = await db.FeatureTypes
                .Where(t => t.Code == "karst_area").Select(t => t.Id).FirstAsync();
            sinkholeTypeId = await db.FeatureTypes
                .Where(t => t.Code == "sinkhole").Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
            wallTypeId = await db.FeatureTypes
                .Where(t => t.Code == "wall").Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"str-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"str-read-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    [Fact]
    public async Task Passage_running_along_the_faults_barely_diverges_from_them()
    {
        const double latitude = 45.20;
        var cave = await CaveWithPassageAsync(latitude);
        await FaultAsync(latitude + 0.001, PassageBearing);
        await FaultAsync(latitude - 0.001, PassageBearing);

        var answer = await ReadAsync(owner, cave);

        answer.GetProperty("structureFeatureCount").GetInt32().ShouldBe(2);

        var divergence = answer.GetProperty("divergence");
        divergence.ValueKind.ShouldNotBe(JsonValueKind.Null);
        divergence.GetProperty("normalized").GetDouble().ShouldBeLessThan(0.05);
        divergence.GetProperty("meanAxisSeparationDegrees").GetDouble().ShouldBeLessThan(1d);
    }

    [Fact]
    public async Task Passage_running_across_the_faults_diverges_most_of_the_way()
    {
        // The same cave against the same traces turned through a right angle's half. The mean-axis
        // separation is the crisp half of the assertion because it is read off the bearings rather
        // than off the sectors, so it cannot be moved by a trend landing a hair either side of a
        // sector edge; the transport figure is asserted more loosely for exactly that reason.
        const double latitude = 45.30;
        var cave = await CaveWithPassageAsync(latitude);
        await FaultAsync(latitude + 0.001, PassageBearing + 45d);
        await FaultAsync(latitude - 0.001, PassageBearing + 45d);

        var answer = await ReadAsync(owner, cave);

        var divergence = answer.GetProperty("divergence");
        divergence.ValueKind.ShouldNotBe(JsonValueKind.Null);
        divergence.GetProperty("meanAxisSeparationDegrees").GetDouble().ShouldBeInRange(43d, 47d);
        divergence.GetProperty("normalized").GetDouble().ShouldBeGreaterThan(0.4);
    }

    /// <summary>
    /// A genuine Viewer holds no grant anywhere, so a trace marked as protected is one they may
    /// read and may not place. It must be in nothing they can observe.
    /// </summary>
    [Fact]
    public async Task A_trace_the_reader_may_not_place_is_in_no_rose_and_no_count()
    {
        const double latitude = 45.40;
        var cave = await CaveWithPassageAsync(latitude);

        // The positive half, for this same caller. Without it a route that answered nobody would
        // satisfy everything below.
        var open = await FaultAsync(latitude + 0.001, PassageBearing);
        var hidden = await FaultAsync(latitude - 0.001, PassageBearing + 45d, locationProtected: true);

        // And the protected trace is genuinely readable by them — so what is withheld below is the
        // placement rule and not plain invisibility.
        (await viewer.GetAsync($"/api/v1/features/{hidden}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.GetAsync($"/api/v1/features/{open}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var mine = await ReadAsync(owner, cave);
        var theirs = await ReadAsync(viewer, cave);

        mine.GetProperty("structureFeatureCount").GetInt32().ShouldBe(2);
        theirs.GetProperty("structureFeatureCount").GetInt32().ShouldBe(1);

        // The whole rose, sector by sector, is the rose of the open trace alone: not merely a
        // smaller total, but no share anywhere the hidden trace runs. A reader who could see the
        // shares shift would be reading the hidden trace's bearing out of the difference.
        var theirBins = theirs.GetProperty("structure").GetProperty("bins");
        var hiddenSectorShare = 0d;
        foreach (var bin in theirBins.EnumerateArray())
        {
            var from = bin.GetProperty("fromDegrees").GetDouble();
            if (from >= 100d && from < 120d)
            {
                hiddenSectorShare += bin.GetProperty("lengthFraction").GetDouble();
            }
        }

        hiddenSectorShare.ShouldBe(0d);

        // And the answer they get is exactly the answer they would get if the hidden trace were not
        // in the ground at all. Compared whole rather than sector by sector, because an
        // implementation that let the trace into the total the shares are taken over would pass a
        // check on one sector and fail this.
        var withHidden = await ReadRawAsync(viewer, cave);
        await owner.DeleteAsync($"/api/v1/features/{hidden}");
        var without = await ReadRawAsync(viewer, cave);

        without.ShouldBe(withHidden);
    }

    [Fact]
    public async Task A_cave_the_reader_may_not_place_is_answered_as_no_such_cave()
    {
        const double latitude = 45.45;
        var cave = await CaveWithPassageAsync(latitude, locationProtected: true);
        await FaultAsync(latitude + 0.001, PassageBearing);

        // Readable, and still refused — the refusal is about placing it, and it is spelled the same
        // way as a cave that is not there, so the set of routes that answer says nothing about
        // which caves are being kept from whom.
        (await viewer.GetAsync($"/api/v1/caves/{cave}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var refused = await viewer.GetAsync($"/api/v1/caves/{cave}/structure-comparison");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(refused)).ShouldBe("cave.not_found");

        var absent = await viewer.GetAsync($"/api/v1/caves/{Guid.CreateVersion7()}/structure-comparison");
        absent.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(absent)).ShouldBe("cave.not_found");
    }

    [Fact]
    public async Task A_cave_with_no_structure_within_reach_is_answered_rather_than_refused()
    {
        var cave = await CaveWithPassageAsync(45.05);

        var answer = await ReadAsync(owner, cave);

        answer.GetProperty("structureFeatureCount").GetInt32().ShouldBe(0);
        answer.GetProperty("structure").ValueKind.ShouldBe(JsonValueKind.Null);

        // Not zero. Nothing was measured on one side, so there is no disagreement to report, and
        // zero would say the cave follows a structure nobody has mapped.
        answer.GetProperty("divergence").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_reach_is_bounded_and_a_reach_outside_it_is_a_malformed_request()
    {
        var cave = await CaveWithPassageAsync(45.10);

        var tooFar = await owner.GetAsync(
            $"/api/v1/caves/{cave}/structure-comparison?radiusMetres=100000");
        tooFar.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var tooNear = await owner.GetAsync(
            $"/api/v1/caves/{cave}/structure-comparison?radiusMetres=1");
        tooNear.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var allowed = await owner.GetAsync(
            $"/api/v1/caves/{cave}/structure-comparison?radiusMetres=5000");
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_trace_out_of_reach_is_out_of_the_answer_and_a_wider_reach_finds_it()
    {
        const double latitude = 45.60;
        var cave = await CaveWithPassageAsync(latitude);

        // Roughly three and a half kilometres north: outside the default kilometre, inside five.
        await FaultAsync(latitude + 0.032, PassageBearing);

        (await ReadAsync(owner, cave)).GetProperty("structureFeatureCount").GetInt32().ShouldBe(0);

        var wider = await ReadJsonAsync(
            owner, $"/api/v1/caves/{cave}/structure-comparison?radiusMetres=5000");
        wider.GetProperty("structureFeatureCount").GetInt32().ShouldBe(1);
        wider.GetProperty("radiusMetres").GetDouble().ShouldBe(5000d);
    }

    /// <summary>
    /// A cave's stored geometries deliberately mix dimensions — the reduced centerline always
    /// carries a height, an entrance recorded without an altitude does not — and gathering them
    /// into one collection is an error in the database rather than a wider collection. A real cave
    /// carries both, so this is the ordinary case and not an edge one.
    /// </summary>
    [Fact]
    public async Task A_cave_whose_entrance_has_no_altitude_is_still_answered()
    {
        const double latitude = 45.65;
        var cave = await CaveWithPassageAsync(latitude);
        await EntranceAsync(cave, latitude, altitude: null);
        await FaultAsync(latitude + 0.001, PassageBearing);

        var answer = await ReadAsync(owner, cave);

        answer.GetProperty("structureFeatureCount").GetInt32().ShouldBe(1);
    }

    /// <summary>
    /// The reach is measured from what the caller can already see. A feature under the cave that
    /// this caller may not read must not widen it, or a trace entering the answer at one reach and
    /// not at a shorter one would bracket where that feature is.
    /// </summary>
    [Fact]
    public async Task A_child_feature_the_reader_may_not_read_does_not_widen_the_reach()
    {
        const double latitude = 45.70;
        var cave = await CaveWithPassageAsync(latitude);

        // Roughly three kilometres north of the cave: well outside the default kilometre.
        const double away = latitude + 0.027;
        var hidden = await ChildLineAsync(cave, away);
        await FaultAsync(away + 0.0005, PassageBearing);

        // Closed to this reader by a rule of its own. Marking it Private would not do it: a
        // descendant is visibility-readable when any row above it admits the caller, so a private
        // child of a readable cave is readable on purpose. A deny anchored on the child is the
        // shape that genuinely hides one.
        (await owner.PutAsJsonAsync($"/api/v1/objects/feature/{hidden}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user", subjectId = viewerUserId, effect = "deny",
                    actions = "read", scopeKind = "object",
                },
            },
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The fixture is doing what it claims: the owner, who may read the private child, does
        // reach the distant trace through it.
        (await ReadAsync(owner, cave)).GetProperty("structureFeatureCount").GetInt32().ShouldBe(1);

        // The reader cannot see the child at all, and so must not reach the trace either.
        (await viewer.GetAsync($"/api/v1/features/{hidden}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var withHidden = await ReadRawAsync(viewer, cave);
        (await viewer.GetAsync($"/api/v1/caves/{cave}/structure-comparison"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonDocument.Parse(withHidden).RootElement
            .GetProperty("structureFeatureCount").GetInt32().ShouldBe(0);

        // And the answer is byte for byte the answer they would get if the child were not there.
        await owner.DeleteAsync($"/api/v1/features/{hidden}");
        (await ReadRawAsync(viewer, cave)).ShouldBe(withHidden);
    }

    /// <summary>
    /// The area end of the cave question: the structure is gathered from a boundary somebody drew
    /// rather than from a circle, so a trace outside the massif is out of the answer however close
    /// it is, and one inside it is in however far.
    /// </summary>
    [Fact]
    public async Task Scoping_the_comparison_to_an_area_reads_that_area_and_not_a_circle()
    {
        const double latitude = 45.75;
        var area = await AreaAsync(latitude, latitude + 0.05);
        var cave = await CaveWithPassageAsync(latitude + 0.001);

        // Inside the boundary but four kilometres away, so no buffer of the default size finds it.
        await FaultAsync(latitude + 0.037, PassageBearing, parentId: area);

        // A stone's throw from the cave and outside the boundary, so a buffer would find it and the
        // area must not.
        await FaultAsync(latitude + 0.0015, PassageBearing + 45d);

        var buffered = await ReadAsync(owner, cave);
        buffered.GetProperty("structureFeatureCount").GetInt32().ShouldBe(1);

        var scoped = await ReadJsonAsync(
            owner, $"/api/v1/caves/{cave}/structure-comparison?areaId={area}");
        scoped.GetProperty("structureFeatureCount").GetInt32().ShouldBe(1);
        scoped.GetProperty("divergence").GetProperty("meanAxisSeparationDegrees")
            .GetDouble().ShouldBeLessThan(2d);

        // An area this caller cannot read refuses the whole question rather than quietly answering
        // from the buffer.
        var closed = await AreaAsync(latitude, latitude + 0.05, visibility: "private");
        var refused = await viewer.GetAsync(
            $"/api/v1/caves/{cave}/structure-comparison?areaId={closed}");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(refused)).ShouldBe("feature.not_found");
    }

    /// <summary>
    /// The surface half of the same question, asserted at both ends: depressions elongated along
    /// the mapped traces barely diverge from them, and the same depressions turned through half a
    /// right angle diverge most of the way. Both ends, because a measure wired up wrongly still
    /// returns small numbers for shapes that agree.
    /// </summary>
    [Fact]
    public async Task Depression_long_axes_are_compared_against_the_fault_strikes_at_both_ends()
    {
        const double along = 45.85;
        var alongArea = await AreaAsync(along, along + 0.02);
        await DolineAsync(alongArea, along + 0.004, PassageBearing);
        await DolineAsync(alongArea, along + 0.008, PassageBearing);
        await FaultAsync(along + 0.012, PassageBearing, parentId: alongArea);

        var agreeing = await ReadAreaAsync(owner, alongArea);
        agreeing.GetProperty("dolineCount").GetInt32().ShouldBe(2);
        agreeing.GetProperty("structureFeatureCount").GetInt32().ShouldBe(1);
        agreeing.GetProperty("divergence").GetProperty("meanAxisSeparationDegrees")
            .GetDouble().ShouldBeLessThan(5d);
        agreeing.GetProperty("divergence").GetProperty("normalized")
            .GetDouble().ShouldBeLessThan(0.2);

        const double across = 45.90;
        var acrossArea = await AreaAsync(across, across + 0.02);
        await DolineAsync(acrossArea, across + 0.004, PassageBearing);
        await DolineAsync(acrossArea, across + 0.008, PassageBearing);
        await FaultAsync(across + 0.012, PassageBearing + 45d, parentId: acrossArea);

        var disagreeing = await ReadAreaAsync(owner, acrossArea);
        disagreeing.GetProperty("divergence").GetProperty("meanAxisSeparationDegrees")
            .GetDouble().ShouldBeInRange(40d, 50d);
        disagreeing.GetProperty("divergence").GetProperty("normalized")
            .GetDouble().ShouldBeGreaterThan(0.4);
    }

    [Fact]
    public async Task A_depression_the_reader_may_not_place_is_in_no_rose_and_no_count()
    {
        const double latitude = 45.95;
        var area = await AreaAsync(latitude, latitude + 0.02);
        await DolineAsync(area, latitude + 0.004, PassageBearing);
        var hidden = await DolineAsync(
            area, latitude + 0.008, PassageBearing + 45d, locationProtected: true);
        await FaultAsync(latitude + 0.012, PassageBearing, parentId: area);

        // Readable by them, and still in nothing they can observe: the rule is about placing it.
        (await viewer.GetAsync($"/api/v1/features/{hidden}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ReadAreaAsync(owner, area)).GetProperty("dolineCount").GetInt32().ShouldBe(2);
        (await ReadAreaAsync(viewer, area)).GetProperty("dolineCount").GetInt32().ShouldBe(1);

        var withHidden = await ReadAreaRawAsync(viewer, area);
        await owner.DeleteAsync($"/api/v1/features/{hidden}");
        (await ReadAreaRawAsync(viewer, area)).ShouldBe(withHidden);
    }

    [Fact]
    public async Task An_area_nobody_may_read_answers_the_same_as_an_area_that_does_not_exist()
    {
        var closed = await AreaAsync(46.05, 46.07, visibility: "private");

        (await viewer.GetAsync($"/api/v1/features/{closed}/structure-comparison"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await viewer.GetAsync($"/api/v1/features/{Guid.CreateVersion7()}/structure-comparison"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The owner is answered for the same area, so the refusal above is the access rule.
        (await owner.GetAsync($"/api/v1/features/{closed}/structure-comparison"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync($"/api/v1/features/{closed}/structure-comparison"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Nobody_signed_in_is_told_nothing()
    {
        var cave = await CaveWithPassageAsync(45.15);

        var response = await anonymous.GetAsync($"/api/v1/caves/{cave}/structure-comparison");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        viewer?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement.GetProperty("code").GetString() ?? string.Empty;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static Task<JsonElement> ReadAsync(HttpClient client, Guid cave) =>
        ReadJsonAsync(client, $"/api/v1/caves/{cave}/structure-comparison");

    private static async Task<string> ReadRawAsync(HttpClient client, Guid cave)
    {
        var response = await client.GetAsync($"/api/v1/caves/{cave}/structure-comparison");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return body;
    }

    /// <summary>
    /// A cave carrying a drawn centerline that runs dead straight at <see cref="PassageBearing"/>.
    /// </summary>
    private async Task<Guid> CaveWithPassageAsync(double latitude, bool locationProtected = false)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Str Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var cave = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var content = new ByteArrayContent(StraightRun(25.30, latitude, PassageBearing, 12, 40d));
        content.Headers.ContentType = new("application/geo+json");
        using var form = new MultipartFormDataContent { { content, "file", "Centerline.geojson" } };
        var upload = await owner.PostAsync($"/api/v1/caves/{cave}/centerlines", form);
        upload.StatusCode.ShouldBe(
            HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());

        return cave;
    }

    /// <summary>A mapped fracture trace running dead straight at the given bearing.</summary>
    private async Task<Guid> FaultAsync(
        double latitude, double bearingDegrees, bool locationProtected = false, Guid? parentId = null)
    {
        var coordinates = new List<double[]>();
        var lon = 25.30;
        var lat = latitude;
        for (var i = 0; i < 6; i++)
        {
            coordinates.Add([lon, lat]);
            (lon, lat) = Step(lon, lat, bearingDegrees, 60d);
        }

        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Str Fault {Guid.NewGuid():N}"[..30],
            featureTypeId = fractureTypeId,
            parents = parentId is null
                ? null
                : new[] { new { parentId = parentId.Value, isPrimary = true } },
            geometry = new { type = "LineString", coordinates },
            locationProtected,
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static Task<JsonElement> ReadAreaAsync(HttpClient client, Guid area) =>
        ReadJsonAsync(client, $"/api/v1/features/{area}/structure-comparison");

    private static async Task<string> ReadAreaRawAsync(HttpClient client, Guid area)
    {
        var response = await client.GetAsync($"/api/v1/features/{area}/structure-comparison");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return body;
    }

    /// <summary>A rectangular area boundary spanning the given band of latitude.</summary>
    private async Task<Guid> AreaAsync(
        double south, double north, string visibility = "authenticated")
    {
        const double west = 25.24;
        const double east = 25.36;
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Str Area {Guid.NewGuid():N}"[..30],
            featureTypeId = karstAreaTypeId,
            geometry = new
            {
                type = "Polygon",
                coordinates = new[]
                {
                    new[]
                    {
                        new[] { west, south }, new[] { east, south }, new[] { east, north },
                        new[] { west, north }, new[] { west, south },
                    },
                },
            },
            locationProtected = false,
            visibility,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// An elongated closed depression: a rectangle five times as long as it is wide, lying at the
    /// given bearing, so its long axis is unambiguous and the weight the rose gives it is not near
    /// zero.
    /// </summary>
    private async Task<Guid> DolineAsync(
        Guid area, double latitude, double bearingDegrees, bool locationProtected = false)
    {
        const double lon = 25.30;
        const double halfLength = 100d;
        const double halfWidth = 20d;

        var corners = new List<double[]>();
        foreach (var (alongSign, acrossSign) in
                 new[] { (1, 1), (1, -1), (-1, -1), (-1, 1) })
        {
            var (l, t) = Step(lon, latitude, bearingDegrees, alongSign * halfLength);
            (l, t) = Step(l, t, bearingDegrees + 90d, acrossSign * halfWidth);
            corners.Add([l, t]);
        }

        corners.Add(corners[0]);

        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Str Doline {Guid.NewGuid():N}"[..30],
            featureTypeId = sinkholeTypeId,
            parents = new[] { new { parentId = area, isPrimary = true } },
            geometry = new { type = "Polygon", coordinates = new[] { corners } },
            locationProtected,
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>A short line drawn under the cave, used to stand for a hidden piece of its own
    /// line work.</summary>
    private async Task<Guid> ChildLineAsync(Guid cave, double latitude)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Str Child {Guid.NewGuid():N}"[..30],
            featureTypeId = wallTypeId,
            parents = new[] { new { parentId = cave, isPrimary = true } },
            geometry = new
            {
                type = "LineString",
                coordinates = new[] { new[] { 25.30, latitude }, new[] { 25.3005, latitude } },
            },
            locationProtected = false,
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>An entrance on the cave, with or without a recorded altitude.</summary>
    private async Task EntranceAsync(Guid cave, double latitude, decimal? altitude)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/caves/{cave}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { 25.3001, latitude } },
            altitude,
            positionQuality = "Gps",
        });
        response.StatusCode.ShouldBe(
            HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static byte[] StraightRun(
        double lon, double lat, double bearingDegrees, int points, double stepMetres)
    {
        var ordinates = new List<string>();
        for (var i = 0; i < points; i++)
        {
            ordinates.Add($"[{F(lon)},{F(lat)},{F(1000d + i)}]");
            (lon, lat) = Step(lon, lat, bearingDegrees, stepMetres);
        }

        var body = "{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\","
            + "\"properties\":{},\"geometry\":{\"type\":\"LineString\",\"coordinates\":["
            + string.Join(",", ordinates) + "]}}]}";
        return Encoding.UTF8.GetBytes(body);
    }

    /// <summary>
    /// The point a given distance from another on a given bearing, near enough over a few hundred
    /// metres. The fixture only needs the bearing back out of it to a fraction of a sector, and the
    /// database measures the result on the spheroid regardless of how it was laid out.
    /// </summary>
    private static (double Lon, double Lat) Step(
        double lon, double lat, double bearingDegrees, double metres)
    {
        var radians = bearingDegrees * Math.PI / 180d;
        var north = Math.Cos(radians) * metres;
        var east = Math.Sin(radians) * metres;
        return (
            lon + (east / (111_320d * Math.Cos(lat * Math.PI / 180d))),
            lat + (north / 110_540d));
    }

    private static string F(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);
}
