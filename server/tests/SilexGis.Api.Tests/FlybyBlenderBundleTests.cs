// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Infrastructure.Flybys;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Tests;

/// <summary>
/// A flyby written as a render bundle: what the zip holds, what its scene plan says about the
/// caves, the camera, the look and the texts — and what it does not say.
///
/// <para>
/// Everything here is invented and built in code: two caves at a made-up place, a camera that
/// turns about the first. Nothing is rendered; the plan and the zip are read back as the plain
/// JSON and files they are.
/// </para>
/// </summary>
public class FlybyBlenderBundleTests
{
    /// <summary>An invented place.</summary>
    private const double Lon = 7.5;
    private const double Lat = 41.25;
    private const double Top = 830;

    [Fact]
    public void The_zip_is_one_folder_with_the_plan_its_meshes_the_player_and_its_licence()
    {
        var (entries, _) = Bundle(Scene());

        var names = entries.Keys.Order(StringComparer.Ordinal).ToList();
        names.ShouldAllBe(name => name.StartsWith("two-caves-blender/", StringComparison.Ordinal));
        string[] expected =
        [
            "LICENSE", "NOTICE", "README.txt", "render.cmd", "render.py", "render.sh",
            "scene-plan.json", "scene-plan.draft.json",
            "cave-01-walls.ply", "cave-01-estimated.ply", "cave-01-lines.ply", "cave-02-lines.ply",
            "thide_player/core.py",
        ];
        foreach (var name in expected)
        {
            names.ShouldContain("two-caves-blender/" + name);
        }

        // A mesh is a binary PLY, which every Blender reads without an add-on.
        Encoding.ASCII.GetString(entries["two-caves-blender/cave-01-walls.ply"], 0, 3).ShouldBe("ply");

        // Nothing but the plan, its meshes, the notes and the player: no file of this application's own.
        names.Where(n => !n.Contains("/thide_player/", StringComparison.Ordinal)).Count().ShouldBe(12);
    }

    [Fact]
    public void The_plan_is_laid_out_about_a_point_it_does_not_name()
    {
        var (_, plan) = Bundle(Scene());

        var frame = plan.GetProperty("frame");
        frame.GetProperty("units").GetString().ShouldBe("metres");
        frame.TryGetProperty("crs", out _).ShouldBeFalse();
        if (frame.TryGetProperty("origin", out var origin))
        {
            origin.EnumerateArray().Select(n => n.GetDouble()).ShouldBe([0, 0, 0]);
        }

        // Neither the plan nor any text file of the bundle carries the place the scene was laid
        // out about, in any spelling a number of degrees would take.
        var (entries, _) = Bundle(Scene());
        foreach (var (name, bytes) in entries.Where(e => !e.Key.EndsWith(".ply", StringComparison.Ordinal)))
        {
            var text = Encoding.UTF8.GetString(bytes);
            text.ShouldNotContain("41.25", Case.Sensitive, name);
            text.ShouldNotContain("7.5,", Case.Sensitive, name);
        }

        plan.GetProperty("generator").GetString().ShouldBe("SilexGIS");
    }

    [Fact]
    public void Each_cave_is_a_model_placed_by_its_offset_and_hung_where_the_scene_drew_it()
    {
        var (_, plan) = Bundle(Scene());

        var models = plan.GetProperty("models").EnumerateArray().ToDictionary(m => m.GetProperty("id").GetString()!);
        models.Keys.Order(StringComparer.Ordinal).ShouldBe(
            ["cave-01-estimated", "cave-01-lines", "cave-01-walls", "cave-02-lines"]);

        // The first cave's walls stand at the origin: its anchor is the place the scene is laid
        // out about, and the scene hung it from the surface by its top, so its zero is the zero.
        Translation(models["cave-01-walls"]).ShouldBe([0, 0, 0]);
        models["cave-01-walls"].GetProperty("role").GetString().ShouldBe("walls");
        models["cave-01-walls"].GetProperty("material").GetString().ShouldBe("rock");
        models["cave-01-estimated"].GetProperty("role").GetString().ShouldBe("estimated-passages");
        models["cave-01-estimated"].GetProperty("material").GetString().ShouldBe("estimated");

        // The second cave is a little over a kilometre east, and its own top — 40 m lower as
        // surveyed — is hung from the same surface: level with the first, not below it.
        var second = Translation(models["cave-02-lines"]);
        second[0].ShouldBe(1045, 3);
        Math.Abs(second[1]).ShouldBeLessThan(0.2);
        Math.Abs(second[2]).ShouldBeLessThan(0.2);
        models["cave-02-lines"].GetProperty("role").GetString().ShouldBe("survey-lines");

        // A kilometre east its "up" and its "north" are not quite the origin's — the two places
        // differ by a turn of the Earth through the longitude between them — and the plan says so
        // rather than standing the cave on the origin's axes.
        var turn = models["cave-02-lines"].GetProperty("rotation");
        turn.GetProperty("degrees").GetDouble().ShouldBe(0.0125, 1e-6);
    }

    [Fact]
    public void Relief_stretches_every_model_along_its_own_up_and_lifts_the_camera_with_them()
    {
        var plain = Bundle(Scene()).Plan;
        var tall = Bundle(Scene() with { Relief = 2 }).Plan;

        foreach (var model in tall.GetProperty("models").EnumerateArray())
        {
            model.GetProperty("scale").EnumerateArray().Select(n => n.GetDouble()).ShouldBe([1, 1, 2]);
        }

        plain.GetProperty("models")[0].TryGetProperty("scale", out _).ShouldBeFalse();

        // The camera stood 120 m above the origin's height; drawn twice as tall, the origin is at
        // twice its height and the camera at twice its own — 240 m above it. (Less the seven
        // millimetres the Earth curves away over the 300 m between them.)
        Eye(plain, 0)[2].ShouldBe(120, 0.01);
        Eye(tall, 0)[2].ShouldBe(240, 0.01);
    }

    [Fact]
    public void The_camera_has_one_pose_a_frame_each_with_its_own_turn()
    {
        var scene = Scene();
        var (_, plan) = Bundle(scene);

        plan.GetProperty("timeline").GetProperty("fps").GetInt32().ShouldBe(25);
        plan.GetProperty("timeline").GetProperty("frameCount").GetInt32().ShouldBe(scene.Poses.Count);

        var camera = plan.GetProperty("camera");
        camera.GetProperty("interpolation").GetString().ShouldBe("linear");
        var track = camera.GetProperty("track").EnumerateArray().ToList();
        track.Count.ShouldBe(scene.Poses.Count);
        for (var i = 0; i < track.Count; i++)
        {
            track[i].GetProperty("t").GetDouble().ShouldBe(i / 25.0, 1e-6);
            var turn = track[i].GetProperty("orientation").EnumerateArray().Select(n => n.GetDouble()).ToArray();
            turn.Length.ShouldBe(4);
            Math.Sqrt(turn.Sum(n => n * n)).ShouldBe(1, 1e-6);
        }

        // The first pose stands 300 m south of the origin and 120 m above it, facing north and
        // tilted 20 degrees down; what it focuses on is 320 m along that line.
        var eye = Eye(plan, 0);
        eye[0].ShouldBe(0, 0.01);
        eye[1].ShouldBe(-300, 0.5);
        eye[2].ShouldBe(120, 0.01);
        var target = track[0].GetProperty("target").EnumerateArray().Select(n => n.GetDouble()).ToArray();
        var along = Math.Sqrt(Enumerable.Range(0, 3).Sum(k => Math.Pow(target[k] - eye[k], 2)));
        along.ShouldBe(320, 1e-3);
        ((target[2] - eye[2]) / along).ShouldBe(-Math.Sin(20 * Math.PI / 180), 1e-4);

        // A 60-degree lens across a landscape frame on the plan's 36 mm sensor.
        camera.GetProperty("sensorWidth").GetDouble().ShouldBe(36);
        camera.GetProperty("lens").GetDouble().ShouldBe(31.177, 1e-3);
        track[0].TryGetProperty("lens", out _).ShouldBeFalse();

        // Far enough to hold the second cave from every frame.
        camera.GetProperty("clipEnd").GetDouble().ShouldBeGreaterThan(1300);
    }

    [Fact]
    public void A_lens_that_changes_is_written_on_every_pose_and_a_portrait_frame_narrows_it()
    {
        var scene = Scene();
        var zooming = scene with
        {
            Width = 1080,
            Height = 1920,
            Poses = [.. scene.Poses.Select((p, i) => p with { LensDeg = 60 - (i * 0.5) })],
        };

        var (_, plan) = Bundle(zooming);

        var track = plan.GetProperty("camera").GetProperty("track").EnumerateArray().ToList();
        // Sixty degrees down the long side of a 9:16 frame is 35.98 across it: a 55.4 mm lens.
        track[0].GetProperty("lens").GetDouble().ShouldBe(55.43, 0.01);
        track[10].GetProperty("lens").GetDouble().ShouldBeGreaterThan(track[0].GetProperty("lens").GetDouble());
        plan.GetProperty("look").GetProperty("width").GetInt32().ShouldBe(1080);
        plan.GetProperty("look").GetProperty("height").GetInt32().ShouldBe(1920);
    }

    [Fact]
    public void The_look_is_rock_a_lamp_on_the_camera_a_sun_and_the_sky_the_flyby_had()
    {
        var (_, plan) = Bundle(Scene());
        var look = plan.GetProperty("look");

        var lights = look.GetProperty("lights").EnumerateArray().Select(l => l.GetProperty("kind").GetString()).ToList();
        lights.ShouldBe(["headlamp", "sun"], ignoreOrder: true);

        var materials = look.GetProperty("materials").EnumerateArray().ToDictionary(m => m.GetProperty("id").GetString()!);
        materials["rock"].GetProperty("kind").GetString().ShouldBe("noiseRamp");
        materials["estimated"].GetProperty("kind").GetString().ShouldBe("flat");

        // The first cave's lines are coloured by depth below its own top: 0–50 m in the first
        // colour, deeper in the second, the change of colour 50 m down a cave 120 m deep.
        var ramp = materials["lines-01"];
        ramp.GetProperty("kind").GetString().ShouldBe("heightRamp");
        (ramp.GetProperty("high").GetDouble() - ramp.GetProperty("low").GetDouble()).ShouldBe(120, 0.01);
        var stops = ramp.GetProperty("stops").EnumerateArray().Select(s => s.GetProperty("at").GetDouble()).ToList();
        stops.ShouldBe(stops.Order().ToList());
        stops.ShouldContain(at => Math.Abs(at - (1 - (50.0 / 120.0))) < 1e-3);

        look.GetProperty("world").GetProperty("kind").GetString().ShouldBe("color");
        look.GetProperty("samples").GetInt32().ShouldBe(96);

        // One colour for every line, when the flyby says so.
        var plainLines = Bundle(Scene() with { Look = new FlybyBundleLook(null, "#ff8800", []) }).Plan.GetProperty("look");
        plainLines.TryGetProperty("world", out _).ShouldBeFalse();
        plainLines.GetProperty("materials").EnumerateArray()
            .Single(m => m.GetProperty("id").GetString() == "lines").GetProperty("kind").GetString().ShouldBe("flat");
    }

    [Fact]
    public void The_texts_keep_the_flybys_own_times_and_the_arrow_its_headings()
    {
        var scene = Scene() with
        {
            Texts = new FlybyBundleTexts(
                Title: new FlybyBundleCard("Two invented caves", "A test", 1.2),
                End: new FlybyBundleCard("The end", null, 0.8),
                Captions: [new FlybyBundleCaption("First cave", 0.4, 1.4), new FlybyBundleCaption("Second cave", 1.0, 1.9)],
                Corner: "Invented survey",
                CornerCaptions: [new FlybyBundleCaption("Relief ×2", 0, 2)],
                FadeInS: 0.5,
                FadeOutS: 0.25,
                NorthArrow: true),
        };

        var (entries, plan) = Bundle(scene);

        plan.GetProperty("features").EnumerateArray().Select(f => f.GetString())
            .ShouldBe(["models", "camera_track", "look", "texts", "output"]);
        var texts = plan.GetProperty("texts");
        texts.GetProperty("fromBlack").GetDouble().ShouldBe(0.5);
        texts.GetProperty("toBlack").GetDouble().ShouldBe(0.25);
        // No frame is held after the flight: the clip is as long as the flyby.
        if (texts.TryGetProperty("hold", out var hold))
        {
            hold.GetDouble().ShouldBe(0);
        }

        var items = texts.GetProperty("items").EnumerateArray().ToList();
        JsonElement Item(string id) => items.Single(i => i.GetProperty("id").GetString() == id);

        // The title has gone by the moment the flyby names.
        Item("title").GetProperty("text").GetString().ShouldBe("Two invented caves");
        Item("title").GetProperty("end").GetDouble().ShouldBe(1.2);
        Item("subtitle").GetProperty("text").GetString().ShouldBe("A test");

        // The end card comes over the last of the flight — two seconds of clip, a card of 0.8 s —
        // and not on a black card after it.
        Item("end-card-text").GetProperty("start").GetDouble().ShouldBe(1.2, 1e-6);
        items.ShouldNotContain(i => i.GetProperty("kind").GetString() == "box" && i.GetProperty("role").GetString() == "end-card");

        // One caption at a time: the first gives way when the second begins.
        Item("caption.1").GetProperty("end").GetDouble().ShouldBe(1.0);
        Item("caption.2").GetProperty("start").GetDouble().ShouldBe(1.0);
        Item("corner").GetProperty("text").GetString().ShouldBe("Invented survey");
        Item("corner.1").GetProperty("text").GetString().ShouldBe("Relief ×2");

        // The camera turned from north to 49 degrees east over the clip, evenly: two turns say it.
        var turns = Item("north-arrow").GetProperty("turn").EnumerateArray()
            .Select(t => (T: t.GetProperty("t").GetDouble(), Degrees: t.GetProperty("degrees").GetDouble())).ToList();
        turns[0].ShouldBe((0, 0));
        turns[^1].T.ShouldBe(49 / 25.0, 1e-6);
        turns[^1].Degrees.ShouldBe(49, 1e-3);

        // The captions also lie beside where the movie will be, as subtitles.
        Encoding.UTF8.GetString(entries["two-caves-blender/out/two-caves.srt"]).ShouldContain("Second cave");
    }

    [Fact]
    public void What_is_left_out_is_said_in_the_bundles_own_notes()
    {
        var (entries, _) = Bundle(Scene() with
        {
            LeftOut = ["the ground and the map drawn on it", "2 of the caves asked for (nothing of them is available to draw)"],
        });

        var readme = Encoding.UTF8.GetString(entries["two-caves-blender/README.txt"]);
        readme.ShouldContain("NOT IN THIS SCENE");
        readme.ShouldContain("  - the ground and the map drawn on it");
        readme.ShouldContain("  - 2 of the caves asked for");
        readme.ShouldContain("Needs Blender 4.2 or newer");
        readme.ShouldContain("Mozilla Public License 2.0");

        Encoding.UTF8.GetString(Bundle(Scene()).Entries["two-caves-blender/README.txt"]).ShouldNotContain("NOT IN THIS SCENE");
    }

    [Fact]
    public void A_flyby_with_nothing_to_draw_or_no_frame_is_refused()
    {
        var scene = Scene();

        Should.Throw<ArgumentException>(() => FlybyBlenderBundle.PlanJson(scene with { Caves = [] }));
        Should.Throw<ArgumentException>(() => FlybyBlenderBundle.PlanJson(scene with { Poses = [] }));
    }

    [Fact]
    public void The_scratch_directory_is_gone_when_the_bundle_is_written()
    {
        var work = Path.Combine(TestScratch.Root, $"silexgis-blender-test-{Guid.NewGuid():N}");
        using var zip = new MemoryStream();

        var summary = FlybyBlenderBundle.Write(Scene(), work, zip);

        Directory.Exists(work).ShouldBeFalse();
        summary.FolderName.ShouldBe("two-caves-blender");
        summary.Frames.ShouldBe(50);
        summary.Models.ShouldBe(4);
    }

    [Theory]
    [InlineData("Peștera Mare — tur", "pestera-mare-tur-blender")]
    [InlineData("  ", "flyby-blender")]
    [InlineData("../../etc/passwd", "etc-passwd-blender")]
    [InlineData("CON", "con-blender")]
    public void The_folder_is_named_after_the_flyby_in_letters_every_file_system_takes(string name, string expected)
    {
        FlybyBlenderBundle.FolderName(name).ShouldBe(expected);
    }

    [Fact]
    public void Survey_legs_become_four_sided_tubes_and_a_leg_of_no_length_none()
    {
        var mesh = SurveyLineTubes.Build(
            [((0, 0, 0), (10, 0, 0)), ((10, 0, 0), (10, 0, 0)), ((10, 0, 0), (10, 0, -8))],
            radiusM: 0.5);

        // Two legs with a length: eight corners and eight faces each.
        mesh.VertexCount.ShouldBe(16);
        mesh.TriangleCount.ShouldBe(16);

        // Every corner is half a metre from the line of its leg.
        for (var v = 0; v < 8; v++)
        {
            Math.Sqrt(Math.Pow(mesh.Positions[(v * 3) + 1], 2) + Math.Pow(mesh.Positions[(v * 3) + 2], 2)).ShouldBe(0.5, 1e-9);
        }

        for (var v = 8; v < 16; v++)
        {
            Math.Sqrt(Math.Pow(mesh.Positions[v * 3] - 10, 2) + Math.Pow(mesh.Positions[(v * 3) + 1], 2)).ShouldBe(0.5, 1e-9);
        }
    }

    // ---- the invented scene ----

    /// <summary>
    /// Two caves: one with walls, an estimated passage and lines, anchored at the place the scene
    /// is laid out about; one a little over a kilometre east, known by its lines alone. The scene
    /// had no relief, so each is hung from the surface by its own top. Fifty frames at 25 a second,
    /// the camera 300 m south and 120 m up, turning a degree a frame.
    /// </summary>
    private static FlybyBundleScene Scene()
    {
        var cube = new IndexedMesh(
            [-5, -5, -10, 5, -5, -10, 5, 5, -10, -5, 5, -10, -5, -5, 0, 5, -5, 0, 5, 5, 0, -5, 5, 0],
            [0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6, 0, 4, 5, 0, 5, 1, 1, 5, 6, 1, 6, 2, 2, 6, 7, 2, 7, 3, 3, 7, 4, 3, 4, 0]);
        var tube = SurveyLineTubes.Build([((0, 0, -10), (30, 0, -40))], 0.5);

        var first = new FlybyBundleCave(
            Walls: new FlybyBundleMesh(cube, Lon, Lat, Top),
            Estimated: new FlybyBundleMesh(tube, Lon, Lat, Top),
            EstimatedColor: (0.8, 0.2, 0.05),
            Legs:
            [
                new FlybyBundleLeg(Lon, Lat, Top, Lon + 0.0002, Lat, Top - 60),
                new FlybyBundleLeg(Lon + 0.0002, Lat, Top - 60, Lon + 0.0004, Lat + 0.0001, Top - 120),
            ],
            AltitudeShiftM: -Top);
        var second = new FlybyBundleCave(
            Walls: null,
            Estimated: null,
            EstimatedColor: null,
            Legs: [new FlybyBundleLeg(Lon + 0.0125, Lat, Top - 40, Lon + 0.0127, Lat, Top - 70)],
            AltitudeShiftM: -(Top - 40));

        // 300 m south of the origin, as degrees of latitude on the ellipsoid there.
        const double south = 300 / 111_050.0;
        var poses = Enumerable.Range(0, 50)
            .Select(i => new FlybyBundlePose(Lon, Lat - south, 120, i, -20, 0, 320, 60))
            .ToList();

        return new FlybyBundleScene(
            Name: "Two caves",
            Width: 1280,
            Height: 720,
            Fps: 25,
            Quality: FlybyBundleQuality.Medium,
            OriginLongitude: Lon,
            OriginLatitude: Lat,
            OriginHeightM: 0,
            Relief: 1,
            Poses: poses,
            Caves: [first, second],
            Look: new FlybyBundleLook(
                "#101820",
                null,
                [new FlybyBundleDepthBand(0, "#d9480f"), new FlybyBundleDepthBand(50, "#8c2f6b"), new FlybyBundleDepthBand(150, "#6b3d9e")]),
            Texts: FlybyBundleTexts.None,
            LeftOut: []);
    }

    private static (Dictionary<string, byte[]> Entries, JsonElement Plan) Bundle(FlybyBundleScene scene)
    {
        var work = Path.Combine(TestScratch.Root, $"silexgis-blender-test-{Guid.NewGuid():N}");
        using var zip = new MemoryStream();
        FlybyBlenderBundle.Write(scene, work, zip);

        zip.Position = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            using var held = new MemoryStream();
            using (var input = entry.Open())
            {
                input.CopyTo(held);
            }

            entries[entry.FullName] = held.ToArray();
        }

        var top = entries.Keys.First().Split('/')[0];
        var plan = JsonDocument.Parse(entries[$"{top}/scene-plan.json"]).RootElement.Clone();
        return (entries, plan);
    }

    private static double[] Translation(JsonElement model) =>
        model.TryGetProperty("translation", out var at) ? [.. at.EnumerateArray().Select(n => n.GetDouble())] : [0, 0, 0];

    private static double[] Eye(JsonElement plan, int frame) =>
        [.. plan.GetProperty("camera").GetProperty("track")[frame].GetProperty("eye").EnumerateArray().Select(n => n.GetDouble())];
}
