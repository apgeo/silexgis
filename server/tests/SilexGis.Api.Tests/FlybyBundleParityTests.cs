// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shouldly;
using SilexGis.Api.Features.Flybys;
using SilexGis.Api.Tests.Support;
using SilexGis.Infrastructure.Flybys;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Tests;

/// <summary>
/// The camera a render bundle gives is the camera the browser flew, frame by frame.
///
/// <para>
/// The browser's tests fly an invented flyby over two invented caves and commit two things: the
/// request an export of it would send, and — worked out from the globe's arithmetic alone — where
/// on the Earth the camera of each frame is, which way it looks and how wide it sees
/// (<c>contract/blender-bundle/v1</c>). Here the bundle is made from that same request, and its
/// camera is taken back out of the bundle's own flat frame onto the Earth and set beside those
/// numbers: first as the plan states it, then, where a Blender is installed, as Blender holds it
/// once the bundle's player has built the scene.
/// </para>
///
/// <para>
/// And the look the browser drew, frame by frame — the relief, the sky, the walls and the lines
/// shown and how much of them, each cave's colours — read by the browser's tests from the function
/// that puts a moment's look on the screen. Here the bundle's keys are played, by the rules a
/// renderer plays them by, written out in this file; and Blender is asked what each model has at
/// a frame.
/// </para>
///
/// <para>
/// While the relief changes, the bundle's caves cannot follow the little their verticals lean away
/// from the origin's, and its camera is moved by the same rule so that the two stay together. So
/// the camera is held to the globe's but for that lean, stated here from the globe's arithmetic —
/// and what a reader sees is checked for itself: points of the bundle's own models through the
/// bundle's camera, against the same points of the globe through the browser's.
/// </para>
///
/// <para>
/// And what a bundle has that the screen has not: its sun and its sky go out while the camera is
/// under the ground. The browser's tests commit the frames at which it is; the rule is restated
/// here and the plan's keys are played against it.
/// </para>
///
/// <para>
/// The way back from the plan's frame to the Earth is written out in this file, and is not the
/// function the exporter went the other way with — agreeing with the browser's numbers is then
/// evidence about the exporter and not an echo of it.
/// </para>
/// </summary>
public class FlybyBundleParityTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static TheoryData<string> Cases => ["two-cave-flyby.landscape", "two-cave-flyby.portrait"];

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_plan_has_the_browsers_camera_at_every_frame(string name)
    {
        var flown = Load(name);
        using var plan = JsonDocument.Parse(FlybyBlenderBundle.PlanJson(Scene(flown.Request)));
        var root = plan.RootElement;

        // The clocks first: a frame for a frame, at the same rate, frame i at i over the rate.
        root.GetProperty("timeline").GetProperty("fps").GetInt32().ShouldBe(flown.Expected.Fps);
        root.GetProperty("timeline").GetProperty("frameCount").GetInt32().ShouldBe(flown.Expected.Frames.Length);
        var camera = root.GetProperty("camera");
        var track = camera.GetProperty("track").EnumerateArray().ToList();
        track.Count.ShouldBe(flown.Expected.Frames.Length);

        var earth = new Earth(flown.Request);
        var lean = new Lean(flown);
        var sensor = camera.GetProperty("sensorWidth").GetDouble();
        var lens = camera.GetProperty("lens").GetDouble();
        for (var i = 0; i < track.Count; i++)
        {
            track[i].GetProperty("t").GetDouble().ShouldBe(i / (double)flown.Expected.Fps, 1e-6, $"frame {i}");

            var eye = Numbers(track[i].GetProperty("eye"));
            var turn = Numbers(track[i].GetProperty("orientation"));
            var seen = new Seen(
                eye,
                Rotated(turn, [0, 0, -1]),
                Rotated(turn, [0, 1, 0]),
                track[i].TryGetProperty("lens", out var own) ? own.GetDouble() : lens,
                sensor);
            Compare(earth, flown.Expected, i, seen, eyeSlack: 0, lean.At(i));

            // What the camera focuses on lies along its line of sight, the distance the path keeps.
            var target = Numbers(track[i].GetProperty("target"));
            var along = Unit([target[0] - eye[0], target[1] - eye[1], target[2] - eye[2]]);
            Degrees(along, seen.Forward).ShouldBeLessThan(0.005, $"frame {i}: the target is not on the line of sight");
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_plans_keys_play_the_browsers_look_at_every_frame(string name)
    {
        var flown = Load(name);
        using var plan = JsonDocument.Parse(FlybyBlenderBundle.PlanJson(Scene(flown.Request)));
        var root = plan.RootElement;
        var fps = flown.Expected.Fps;

        var models = root.GetProperty("models").EnumerateArray().ToDictionary(m => m.GetProperty("id").GetString()!);
        var materials = root.GetProperty("look").GetProperty("materials").EnumerateArray()
            .ToDictionary(m => m.GetProperty("id").GetString()!, m => m.GetProperty("kind").GetString());
        var changes = root.TryGetProperty("changes", out var listed) ? listed.EnumerateArray().ToList() : [];
        JsonElement? Of(string id) => changes
            .Where(c => c.TryGetProperty("target", out var target) && target.GetProperty("id").GetString() == id)
            .Select(c => (JsonElement?)c)
            .SingleOrDefault();
        Keys? Track(string id, string property, string value = "value") =>
            Of(id) is { } change && change.TryGetProperty(property, out var keys) ? new Keys(keys, fps, value) : null;

        var world = changes
            .Where(c => !c.TryGetProperty("target", out _) && c.TryGetProperty("worldColor", out _))
            .Select(c => new Keys(c.GetProperty("worldColor"), fps))
            .SingleOrDefault();
        var sky = root.GetProperty("look").TryGetProperty("world", out var behind) ? Numbers(behind.GetProperty("color")) : [0, 0, 0];

        for (var i = 0; i < flown.Expected.Frames.Length; i++)
        {
            var row = flown.Expected.Look[i];
            var relief = flown.Expected.Frames[i][10];

            // Every model is as tall as the frame is drawn: stretched by the relief the browser
            // multiplied the camera's height by, and by nothing else.
            foreach (var (id, model) in models)
            {
                var still = model.TryGetProperty("scale", out var scale) ? Numbers(scale) : [1, 1, 1];
                var now = Track(id, "scale")?.At(i) ?? still;
                now[0].ShouldBe(1);
                now[1].ShouldBe(1);
                now[2].ShouldBe(relief, 2e-6, $"frame {i}: the stretch of {id}");
            }

            // The sky.
            Screen(world?.At(i) ?? sky).ShouldBe(Channels(row[0].GetString()!), 1.0, $"frame {i}: the sky");

            // The walls and the lines: shown or not, and how much of them while they are.
            foreach (var (id, shownAt, opacityAt) in ((string, int, int)[])[("cave-01-walls", 1, 2), ("cave-01-lines", 3, 4), ("cave-02-lines", 3, 4)])
            {
                var (shown, fade) = Shown(Of(id), i, fps);
                shown.ShouldBe(row[shownAt].GetBoolean(), $"frame {i}: {id} shown");
                if (shown)
                {
                    // How far it has come or gone, times its own strength: a renderer multiplies the two.
                    ((Track(id, "opacity")?.At(i)[0] ?? 1) * fade).ShouldBe(row[opacityAt].GetDouble(), 1e-5, $"frame {i}: how much of {id}");
                }
            }

            // The colours of each cave, at every frame: the frames it changes hands on among them.
            var tint = Track("cave-01-walls", "tint", "color");
            var tinted = Track("cave-01-walls", "tint", "strength");
            (tinted?.At(i)[0] ?? 0).ShouldBe(row[6].GetDouble(), 2e-6, $"frame {i}: the strength of the colour on the first cave's walls");
            if (row[6].GetDouble() > 0)
            {
                Screen(tint!.At(i)).ShouldBe(Channels(row[5].GetString()!), 1.0, $"frame {i}: the colour on the first cave's walls");
            }

            foreach (var (id, cave) in ((string, int)[])[("cave-01-lines", 0), ("cave-02-lines", 1)])
            {
                var want = row[7 + (cave * 3)];
                var strength = Track(id, "tint", "strength")?.At(i)[0];
                var material = materials[models[id].GetProperty("material").GetString()!];
                if (want.ValueKind == JsonValueKind.Null)
                {
                    // By depth: a material that colours by depth, and nothing laid over it.
                    material.ShouldNotBe("flat", $"frame {i}: {id} is drawn by depth");
                    (strength ?? 0).ShouldBe(0, $"frame {i}: no colour over {id}");
                }
                else if (strength is null)
                {
                    material.ShouldBe("flat", $"frame {i}: {id} has one colour and no track");
                }
                else
                {
                    strength.Value.ShouldBe(1, $"frame {i}: the colour of {id} whole");
                    Screen(Track(id, "tint", "color")!.At(i)).ShouldBe(Channels(want.GetString()!), 1.0, $"frame {i}: the colour of {id}");
                }
            }
        }
    }

    /// <summary>
    /// The sun and the sky of the bundle are as strong as the daylight its camera has: whole above
    /// the ground, and gone under it — going over a second from the frame the camera goes under,
    /// and back on the frame it comes out, having risen over the second before; half of each for a
    /// stay of less than two seconds. The lamp on the camera has no part in it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void The_sun_and_the_sky_go_out_while_the_camera_is_under_the_ground(string name)
    {
        var flown = Load(name);
        using var plan = JsonDocument.Parse(FlybyBlenderBundle.PlanJson(Scene(flown.Request)));
        var root = plan.RootElement;
        var fps = flown.Expected.Fps;
        var look = root.GetProperty("look");
        var lights = look.GetProperty("lights").EnumerateArray().ToDictionary(l => l.GetProperty("id").GetString()!);
        lights.Keys.Order().ShouldBe(["Headlamp", "Sun"]);
        var whole = lights["Sun"].GetProperty("energy").GetDouble();
        whole.ShouldBeGreaterThan(0);
        look.GetProperty("world").GetProperty("strength").GetDouble().ShouldBe(1);

        var changes = root.TryGetProperty("changes", out var listed) ? listed.EnumerateArray().ToList() : [];
        var ofTheSun = changes
            .Where(c => c.TryGetProperty("target", out var target) && target.GetProperty("id").GetString() == "Sun")
            .ToList();
        var ofTheWorld = changes.Where(c => !c.TryGetProperty("target", out _) && c.TryGetProperty("worldStrength", out _)).ToList();
        changes
            .Where(c => c.TryGetProperty("target", out var target) && target.GetProperty("id").GetString() == "Headlamp")
            .ShouldBeEmpty("the lamp on the camera is on throughout");

        var daylight = Daylight(flown.Expected, fps);
        if (flown.Expected.Underground.Length == 0)
        {
            ofTheSun.ShouldBeEmpty();
            ofTheWorld.ShouldBeEmpty();
            return;
        }

        var energy = new Keys(ofTheSun.ShouldHaveSingleItem().GetProperty("energy"), fps);
        var strength = new Keys(ofTheWorld.ShouldHaveSingleItem().GetProperty("worldStrength"), fps);
        for (var i = 0; i < daylight.Length; i++)
        {
            energy.At(i)[0].ShouldBe(whole * daylight[i], 2e-6, $"frame {i}: the sun");
            strength.At(i)[0].ShouldBe(daylight[i], 2e-6, $"frame {i}: the sky");
        }

        // And the camera really is in the dark at some frame, and in full daylight at others.
        daylight.Min().ShouldBe(0);
        daylight.Max().ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_cave_stands_where_the_globe_draws_it_at_every_frames_relief(string name)
    {
        var flown = Load(name);
        var scene = Scene(flown.Request);
        var folder = Unpacked(scene, name + ".stands");
        using var plan = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "scene-plan.json")));
        var earth = new Earth(flown.Request);
        var placed = new Placed(plan.RootElement, flown.Expected.Fps);

        // A point of a model at a frame, set back on the Earth: in the frame whose zero is the
        // origin at that frame's relief.
        double[] Stands(string id, double[] own, int frame) =>
            earth.OnTheEarth(placed.At(id, own, frame), flown.Expected.Frames[frame][10]);

        // The first cave's box has its top at the cave's own zero, 60 m above the origin and on
        // its vertical. The second cave's lines start at a place a kilometre and a half away.
        var origin = flown.Request.Origin!;
        var boxTop = BundlePly.Read(File.ReadAllBytes(Path.Combine(folder, "cave-01-walls.ply"))).Vertices.Max(v => v[2]);
        var start = BundlePly.Read(File.ReadAllBytes(Path.Combine(folder, "cave-02-lines.ply"))).Vertices.Take(4).ToList();
        double[] lineStart = [start.Average(v => v[0]), start.Average(v => v[1]), start.Average(v => v[2])];

        var reliefs = flown.Expected.Frames.Select(f => f[10]).ToList();
        var changing = reliefs.Max() > reliefs.Min();
        var worst = 0.0;
        for (var i = 0; i < reliefs.Count; i++)
        {
            var top = Stands("cave-01-walls", [0, 0, boxTop], i);
            Apart(top, Earth.Point(origin.Longitude, origin.Latitude, (origin.HeightM + 60) * reliefs[i]))
                .ShouldBeLessThan(0.002, $"frame {i}: the top of the first cave's walls");

            var off = Apart(Stands("cave-02-lines", lineStart, i), Earth.Point(9.7648, 44.3592, 700 * reliefs[i]));
            worst = Math.Max(worst, off);

            // Away from the origin's vertical a model that is being stretched is off by the lean
            // between the two verticals: here at most a quarter of a factor from the middle of the
            // ramp, 640 m of origin height and 1.56 km away — four centimetres. Exactly there
            // would take the origin's height above the ellipsoid, which the bundle does not say;
            // the camera is moved by the same rule instead, so the picture does not show it.
            off.ShouldBeLessThan(changing ? 0.045 : 0.002, $"frame {i}: the start of the second cave's lines");
        }

        if (changing)
        {
            // And it is that lean, not nothing: the bound is the real size of the thing.
            worst.ShouldBeGreaterThan(0.03);
        }
    }

    /// <summary>
    /// What a reader of the movie sees of a cave: its points, through the camera of each frame. The
    /// globe's own arithmetic says where each stands and where the browser's camera puts it in the
    /// picture; the bundle's models, stretched, turned and placed as its plan says, are put through
    /// the bundle's camera. The two pictures are one, at every frame, whatever the relief is doing.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void What_is_drawn_of_a_cave_is_in_the_picture_where_the_globe_has_it(string name)
    {
        var flown = Load(name);
        var folder = Unpacked(Scene(flown.Request), name + ".picture");
        using var plan = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "scene-plan.json")));
        var root = plan.RootElement;
        var fps = flown.Expected.Fps;
        var earth = new Earth(flown.Request);
        var placed = new Placed(root, fps);
        var camera = root.GetProperty("camera");
        var track = camera.GetProperty("track").EnumerateArray().ToList();
        var sensor = camera.GetProperty("sensorWidth").GetDouble();
        var lens = camera.GetProperty("lens").GetDouble();
        var marks = Marks(flown.Request, folder);
        var height = 1920.0 * flown.Expected.Height / flown.Expected.Width;

        var seen = 0;
        var (worstPixels, worstMetres, worstNearMetres) = (0.0, 0.0, 0.0);
        var (anchorPixels, anchorNearMetres) = (0.0, 0.0);
        var where = string.Empty;
        for (var i = 0; i < track.Count; i++)
        {
            var want = flown.Expected.Frames[i];
            var relief = want[10];
            double[] wantEye = [want[0], want[1], want[2]];
            double[] wantForward = [want[3], want[4], want[5]];
            double[] wantUp = [want[6], want[7], want[8]];

            var eye = Numbers(track[i].GetProperty("eye"));
            var turn = Numbers(track[i].GetProperty("orientation"));
            var forward = earth.Direction(Rotated(turn, [0, 0, -1]));
            var up = earth.Direction(Rotated(turn, [0, 1, 0]));
            var focal = track[i].TryGetProperty("lens", out var own) ? own.GetDouble() : lens;
            var across = 2 * Math.Atan(sensor / (2 * focal)) * 180 / Math.PI;

            foreach (var mark in marks)
            {
                // From each camera to the point, in the Earth's axes: what the picture is made of.
                var inPlan = placed.At(mark.Model, mark.Own, i);
                var drawn = earth.Direction([inPlan[0] - eye[0], inPlan[1] - eye[1], inPlan[2] - eye[2]]);
                var onGlobe = mark.OnGlobe(relief);
                double[] real = [onGlobe[0] - wantEye[0], onGlobe[1] - wantEye[1], onGlobe[2] - wantEye[2]];

                var onEarth = Projected(real, [0, 0, 0], wantForward, wantUp, want[9], 1920, height);
                var inBundle = Projected(drawn, [0, 0, 0], forward, up, across, 1920, height);
                if (onEarth is null || Math.Abs(onEarth.Value.X) > 960 || Math.Abs(onEarth.Value.Y) > height / 2)
                {
                    continue;
                }

                inBundle.ShouldNotBeNull($"frame {i}: {mark.Name} is before the browser's camera and behind the bundle's");
                seen++;
                var pixels = Math.Max(Math.Abs(onEarth.Value.X - inBundle.Value.X), Math.Abs(onEarth.Value.Y - inBundle.Value.Y));
                var metres = Apart(drawn, real);
                if (pixels > worstPixels)
                {
                    (worstPixels, where) = (pixels, $"frame {i}, {mark.Name}, {Length(real):0.#} m from the eye");
                }

                worstMetres = Math.Max(worstMetres, metres);
                if (Length(real) < 200)
                {
                    worstNearMetres = Math.Max(worstNearMetres, metres);
                }

                if (mark.AtItsModelsZero)
                {
                    anchorPixels = Math.Max(anchorPixels, pixels);
                    anchorNearMetres = Length(real) < 200 ? Math.Max(anchorNearMetres, metres) : anchorNearMetres;
                }
            }
        }

        // Kept beside the unpacked bundles, for whoever wants the numbers and not only the verdict.
        File.WriteAllText(
            Path.Combine(TestScratch.Root, "blender-bundle-parity", name + ".picture.json"),
            JsonSerializer.Serialize(new { seen, worstPixels, where, worstMetres, worstNearMetres, anchorPixels, anchorNearMetres }));

        // Every mark is in the picture at some frame, many times over.
        seen.ShouldBeGreaterThan(track.Count);

        // Where a model stands — the point it is placed by — is in the picture where the globe
        // has it to a twentieth of a pixel, the cave a kilometre and a half from the origin
        // included, and while the relief changes: a camera in the frame of each moment, beside
        // models that cannot follow, had that point eight pixels off.
        anchorPixels.ShouldBeLessThan(0.05);

        // A point away from its model's own zero is off by what stretching a model along one
        // vertical differs from raising each of its points along its own: its height above the
        // ellipsoid, times the relief less one, times the lean between the two — a centimetre
        // for a leg 200 m from where its lines start, with or without a change of relief.
        worstPixels.ShouldBeLessThan(1, where);
        worstNearMetres.ShouldBeLessThan(0.015);
    }

    [BlenderFact]
    public async Task Blender_holds_the_browsers_camera_once_the_bundle_has_built_its_scene()
    {
        foreach (var name in Cases)
        {
            var flown = Load(name);
            var folder = Unpacked(Scene(flown.Request), name);

            // Every fifth frame and the last: enough to catch a camera that drifts, turns the
            // other way or jumps at a join, at a cost of seconds. And the frames about each jump
            // of a cave's colour — where one changes hands, and a key a frame out would show —
            // and about each turn of the daylight and of the walls.
            var said = flown.Request.Changes;
            var about = (said?.Caves ?? [])
                .SelectMany(c => (c.Walls ?? []).Concat(c.Lines ?? []))
                .Where(k => k.Easing == FlybyBundleEasing.Constant)
                .Select(k => k.Frame)
                .Concat((said?.Daylight ?? []).Select(k => k.Frame))
                .Concat((said?.Walls?.Shown ?? []).SelectMany(k => (int[])[k.Frame, k.Frame + (k.FadeFrames ?? 0)]))
                .SelectMany(f => (int[])[f - 2, f - 1, f, f + 1, f + 2])
                .ToHashSet();
            var frames = Enumerable.Range(0, flown.Expected.Frames.Length)
                .Where(i => i % 5 == 0 || i == flown.Expected.Frames.Length - 1 || about.Contains(i))
                .ToList();
            var report = await ProbeAsync(folder, frames.Select(i => i + 1));

            var scene = report.GetProperty("scene");
            scene.GetProperty("frameEnd").GetInt32().ShouldBe(flown.Expected.Frames.Length);
            scene.GetProperty("fps").GetDouble().ShouldBe(flown.Expected.Fps);
            Numbers(scene.GetProperty("resolution")).ShouldBe([flown.Expected.Width, flown.Expected.Height]);

            // The caves are there, as meshes, under the names the plan gave them.
            var models = report.GetProperty("objects").EnumerateArray()
                .Where(o => o.GetProperty("type").GetString() == "MESH")
                .Select(o => o.GetProperty("name").GetString())
                .ToList();
            models.ShouldContain("cave-01-walls");
            models.ShouldContain("cave-01-lines");
            models.ShouldContain("cave-02-lines");

            var earth = new Earth(flown.Request);
            var lean = new Lean(flown);
            var daylight = Daylight(flown.Expected, flown.Expected.Fps);
            var sun = report.GetProperty("objects").EnumerateArray()
                .Single(o => o.GetProperty("name").GetString() == "Sun")
                .GetProperty("energy").GetDouble();
            var sampled = report.GetProperty("frames").EnumerateArray().ToList();
            sampled.Count.ShouldBe(frames.Count);
            for (var k = 0; k < sampled.Count; k++)
            {
                var i = frames[k];
                sampled[k].GetProperty("frame").GetInt32().ShouldBe(i + 1);
                sampled[k].GetProperty("seconds").GetDouble().ShouldBe(i / (double)flown.Expected.Fps, 1e-6);
                var held = sampled[k].GetProperty("camera");
                var seen = new Seen(
                    Numbers(held.GetProperty("location")),
                    Numbers(held.GetProperty("forward")),
                    Numbers(held.GetProperty("up")),
                    held.GetProperty("lens").GetDouble(),
                    held.GetProperty("sensorWidth").GetDouble());

                // Blender keeps positions as 32-bit floats: seven digits, whatever the distance.
                Compare(earth, flown.Expected, i, seen, eyeSlack: 2e-7, lean.At(i));

                // What each model has at this frame, as Blender worked it out from the keys.
                var row = flown.Expected.Look[i];
                var things = sampled[k].GetProperty("things").EnumerateArray().ToDictionary(t => t.GetProperty("name").GetString()!);
                foreach (var id in (string[])["cave-01-walls", "cave-01-lines", "cave-02-lines"])
                {
                    Numbers(things[id].GetProperty("scale"))[2]
                        .ShouldBe(flown.Expected.Frames[i][10], 1e-5, $"{name} frame {i}: the stretch of {id} in Blender");
                }

                foreach (var (id, shownAt, opacityAt) in ((string, int, int)[])[("cave-01-walls", 1, 2), ("cave-01-lines", 3, 4), ("cave-02-lines", 3, 4)])
                {
                    var shown = !things[id].GetProperty("hidden").GetBoolean();
                    shown.ShouldBe(row[shownAt].GetBoolean(), $"{name} frame {i}: {id} shown in Blender");
                    if (shown)
                    {
                        // Two numbers of the model multiplied by its material: a fade and a strength.
                        var there = Number(things[id], "opacity", 1) * Number(things[id], "fade", 1);
                        there.ShouldBe(row[opacityAt].GetDouble(), 1e-4, $"{name} frame {i}: how much of {id} in Blender");
                    }
                }

                Number(things["cave-01-walls"], "tintStrength", 0)
                    .ShouldBe(row[6].GetDouble(), 1e-4, $"{name} frame {i}: the strength of the colour on the first cave's walls in Blender");
                if (row[6].GetDouble() > 0)
                {
                    Screen(Numbers(things["cave-01-walls"].GetProperty("tint")))
                        .ShouldBe(Channels(row[5].GetString()!), 1.0, $"{name} frame {i}: the colour on the first cave's walls in Blender");
                }

                foreach (var (id, cave) in ((string, int)[])[("cave-01-lines", 0), ("cave-02-lines", 1)])
                {
                    var want = row[7 + (cave * 3)];
                    _ = cave;
                    if (!things[id].TryGetProperty("tintStrength", out _))
                    {
                        continue;
                    }

                    Number(things[id], "tintStrength", 0)
                        .ShouldBe(want.ValueKind == JsonValueKind.Null ? 0 : 1, 1e-4, $"{name} frame {i}: a colour over {id} in Blender");
                    if (want.ValueKind != JsonValueKind.Null)
                    {
                        Screen(Numbers(things[id].GetProperty("tint")))
                            .ShouldBe(Channels(want.GetString()!), 1.0, $"{name} frame {i}: the colour of {id} in Blender");
                    }
                }

                Screen(Numbers(sampled[k].GetProperty("world").GetProperty("color")))
                    .ShouldBe(Channels(row[0].GetString()!), 1.0, $"{name} frame {i}: the sky in Blender");

                // The daylight: the sun and the sky as strong as the camera's place lets them be,
                // and the lamp on the camera as it was built.
                things["Sun"].GetProperty("energy").GetDouble()
                    .ShouldBe(sun * daylight[i], 1e-4 * sun, $"{name} frame {i}: the sun in Blender");
                sampled[k].GetProperty("world").GetProperty("strength").GetDouble()
                    .ShouldBe(daylight[i], 1e-4, $"{name} frame {i}: the sky's strength in Blender");
                things["Headlamp"].GetProperty("energy").GetDouble()
                    .ShouldBe(Number(report.GetProperty("objects").EnumerateArray().Single(o => o.GetProperty("name").GetString() == "Headlamp"), "energy", -1), 1e-6);
            }

            // The models whose colours change read them through their materials, and the ones that
            // fade are see-through by theirs: the numbers above are what the picture is made from.
            if (flown.Request.Changes is not null)
            {
                var read = report.GetProperty("materials").EnumerateArray()
                    .ToDictionary(m => m.GetProperty("name").GetString()!);
                read["rock"].GetProperty("tinted").GetBoolean().ShouldBeTrue();
            }
        }
    }

    // ---- what is drawn, and where the globe has it ----

    /// <summary>
    /// A point of something the bundle draws: the model it is of, where it is in that model's own
    /// file, and where the globe has it when every height is multiplied by a relief.
    /// </summary>
    /// <param name="AtItsModelsZero">Whether the point is the one its model is placed by, or within a few metres of it.</param>
    private sealed record Mark(string Name, string Model, double[] Own, Func<double, double[]> OnGlobe, bool AtItsModelsZero);

    /// <summary>
    /// The corners of the first cave's walls and the ends of both caves' survey legs. Where each is
    /// on the globe is worked out here from the invented scene — a mesh point stands east, north
    /// and up of its anchor, its height above the ellipsoid multiplied by the relief — and where it
    /// is in the bundle is read from the bundle's own files.
    /// </summary>
    private static List<Mark> Marks(FlybyBlenderBundleRequest request, string folder)
    {
        var origin = request.Origin!;
        var marks = new List<Mark>();

        var walls = BundlePly.Read(File.ReadAllBytes(Path.Combine(folder, "cave-01-walls.ply"))).Vertices;
        walls.Count.ShouldBe(Box.Length / 3);
        var anchor = (origin.Longitude, origin.Latitude, Height: origin.HeightM + 60);
        for (var v = 0; v < walls.Count; v++)
        {
            double[] corner = [Box[v * 3], Box[(v * 3) + 1], Box[(v * 3) + 2]];
            marks.Add(new Mark($"corner {v} of the first cave's walls", "cave-01-walls", walls[v], relief =>
            {
                var at = Earth.Point(anchor.Longitude, anchor.Latitude, (anchor.Height + corner[2]) * relief);
                var (east, north, _) = Earth.Axes(anchor.Longitude, anchor.Latitude);
                return [.. at.Select((value, axis) => value + (corner[0] * east[axis]) + (corner[1] * north[axis]))];
            }, AtItsModelsZero: true));
        }

        foreach (var (model, legs) in ((string, FlybyBundleLeg[])[])[("cave-01-lines", FirstLegs(request)), ("cave-02-lines", SecondLegs)])
        {
            // Tubes in the order of the legs, four corners about each end: the first four stand
            // about where the first leg starts, the last four about where the last one ends.
            var tubes = BundlePly.Read(File.ReadAllBytes(Path.Combine(folder, model + ".ply"))).Vertices;
            foreach (var (what, corners, longitude, latitude, altitude) in ((string, List<double[]>, double, double, double)[])
                     [
                         ("start", [.. tubes.Take(4)], legs[0].FromLongitude, legs[0].FromLatitude, legs[0].FromAltitudeM),
                         ("end", [.. tubes.TakeLast(4)], legs[^1].ToLongitude, legs[^1].ToLatitude, legs[^1].ToAltitudeM),
                     ])
            {
                double[] middle = [corners.Average(c => c[0]), corners.Average(c => c[1]), corners.Average(c => c[2])];
                marks.Add(new Mark(
                    $"the {what} of {model}", model, middle, relief => Earth.Point(longitude, latitude, altitude * relief), what == "start"));
            }
        }

        return marks;
    }

    /// <summary>
    /// The models of a plan, each point of which can be asked where it stands at a frame: stretched
    /// as the model is at that frame, turned and placed as the plan says.
    /// </summary>
    private sealed class Placed(JsonElement root, int fps)
    {
        private readonly Dictionary<string, JsonElement> models =
            root.GetProperty("models").EnumerateArray().ToDictionary(m => m.GetProperty("id").GetString()!);

        private readonly List<JsonElement> changes =
            root.TryGetProperty("changes", out var listed) ? [.. listed.EnumerateArray()] : [];

        public double[] At(string id, double[] own, int frame)
        {
            var model = models[id];
            var keys = changes
                .Where(c => c.TryGetProperty("target", out var target) && target.GetProperty("id").GetString() == id && c.TryGetProperty("scale", out _))
                .Select(c => new Keys(c.GetProperty("scale"), fps))
                .SingleOrDefault();
            var scale = keys?.At(frame) ?? (model.TryGetProperty("scale", out var s) ? Numbers(s) : [1, 1, 1]);
            double[] stretched = [own[0] * scale[0], own[1] * scale[1], own[2] * scale[2]];
            if (model.TryGetProperty("rotation", out var rotation))
            {
                stretched = Turned(stretched, Unit(Numbers(rotation.GetProperty("axis"))), rotation.GetProperty("degrees").GetDouble());
            }

            var at = model.TryGetProperty("translation", out var t) ? Numbers(t) : [0, 0, 0];
            return [at[0] + stretched[0], at[1] + stretched[1], at[2] + stretched[2]];
        }
    }

    // ---- the lean, and the daylight ----

    /// <summary>
    /// How far, at each frame, the bundle's camera stands from where the globe has it, in metres.
    ///
    /// <para>
    /// The globe multiplies heights about the ellipsoid; a bundle that says no height above the
    /// ellipsoid stretches about the level of its own zero instead. On the origin's vertical the two
    /// are one. Away from it they differ by the change of the relief from the one the scene was laid
    /// out at — the middle of the clip's range — times the origin's height, times the sine of the
    /// angle between the place's vertical and the origin's. A model there is off by that, and so,
    /// by the same rule, is a camera.
    /// </para>
    /// </summary>
    private sealed class Lean
    {
        private readonly double[] metres;

        public Lean(Flown flown)
        {
            var reliefs = flown.Expected.Frames.Select(f => f[10]).ToList();
            var laidOutAt = (reliefs.Min() + reliefs.Max()) / 2;
            var origin = flown.Request.Origin!;
            var up = Earth.Axes(origin.Longitude, origin.Latitude).Up;
            metres =
            [
                .. flown.Request.Frames!.Select((frame, i) =>
                {
                    var own = Earth.Axes(frame[0], frame[1]).Up;
                    double[] cross =
                    [
                        (own[1] * up[2]) - (own[2] * up[1]),
                        (own[2] * up[0]) - (own[0] * up[2]),
                        (own[0] * up[1]) - (own[1] * up[0]),
                    ];
                    return Math.Abs(reliefs[i] - laidOutAt) * origin.HeightM * Length(cross);
                }),
            ];
        }

        public double At(int frame) => metres[frame];
    }

    /// <summary>
    /// How much of the daylight each frame has, from the frames the camera is under the ground at:
    /// all of it above the ground; under it, eased down over a second from the frame it goes under
    /// and eased up over the second before the frame it comes out — or over half the stay, where
    /// that is less. Written out here from the rule, not taken from what wrote the keys.
    /// </summary>
    private static double[] Daylight(Expected expected, int fps)
    {
        var daylight = Enumerable.Repeat(1.0, expected.Frames.Length).ToArray();
        foreach (var run in expected.Underground)
        {
            var (goes, comes) = (run[0], run[1]);
            (comes - goes).ShouldBeGreaterThanOrEqualTo(fps, "a dip of less than a second is no stay underground, and this check has none");
            goes.ShouldBeGreaterThan(0);
            comes.ShouldBeLessThan(expected.Frames.Length);
            var takes = Math.Min(fps, (comes - goes) / 2);
            for (var i = goes; i < comes; i++)
            {
                var share = Math.Min(1.0, Math.Min(i - goes, comes - i) / (double)takes);
                daylight[i] = 1 - (share * share * (3 - (2 * share)));
            }
        }

        return daylight;
    }

    // ---- the comparison ----

    /// <summary>A camera in the plan's frame: eye, the way it looks, the top of its picture, its lens.</summary>
    private sealed record Seen(double[] Eye, double[] Forward, double[] Up, double Lens, double Sensor);

    /// <param name="lean">How far the bundle's camera stands from the globe's at this frame, by the rule its caves are stretched by.</param>
    private static void Compare(Earth earth, Expected expected, int i, Seen seen, double eyeSlack, double lean)
    {
        var want = expected.Frames[i];
        double[] wantEye = [want[0], want[1], want[2]];
        double[] wantForward = [want[3], want[4], want[5]];
        double[] wantUp = [want[6], want[7], want[8]];

        // A millimetre, and for a renderer's own arithmetic a share of how far from the origin the
        // eye stands. Plus the rounding the plan is written to. The frame of each moment has its
        // zero at the origin's height times the relief that moment is drawn at. And the lean: the
        // camera is where the globe has it but for that — and it is off by that, not by nothing,
        // or the caves, which are, would be seen from the wrong place.
        var relief = want[10];
        var eye = earth.OnTheEarth(seen.Eye, relief);
        var apart = Length([eye[0] - wantEye[0], eye[1] - wantEye[1], eye[2] - wantEye[2]]);
        var slack = 0.001 + 2e-6 + (eyeSlack * Length(seen.Eye));
        apart.ShouldBeLessThan(slack + lean, $"frame {i}: the eye");
        apart.ShouldBeGreaterThan(lean - slack, $"frame {i}: the eye is moved with the caves");

        Degrees(earth.Direction(seen.Forward), wantForward).ShouldBeLessThan(0.005, $"frame {i}: the way it looks");
        Degrees(earth.Direction(seen.Up), wantUp).ShouldBeLessThan(0.01, $"frame {i}: the top of the picture");

        var across = 2 * Math.Atan(seen.Sensor / (2 * seen.Lens)) * 180 / Math.PI;
        across.ShouldBe(want[9], 1e-4, $"frame {i}: the angle it sees");

        // Twenty points through both cameras, on a picture 1920 wide: within half a pixel. A wrong
        // axis or a wrong shape of frame shows here even when each number above looks right. The
        // points are the globe's and the camera is the bundle's, so a point is off by what the
        // lean is at its distance, and by no more.
        var height = 1920.0 * expected.Height / expected.Width;
        var pixelsAMetre = 1920 / 2 / Math.Tan(want[9] * Math.PI / 360);
        foreach (var place in expected.Points)
        {
            // Drawn at this frame's relief, like everything else in it.
            var point = Earth.Point(place[0], place[1], place[2] * relief);
            var onEarth = Projected(point, wantEye, wantForward, wantUp, want[9], 1920, height);
            var inPlan = Projected(
                point, eye, earth.Direction(seen.Forward), earth.Direction(seen.Up), across, 1920, height);
            if (onEarth is null || inPlan is null)
            {
                (onEarth is null).ShouldBe(inPlan is null, $"frame {i}: a point is before one camera and behind the other");
                continue;
            }

            // Only where the point is in or near the picture: far outside it, half a pixel is a
            // demand on the tangent of an angle nobody will ever see.
            if (Math.Abs(onEarth.Value.X) > 4000 || Math.Abs(onEarth.Value.Y) > 4000)
            {
                continue;
            }

            var depth = Dot([point[0] - wantEye[0], point[1] - wantEye[1], point[2] - wantEye[2]], wantForward);
            var pixels = 0.5 + (eyeSlack > 0 ? 0.5 : 0) + (lean / depth * pixelsAMetre * 3);
            Math.Abs(onEarth.Value.X - inPlan.Value.X).ShouldBeLessThan(pixels, $"frame {i}: a point, across");
            Math.Abs(onEarth.Value.Y - inPlan.Value.Y).ShouldBeLessThan(pixels, $"frame {i}: a point, down");
        }
    }

    /// <summary>Where a point falls in a picture, in pixels from its middle; null when it is behind the camera.</summary>
    private static (double X, double Y)? Projected(
        double[] point, double[] eye, double[] forward, double[] up, double acrossDegrees, double width, double height)
    {
        double[] to = [point[0] - eye[0], point[1] - eye[1], point[2] - eye[2]];
        var depth = Dot(to, forward);
        if (depth <= 1)
        {
            return null;
        }

        double[] right =
        [
            (forward[1] * up[2]) - (forward[2] * up[1]),
            (forward[2] * up[0]) - (forward[0] * up[2]),
            (forward[0] * up[1]) - (forward[1] * up[0]),
        ];
        var scale = width / 2 / Math.Tan(acrossDegrees * Math.PI / 360);
        _ = height;
        return (Dot(to, right) / depth * scale, Dot(to, up) / depth * scale);
    }

    /// <summary>
    /// The plan's frame set back on the Earth: east, north and up at the request's origin, the origin
    /// at its height times the relief a frame is drawn at. Written out from the ellipsoid, here.
    /// </summary>
    private sealed class Earth
    {
        private const double A = 6_378_137;
        private const double F = 1 / 298.257223563;
        private readonly double longitude;
        private readonly double latitude;
        private readonly double heightM;
        private readonly double[] east;
        private readonly double[] north;
        private readonly double[] up;

        public Earth(FlybyBlenderBundleRequest request)
        {
            longitude = request.Origin!.Longitude;
            latitude = request.Origin.Latitude;
            heightM = request.Origin.HeightM;
            var lambda = longitude * Math.PI / 180;
            var phi = latitude * Math.PI / 180;
            east = [-Math.Sin(lambda), Math.Cos(lambda), 0];
            north = [-Math.Sin(phi) * Math.Cos(lambda), -Math.Sin(phi) * Math.Sin(lambda), Math.Cos(phi)];
            up = [Math.Cos(phi) * Math.Cos(lambda), Math.Cos(phi) * Math.Sin(lambda), Math.Sin(phi)];
        }

        /// <summary>A place of the globe at a height, earth-centred.</summary>
        public static double[] Point(double longitude, double latitude, double height)
        {
            var lambda = longitude * Math.PI / 180;
            var phi = latitude * Math.PI / 180;
            var e2 = F * (2 - F);
            var n = A / Math.Sqrt(1 - (e2 * Math.Sin(phi) * Math.Sin(phi)));
            return
            [
                (n + height) * Math.Cos(phi) * Math.Cos(lambda),
                (n + height) * Math.Cos(phi) * Math.Sin(lambda),
                ((n * (1 - e2)) + height) * Math.Sin(phi),
            ];
        }

        /// <summary>East, north and up at a place, each as a direction in earth-centred axes.</summary>
        public static (double[] East, double[] North, double[] Up) Axes(double longitude, double latitude)
        {
            var lambda = longitude * Math.PI / 180;
            var phi = latitude * Math.PI / 180;
            return (
                [-Math.Sin(lambda), Math.Cos(lambda), 0],
                [-Math.Sin(phi) * Math.Cos(lambda), -Math.Sin(phi) * Math.Sin(lambda), Math.Cos(phi)],
                [Math.Cos(phi) * Math.Cos(lambda), Math.Cos(phi) * Math.Sin(lambda), Math.Sin(phi)]);
        }

        public double[] Direction(double[] v) =>
        [
            (v[0] * east[0]) + (v[1] * north[0]) + (v[2] * up[0]),
            (v[0] * east[1]) + (v[1] * north[1]) + (v[2] * up[1]),
            (v[0] * east[2]) + (v[1] * north[2]) + (v[2] * up[2]),
        ];

        public double[] OnTheEarth(double[] p, double relief)
        {
            var origin = Point(longitude, latitude, heightM * relief);
            var d = Direction(p);
            return [origin[0] + d[0], origin[1] + d[1], origin[2] + d[2]];
        }
    }

    // ---- keys, played ----

    /// <summary>
    /// One property's keys as the plan writes them, played as a renderer plays them: a key's time
    /// falls on the frame nearest to it — which may be after the clip's last; before the first key
    /// its value, after the last its value; between two, moved as the later one's easing says —
    /// evenly, along 3x² − 2x³, or not at all until its frame. Of two keys on one frame the later is
    /// the value from that frame on, and the earlier where the value before was going. A colour
    /// whose key says so is mixed on its way as a screen mixes two colours, not as light.
    /// </summary>
    private sealed class Keys
    {
        private readonly List<(int Frame, double[] Value, string Easing, bool AsAScreen)> keys;

        public Keys(JsonElement list, int fps, string value = "value")
        {
            keys =
            [
                .. list.EnumerateArray().Select(k => (
                    (int)Math.Round(k.GetProperty("t").GetDouble() * fps),
                    k.GetProperty(value).ValueKind == JsonValueKind.Array ? Numbers(k.GetProperty(value)) : [k.GetProperty(value).GetDouble()],
                    k.TryGetProperty("easing", out var easing) ? easing.GetString()! : "linear",
                    k.GetProperty(value).ValueKind == JsonValueKind.Array && k.TryGetProperty("mix", out var mix) && mix.GetString() == "srgb")),
            ];
            for (var k = 1; k < keys.Count; k++)
            {
                keys[k].Frame.ShouldBeGreaterThanOrEqualTo(keys[k - 1].Frame);
                if (keys[k].Frame == keys[k - 1].Frame)
                {
                    keys[k].Easing.ShouldBe("constant", "the second key on a frame is a jump");
                    (k < 2 || keys[k - 2].Frame < keys[k].Frame).ShouldBeTrue("no more than two keys stand on one frame");
                }
            }
        }

        /// <summary>How many keys the property has.</summary>
        public int Count => keys.Count;

        public double[] At(int frame)
        {
            var after = keys.FindIndex(k => k.Frame > frame);
            if (after <= 0)
            {
                return after == 0 ? keys[0].Value : keys[^1].Value;
            }

            var (a, b) = (keys[after - 1], keys[after]);
            var share = (frame - a.Frame) / (double)(b.Frame - a.Frame);
            var eased = b.Easing switch
            {
                "constant" => 0,
                "smooth" => share * share * (3 - (2 * share)),
                _ => share,
            };
            return b.AsAScreen
                ? [.. a.Value.Zip(b.Value, (from, to) => FromScreen(ToScreen(from) + ((ToScreen(to) - ToScreen(from)) * eased)))]
                : [.. a.Value.Zip(b.Value, (from, to) => from + ((to - from) * eased))];
        }
    }

    /// <summary>
    /// Whether a model is shown at a frame, and how far it has come or gone: by its keys, each of
    /// which shows it from its frame on, or hides it once the frames it takes to go have passed;
    /// before the first it is the other way round; with none it is shown. A key with a fade takes
    /// that long from its own frame, evenly or eased.
    /// </summary>
    private static (bool Shown, double Fade) Shown(JsonElement? change, int frame, int fps)
    {
        if (change is not { } has || !has.TryGetProperty("visible", out var keys))
        {
            return (true, 1);
        }

        var list = keys.EnumerateArray().ToList();
        var shown = !list[0].GetProperty("value").GetBoolean();
        var fade = 1.0;
        foreach (var key in list)
        {
            var t = key.GetProperty("t").GetDouble();
            var at = (int)Math.Round(t * fps);
            var takes = key.TryGetProperty("fade", out var length) ? (int)Math.Round((t + length.GetDouble()) * fps) - at : 0;
            if (at > frame)
            {
                break;
            }

            var share = takes == 0 ? 1 : Math.Min(1.0, (frame - at) / (double)takes);
            if (takes > 0 && key.TryGetProperty("easing", out var easing) && easing.GetString() == "smooth")
            {
                share = share * share * (3 - (2 * share));
            }

            (shown, fade) = key.GetProperty("value").GetBoolean() ? (true, share) : (frame < at + takes, 1 - share);
        }

        return (shown, shown ? fade : 0);
    }

    /// <summary>A share of light, 0–1, as the share of a screen's number that shows it; and back.</summary>
    private static double ToScreen(double light) => light <= 0.0031308 ? light * 12.92 : (1.055 * Math.Pow(light, 1 / 2.4)) - 0.055;

    private static double FromScreen(double screen) => screen <= 0.04045 ? screen / 12.92 : Math.Pow((screen + 0.055) / 1.055, 2.4);

    /// <summary>A colour written <c>#rrggbb</c> as the three numbers a screen is given, 0–255.</summary>
    private static double[] Channels(string hex) =>
        [Convert.ToInt32(hex[1..3], 16), Convert.ToInt32(hex[3..5], 16), Convert.ToInt32(hex[5..7], 16)];

    /// <summary>A colour as light, 0–1 a channel, as the numbers a screen is given for it.</summary>
    private static double[] Screen(double[] light) =>
        [.. light.Take(3).Select(c => 255 * (c <= 0.0031308 ? c * 12.92 : (1.055 * Math.Pow(c, 1 / 2.4)) - 0.055))];

    private static double Number(JsonElement thing, string name, double otherwise) =>
        thing.TryGetProperty(name, out var value) ? value.GetDouble() : otherwise;

    // ---- small arithmetic ----

    private static double[] Numbers(JsonElement list) => [.. list.EnumerateArray().Select(n => n.GetDouble())];

    private static double Dot(double[] a, double[] b) => (a[0] * b[0]) + (a[1] * b[1]) + (a[2] * b[2]);

    private static double Length(double[] v) => Math.Sqrt(Dot(v, v));

    private static double Apart(double[] a, double[] b) => Length([a[0] - b[0], a[1] - b[1], a[2] - b[2]]);

    /// <summary>A vector turned about a unit axis, right-handed, by an angle in degrees.</summary>
    private static double[] Turned(double[] v, double[] axis, double degrees)
    {
        var (sin, cos) = Math.SinCos(degrees * Math.PI / 180);
        double[] cross =
        [
            (axis[1] * v[2]) - (axis[2] * v[1]),
            (axis[2] * v[0]) - (axis[0] * v[2]),
            (axis[0] * v[1]) - (axis[1] * v[0]),
        ];
        var along = Dot(axis, v) * (1 - cos);
        return [(v[0] * cos) + (cross[0] * sin) + (axis[0] * along), (v[1] * cos) + (cross[1] * sin) + (axis[1] * along), (v[2] * cos) + (cross[2] * sin) + (axis[2] * along)];
    }

    private static double[] Unit(double[] v)
    {
        var length = Length(v);
        return [v[0] / length, v[1] / length, v[2] / length];
    }

    /// <summary>The angle between two directions, in degrees.</summary>
    private static double Degrees(double[] a, double[] b)
    {
        double[] cross =
        [
            (a[1] * b[2]) - (a[2] * b[1]),
            (a[2] * b[0]) - (a[0] * b[2]),
            (a[0] * b[1]) - (a[1] * b[0]),
        ];
        return Math.Atan2(Length(cross), Dot(a, b)) * 180 / Math.PI;
    }

    /// <summary>A vector turned by a quaternion written w, x, y, z.</summary>
    private static double[] Rotated(double[] q, double[] v)
    {
        double[] axis = [q[1], q[2], q[3]];
        double[] t =
        [
            2 * ((axis[1] * v[2]) - (axis[2] * v[1])),
            2 * ((axis[2] * v[0]) - (axis[0] * v[2])),
            2 * ((axis[0] * v[1]) - (axis[1] * v[0])),
        ];
        return
        [
            v[0] + (q[0] * t[0]) + ((axis[1] * t[2]) - (axis[2] * t[1])),
            v[1] + (q[0] * t[1]) + ((axis[2] * t[0]) - (axis[0] * t[2])),
            v[2] + (q[0] * t[2]) + ((axis[0] * t[1]) - (axis[1] * t[0])),
        ];
    }

    // ---- the request, and an invented scene around it ----

    /// <param name="Points">Invented places: longitude, latitude and true height.</param>
    /// <param name="Look">Per frame: sky, walls shown, how much; lines shown, how much; then per cave the colour on its walls, its strength and its lines' one colour.</param>
    /// <param name="Frames">Per frame: the eye, the way it looks, the top of its picture, the angle it sees and the relief it is drawn at.</param>
    /// <param name="Underground">The frames at which the camera is under the ground, as runs: a first frame, and the frame it is above the ground again.</param>
    private sealed record Expected(int Width, int Height, int Fps, double[][] Points, int[][] Underground, JsonElement[][] Look, double[][] Frames);

    private sealed record Flown(FlybyBlenderBundleRequest Request, Expected Expected);

    private static Flown Load(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "server", "SilexGis.slnx")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("Could not find the working tree above the test assembly.");
        var contract = Path.Combine(directory.FullName, "contract", "blender-bundle", "v1");
        var request = JsonSerializer.Deserialize<FlybyBlenderBundleRequest>(
            File.ReadAllText(Path.Combine(contract, name + ".request.json")), Web)!;
        var expected = JsonSerializer.Deserialize<Expected>(
            File.ReadAllText(Path.Combine(contract, name + ".expected.json")), Web)!;

        // What the browser committed is a request this application would take.
        new FlybyBlenderBundleRequestValidator().Validate(request).Errors.ShouldBeEmpty();
        expected.Frames.Length.ShouldBe(request.Frames!.Count);
        expected.Look.Length.ShouldBe(expected.Frames.Length);
        expected.Underground.ShouldAllBe(run => run.Length == 2 && run[0] < run[1]);
        return new Flown(request, expected);
    }

    /// <summary>The first cave's walls: a box forty metres by sixteen, thirty deep, its top at the cave's own zero.</summary>
    private static readonly double[] Box =
        [-20, -8, -30, 20, -8, -30, 20, 8, -30, -20, 8, -30, -20, -8, 0, 20, -8, 0, 20, 8, 0, -20, 8, 0];

    private static FlybyBundleLeg[] FirstLegs(FlybyBlenderBundleRequest request)
    {
        var origin = request.Origin!;
        return
        [
            new FlybyBundleLeg(origin.Longitude - 0.0006, origin.Latitude + 0.0003, 710, origin.Longitude, origin.Latitude, 640),
            new FlybyBundleLeg(origin.Longitude, origin.Latitude, 640, origin.Longitude + 0.0009, origin.Latitude - 0.0005, 570),
        ];
    }

    private static readonly FlybyBundleLeg[] SecondLegs =
    [
        new(9.7648, 44.3592, 700, 9.7655, 44.3588, 590),
        new(9.7655, 44.3588, 590, 9.7667, 44.3581, 480),
    ];

    /// <summary>
    /// The request's camera, look and texts, about two invented caves: the first a box of walls and
    /// a few legs at the origin, the second a few legs where the flyby's second subject stands.
    /// </summary>
    private static FlybyBundleScene Scene(FlybyBlenderBundleRequest request)
    {
        var origin = request.Origin!;
        var box = new IndexedMesh(
            Box,
            [0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6, 0, 4, 5, 0, 5, 1, 1, 5, 6, 1, 6, 2, 2, 6, 7, 2, 7, 3, 3, 7, 4, 3, 4, 0]);
        // What the request says of the look over the clip, as the route hands it on.
        var changes = request.Changes;
        static List<FlybyBundleTintKey>? Tints(IReadOnlyList<FlybyBundleTintKeyRequest>? keys) =>
            keys is null ? null : [.. keys.Select(k => new FlybyBundleTintKey(k.Frame, k.Colour, k.Strength, k.Easing ?? FlybyBundleEasing.Linear))];
        static List<FlybyBundleNumberKey> Numbered(IReadOnlyList<FlybyBundleNumberKeyRequest>? keys) =>
            [.. (keys ?? []).Select(k => new FlybyBundleNumberKey(k.Frame, k.Value, k.Easing ?? FlybyBundleEasing.Linear))];
        static FlybyBundleLayerChanges Layer(FlybyBundleLayerChangesRequest? layer) => new(
            [.. (layer?.Shown ?? []).Select(k => new FlybyBundleShownKey(k.Frame, k.Shown, k.FadeFrames ?? 0))],
            Numbered(layer?.Opacity),
            Tints(layer?.Tint) ?? []);
        FlybyBundleCaveChangesRequest? Apart(int cave) => changes?.Caves?.FirstOrDefault(c => c.CaveId == request.Caves![cave].CaveId);

        var first = new FlybyBundleCave(
            new FlybyBundleMesh(box, origin.Longitude, origin.Latitude, origin.HeightM + 60),
            null,
            null,
            FirstLegs(request),
            AltitudeShiftM: 0)
        {
            WallsTint = Tints(Apart(0)?.Walls),
            LinesTint = Tints(Apart(0)?.Lines),
        };
        var second = new FlybyBundleCave(
            null,
            null,
            null,
            SecondLegs,
            AltitudeShiftM: 0)
        {
            WallsTint = Tints(Apart(1)?.Walls),
            LinesTint = Tints(Apart(1)?.Lines),
        };

        var texts = request.Texts!;
        return new FlybyBundleScene(
            request.Name ?? string.Empty,
            request.Output!.Width,
            request.Output.Height,
            (int)request.Output.Fps,
            request.Output.Quality!.Value,
            origin.Longitude,
            origin.Latitude,
            origin.HeightM,
            request.Relief,
            [.. request.Frames!.Select(f => new FlybyBundlePose(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7]))],
            [first, second],
            new FlybyBundleLook(
                request.Look!.Sky,
                request.Look.LineColour,
                [.. (request.Look.DepthBands ?? []).Select(b => new FlybyBundleDepthBand(b.FromM, b.Colour))]),
            new FlybyBundleTexts(
                texts.Title is { } title ? new FlybyBundleCard(title.Text, title.Subtitle, title.Seconds) : null,
                texts.End is { } end ? new FlybyBundleCard(end.Text, end.Subtitle, end.Seconds) : null,
                [.. (texts.Captions ?? []).Select(c => new FlybyBundleCaption(c.Text, c.StartS, c.EndS))],
                texts.Corner,
                [.. (texts.CornerCaptions ?? []).Select(c => new FlybyBundleCaption(c.Text, c.StartS, c.EndS))],
                texts.FadeInS,
                texts.FadeOutS,
                texts.NorthArrow),
            [])
        {
            Changes = new FlybyBundleChanges(
                Numbered(changes?.Relief),
                [.. (changes?.Sky ?? []).Select(k => new FlybyBundleColourKey(k.Frame, k.Colour, k.Easing ?? FlybyBundleEasing.Linear))],
                Layer(changes?.Walls),
                Layer(changes?.Lines))
            {
                Daylight = Numbered(changes?.Daylight),
            },
        };
    }

    // ---- Blender ----

    /// <summary>The bundle of a scene, unpacked the way its reader would unpack it; the folder is kept for a failure to be looked at.</summary>
    private static string Unpacked(FlybyBundleScene scene, string name)
    {
        var root = Path.Combine(TestScratch.Root, "blender-bundle-parity", name);
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

    /// <summary>
    /// Builds the bundle's scene in Blender without rendering it — the player's own inspect mode —
    /// and answers what the survey library's scene probe reads of it at the frames named.
    /// </summary>
    private static async Task<JsonElement> ProbeAsync(string folder, IEnumerable<int> frames)
    {
        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "server", "SilexGis.slnx")))
        {
            tree = tree.Parent;
        }

        var probe = Path.Combine(
            tree!.FullName, "server", "external", "ThIDE", "tests", "Therion.Blender.Tests", "SceneProbe", "scene_probe.py");
        File.Exists(probe).ShouldBeTrue($"The scene probe is not in the survey library's checkout: {probe}");

        var report = Path.Combine(folder, "scene-report.json");
        var start = new ProcessStartInfo(BlenderFactAttribute.Executable!)
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in (string[])
                 [
                     "-b", "--factory-startup", "--python", Path.Combine(folder, "render.py"), "--python", probe, "--",
                     "--build-only", "--report", report,
                     "--frames", string.Join(',', frames.Select(f => f.ToString(CultureInfo.InvariantCulture))),
                 ])
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
            throw new TimeoutException("Blender did not finish building the scene within five minutes.");
        }

        var log = (await output) + (await errors);
        File.WriteAllText(Path.Combine(folder, "run.log"), log);
        File.Exists(report).ShouldBeTrue($"Blender wrote no report (exit {blender.ExitCode}). The end of its log:\n{Tail(log)}");
        return JsonDocument.Parse(File.ReadAllText(report)).RootElement.Clone();
    }

    private static string Tail(string log) => log.Length <= 3000 ? log : log[^3000..];
}
