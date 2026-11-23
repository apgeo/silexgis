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
            Compare(earth, flown.Expected, i, seen, eyeSlack: 0);

            // What the camera focuses on lies along its line of sight, the distance the path keeps.
            var target = Numbers(track[i].GetProperty("target"));
            var along = Unit([target[0] - eye[0], target[1] - eye[1], target[2] - eye[2]]);
            Degrees(along, seen.Forward).ShouldBeLessThan(0.005, $"frame {i}: the target is not on the line of sight");
        }
    }

    [BlenderFact]
    public async Task Blender_holds_the_browsers_camera_once_the_bundle_has_built_its_scene()
    {
        foreach (var name in Cases)
        {
            var flown = Load(name);
            var folder = Unpacked(Scene(flown.Request), name);

            // Every fifth frame and the last: enough to catch a camera that drifts, turns the
            // other way or jumps at a join, at a cost of seconds.
            var frames = Enumerable.Range(0, flown.Expected.Frames.Length)
                .Where(i => i % 5 == 0 || i == flown.Expected.Frames.Length - 1)
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
                Compare(earth, flown.Expected, i, seen, eyeSlack: 2e-7);
            }
        }
    }

    // ---- the comparison ----

    /// <summary>A camera in the plan's frame: eye, the way it looks, the top of its picture, its lens.</summary>
    private sealed record Seen(double[] Eye, double[] Forward, double[] Up, double Lens, double Sensor);

    private static void Compare(Earth earth, Expected expected, int i, Seen seen, double eyeSlack)
    {
        var want = expected.Frames[i];
        double[] wantEye = [want[0], want[1], want[2]];
        double[] wantForward = [want[3], want[4], want[5]];
        double[] wantUp = [want[6], want[7], want[8]];

        // A millimetre, and for a renderer's own arithmetic a share of how far from the origin the
        // eye stands. Plus the rounding the plan is written to.
        var eye = earth.OnTheEarth(seen.Eye);
        var apart = Length([eye[0] - wantEye[0], eye[1] - wantEye[1], eye[2] - wantEye[2]]);
        apart.ShouldBeLessThan(0.001 + 2e-6 + (eyeSlack * Length(seen.Eye)), $"frame {i}: the eye");

        Degrees(earth.Direction(seen.Forward), wantForward).ShouldBeLessThan(0.005, $"frame {i}: the way it looks");
        Degrees(earth.Direction(seen.Up), wantUp).ShouldBeLessThan(0.01, $"frame {i}: the top of the picture");

        var across = 2 * Math.Atan(seen.Sensor / (2 * seen.Lens)) * 180 / Math.PI;
        across.ShouldBe(want[9], 1e-4, $"frame {i}: the angle it sees");

        // Twenty points through both cameras, on a picture 1920 wide: within half a pixel. A wrong
        // axis or a wrong shape of frame shows here even when each number above looks right.
        var height = 1920.0 * expected.Height / expected.Width;
        foreach (var point in expected.Points)
        {
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

            var slack = 0.5 + (eyeSlack > 0 ? 0.5 : 0);
            Math.Abs(onEarth.Value.X - inPlan.Value.X).ShouldBeLessThan(slack, $"frame {i}: a point, across");
            Math.Abs(onEarth.Value.Y - inPlan.Value.Y).ShouldBeLessThan(slack, $"frame {i}: a point, down");
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
    /// at its height times the relief. Written out from the ellipsoid, here.
    /// </summary>
    private sealed class Earth
    {
        private const double A = 6_378_137;
        private const double F = 1 / 298.257223563;
        private readonly double[] origin;
        private readonly double[] east;
        private readonly double[] north;
        private readonly double[] up;

        public Earth(FlybyBlenderBundleRequest request)
        {
            var lambda = request.Origin!.Longitude * Math.PI / 180;
            var phi = request.Origin.Latitude * Math.PI / 180;
            var e2 = F * (2 - F);
            var n = A / Math.Sqrt(1 - (e2 * Math.Sin(phi) * Math.Sin(phi)));
            var h = request.Origin.HeightM * request.Relief;
            origin =
            [
                (n + h) * Math.Cos(phi) * Math.Cos(lambda),
                (n + h) * Math.Cos(phi) * Math.Sin(lambda),
                ((n * (1 - e2)) + h) * Math.Sin(phi),
            ];
            east = [-Math.Sin(lambda), Math.Cos(lambda), 0];
            north = [-Math.Sin(phi) * Math.Cos(lambda), -Math.Sin(phi) * Math.Sin(lambda), Math.Cos(phi)];
            up = [Math.Cos(phi) * Math.Cos(lambda), Math.Cos(phi) * Math.Sin(lambda), Math.Sin(phi)];
        }

        public double[] Direction(double[] v) =>
        [
            (v[0] * east[0]) + (v[1] * north[0]) + (v[2] * up[0]),
            (v[0] * east[1]) + (v[1] * north[1]) + (v[2] * up[1]),
            (v[0] * east[2]) + (v[1] * north[2]) + (v[2] * up[2]),
        ];

        public double[] OnTheEarth(double[] p)
        {
            var d = Direction(p);
            return [origin[0] + d[0], origin[1] + d[1], origin[2] + d[2]];
        }
    }

    // ---- small arithmetic ----

    private static double[] Numbers(JsonElement list) => [.. list.EnumerateArray().Select(n => n.GetDouble())];

    private static double Dot(double[] a, double[] b) => (a[0] * b[0]) + (a[1] * b[1]) + (a[2] * b[2]);

    private static double Length(double[] v) => Math.Sqrt(Dot(v, v));

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

    private sealed record Expected(double Relief, int Width, int Height, int Fps, double[][] Points, double[][] Frames);

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
        return new Flown(request, expected);
    }

    /// <summary>
    /// The request's camera, look and texts, about two invented caves: the first a box of walls and
    /// a few legs at the origin, the second a few legs where the flyby's second subject stands.
    /// </summary>
    private static FlybyBundleScene Scene(FlybyBlenderBundleRequest request)
    {
        var origin = request.Origin!;
        var box = new IndexedMesh(
            [-20, -8, -30, 20, -8, -30, 20, 8, -30, -20, 8, -30, -20, -8, 0, 20, -8, 0, 20, 8, 0, -20, 8, 0],
            [0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6, 0, 4, 5, 0, 5, 1, 1, 5, 6, 1, 6, 2, 2, 6, 7, 2, 7, 3, 3, 7, 4, 3, 4, 0]);
        var first = new FlybyBundleCave(
            new FlybyBundleMesh(box, origin.Longitude, origin.Latitude, origin.HeightM + 60),
            null,
            null,
            [
                new FlybyBundleLeg(origin.Longitude - 0.0006, origin.Latitude + 0.0003, 710, origin.Longitude, origin.Latitude, 640),
                new FlybyBundleLeg(origin.Longitude, origin.Latitude, 640, origin.Longitude + 0.0009, origin.Latitude - 0.0005, 570),
            ],
            AltitudeShiftM: 0);
        var second = new FlybyBundleCave(
            null,
            null,
            null,
            [
                new FlybyBundleLeg(9.7648, 44.3592, 700, 9.7655, 44.3588, 590),
                new FlybyBundleLeg(9.7655, 44.3588, 590, 9.7667, 44.3581, 480),
            ],
            AltitudeShiftM: 0);

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
            []);
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
        var summary = FlybyBlenderBundle.Write(scene, Path.Combine(root, "work"), zip);
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
