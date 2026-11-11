// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;

namespace SilexGis.Domain.Scans;

/// <summary>
/// One satellite fix as a MANDEYE scanner logged it, stamped with the lidar's own clock.
/// </summary>
/// <param name="TimeNs">The lidar clock at the fix, in nanoseconds — the clock the trajectory is stamped with.</param>
/// <param name="Latitude">Degrees north, WGS 84.</param>
/// <param name="Longitude">Degrees east, WGS 84.</param>
/// <param name="AltitudeM">Height above mean sea level, metres, as the receiver reported it.</param>
/// <param name="Hdop">Horizontal dilution of precision; lower is better.</param>
/// <param name="Satellites">Satellites the receiver used.</param>
/// <param name="Quality">The receiver's fix quality: zero is no fix, one a plain fix, higher a corrected one.</param>
public sealed record GnssFix(
    long TimeNs,
    double Latitude,
    double Longitude,
    double AltitudeM,
    double Hdop,
    int Satellites,
    int Quality);

/// <summary>One pose of a scan's trajectory: the scanner's position, in the frame the exported cloud is in.</summary>
public readonly record struct TrajectoryPoint(long TimeNs, double X, double Y, double Z);

/// <summary>
/// Reading the satellite fixes a MANDEYE recording carries.
///
/// <para>
/// The scanner writes one <c>gnssNNNN.gnss</c> file per chunk, one fix per line, whitespace
/// separated: the lidar clock in nanoseconds, latitude, longitude, altitude, HDOP, satellites,
/// geoid separation, age of the correction, the receiver's UTC time, the fix quality and the
/// computer's clock in milliseconds. A line without a fix carries <c>nan</c> in the position
/// fields and quality zero. The lidar clock is what makes a fix usable for placing a scan: it is
/// the clock the trajectory is stamped with, so a fix says where the scanner was at a known pose.
/// The <c>.nmea</c> files the scanner may also write hold the receiver's sentences without that
/// clock and are not read here.
/// </para>
/// </summary>
public static class MandeyeGnss
{
    /// <summary>The fewest satellites a fix is trusted with.</summary>
    public const int MinimumSatellites = 6;

    /// <summary>The largest horizontal dilution a fix is trusted with.</summary>
    public const double MaximumHdop = 5.0;

    /// <summary>
    /// The shortest stretch of good fixes, end to end, a heading is taken from. A receiver at the
    /// fix quality a handheld scanner carries wanders by a few metres; a bearing taken over less
    /// than this is mostly that wander. Measured on a published two-sensor MANDEYE recording, against
    /// the heading its first two minutes of walking give: about 17° off from a 17 m track, 7° from
    /// 22 m, 2.4° from 37 m and 1.5° from 54 m. Thirty metres keeps the error to a few degrees —
    /// still several metres sideways a hundred metres into a cave, which control points then correct.
    /// </summary>
    public const double MinimumTrackM = 30.0;

    /// <summary>The most fixes a placement is fitted through; a longer walk is sampled evenly down to this.</summary>
    public const int MaximumFitFixes = ScanControlPoints.MaximumPairs;

    /// <summary>Every fix a file holds, in file order, skipping lines that are not fixes. Lines without a position are kept, with NaN.</summary>
    public static IReadOnlyList<GnssFix> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var fixes = new List<GnssFix>();
        foreach (var raw in text.Split('\n'))
        {
            var f = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 10
                || !long.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var time)
                || !TryDouble(f[1], out var lat)
                || !TryDouble(f[2], out var lon)
                || !TryDouble(f[3], out var alt)
                || !TryDouble(f[4], out var hdop)
                || !int.TryParse(f[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sats)
                || !int.TryParse(f[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out var quality))
            {
                continue;
            }

            fixes.Add(new GnssFix(time, lat, lon, alt, hdop, sats, quality));
        }

        return fixes;
    }

    /// <summary>A fix good enough to place a scan by: a real fix, enough satellites, a small dilution, a position on earth.</summary>
    public static bool IsGood(GnssFix fix)
    {
        ArgumentNullException.ThrowIfNull(fix);
        return fix.Quality >= 1
            && fix.Satellites >= MinimumSatellites
            && double.IsFinite(fix.Hdop) && fix.Hdop <= MaximumHdop
            && double.IsFinite(fix.AltitudeM)
            && ScanPlacement.IsOnEarth(fix.Longitude, fix.Latitude, Math.Clamp(fix.AltitudeM, ScanPlacement.LowestHeightM, ScanPlacement.HighestHeightM));
    }

    /// <summary>
    /// A trajectory file's poses, in time order. Two layouts are read: the export's
    /// <c>trajectories.csv</c> — no header, comma separated, the lidar clock, the computer's clock,
    /// then x, y, z and an orientation — and the odometry's own files, whose header names the
    /// columns, with the position in <c>pose03</c>, <c>pose13</c> and <c>pose23</c>.
    /// </summary>
    public static IReadOnlyList<TrajectoryPoint> ParseTrajectory(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var points = new List<TrajectoryPoint>();
        int t = 0, x = 2, y = 3, z = 4;
        var first = true;
        foreach (var raw in text.Split('\n'))
        {
            var f = raw.Split([',', ' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries);
            if (f.Length == 0)
            {
                continue;
            }

            if (first)
            {
                first = false;
                var named = Array.IndexOf(f, "timestamp_nanoseconds");
                if (named >= 0)
                {
                    (t, x, y, z) = (named, Array.IndexOf(f, "pose03"), Array.IndexOf(f, "pose13"), Array.IndexOf(f, "pose23"));
                    if (x < 0 || y < 0 || z < 0)
                    {
                        return [];
                    }

                    continue;
                }
            }

            var widest = Math.Max(Math.Max(t, x), Math.Max(y, z));
            if (f.Length <= widest
                || !TryDouble(f[t], out var time)
                || !TryDouble(f[x], out var px) || !double.IsFinite(px)
                || !TryDouble(f[y], out var py) || !double.IsFinite(py)
                || !TryDouble(f[z], out var pz) || !double.IsFinite(pz)
                || !double.IsFinite(time))
            {
                continue;
            }

            points.Add(new TrajectoryPoint((long)time, px, py, pz));
        }

        // A stable order: a recording without a clock writes every pose at time zero, and its
        // poses must then stay in the order they were written, which is the order they were taken.
        return [.. points.OrderBy(p => p.TimeNs)];
    }

    /// <summary>The trajectory's position at a time, between the two poses either side; null outside its span.</summary>
    public static (double X, double Y, double Z)? At(IReadOnlyList<TrajectoryPoint> trajectory, long timeNs)
    {
        ArgumentNullException.ThrowIfNull(trajectory);
        if (trajectory.Count == 0 || timeNs < trajectory[0].TimeNs || timeNs > trajectory[^1].TimeNs)
        {
            return null;
        }

        int lo = 0, hi = trajectory.Count - 1;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (trajectory[mid].TimeNs <= timeNs)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        var a = trajectory[lo];
        var b = trajectory[hi];
        var span = b.TimeNs - a.TimeNs;
        var s = span == 0 ? 0 : (double)(timeNs - a.TimeNs) / span;
        return (a.X + ((b.X - a.X) * s), a.Y + ((b.Y - a.Y) * s), a.Z + ((b.Z - a.Z) * s));
    }

    /// <summary>
    /// The longest break between two good fixes inside one stretch of open sky. A receiver logs a
    /// fix a second; a longer silence means the sky was lost — the scanner went underground — and
    /// the fixes after it belong to a different stretch of walking.
    /// </summary>
    public const double RunGapSeconds = 10.0;

    /// <summary>
    /// How much walking, in time, a placement is fitted over, counted from the fix nearest the cave
    /// (where the sky was lost, or found again). The scanner's own track drifts the longer it runs: on a published fifteen-minute MANDEYE
    /// recording it stayed within about a metre of the fixes over any two minutes but strayed tens of
    /// metres over the whole walk, so a heading fitted over everything was far off. A shorter window
    /// is widened, away from the cave, until its fixes span <see cref="MinimumTrackM"/>.
    /// </summary>
    public const double FitWindowSeconds = 120.0;

    /// <summary>
    /// A first placement from the fixes the scanner logged on the surface, or null when they do
    /// not give one.
    ///
    /// <para>
    /// The good fixes are split into stretches of open sky wherever they stop for longer than
    /// <see cref="RunGapSeconds"/>. The scan is placed from the end of the first stretch — the walk
    /// up to where the sky was lost on the way in, nearest the entrance and nearest in time to the
    /// cave the scanner went on to record: the walking just before that point
    /// (<see cref="FitWindowSeconds"/>, widened until it spans <see cref="MinimumTrackM"/>). When the
    /// walk to the entrance is too short for a heading, the next stretch is used the other way round:
    /// the walking just after the scanner came back out.
    /// </para>
    /// <para>
    /// The placement is a least-squares fit of the trajectory points at the fix times to the fixes
    /// over that window, level, with no tilt. Fixes from the rest of the walk are left out: the
    /// scanner's own track drifts, and on a published recording that went into a bunker and back out
    /// a fit over everything left the walk in about 90 m from its fixes, against 3 m for this one.
    /// </para>
    /// <para>
    /// No placement is given when no stretch holds good fixes spanning <see cref="MinimumTrackM"/>
    /// inside the trajectory: a scanner that stood still outside says where it was but not which way
    /// it faced.
    /// </para>
    /// </summary>
    /// <param name="fixes">Every fix of the recording, in any order.</param>
    /// <param name="trajectory">The trajectory, in time order, in the exported cloud's frame.</param>
    /// <param name="tilesZero">The point of that frame the tiles are written about.</param>
    public static ScanPlacement? Place(
        IReadOnlyList<GnssFix> fixes,
        IReadOnlyList<TrajectoryPoint> trajectory,
        (double X, double Y, double Z) tilesZero)
    {
        ArgumentNullException.ThrowIfNull(fixes);
        ArgumentNullException.ThrowIfNull(trajectory);

        var usable = fixes
            .Where(IsGood)
            .OrderBy(f => f.TimeNs)
            .Select(f => (Fix: f, Pose: At(trajectory, f.TimeNs)))
            .Where(p => p.Pose is not null)
            .Select(p => new Located(p.Fix, p.Pose!.Value, Geo.LocalTangentFrame.ToEcef(p.Fix.Longitude, p.Fix.Latitude, Height(p.Fix))))
            .ToList();

        var runs = Runs(usable);
        for (var i = 0; i < runs.Count; i++)
        {
            // The first stretch is the walk in, nearest the cave where it ends; any later one is a walk
            // back out, nearest the cave where it starts. Either way the window reaches away from the cave.
            var walk = i == 0 ? runs[i].AsEnumerable().Reverse().ToList() : runs[i];
            var window = Window(walk);
            if (window is null)
            {
                continue;
            }

            var placed = Fit(window, tilesZero);
            if (placed is not null)
            {
                return placed;
            }
        }

        return null;
    }

    /// <summary>One good fix with the trajectory's pose at its time and its own position in earth-centred metres.</summary>
    private sealed record Located(GnssFix Fix, (double X, double Y, double Z) Pose, Geo.EcefPoint Ecef);

    /// <summary>The good fixes, in time order, cut wherever they fall silent for longer than <see cref="RunGapSeconds"/>.</summary>
    private static List<List<Located>> Runs(List<Located> usable)
    {
        var runs = new List<List<Located>>();
        var gapNs = (long)(RunGapSeconds * 1e9);
        foreach (var fix in usable)
        {
            if (runs.Count == 0 || fix.Fix.TimeNs - runs[^1][^1].Fix.TimeNs > gapNs)
            {
                runs.Add([]);
            }

            runs[^1].Add(fix);
        }

        return runs;
    }

    /// <summary>
    /// The fixes a placement is fitted over, starting from the fix nearest the cave (the walk's first
    /// entry, called the anchor here): those
    /// within <see cref="FitWindowSeconds"/> of it, then as many more as it takes to reach
    /// <see cref="MinimumTrackM"/> from the anchor; null when the whole walk never gets that far.
    /// </summary>
    private static List<Located>? Window(List<Located> walk)
    {
        if (walk.Count < 2)
        {
            return null;
        }

        var anchor = walk[0];
        var windowNs = (long)(FitWindowSeconds * 1e9);
        var farthest = 0.0;
        var taken = new List<Located>();
        foreach (var fix in walk)
        {
            var within = Math.Abs(fix.Fix.TimeNs - anchor.Fix.TimeNs) <= windowNs;
            if (!within && farthest >= MinimumTrackM)
            {
                break;
            }

            taken.Add(fix);
            farthest = Math.Max(farthest, fix.Ecef.DistanceTo(anchor.Ecef));
        }

        return farthest >= MinimumTrackM ? taken : null;
    }

    /// <summary>A level least-squares fit of the window's poses onto its fixes.</summary>
    private static ScanPlacement? Fit(List<Located> window, (double X, double Y, double Z) tilesZero)
    {
        var sampled = window.Count <= MaximumFitFixes
            ? window
            : Enumerable.Range(0, MaximumFitFixes)
                .Select(i => window[(int)Math.Round(i * (window.Count - 1) / (double)(MaximumFitFixes - 1))])
                .ToList();

        var pairs = sampled.Select(p => new ScanControlPair(
                p.Pose.X - tilesZero.X,
                p.Pose.Y - tilesZero.Y,
                p.Pose.Z - tilesZero.Z,
                p.Fix.Longitude,
                p.Fix.Latitude,
                Height(p.Fix)))
            .ToList();

        var outcome = ScanControlPoints.Fit(pairs, new ScanControlOptions(FitTilt: false, WarnM: double.MaxValue));
        if (outcome.Fit is not { } fit)
        {
            return null;
        }

        // The fit itself is the placement. Moving it on to the anchor fix alone was tried and was
        // worse: the last fix before the sky is lost is often the poorest of the walk (the receiver is
        // already under the rock face), and on a published recording the fixes of the last minute
        // before it landed a median 10 m off that way against 3–4 m from the fit over the window.
        var placement = fit.Placement;
        return ScanPlacement.IsOnEarth(placement.Longitude, placement.Latitude, placement.HeightM)
            ? new ScanPlacement(placement.Longitude, placement.Latitude, placement.HeightM, placement.HeadingDeg, ScanPlacementSource.Gnss)
            : null;
    }

    private static double Height(GnssFix fix) =>
        Math.Clamp(fix.AltitudeM, ScanPlacement.LowestHeightM, ScanPlacement.HighestHeightM);

    private static bool TryDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
