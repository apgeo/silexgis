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
/// The vertical views: where a cave's passage sits, where an area's entrances sit, and what
/// somebody decided either of those shows.
///
/// <para>
/// The test this file exists for is that a protected entrance moves nothing. An altitude places a
/// hole on a hillside, and a histogram is readable one bin at a time — so it is not enough that a
/// protected cave is off the map if adding it makes a bin appear. The proof is two answers to the
/// same question, one asked while the protected cave is there and one after it is gone, compared
/// whole rather than bin by bin: an implementation that shifted a neighbouring edge or moved the
/// total the fractions are taken over would pass a check on the one bin and fail this.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class HypsometryTests : IAsyncLifetime, IDisposable
{
    private const double AreaWest = 25.40;
    private const double AreaEast = 25.55;
    private const double AreaSouth = 45.50;
    private const double AreaNorth = 45.62;

    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private HttpClient anonymous = null!;
    private long caveTypeId;
    private long springCaveTypeId;
    private long entranceTypeId;
    private long karstAreaTypeId;

    public HypsometryTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"hyp-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"hyp-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).FirstAsync();
            springCaveTypeId = await db.CaveTypes
                .Where(t => t.Code == CaveTypeSeeds.SpringCave).Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
            karstAreaTypeId = await db.FeatureTypes
                .Where(t => t.Code == "karst_area").Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"hyp-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"hyp-read-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    /// <summary>
    /// The protection test that matters. A genuine Viewer holds no grant anywhere, so the protected
    /// cave in this area is one they may read and may not place.
    /// </summary>
    [Fact]
    public async Task A_protected_entrance_contributes_to_no_bin_a_reader_can_observe()
    {
        var area = await CreateAreaAsync();
        await CaveWithEntranceAsync(area, 1000m);
        await CaveWithEntranceAsync(area, 1100m);
        await CaveWithEntranceAsync(area, 1200m);
        var spring = await CaveWithEntranceAsync(area, 800m, springCaveTypeId);
        var hidden = await CaveWithEntranceAsync(area, 1500m, locationProtected: true);

        // The positive half, for this same caller: the route answers them, and it answers with the
        // entrances they may place. Without this a route that refused everybody would pass below.
        var seen = await ReadAreaAsync(viewer, area);
        seen.GetProperty("entranceCount").GetInt32().ShouldBe(4);
        seen.GetProperty("springAltitudesM").EnumerateArray()
            .Select(a => a.GetDouble()).ShouldBe([800d]);

        // And the withheld cave is genuinely readable by them — so what follows is the placement
        // rule and not visibility doing the work.
        (await viewer.GetAsync($"/api/v1/caves/{hidden}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The owner, who may place everything, sees the fifth entrance and the higher spread.
        var byOwner = await ReadAreaAsync(owner, area);
        byOwner.GetProperty("entranceCount").GetInt32().ShouldBe(5);
        byOwner.GetProperty("proposal").GetProperty("highestM").GetDouble().ShouldBe(1500d);
        seen.GetProperty("proposal").GetProperty("highestM").GetDouble().ShouldBe(1200d);

        var before = seen.GetRawText();

        // Now remove the protected cave altogether and ask again. If its altitude had reached any
        // part of the answer — a bin, a band edge, a count, the total the fractions divide by —
        // these two would differ.
        (await owner.DeleteAsync($"/api/v1/caves/{hidden}")).IsSuccessStatusCode.ShouldBeTrue();

        var after = (await ReadAreaAsync(viewer, area)).GetRawText();
        after.ShouldBe(before);

        // The spring is still a spring after all that, which is what the reference lines are drawn
        // from; nothing about the protected cave touched them either.
        spring.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task An_area_nobody_may_read_answers_the_same_as_an_area_that_does_not_exist()
    {
        var closed = await CreateAreaAsync(visibility: "private");

        var refused = await viewer.GetAsync($"/api/v1/features/{closed}/entrance-hypsometry");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var absent = await viewer.GetAsync($"/api/v1/features/{Guid.CreateVersion7()}/entrance-hypsometry");
        absent.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The owner is answered for the same area, so the refusal above is the access rule.
        (await owner.GetAsync($"/api/v1/features/{closed}/entrance-hypsometry"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await anonymous.GetAsync($"/api/v1/features/{closed}/entrance-hypsometry"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Two storeys in the line work come back as two proposed levels; the same cave drawn in plan
    /// is refused its levels rather than reported as one storey at height zero.
    /// </summary>
    [Fact]
    public async Task Passage_at_two_heights_proposes_two_levels_and_a_plan_drawing_proposes_none()
    {
        var storeyed = await CreateCaveAsync();
        await UploadCenterlineAsync(storeyed, TwoStoreys(withAltitudes: true));

        var measured = await ReadJsonAsync(owner, $"/api/v1/caves/{storeyed}/hypsometry");
        measured.GetProperty("hasAltitudes").GetBoolean().ShouldBeTrue();
        var proposal = measured.GetProperty("proposal");
        proposal.GetProperty("bands").GetArrayLength().ShouldBe(2);
        proposal.GetProperty("bands")[0].GetProperty("toM").GetDouble().ShouldBeLessThan(1100d);
        proposal.GetProperty("bands")[1].GetProperty("fromM").GetDouble().ShouldBeGreaterThan(1100d);
        proposal.GetProperty("totalWeightM").GetDouble().ShouldBeGreaterThan(0d);

        var flat = await CreateCaveAsync();
        await UploadCenterlineAsync(flat, TwoStoreys(withAltitudes: false));

        var drawn = await ReadJsonAsync(owner, $"/api/v1/caves/{flat}/hypsometry");
        drawn.GetProperty("hasAltitudes").GetBoolean().ShouldBeFalse();
        drawn.GetProperty("proposal").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>
    /// The proposed storeys are half a judgment on their own. The height water leaves the massif at
    /// is what a reader sets them against, so the per-cave view carries the springs of the area the
    /// cave sits in — and, being altitudes, only the ones this reader may place.
    /// </summary>
    [Fact]
    public async Task A_cave_carries_the_spring_altitudes_of_its_area_as_reference_lines()
    {
        var area = await CreateAreaAsync();
        await CaveWithEntranceAsync(area, 800m, springCaveTypeId);
        var hiddenSpring = await CaveWithEntranceAsync(
            area, 640m, springCaveTypeId, locationProtected: true);

        var storeyed = await CreateCaveAsync(area);
        await UploadCenterlineAsync(storeyed, TwoStoreys(withAltitudes: true));

        var byOwner = await ReadJsonAsync(owner, $"/api/v1/caves/{storeyed}/hypsometry");
        byOwner.GetProperty("springAltitudesM").EnumerateArray()
            .Select(a => a.GetDouble()).ShouldBe([640d, 800d]);

        // The reader may read the protected spring cave and may not place it, so its altitude is
        // no line on their chart.
        (await viewer.GetAsync($"/api/v1/caves/{hiddenSpring}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var byReader = await ReadJsonAsync(viewer, $"/api/v1/caves/{storeyed}/hypsometry");
        byReader.GetProperty("springAltitudesM").EnumerateArray()
            .Select(a => a.GetDouble()).ShouldBe([800d]);

        // A cave that sits in no area has no springs to draw against, and says so as an empty list
        // rather than by leaving the field out.
        var loose = await CreateCaveAsync();
        (await ReadJsonAsync(owner, $"/api/v1/caves/{loose}/hypsometry"))
            .GetProperty("springAltitudesM").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_cave_a_reader_may_not_place_has_no_hypsometry_and_says_so_as_no_such_cave()
    {
        var open = await CreateCaveAsync();
        await UploadCenterlineAsync(open, TwoStoreys(withAltitudes: true));
        var hidden = await CreateCaveAsync(locationProtected: true);
        await UploadCenterlineAsync(hidden, TwoStoreys(withAltitudes: true));

        // The positive half for this same caller.
        (await viewer.GetAsync($"/api/v1/caves/{open}/hypsometry")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.GetAsync($"/api/v1/caves/{hidden}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var refused = await viewer.GetAsync($"/api/v1/caves/{hidden}/hypsometry");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(refused)).ShouldBe("cave.not_found");

        var absent = await viewer.GetAsync($"/api/v1/caves/{Guid.CreateVersion7()}/hypsometry");
        absent.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(absent)).ShouldBe("cave.not_found");
    }

    [Fact]
    public async Task A_recorded_reading_survives_a_reload_and_a_later_one_supersedes_it()
    {
        var cave = await CreateCaveAsync();

        var empty = await ReadJsonAsync(owner, $"/api/v1/caves/{cave}/level-bands");
        empty.GetProperty("confirmed").GetBoolean().ShouldBeFalse();
        empty.GetProperty("bands").GetArrayLength().ShouldBe(0);

        var saved = await owner.PutAsJsonAsync($"/api/v1/caves/{cave}/level-bands", new
        {
            bands = new[]
            {
                new { fromM = 1000d, toM = 1010d, label = "Lower" },
                new { fromM = 1200d, toM = 1210d, label = "Upper" },
            },
            note = "Confirmed against the 2026 resurvey.",
        });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        var reloaded = await ReadJsonAsync(owner, $"/api/v1/caves/{cave}/level-bands");
        reloaded.GetProperty("confirmed").GetBoolean().ShouldBeTrue();
        reloaded.GetProperty("bands").GetArrayLength().ShouldBe(2);
        reloaded.GetProperty("bands")[1].GetProperty("label").GetString().ShouldBe("Upper");
        reloaded.GetProperty("note").GetString().ShouldBe("Confirmed against the 2026 resurvey.");

        var again = await owner.PutAsJsonAsync($"/api/v1/caves/{cave}/level-bands", new
        {
            bands = new[] { new { fromM = 900d, toM = 1300d, label = (string?)null } },
            note = (string?)null,
        });
        again.StatusCode.ShouldBe(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());

        var current = await ReadJsonAsync(owner, $"/api/v1/caves/{cave}/level-bands");
        current.GetProperty("bands").GetArrayLength().ShouldBe(1);

        // The superseded row is kept — a reading is somebody's conclusion, and a later one does not
        // unmake it — while exactly one row is current.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.CaveLevelBands.CountAsync(b => b.CaveFeatureId == cave)).ShouldBe(2);
        (await db.CaveLevelBands.CountAsync(b => b.CaveFeatureId == cave && b.SupersededAt == null))
            .ShouldBe(1);

        (await owner.DeleteAsync($"/api/v1/caves/{cave}/level-bands"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadJsonAsync(owner, $"/api/v1/caves/{cave}/level-bands"))
            .GetProperty("confirmed").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_reading_is_refused_to_a_caller_who_may_read_the_cave_but_not_write_it()
    {
        var cave = await CreateCaveAsync();

        // The positive half: this caller may read the cave and may read the route.
        (await viewer.GetAsync($"/api/v1/caves/{cave}/level-bands")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var refused = await viewer.PutAsJsonAsync($"/api/v1/caves/{cave}/level-bands", new
        {
            bands = new[] { new { fromM = 1d, toM = 2d, label = (string?)null } },
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await anonymous.GetAsync($"/api/v1/caves/{cave}/level-bands"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Bands_that_overlap_or_run_backwards_are_refused_as_a_malformed_request()
    {
        var cave = await CreateCaveAsync();

        var backwards = await owner.PutAsJsonAsync($"/api/v1/caves/{cave}/level-bands", new
        {
            bands = new[] { new { fromM = 1200d, toM = 1000d, label = (string?)null } },
        });
        backwards.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var overlapping = await owner.PutAsJsonAsync($"/api/v1/caves/{cave}/level-bands", new
        {
            bands = new[]
            {
                new { fromM = 1000d, toM = 1100d, label = (string?)null },
                new { fromM = 1050d, toM = 1200d, label = (string?)null },
            },
        });
        overlapping.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var none = await owner.PutAsJsonAsync($"/api/v1/caves/{cave}/level-bands", new
        {
            bands = Array.Empty<object>(),
        });
        none.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();

    private static async Task<string?> Code(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement.TryGetProperty("code", out var code)
            ? code.GetString()
            : null;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static Task<JsonElement> ReadAreaAsync(HttpClient client, Guid area) =>
        ReadJsonAsync(client, $"/api/v1/features/{area}/entrance-hypsometry");

    private async Task<Guid> CreateAreaAsync(string visibility = "authenticated")
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Hyps Area {Guid.NewGuid():N}"[..40],
            featureTypeId = karstAreaTypeId,
            geometry = new
            {
                type = "Polygon",
                coordinates = new[]
                {
                    new[]
                    {
                        new[] { AreaWest, AreaSouth }, new[] { AreaEast, AreaSouth },
                        new[] { AreaEast, AreaNorth }, new[] { AreaWest, AreaNorth },
                        new[] { AreaWest, AreaSouth },
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

    private async Task<Guid> CreateCaveAsync(
        Guid? parentId = null, bool locationProtected = false, long? typeId = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Hyps Cave {Guid.NewGuid():N}"[..30],
            caveTypeId = typeId ?? caveTypeId,
            visibility = "authenticated",
            locationProtected,
            parentId,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CaveWithEntranceAsync(
        Guid area, decimal altitude, long? typeId = null, bool locationProtected = false)
    {
        var cave = await CreateCaveAsync(area, locationProtected, typeId);
        var response = await owner.PostAsJsonAsync($"/api/v1/caves/{cave}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { 25.45, 45.55 } },
            altitude,
            positionQuality = "Gps",
        });
        response.StatusCode.ShouldBe(
            HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return cave;
    }

    private async Task UploadCenterlineAsync(Guid caveId, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/geo+json");
        using var form = new MultipartFormDataContent { { content, "file", "Centerline.geojson" } };
        var response = await owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", form);
        response.StatusCode.ShouldBe(
            HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Two runs of passage two hundred metres apart vertically, or the same drawing with no third
    /// coordinate at all — which is what a survey recorded as a plan looks like on disk.
    /// </summary>
    private static byte[] TwoStoreys(bool withAltitudes)
    {
        static string Run(double latitude, double baseAltitude, bool withAltitudes)
        {
            var points = new List<string>();
            for (var i = 0; i < 12; i++)
            {
                var longitude = 25.44 + (i * 0.0002);
                var ordinates = withAltitudes
                    ? $"[{F(longitude)},{F(latitude)},{F(baseAltitude + i)}]"
                    : $"[{F(longitude)},{F(latitude)}]";
                points.Add(ordinates);
            }

            return "{\"type\":\"Feature\",\"properties\":{},\"geometry\":"
                + "{\"type\":\"LineString\",\"coordinates\":[" + string.Join(",", points) + "]}}";
        }

        var body = "{\"type\":\"FeatureCollection\",\"features\":["
            + Run(45.53, 1000, withAltitudes) + "," + Run(45.54, 1200, withAltitudes)
            + "]}";
        return Encoding.UTF8.GetBytes(body);
    }

    private static string F(double value) => value.ToString("0.#######", CultureInfo.InvariantCulture);
}
