// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Infrastructure.Flybys;

/// <summary>
/// Survey legs as a mesh: a thin four-sided tube along each leg.
///
/// <para>
/// A renderer that draws surfaces has no line to draw a survey with, so each leg is given just
/// enough body to be seen — a square tube of one small size, the same for every leg, open at both
/// ends. It says where a leg runs and nothing about how wide the passage is: these are lines, and
/// are named and coloured as lines wherever they are used, never as walls.
/// </para>
/// </summary>
public static class SurveyLineTubes
{
    /// <summary>A leg shorter than this, in metres, has no direction to build a tube along.</summary>
    private const double ShortestLegM = 1e-6;

    /// <param name="legs">Each leg's two ends, in metres east, north and up of one point.</param>
    /// <param name="radiusM">From a leg to each corner of its tube.</param>
    public static IndexedMesh Build(
        IEnumerable<((double East, double North, double Up) From, (double East, double North, double Up) To)> legs,
        double radiusM) => Build(legs, radiusM, out _);

    /// <param name="legs">Each leg's two ends, in metres east, north and up of one point.</param>
    /// <param name="radiusM">From a leg to each corner of its tube.</param>
    /// <param name="legOfVertex">
    /// For each vertex of the mesh, which of <paramref name="legs"/> it belongs to, counted from 0
    /// — for whoever colours the legs one by one. A leg too short to build is counted and has no vertex.
    /// </param>
    public static IndexedMesh Build(
        IEnumerable<((double East, double North, double Up) From, (double East, double North, double Up) To)> legs,
        double radiusM,
        out int[] legOfVertex)
    {
        var positions = new List<double>();
        var indices = new List<int>();
        var owners = new List<int>();
        var leg = -1;

        foreach (var (a, b) in legs)
        {
            leg++;
            var dx = b.East - a.East;
            var dy = b.North - a.North;
            var dz = b.Up - a.Up;
            var length = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            if (!(length > ShortestLegM) || !double.IsFinite(length))
            {
                continue;
            }

            dx /= length;
            dy /= length;
            dz /= length;

            // Two directions across the leg. The first is level wherever the leg is not plumb, so a
            // tube along a level passage stands on a corner the same way all along it.
            double ux, uy, uz;
            if (Math.Abs(dz) > 0.99)
            {
                (ux, uy, uz) = (1, 0, 0);
            }
            else
            {
                var level = Math.Sqrt((dx * dx) + (dy * dy));
                (ux, uy, uz) = (dy / level, -dx / level, 0);
            }

            var vx = (dy * uz) - (dz * uy);
            var vy = (dz * ux) - (dx * uz);
            var vz = (dx * uy) - (dy * ux);

            var first = positions.Count / 3;
            foreach (var end in (ReadOnlySpan<(double East, double North, double Up)>)[a, b])
            {
                foreach (var (su, sv) in (ReadOnlySpan<(double, double)>)[(1, 0), (0, 1), (-1, 0), (0, -1)])
                {
                    positions.Add(end.East + (radiusM * ((su * ux) + (sv * vx))));
                    positions.Add(end.North + (radiusM * ((su * uy) + (sv * vy))));
                    positions.Add(end.Up + (radiusM * ((su * uz) + (sv * vz))));
                    owners.Add(leg);
                }
            }

            for (var side = 0; side < 4; side++)
            {
                var next = (side + 1) % 4;
                indices.AddRange([first + side, first + next, first + 4 + next]);
                indices.AddRange([first + side, first + 4 + next, first + 4 + side]);
            }
        }

        legOfVertex = [.. owners];
        return new IndexedMesh([.. positions], [.. indices]);
    }
}
