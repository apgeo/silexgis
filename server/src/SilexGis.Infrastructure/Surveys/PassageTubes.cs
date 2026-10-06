// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Geo;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>A point in metres about a survey model's anchor: east of it, north of it and above it.</summary>
public readonly record struct LocalPoint(double East, double North, double Up);

/// <summary>
/// How far the passage walls stand from one station — to the left and right of the direction of
/// travel, and above and below — in metres, with nothing where the surveyor measured nothing.
/// </summary>
public readonly record struct WallDistances(double? Left, double? Right, double? Up, double? Down)
{
    /// <summary>
    /// The four numbers a survey file states at a station, read the way every other reader of them
    /// here reads them: a negative or non-finite number is the file saying "not measured". Null
    /// when none of the four was measured, because a station nobody measured the walls at has no
    /// cross-section, and a zero-sized one would be a claim that the passage closes there.
    /// </summary>
    public static WallDistances? Read(double left, double right, double up, double down)
    {
        var walls = new WallDistances(
            SurveyDimensions.Measured(left),
            SurveyDimensions.Measured(right),
            SurveyDimensions.Measured(up),
            SurveyDimensions.Measured(down));

        return SurveyDimensions.AnyMeasured(walls.Left, walls.Right, walls.Up, walls.Down) ? walls : null;
    }
}

/// <summary>One measured cross-section of a passage: where the station is, and its walls.</summary>
public readonly record struct PassageSection(LocalPoint Station, WallDistances Walls);

/// <summary>
/// Collects triangles into one mesh whose vertices are shared.
///
/// <para>
/// Every corner is looked up by its exact position, so two corners at one point are one vertex. That
/// is what lets a triangle with no surface be recognised by its indices alone — a cross-section
/// measured as zero on one side folds two of its corners onto the station, and the triangles
/// spanning that fold are dropped here rather than written, counted and downloaded. Exact equality
/// and not a tolerance, for the reason the wall-mesh reader gives: a tolerance merges points that
/// were deliberately recorded a few millimetres apart, and makes the result depend on arrival order.
/// </para>
/// </summary>
public sealed class WallMeshAssembler
{
    private readonly Dictionary<LocalPoint, int> seen = [];
    private readonly List<double> positions = [];
    private readonly List<int> indices = [];

    /// <summary>Triangles kept so far.</summary>
    public int TriangleCount => indices.Count / 3;

    /// <summary>
    /// The index of the vertex at <paramref name="point"/>, or -1 for a point that is not a position
    /// at all. A corrupt file can state a coordinate that is not a finite number, and one such
    /// vertex written into a mesh poisons its bounds and blanks the whole model in a viewer.
    /// </summary>
    public int Vertex(LocalPoint point)
    {
        if (!double.IsFinite(point.East) || !double.IsFinite(point.North) || !double.IsFinite(point.Up))
        {
            return -1;
        }

        if (seen.TryGetValue(point, out var known))
        {
            return known;
        }

        var index = positions.Count / 3;
        positions.Add(point.East);
        positions.Add(point.North);
        positions.Add(point.Up);
        seen[point] = index;
        return index;
    }

    /// <summary>
    /// Adds one triangle, unless its corners are not three distinct points — in which case it has
    /// no surface, draws nothing, and is left out.
    /// </summary>
    public void Triangle(int a, int b, int c)
    {
        if (a < 0 || b < 0 || c < 0 || a == b || b == c || a == c)
        {
            return;
        }

        indices.Add(a);
        indices.Add(b);
        indices.Add(c);
    }

    /// <summary>
    /// The mesh, holding only the vertices some kept triangle uses; null when no triangle was kept.
    /// </summary>
    public IndexedMesh? ToMesh()
    {
        if (indices.Count == 0)
        {
            return null;
        }

        var remap = new int[positions.Count / 3];
        Array.Fill(remap, -1);

        var kept = new List<double>(positions.Count);
        var compacted = new int[indices.Count];
        for (var i = 0; i < indices.Count; i++)
        {
            var old = indices[i];
            if (remap[old] < 0)
            {
                remap[old] = kept.Count / 3;
                kept.Add(positions[(old * 3) + 0]);
                kept.Add(positions[(old * 3) + 1]);
                kept.Add(positions[(old * 3) + 2]);
            }

            compacted[i] = remap[old];
        }

        return new IndexedMesh(kept.ToArray(), compacted);
    }
}

/// <summary>
/// Walls around a surveyed passage, built from nothing but the distances measured at its stations.
///
/// <para>
/// Each measured station becomes a ring through its four measured points — the left and right wall
/// laid out level, across the direction of travel, and the roof and floor plumb above and below the
/// station, which is how those four distances are taken underground. Consecutive rings are joined
/// by flat faces and the two ends are closed. The corners between the four measured points lie on
/// the ellipse through them: the measurements fix four points of the cross-section and say nothing
/// of its corners, and an ellipse is the shape that adds the least.
/// </para>
///
/// <para>
/// <b>Nothing is invented.</b> A distance that was not measured is drawn as no distance at all, so
/// the ring stops at the station on that side instead of standing off it by a made-up amount; and
/// this is only ever handed stations that were measured, so a leg nobody took the walls of gets no
/// tube. A uniform tube of a plausible size around an unmeasured leg is indistinguishable on screen
/// from a surveyed passage, which is exactly why it must not be drawn.
/// </para>
/// </summary>
public static class PassageTubes
{
    /// <summary>
    /// Corners in a ring, ordinarily. Four of them are the measured points and four lie between;
    /// more would round the corners further without showing anything the survey knows.
    /// </summary>
    public const int FullSides = 8;

    /// <summary>
    /// Corners in a ring when the cave is too large for <see cref="FullSides"/>: the four measured
    /// points and nothing else. Half the triangles, and still every measurement in its place.
    /// </summary>
    public const int FewerSides = 4;

    /// <summary>A leg shorter than this joins two readings of one place and carries no tube.</summary>
    public const double ShortestLegM = 0.001;

    /// <summary>
    /// How steep a passage has to be, in degrees from level, before its rings are laid across the
    /// passage itself rather than stood upright.
    ///
    /// <para>
    /// An upright ring is right for anything a person walks or crawls along, because up and down
    /// are measured plumb. Down a pitch it is edge-on to the passage, and the tube between two such
    /// rings flattens into a ribbon however wide the shaft was measured. Past this slope the ring
    /// is turned to face along the leg, so the roof and floor distances become the shaft's near and
    /// far walls — the reading a surveyor hanging on a rope gives them.
    /// </para>
    /// </summary>
    public const double SteepLegDegrees = 60.0;

    private static readonly double SteepLegSine = Math.Sin(SteepLegDegrees * Math.PI / 180.0);

    /// <summary>
    /// How many triangles a run of <paramref name="sections"/> cross-sections makes at
    /// <paramref name="sides"/> corners a ring, before any that carry no surface are dropped: two
    /// per side for each stretch between rings, and one per side for each of the two ends.
    /// </summary>
    public static long TriangleCount(int sections, int sides) =>
        sections < 2 ? 0 : 2L * sides * sections;

    /// <summary>
    /// Adds the walls of one unbroken run of measured cross-sections, in the order travelled.
    /// </summary>
    /// <param name="run">
    /// At least two cross-sections, each a real distance from the one before. The caller decides
    /// what a run is — which stations follow which, and where a missing measurement breaks it —
    /// because that is a fact about the survey file and not about geometry.
    /// </param>
    /// <param name="sides">Corners per ring; a multiple of four, so the measured points are corners.</param>
    public static void Append(WallMeshAssembler mesh, IReadOnlyList<PassageSection> run, int sides)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(run);
        if (sides < 4 || sides % 4 != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sides), sides, "A ring needs a corner at each of the four measured points.");
        }

        if (run.Count < 2)
        {
            return;
        }

        var circle = UnitCircle(sides);
        int[]? previous = null;
        for (var i = 0; i < run.Count; i++)
        {
            var ring = Ring(mesh, run[i], DirectionAt(run, i), circle);
            if (previous is null)
            {
                Cap(mesh, run[i].Station, ring, facingAhead: false);
            }
            else
            {
                Join(mesh, previous, ring);
            }

            previous = ring;
        }

        Cap(mesh, run[^1].Station, previous!, facingAhead: true);
    }

    /// <summary>
    /// The corners of a unit ring, with the four that fall on an axis written as exact zeros and
    /// ones. Computed, the cosine of a right angle is a few parts in ten to the seventeenth rather
    /// than nothing, which would stand a wall measured as zero a hair off its station — close enough
    /// to look right and far enough that the fold is no longer recognised as one.
    /// </summary>
    private static (double Across, double Along)[] UnitCircle(int sides)
    {
        var circle = new (double, double)[sides];
        for (var k = 0; k < sides; k++)
        {
            var angle = 2.0 * Math.PI * k / sides;
            circle[k] = (Snap(Math.Cos(angle)), Snap(Math.Sin(angle)));
        }

        return circle;

        static double Snap(double value) => Math.Abs(value) < 1e-12 ? 0.0 : value;
    }

    /// <summary>
    /// The ring at one station, as vertex indices: starting at the right wall and going round by the
    /// roof, the left wall and the floor.
    /// </summary>
    private static int[] Ring(
        WallMeshAssembler mesh, PassageSection section, Vector direction, (double Across, double Along)[] circle)
    {
        var (right, up) = Frame(direction);
        var walls = section.Walls;
        var toRight = walls.Right ?? 0.0;
        var toLeft = walls.Left ?? 0.0;
        var toRoof = walls.Up ?? 0.0;
        var toFloor = walls.Down ?? 0.0;

        var ring = new int[circle.Length];
        for (var k = 0; k < circle.Length; k++)
        {
            var (across, along) = circle[k];
            var sideways = across * (across >= 0 ? toRight : toLeft);
            var upwards = along * (along >= 0 ? toRoof : toFloor);
            ring[k] = mesh.Vertex(new LocalPoint(
                section.Station.East + (right.X * sideways) + (up.X * upwards),
                section.Station.North + (right.Y * sideways) + (up.Y * upwards),
                section.Station.Up + (right.Z * sideways) + (up.Z * upwards)));
        }

        return ring;
    }

    /// <summary>
    /// The two axes a ring is laid out on: which way is "right" at this station, and which way the
    /// roof is.
    ///
    /// <para>
    /// Right is always level, square to the way the passage is heading. A leg that is exactly plumb
    /// is heading nowhere, so its right is taken as east — an arbitrary answer, but the same one
    /// every time the file is read.
    /// </para>
    /// </summary>
    private static (Vector Right, Vector Up) Frame(Vector direction)
    {
        var level = Math.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y));
        var right = level < 1e-9
            ? new Vector(1, 0, 0)
            : new Vector(direction.Y / level, -direction.X / level, 0);

        if (Math.Abs(direction.Z) <= SteepLegSine)
        {
            return (right, new Vector(0, 0, 1));
        }

        return (right, right.Cross(direction).Unit());
    }

    /// <summary>
    /// Which way the passage is heading at one station of a run: along its one leg at either end,
    /// and midway between the leg arriving and the leg leaving everywhere else, so that a ring at a
    /// bend faces half-way round it and the two stretches it joins meet without a gap or an overlap.
    /// </summary>
    private static Vector DirectionAt(IReadOnlyList<PassageSection> run, int i)
    {
        var arriving = i > 0 ? Between(run[i - 1].Station, run[i].Station).Unit() : default;
        var leaving = i < run.Count - 1 ? Between(run[i].Station, run[i + 1].Station).Unit() : default;

        var both = arriving.Plus(leaving);
        if (both.Length > 1e-6)
        {
            return both.Unit();
        }

        // The passage doubles straight back on itself, so the two legs cancel and there is no
        // "midway" to face. The way out is as good an answer as the way in.
        return i < run.Count - 1 ? leaving : arriving;
    }

    /// <summary>The faces between two rings, wound so that they face out of the passage.</summary>
    private static void Join(WallMeshAssembler mesh, int[] behind, int[] ahead)
    {
        for (var k = 0; k < behind.Length; k++)
        {
            var next = (k + 1) % behind.Length;
            mesh.Triangle(behind[k], ahead[k], ahead[next]);
            mesh.Triangle(behind[k], ahead[next], behind[next]);
        }
    }

    /// <summary>
    /// Closes one end of a run with a fan about the station itself.
    ///
    /// <para>
    /// About the station and not about the middle of the ring: the station is the one point of a
    /// cross-section the survey fixed, and a mesh that contains it can be checked against the
    /// survey it was built from.
    /// </para>
    /// </summary>
    private static void Cap(WallMeshAssembler mesh, LocalPoint station, int[] ring, bool facingAhead)
    {
        var centre = mesh.Vertex(station);
        for (var k = 0; k < ring.Length; k++)
        {
            var next = (k + 1) % ring.Length;
            if (facingAhead)
            {
                mesh.Triangle(centre, ring[next], ring[k]);
            }
            else
            {
                mesh.Triangle(centre, ring[k], ring[next]);
            }
        }
    }

    private static Vector Between(LocalPoint from, LocalPoint to) =>
        new(to.East - from.East, to.North - from.North, to.Up - from.Up);

    /// <summary>A direction or an offset in the mesh's own frame: east, north, up.</summary>
    private readonly record struct Vector(double X, double Y, double Z)
    {
        public double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

        public Vector Plus(Vector other) => new(X + other.X, Y + other.Y, Z + other.Z);

        public Vector Cross(Vector other) => new(
            (Y * other.Z) - (Z * other.Y),
            (Z * other.X) - (X * other.Z),
            (X * other.Y) - (Y * other.X));

        /// <summary>The same direction at length one, or nothing at all for a vector of no length.</summary>
        public Vector Unit()
        {
            var length = Length;
            return length < 1e-12 ? default : new Vector(X / length, Y / length, Z / length);
        }
    }
}
