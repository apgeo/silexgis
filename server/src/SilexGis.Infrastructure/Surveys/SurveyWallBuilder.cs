// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Therion.Blender;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>What a survey file's walls were built from.</summary>
public enum SurveyWallSource
{
    /// <summary>The file gave nothing to build walls from, or too much to draw.</summary>
    None = 0,

    /// <summary>The wall surfaces the survey compiler modelled and wrote into the file.</summary>
    Scraps = 1,

    /// <summary>Tubes around the legs whose passage dimensions were measured.</summary>
    PassageDimensions = 2,

    /// <summary>
    /// The compiler's wall surfaces, and tubes around the measured legs those surfaces do not
    /// reach.
    /// </summary>
    ScrapsAndPassageDimensions = 3,
}

/// <summary>
/// The walls one compiled survey yields, or the reason it yields none.
/// </summary>
/// <param name="Mesh">
/// The walls in metres about the model's anchor — east, north, up — or null when there are none.
/// </param>
/// <param name="Source">What they were built from.</param>
/// <param name="RingSides">Corners in each ring of a tube; zero when the walls are not tubes.</param>
/// <param name="WalledLegs">Stretches of passage that carry a tube.</param>
/// <param name="PassageLegs">
/// Legs of the survey that are passage at all — not shots at the wall, not above ground. Beside
/// <paramref name="WalledLegs"/> this says how much of the cave the walls cover, which is the
/// first thing anybody looking at a partly-walled model wants to know.
/// </param>
/// <param name="Reason">Why there is no mesh, in words for a log; null when there is one.</param>
public sealed record SurveyWalls(
    IndexedMesh? Mesh,
    SurveyWallSource Source,
    int RingSides,
    int WalledLegs,
    int PassageLegs,
    string? Reason)
{
    /// <summary>
    /// Tubes of a size nobody measured, around the passages the file gives no size for — only when
    /// they were asked for, and kept apart from <see cref="Mesh"/> so that whatever draws them can
    /// tell them from the walls the survey does state.
    /// </summary>
    public IndexedMesh? Estimated { get; init; }

    /// <summary>How many legs <see cref="Estimated"/> holds tubes for.</summary>
    public int EstimatedLegs { get; init; }

    /// <summary>
    /// The passage the file gives no size for: how many legs and how many metres. Null when that
    /// was not worked out — the file's own wall surfaces were too many to draw at all.
    /// </summary>
    public UnsizedPassages? Unsized { get; init; }
}

/// <summary>Legs of passage that have no size in the file, and their length in metres.</summary>
public sealed record UnsizedPassages(int Legs, double LengthM);

/// <summary>
/// The one size an estimated passage is drawn at, when somebody asked for passages without a
/// measured size to be drawn at all.
/// </summary>
/// <param name="DiameterM">How wide and how high each estimated tube is, in metres.</param>
public sealed record EstimatedPassageSize(double DiameterM);

/// <summary>
/// Builds the walls a compiled survey can honestly be given, out of what the file itself says.
///
/// <para>
/// A line plot is a skeleton, and a cave drawn as one reads as a wire in the dark. Two things in
/// these files say where the rock is. One format can carry the wall surfaces its own compiler
/// modelled, and those are used as they stand: they are the surveyor's drawing turned into
/// triangles by the tool that owns the drawing. Failing that, both formats can carry the distance
/// to each wall measured at a station, and a tube through those measured points is the passage as
/// far as the survey knows it.
/// </para>
///
/// <para>
/// <b>What is not done unasked is the thing that would be easiest: giving every leg a tube.</b> A
/// leg with no measured walls is a line somebody walked along, and nothing in the file says whether
/// it runs down a crawl or across a chamber. A tube of a default size around it looks, on screen,
/// like a measured passage — so by default it is not drawn, a file with nothing measured gets no
/// walls at all, and that outcome is an answer rather than a failure. Somebody who would rather see
/// those passages at a guessed size than not at all can ask for it, and then they are built as a
/// separate mesh, never merged into the walls the survey states, so the viewer can draw them in a
/// colour of their own and turn them off. A shot at the wall gets no tube either way: it is a
/// measurement <i>of</i> the wall, and a passage drawn around it would be a passage through the
/// rock.
/// </para>
///
/// <para>
/// Pure by design, like the reading it follows: a parsed file and its placement in, a mesh out.
/// Where the file sits is not worked out again here. It is handed over by the reading, so the walls
/// and the stations stored beside them are turned onto true north and scaled off the grid by the
/// same placement, and a tube's axis passes through the stored position of its station.
/// </para>
/// </summary>
public static class SurveyWallBuilder
{
    /// <summary>
    /// The most triangles a built mesh may hold.
    ///
    /// <para>
    /// This is the size of the largest wall mesh the 3D scene is already asked to draw: an
    /// uploaded mesh is fifty bytes a triangle and uploads stop at a hundred megabytes, so two
    /// million is what the other path into the same scene tops out at. Built walls are held to it
    /// because the cost is the viewer's and not the server's — some fifty megabytes to fetch and
    /// the same again on the graphics card of whatever opens the cave, which is routinely a phone.
    /// A whole-system export stays far below it: its tens of thousands of legs are nearly all shots
    /// at the wall, and those carry no tube.
    /// </para>
    /// </summary>
    public const int MaxTriangles = 2_000_000;

    /// <summary>Legs that are not passage to put walls around.</summary>
    /// <remarks>
    /// A leg surveyed twice is left out with the other two: the passage it runs along already has
    /// its walls from the first survey of it, and a second tube a few decimetres off the first —
    /// the two surveys never agree exactly — draws one passage as two.
    /// </remarks>
    private const CaveShotFlags NotWalled =
        CaveShotFlags.Splay | CaveShotFlags.Surface | CaveShotFlags.Duplicate | CaveShotFlags.NotLrud;

    /// <summary>Legs that are not passage at all: fired at the wall, or run above ground.</summary>
    private const CaveShotFlags NotPassage = CaveShotFlags.Splay | CaveShotFlags.Surface;

    /// <summary>
    /// The walls of <paramref name="source"/>, placed by <paramref name="placement"/>; and, when
    /// <paramref name="estimate"/> is given, tubes of that size around the passages the file gives
    /// no size for.
    /// </summary>
    public static SurveyWalls Build(CaveModel source, SurveyPlacement placement, EstimatedPassageSize? estimate = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(placement);

        // Settled here as the reading settles it, so the legs this takes for wall shots are the
        // legs every other figure about the survey takes for wall shots. An exporter that flags
        // none of them still fires them at points it declined to name.
        var model = SurveyWallShots.Flagged(source);
        var passageLegs = model.Shots.Count(shot => (shot.Flags & NotPassage) == 0);

        var measured = new List<PassageSection[]>();
        AddMeasuredLegs(model, placement, measured);
        AddMeasuredPassages(model, placement, measured);

        var scraps = FromScraps(model, placement, passageLegs);
        if (scraps is { Mesh: null })
        {
            // Too many drawn surfaces to draw at all. Estimated tubes around the rest would be the
            // only thing on screen, and would read as the whole cave.
            return scraps;
        }

        WallCoverage? coverage = null;
        SurveyWalls walls;
        if (scraps is { Mesh: { } drawn })
        {
            coverage = new WallCoverage(drawn);
            walls = WithUndrawnPassages(scraps, drawn, coverage, measured, passageLegs);
        }
        else
        {
            walls = FromPassageDimensions(measured, passageLegs);
        }

        return WithUnsizedPassages(walls, model, placement, measured, coverage, estimate);
    }

    /// <summary>
    /// How near a drawn wall must pass to the middle of a leg nobody measured for the leg to be
    /// taken for part of the drawn passage. Wider than the allowance a measured leg is given,
    /// because there is no measured width to start from: this is a typical passage's half-width
    /// and the same slack for a drawing that is off its survey.
    /// </summary>
    private const double UnmeasuredDrawnReachM = 5;

    /// <summary>
    /// Counts the passage the file gives no size for — legs that are passage, that no measured
    /// cross-section covers and that no drawn wall comes near — and, when asked to, builds a tube
    /// of the one estimated size around each of them, as a mesh of its own.
    ///
    /// <para>
    /// One straight tube a leg rather than runs joined at their stations: a guessed size gains
    /// nothing from a smooth bend, and per-leg tubes need no decision about which legs continue
    /// which. The estimated tubes are held to what is left of the triangle budget after the walls,
    /// coarser first and then none, because the walls the survey does state are never thinned to
    /// make room for guesses.
    /// </para>
    /// </summary>
    private static SurveyWalls WithUnsizedPassages(
        SurveyWalls walls,
        CaveModel model,
        SurveyPlacement placement,
        List<PassageSection[]> measured,
        WallCoverage? coverage,
        EstimatedPassageSize? estimate)
    {
        // A leg is sized when a cross-section at either end has some width. One measured as
        // nothing but zeros at both ends has a tube with no surface: on screen it is as absent as
        // a leg nobody measured, and some exporters write zeros where nothing was measured at all.
        var sized = new HashSet<(LocalPoint, LocalPoint)>();
        foreach (var run in measured)
        {
            for (var i = 0; i + 1 < run.Length; i++)
            {
                if (Widest(run[i].Walls) <= 0 && Widest(run[i + 1].Walls) <= 0)
                {
                    continue;
                }

                sized.Add((run[i].Station, run[i + 1].Station));
                sized.Add((run[i + 1].Station, run[i].Station));
            }
        }

        var unsized = new List<(LocalPoint From, LocalPoint To)>();
        var lengthM = 0.0;
        foreach (var shot in model.Shots)
        {
            if ((shot.Flags & NotWalled) != 0
                || (shot.ToPosition - shot.FromPosition).Length < PassageTubes.ShortestLegM)
            {
                continue;
            }

            var from = Local(placement, shot.FromPosition);
            var to = Local(placement, shot.ToPosition);
            if (sized.Contains((from, to)))
            {
                continue;
            }

            var middle = new LocalPoint((from.East + to.East) / 2, (from.North + to.North) / 2, (from.Up + to.Up) / 2);
            if (coverage is not null && coverage.Within(middle, UnmeasuredDrawnReachM))
            {
                continue;
            }

            unsized.Add((from, to));
            lengthM += Distance(from, to);
        }

        var counted = walls with { Unsized = new UnsizedPassages(unsized.Count, lengthM) };

        // Walls the file measures but that could not be drawn leave nothing to set guesses beside:
        // the guesses alone would be taken for the cave.
        var measuredButUndrawn = walls.Mesh is null && sized.Count > 0;
        if (estimate is null || unsized.Count == 0 || !(estimate.DiameterM > 0) || measuredButUndrawn)
        {
            return counted;
        }

        var radius = estimate.DiameterM / 2;
        var size = new WallDistances(radius, radius, radius, radius);
        var budget = MaxTriangles - (walls.Mesh?.TriangleCount ?? 0);
        var sides = PassageTubes.FullSides;
        if (PassageTubes.TriangleCount(2, sides) * unsized.Count > budget)
        {
            sides = PassageTubes.FewerSides;
            if (PassageTubes.TriangleCount(2, sides) * unsized.Count > budget)
            {
                return counted;
            }
        }

        var mesh = new WallMeshAssembler();
        foreach (var (from, to) in unsized)
        {
            PassageTubes.Append(mesh, [new PassageSection(from, size), new PassageSection(to, size)], sides);
        }

        return mesh.ToMesh() is { } built
            ? counted with { Estimated = built, EstimatedLegs = unsized.Count }
            : counted;
    }

    private static double Distance(LocalPoint a, LocalPoint b)
    {
        var east = b.East - a.East;
        var north = b.North - a.North;
        var up = b.Up - a.Up;
        return Math.Sqrt((east * east) + (north * north) + (up * up));
    }

    /// <summary>
    /// How much farther than the widest measured wall of a leg the drawn walls may stand from the
    /// middle of it and still be taken for that leg's passage. Drawn walls follow the map, not the
    /// four distances, and a map drawn over a survey is routinely a metre or two off its readings.
    /// </summary>
    private const double DrawnWallSlackM = 3;

    /// <summary>
    /// The file's own wall surfaces, with tubes added around the measured legs they leave out.
    ///
    /// <para>
    /// A compiler writes wall surfaces only for the passages somebody drew, and a survey is rarely
    /// drawn whole: the passages surveyed and never mapped have no surface, and taking the surfaces
    /// alone leaves them out of the model altogether — large stretches of a cave missing from its
    /// own 3D model, with nothing on screen saying why. Where such a leg had its walls measured, the
    /// tube those measurements give is the passage as far as the survey knows it, exactly as it is
    /// for a file with no surfaces at all; where it did not, it stays a line.
    /// </para>
    ///
    /// <para>
    /// A leg counts as drawn when a drawn wall passes within its own widest measurement, plus
    /// <see cref="DrawnWallSlackM"/>, of its middle. A tube is never laid inside a drawn passage
    /// beside the drawing of it, which would show one passage twice.
    /// </para>
    /// </summary>
    private static SurveyWalls WithUndrawnPassages(
        SurveyWalls scraps, IndexedMesh drawn, WallCoverage coverage, List<PassageSection[]> measured, int passageLegs)
    {
        if (measured.Count == 0)
        {
            return scraps;
        }

        var runs = new List<PassageSection[]>();
        foreach (var run in measured)
        {
            var undrawn = new List<PassageSection>();
            for (var i = 0; i + 1 < run.Length; i++)
            {
                if (Drawn(run[i], run[i + 1], coverage))
                {
                    Close(undrawn, runs);
                    continue;
                }

                if (undrawn.Count == 0)
                {
                    undrawn.Add(run[i]);
                }

                undrawn.Add(run[i + 1]);
            }

            Close(undrawn, runs);
        }

        if (runs.Count == 0)
        {
            return scraps;
        }

        // The drawn surfaces are kept whole whatever happens to the tubes: coarser tubes first,
        // then none, rather than a drawing with holes in it.
        var sides = PassageTubes.FullSides;
        if (drawn.TriangleCount + TrianglesAt(runs, sides) > MaxTriangles)
        {
            sides = PassageTubes.FewerSides;
            if (drawn.TriangleCount + TrianglesAt(runs, sides) > MaxTriangles)
            {
                return scraps;
            }
        }

        var mesh = new WallMeshAssembler();
        var vertices = new int[drawn.VertexCount];
        for (var v = 0; v < vertices.Length; v++)
        {
            vertices[v] = mesh.Vertex(new LocalPoint(
                drawn.Positions[v * 3], drawn.Positions[(v * 3) + 1], drawn.Positions[(v * 3) + 2]));
        }

        for (var t = 0; t < drawn.Indices.Length; t += 3)
        {
            mesh.Triangle(vertices[drawn.Indices[t]], vertices[drawn.Indices[t + 1]], vertices[drawn.Indices[t + 2]]);
        }

        var walledLegs = 0;
        foreach (var run in runs)
        {
            PassageTubes.Append(mesh, run, sides);
            walledLegs += run.Length - 1;
        }

        return mesh.ToMesh() is { } built
            ? new SurveyWalls(built, SurveyWallSource.ScrapsAndPassageDimensions, sides, walledLegs, passageLegs, null)
            : scraps;
    }

    /// <summary>Whether a drawn wall passes close enough to the stretch between two sections.</summary>
    private static bool Drawn(PassageSection from, PassageSection to, WallCoverage coverage)
    {
        var middle = new LocalPoint(
            (from.Station.East + to.Station.East) / 2,
            (from.Station.North + to.Station.North) / 2,
            (from.Station.Up + to.Station.Up) / 2);
        return coverage.Within(middle, Math.Max(Widest(from.Walls), Widest(to.Walls)) + DrawnWallSlackM);
    }

    private static double Widest(WallDistances walls) =>
        Math.Max(
            Math.Max(walls.Left ?? 0, walls.Right ?? 0),
            Math.Max(walls.Up ?? 0, walls.Down ?? 0));

    /// <summary>
    /// The compiler's own wall surfaces, joined into one mesh; null when the file has none that
    /// draw, which leaves the measured dimensions to be tried.
    /// </summary>
    private static SurveyWalls? FromScraps(CaveModel model, SurveyPlacement placement, int passageLegs)
    {
        long stated = 0;
        foreach (var scrap in model.Scraps)
        {
            stated += scrap.Triangles.Count;
        }

        if (stated == 0)
        {
            return null;
        }

        // These surfaces cannot be thinned the way a tube can: they are somebody else's triangles,
        // and dropping some of them leaves holes rather than a coarser wall.
        if (stated > MaxTriangles)
        {
            return new SurveyWalls(
                null, SurveyWallSource.None, 0, 0, passageLegs,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"the file's own wall surfaces hold {stated} triangles, more than the {MaxTriangles} a built mesh may hold"));
        }

        var mesh = new WallMeshAssembler();
        foreach (var scrap in model.Scraps)
        {
            var vertices = new int[scrap.Points.Count];
            for (var i = 0; i < vertices.Length; i++)
            {
                var point = scrap.Points[i];
                var (east, north, up) = placement.ToLocal(point.X, point.Y, point.Z);
                vertices[i] = mesh.Vertex(new LocalPoint(east, north, up));
            }

            // Every corner is inside its own point list: the reader refuses a file where one is
            // not. What does occur is a face whose corners are not three distinct places — written
            // where a drawn passage pinches out — and those are dropped as they are added.
            foreach (var triangle in scrap.Triangles)
            {
                mesh.Triangle(vertices[triangle.A], vertices[triangle.B], vertices[triangle.C]);
            }
        }

        return mesh.ToMesh() is { } built
            ? new SurveyWalls(built, SurveyWallSource.Scraps, 0, 0, passageLegs, null)
            : null;
    }

    /// <summary>
    /// Tubes around every stretch of passage whose walls were measured at both ends.
    /// </summary>
    private static SurveyWalls FromPassageDimensions(List<PassageSection[]> runs, int passageLegs)
    {
        if (runs.Count == 0)
        {
            return new SurveyWalls(
                null, SurveyWallSource.None, 0, 0, passageLegs,
                "the file carries no wall surfaces and no leg with passage dimensions measured at both ends");
        }

        var walledLegs = 0;
        foreach (var run in runs)
        {
            walledLegs += run.Length - 1;
        }

        // Coarser before nothing. Halving the corners of every ring halves the mesh and moves no
        // measured point, so a very large cave is drawn squarer rather than not drawn.
        var sides = PassageTubes.FullSides;
        if (TrianglesAt(runs, sides) > MaxTriangles)
        {
            sides = PassageTubes.FewerSides;
            if (TrianglesAt(runs, sides) is var needed && needed > MaxTriangles)
            {
                return new SurveyWalls(
                    null, SurveyWallSource.None, 0, 0, passageLegs,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"tubes around its {walledLegs} measured legs need {needed} triangles even at {sides} sides, more than the {MaxTriangles} a built mesh may hold"));
            }
        }

        var mesh = new WallMeshAssembler();
        foreach (var run in runs)
        {
            PassageTubes.Append(mesh, run, sides);
        }

        // Every measured distance in the file can be a zero — a survey run along one wall of a
        // bedding plane with the roof on the stations — and then the rings have no area and there
        // is no surface to show, although something was measured.
        return mesh.ToMesh() is { } built
            ? new SurveyWalls(built, SurveyWallSource.PassageDimensions, sides, walledLegs, passageLegs, null)
            : new SurveyWalls(
                null, SurveyWallSource.None, 0, 0, passageLegs,
                "every passage dimension the file measured is zero, so the tubes have no surface");
    }

    private static long TrianglesAt(List<PassageSection[]> runs, int sides)
    {
        long total = 0;
        foreach (var run in runs)
        {
            total += PassageTubes.TriangleCount(run.Length, sides);
        }

        return total;
    }

    /// <summary>
    /// The format that measures the walls at each end of a leg: one tube per leg, between the
    /// cross-section at its start and the one at its end.
    ///
    /// <para>
    /// Both ends or nothing. A leg measured at one end only would have to be closed to a point at
    /// the other or carried on at the one size it has, and either is a statement about a place
    /// nobody measured.
    /// </para>
    /// </summary>
    private static void AddMeasuredLegs(CaveModel model, SurveyPlacement placement, List<PassageSection[]> runs)
    {
        foreach (var shot in model.Shots)
        {
            if ((shot.Flags & NotWalled) != 0
                || shot.FromLrud is not { } atStart
                || shot.ToLrud is not { } atEnd
                || WallDistances.Read(atStart.Left, atStart.Right, atStart.Up, atStart.Down) is not { } startWalls
                || WallDistances.Read(atEnd.Left, atEnd.Right, atEnd.Up, atEnd.Down) is not { } endWalls
                || (shot.ToPosition - shot.FromPosition).Length < PassageTubes.ShortestLegM)
            {
                continue;
            }

            runs.Add(
            [
                new PassageSection(Local(placement, shot.FromPosition), startWalls),
                new PassageSection(Local(placement, shot.ToPosition), endWalls),
            ]);
        }
    }

    /// <summary>
    /// The format that states runs of cross-sections keyed by station: one tube per unbroken run,
    /// its rings shared between the stretches either side of them.
    ///
    /// <para>
    /// A run is broken wherever continuing it would mean drawing something the file does not say:
    /// at a cross-section naming a station the file does not have, at one where nothing was
    /// measured, and between two consecutive cross-sections that no leg of the survey joins. The
    /// last is not a formality. A run whose end was never marked carries straight on into the next
    /// passage's first station, and joining those two draws a tube clean through the rock between
    /// two places that are not connected.
    /// </para>
    /// </summary>
    private static void AddMeasuredPassages(CaveModel model, SurveyPlacement placement, List<PassageSection[]> runs)
    {
        if (model.Passages.Count == 0)
        {
            return;
        }

        var positions = new Dictionary<string, CaveVector3>(model.Stations.Count, StringComparer.Ordinal);
        foreach (var station in model.Stations)
        {
            positions.TryAdd(station.Name, station.Position);
        }

        // Legs are matched to stations by position because this format's legs name no stations;
        // the reading that stores them does the same, for the same reason.
        var joined = new HashSet<(CaveVector3, CaveVector3)>();
        foreach (var shot in model.Shots)
        {
            if ((shot.Flags & NotPassage) == 0)
            {
                joined.Add((shot.FromPosition, shot.ToPosition));
                joined.Add((shot.ToPosition, shot.FromPosition));
            }
        }

        var run = new List<PassageSection>();
        foreach (var passage in model.Passages)
        {
            CaveVector3? previous = null;
            foreach (var section in passage.Stations)
            {
                if (!positions.TryGetValue(section.StationName, out var at)
                    || WallDistances.Read(section.Left, section.Right, section.Up, section.Down) is not { } walls)
                {
                    Close(run, runs);
                    previous = null;
                    continue;
                }

                if (previous is { } before
                    && ((at - before).Length < PassageTubes.ShortestLegM || !joined.Contains((before, at))))
                {
                    Close(run, runs);
                }

                run.Add(new PassageSection(Local(placement, at), walls));
                previous = at;
            }

            Close(run, runs);
        }
    }

    /// <summary>Ends the run being gathered, keeping it if it is long enough to have a tube.</summary>
    private static void Close(List<PassageSection> run, List<PassageSection[]> runs)
    {
        if (run.Count >= 2)
        {
            runs.Add([.. run]);
        }

        run.Clear();
    }

    private static LocalPoint Local(SurveyPlacement placement, CaveVector3 point)
    {
        var (east, north, up) = placement.ToLocal(point.X, point.Y, point.Z);
        return new LocalPoint(east, north, up);
    }
}
