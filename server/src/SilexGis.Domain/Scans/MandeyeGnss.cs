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
    /// than this is mostly that wander.
    /// </summary>
    public const double MinimumTrackM = 15.0;

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
    /// A first placement from the fixes the scanner logged on the surface, or null when they do
    /// not give one.
    ///
    /// <para>
    /// The heading is the one that best turns the trajectory's track onto the satellites' track
    /// over the same stretch of time — a least-squares fit of the trajectory points at the fix
    /// times to the fixes, level, with no tilt. The position is then taken from the last good fix
    /// alone: the trajectory point at that fix is put exactly on it. The last fix is the one taken
    /// nearest the entrance, before the sky was lost, and every metre of the cave is reached from
    /// there; a position averaged over the whole walk would carry the receiver's wander from the
    /// car park into the cave.
    /// </para>
    /// <para>
    /// No placement is given when fewer than two good fixes fall inside the trajectory, or when the
    /// good fixes span less than <see cref="MinimumTrackM"/>: a scanner that stood still outside
    /// says where it was but not which way it faced.
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
            .Select(p => (p.Fix, Pose: p.Pose!.Value))
            .ToList();
        if (usable.Count < 2)
        {
            return null;
        }

        var sampled = usable.Count <= MaximumFitFixes
            ? usable
            : Enumerable.Range(0, MaximumFitFixes)
                .Select(i => usable[(int)Math.Round(i * (usable.Count - 1) / (double)(MaximumFitFixes - 1))])
                .ToList();

        var pairs = sampled.Select(p => new ScanControlPair(
                p.Pose.X - tilesZero.X,
                p.Pose.Y - tilesZero.Y,
                p.Pose.Z - tilesZero.Z,
                p.Fix.Longitude,
                p.Fix.Latitude,
                Height(p.Fix)))
            .ToList();

        var firstFix = Geo.LocalTangentFrame.ToEcef(usable[0].Fix.Longitude, usable[0].Fix.Latitude, Height(usable[0].Fix));
        var farthest = usable.Max(p => Geo.LocalTangentFrame.ToEcef(p.Fix.Longitude, p.Fix.Latitude, Height(p.Fix)).DistanceTo(firstFix));
        if (farthest < MinimumTrackM)
        {
            return null;
        }

        var outcome = ScanControlPoints.Fit(pairs, new ScanControlOptions(FitTilt: false, WarnM: double.MaxValue));
        if (outcome.Fit is not { } fit)
        {
            return null;
        }

        // Carry the fitted placement over by the distance between where it puts the last good fix's
        // pose and where that fix is. A shift of metres turns the east-north-up frame by a few
        // millionths of a degree, so the heading needs no second round.
        var (last, lastPose) = usable[^1];
        var target = Geo.LocalTangentFrame.ToEcef(last.Longitude, last.Latitude, Height(last));
        var landed = fit.Placement.EcefOf(lastPose.X - tilesZero.X, lastPose.Y - tilesZero.Y, lastPose.Z - tilesZero.Z);
        var zero = Geo.LocalTangentFrame.ToEcef(fit.Placement.Longitude, fit.Placement.Latitude, fit.Placement.HeightM);
        var moved = Geo.LocalTangentFrame.ToGeodetic(new Geo.EcefPoint(
            zero.X + (target.X - landed.X),
            zero.Y + (target.Y - landed.Y),
            zero.Z + (target.Z - landed.Z)));
        var height = Math.Clamp(moved.HeightM, ScanPlacement.LowestHeightM, ScanPlacement.HighestHeightM);
        if (!ScanPlacement.IsOnEarth(moved.Longitude, moved.Latitude, height))
        {
            return null;
        }

        return new ScanPlacement(moved.Longitude, moved.Latitude, height, fit.Placement.HeadingDeg, ScanPlacementSource.Gnss);
    }

    private static double Height(GnssFix fix) =>
        Math.Clamp(fix.AltitudeM, ScanPlacement.LowestHeightM, ScanPlacement.HighestHeightM);

    private static bool TryDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
