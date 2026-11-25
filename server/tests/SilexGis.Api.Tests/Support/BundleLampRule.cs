// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// The rule a bundle's lamp is turned up by, restated from an unpacked bundle's own files.
///
/// <para>
/// At each frame: how far the camera of the plan is from the nearest point of the meshes a light
/// shows — the models of walls and of estimated passages, as the plan places and stretches them —
/// and how much of the daylight is gone, which is how far the plan's sun is from its own strength.
/// The lamp has its own energy, times the square of that distance over three metres where the
/// daylight is gone, and between the two by the share of the daylight that is gone.
/// </para>
///
/// <para>
/// Nothing here is the exporter's: the geometry is worked out another way, and everything is read
/// from the folder. That is also the proof that the lamp's keys say nothing the camera, the meshes
/// and the sun of the same bundle do not already say.
/// </para>
/// </summary>
internal static class BundleLampRule
{
    /// <summary>How far the lamp lights as it is built, in metres.</summary>
    public const double Metres = 3;

    /// <summary>The roles of the models a light shows.</summary>
    private static readonly string[] Lit = ["walls", "estimated-passages"];

    /// <param name="Nearest">How far the camera is from the nearest lit point; infinite where nothing is lit.</param>
    /// <param name="Gone">The share of the daylight that is gone, 0 to 1.</param>
    /// <param name="Energy">What the rule gives the lamp.</param>
    public sealed record Frame(double Nearest, double Gone, double Energy);

    /// <summary>The lamp of the plan as it is built: its energy.</summary>
    public static double Built(JsonElement plan) =>
        plan.GetProperty("look").GetProperty("lights").EnumerateArray()
            .Single(light => light.GetProperty("kind").GetString() == "headlamp").GetProperty("energy").GetDouble();

    /// <summary>What the rule gives at every frame of the bundle unpacked in <paramref name="folder"/>.</summary>
    public static List<Frame> Frames(string folder)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "scene-plan.json")));
        var plan = document.RootElement;
        var fps = plan.GetProperty("timeline").GetProperty("fps").GetInt32();
        var count = plan.GetProperty("timeline").GetProperty("frameCount").GetInt32();
        var changes = plan.TryGetProperty("changes", out var listed) ? listed.EnumerateArray().ToList() : [];
        JsonElement? Keys(string id, string property) => changes
            .Where(c => c.TryGetProperty("target", out var target) && target.TryGetProperty("id", out var named) && named.GetString() == id
                        && c.TryGetProperty(property, out _))
            .Select(c => (JsonElement?)c.GetProperty(property))
            .SingleOrDefault();

        var lights = plan.GetProperty("look").GetProperty("lights").EnumerateArray().ToList();
        var sun = lights.Single(light => light.GetProperty("kind").GetString() == "sun");
        var whole = sun.GetProperty("energy").GetDouble();
        var sunKeys = Keys(sun.GetProperty("id").GetString()!, "energy");
        var built = Built(plan);

        var models = plan.GetProperty("models").EnumerateArray()
            .Where(model => Lit.Contains(model.GetProperty("role").GetString()))
            .Select(model =>
            {
                var file = File.ReadAllBytes(Path.Combine(folder, model.GetProperty("path").GetString()!));
                return (Model: model, BundlePly.Read(file).Vertices, Triangles: BundlePly.Triangles(file), Scale: Keys(model.GetProperty("id").GetString()!, "scale"));
            })
            .ToList();
        var track = plan.GetProperty("camera").GetProperty("track").EnumerateArray().ToList();

        var frames = new List<Frame>(count);
        for (var i = 0; i < count; i++)
        {
            var eye = Numbers(track[i].GetProperty("eye"));
            var nearest = double.PositiveInfinity;
            foreach (var (model, vertices, triangles, scaleKeys) in models)
            {
                var scale = scaleKeys is { } keyed
                    ? Played(keyed, i, fps)
                    : model.TryGetProperty("scale", out var still) ? Numbers(still) : [1, 1, 1];
                var placed = vertices.Select(vertex => Placed(model, vertex, scale)).ToList();
                foreach (var triangle in triangles)
                {
                    nearest = Math.Min(nearest, ToTriangle(eye, placed[triangle[0]], placed[triangle[1]], placed[triangle[2]]));
                }
            }

            var gone = sunKeys is { } going ? 1 - (Played(going, i, fps)[0] / whole) : 0;
            var energy = nearest > Metres && double.IsFinite(nearest)
                ? built * (1 + ((((nearest / Metres) * (nearest / Metres)) - 1) * gone))
                : built;
            frames.Add(new Frame(nearest, gone, energy));
        }

        return frames;
    }

    /// <summary>The lamp's energy at every frame as the plan's own keys give it; its built energy where it has none.</summary>
    public static double[] Said(string folder)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "scene-plan.json")));
        var plan = document.RootElement;
        var fps = plan.GetProperty("timeline").GetProperty("fps").GetInt32();
        var count = plan.GetProperty("timeline").GetProperty("frameCount").GetInt32();
        var keys = (plan.TryGetProperty("changes", out var listed) ? listed.EnumerateArray().ToList() : [])
            .Where(c => c.TryGetProperty("target", out var target) && target.TryGetProperty("id", out var named) && named.GetString() == "Headlamp")
            .Select(c => (JsonElement?)c.GetProperty("energy"))
            .SingleOrDefault();
        var built = Built(plan);
        return [.. Enumerable.Range(0, count).Select(i => keys is { } keyed ? Played(keyed, i, fps)[0] : built)];
    }

    /// <summary>
    /// The value of keys at a frame, as a renderer plays them: held before the first and after the
    /// last, between two moved as the later says; of the keys on one frame the second is what the
    /// frame shows and the last where the way on starts.
    /// </summary>
    private static double[] Played(JsonElement keys, int frame, int fps)
    {
        var list = keys.EnumerateArray()
            .Select(key => (
                Frame: (int)Math.Round(key.GetProperty("t").GetDouble() * fps),
                Value: key.GetProperty("value").ValueKind == JsonValueKind.Array ? Numbers(key.GetProperty("value")) : [key.GetProperty("value").GetDouble()],
                Easing: key.TryGetProperty("easing", out var easing) ? easing.GetString() : "linear"))
            .ToList();
        var on = list.Where(key => key.Frame == frame).ToList();
        if (on.Count > 0)
        {
            return on[Math.Min(1, on.Count - 1)].Value;
        }

        var before = list.LastOrDefault(key => key.Frame < frame);
        var after = list.FirstOrDefault(key => key.Frame > frame);
        if (before.Value is null)
        {
            return list[0].Value;
        }

        if (after.Value is null || after.Easing == "constant")
        {
            return before.Value;
        }

        var share = (frame - before.Frame) / (double)(after.Frame - before.Frame);
        var eased = after.Easing == "smooth" ? share * share * (3 - (2 * share)) : share;
        return [.. before.Value.Zip(after.Value, (from, to) => from + ((to - from) * eased))];
    }

    /// <summary>A vertex of a model where the plan puts it: stretched, turned, moved.</summary>
    private static double[] Placed(JsonElement model, double[] own, double[] scale)
    {
        double[] at = [own[0] * scale[0], own[1] * scale[1], own[2] * scale[2]];
        if (model.TryGetProperty("rotation", out var rotation))
        {
            var axis = Numbers(rotation.GetProperty("axis"));
            var length = Math.Sqrt(Dot(axis, axis));
            axis = [axis[0] / length, axis[1] / length, axis[2] / length];
            var angle = rotation.GetProperty("degrees").GetDouble() * Math.PI / 180;
            var (sin, cos) = Math.SinCos(angle);
            double[] cross = [(axis[1] * at[2]) - (axis[2] * at[1]), (axis[2] * at[0]) - (axis[0] * at[2]), (axis[0] * at[1]) - (axis[1] * at[0])];
            var along = Dot(axis, at) * (1 - cos);
            at = [(at[0] * cos) + (cross[0] * sin) + (axis[0] * along), (at[1] * cos) + (cross[1] * sin) + (axis[1] * along), (at[2] * cos) + (cross[2] * sin) + (axis[2] * along)];
        }

        var moved = model.TryGetProperty("translation", out var translation) ? Numbers(translation) : [0, 0, 0];
        return [at[0] + moved[0], at[1] + moved[1], at[2] + moved[2]];
    }

    /// <summary>
    /// How far a point is from the nearest point of a triangle: from its foot on the triangle's
    /// plane where that lies inside the triangle, else from the nearest of the three edges.
    /// </summary>
    internal static double ToTriangle(double[] p, double[] a, double[] b, double[] c)
    {
        double[] ab = Minus(b, a), ac = Minus(c, a), ap = Minus(p, a);
        double[] normal = [(ab[1] * ac[2]) - (ab[2] * ac[1]), (ab[2] * ac[0]) - (ab[0] * ac[2]), (ab[0] * ac[1]) - (ab[1] * ac[0])];
        var area = Dot(normal, normal);
        if (area > 1e-18)
        {
            // The foot's place in the triangle, as shares of its two edges from the first corner.
            double d00 = Dot(ab, ab), d01 = Dot(ab, ac), d11 = Dot(ac, ac), d20 = Dot(ap, ab), d21 = Dot(ap, ac);
            var whole = (d00 * d11) - (d01 * d01);
            var v = ((d11 * d20) - (d01 * d21)) / whole;
            var w = ((d00 * d21) - (d01 * d20)) / whole;
            if (v >= 0 && w >= 0 && v + w <= 1)
            {
                return Math.Abs(Dot(ap, normal)) / Math.Sqrt(area);
            }
        }

        return Math.Min(ToSegment(p, a, b), Math.Min(ToSegment(p, b, c), ToSegment(p, c, a)));
    }

    private static double ToSegment(double[] p, double[] a, double[] b)
    {
        double[] ab = Minus(b, a), ap = Minus(p, a);
        var length = Dot(ab, ab);
        var share = length > 0 ? Math.Clamp(Dot(ap, ab) / length, 0, 1) : 0;
        double[] away = [ap[0] - (ab[0] * share), ap[1] - (ab[1] * share), ap[2] - (ab[2] * share)];
        return Math.Sqrt(Dot(away, away));
    }

    private static double[] Minus(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];

    private static double Dot(double[] a, double[] b) => (a[0] * b[0]) + (a[1] * b[1]) + (a[2] * b[2]);

    private static double[] Numbers(JsonElement list) => [.. list.EnumerateArray().Select(n => n.GetDouble())];
}
