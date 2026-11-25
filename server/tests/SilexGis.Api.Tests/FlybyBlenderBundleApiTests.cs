// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Features.Flybys;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;

namespace SilexGis.Api.Tests;

/// <summary>
/// A flyby asked for as a render bundle over HTTP: who gets one, what of each cave goes into it,
/// and what a cave closed to the caller looks like in the answer — nothing, and no reason.
/// </summary>
/// <remarks>
/// Every cave here is invented: a few legs in plain metres about a made-up point on open ocean,
/// written in the compiled survey format by its own writer and read by the application's own
/// reading, so the walls and the legs in a bundle are the ones the application built.
/// </remarks>
public sealed class FlybyBlenderBundleApiTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Route = "/api/v1/flybys/blender-bundle";

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private Guid ownerId;
    private Guid readerId;
    private long caveTypeId;
    private long karstAreaTypeId;

    public FlybyBlenderBundleApiTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            // No drain in this host: this class runs the readings it queues itself, one at a time.
            JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"fbb-own-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"fbb-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            karstAreaTypeId = await db.FeatureTypes.Where(t => t.Code == "karst_area").Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"fbb-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"fbb-read-{suffix}@t.local");
    }

    [Fact]
    public async Task A_flyby_of_two_caves_comes_back_as_a_zip_with_walls_for_one_and_lines_for_both()
    {
        var spot = Spot.Somewhere();
        var first = await CreateSurveyedCaveAsync(spot, heightM: 900);
        var second = await CreateSurveyedCaveAsync(spot.East(0.01), heightM: 860);

        var answer = await owner.PostAsJsonAsync(Route, Request(spot, [Cave(first, walls: true), Cave(second, walls: false)]));

        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());
        answer.Content.Headers.ContentType!.MediaType.ShouldBe("application/zip");
        answer.Content.Headers.ContentDisposition!.FileName!.Trim('"').ShouldBe("an-invented-flyby-blender.zip");
        Header(answer, FlybyBlenderBundleEndpoints.WallsHeader).ShouldBe(1);
        Header(answer, FlybyBlenderBundleEndpoints.LinesHeader).ShouldBe(1);
        Header(answer, FlybyBlenderBundleEndpoints.NotDrawnHeader).ShouldBe(0);
        Header(answer, FlybyBlenderBundleEndpoints.WallsLeftOutHeader).ShouldBe(0);

        var bundle = await Bundle.ReadAsync(answer);
        bundle.Meshes.ShouldBe(["cave-01-lines.ply", "cave-01-walls.ply", "cave-02-lines.ply"]);
        bundle.Files.ShouldContain("README.txt");
        bundle.Files.ShouldContain("LICENSE");
        bundle.Files.ShouldContain("render.py");

        // The walls are the mesh the reading built: one measured leg, 32 faces, as a binary PLY.
        Encoding.ASCII.GetString(bundle.Entries["cave-01-walls.ply"], 0, 200).ShouldContain("element face 32");

        // The frame names no place: zero, no coordinate system, and the spot is in no text file.
        bundle.Plan.GetProperty("frame").TryGetProperty("crs", out _).ShouldBeFalse();
        foreach (var (name, bytes) in bundle.Entries.Where(e => !e.Key.EndsWith(".ply", StringComparison.Ordinal)))
        {
            Encoding.UTF8.GetString(bytes).ShouldNotContain(
                spot.Latitude.ToString("0.00", CultureInfo.InvariantCulture), Case.Sensitive, name);
        }

        // The scene had relief, so each cave stands at its surveyed altitude: the first, whose
        // file's zero is 900 m up at the origin, 300 m above an origin taken at 600; the second
        // some 770 m east of it and 40 m lower.
        var models = bundle.Models;
        var walls = Translation(models["cave-01-walls"]);
        walls[0].ShouldBe(0, 0.05);
        walls[1].ShouldBe(0, 0.05);
        walls[2].ShouldBe(300, 0.05);
        var other = Translation(models["cave-02-lines"]);
        other[0].ShouldBeInRange(500, 1200);
        Math.Abs(other[1]).ShouldBeLessThan(1);
        other[2].ShouldBe(260, 0.2);

        // One pose a frame, each with its own turn.
        var track = bundle.Plan.GetProperty("camera").GetProperty("track").EnumerateArray().ToList();
        track.Count.ShouldBe(10);
        track.Count(pose => pose.TryGetProperty("orientation", out var turn) && turn.GetArrayLength() == 4).ShouldBe(10);
        bundle.Plan.GetProperty("timeline").GetProperty("fps").GetInt32().ShouldBe(25);

        // What the flyby showed that a bundle does not carry is in its own notes.
        var readme = Encoding.UTF8.GetString(bundle.Entries["README.txt"]);
        readme.ShouldContain("NOT IN THIS SCENE");
        readme.ShouldContain("the ground and the map drawn on it");
        readme.ShouldContain("the entrance markers");
    }

    [Fact]
    public async Task A_scene_without_relief_hangs_each_cave_from_the_surface_by_the_top_the_scene_used()
    {
        var spot = Spot.Somewhere();
        var cave = await CreateSurveyedCaveAsync(spot, heightM: 900);

        // No top told: the highest leg as read here — the file's zero, station A — goes to height
        // nought, which is where the origin is.
        var hung = await Bundle.ReadAsync(await owner.PostAsJsonAsync(
            Route, Request(spot, [Cave(cave, walls: true)], absolute: false, originHeightM: 0)));
        Translation(hung.Models["cave-01-walls"])[2].ShouldBe(0, 0.05);

        // A top the scene told — twelve metres above the file's zero, a splay perhaps — hangs the
        // cave from that instead: everything twelve metres lower.
        var told = await Bundle.ReadAsync(await owner.PostAsJsonAsync(
            Route, Request(spot, [Cave(cave, walls: true, surveyTopM: 912)], absolute: false, originHeightM: 0)));
        Translation(told.Models["cave-01-walls"])[2].ShouldBe(-12, 0.05);
        // And its lines with its walls: they are placed about one of its stations, none of which
        // is above the file's zero.
        Translation(told.Models["cave-01-lines"])[2].ShouldBeLessThanOrEqualTo(-12 + 0.05);
        Translation(hung.Models["cave-01-lines"])[2].ShouldBe(Translation(told.Models["cave-01-lines"])[2] + 12, 0.05);
    }

    [Fact]
    public async Task Walls_asked_of_a_cave_that_has_none_fall_back_to_its_lines_and_say_so()
    {
        var spot = Spot.Somewhere();
        var cave = await CreateSurveyedCaveAsync(spot, heightM: 900, measured: false);

        var answer = await owner.PostAsJsonAsync(Route, Request(spot, [Cave(cave, walls: true)], lines: false));

        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());
        Header(answer, FlybyBlenderBundleEndpoints.WallsHeader).ShouldBe(0);
        Header(answer, FlybyBlenderBundleEndpoints.LinesHeader).ShouldBe(1);
        Header(answer, FlybyBlenderBundleEndpoints.WallsLeftOutHeader).ShouldBe(1);
        var bundle = await Bundle.ReadAsync(answer);
        // Lines although the look hides them: a cave asked for is not left as a hole in the scene.
        bundle.Meshes.ShouldBe(["cave-01-lines.ply"]);
        Encoding.UTF8.GetString(bundle.Entries["README.txt"]).ShouldContain("the walls of 1 cave, which has none to draw");
    }

    [Fact]
    public async Task Lines_are_left_out_of_a_walled_cave_when_the_look_hides_them()
    {
        var spot = Spot.Somewhere();
        var cave = await CreateSurveyedCaveAsync(spot, heightM: 900);

        var bundle = await Bundle.ReadAsync(await owner.PostAsJsonAsync(
            Route, Request(spot, [Cave(cave, walls: true)], lines: false)));

        bundle.Meshes.ShouldBe(["cave-01-walls.ply"]);
    }

    [Fact]
    public async Task A_cave_the_caller_may_not_read_is_not_drawn_and_looks_exactly_like_one_that_does_not_exist()
    {
        var spot = Spot.Somewhere();
        var shared = await CreateSurveyedCaveAsync(spot, heightM: 900);
        var hidden = await CreateSurveyedCaveAsync(spot.East(0.01), heightM: 900, visibility: "private");

        // The positive half first: its owner gets both.
        var mine = await owner.PostAsJsonAsync(Route, Request(spot, [Cave(shared, walls: true), Cave(hidden, walls: true)]));
        Header(mine, FlybyBlenderBundleEndpoints.WallsHeader).ShouldBe(2);
        Header(mine, FlybyBlenderBundleEndpoints.NotDrawnHeader).ShouldBe(0);

        var withheld = await reader.PostAsJsonAsync(Route, Request(spot, [Cave(shared, walls: true), Cave(hidden, walls: true)]));
        var invented = await reader.PostAsJsonAsync(Route, Request(spot, [Cave(shared, walls: true), Cave(Guid.NewGuid(), walls: true)]));

        foreach (var answer in new[] { withheld, invented })
        {
            answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());
            Header(answer, FlybyBlenderBundleEndpoints.WallsHeader).ShouldBe(1);
            Header(answer, FlybyBlenderBundleEndpoints.NotDrawnHeader).ShouldBe(1);
            Header(answer, FlybyBlenderBundleEndpoints.WallsLeftOutHeader).ShouldBe(0);
        }

        // Byte for byte the same file: nothing in it — not a count, not a sentence, not a mesh —
        // tells a cave being kept back from an id that was made up.
        (await withheld.Content.ReadAsByteArrayAsync()).ShouldBe(await invented.Content.ReadAsByteArrayAsync());
        var bundle = await Bundle.ReadAsync(withheld);
        bundle.Meshes.ShouldBe(["cave-01-lines.ply", "cave-01-walls.ply"]);
        Encoding.UTF8.GetString(bundle.Entries["README.txt"]).ShouldContain("1 of the caves asked for (nothing of it is available to draw)");

        // Asked for alone, there is no scene — again the same answer either way.
        var alone = await reader.PostAsJsonAsync(Route, Request(spot, [Cave(hidden, walls: true)]));
        var nothing = await reader.PostAsJsonAsync(Route, Request(spot, [Cave(Guid.NewGuid(), walls: true)]));
        alone.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        nothing.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(alone)).ShouldBe(FlybyBlenderBundleEndpoints.NothingToDrawCode);
        (await CodeAsync(nothing)).ShouldBe(FlybyBlenderBundleEndpoints.NothingToDrawCode);
    }

    [Fact]
    public async Task A_location_protected_cave_is_not_drawn_until_its_exact_location_is_granted()
    {
        var spot = Spot.Somewhere();
        var open = await CreateSurveyedCaveAsync(spot, heightM: 900);
        var closed = await CreateSurveyedCaveAsync(spot.East(0.01), heightM: 900, locationProtected: true);

        // The reader may read the protected cave itself; what is closed to them is where it is —
        // and its walls and its legs are where it is.
        (await reader.GetAsync($"/api/v1/caves/{closed}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var withheld = await reader.PostAsJsonAsync(Route, Request(spot, [Cave(open, walls: true), Cave(closed, walls: true)]));
        var invented = await reader.PostAsJsonAsync(Route, Request(spot, [Cave(open, walls: true), Cave(Guid.NewGuid(), walls: true)]));
        withheld.StatusCode.ShouldBe(HttpStatusCode.OK, await withheld.Content.ReadAsStringAsync());
        Header(withheld, FlybyBlenderBundleEndpoints.NotDrawnHeader).ShouldBe(1);
        // The same bytes as for a made-up id: not even that the cave has a survey is said.
        (await withheld.Content.ReadAsByteArrayAsync()).ShouldBe(await invented.Content.ReadAsByteArrayAsync());
        (await Bundle.ReadAsync(withheld)).Meshes.ShouldBe(["cave-01-lines.ply", "cave-01-walls.ply"]);

        // Neither its walls nor its lines: asked for by its lines alone it is withheld the same.
        var linesOnly = await reader.PostAsJsonAsync(Route, Request(spot, [Cave(open, walls: true), Cave(closed, walls: false)]));
        Header(linesOnly, FlybyBlenderBundleEndpoints.NotDrawnHeader).ShouldBe(1);

        // Whoever owns it never lost sight of it.
        var mine = await owner.PostAsJsonAsync(Route, Request(spot, [Cave(open, walls: true), Cave(closed, walls: true)]));
        Header(mine, FlybyBlenderBundleEndpoints.WallsHeader).ShouldBe(2);

        await GrantExactViewAsync(closed);

        var granted = await reader.PostAsJsonAsync(Route, Request(spot, [Cave(open, walls: true), Cave(closed, walls: true)]));
        Header(granted, FlybyBlenderBundleEndpoints.WallsHeader).ShouldBe(2);
        Header(granted, FlybyBlenderBundleEndpoints.NotDrawnHeader).ShouldBe(0);
        (await Bundle.ReadAsync(granted)).Meshes.ShouldContain("cave-02-walls.ply");
    }

    [Fact]
    public async Task Protection_inherited_from_an_area_withholds_the_caves_inside_it()
    {
        var spot = Spot.Somewhere();
        var areaId = await CreateProtectedAreaAsync();
        var inside = await CreateSurveyedCaveAsync(spot, heightM: 900, parentId: areaId);

        (await reader.PostAsJsonAsync(Route, Request(spot, [Cave(inside, walls: true)])))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // A grant on the cave is not a grant on the root above it.
        await GrantExactViewAsync(inside);
        (await reader.PostAsJsonAsync(Route, Request(spot, [Cave(inside, walls: true)])))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        await GrantExactViewAsync(areaId);
        var granted = await reader.PostAsJsonAsync(Route, Request(spot, [Cave(inside, walls: true)]));
        granted.StatusCode.ShouldBe(HttpStatusCode.OK, await granted.Content.ReadAsStringAsync());
        Header(granted, FlybyBlenderBundleEndpoints.WallsHeader).ShouldBe(1);
    }

    [Fact]
    public async Task A_cave_with_no_survey_is_counted_with_the_caves_not_drawn_and_given_no_reason_of_its_own()
    {
        var spot = Spot.Somewhere();
        var surveyed = await CreateSurveyedCaveAsync(spot, heightM: 900);
        var bare = await CreateCaveAsync();

        var answer = await owner.PostAsJsonAsync(Route, Request(spot, [Cave(surveyed, walls: true), Cave(bare, walls: true)]));

        Header(answer, FlybyBlenderBundleEndpoints.NotDrawnHeader).ShouldBe(1);
        Header(answer, FlybyBlenderBundleEndpoints.WallsLeftOutHeader).ShouldBe(0);
        var readme = Encoding.UTF8.GetString((await Bundle.ReadAsync(answer)).Entries["README.txt"]);
        readme.ShouldContain("1 of the caves asked for (nothing of it is available to draw)");
        readme.ShouldNotContain("which has none to draw");
    }

    [Fact]
    public async Task Each_bundle_is_recorded_with_the_caves_that_went_into_it_and_no_position()
    {
        var spot = Spot.Somewhere();
        var walled = await CreateSurveyedCaveAsync(spot, heightM: 900);
        var lined = await CreateSurveyedCaveAsync(spot.East(0.01), heightM: 900);

        var answer = await owner.PostAsJsonAsync(
            Route, Request(spot, [Cave(walled, walls: true), Cave(lined, walls: false), Cave(Guid.NewGuid(), walls: true)]));
        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());

        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var entries = await db.AuditEntries.AsNoTracking()
            .Where(e => e.Action == AuditActions.Exported
                        && e.EntityType == FlybyBlenderBundleEndpoints.AuditEntityType
                        && e.UserId == ownerId)
            .ToListAsync();
        var mine = entries.Select(e => JsonDocument.Parse(e.Changes!).RootElement)
            .Single(c => c.GetProperty("wallsCaveIds").EnumerateArray().Any(id => id.GetString() == walled.ToString()));

        mine.GetProperty("format").GetString().ShouldBe("blender");
        mine.GetProperty("frames").GetInt32().ShouldBe(10);
        mine.GetProperty("linesCaveIds").EnumerateArray().Select(id => id.GetString()).ShouldBe([lined.ToString()]);
        mine.GetProperty("notDrawnCount").GetInt32().ShouldBe(1);
        // Which caves, and how each went in — not where anything is.
        mine.ToString().ShouldNotContain(spot.Longitude.ToString("0.00", CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task The_route_refuses_a_caller_with_no_account_and_a_key_made_for_a_field_device()
    {
        var spot = Spot.Somewhere();

        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync(Route, Request(spot, [Cave(Guid.NewGuid(), walls: true)])))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // A key opens the routes a device needs and no other; this is not one of them.
        var key = await AuthHelper.CreateAccessKeyAsync(reader, "Field tablet");
        using var device = AuthHelper.AccessKeyClient(factory, key);
        var refused = await device.PostAsJsonAsync(Route, Request(spot, [Cave(Guid.NewGuid(), walls: true)]));
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(refused)).ShouldBe("auth.access_key_not_accepted");
    }

    [Fact]
    public async Task A_frame_rate_with_a_fraction_is_refused_by_name()
    {
        var spot = Spot.Somewhere();

        var refused = await owner.PostAsJsonAsync(Route, Request(spot, [Cave(Guid.NewGuid(), walls: true)], fps: 12.5));

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(refused)).ShouldBe(FlybyBlenderBundleEndpoints.FrameRateCode);

        // Every whole rate a movie of a flyby is offered at is taken.
        foreach (var fps in new[] { 10, 15, 20, 24, 25, 30 })
        {
            // Past validation: refused for having nothing to draw, which is the next thing asked.
            var passed = await owner.PostAsJsonAsync(Route, Request(spot, [Cave(Guid.NewGuid(), walls: true)], fps: fps));
            passed.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, $"{fps} fps");
        }
    }

    [Fact]
    public async Task A_request_that_is_not_a_flyby_is_refused_field_by_field()
    {
        var spot = Spot.Somewhere();
        var cave = Cave(Guid.NewGuid(), walls: true);
        double[] frame = [spot.Longitude, spot.Latitude, 1200, 0, -30, 0, 400, 60];

        async Task Refused(object body, string because, string code = "validation.failed")
        {
            var answer = await owner.PostAsJsonAsync(Route, body);
            answer.StatusCode.ShouldBe(HttpStatusCode.BadRequest, because);
            (await CodeAsync(answer)).ShouldBe(code, because);
        }

        await Refused(Request(spot, []), "no cave");
        await Refused(Request(spot, [cave, cave]), "a cave named twice");
        await Refused(Request(spot, [cave], frames: []), "no frame");
        await Refused(Request(spot, [cave], frames: [[spot.Longitude, spot.Latitude, 1200]]), "a frame of three numbers");
        await Refused(Request(spot, [cave], frames: [[.. frame.Take(4), -91, 0, 400, 60]]), "a tilt past straight down");
        await Refused(Request(spot, [cave], frames: [[.. frame.Take(6), 0, 60]]), "a camera that looks at the point it stands on");
        await Refused(Request(spot, [cave], frames: [[181, .. frame.Skip(1)]]), "a camera off the globe");
        await Refused(Request(spot, [cave], relief: 9), "a relief past the tallest the scene draws");
        await Refused(Request(spot, [cave], width: 1281), "an odd width");
        await Refused(Request(spot, [cave], sky: "blue"), "a colour that is a word");
        await Refused(
            Request(spot, [cave], frames: [.. Enumerable.Repeat(frame, FlybyBlenderBundleRequestValidator.MaxFrames + 1)]),
            "more frames than a bundle holds",
            FlybyBlenderBundleEndpoints.TooLongCode);
        await Refused(Request(spot, [cave], leftOut: ["ground", "everything"]), "a thing left out that has no name", "validation.invalid_enum");
    }

    // ---- helpers ----

    /// <summary>The middle of one test's scene: a point on open ocean nobody else is using.</summary>
    private sealed record Spot(double Longitude, double Latitude)
    {
        public static Spot Somewhere() => new(
            Math.Round(-170d + (Random.Shared.NextDouble() * 60d), 6),
            Math.Round(-60d + (Random.Shared.NextDouble() * 30d), 6));

        public Spot East(double degrees) => new(Longitude + degrees, Latitude);
    }

    private static object Cave(Guid caveId, bool walls, double? surveyTopM = null) =>
        new { caveId, walls, surveyTopM, groundM = (double?)null };

    /// <summary>
    /// A request as the browser would send one: ten frames of a camera standing 400 m south of the
    /// spot and looking at it, a scene with relief unless told otherwise, the origin 600 m up.
    /// </summary>
    private static object Request(
        Spot spot,
        object[] caves,
        bool absolute = true,
        double originHeightM = 600,
        bool lines = true,
        double fps = 25,
        double relief = 1,
        int width = 1280,
        string? sky = "#102030",
        double[][]? frames = null,
        string[]? leftOut = null) => new
        {
            name = "An invented flyby",
            output = new { width, height = 720, fps, quality = "low" },
            origin = new { longitude = spot.Longitude, latitude = spot.Latitude, heightM = originHeightM },
            placement = new { absolute, offsetM = 0 },
            relief,
            frames = frames ?? [.. Enumerable.Range(0, 10)
                .Select(i => new[] { spot.Longitude, spot.Latitude - 0.0036, 1200d, i * 2d, -30d, 0d, 400d, 60d })],
            caves,
            look = new
            {
                lines,
                sky,
                lineColour = (string?)null,
                depthBands = new[] { new { fromM = 0d, colour = "#d9480f" }, new { fromM = 50d, colour = "#8c2f6b" } },
            },
            texts = new
            {
                title = new { text = "An invented flyby", subtitle = (string?)null, seconds = 0.2 },
                end = (object?)null,
                captions = Array.Empty<object>(),
                corner = (string?)null,
                cornerCaptions = Array.Empty<object>(),
                fadeInS = 0d,
                fadeOutS = 0d,
                northArrow = true,
            },
            leftOut = leftOut ?? ["ground", "entrances"],
        };

    private static int Header(HttpResponseMessage answer, string name) =>
        int.Parse(answer.Headers.GetValues(name).Single(), CultureInfo.InvariantCulture);

    private static async Task<string?> CodeAsync(HttpResponseMessage answer) =>
        (await answer.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out var code) ? code.GetString() : null;

    private static double[] Translation(JsonElement model) =>
        model.TryGetProperty("translation", out var at) ? [.. at.EnumerateArray().Select(n => n.GetDouble())] : [0, 0, 0];

    /// <summary>A bundle read back: its files by their path inside its one folder.</summary>
    private sealed record Bundle(Dictionary<string, byte[]> Entries, JsonElement Plan)
    {
        public IReadOnlyList<string> Files => [.. Entries.Keys.Order(StringComparer.Ordinal)];

        public IReadOnlyList<string> Meshes => [.. Files.Where(f => f.EndsWith(".ply", StringComparison.Ordinal))];

        public Dictionary<string, JsonElement> Models =>
            Plan.GetProperty("models").EnumerateArray().ToDictionary(m => m.GetProperty("id").GetString()!);

        public static async Task<Bundle> ReadAsync(HttpResponseMessage answer)
        {
            answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());
            using var archive = new ZipArchive(new MemoryStream(await answer.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);
            var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
            {
                using var held = new MemoryStream();
                await using (var input = entry.Open())
                {
                    await input.CopyToAsync(held);
                }

                // Without the one folder everything lies in.
                entries[entry.FullName[(entry.FullName.IndexOf('/') + 1)..]] = held.ToArray();
            }

            return new Bundle(entries, JsonDocument.Parse(entries["scene-plan.json"]).RootElement.Clone());
        }
    }

    private async Task<Guid> CreateCaveAsync(
        string visibility = "authenticated", bool locationProtected = false, Guid? parentId = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Bundle Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
            parentId,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// A cave with one invented survey, read: three legs from a zero point at the spot, the first
    /// of them measured at both ends — so the cave has walls — unless told otherwise.
    /// </summary>
    private async Task<Guid> CreateSurveyedCaveAsync(
        Spot at,
        double heightM,
        string visibility = "authenticated",
        bool locationProtected = false,
        Guid? parentId = null,
        bool measured = true)
    {
        var caveId = await CreateCaveAsync(visibility, locationProtected, parentId);

        var content = new ByteArrayContent(Survey(measured));
        content.Headers.ContentType = new("application/octet-stream");
        using var form = new MultipartFormDataContent
        {
            { content, "file", "Invented.lox" },
            { new StringContent(at.Longitude.ToString("R", CultureInfo.InvariantCulture)), "originLongitude" },
            { new StringContent(at.Latitude.ToString("R", CultureInfo.InvariantCulture)), "originLatitude" },
            { new StringContent(heightM.ToString("R", CultureInfo.InvariantCulture)), "originHeightM" },
        };
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var modelId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var mine = (await QueuedJob.OfKindAsync(factory.Services, ProcessingJobKinds.SurveyGraph))
            .Where(j => JsonSerializer.Deserialize<SurveyGraphPayload>(j.Payload, JsonSerializerOptions.Web)
                ?.SurveyModelId == modelId)
            .ToList();
        mine.ShouldHaveSingleItem();
        await QueuedJob.RunAsync(factory.Services, mine[0].Id);

        var read = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-models/{modelId}");
        read.GetProperty("status").GetString().ShouldBe("ready", read.ToString());
        return caveId;
    }

    /// <summary>Four stations going down and east from the file's zero; walls measured along the first leg, or nowhere.</summary>
    private static byte[] Survey(bool measured)
    {
        CaveLrud? atA = measured ? new CaveLrud(Left: 1.5, Right: 0.5, Up: 2, Down: 0.3) : null;
        CaveLrud? atB = measured ? new CaveLrud(Left: 0.8, Right: 1.2, Up: 1, Down: 1) : null;
        CaveShot Shot(uint from, uint to, CaveLrud? atStart, CaveLrud? atEnd) => new()
        {
            FromStationId = from,
            ToStationId = to,
            FromLrud = atStart,
            ToLrud = atEnd,
            SectionType = CaveShotSection.Oval,
        };

        return LoxWriter.Write(new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox,
            Stations =
            [
                new CaveStation { Id = 1, Name = "A", Position = new CaveVector3(0, 0, 0) },
                new CaveStation { Id = 2, Name = "B", Position = new CaveVector3(12, 5, -3) },
                new CaveStation { Id = 3, Name = "C", Position = new CaveVector3(20, 5, -30) },
                new CaveStation { Id = 4, Name = "D", Position = new CaveVector3(31, 9, -64) },
            ],
            Shots = [Shot(1, 2, atA, atB), Shot(2, 3, null, null), Shot(3, 4, null, null)],
        });
    }

    /// <summary>Grants the reader Read + ViewExactLocation on one feature, whatever its kind.</summary>
    private async Task GrantExactViewAsync(Guid featureId)
    {
        var grant = await owner.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = readerId,
                    effect = "allow",
                    actions = "read, viewExactLocation",
                    scopeKind = "object",
                },
            },
        });
        grant.StatusCode.ShouldBe(HttpStatusCode.OK, await grant.Content.ReadAsStringAsync());
    }

    /// <summary>A location-protected karst area: a protection root that is not a cave.</summary>
    private async Task<Guid> CreateProtectedAreaAsync()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Protected Karst {Guid.NewGuid():N}"[..30],
            featureTypeId = karstAreaTypeId,
            geometry = (object?)null,
            description = (string?)null,
            locationProtected = true,
            visibility = "authenticated",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        factory.Dispose();
        try
        {
            if (Directory.Exists(filesRoot))
            {
                Directory.Delete(filesRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temp files; best effort.
        }
    }
}
