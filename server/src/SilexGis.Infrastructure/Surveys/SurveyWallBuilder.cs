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
    string? Reason);

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
/// <b>What is never done here is the thing that would be easiest: giving every leg a tube.</b> A
/// leg with no measured walls is a line somebody walked along, and nothing in the file says whether
/// it runs down a crawl or across a chamber. A tube of a default size around it looks, on screen,
/// exactly like a measured passage — so it is not drawn, a file with nothing measured gets no walls
/// at all, and that outcome is an answer rather than a failure. A shot at the wall gets no tube
/// either: it is a measurement <i>of</i> the wall, and a passage drawn around it would be a
/// passage through the rock.
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
    /// The walls of <paramref name="source"/>, placed by <paramref name="placement"/>.
    /// </summary>
    public static SurveyWalls Build(CaveModel source, SurveyPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(placement);

        // Settled here as the reading settles it, so the legs this takes for wall shots are the
        // legs every other figure about the survey takes for wall shots. An exporter that flags
        // none of them still fires them at points it declined to name.
        var model = SurveyWallShots.Flagged(source);
        var passageLegs = model.Shots.Count(shot => (shot.Flags & NotPassage) == 0);

        var scraps = FromScraps(model, placement, passageLegs);
        if (scraps is not null)
        {
            return scraps;
        }

        return FromPassageDimensions(model, placement, passageLegs);
    }

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
    private static SurveyWalls FromPassageDimensions(CaveModel model, SurveyPlacement placement, int passageLegs)
    {
        var runs = new List<PassageSection[]>();
        AddMeasuredLegs(model, placement, runs);
        AddMeasuredPassages(model, placement, runs);

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
