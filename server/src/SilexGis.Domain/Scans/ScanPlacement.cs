// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Scans;

/// <summary>How a scan came to stand where it does.</summary>
public enum ScanPlacementSource : short
{
    /// <summary>Somebody put it there: on an entrance and turned by a bearing, or corrected by hand.</summary>
    Manual = 0,

    /// <summary>
    /// Its file named a coordinate system, and the tiles' own zero point was carried into the
    /// world through it when the tiles were made. Corrected by hand it becomes manual, and is
    /// then left alone by every later tiling.
    /// </summary>
    FileCrs = 1,

    /// <summary>
    /// Fitted to pairs of a point picked in the scan and the survey station it stands on: the
    /// translation and heading (and, when asked for, a small tilt) that bring the picked points
    /// closest to their stations, with the residual of every pair kept beside it. Left alone by
    /// every later tiling, like a hand placement.
    /// </summary>
    ControlPoints = 2,

    /// <summary>
    /// A first placement taken from the satellite fixes the scanner logged before it went
    /// underground. Made only for a scan that had no placement; left alone by every later tiling,
    /// and meant to be refined by control points.
    /// </summary>
    Gnss = 3,
}

/// <summary>
/// Where a point cloud's tiles stand on the earth, and which way they face.
///
/// <para>
/// A scan is tiled once, in its own frame: the worker subtracts one point of the cloud from every
/// coordinate and the tiles hold offsets from that point and nothing else. Placing the scan never
/// touches the tiles; it says where that zero point is in the world (<see cref="Longitude"/>,
/// <see cref="Latitude"/>, <see cref="HeightM"/>) and how the frame is turned about it
/// (<see cref="HeadingDeg"/>). A scene composes the two into the matrix it draws the tiles by,
/// so a correction is a changed row, not a second tiling — a wall mesh, by contrast, has its turn
/// baked into its vertices by its conversion, which is why a mesh is placed once, at upload, and
/// a scan can be placed again at any time.
/// </para>
///
/// <para>
/// This is the cave's position by another name. It is written only by callers who may write the
/// cave and already hold its exact location, and it is read out only on the paths that withhold
/// the cave's position from everybody else.
/// </para>
/// </summary>
/// <param name="Longitude">Where the tiles' zero point sits, degrees east, WGS 84.</param>
/// <param name="Latitude">Where the tiles' zero point sits, degrees north, WGS 84.</param>
/// <param name="HeightM">The altitude the tiles' zero plane sits at, in metres.</param>
/// <param name="HeadingDeg">
/// The compass bearing the tiles' own +Y axis points along: degrees clockwise from true north,
/// in [0, 360). Zero means the frame's north is true north. For a file tiled in a projected grid
/// this is the grid's convergence at the zero point — grid north is a degree or two off true
/// north away from the central meridian, and the tiles keep grid north because nothing rewrote
/// them.
/// </param>
/// <param name="Source">How the placement was arrived at.</param>
/// <param name="PitchDeg">
/// A small tilt that raises the frame's +X end, in degrees; zero for a level scan. A scanner with
/// an inertial unit levels its own recording, so this is a correction of a degree or so that a fit
/// against surveyed stations may find, never a free orientation.
/// </param>
/// <param name="RollDeg">A small tilt that raises the frame's +Y end, in degrees; zero for a level scan.</param>
/// <remarks>
/// The three angles compose the way a 3D globe composes a heading, pitch and roll: the tile frame
/// is rolled about its +X axis, then pitched about its +Y axis, then turned clockwise about the
/// vertical by the heading, and the result is set on the east-north-up frame at the zero point.
/// <see cref="Rotation"/> is that composition written out, so a fit, a check and the scene that
/// draws the tiles all mean the same thing by the same three numbers.
/// </remarks>
public sealed record ScanPlacement(
    double Longitude,
    double Latitude,
    double HeightM,
    double HeadingDeg,
    ScanPlacementSource Source,
    double PitchDeg = 0,
    double RollDeg = 0)
{
    /// <summary>
    /// The lowest altitude a zero plane may be declared at: the Dead Sea shore, with room to spare.
    /// Shared with the wall-mesh declaration, which bounds the same quantity.
    /// </summary>
    public const double LowestHeightM = -500;

    /// <summary>The highest: above every cave entrance on earth, below the summits nobody scans.</summary>
    public const double HighestHeightM = 9000;

    /// <summary>A bearing brought into [0, 360), so that 360 and -90 are stored as 0 and 270.</summary>
    public static double NormalizeHeading(double headingDeg)
    {
        var turned = headingDeg % 360.0;
        if (turned < 0)
        {
            turned += 360.0;
        }

        // -0.0 and a value that rounded back up to 360 both mean north.
        return turned >= 360.0 || turned == 0.0 ? 0.0 : turned;
    }

    /// <summary>Whether a longitude, latitude and height could be a place on earth a cave is at.</summary>
    public static bool IsOnEarth(double longitude, double latitude, double heightM) =>
        double.IsFinite(longitude) && Math.Abs(longitude) <= 180
        && double.IsFinite(latitude) && Math.Abs(latitude) <= 90
        && double.IsFinite(heightM) && heightM >= LowestHeightM && heightM <= HighestHeightM;

    /// <summary>
    /// A placement somebody made by hand: the zero point put at a position — an entrance, as a
    /// rule — and the frame turned to a bearing.
    /// </summary>
    public static ScanPlacement Manual(double longitude, double latitude, double heightM, double headingDeg) =>
        new(longitude, latitude, heightM, NormalizeHeading(headingDeg), ScanPlacementSource.Manual);

    /// <summary>
    /// The placement a file's own coordinate system gives: the tiles' zero point, transformed
    /// into the world, and the frame left pointing at grid north — which is
    /// <paramref name="convergenceDeg"/> clockwise of true north at that point. The height is
    /// the zero point's own Z, which for a georeferenced file is an altitude in the file's
    /// vertical system; nothing better is known without a vertical transform.
    /// </summary>
    public static ScanPlacement FromFileCrs(double longitude, double latitude, double heightM, double convergenceDeg) =>
        new(longitude, latitude, heightM, NormalizeHeading(convergenceDeg), ScanPlacementSource.FileCrs);

    /// <summary>
    /// The rotation from the tile frame to east-north-up at the zero point, row by row: a tile
    /// offset (x, y, z) lands at east = row 0 · (x, y, z), north = row 1 · …, up = row 2 · ….
    /// </summary>
    public double[,] Rotation()
    {
        var (sh, ch) = Math.SinCos(HeadingDeg * Math.PI / 180);
        var (sp, cp) = Math.SinCos(PitchDeg * Math.PI / 180);
        var (sr, cr) = Math.SinCos(RollDeg * Math.PI / 180);
        return new[,]
        {
            { ch * cp, (-ch * sp * sr) + (sh * cr), (-ch * sp * cr) - (sh * sr) },
            { -sh * cp, (sh * sp * sr) + (ch * cr), (sh * sp * cr) - (ch * sr) },
            { sp, cp * sr, cp * cr },
        };
    }

    /// <summary>
    /// Where a point of the tiles stands on the earth under this placement: the offset, in metres
    /// from the tiles' zero point, turned by the placement and carried out from the zero point.
    /// </summary>
    public EcefPoint EcefOf(double x, double y, double z)
    {
        var r = Rotation();
        var frame = new LocalTangentFrame(Longitude, Latitude, HeightM);
        return frame.EcefAt(
            (r[0, 0] * x) + (r[0, 1] * y) + (r[0, 2] * z),
            (r[1, 0] * x) + (r[1, 1] * y) + (r[1, 2] * z),
            (r[2, 0] * x) + (r[2, 1] * y) + (r[2, 2] * z));
    }

    /// <summary>The same as <see cref="EcefOf"/>, as longitude, latitude and height.</summary>
    public (double Longitude, double Latitude, double HeightM) PositionOf(double x, double y, double z) =>
        LocalTangentFrame.ToGeodetic(EcefOf(x, y, z));

    /// <summary>
    /// The point of the file's own frame the tiles are written about, once tiled: what the
    /// worker subtracted. Null before tiling — there is no frame to place until there are tiles,
    /// and the worker chooses the point, so nothing read from the header can stand in for it.
    /// </summary>
    public static (double X, double Y, double Z)? TilesZeroPoint(PointCloudFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return facts.OriginShift is { Length: 3 } shift && shift.All(double.IsFinite)
            ? (shift[0], shift[1], shift[2])
            : null;
    }
}
