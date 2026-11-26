// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Infrastructure.Flybys;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Tests;

/// <summary>
/// A bundle's caves can be seen where its sun does not reach.
///
/// <para>
/// An invented cave — a box of walls and four survey legs beside it, both facing south — and a
/// camera that stands south of them, level with their middle, under the ground from the first
/// frame to the last: 25 m away for a fifth of a second, backing off to 140 m, and standing there.
/// Nothing here is a real place or a real survey.
/// </para>
///
/// <para>
/// Two things make it seen. The survey lines wear a material no light changes, so they read in the
/// dark as they do under the sun. And the lamp on the camera is turned up, as the daylight goes,
/// by the square of the distance to the nearest wall — keys of its energy that the plan's own
/// camera, meshes and sun already imply, which is restated here from an unpacked bundle's files.
/// Where a Blender is installed, the scene is built and two small frames are rendered: the walls
/// are as bright from 140 m as from 25 m, and so are the lines.
/// </para>
/// </summary>
public class FlybyBundleLampTests
{
    private const double Lon = 9.75;
    private const double Lat = 44.35;
    private const double Height = 400;

    /// <summary>The frames the camera stands still on at the start, and at the end.</summary>
    private const int Still = 5;

    /// <summary>How many frames the clip has.</summary>
    private const int Frames = 51;

    /// <summary>How far south of the cave's front the camera stands at first, and at last, in metres.</summary>
    private const double Near = 25;
    private const double Far = 140;

    /// <summary>
    /// From the camera to the nearest point of the walls at those two places: the near edge of
    /// the box's front is four metres west of the line the camera backs off along.
    /// </summary>
    private static readonly double NearestNear = Math.Sqrt((Near * Near) + 16);
    private static readonly double NearestFar = Math.Sqrt((Far * Far) + 16);

    [Fact]
    public void Survey_lines_wear_a_material_no_light_changes_in_bands_of_one_stop_each()
    {
        var plan = Plan(Scene());
        var materials = plan.GetProperty("look").GetProperty("materials").EnumerateArray().ToDictionary(m => m.GetProperty("id").GetString()!);

        var lines = materials["lines-01"];
        lines.GetProperty("unlit").GetBoolean().ShouldBeTrue();
        lines.TryGetProperty("strength", out _).ShouldBeFalse("the colours as they are");
        plan.GetProperty("models").EnumerateArray().Single(m => m.GetProperty("role").GetString() == "survey-lines")
            .GetProperty("material").GetString().ShouldBe("lines-01");

        // Three bands down ten metres of lines — from the top, from 4 m and from 8 m: three stops,
        // each where its band begins from below, and none a ten-thousandth before another.
        lines.GetProperty("banded").GetBoolean().ShouldBeTrue();
        var stops = lines.GetProperty("stops").EnumerateArray().Select(stop => stop.GetProperty("at").GetDouble()).ToList();
        stops.ShouldBe([0, 0.2, 0.6], 1e-6);
        var reds = lines.GetProperty("stops").EnumerateArray().Select(stop => stop.GetProperty("color")[0].GetDouble()).ToList();
        reds.Distinct().Count().ShouldBe(3);

        // The walls are a thing in the cave, and are lit.
        materials["rock"].TryGetProperty("unlit", out _).ShouldBeFalse();
        materials["rock"].TryGetProperty("banded", out _).ShouldBeFalse();

        // One band from top to bottom is still a ramp, of the two stops a ramp has at the least.
        var one = Plan(Scene() with { Look = new FlybyBundleLook(null, null, [new FlybyBundleDepthBand(0, "#d9480f")]) });
        var only = one.GetProperty("look").GetProperty("materials").EnumerateArray().Single(m => m.GetProperty("id").GetString() == "lines-01");
        only.GetProperty("stops").EnumerateArray().Select(stop => stop.GetProperty("at").GetDouble()).ShouldBe([0, 1]);
        only.GetProperty("stops")[0].GetProperty("color").GetRawText().ShouldBe(only.GetProperty("stops")[1].GetProperty("color").GetRawText());
    }

    [Fact]
    public void In_the_dark_the_lamp_is_turned_up_by_the_square_of_the_distance_to_the_nearest_wall()
    {
        var folder = Unpacked(Scene(), "lamp");
        var rule = BundleLampRule.Frames(folder);
        var said = BundleLampRule.Said(folder);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "scene-plan.json")));
        var built = BundleLampRule.Built(document.RootElement);

        // The rule, from the bundle's own files, finds the distances the invented scene was made with.
        rule[0].Nearest.ShouldBe(NearestNear, 0.01);
        rule[^1].Nearest.ShouldBe(NearestFar, 0.01);
        rule.ShouldAllBe(frame => Math.Abs(frame.Gone - 1) < 1e-9);

        // Standing still: the lamp's own energy times the square of the distance over three metres.
        for (var i = 0; i < Still; i++)
        {
            said[i].ShouldBe(built * rule[i].Nearest * rule[i].Nearest / 9, 1e-3, $"frame {i}");
            said[i].ShouldBe(built * NearestNear * NearestNear / 9, 0.001 * said[i], $"frame {i}");
            said[Frames - 1 - i].ShouldBe(built * rule[^1].Nearest * rule[^1].Nearest / 9, 1e-2, $"frame {Frames - 1 - i}");
            said[Frames - 1 - i].ShouldBe(built * NearestFar * NearestFar / 9, 0.001 * said[^1], $"frame {Frames - 1 - i}");
        }

        // Backing off: at every frame within the fiftieth the survey library thins its keys to.
        for (var i = 0; i < Frames; i++)
        {
            said[i].ShouldBe(rule[i].Energy, (0.0201 * rule[i].Energy) + 1e-3, $"frame {i}");
        }

        // So from 140 m the walls get the light they get from 25 m: thirty times the lamp.
        (said[^1] / said[0]).ShouldBe(NearestFar * NearestFar / (NearestNear * NearestNear), 0.01);
        (said[^1] / said[0]).ShouldBeGreaterThan(30);

        // The keys are few: straight between the two stands, and what a curve needs between.
        var keys = document.RootElement.GetProperty("changes").EnumerateArray()
            .Single(c => c.TryGetProperty("target", out var target) && target.GetProperty("id").GetString() == "Headlamp")
            .GetProperty("energy").GetArrayLength();
        keys.ShouldBeLessThan(Frames / 2);
    }

    [Fact]
    public void Under_the_sun_the_lamp_is_as_it_is_built_and_it_takes_over_by_the_share_of_the_daylight_that_is_gone()
    {
        // Daylight throughout: nothing of the lamp is said, and the plan is the one a bundle had.
        var day = Plan(Scene(underground: false));
        day.TryGetProperty("changes", out _).ShouldBeFalse();
        var lamp = day.GetProperty("look").GetProperty("lights").EnumerateArray().Single(l => l.GetProperty("id").GetString() == "Headlamp");
        lamp.GetProperty("energy").GetDouble().ShouldBe(80);
        BundleLampRule.Said(Unpacked(Scene(underground: false), "lamp-day")).ShouldAllBe(energy => energy == 80);

        // Going under at frame 10, dark from frame 35, out again from frame 40 to 45.
        var scene = Scene() with
        {
            Changes = FlybyBundleChanges.None with
            {
                Daylight = [new(0, 1), new(10, 1), new(35, 0, FlybyBundleEasing.Smooth), new(40, 0), new(45, 1, FlybyBundleEasing.Smooth)],
            },
        };
        var folder = Unpacked(scene, "lamp-dusk");
        var rule = BundleLampRule.Frames(folder);
        var said = BundleLampRule.Said(folder);

        // In whole daylight the lamp's own energy, to the last digit.
        foreach (var i in (int[])[0, 5, 10, 45, 50])
        {
            rule[i].Gone.ShouldBe(0, 1e-9);
            said[i].ShouldBe(80, 1e-9, $"frame {i}");
        }

        // Half the daylight gone: half-way, in energy, between the lamp as built and the lamp
        // that reaches — not the lamp that reaches half as far.
        var half = Enumerable.Range(0, Frames).MinBy(i => Math.Abs(rule[i].Gone - 0.5));
        rule[half].Gone.ShouldBe(0.5, 0.05);
        var reaching = 80 * rule[half].Nearest * rule[half].Nearest / 9;
        said[half].ShouldBe(80 + ((reaching - 80) * rule[half].Gone), 0.0201 * reaching);

        for (var i = 0; i < Frames; i++)
        {
            said[i].ShouldBe(rule[i].Energy, (0.0201 * rule[i].Energy) + 1e-3, $"frame {i}");
        }
    }

    [Fact]
    public void The_lamp_is_set_by_what_a_light_shows_and_by_nothing_else()
    {
        // No walls: the lines are all there is, they need no light, and the lamp is left alone.
        var scene = Scene();
        var linesOnly = Plan(scene with { Caves = [scene.Caves[0] with { Walls = null }] });
        Targets(linesOnly).ShouldBe(["Sun"]);

        // A second cave of lines alone, three metres in front of the camera all the way: the
        // lamp is still set by the walls, which it is there to light.
        var north = 111_132.0;
        var beside = new FlybyBundleCave(
            null,
            null,
            null,
            [.. Enumerable.Range(0, Frames - 1).Select(i => new FlybyBundleLeg(
                Lon, Lat - ((Back(i) - 3) / north), Height - 1, Lon, Lat - ((Back(i + 1) - 3) / north), Height - 1))],
            AltitudeShiftM: 0);
        var folder = Unpacked(scene with { Caves = [scene.Caves[0], beside] }, "lamp-beside");
        var withLines = BundleLampRule.Said(folder);
        var without = BundleLampRule.Said(Unpacked(scene, "lamp-walls"));
        withLines[^1].ShouldBe(80 * NearestFar * NearestFar / 9, 0.001 * withLines[^1]);
        for (var i = 0; i < Frames; i++)
        {
            withLines[i].ShouldBe(without[i], 1e-6, $"frame {i}");
        }

        // Nearer than three metres the lamp is never turned down: a camera inside a passage has
        // the lamp the survey library builds.
        var inside = scene with { Poses = [.. scene.Poses.Select(pose => pose with { Latitude = Lat + (1 / north), Longitude = Lon - (10 / (111_320.0 * Math.Cos(Lat * Math.PI / 180))) })] };
        var close = Plan(inside);
        Targets(close).ShouldBe(["Sun"]);
        close.GetProperty("look").GetProperty("lights").EnumerateArray().Single(l => l.GetProperty("id").GetString() == "Headlamp")
            .GetProperty("energy").GetDouble().ShouldBe(80);
    }

    [Fact]
    public void Walls_stretched_by_a_relief_are_as_far_as_they_are_drawn()
    {
        // Twice as tall by the last frame, and the camera forty metres above the level it flew at:
        // the nearest of the walls is then their top edge, which the stretch has raised.
        var scene = Scene();
        scene = scene with
        {
            OriginHeightM = 0,
            Poses = [.. scene.Poses.Select(pose => pose with { HeightM = Height + 40 })],
            Changes = scene.Changes with { Relief = [new(0, 1), new(Frames - 1, 2, FlybyBundleEasing.Smooth)] },
        };
        var folder = Unpacked(scene, "lamp-relief");
        var rule = BundleLampRule.Frames(folder);
        var said = BundleLampRule.Said(folder);
        for (var i = 0; i < Frames; i++)
        {
            said[i].ShouldBe(rule[i].Energy, (0.0201 * rule[i].Energy) + 1e-3, $"frame {i}");
        }

        // At true height the top of the box is 34 m below the camera; drawn twice as tall, the
        // camera is 2 × 440 m up and the top 2 × 406 m: 68 m below.
        rule[0].Nearest.ShouldBe(Math.Sqrt((Near * Near) + 16 + (34 * 34)), 0.01);
        rule[^1].Nearest.ShouldBe(Math.Sqrt((Far * Far) + 16 + (68 * 68)), 0.05);
    }

    [BlenderFact]
    public async Task In_Blender_the_walls_are_as_bright_from_far_as_from_near_and_the_lines_need_no_light()
    {
        var folder = Unpacked(Scene(), "lamp-blender");
        var said = BundleLampRule.Said(folder);
        var report = await RenderedAsync(folder, [1, Frames]);

        // The lines' material has nothing a light acts on: an emission, and its ramp in steps.
        var lines = report.GetProperty("materials").GetProperty("lines-01");
        lines.GetProperty("emission").GetBoolean().ShouldBeTrue();
        lines.GetProperty("lit").GetBoolean().ShouldBeFalse();
        lines.GetProperty("ramps").EnumerateArray().Select(ramp => ramp.GetString()).ShouldBe(["CONSTANT"]);
        report.GetProperty("materials").GetProperty("rock").GetProperty("lit").GetBoolean().ShouldBeTrue();

        var near = report.GetProperty("frames").GetProperty("1");
        var far = report.GetProperty("frames").GetProperty(Frames.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // The lamp Blender holds is the plan's, and the sun is out.
        near.GetProperty("lamp").GetDouble().ShouldBe(said[0], 1e-3 * said[0]);
        far.GetProperty("lamp").GetDouble().ShouldBe(said[^1], 1e-3 * said[^1]);
        near.GetProperty("sun").GetDouble().ShouldBe(0);

        // On the screen, of 255: the walls are lit to the middle of the picture from both
        // distances — without the reach they were at 7 from 25 m and at 0 from 140 m —, and the
        // lines show their colours the same from both.
        double Luma(JsonElement frame, string model) => frame.GetProperty(model).GetProperty("luma").GetDouble();
        Luma(near, "cave-01-walls").ShouldBeInRange(60, 200);
        Luma(far, "cave-01-walls").ShouldBeInRange(60, 200);
        (Luma(far, "cave-01-walls") / Luma(near, "cave-01-walls")).ShouldBeInRange(0.75, 1.33);
        Luma(near, "cave-01-lines").ShouldBeGreaterThan(80);
        Luma(far, "cave-01-lines").ShouldBe(Luma(near, "cave-01-lines"), 20);
    }

    // ---- the invented cave ----

    /// <summary>How far south of the cave's front the camera of a frame stands.</summary>
    private static double Back(int frame) =>
        frame < Still ? Near
        : frame >= Frames - Still ? Far
        : Near + ((Far - Near) * (frame - (Still - 1)) / (Frames - (2.0 * Still) + 1));

    internal static FlybyBundleScene Scene(bool underground = true)
    {
        // A degree of longitude and of latitude at the invented place, in metres.
        var east = 111_320.0 * Math.Cos(Lat * Math.PI / 180);
        const double north = 111_132.0;

        // Walls: a box twelve metres a side, west of the middle, its front in the plane the
        // lines are in.
        var box = new IndexedMesh(
            [-16, 0, -6, -4, 0, -6, -4, 12, -6, -16, 12, -6, -16, 0, 6, -4, 0, 6, -4, 12, 6, -16, 12, 6],
            [0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6, 0, 4, 5, 0, 5, 1, 1, 5, 6, 1, 6, 2, 2, 6, 7, 2, 7, 3, 3, 7, 4, 3, 4, 0]);

        // Lines: a zig-zag east of the middle, ten metres from its top to its bottom.
        (double East, double Up)[] ends = [(4, 5), (8, -5), (12, 5), (16, -5), (20, 5)];
        var legs = ends.Zip(ends.Skip(1))
            .Select(pair => new FlybyBundleLeg(
                Lon + (pair.First.East / east), Lat, Height + pair.First.Up,
                Lon + (pair.Second.East / east), Lat, Height + pair.Second.Up))
            .ToList();

        var cave = new FlybyBundleCave(
            Walls: new FlybyBundleMesh(box, Lon, Lat, Height),
            Estimated: null,
            EstimatedColor: null,
            Legs: legs,
            AltitudeShiftM: 0);

        var poses = Enumerable.Range(0, Frames)
            .Select(i => new FlybyBundlePose(Lon, Lat - (Back(i) / north), Height, 0, 0, 0, Back(i), 60))
            .ToList();

        return new FlybyBundleScene(
            Name: "Invented cave in the dark",
            Width: 960,
            Height: 540,
            Fps: 25,
            Quality: FlybyBundleQuality.Low,
            OriginLongitude: Lon,
            OriginLatitude: Lat,
            OriginHeightM: Height,
            Relief: 1,
            Poses: poses,
            Caves: [cave],
            Look: new FlybyBundleLook(
                "#6f8fb0",
                null,
                [new FlybyBundleDepthBand(0, "#d9480f"), new FlybyBundleDepthBand(4, "#e8b004"), new FlybyBundleDepthBand(8, "#2f9e44")]),
            Texts: FlybyBundleTexts.None,
            LeftOut: [])
        {
            // Under the ground throughout: no daylight at any frame.
            Changes = underground ? FlybyBundleChanges.None with { Daylight = [new(0, 0)] } : FlybyBundleChanges.None,
        };
    }

    /// <summary>The things the plan's changes are of, by id; a change of the scene itself is of none.</summary>
    private static List<string?> Targets(JsonElement plan) =>
    [
        .. plan.GetProperty("changes").EnumerateArray()
            .Where(c => c.TryGetProperty("target", out _))
            .Select(c => c.GetProperty("target").GetProperty("id").GetString()),
    ];

    private static JsonElement Plan(FlybyBundleScene scene) => JsonDocument.Parse(FlybyBlenderBundle.PlanJson(scene)).RootElement.Clone();

    internal static string Unpacked(FlybyBundleScene scene, string name)
    {
        var root = Path.Combine(TestScratch.Root, "blender-bundle-lamp", name);
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(root);
        using var zip = new MemoryStream();
        var summary = FlybyBlenderBundle.Write(scene, zip);
        zip.Position = 0;
        ZipFile.ExtractToDirectory(zip, root);
        return Path.Combine(root, summary.FolderName);
    }

    // ---- Blender ----

    /// <summary>
    /// What is asked of the scene once the bundle's player has built it: of each material, whether
    /// a light acts on it; at each frame, the lamp and the sun, and how bright the walls and the
    /// lines are on the screen — each rendered alone, small, its pixels found by rendering it once
    /// more as a plain white shape on nothing.
    /// </summary>
    private const string Probe = """
        import array, bpy, json, os

        out = os.environ["SILEXGIS_LAMP_OUT"]
        frames = [int(f) for f in os.environ["SILEXGIS_LAMP_FRAMES"].split(",")]
        width, height = 240, 136
        scene = bpy.context.scene
        scene.render.resolution_x = width
        scene.render.resolution_y = height
        scene.render.resolution_percentage = 100
        settings = scene.render.image_settings
        if hasattr(settings, "media_type"):
            settings.media_type = "IMAGE"
        settings.file_format = "PNG"
        settings.color_mode = "RGBA"
        scene.cycles.samples = 16
        scene.cycles.use_denoising = False
        scene.render.threads_mode = "FIXED"
        scene.render.threads = 4

        materials = {}
        for material in bpy.data.materials:
            nodes = material.node_tree.nodes if material.node_tree else []
            materials[material.name] = {
                "emission": any(n.type == "EMISSION" for n in nodes),
                "lit": any(n.type.startswith("BSDF") and n.type != "BSDF_TRANSPARENT" for n in nodes),
                "ramps": [n.color_ramp.interpolation for n in nodes if n.type == "VALTORGB"],
            }

        white = bpy.data.materials.new("mask")
        white.use_nodes = True
        white.node_tree.nodes.clear()
        emit = white.node_tree.nodes.new("ShaderNodeEmission")
        end = white.node_tree.nodes.new("ShaderNodeOutputMaterial")
        white.node_tree.links.new(emit.outputs[0], end.inputs[0])

        def render(name, mask=False):
            scene.view_layers[0].material_override = white if mask else None
            scene.render.film_transparent = mask
            scene.render.filepath = os.path.join(out, name)
            bpy.ops.render.render(write_still=True)
            scene.view_layers[0].material_override = None
            scene.render.film_transparent = False
            image = bpy.data.images.load(scene.render.filepath)
            image.colorspace_settings.name = "Non-Color"
            pixels = array.array("f", [0.0]) * (width * height * 4)
            image.pixels.foreach_get(pixels)
            bpy.data.images.remove(image)
            return pixels

        meshes = [o for o in scene.objects if o.type == "MESH"]
        report = {"materials": materials, "frames": {}}
        for frame in frames:
            scene.frame_set(frame)
            row = {"lamp": scene.objects["Headlamp"].data.energy, "sun": scene.objects["Sun"].data.energy}
            for target in meshes:
                for o in meshes:
                    o.hide_render = o is not target
                seen = render("%s_%04d.png" % (target.name, frame))
                mask = render("%s_%04d.mask.png" % (target.name, frame), mask=True)
                count, luma = 0, 0.0
                for at in range(0, len(mask), 4):
                    if mask[at + 3] > 0.5:
                        count += 1
                        luma += 255.0 * (0.2126 * seen[at] + 0.7152 * seen[at + 1] + 0.0722 * seen[at + 2])
                row[target.name] = {"pixels": count, "luma": luma / count if count else 0.0}
            for o in meshes:
                o.hide_render = False
            report["frames"][str(frame)] = row
        with open(os.path.join(out, "lamp-report.json"), "w") as f:
            json.dump(report, f)
        """;

    private static async Task<JsonElement> RenderedAsync(string folder, IEnumerable<int> frames)
    {
        var probe = Path.Combine(folder, "lamp_probe.py");
        await File.WriteAllTextAsync(probe, Probe);
        var report = Path.Combine(folder, "lamp-report.json");
        var start = new ProcessStartInfo(BlenderFactAttribute.Executable!)
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["SILEXGIS_LAMP_OUT"] = folder;
        start.Environment["SILEXGIS_LAMP_FRAMES"] = string.Join(',', frames);
        foreach (var argument in (string[])["-b", "--factory-startup", "--python", Path.Combine(folder, "render.py"), "--python", probe, "--", "--build-only"])
        {
            start.ArgumentList.Add(argument);
        }

        using var blender = Process.Start(start)!;
        var output = blender.StandardOutput.ReadToEndAsync();
        var errors = blender.StandardError.ReadToEndAsync();
        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            await blender.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            blender.Kill(entireProcessTree: true);
            throw new TimeoutException("Blender did not finish rendering the invented cave within five minutes.");
        }

        var log = (await output) + (await errors);
        await File.WriteAllTextAsync(Path.Combine(folder, "run.log"), log);
        File.Exists(report).ShouldBeTrue($"Blender wrote no report (exit {blender.ExitCode}). The end of its log:\n{(log.Length <= 3000 ? log : log[^3000..])}");
        return JsonDocument.Parse(await File.ReadAllTextAsync(report)).RootElement.Clone();
    }
}
