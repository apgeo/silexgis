// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// Answers whether a mesh's surface passes within a given distance of a point: which passages of a
/// survey its drawn walls already enclose.
///
/// <para>
/// Measured against the triangles, not their corners. A long straight passage is drawn with few
/// corners and triangles tens of metres long, and the middle of a leg running down it can be far
/// from every corner while sitting a metre from the wall.
/// </para>
/// </summary>
public sealed class WallCoverage
{
    /// <summary>
    /// Edge of one cell of the index. Near the reach a query is asked with, so a query reads a few
    /// cells; and large enough that a long triangle is filed in a modest number of them.
    /// </summary>
    private const double CellM = 8;

    private readonly double[] positions;
    private readonly int[] indices;
    private readonly Dictionary<(int, int, int), List<int>> cells = [];
    private readonly int[] visitedAt;
    private int query;

    public WallCoverage(IndexedMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        positions = mesh.Positions;
        indices = mesh.Indices;
        visitedAt = new int[mesh.TriangleCount];

        for (var t = 0; t < mesh.TriangleCount; t++)
        {
            var (min, max) = Bounds(t);
            for (var x = Cell(min.East); x <= Cell(max.East); x++)
            {
                for (var y = Cell(min.North); y <= Cell(max.North); y++)
                {
                    for (var z = Cell(min.Up); z <= Cell(max.Up); z++)
                    {
                        if (!cells.TryGetValue((x, y, z), out var list))
                        {
                            cells[(x, y, z)] = list = [];
                        }

                        list.Add(t);
                    }
                }
            }
        }
    }

    /// <summary>Whether any triangle comes within <paramref name="reachM"/> of the point.</summary>
    public bool Within(LocalPoint point, double reachM)
    {
        query++;
        var limit = reachM * reachM;
        for (var x = Cell(point.East - reachM); x <= Cell(point.East + reachM); x++)
        {
            for (var y = Cell(point.North - reachM); y <= Cell(point.North + reachM); y++)
            {
                for (var z = Cell(point.Up - reachM); z <= Cell(point.Up + reachM); z++)
                {
                    if (!cells.TryGetValue((x, y, z), out var list))
                    {
                        continue;
                    }

                    foreach (var t in list)
                    {
                        if (visitedAt[t] == query)
                        {
                            continue;
                        }

                        visitedAt[t] = query;
                        if (SquaredDistance(point, t) <= limit)
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private static int Cell(double value) => (int)Math.Floor(value / CellM);

    private (LocalPoint Min, LocalPoint Max) Bounds(int triangle)
    {
        var a = Corner(triangle, 0);
        var b = Corner(triangle, 1);
        var c = Corner(triangle, 2);
        return (
            new LocalPoint(Math.Min(a.East, Math.Min(b.East, c.East)), Math.Min(a.North, Math.Min(b.North, c.North)), Math.Min(a.Up, Math.Min(b.Up, c.Up))),
            new LocalPoint(Math.Max(a.East, Math.Max(b.East, c.East)), Math.Max(a.North, Math.Max(b.North, c.North)), Math.Max(a.Up, Math.Max(b.Up, c.Up))));
    }

    private LocalPoint Corner(int triangle, int corner)
    {
        var i = indices[(triangle * 3) + corner] * 3;
        return new LocalPoint(positions[i], positions[i + 1], positions[i + 2]);
    }

    /// <summary>
    /// Squared distance from a point to the nearest point of a triangle: the closest-point test by
    /// regions of the triangle's plane, as given in Ericson, <i>Real-Time Collision Detection</i>.
    /// </summary>
    private double SquaredDistance(LocalPoint p, int triangle)
    {
        var a = Corner(triangle, 0);
        var b = Corner(triangle, 1);
        var c = Corner(triangle, 2);

        var ab = Sub(b, a);
        var ac = Sub(c, a);
        var ap = Sub(p, a);
        var d1 = Dot(ab, ap);
        var d2 = Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0)
        {
            return Squared(ap);
        }

        var bp = Sub(p, b);
        var d3 = Dot(ab, bp);
        var d4 = Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3)
        {
            return Squared(bp);
        }

        var vc = (d1 * d4) - (d3 * d2);
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        {
            var v = d1 / (d1 - d3);
            return Squared(Sub(p, Add(a, Scale(ab, v))));
        }

        var cp = Sub(p, c);
        var d5 = Dot(ab, cp);
        var d6 = Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6)
        {
            return Squared(cp);
        }

        var vb = (d5 * d2) - (d1 * d6);
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        {
            var w = d2 / (d2 - d6);
            return Squared(Sub(p, Add(a, Scale(ac, w))));
        }

        var va = (d3 * d6) - (d5 * d4);
        if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
        {
            var w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return Squared(Sub(p, Add(b, Scale(Sub(c, b), w))));
        }

        var denominator = 1 / (va + vb + vc);
        var vv = vb * denominator;
        var ww = vc * denominator;
        if (!double.IsFinite(vv) || !double.IsFinite(ww))
        {
            // A triangle with no area: as far as the nearest of its corners.
            return Math.Min(Squared(ap), Math.Min(Squared(bp), Squared(cp)));
        }

        return Squared(Sub(p, Add(a, Add(Scale(ab, vv), Scale(ac, ww)))));
    }

    private static LocalPoint Sub(LocalPoint x, LocalPoint y) => new(x.East - y.East, x.North - y.North, x.Up - y.Up);

    private static LocalPoint Add(LocalPoint x, LocalPoint y) => new(x.East + y.East, x.North + y.North, x.Up + y.Up);

    private static LocalPoint Scale(LocalPoint x, double s) => new(x.East * s, x.North * s, x.Up * s);

    private static double Dot(LocalPoint x, LocalPoint y) => (x.East * y.East) + (x.North * y.North) + (x.Up * y.Up);

    private static double Squared(LocalPoint x) => Dot(x, x);
}
