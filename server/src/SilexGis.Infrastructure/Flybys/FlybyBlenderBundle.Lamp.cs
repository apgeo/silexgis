// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Surveys;
using Therion.Blender.Plan;

namespace SilexGis.Infrastructure.Flybys;

// The lamp on the camera, where the daylight goes.
//
// A bundle's sun and sky go out while its camera is under the ground. What is left is the survey
// library's lamp on the camera, which lights a wall three metres away to the middle of what a
// picture shows and one thirty metres away hardly at all — and a flyby keeps its camera tens to
// hundreds of metres from a cave. So the lamp is turned up by the square of the distance to what
// it has to light, which the survey library does from distances it is given: here, at each frame,
// how far the camera is from the nearest point of anything a light shows.
//
// - What a light shows are the walls and the passages drawn at a size nobody measured. Survey
//   lines are drawn in their own colours whatever the light, markers and photographs glow, so none
//   of them sets the lamp: a camera beside the lines of one cave, looking at the walls of another,
//   has a lamp for the walls.
// - The nearest point, not what the camera looks at: the distance to the nearest point changes
//   no faster than the camera moves, so the lamp never jumps; the distance to whatever is in the
//   middle of the picture jumps whenever something comes in at its edge.
// - Only as far as the daylight has gone. In whole daylight the lamp is as the library builds it,
//   and the picture is the one a bundle has always had; as the sun goes, the lamp takes over, by
//   the share of the daylight that is gone. A lamp turned up under the sun would light a cave a
//   second time, flatly, from where the camera is.
//
// Everything this is worked out from is in the bundle already: where the camera of each frame is,
// the meshes and where they stand and how they are stretched, and how strong the sun is. The
// lamp's keys say nothing those do not.
public static partial class FlybyBlenderBundle
{
    /// <summary>
    /// How far from the camera the lamp lights as it is built, in metres — the survey library's
    /// own distance. Nearer than this the lamp is never turned down, and what is this far away in
    /// the dark is lit to the middle of what a picture shows however far it really is.
    /// </summary>
    public const double LampDistanceM = LampReach.DefaultDistance;

    /// <summary>
    /// The distance the lamp is to light at moments of the clip, for the survey library to key its
    /// energy by: in whole daylight the lamp's own, in the dark the distance from the camera to the
    /// nearest lit point, and between the two by the share of the daylight that is gone — in the
    /// lamp's energy, which goes by the square of the distance. Null where the lamp stays as it is
    /// built: no frame is short of daylight, or there is nothing for a lamp to light.
    /// </summary>
    /// <param name="lit">The models a light shows, each with where it stands.</param>
    /// <param name="daylight">How much of the daylight each frame has, from 0 to 1.</param>
    private static List<(double T, double Distance)>? LampDistances(
        IReadOnlyList<(Standing Stand, IndexedMesh Mesh)> lit,
        IReadOnlyList<SceneView> views,
        double[] reliefs,
        Layout layout,
        double[] daylight,
        Func<int, double> seconds)
    {
        var frames = views.Count;
        var dark = daylight.Count(share => share < 1);
        var walls = lit.Where(each => each.Mesh.TriangleCount > 0).Select(each => new LitMesh(each.Stand, each.Mesh)).ToList();
        if (dark == 0 || walls.Count == 0)
        {
            return null;
        }

        // Every frame short of daylight, or every few of them where there is too much to measure.
        var triangles = walls.Sum(wall => (long)wall.Triangles);
        var every = (int)Math.Max(1, Math.Ceiling(triangles * (double)dark / MostLampWork));

        bool Whole(int frame) => frame < 0 || frame >= frames || daylight[frame] >= 1;

        var distances = new List<(double T, double Distance)>();
        var passed = 0;
        for (var i = 0; i < frames; i++)
        {
            var ends = i == 0 || i == frames - 1;
            if (Whole(i))
            {
                // A stretch in whole daylight is said by its two ends: the lamp as it is built.
                passed = 0;
                if (ends || !Whole(i - 1) || !Whole(i + 1))
                {
                    distances.Add((seconds(i), LampDistanceM));
                }

                continue;
            }

            if (!ends && !Whole(i - 1) && !Whole(i + 1) && passed++ % every != 0)
            {
                continue;
            }

            var up = layout.Stretch is null ? 1 : reliefs[i];
            var nearest = walls.Min(wall => wall.DistanceFrom(views[i].Eye, up));
            var gone = 1 - Math.Clamp(daylight[i], 0, 1);
            var farther = Math.Max((nearest * nearest) - (LampDistanceM * LampDistanceM), 0) * gone;
            distances.Add((seconds(i), Rounded(Math.Sqrt((LampDistanceM * LampDistanceM) + farther))));
        }

        return distances;
    }

    /// <summary>
    /// A model a light shows, for asking how far a point is from the nearest of its triangles
    /// while the model is stretched along its own up.
    /// </summary>
    private sealed class LitMesh
    {
        private readonly Standing stand;

        /// <summary>Each vertex in the model's own metres, raised by what its mesh is raised by.</summary>
        private readonly double[] positions;
        private readonly int[] indices;

        /// <summary>The middle of each triangle, and how far its farthest corner is from it.</summary>
        private readonly double[] middles;
        private readonly double[] radii;

        /// <summary>The triangle that was nearest the last time it was asked: a camera moves a little a frame.</summary>
        private int nearest;

        public LitMesh(Standing stand, IndexedMesh mesh)
        {
            this.stand = stand;
            indices = mesh.Indices;
            positions = (double[])mesh.Positions.Clone();
            for (var v = 2; v < positions.Length; v += 3)
            {
                positions[v] += stand.LiftM;
            }

            middles = new double[indices.Length];
            radii = new double[indices.Length / 3];
            for (var t = 0; t < radii.Length; t++)
            {
                var (a, b, c) = (indices[t * 3] * 3, indices[(t * 3) + 1] * 3, indices[(t * 3) + 2] * 3);
                for (var axis = 0; axis < 3; axis++)
                {
                    middles[(t * 3) + axis] = (positions[a + axis] + positions[b + axis] + positions[c + axis]) / 3;
                }

                var farthest = 0.0;
                foreach (var corner in (ReadOnlySpan<int>)[a, b, c])
                {
                    var dx = positions[corner] - middles[t * 3];
                    var dy = positions[corner + 1] - middles[(t * 3) + 1];
                    var dz = positions[corner + 2] - middles[(t * 3) + 2];
                    farthest = Math.Max(farthest, (dx * dx) + (dy * dy) + (dz * dz));
                }

                radii[t] = Math.Sqrt(farthest);
            }
        }

        public int Triangles => radii.Length;

        /// <summary>
        /// How far a point of the scene is from the nearest point of the model, when the model is
        /// <paramref name="up"/> times as tall as its mesh.
        /// </summary>
        public double DistanceFrom(SceneVector eye, double up)
        {
            // The point in the model's own metres: its place taken off, its turn undone.
            var own = Turned(eye - stand.Translation, stand.Axis, -stand.Degrees);

            // A stretch along one axis makes no distance within the mesh longer than this many times.
            var longer = Math.Max(1, up);
            var best = Math.Sqrt(Squared(nearest, own, up));
            for (var t = 0; t < radii.Length; t++)
            {
                // No point of a triangle is farther from its middle than its farthest corner: one
                // whose middle is farther than the nearest so far by more than that is not nearer.
                var dx = own.X - middles[t * 3];
                var dy = own.Y - middles[(t * 3) + 1];
                var dz = own.Z - (middles[(t * 3) + 2] * up);
                var within = best + (radii[t] * longer);
                if ((dx * dx) + (dy * dy) + (dz * dz) >= within * within)
                {
                    continue;
                }

                var squared = Squared(t, own, up);
                if (squared < best * best)
                {
                    best = Math.Sqrt(squared);
                    nearest = t;
                }
            }

            return best;
        }

        /// <summary>The square of the distance from a point to the nearest point of one triangle.</summary>
        private double Squared(int triangle, SceneVector p, double up)
        {
            var a = Corner(indices[triangle * 3], up);
            var b = Corner(indices[(triangle * 3) + 1], up);
            var c = Corner(indices[(triangle * 3) + 2], up);

            // Which part of the triangle the point is nearest to — a corner, an edge or the face —
            // by where it lies against the edges from the first corner.
            var (ab, ac, ap) = (b - a, c - a, p - a);
            var (d1, d2) = (ab.Dot(ap), ac.Dot(ap));
            SceneVector closest;
            if (d1 <= 0 && d2 <= 0)
            {
                closest = a;
            }
            else
            {
                var bp = p - b;
                var (d3, d4) = (ab.Dot(bp), ac.Dot(bp));
                var cp = p - c;
                var (d5, d6) = (ab.Dot(cp), ac.Dot(cp));
                var (va, vb, vc) = ((d3 * d6) - (d5 * d4), (d5 * d2) - (d1 * d6), (d1 * d4) - (d3 * d2));
                if (d3 >= 0 && d4 <= d3)
                {
                    closest = b;
                }
                else if (vc <= 0 && d1 >= 0 && d3 <= 0)
                {
                    closest = a + (ab * (d1 / (d1 - d3)));
                }
                else if (d6 >= 0 && d5 <= d6)
                {
                    closest = c;
                }
                else if (vb <= 0 && d2 >= 0 && d6 <= 0)
                {
                    closest = a + (ac * (d2 / (d2 - d6)));
                }
                else if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
                {
                    closest = b + ((c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))));
                }
                else
                {
                    var whole = va + vb + vc;
                    closest = a + (ab * (vb / whole)) + (ac * (vc / whole));
                }
            }

            var away = p - closest;
            var squared = away.Dot(away);
            if (double.IsFinite(squared))
            {
                return squared;
            }

            // A triangle with no area has no face to be nearest to: the nearest of its corners.
            return Math.Min((p - a).Dot(p - a), Math.Min((p - b).Dot(p - b), (p - c).Dot(p - c)));
        }

        private SceneVector Corner(int vertex, double up) =>
            new(positions[vertex * 3], positions[(vertex * 3) + 1], positions[(vertex * 3) + 2] * up);
    }
}
