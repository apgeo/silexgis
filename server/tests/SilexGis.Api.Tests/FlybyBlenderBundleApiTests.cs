// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Features.Flybys;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Infrastructure.Flybys;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;
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
        readme.ShouldContain("the point-cloud scans");

        // And they open as this application's, saying how the scene is laid out — not where.
        readme.ShouldContain("A flyby of caves made by SilexGIS for Blender");
        readme.ShouldContain("HOW THE SCENE IS LAID OUT");

        // The lines' colours by depth are a ramp in each cave's own mesh.
        var materials = bundle.Plan.GetProperty("look").GetProperty("materials").EnumerateArray()
            .ToDictionary(m => m.GetProperty("id").GetString()!);
        foreach (var id in (string[])["cave-01-lines", "cave-02-lines"])
        {
            var worn = materials[models[id].GetProperty("material").GetString()!];
            worn.GetProperty("kind").GetString().ShouldBe("heightRamp");
            worn.GetProperty("space").GetString().ShouldBe("model");
        }
    }

    [Fact]
    public async Task The_notes_of_a_bundle_are_in_Romanian_for_a_reader_who_has_the_application_in_Romanian()
    {
        var spot = Spot.Somewhere();
        var cave = await CreateSurveyedCaveAsync(spot, heightM: 900);

        var answer = await owner.PostAsJsonAsync(
            Route, Request(spot, [Cave(cave, walls: true), Cave(Guid.NewGuid(), walls: true)], language: "ro-RO"));
        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());
        var readme = Encoding.UTF8.GetString((await Bundle.ReadAsync(answer)).Entries["README.txt"]);

        readme.ShouldContain("Un survol printre peșteri, făcut de SilexGIS pentru Blender");
        readme.ShouldContain("CUM SE RANDEAZĂ");
        readme.ShouldContain("CE LIPSEȘTE DIN ACEASTĂ SCENĂ");
        // What the browser named, and what only the server knows, in the same language.
        readme.ShouldContain("  - terenul și harta desenată pe el");
        readme.ShouldContain("  - scanările");
        readme.ShouldContain("  - 1 dintre peșterile cerute (nimic din ea nu este disponibil pentru desenare)");
        readme.ShouldNotContain("the ground and the map");

        // Any other language is given the English notes.
        var german = await owner.PostAsJsonAsync(Route, Request(spot, [Cave(cave, walls: true)], language: "de"));
        Encoding.UTF8.GetString((await Bundle.ReadAsync(german)).Entries["README.txt"]).ShouldContain("HOW TO RENDER");
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

        // And when the flyby colours that cave apart: its colours go nowhere, whichever it is.
        var made = Guid.NewGuid();
        // With the daylight going and the notes in Romanian, too: nothing of either tells them apart.
        object Lit(Guid caveId) => Changes(
            relief: [Number(0, 1), Number(9, 1.5, "smooth")],
            caves: [new { caveId, walls = new[] { Tint(0, "#ffcc00", 0.85) }, lines = new[] { Tint(0, "#ffcc00", 1) } }],
            daylight: [Number(0, 1), Number(3, 1), Number(28, 0, "smooth")]);
        var withheldLit = await reader.PostAsJsonAsync(
            Route, Request(spot, [Cave(shared, walls: true), Cave(hidden, walls: true)], changes: Lit(hidden), language: "ro"));
        var inventedLit = await reader.PostAsJsonAsync(
            Route, Request(spot, [Cave(shared, walls: true), Cave(made, walls: true)], changes: Lit(made), language: "ro"));
        withheldLit.StatusCode.ShouldBe(HttpStatusCode.OK, await withheldLit.Content.ReadAsStringAsync());
        (await withheldLit.Content.ReadAsByteArrayAsync()).ShouldBe(await inventedLit.Content.ReadAsByteArrayAsync());
        var lit = await Bundle.ReadAsync(withheldLit);
        // A colour of a cave that is not drawn is on no model.
        lit.Plan.GetProperty("changes").EnumerateArray().Count(c => c.TryGetProperty("tint", out _)).ShouldBe(0);
        Encoding.UTF8.GetString(lit.Entries["README.txt"]).ShouldContain("1 dintre peșterile cerute");

        // Asked for alone, there is no scene — again the same answer either way.
        var alone = await reader.PostAsJsonAsync(Route, Request(spot, [Cave(hidden, walls: true)]));
        var nothing = await reader.PostAsJsonAsync(Route, Request(spot, [Cave(Guid.NewGuid(), walls: true)]));
        alone.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        nothing.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(alone)).ShouldBe(FlybyBlenderBundleEndpoints.NothingToDrawCode);
        (await CodeAsync(nothing)).ShouldBe(FlybyBlenderBundleEndpoints.NothingToDrawCode);
    }

    [Fact]
    public async Task What_a_flyby_changes_while_it_plays_comes_back_as_tracks_on_the_caves_drawn()
    {
        var spot = Spot.Somewhere();
        var first = await CreateSurveyedCaveAsync(spot, heightM: 900);
        var second = await CreateSurveyedCaveAsync(spot.East(0.01), heightM: 860);

        // Ten frames: the relief doubles over them, the sky is cut to another colour, every cave's
        // walls are dimmed and fade out from the fifth frame — gone on the frame after the clip's
        // last —, the first cave is lit, the lines are thinned, and the camera goes under the
        // ground on the fourth frame, so the daylight is on its way out when the clip ends.
        var changes = Changes(
            relief: [Number(0, 1), Number(9, 2, "smooth")],
            sky: [new { frame = 0, colour = "#102030", easing = (string?)null }, new { frame = 5, colour = "#000000", easing = "constant" }],
            walls: Layer(
                shown: [new { frame = 4, shown = false, fadeFrames = 6 }],
                tint: [Tint(0, "#3a3f4a", 0.6)]),
            lines: Layer(opacity: [Number(0, 0.5)]),
            caves:
            [
                new { caveId = first, walls = new[] { Tint(0, "#3a3f4a", 0.6), Tint(4, "#ffcc00", 0.85, "smooth") }, lines = (object[]?)null },
            ],
            daylight: [Number(0, 1), Number(3, 1), Number(28, 0, "smooth")]);
        var answer = await owner.PostAsJsonAsync(
            Route, Request(spot, [Cave(first, walls: true), Cave(second, walls: false)], changes: changes));
        var bundle = await Bundle.ReadAsync(answer);

        bundle.Plan.GetProperty("features").EnumerateArray().Select(f => f.GetString())
            .ShouldBe(["models", "camera_track", "look", "changes", "texts", "output"]);
        var tracks = bundle.Plan.GetProperty("changes").EnumerateArray().ToList();
        JsonElement Of(string id) => tracks.Single(c => c.TryGetProperty("target", out var target) && target.GetProperty("id").GetString() == id);

        // Every model is stretched by the relief's keys, on the clip's own clock.
        foreach (var id in bundle.Models.Keys)
        {
            // (A model: the plan's changes are of the models, of the sun and of the world.)
            var scale = Of(id).GetProperty("scale").EnumerateArray().ToList();
            scale.Select(k => k.GetProperty("t").GetDouble()).ShouldBe([0, 0.36]);
            scale[1].GetProperty("value")[2].GetDouble().ShouldBe(2);
            scale[1].GetProperty("easing").GetString().ShouldBe("smooth");
        }

        // The camera, 1,200 m up over an origin at 600: 600 above it at first, twice that at the end.
        var eyes = bundle.Plan.GetProperty("camera").GetProperty("track").EnumerateArray()
            .Select(pose => pose.GetProperty("eye")[2].GetDouble()).ToList();
        eyes[0].ShouldBe(600, 0.05);
        eyes[9].ShouldBe(1200, 0.05);
        eyes.Zip(eyes.Skip(1)).ShouldAllBe(pair => pair.Second > pair.First);

        // The first cave's walls: going with every cave's — eased, over the frames the flyby takes —
        // in its own colour and not the common one, which it crosses from as a screen mixes them.
        var walls = Of("cave-01-walls");
        var going = walls.GetProperty("visible").EnumerateArray().Single();
        going.GetProperty("value").GetBoolean().ShouldBeFalse();
        going.GetProperty("t").GetDouble().ShouldBe(0.16);
        going.GetProperty("fade").GetDouble().ShouldBe(0.24);
        going.GetProperty("easing").GetString().ShouldBe("smooth");
        var tint = walls.GetProperty("tint").EnumerateArray().ToList();
        tint.Select(k => k.GetProperty("strength").GetDouble()).ShouldBe([0.6, 0.85]);
        tint[1].GetProperty("mix").GetString().ShouldBe("srgb");

        // The lines go with the lines' layer: thinned, never hidden, in their colours by depth —
        // a ramp in each cave's own mesh, which the stretch of the relief does not move.
        var materials = bundle.Plan.GetProperty("look").GetProperty("materials").EnumerateArray()
            .ToDictionary(m => m.GetProperty("id").GetString()!);
        foreach (var id in (string[])["cave-01-lines", "cave-02-lines"])
        {
            Of(id).GetProperty("opacity").EnumerateArray().Single().GetProperty("value").GetDouble().ShouldBe(0.5);
            Of(id).TryGetProperty("visible", out _).ShouldBeFalse();
            Of(id).TryGetProperty("tint", out _).ShouldBeFalse();
            var worn = materials[bundle.Models[id].GetProperty("material").GetString()!];
            worn.GetProperty("kind").GetString().ShouldBe("heightRamp");
            worn.GetProperty("space").GetString().ShouldBe("model");
        }

        // The sky: a world of one colour, cut to black on the sixth frame — and as strong as the
        // daylight, which is on its way out: its last key is after the clip's last frame.
        bundle.Plan.GetProperty("look").GetProperty("world").GetProperty("kind").GetString().ShouldBe("color");
        var world = tracks.Single(c => !c.TryGetProperty("target", out _));
        var sky = world.GetProperty("worldColor").EnumerateArray().ToList();
        sky[1].GetProperty("t").GetDouble().ShouldBe(0.2);
        sky[1].GetProperty("easing").GetString().ShouldBe("constant");
        world.GetProperty("worldStrength").EnumerateArray().Select(k => k.GetProperty("value").GetDouble()).ShouldBe([1, 1, 0]);

        // The sun goes with it, by the same keys; and as it goes the lamp on the camera is turned
        // up towards what lights the first cave's walls from where the camera is — from the lamp
        // as it is built, while the daylight is whole.
        var lights = bundle.Plan.GetProperty("look").GetProperty("lights").EnumerateArray().ToDictionary(l => l.GetProperty("id").GetString()!);
        var sun = Of("Sun").GetProperty("energy").EnumerateArray().ToList();
        sun.Select(k => k.GetProperty("t").GetDouble()).ShouldBe([0, 0.12, 1.12]);
        sun[0].GetProperty("value").GetDouble().ShouldBe(lights["Sun"].GetProperty("energy").GetDouble());
        sun[2].GetProperty("value").GetDouble().ShouldBe(0);
        var lamp = Of("Headlamp").GetProperty("energy").EnumerateArray().ToList();
        var built = lights["Headlamp"].GetProperty("energy").GetDouble();
        lamp[0].GetProperty("t").GetDouble().ShouldBe(0);
        lamp[0].GetProperty("value").GetDouble().ShouldBe(built);
        lamp[1].GetProperty("t").GetDouble().ShouldBe(0.12);
        lamp[1].GetProperty("value").GetDouble().ShouldBe(built);
        lamp[^1].GetProperty("t").GetDouble().ShouldBe(0.36);
        lamp[^1].GetProperty("value").GetDouble().ShouldBeGreaterThan(built);
        lamp.Select(k => k.GetProperty("value").GetDouble()).ShouldBe(lamp.Select(k => k.GetProperty("value").GetDouble()).Order());
        var notes = Encoding.UTF8.GetString(bundle.Entries["README.txt"]);
        notes.ShouldContain("The sun and the sky go out while the camera is under");
        notes.ShouldContain("the lamp is turned up by the square of the distance");

        // The lines of both caves wear a material no light changes.
        foreach (var id in (string[])["cave-01-lines", "cave-02-lines"])
        {
            materials[bundle.Models[id].GetProperty("material").GetString()!].GetProperty("unlit").GetBoolean().ShouldBeTrue();
        }

        // Still no place in it: with the relief changing, each model is stretched about the origin's
        // own level — its height above that, never its height above the sea, is all a file holds.
        foreach (var model in bundle.Models.Values)
        {
            Math.Abs(Translation(model)[2]).ShouldBeLessThan(1);
        }

        foreach (var (name, bytes) in bundle.Entries.Where(e => !e.Key.EndsWith(".ply", StringComparison.Ordinal)))
        {
            Encoding.UTF8.GetString(bytes).ShouldNotContain(
                spot.Latitude.ToString("0.00", CultureInfo.InvariantCulture), Case.Sensitive, name);
        }
    }

    [Fact]
    public async Task Lines_that_stand_in_for_walls_a_cave_has_none_of_are_shown_and_hidden_as_the_walls_are()
    {
        var spot = Spot.Somewhere();
        var bare = await CreateSurveyedCaveAsync(spot, heightM: 900, measured: false);

        // Walls wanted, none there; the look draws no lines at all. The walls are hidden at frame 6.
        var changes = Changes(walls: Layer(shown: [new { frame = 6, shown = false, fadeFrames = (int?)null }]), lines: Layer(opacity: [Number(0, 0.25)]));
        var answer = await owner.PostAsJsonAsync(Route, Request(spot, [Cave(bare, walls: true)], lines: false, changes: changes));
        var bundle = await Bundle.ReadAsync(answer);

        bundle.Meshes.ShouldBe(["cave-01-lines.ply"]);
        var only = bundle.Plan.GetProperty("changes").EnumerateArray().Single();
        only.GetProperty("target").GetProperty("id").GetString().ShouldBe("cave-01-lines");
        only.GetProperty("visible").EnumerateArray().Single().GetProperty("t").GetDouble().ShouldBe(0.24);
        // Not the lines' layer's strength: these lines are what there is of the cave's walls.
        only.TryGetProperty("opacity", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Changes_that_are_not_keys_on_the_clips_frames_are_refused()
    {
        var spot = Spot.Somewhere();
        var named = Guid.NewGuid();
        var cave = Cave(named, walls: true);

        async Task Refused(object changes, string because, string code = "validation.failed")
        {
            var answer = await owner.PostAsJsonAsync(Route, Request(spot, [cave], changes: changes));
            answer.StatusCode.ShouldBe(HttpStatusCode.BadRequest, because);
            (await CodeAsync(answer)).ShouldBe(code, because);
        }

        // The clip has ten frames, 0 to 9. A key may lie after the last — the clip ends on the way
        // to it — and before the first — it begins on the way from it —, but no further either
        // way than a clip is long.
        await Refused(Changes(relief: [Number(0, 1), Number(9 + 20_001, 2)]), "a key further past the clip than a clip is long");
        await Refused(Changes(relief: [Number(-20_001, 1), Number(9, 2)]), "a key further before the clip than a clip is long");
        await Refused(Changes(relief: [Number(4, 1), Number(4, 2)]), "a second key on a frame that is no jump");
        await Refused(
            Changes(relief: [Number(4, 1), Number(4, 2, "constant"), Number(4, 3)]), "a third key on a frame that is no jump");
        await Refused(
            Changes(relief: [Number(4, 1), Number(4, 2, "constant"), Number(4, 3, "constant"), Number(4, 4, "constant")]),
            "four keys on one frame");
        await Refused(Changes(daylight: [Number(0, 1.5)]), "more daylight than all of it");
        await Refused(Changes(daylight: [Number(3, 1), Number(1, 0)]), "daylight out of order");
        await Refused(
            Changes(walls: Layer(shown: [new { frame = 2, shown = false, fadeFrames = -1 }])), "a fade of fewer than no frames");
        await Refused(
            Changes(walls: Layer(shown: [new { frame = 12, shown = false, fadeFrames = 0 }])), "a layer hidden after the clip's last frame");
        await Refused(
            Changes(walls: Layer(shown: [new { frame = 2, shown = false, fadeFrames = 0 }, new { frame = 2, shown = true, fadeFrames = 0 }])),
            "a layer hidden and shown on one frame");
        await Refused(Changes(relief: [Number(5, 1), Number(2, 2)]), "keys out of order");
        await Refused(Changes(relief: []), "a track of no keys");
        await Refused(Changes(relief: [Number(0, 9)]), "a relief past the tallest the scene draws");
        await Refused(
            Changes(walls: Layer(shown: [new { frame = -3, shown = false, fadeFrames = 0 }])), "a layer cut before the clip's first frame");
        await Refused(
            Changes(walls: Layer(shown: [new { frame = -8, shown = false, fadeFrames = 8 }])), "a fade that was over by the clip's first frame");
        await Refused(
            Changes(walls: Layer(shown: [new { frame = -20_001, shown = false, fadeFrames = 20_000 }])),
            "a fade that began further before the clip than a clip is long");
        await Refused(Changes(sky: [new { frame = 0, colour = "navy", easing = (string?)null }]), "a sky that is a word");
        await Refused(Changes(walls: Layer(opacity: [Number(0, 1.5)])), "more of a layer than all of it");
        await Refused(Changes(walls: Layer(tint: [Tint(0, "#3a3f4a", 2)])), "a colour stronger than whole");
        await Refused(
            Changes(lines: Layer(shown: [new { frame = 2, shown = false, fadeFrames = 0 }, new { frame = 5, shown = false, fadeFrames = 0 }])),
            "a layer hidden twice running");
        await Refused(
            Changes(caves: [new { caveId = Guid.NewGuid(), walls = new[] { Tint(0, "#ffcc00", 0.5) }, lines = (object[]?)null }]),
            "a cave coloured apart that the request does not name");
        await Refused(
            Changes(caves: [new { caveId = named, walls = new[] { Tint(3, "#ffcc00", 0.5), Tint(1, "#ffcc00", 0.5) }, lines = (object[]?)null }]),
            "a cave's colours out of order");
        await Refused(Changes(relief: [Number(0, 1), Number(9, 2, "bouncy")]), "an easing that has no name", "validation.invalid_enum");

        // A language that is no language tag.
        var gibberish = await owner.PostAsJsonAsync(Route, Request(spot, [cave], language: "the one I speak"));
        gibberish.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(gibberish)).ShouldBe("validation.failed");

        // And keys in order and in range are taken — a change that ends after the clip and one
        // that began before it, a move that ends in a jump on its own last frame, a third key on
        // that frame for the next move to start from, a layer that takes frames to go and one
        // that was going when the clip began, the daylight: the cave is made up, so there is
        // nothing to draw — which is a refusal of another kind, further on.
        var begun = await owner.PostAsJsonAsync(
            Route,
            Request(spot, [cave], changes: Changes(
                relief: [Number(-20, 1), Number(30, 2, "smooth")],
                walls: Layer(shown: [new { frame = -5, shown = false, fadeFrames = 12 }], opacity: [Number(-4, 1), Number(4, 0.5, "smooth")]),
                caves:
                [
                    new
                    {
                        caveId = named,
                        walls = new[] { Tint(0, "#3a3f4a", 0.6), Tint(3, "#3a3f4a", 0.6, "constant"), Tint(3, "#ffcc00", 0, "constant"), Tint(8, "#ffcc00", 0.85, "smooth") },
                        lines = (object[]?)null,
                    },
                ])));
        begun.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await begun.Content.ReadAsStringAsync());

        var taken = await owner.PostAsJsonAsync(
            Route,
            Request(spot, [cave], language: "ro-RO", changes: Changes(
                relief: [Number(0, 1), Number(30, 2, "smooth")],
                sky: [
                    new { frame = 0, colour = "#102030", easing = (string?)null },
                    new { frame = 5, colour = "#000000", easing = "smooth" },
                    new { frame = 5, colour = "#203040", easing = "constant" },
                ],
                walls: Layer(shown: [new { frame = 9, shown = false, fadeFrames = 40 }]),
                caves: [new { caveId = named, walls = new[] { Tint(1, "#ffcc00", 0.5), Tint(1, "#ffcc00", 0.2, "constant") }, lines = (object[]?)null }],
                daylight: [Number(0, 1), Number(4, 0, "smooth"), Number(20_009, 1)])));
        taken.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await taken.Content.ReadAsStringAsync());
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
    // ---- what a bundle points out, and for whom ----
    //
    // Entrances and features as markers, the photographs somebody linked to stations of a cave's
    // survey, and the caves appearing part by part. Every entrance, feature and photograph below
    // is invented — the pictures are bands of colour made in the test, one of them stamped with a
    // made-up position on open ocean so that there is something for a bundle to be shown not to
    // carry.
    [Fact]
    public async Task Entrances_and_features_named_by_id_come_back_as_markers_and_what_cannot_be_drawn_as_one_count()
    {
        var spot = Spot.Somewhere();
        var cave = await CreateSurveyedCaveAsync(spot, heightM: 900);
        var named = await CreateEntranceAsync(cave, spot.East(0.0002), "Lower mouth");
        var nameless = await CreateEntranceAsync(cave, spot.East(-0.0002), name: null);
        var sinkhole = await CreateFeatureAsync(Point(spot.East(0.001)));
        var outline = await CreateFeatureAsync(new
        {
            type = "Polygon",
            coordinates = new[]
            {
                new[]
                {
                    new[] { spot.Longitude, spot.Latitude }, new[] { spot.Longitude + 0.001, spot.Latitude },
                    new[] { spot.Longitude + 0.001, spot.Latitude + 0.001 }, new[] { spot.Longitude, spot.Latitude },
                },
            },
        });

        var answer = await owner.PostAsJsonAsync(Route, Request(
            spot,
            [Cave(cave, walls: true)],
            markers:
            [
                MarkerLayer("entrances", "#146262", [Marker(named, 905, named: true), Marker(nameless, 903, named: true), Marker(Guid.NewGuid(), 900)]),
                MarkerLayer("features", "#722ed1", [Marker(sinkhole, 910), Marker(outline, 910)], shown: [new { frame = 4, shown = false, fadeFrames = 2 }]),
            ]));

        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());
        Header(answer, FlybyBlenderBundleEndpoints.MarkersHeader).ShouldBe(3);
        // One made up and one that is no point: one number, and no word about which is which.
        Header(answer, FlybyBlenderBundleEndpoints.MarkersNotDrawnHeader).ShouldBe(2);
        Header(answer, FlybyBlenderBundleEndpoints.PhotosHeader).ShouldBe(0);

        var bundle = await Bundle.ReadAsync(answer);
        var said = bundle.Plan.GetProperty("annotations").EnumerateArray().ToDictionary(a => a.GetProperty("id").GetString()!);
        said.Keys.ShouldBe(["entrance-001", "entrance-001-name", "entrance-002", "entrance-002-name", "feature-layer-1-001"], ignoreOrder: true);
        said["entrance-001-name"].GetProperty("text").GetString().ShouldBe("Lower mouth");
        // An entrance with no name of its own goes by its cave's.
        said["entrance-002-name"].GetProperty("text").GetString().ShouldStartWith("Bundle Cave ");

        // Each rides the cave's walls, at the place the route read for it: metres east of the
        // cave's own zero, and as far above it as the ground the request gave is above the origin
        // the survey was placed at.
        var at = said["entrance-001"].GetProperty("at").EnumerateArray().Select(n => n.GetDouble()).ToArray();
        said["entrance-001"].GetProperty("model").GetString().ShouldBe("cave-01-walls");
        at[0].ShouldBeInRange(5, 25);
        at[1].ShouldBe(0, 0.05);
        at[2].ShouldBe(5, 0.05);
        said["feature-layer-1-001"].GetProperty("role").GetString().ShouldBe("feature-layer-1");

        // The layer the request hides is hidden by one change for its role.
        var hidden = bundle.Plan.GetProperty("changes").EnumerateArray()
            .Single(c => c.TryGetProperty("target", out var target) && target.TryGetProperty("role", out var role) && role.GetString() == "feature-layer-1");
        hidden.GetProperty("visible")[0].GetProperty("value").GetBoolean().ShouldBeFalse();

        var readme = Encoding.UTF8.GetString(bundle.Entries["README.txt"]);
        readme.ShouldContain("2 of the entrances and features asked for (nothing of them is available to draw)");
        readme.ShouldContain("The scene has 3 markers,");
        readme.ShouldNotContain(spot.Longitude.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));

        // And it is on record which markers went in — by id, with no position.
        var record = await LastRecordAsync(cave);
        record.GetProperty("markerFeatureIds").EnumerateArray().Select(id => id.GetGuid()).ShouldBe([named, nameless, sinkhole], ignoreOrder: true);
        record.GetProperty("markersNotDrawnCount").GetInt32().ShouldBe(2);
        record.GetRawText().ShouldNotContain("coordinates");
    }

    [Fact]
    public async Task A_marker_the_caller_may_not_read_or_may_not_place_exactly_looks_exactly_like_one_that_does_not_exist()
    {
        var spot = Spot.Somewhere();
        var cave = await CreateSurveyedCaveAsync(spot, heightM: 900);
        var open = await CreateFeatureAsync(Point(spot.East(0.001)));
        var unread = await CreateFeatureAsync(Point(spot.East(0.002)), visibility: "private");
        var guarded = await CreateFeatureAsync(Point(spot.East(0.003)), locationProtected: true);

        // An entrance of a cave whose place is protected: readable, shown on a map moved to the
        // grid, and so no marker — a marker stands at a distance from the caves, and one put on
        // the grid would say where the caves lie on it.
        var protectedCave = await CreateCaveAsync(locationProtected: true);
        var itsMouth = await CreateEntranceAsync(protectedCave, spot.East(0.004), "A mouth kept from the map");

        object Asked(Guid id) => Request(
            spot, [Cave(cave, walls: true)], markers: [MarkerLayer("features", "#722ed1", [Marker(open, 900), Marker(id, 900)])]);

        // The positive half first: their owner gets every one of them.
        foreach (var id in (Guid[])[unread, guarded, itsMouth])
        {
            var mine = await owner.PostAsJsonAsync(Route, Asked(id));
            Header(mine, FlybyBlenderBundleEndpoints.MarkersHeader).ShouldBe(2);
            Header(mine, FlybyBlenderBundleEndpoints.MarkersNotDrawnHeader).ShouldBe(0);
        }

        var invented = await reader.PostAsJsonAsync(Route, Asked(Guid.NewGuid()));
        Header(invented, FlybyBlenderBundleEndpoints.MarkersHeader).ShouldBe(1);
        Header(invented, FlybyBlenderBundleEndpoints.MarkersNotDrawnHeader).ShouldBe(1);
        var none = await invented.Content.ReadAsByteArrayAsync();
        foreach (var id in (Guid[])[unread, guarded, itsMouth])
        {
            // Byte for byte the file a made-up id gets: not a count, not a sentence, not a marker
            // tells one kept back from one that is not there.
            var withheld = await reader.PostAsJsonAsync(Route, Asked(id));
            withheld.StatusCode.ShouldBe(HttpStatusCode.OK, await withheld.Content.ReadAsStringAsync());
            (await withheld.Content.ReadAsByteArrayAsync()).ShouldBe(none);
        }

        // Granted its exact place, the reader gets the marker.
        await GrantExactViewAsync(guarded);
        Header(await reader.PostAsJsonAsync(Route, Asked(guarded)), FlybyBlenderBundleEndpoints.MarkersHeader).ShouldBe(2);
    }

    [Fact]
    public async Task Photographs_linked_to_stations_go_in_as_renderings_and_one_the_caller_may_not_read_is_not_there_at_all()
    {
        var spot = Spot.Somewhere();
        var cave = await CreateSurveyedCaveAsync(spot, heightM: 900);
        var modelId = await LinePlotOfAsync(cave);

        // Two invented pictures: a large one stamped with a made-up position, shared with every
        // signed-in reader; and one in a format a renderer does not read, left private.
        var (sharedDocument, _) = await UploadPictureAsync("shared.jpg", StampedPicture(2600, 1300), "image/jpeg");
        await ShareDocumentAsync(sharedDocument);
        var (privateDocument, _) = await UploadPictureAsync("private.webp", PlainPicture(300, 400, MagickFormat.WebP), "image/webp");
        await LinkToStationAsync(modelId, "A", sharedDocument);
        await LinkToStationAsync(modelId, "C", privateDocument);

        var asked = Request(spot, [Cave(cave, walls: true)], photos: new { caves = new[] { cave }, shown = (object?)null });

        // Their owner gets both, each a JPEG a renderer reads, no longer than two thousand pixels
        // on a side, turned as the application shows it, and with nothing of the camera's in it.
        var mine = await Bundle.ReadAsync(await owner.PostAsJsonAsync(Route, asked));
        mine.Files.Where(f => f.StartsWith("photos/", StringComparison.Ordinal)).ShouldBe(["photos/photo-001.jpg", "photos/photo-002.jpg"]);
        var sizes = new List<(uint Width, uint Height)>();
        foreach (var name in (string[])["photos/photo-001.jpg", "photos/photo-002.jpg"])
        {
            using var picture = new MagickImage(mine.Entries[name]);
            picture.Format.ShouldBe(MagickFormat.Jpeg);
            picture.GetExifProfile().ShouldBeNull($"{name} carries nothing the camera wrote");
            picture.GetXmpProfile().ShouldBeNull();
            Math.Max(picture.Width, picture.Height).ShouldBeLessThanOrEqualTo((uint)FlybyBlenderBundleEndpoints.PhotoLongestSidePx);
            sizes.Add((picture.Width, picture.Height));
        }

        sizes.ShouldBe([(2000u, 1000u), (300u, 400u)], ignoreOrder: true);

        // Each stands at the station it was linked to: a point of the cave's walls, where the
        // survey has that station — its zero, and twenty metres east and thirty down.
        var places = mine.Plan.GetProperty("annotations").EnumerateArray()
            .Where(a => a.GetProperty("kind").GetString() == "photo")
            .Select(a => a.GetProperty("at").EnumerateArray().Select(n => n.GetDouble()).ToArray())
            .OrderBy(at => at[2])
            .ToList();
        places.Count.ShouldBe(2);
        places[1].ShouldBe([0, 0, 0], 0.05);
        places[0][0].ShouldBe(20, 0.05);
        places[0][2].ShouldBe(-30, 0.05);
        mine.Plan.GetProperty("annotations").EnumerateArray()
            .ShouldAllBe(a => a.GetProperty("model").GetString() == "cave-01-walls" && a.GetProperty("role").GetString() == FlybyBlenderBundle.PhotoRole);

        // No file of the bundle says where the stamped picture was taken, or what it was called.
        foreach (var (name, bytes) in mine.Entries)
        {
            var text = Encoding.Latin1.GetString(bytes);
            text.ShouldNotContain("shared.jpg", Case.Insensitive, name);
            text.ShouldNotContain("GPSLatitude", Case.Insensitive, name);
        }

        // The reader may read one of the two documents, and gets that one. Nothing of the other:
        // not its file, not a count of what is missing, not a line in the notes or on record.
        var answer = await reader.PostAsJsonAsync(Route, asked);
        Header(answer, FlybyBlenderBundleEndpoints.PhotosHeader).ShouldBe(1);
        var theirs = await Bundle.ReadAsync(answer);
        theirs.Files.Where(f => f.StartsWith("photos/", StringComparison.Ordinal)).ShouldBe(["photos/photo-001.jpg"]);
        using (var only = new MagickImage(theirs.Entries["photos/photo-001.jpg"]))
        {
            (only.Width, only.Height).ShouldBe((2000u, 1000u));
        }

        Encoding.UTF8.GetString(theirs.Entries["README.txt"]).ShouldNotContain("taken at station");
        var record = await LastRecordAsync(cave);
        record.GetProperty("photoDocumentIds").EnumerateArray().Select(id => id.GetGuid()).ShouldBe([sharedDocument]);

        // A second photograph at a station that shows one: counted, and said in the notes.
        var (third, _) = await UploadPictureAsync("third.png", PlainPicture(120, 90, MagickFormat.Png), "image/png");
        await LinkToStationAsync(modelId, "A", third);
        var again = await Bundle.ReadAsync(await owner.PostAsJsonAsync(Route, asked));
        again.Files.Count(f => f.StartsWith("photos/", StringComparison.Ordinal)).ShouldBe(2);
        Encoding.UTF8.GetString(again.Entries["README.txt"]).ShouldContain("  - 1 photograph taken at a station");

        // Not asked for, none goes in; and a layer hidden throughout shows none either.
        (await Bundle.ReadAsync(await owner.PostAsJsonAsync(Route, Request(spot, [Cave(cave, walls: true)]))))
            .Files.ShouldAllBe(f => !f.StartsWith("photos/", StringComparison.Ordinal));
        var off = await owner.PostAsJsonAsync(Route, Request(
            spot, [Cave(cave, walls: true)], photos: new { caves = new[] { cave }, shown = new[] { new { frame = 0, shown = false } } }));
        Header(off, FlybyBlenderBundleEndpoints.PhotosHeader).ShouldBe(0);
    }

    [Fact]
    public async Task A_photograph_of_a_cave_the_caller_may_not_place_never_enters_the_bundle()
    {
        var spot = Spot.Somewhere();
        var open = await CreateSurveyedCaveAsync(spot, heightM: 900);
        var guarded = await CreateSurveyedCaveAsync(spot.East(0.01), heightM: 900, locationProtected: true);
        var (document, _) = await UploadPictureAsync("in-a-kept-cave.jpg", PlainPicture(200, 100, MagickFormat.Jpeg), "image/jpeg");
        await ShareDocumentAsync(document);
        await LinkToStationAsync(await LinePlotOfAsync(guarded), "B", document);

        var asked = Request(
            spot, [Cave(open, walls: true), Cave(guarded, walls: true)], photos: new { caves = new[] { open, guarded }, shown = (object?)null });

        // The reader may read the photograph itself — and may not be told where that cave is, so
        // the cave is not drawn and nothing linked to its stations is looked for.
        var withheld = await reader.PostAsJsonAsync(Route, asked);
        Header(withheld, FlybyBlenderBundleEndpoints.PhotosHeader).ShouldBe(0);
        Header(withheld, FlybyBlenderBundleEndpoints.NotDrawnHeader).ShouldBe(1);
        (await Bundle.ReadAsync(withheld)).Files.ShouldAllBe(f => !f.StartsWith("photos/", StringComparison.Ordinal));

        // Granted the cave's exact place, they get the photograph at its station.
        await GrantExactViewAsync(guarded);
        var granted = await reader.PostAsJsonAsync(Route, asked);
        Header(granted, FlybyBlenderBundleEndpoints.PhotosHeader).ShouldBe(1);
        (await Bundle.ReadAsync(granted)).Plan.GetProperty("annotations")[0].GetProperty("model").GetString().ShouldBe("cave-02-walls");
    }

    [Fact]
    public async Task A_bundle_that_reveals_its_caves_measures_each_from_the_entrance_its_survey_flags()
    {
        var spot = Spot.Somewhere();
        var flagged = await CreateSurveyedCaveAsync(spot, heightM: 900, entranceAtTheBottom: true);
        var unflagged = await CreateSurveyedCaveAsync(spot.East(0.01), heightM: 900);

        // The lines hidden by the look: a cave is measured along its survey all the same.
        var answer = await owner.PostAsJsonAsync(Route, Request(
            spot,
            [Cave(flagged, walls: true), Cave(unflagged, walls: false)],
            reveal: new { by = "distance", startS = 0.04, endS = 0.32 }));
        var bundle = await Bundle.ReadAsync(answer);

        // The invented survey: A — B — C — D, 13.3 m, 28.2 m and 36.0 m of passage.
        const double whole = 13.342 + 28.160 + 35.958;
        var changes = bundle.Plan.GetProperty("changes").EnumerateArray()
            .Where(c => c.TryGetProperty("reveal", out _))
            .ToDictionary(c => c.GetProperty("target").GetProperty("id").GetString()!);
        changes.Keys.ShouldBe(["cave-01-walls", "cave-01-lines", "cave-02-lines"], ignoreOrder: true);
        foreach (var change in changes.Values)
        {
            var keys = change.GetProperty("reveal");
            keys[0].GetProperty("t").GetDouble().ShouldBe(0.04, 1e-9);
            keys[1].GetProperty("t").GetDouble().ShouldBe(0.32, 1e-9);
            keys[0].GetProperty("value").GetDouble().ShouldBe(0, 1e-6);
            keys[1].GetProperty("value").GetDouble().ShouldBe(whole * 1.041, 0.1);
        }

        // The first cave from D, the station its survey flags as an entrance: the tubes of its
        // legs run A → B → C → D, so its last four corners are at nothing and its first at all.
        var from = BundlePly.Reveal(bundle.Entries["cave-01-lines.ply"]).ShouldNotBeNull();
        from.Count.ShouldBe(24);
        from.TakeLast(4).ShouldAllBe(value => value == 0);
        from.Take(4).ShouldAllBe(value => Math.Abs(value - whole) < 0.05);
        // The second, which flags none, from the first station of its first leg.
        var plain = BundlePly.Reveal(bundle.Entries["cave-02-lines.ply"]).ShouldNotBeNull();
        plain.Take(4).ShouldAllBe(value => value == 0);
        plain.TakeLast(4).ShouldAllBe(value => Math.Abs(value - whole) < 0.05);
        // The walls, built about the first leg, by the nearest place on the centreline.
        var walls = BundlePly.Reveal(bundle.Entries["cave-01-walls.ply"]).ShouldNotBeNull();
        walls.Min().ShouldBeGreaterThan(whole - 13.342 - 1.5);
        walls.Max().ShouldBeLessThanOrEqualTo(whole + 0.05);

        Encoding.UTF8.GetString(bundle.Entries["README.txt"]).ShouldContain("THE REVEAL");
        (await LastRecordAsync(flagged)).GetProperty("reveal").GetString().ShouldBe("distance");

        // Without one, no mesh carries a number.
        var whole2 = await Bundle.ReadAsync(await owner.PostAsJsonAsync(Route, Request(spot, [Cave(flagged, walls: true)])));
        BundlePly.Reveal(whole2.Entries["cave-01-walls.ply"]).ShouldBeNull();
    }

    [Fact]
    public async Task A_bundle_that_reveals_its_caves_by_date_keys_them_on_one_clock_of_the_days_their_legs_were_surveyed()
    {
        var spot = Spot.Somewhere();
        var dated = await CreateSurveyedCaveAsync(spot, heightM: 900, dated: true);
        var wholeSurvey = await CreateSurveyedCaveAsync(spot.East(0.01), heightM: 900);
        await DateTheSurveyAsync(wholeSurvey, new DateOnly(1995, 1, 1));
        var undated = await CreateSurveyedCaveAsync(spot.East(0.02), heightM: 900);

        var answer = await owner.PostAsJsonAsync(Route, Request(
            spot,
            [Cave(dated, walls: false), Cave(wholeSurvey, walls: false), Cave(undated, walls: false)],
            reveal: new { by = "date", startS = 0.04, endS = 0.32 }));
        var bundle = await Bundle.ReadAsync(answer);

        // The two caves with a date on one clock, from the first day of the first to the date of
        // the second's survey; the third, with none, is there from the start.
        var changes = bundle.Plan.GetProperty("changes").EnumerateArray()
            .Where(c => c.TryGetProperty("reveal", out _))
            .ToDictionary(c => c.GetProperty("target").GetProperty("id").GetString()!);
        changes.Keys.ShouldBe(["cave-01-lines", "cave-02-lines"], ignoreOrder: true);
        changes.Values.Select(c => c.GetProperty("reveal").GetRawText()).Distinct().ShouldHaveSingleItem();
        var keys = changes["cave-01-lines"].GetProperty("reveal");
        keys[0].GetProperty("value").GetDouble().ShouldBe(Therion.CaveGeometry.RevealField.DecimalYear(FirstDay), 1e-6);
        keys[1].GetProperty("value").GetDouble().ShouldBeGreaterThan(1995);

        // The days the file gave its legs, on the lines; the undated third leg at the earliest.
        var years = BundlePly.Reveal(bundle.Entries["cave-01-lines.ply"]).ShouldNotBeNull();
        years.Min().ShouldBe(Therion.CaveGeometry.RevealField.DecimalYear(FirstDay), 1e-4);
        years.Max().ShouldBe(1990, 1e-4);
        years.Count(value => Math.Abs(value - 1990) < 1e-4).ShouldBe(8);
        BundlePly.Reveal(bundle.Entries["cave-02-lines.ply"]).ShouldNotBeNull().ShouldAllBe(value => Math.Abs(value - 1995) < 1e-4);
        BundlePly.Reveal(bundle.Entries["cave-03-lines.ply"]).ShouldBeNull();

        bundle.Plan.GetProperty("texts").GetProperty("items").EnumerateArray()
            .Count(item => item.GetProperty("id").GetString() == "reveal-counter").ShouldBe(1);
        Header(answer, FlybyBlenderBundleEndpoints.UndatedLegsHeader).ShouldBe(1);
        Header(answer, FlybyBlenderBundleEndpoints.CavesOnSurveyDateHeader).ShouldBe(1);
        Header(answer, FlybyBlenderBundleEndpoints.UndatedCavesHeader).ShouldBe(1);
        Encoding.UTF8.GetString(bundle.Entries["README.txt"]).ShouldContain("THE REVEAL BY SURVEY DATE");
        (await LastRecordAsync(dated)).GetProperty("reveal").GetString().ShouldBe("date");

        // A caller who may not place the dated cave exactly is given nothing of it: no lines, so
        // no days either.
        var guarded = await CreateSurveyedCaveAsync(spot, heightM: 900, locationProtected: true, dated: true);
        var withheld = await reader.PostAsJsonAsync(Route, Request(
            spot,
            [Cave(guarded, walls: false), Cave(wholeSurvey, walls: false)],
            reveal: new { by = "date", startS = 0.04, endS = 0.32 }));
        var seen = await Bundle.ReadAsync(withheld);
        Header(withheld, FlybyBlenderBundleEndpoints.NotDrawnHeader).ShouldBe(1);
        Header(withheld, FlybyBlenderBundleEndpoints.UndatedLegsHeader).ShouldBe(0);
        seen.Entries.Keys.ShouldNotContain("cave-02-lines.ply");
        BundlePly.Reveal(seen.Entries["cave-01-lines.ply"]).ShouldNotBeNull().ShouldAllBe(value => Math.Abs(value - 1995) < 1e-4);
    }

    [Fact]
    public async Task Markers_photographs_and_a_reveal_that_are_not_what_a_request_may_say_are_refused()
    {
        var spot = Spot.Somewhere();
        var cave = Cave(Guid.NewGuid(), walls: true);
        var caveId = Guid.NewGuid();
        async Task Refused(object request, string what, string code = "validation.failed")
        {
            var answer = await owner.PostAsJsonAsync(Route, request);
            answer.StatusCode.ShouldBe(HttpStatusCode.BadRequest, what);
            (await CodeAsync(answer)).ShouldBe(code, what);
        }

        var one = Guid.NewGuid();
        await Refused(Request(spot, [cave], markers: [MarkerLayer("entrances", "teal", [Marker(one, 1)])]), "a colour that is a word");
        await Refused(Request(spot, [cave], markers: [MarkerLayer("entrances", "#146262", [Marker(one, 1), Marker(one, 2)])]), "a marker named twice in a layer");
        await Refused(Request(spot, [cave], markers: [MarkerLayer("entrances", "#146262", [Marker(one, 90_000)])]), "ground far above any");
        await Refused(
            Request(spot, [cave], markers: [MarkerLayer("entrances", "#146262", [Marker(one, 1)]), MarkerLayer("entrances", "#146262", [Marker(Guid.NewGuid(), 1)])]),
            "two layers of entrances");
        await Refused(
            Request(spot, [cave], markers: [MarkerLayer("entrances", "#146262", [.. Enumerable.Range(0, 1001).Select(_ => Marker(Guid.NewGuid(), 1))])]),
            "more markers than a bundle holds");
        await Refused(
            Request(spot, [cave], markers: [MarkerLayer("entrances", "#146262", [Marker(one, 1)], shown: [new { frame = 3, shown = true, fadeFrames = 0 }, new { frame = 2, shown = false, fadeFrames = 0 }])]),
            "a layer switched in falling order");
        await Refused(Request(spot, [cave], markers: [MarkerLayer("everything", "#146262", [Marker(one, 1)])]), "a layer of no kind", "validation.invalid_enum");

        await Refused(Request(spot, [Cave(caveId, walls: true)], photos: new { caves = new[] { Guid.NewGuid() }, shown = (object?)null }), "photographs of a cave the request does not name");
        await Refused(Request(spot, [Cave(caveId, walls: true)], photos: new { caves = new[] { caveId, caveId }, shown = (object?)null }), "a cave named twice");
        await Refused(
            Request(spot, [Cave(caveId, walls: true)], photos: new { caves = new[] { caveId }, shown = new[] { new { frame = 2, shown = false, fadeFrames = 3 } } }),
            "photographs that fade");

        await Refused(Request(spot, [cave], reveal: new { by = "distance", startS = 0.3, endS = 0.1 }), "a reveal that ends before it starts");
        await Refused(Request(spot, [cave], reveal: new { by = (string?)null, startS = 0d, endS = 0.2 }), "a reveal by nothing");
        await Refused(Request(spot, [cave], reveal: new { by = "order", startS = 0d, endS = 0.2 }), "a reveal by the order of the legs, which a bundle does not have", "validation.invalid_enum");
    }

    // ---- what the tests above stand up ----

    private static object Point(Spot at) => new { type = "Point", coordinates = new[] { at.Longitude, at.Latitude } };

    private static object Marker(Guid featureId, double groundM, bool named = false) => new { featureId, groundM, named };

    private static object MarkerLayer(string kind, string colour, object[] markers, object[]? shown = null, object[]? opacity = null) =>
        new { kind, colour, markers, shown, opacity };

    private async Task<Guid> CreateEntranceAsync(Guid caveId, Spot at, string? name)
    {
        long entranceTypeId;
        using (var scope = factory.Services.CreateScope())
        {
            entranceTypeId = await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().EntranceTypes.Select(t => t.Id).FirstAsync();
        }

        var response = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            name,
            entranceTypeId,
            isMain = false,
            geom = Point(at),
            positionQuality = "Gps",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>An invented sinkhole: a kind of feature that may be a point or an outline.</summary>
    private async Task<Guid> CreateFeatureAsync(object geometry, string visibility = "authenticated", bool locationProtected = false)
    {
        long typeId;
        using (var scope = factory.Services.CreateScope())
        {
            typeId = await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>()
                .FeatureTypes.Where(t => t.Code == FeatureTypeSeeds.Sinkhole).Select(t => t.Id).SingleAsync();
        }

        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Invented hollow {Guid.NewGuid():N}"[..30],
            featureTypeId = typeId,
            geometry,
            description = (string?)null,
            locationProtected,
            visibility,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> LinePlotOfAsync(Guid caveId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>()
            .SurveyModels.Where(m => m.CaveFeatureId == caveId).Select(m => m.Id).SingleAsync();
    }

    private async Task<(Guid DocumentId, Guid FileId)> UploadPictureAsync(string fileName, byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(contentType);
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        var response = await owner.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var fileId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);
        var version = await db.DocumentVersions.AsNoTracking().FirstAsync(v => v.Id == file.DocumentVersionId);
        return (version.DocumentId, fileId);
    }

    /// <summary>Widens a born-private document to every signed-in reader, through the document's own write surface.</summary>
    private async Task ShareDocumentAsync(Guid documentId)
    {
        var current = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}");
        var updated = await owner.PutAsJsonAsync($"/api/v1/documents/{documentId}", new
        {
            title = current.GetProperty("title").GetString(),
            documentTypeId = current.GetProperty("documentTypeId").ValueKind == JsonValueKind.Number
                ? current.GetProperty("documentTypeId").GetInt64()
                : (long?)null,
            metadata = (object?)null,
            visibility = "authenticated",
            cavingGroupId = (Guid?)null,
        });
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
    }

    /// <summary>Says a photograph was taken at a station: a link naming the station of the model and the document.</summary>
    private async Task LinkToStationAsync(Guid modelId, string station, Guid documentId)
    {
        object Member(string targetType, Guid targetId, int sortOrder, string anchorKind, object? anchor) => new
        {
            targetType,
            targetId,
            isMain = false,
            sortOrder,
            note = (string?)null,
            anchorKind,
            anchor,
            anchorFileId = (Guid?)null,
        };
        var response = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = (long?)null,
            description = (string?)null,
            members = new[]
            {
                Member("surveyModel", modelId, 0, "modelStation", new { station }),
                Member("document", documentId, 1, "whole", null),
            },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    /// <summary>The newest record of a bundle that drew this cave.</summary>
    private async Task<JsonElement> LastRecordAsync(Guid caveId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var entries = await db.AuditEntries.AsNoTracking()
            .Where(e => e.Action == AuditActions.Exported && e.EntityType == FlybyBlenderBundleEndpoints.AuditEntityType)
            .OrderByDescending(e => e.Id)
            .Select(e => e.Changes)
            .ToListAsync();
        return entries
            .Select(changes => JsonDocument.Parse(changes!).RootElement.Clone())
            .First(c => c.GetProperty("wallsCaveIds").EnumerateArray().Concat(c.GetProperty("linesCaveIds").EnumerateArray())
                .Any(id => id.GetString() == caveId.ToString()));
    }

    /// <summary>A picture of nothing: bands of colour, in the format asked for.</summary>
    private static byte[] PlainPicture(uint width, uint height, MagickFormat format)
    {
        using var image = new MagickImage(new MagickColor(40, 110, 170), width, height);
        using var band = new MagickImage(new MagickColor(235, 205, 70), width, Math.Max(1, height / 4));
        image.Composite(band, 0, (int)(height / 2));
        return image.ToByteArray(format);
    }

    /// <summary>
    /// The same, as a JPEG stamped the way a camera stamps one — with a position, made up, on
    /// open ocean — so that a bundle can be shown to carry none of it.
    /// </summary>
    private static byte[] StampedPicture(uint width, uint height)
    {
        using var image = new MagickImage(PlainPicture(width, height, MagickFormat.Png));
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, "S");
        exif.SetValue(ExifTag.GPSLatitude, [new Rational(48), new Rational(30), new Rational(0)]);
        exif.SetValue(ExifTag.GPSLongitudeRef, "W");
        exif.SetValue(ExifTag.GPSLongitude, [new Rational(151), new Rational(15), new Rational(0)]);
        exif.SetValue(ExifTag.Model, "Invented camera");
        image.SetProfile(exif);
        return image.ToByteArray(MagickFormat.Jpeg);
    }

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
        string[]? leftOut = null,
        object? changes = null,
        string? language = null,
        object[]? markers = null,
        object? photos = null,
        object? reveal = null) => new
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
            leftOut = leftOut ?? ["ground", "scans"],
            changes,
            language,
            markers,
            photos,
            reveal,
        };

    /// <summary>What a flyby changes over the clip, as a request writes it; a member left out is said as null.</summary>
    private static object Changes(
        object[]? relief = null,
        object[]? sky = null,
        object? walls = null,
        object? lines = null,
        object[]? caves = null,
        object[]? daylight = null) =>
        new { relief, sky, walls, lines, caves, daylight };

    private static object Layer(object[]? shown = null, object[]? opacity = null, object[]? tint = null) => new { shown, opacity, tint };

    private static object Number(int frame, double value, string? easing = null) => new { frame, value, easing };

    private static object Tint(int frame, string colour, double strength, string? easing = null) => new { frame, colour, strength, easing };

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
        bool measured = true,
        bool entranceAtTheBottom = false,
        bool dated = false)
    {
        var caveId = await CreateCaveAsync(visibility, locationProtected, parentId);

        var content = new ByteArrayContent(dated ? DatedSurvey() : Survey(measured, entranceAtTheBottom));
        content.Headers.ContentType = new("application/octet-stream");
        using var form = new MultipartFormDataContent
        {
            { content, "file", dated ? "Invented.3d" : "Invented.lox" },
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
    private static byte[] Survey(bool measured, bool entranceAtTheBottom = false)
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
                new CaveStation
                {
                    Id = 4,
                    Name = "D",
                    Position = new CaveVector3(31, 9, -64),
                    // The format's own bit for an entrance: the writer writes a station's flags as the file has them.
                    RawFlags = entranceAtTheBottom ? 2u : 0u,
                },
            ],
            Shots = [Shot(1, 2, atA, atB), Shot(2, 3, null, null), Shot(3, 4, null, null)],
        });
    }

    /// <summary>The first leg's day and the second's of <see cref="DatedSurvey"/>; its third has none.</summary>
    private static readonly DateOnly FirstDay = new(1985, 6, 1);
    private static readonly DateOnly SecondDay = new(1990, 1, 1);

    /// <summary>
    /// The four stations of <see cref="Survey"/> as a Survex file, whose legs say when they were
    /// surveyed: the first and the second on two invented days, the third on none.
    /// </summary>
    private static byte[] DatedSurvey()
    {
        InventedSurveyFile.Station[] stations =
        [
            new("A", 0, 0, 0), new("B", 12, 5, -3), new("C", 20, 5, -30), new("D", 31, 9, -64),
        ];
        InventedSurveyFile.Leg Leg(int from, int to, DateOnly? day) =>
            new((stations[from].X, stations[from].Y, stations[from].Z), (stations[to].X, stations[to].Y, stations[to].Z), Date: day);
        return SurveyFileSamples.Survex3d(stations, [Leg(0, 1, FirstDay), Leg(1, 2, SecondDay), Leg(2, 3, null)], []);
    }

    /// <summary>Gives the survey models of a cave one date for the whole survey, as its page does.</summary>
    private async Task DateTheSurveyAsync(Guid caveId, DateOnly day)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.SurveyModels.Where(m => m.CaveFeatureId == caveId).ExecuteUpdateAsync(set => set.SetProperty(m => m.SurveyedAt, day));
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

    // ---- rendered on the server: the bundle for a render node holds nothing under location
    // protection, what it left out is counted, said in its notes and recorded; and who may ask, and
    // when the asking is refused ----

    private const string RenderRoute = "/api/v1/flybys/blender-renders";

    [Fact]
    public async Task A_render_on_the_server_leaves_out_the_protected_caves_and_places_its_asker_may_see_and_says_so()
    {
        var spot = Spot.Somewhere();
        var open = await CreateSurveyedCaveAsync(spot, heightM: 900);
        var closed = await CreateSurveyedCaveAsync(spot.East(0.01), heightM: 900, locationProtected: true);
        var openPlace = await CreateFeatureAsync(Point(spot.East(0.001)));
        var closedPlace = await CreateFeatureAsync(Point(spot.East(0.003)), locationProtected: true);
        var bundleRequest = Request(
            spot,
            [Cave(open, walls: true), Cave(closed, walls: true)],
            markers: [MarkerLayer("features", "#722ed1", [Marker(openPlace, 900), Marker(closedPlace, 900)])]);

        // The positive half first: their owner downloads both caves and both places.
        var download = await owner.PostAsJsonAsync(Route, bundleRequest);
        Header(download, FlybyBlenderBundleEndpoints.WallsHeader).ShouldBe(2);
        Header(download, FlybyBlenderBundleEndpoints.MarkersHeader).ShouldBe(2);

        var started = await owner.PostAsJsonAsync(RenderRoute, new { bundle = bundleRequest, plan = "draft", container = "mp4" });
        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync());
        var body = await started.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("walls").GetInt32().ShouldBe(1);
        body.GetProperty("notDrawn").GetInt32().ShouldBe(0);
        body.GetProperty("protectedLeftOut").GetInt32().ShouldBe(1);
        body.GetProperty("markers").GetInt32().ShouldBe(1);
        body.GetProperty("markersNotDrawn").GetInt32().ShouldBe(0);
        body.GetProperty("protectedMarkersLeftOut").GetInt32().ShouldBe(1);
        var renderId = body.GetProperty("render").GetProperty("id").GetGuid();
        body.GetProperty("render").GetProperty("status").GetString().ShouldBe("queued");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var render = await db.FlybyRenders.AsNoTracking().SingleAsync(r => r.Id == renderId);
        render.RequestedBy.ShouldBe(ownerId);
        render.Plan.ShouldBe("draft");
        render.ScratchBytes.ShouldBeGreaterThan(render.BundleSize);

        // What the node will be given: the open cave alone, and the notes saying why.
        var stored = StoredBundle(render.BundlePath!);
        stored.Meshes.ShouldBe(["cave-01-lines.ply", "cave-01-walls.ply"]);
        var readme = Encoding.UTF8.GetString(stored.Entries["README.txt"]);
        readme.ShouldContain("RENDERED ON THE SERVER");
        readme.ShouldContain("Left out for that: 1 cave and 1 entrance or feature.");
        readme.ShouldContain("1 cave under location protection");

        // Recorded as made for a render, with what it left out and the SHA-256 a node's download
        // of it will be recorded with.
        var made = await db.Set<AuditEntry>().AsNoTracking()
            .Where(a => a.UserId == ownerId && a.EntityType == FlybyBlenderBundleEndpoints.AuditEntityType)
            .OrderByDescending(a => a.Id)
            .FirstAsync();
        var changes = JsonDocument.Parse(made.Changes!).RootElement;
        changes.GetProperty("format").GetString().ShouldBe("blender-render");
        changes.GetProperty("wallsCaveIds").EnumerateArray().Select(e => e.GetString()).ShouldBe([open.ToString()]);
        changes.GetProperty("protectedLeftOutCount").GetInt32().ShouldBe(1);
        changes.GetProperty("protectedMarkersLeftOutCount").GetInt32().ShouldBe(1);
        changes.GetProperty("sha256").GetString().ShouldBe(render.BundleSha256);
    }

    [Fact]
    public async Task A_render_on_the_server_is_refused_while_one_is_going_when_storage_is_full_and_when_only_protected_caves_are_asked_for()
    {
        var spot = Spot.Somewhere();
        var open = await CreateSurveyedCaveAsync(spot, heightM: 900);
        var closed = await CreateSurveyedCaveAsync(spot.East(0.01), heightM: 900, locationProtected: true);
        object Asked(params object[] caves) => new { bundle = Request(spot, caves), plan = "full" };

        // Only protected caves: there is nothing a node may be given.
        var onlyProtected = await owner.PostAsJsonAsync(RenderRoute, Asked(Cave(closed, walls: true)));
        onlyProtected.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(onlyProtected)).ShouldBe(FlybyBlenderBundleEndpoints.OnlyProtectedCode);

        var first = await owner.PostAsJsonAsync(RenderRoute, Asked(Cave(open, walls: true)));
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("render").GetProperty("id").GetGuid();

        // One at a time: refused before a bundle is made, so no record of one is left.
        var records = await RenderRecordsAsync();
        var second = await owner.PostAsJsonAsync(RenderRoute, Asked(Cave(open, walls: true)));
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(second)).ShouldBe(FlybyRenderEndpoints.AlreadyActiveCode);
        (await RenderRecordsAsync()).ShouldBe(records);

        // Stopped, the turn is free again; with the space for renders taken, it is refused for that.
        (await owner.PostAsync($"{RenderRoute}/{firstId}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        Guid filler;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var row = new FlybyRender
            {
                RequestedBy = readerId,
                Name = "An invented placeholder",
                Plan = "full",
                Container = "mp4",
                BundlePath = "2026/11/an-invented-placeholder.zip",
                BundleSize = 60_000_000_000,
                BundleSha256 = new string('0', 64),
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.FlybyRenders.Add(row);
            await db.SaveChangesAsync();
            filler = row.Id;
        }

        try
        {
            var full = await owner.PostAsJsonAsync(RenderRoute, Asked(Cave(open, walls: true)));
            full.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await CodeAsync(full)).ShouldBe(FlybyRenderEndpoints.StorageFullCode);
            (await RenderRecordsAsync()).ShouldBe(records);
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.FlybyRenders.Where(r => r.Id == filler).ExecuteDeleteAsync();
        }

        // Nobody signed in may ask, and a personal access key may not either: a render is asked
        // for from the application, not from a field device.
        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync(RenderRoute, Asked(Cave(open, walls: true)))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var key = await AuthHelper.CreateAccessKeyAsync(owner);
        using var device = AuthHelper.AccessKeyClient(factory, key);
        (await device.PostAsJsonAsync(RenderRoute, Asked(Cave(open, walls: true)))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task<int> RenderRecordsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var rows = await db.Set<AuditEntry>().AsNoTracking()
            .Where(a => a.UserId == ownerId && a.EntityType == FlybyBlenderBundleEndpoints.AuditEntityType)
            .Select(a => a.Changes)
            .ToListAsync();
        return rows.Count(changes => changes is not null
            && JsonDocument.Parse(changes).RootElement.GetProperty("format").GetString() == "blender-render");
    }

    private Bundle StoredBundle(string storagePath)
    {
        using var scope = factory.Services.CreateScope();
        var path = scope.ServiceProvider.GetRequiredService<IFileStore>().GetAbsolutePath(storagePath);
        using var archive = ZipFile.OpenRead(path);
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            using var held = new MemoryStream();
            using (var input = entry.Open())
            {
                input.CopyTo(held);
            }

            entries[entry.FullName[(entry.FullName.IndexOf('/') + 1)..]] = held.ToArray();
        }

        return new Bundle(entries, JsonDocument.Parse(entries["scene-plan.json"]).RootElement.Clone());
    }
}
