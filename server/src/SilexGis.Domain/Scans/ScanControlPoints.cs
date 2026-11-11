// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Scans;

/// <summary>
/// One control point: a point somebody picked in the scan and the survey station it stands on.
/// </summary>
/// <param name="ScanX">The picked point's offset from the tiles' zero point along the tiles' +X, metres.</param>
/// <param name="ScanY">Along the tiles' +Y, metres.</param>
/// <param name="ScanZ">Along the tiles' +Z (up), metres.</param>
/// <param name="StationLongitude">The station's position, degrees east, WGS 84.</param>
/// <param name="StationLatitude">The station's position, degrees north, WGS 84.</param>
/// <param name="StationHeightM">The station's altitude, in the same kind of height the placement is stored in.</param>
public sealed record ScanControlPair(
    double ScanX,
    double ScanY,
    double ScanZ,
    double StationLongitude,
    double StationLatitude,
    double StationHeightM);

/// <summary>
/// How far one pair's picked point lands from its station once the scan is placed by the fit:
/// the station subtracted from where the point lands, in metres east, north and up at the station.
/// </summary>
/// <param name="Flagged">Whether the pair is further off than the installation's warning distance.</param>
public sealed record ScanControlResidual(
    double EastM,
    double NorthM,
    double UpM,
    double HorizontalM,
    double DistanceM,
    bool Flagged);

/// <summary>A fitted placement, the residual of every pair in the order given, and their root mean square.</summary>
/// <param name="Placement">The placement, with source <see cref="ScanPlacementSource.ControlPoints"/>.</param>
/// <param name="Residuals">One per pair, in the order the pairs were given.</param>
/// <param name="RmsM">The root mean square of the pairs' distances, metres.</param>
/// <param name="WarnM">The distance over which a pair was flagged.</param>
/// <param name="TiltFitted">Whether the fit was allowed to tilt the scan.</param>
public sealed record ScanControlFit(
    ScanPlacement Placement,
    IReadOnlyList<ScanControlResidual> Residuals,
    double RmsM,
    double WarnM,
    bool TiltFitted)
{
    /// <summary>How many pairs are further off than <see cref="WarnM"/>.</summary>
    public int FlaggedCount => Residuals.Count(r => r.Flagged);
}

/// <summary>A reason the pairs cannot be fitted, with the code a client can translate.</summary>
public sealed record ScanControlProblem(string Code, string Message);

/// <summary>Either a fit or the reasons there is none.</summary>
public sealed record ScanControlOutcome(ScanControlFit? Fit, IReadOnlyList<ScanControlProblem> Problems)
{
    internal static ScanControlOutcome Refused(params ScanControlProblem[] problems) => new(null, problems);
}

/// <summary>What a fit is asked to do.</summary>
/// <param name="FitTilt">
/// Whether to allow a small tilt as well as the heading. Off by default: the scanner's inertial
/// unit levels the recording better than a few hand-picked points can, and a tilt fitted from
/// points that are nearly in a line is decided by the picking error, not by the scan.
/// </param>
/// <param name="WarnM">The residual, in metres, above which a pair is flagged.</param>
public sealed record ScanControlOptions(bool FitTilt = false, double WarnM = ScanControlPoints.DefaultWarnM);

/// <summary>
/// Placing a scan by control points: the rigid motion — no scale — that brings points picked in
/// the scan closest, in the least-squares sense, to the survey stations they stand on.
///
/// <para>
/// A scan made by a levelled scanner is already upright and already in metres; what it lacks is a
/// position and a north. So by default the fit finds a translation in three dimensions and a turn
/// about the vertical only: the horizontal part is the closed-form two-dimensional solution, and the
/// vertical offset is the mean of the pairs' height differences. Two pairs determine it; with more
/// the residuals say how well the scan and the survey agree. A scale is never fitted — the scan's
/// metres are measured, and a scale fitted to a few picked points would only absorb picking error
/// into the scan's size. When a tilt is allowed, the turn becomes a full rotation (the quaternion
/// solution, which needs three points not in a line) and is refused if it is more than a small
/// correction.
/// </para>
///
/// <para>
/// The fit is solved in the east-north-up frame at the zero point it finds, re-solved until that
/// zero point stops moving, so the heading and tilt it stores are exactly the ones a globe applies
/// at the zero point when it draws the tiles. The residuals are then computed from the stored
/// placement itself, through the same composition, so they describe the placement as it will be
/// drawn and not some intermediate of the solution.
/// </para>
/// </summary>
public static class ScanControlPoints
{
    /// <summary>Two pairs fix a heading and a position.</summary>
    public const int MinimumPairs = 2;

    /// <summary>A tilt needs a third point off the line through the other two.</summary>
    public const int MinimumTiltPairs = 3;

    /// <summary>More points than anyone picks by hand; a ceiling so a request stays a request.</summary>
    public const int MaximumPairs = 100;

    /// <summary>The default distance over which a pair is flagged, in metres.</summary>
    public const double DefaultWarnM = 0.5;

    /// <summary>
    /// The least horizontal spread, in metres, the picked points (and, separately, their stations)
    /// must have. Two points closer than this decide a heading by the picking error: picking to five
    /// centimetres on a one-metre baseline is a three-degree heading, which at fifty metres down the
    /// passage is two and a half metres of wall in the wrong place.
    /// </summary>
    public const double MinimumSpreadM = 1.0;

    /// <summary>
    /// The largest tilt, in degrees, a fit may report. A levelled scanner is upright to a fraction of
    /// a degree; a fit that wants more than this is fitting a mismatched pair, not the scan.
    /// </summary>
    public const double MaximumTiltDeg = 10.0;

    /// <summary>The furthest a picked point may be from the tiles' zero point, in metres.</summary>
    public const double MaximumOffsetM = 50_000;

    public const string TooFewPairs = "survey_model.control_points_too_few";
    public const string TooManyPairs = "survey_model.control_points_too_many";
    public const string NotANumber = "survey_model.control_points_invalid";
    public const string StationOffEarth = "survey_model.control_points_station_off_earth";
    public const string OffsetTooFar = "survey_model.control_points_offset_too_far";
    public const string ScanPointsTooClose = "survey_model.control_points_scan_too_close";
    public const string StationsTooClose = "survey_model.control_points_stations_too_close";
    public const string TiltNeedsThree = "survey_model.control_points_tilt_needs_three";
    public const string TiltPointsInLine = "survey_model.control_points_tilt_in_line";
    public const string TiltTooLarge = "survey_model.control_points_tilt_too_large";
    public const string PlacementOffEarth = "survey_model.control_points_placement_off_earth";

    /// <summary>Fits the placement, or says every reason it cannot, in a stable order.</summary>
    public static ScanControlOutcome Fit(IReadOnlyList<ScanControlPair> pairs, ScanControlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        options ??= new ScanControlOptions();
        var warnM = double.IsFinite(options.WarnM) && options.WarnM > 0 ? options.WarnM : DefaultWarnM;

        var problems = Validate(pairs, options.FitTilt);
        if (problems.Count > 0)
        {
            return new(null, problems);
        }

        var stations = pairs
            .Select(p => LocalTangentFrame.ToEcef(p.StationLongitude, p.StationLatitude, p.StationHeightM))
            .ToArray();
        var scan = pairs.Select(p => new[] { p.ScanX, p.ScanY, p.ScanZ }).ToArray();

        // Start in the frame at the stations' centroid, then move the frame to the zero point the
        // fit finds and solve again. A frame a kilometre away is turned by a hundredth of a degree
        // against the one at the zero point; two or three rounds put the frame on the zero point to
        // well under a millimetre.
        var start = LocalTangentFrame.ToGeodetic(Centroid(stations));
        var frame = new LocalTangentFrame(start.Longitude, start.Latitude, start.HeightM);
        double[,] rotation = Identity();
        (double Longitude, double Latitude, double HeightM) zero = start;

        for (var round = 0; round < 8; round++)
        {
            var local = stations.Select(s =>
            {
                var (e, n, u) = frame.ToLocal(s);
                return new[] { e, n, u };
            }).ToArray();

            double[] translation;
            (rotation, translation) = options.FitTilt ? SolveRotation(scan, local) : SolveHeading(scan, local);

            var moved = frame.EcefAt(translation[0], translation[1], translation[2]);
            zero = LocalTangentFrame.ToGeodetic(moved);
            var settled = moved.DistanceTo(frame.EcefAt(0, 0, 0)) < 1e-5;
            frame = new LocalTangentFrame(zero.Longitude, zero.Latitude, zero.HeightM);
            if (settled)
            {
                break;
            }
        }

        if (!ScanPlacement.IsOnEarth(zero.Longitude, zero.Latitude, zero.HeightM))
        {
            return ScanControlOutcome.Refused(new ScanControlProblem(
                PlacementOffEarth,
                "The pairs put the scan's zero point somewhere no cave is; check that each point was paired with its own station."));
        }

        var (heading, pitch, roll) = Angles(rotation);
        if (options.FitTilt)
        {
            var tilt = Math.Acos(Math.Clamp(rotation[2, 2], -1, 1)) * 180 / Math.PI;
            if (tilt > MaximumTiltDeg)
            {
                return ScanControlOutcome.Refused(new ScanControlProblem(
                    TiltTooLarge,
                    $"The pairs ask for a tilt of {tilt:0.#}°, more than the {MaximumTiltDeg:0}° a levelled scan can be off; check the pairs, or fit without tilt."));
            }
        }
        else
        {
            // A heading-only fit was solved with the vertical fixed; nothing but rounding is left in
            // the other two angles.
            pitch = 0;
            roll = 0;
        }

        var placement = new ScanPlacement(
            zero.Longitude,
            zero.Latitude,
            zero.HeightM,
            ScanPlacement.NormalizeHeading(heading),
            ScanPlacementSource.ControlPoints,
            pitch,
            roll);

        var residuals = new ScanControlResidual[pairs.Count];
        var sumSquares = 0.0;
        for (var i = 0; i < pairs.Count; i++)
        {
            var pair = pairs[i];
            var landed = placement.EcefOf(pair.ScanX, pair.ScanY, pair.ScanZ);
            var atStation = new LocalTangentFrame(pair.StationLongitude, pair.StationLatitude, pair.StationHeightM);
            var (east, north, up) = atStation.ToLocal(landed);
            var horizontal = Math.Sqrt((east * east) + (north * north));
            var distance = Math.Sqrt((horizontal * horizontal) + (up * up));
            residuals[i] = new ScanControlResidual(east, north, up, horizontal, distance, distance > warnM);
            sumSquares += distance * distance;
        }

        return new(
            new ScanControlFit(placement, residuals, Math.Sqrt(sumSquares / pairs.Count), warnM, options.FitTilt),
            []);
    }

    private static List<ScanControlProblem> Validate(IReadOnlyList<ScanControlPair> pairs, bool fitTilt)
    {
        var problems = new List<ScanControlProblem>();
        if (pairs.Count < MinimumPairs)
        {
            problems.Add(new(TooFewPairs, $"At least {MinimumPairs} pairs of a scan point and a station are needed; {pairs.Count} given."));
            return problems;
        }

        if (pairs.Count > MaximumPairs)
        {
            problems.Add(new(TooManyPairs, $"At most {MaximumPairs} pairs; {pairs.Count} given."));
            return problems;
        }

        if (pairs.Any(p => !double.IsFinite(p.ScanX) || !double.IsFinite(p.ScanY) || !double.IsFinite(p.ScanZ)
            || !double.IsFinite(p.StationLongitude) || !double.IsFinite(p.StationLatitude) || !double.IsFinite(p.StationHeightM)))
        {
            problems.Add(new(NotANumber, "Every coordinate of every pair must be a finite number."));
            return problems;
        }

        if (pairs.Any(p => !ScanPlacement.IsOnEarth(p.StationLongitude, p.StationLatitude, p.StationHeightM)))
        {
            problems.Add(new(StationOffEarth, "A station's position is not a place on earth."));
        }

        if (pairs.Any(p => Math.Abs(p.ScanX) > MaximumOffsetM || Math.Abs(p.ScanY) > MaximumOffsetM || Math.Abs(p.ScanZ) > MaximumOffsetM))
        {
            problems.Add(new(OffsetTooFar, $"A scan point lies more than {MaximumOffsetM / 1000:0} km from the scan's zero point."));
        }

        if (problems.Count > 0)
        {
            return problems;
        }

        if (WidestSpread(pairs.Select(p => (p.ScanX, p.ScanY)).ToArray()) < MinimumSpreadM)
        {
            problems.Add(new(ScanPointsTooClose, $"The scan points are less than {MinimumSpreadM:0.#} m apart; pick points further apart."));
        }

        var stations = pairs
            .Select(p => LocalTangentFrame.ToEcef(p.StationLongitude, p.StationLatitude, p.StationHeightM))
            .ToArray();
        var centre = LocalTangentFrame.ToGeodetic(Centroid(stations));
        var frame = new LocalTangentFrame(centre.Longitude, centre.Latitude, centre.HeightM);
        var flat = stations.Select(s =>
        {
            var (e, n, _) = frame.ToLocal(s);
            return (e, n);
        }).ToArray();
        if (WidestSpread(flat) < MinimumSpreadM)
        {
            problems.Add(new(StationsTooClose, $"The stations are less than {MinimumSpreadM:0.#} m apart; pair points with stations further apart."));
        }

        if (fitTilt)
        {
            if (pairs.Count < MinimumTiltPairs)
            {
                problems.Add(new(TiltNeedsThree, $"A tilt needs at least {MinimumTiltPairs} pairs; {pairs.Count} given."));
            }
            else if (OffLine(pairs.Select(p => new[] { p.ScanX, p.ScanY, p.ScanZ }).ToArray()) < MinimumSpreadM)
            {
                problems.Add(new(TiltPointsInLine, $"For a tilt, one scan point must lie at least {MinimumSpreadM:0.#} m off the line through the others."));
            }
        }

        return problems;
    }

    /// <summary>The largest horizontal distance between any two of the points.</summary>
    private static double WidestSpread(IReadOnlyList<(double X, double Y)> points)
    {
        var widest = 0.0;
        for (var i = 0; i < points.Count; i++)
        {
            for (var j = i + 1; j < points.Count; j++)
            {
                var dx = points[i].X - points[j].X;
                var dy = points[i].Y - points[j].Y;
                widest = Math.Max(widest, Math.Sqrt((dx * dx) + (dy * dy)));
            }
        }

        return widest;
    }

    /// <summary>
    /// How far the point furthest from the line through the two most distant points lies from it:
    /// zero for points in a line, about which a rotation is undetermined.
    /// </summary>
    private static double OffLine(double[][] points)
    {
        int a = 0, b = 0;
        var widest = -1.0;
        for (var i = 0; i < points.Length; i++)
        {
            for (var j = i + 1; j < points.Length; j++)
            {
                var d = Distance(points[i], points[j]);
                if (d > widest)
                {
                    (widest, a, b) = (d, i, j);
                }
            }
        }

        if (widest <= 0)
        {
            return 0;
        }

        var axis = Sub(points[b], points[a]);
        var furthest = 0.0;
        foreach (var p in points)
        {
            var along = Sub(p, points[a]);
            var cross = Cross(axis, along);
            furthest = Math.Max(furthest, Norm(cross) / widest);
        }

        return furthest;
    }

    /// <summary>
    /// A turn about the vertical and a translation: the closed-form least-squares solution on the
    /// horizontal, the mean height difference on the vertical.
    /// </summary>
    private static (double[,] Rotation, double[] Translation) SolveHeading(double[][] scan, double[][] local)
    {
        var p = Mean(scan);
        var s = Mean(local);
        double dot = 0, cross = 0;
        for (var i = 0; i < scan.Length; i++)
        {
            var px = scan[i][0] - p[0];
            var py = scan[i][1] - p[1];
            var sx = local[i][0] - s[0];
            var sy = local[i][1] - s[1];
            dot += (sx * px) + (sy * py);
            cross += (sx * py) - (sy * px);
        }

        // Clockwise from north: a tile +Y lands at (sin h, cos h) east and north.
        var heading = Math.Atan2(cross, dot);
        var (sh, ch) = Math.SinCos(heading);
        var rotation = new[,] { { ch, sh, 0 }, { -sh, ch, 0 }, { 0, 0, 1.0 } };
        return (rotation, Sub(s, Apply(rotation, p)));
    }

    /// <summary>
    /// A full rotation and a translation, by the unit-quaternion solution: the rotation is the
    /// eigenvector of the largest eigenvalue of a symmetric four-by-four matrix built from the
    /// points' cross-covariance. It needs no matrix library and never returns a reflection.
    /// </summary>
    private static (double[,] Rotation, double[] Translation) SolveRotation(double[][] scan, double[][] local)
    {
        var p = Mean(scan);
        var s = Mean(local);
        var m = new double[3, 3];
        for (var i = 0; i < scan.Length; i++)
        {
            for (var a = 0; a < 3; a++)
            {
                for (var b = 0; b < 3; b++)
                {
                    m[a, b] += (scan[i][a] - p[a]) * (local[i][b] - s[b]);
                }
            }
        }

        double sxx = m[0, 0], sxy = m[0, 1], sxz = m[0, 2];
        double syx = m[1, 0], syy = m[1, 1], syz = m[1, 2];
        double szx = m[2, 0], szy = m[2, 1], szz = m[2, 2];
        var n = new[,]
        {
            { sxx + syy + szz, syz - szy, szx - sxz, sxy - syx },
            { syz - szy, sxx - syy - szz, sxy + syx, szx + sxz },
            { szx - sxz, sxy + syx, -sxx + syy - szz, syz + szy },
            { sxy - syx, szx + sxz, syz + szy, -sxx - syy + szz },
        };

        var q = LargestEigenvector(n);
        double w = q[0], x = q[1], y = q[2], z = q[3];
        var rotation = new[,]
        {
            { (w * w) + (x * x) - (y * y) - (z * z), 2 * ((x * y) - (w * z)), 2 * ((x * z) + (w * y)) },
            { 2 * ((y * x) + (w * z)), (w * w) - (x * x) + (y * y) - (z * z), 2 * ((y * z) - (w * x)) },
            { 2 * ((z * x) - (w * y)), 2 * ((z * y) + (w * x)), (w * w) - (x * x) - (y * y) + (z * z) },
        };
        return (rotation, Sub(s, Apply(rotation, p)));
    }

    /// <summary>
    /// Heading, pitch and roll, in degrees, of a rotation composed as <see cref="ScanPlacement.Rotation"/>
    /// composes them: row 2 is (sin pitch, cos pitch · sin roll, cos pitch · cos roll) and column 0
    /// is cos pitch · (cos heading, −sin heading, ·).
    /// </summary>
    private static (double Heading, double Pitch, double Roll) Angles(double[,] r)
    {
        var pitch = Math.Asin(Math.Clamp(r[2, 0], -1, 1));
        var roll = Math.Atan2(r[2, 1], r[2, 2]);
        var heading = Math.Atan2(-r[1, 0], r[0, 0]);
        return (heading * 180 / Math.PI, pitch * 180 / Math.PI, roll * 180 / Math.PI);
    }

    /// <summary>Cyclic Jacobi rotations on a small symmetric matrix; the eigenvector of its largest eigenvalue.</summary>
    private static double[] LargestEigenvector(double[,] matrix)
    {
        const int size = 4;
        var a = (double[,])matrix.Clone();
        var v = new double[size, size];
        for (var i = 0; i < size; i++)
        {
            v[i, i] = 1;
        }

        for (var sweep = 0; sweep < 100; sweep++)
        {
            var off = 0.0;
            var scale = 0.0;
            for (var i = 0; i < size; i++)
            {
                scale += a[i, i] * a[i, i];
                for (var j = i + 1; j < size; j++)
                {
                    off += a[i, j] * a[i, j];
                }
            }

            if (off <= 1e-30 * Math.Max(scale, 1e-300))
            {
                break;
            }

            for (var p = 0; p < size; p++)
            {
                for (var q = p + 1; q < size; q++)
                {
                    if (a[p, q] == 0)
                    {
                        continue;
                    }

                    var theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                    var t = Math.Sign(theta == 0 ? 1 : theta) / (Math.Abs(theta) + Math.Sqrt((theta * theta) + 1));
                    var c = 1 / Math.Sqrt((t * t) + 1);
                    var s = t * c;

                    for (var k = 0; k < size; k++)
                    {
                        var akp = a[k, p];
                        var akq = a[k, q];
                        a[k, p] = (c * akp) - (s * akq);
                        a[k, q] = (s * akp) + (c * akq);
                    }

                    for (var k = 0; k < size; k++)
                    {
                        var apk = a[p, k];
                        var aqk = a[q, k];
                        a[p, k] = (c * apk) - (s * aqk);
                        a[q, k] = (s * apk) + (c * aqk);
                    }

                    for (var k = 0; k < size; k++)
                    {
                        var vkp = v[k, p];
                        var vkq = v[k, q];
                        v[k, p] = (c * vkp) - (s * vkq);
                        v[k, q] = (s * vkp) + (c * vkq);
                    }
                }
            }
        }

        var best = 0;
        for (var i = 1; i < size; i++)
        {
            if (a[i, i] > a[best, best])
            {
                best = i;
            }
        }

        var vector = new double[size];
        var norm = 0.0;
        for (var k = 0; k < size; k++)
        {
            vector[k] = v[k, best];
            norm += vector[k] * vector[k];
        }

        norm = Math.Sqrt(norm);
        for (var k = 0; k < size; k++)
        {
            vector[k] /= norm;
        }

        return vector;
    }

    private static EcefPoint Centroid(IReadOnlyList<EcefPoint> points) =>
        new(points.Average(p => p.X), points.Average(p => p.Y), points.Average(p => p.Z));

    private static double[] Mean(double[][] points) =>
        [points.Average(p => p[0]), points.Average(p => p[1]), points.Average(p => p[2])];

    private static double[,] Identity() => new[,] { { 1.0, 0, 0 }, { 0, 1.0, 0 }, { 0, 0, 1.0 } };

    private static double[] Apply(double[,] r, double[] v) =>
    [
        (r[0, 0] * v[0]) + (r[0, 1] * v[1]) + (r[0, 2] * v[2]),
        (r[1, 0] * v[0]) + (r[1, 1] * v[1]) + (r[1, 2] * v[2]),
        (r[2, 0] * v[0]) + (r[2, 1] * v[1]) + (r[2, 2] * v[2]),
    ];

    private static double[] Sub(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];

    private static double[] Cross(double[] a, double[] b) =>
        [(a[1] * b[2]) - (a[2] * b[1]), (a[2] * b[0]) - (a[0] * b[2]), (a[0] * b[1]) - (a[1] * b[0])];

    private static double Norm(double[] a) => Math.Sqrt((a[0] * a[0]) + (a[1] * a[1]) + (a[2] * a[2]));

    private static double Distance(double[] a, double[] b) => Norm(Sub(a, b));
}
