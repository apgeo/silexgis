// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>One vertex of a depth-coloured run, degrees.</summary>
public readonly record struct DepthPoint(double Longitude, double Latitude);

/// <summary>
/// A stretch of passage that falls in one depth class: the vertices to draw and the class to
/// draw them in.
/// </summary>
/// <param name="DepthClass">The class, an index into <see cref="PassageDepth.ClassEdgesM"/>
/// plus one; see <see cref="PassageDepth.ClassOf"/>.</param>
/// <param name="Points">Two or more vertices, in passage order.</param>
public sealed record DepthRun(int DepthClass, IReadOnlyList<DepthPoint> Points);

/// <summary>
/// How a map layer reads rock cover as a graduated colour: where the classes break, which class a
/// thickness falls in, and how a profile's stations become drawable runs.
///
/// <para>
/// The classes live here so the legend a reader sees and the colour a stretch of passage gets cannot
/// disagree: the answer carries these edges, and the client draws its legend from them.
/// </para>
/// </summary>
public static class PassageDepth
{
    /// <summary>
    /// The class edges, metres of rock above the passage, ascending. Class 0 is below the first edge
    /// (a passage reading as at or above its ground), class <c>n</c> lies from edge <c>n-1</c> up to
    /// edge <c>n</c>, and the last class is everything from the last edge up.
    /// </summary>
    public static readonly IReadOnlyList<double> ClassEdgesM = [0, 10, 25, 50, 100, 200];

    /// <summary>How many classes there are: one more than there are edges.</summary>
    public static int ClassCount => ClassEdgesM.Count + 1;

    /// <summary>
    /// The class a thickness of rock falls in. Negative values are kept as their own class rather than
    /// clamped to nought: a passage reading above its ground is the finding, not noise to hide.
    /// </summary>
    public static int ClassOf(double overburdenM)
    {
        for (var i = 0; i < ClassEdgesM.Count; i++)
        {
            if (overburdenM < ClassEdgesM[i])
            {
                return i;
            }
        }

        return ClassEdgesM.Count;
    }

    /// <summary>
    /// The runs to draw for one cave's stations, given the rock cover read at each.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two neighbouring stations are joined only when both were read and only when they lie no more
    /// than one and a half station spacings apart on the ground. Stations are spread along the
    /// line work in the order it is recorded, and a survey is a network, not a walk: the end of one
    /// branch is followed in that order by the start of an unrelated one, and drawing a line between
    /// the two would invent passage. The real spacing is the passage length over the number of
    /// intervals, so a join that spans more ground than that is a jump, not a stretch of passage.
    /// </para>
    /// <para>
    /// A join takes the mean of the two readings, so a stretch is coloured by the cover over it
    /// rather than by whichever end was visited first. An unread station contributes no join at
    /// all: a gap in the terrain is a gap in the line, never a stretch drawn at nought.
    /// </para>
    /// </remarks>
    /// <param name="stations">The cave's stations, in order.</param>
    /// <param name="overburdenM">The cover at each station, null where the ground was not read; same
    /// length as <paramref name="stations"/>.</param>
    /// <param name="passageLengthM">The length the stations were spread over.</param>
    public static IReadOnlyList<DepthRun> Runs(
        IReadOnlyList<OverburdenStation> stations, IReadOnlyList<double?> overburdenM, double passageLengthM)
    {
        ArgumentNullException.ThrowIfNull(stations);
        ArgumentNullException.ThrowIfNull(overburdenM);
        if (stations.Count != overburdenM.Count)
        {
            throw new ArgumentException("One reading per station is required.", nameof(overburdenM));
        }

        var runs = new List<(int Class, List<DepthPoint> Points)>();
        if (stations.Count < 2)
        {
            return [];
        }

        const double MetresPerDegree = 111_320d;
        var reach = 1.5 * passageLengthM / (stations.Count - 1);
        (int Class, List<DepthPoint> Points)? open = null;

        for (var i = 0; i + 1 < stations.Count; i++)
        {
            var a = stations[i];
            var b = stations[i + 1];
            if (overburdenM[i] is not { } coverA || overburdenM[i + 1] is not { } coverB)
            {
                open = null;
                continue;
            }

            var cosLat = Math.Cos((a.Latitude + b.Latitude) / 2 * Math.PI / 180d);
            var dx = (b.Longitude - a.Longitude) * cosLat * MetresPerDegree;
            var dy = (b.Latitude - a.Latitude) * MetresPerDegree;
            if (Math.Sqrt(dx * dx + dy * dy) > reach)
            {
                open = null;
                continue;
            }

            var cls = ClassOf((coverA + coverB) / 2);
            if (open is { } current && current.Class == cls)
            {
                current.Points.Add(new DepthPoint(b.Longitude, b.Latitude));
            }
            else
            {
                var run = (Class: cls, Points: new List<DepthPoint>
                {
                    new(a.Longitude, a.Latitude),
                    new(b.Longitude, b.Latitude),
                });
                runs.Add(run);
                open = run;
            }
        }

        return [.. runs.Select(r => new DepthRun(r.Class, r.Points))];
    }
}
