// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Infrastructure.Geodata;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// What the person uploading a survey had to tell us, because the file could not.
///
/// <para>
/// An STL carries eighty bytes of free text, a triangle count and triangles. Nothing in it says
/// what its numbers mean — and measurement across real exports from the same toolchain found some
/// written in metres about a fixed station and others in a projected national grid, with no way to
/// tell from the artifact which. Two of them were in *different* grids. The compiled line-plot
/// formats are only a little better: one of them has a field naming its coordinate system, which an
/// export fills in only when the survey was compiled with one declared, and the other format has no
/// such field at all — so a file of the second kind never states where it is, and a file of the
/// first kind may not. So the convention is declared, never guessed: a wrong guess puts a cave
/// hundreds of kilometres from where it is, and puts it there silently.
/// </para>
/// </summary>
/// <param name="SourceEpsg">
/// The projected system the file's X and Y are in, or null when they are plain metres about a point
/// the uploader names.
/// </param>
/// <param name="OriginLongitude">Where the file's own zero point is, for a local file.</param>
/// <param name="OriginLatitude">Where the file's own zero point is, for a local file.</param>
/// <param name="OriginHeightM">
/// The altitude the file's Z = 0 plane sits at.
///
/// <para>
/// Asked for rather than read, because Z in these files is not an altitude: across every real
/// sample measured, the highest Z anywhere was 38 m, in a country whose caves are between 200 and
/// 2000. The exports are written about a station fixed at zero height, so the plane they are
/// measured from is knowledge that only the surveyor has.
/// </para>
/// </param>
public sealed record SurveySourceDeclaration(
    int? SourceEpsg,
    double? OriginLongitude,
    double? OriginLatitude,
    double OriginHeightM);

/// <summary>
/// Where a survey file's own numbers sit in the world: which point the file is written about, where
/// in the world that point is, and how far the grid's north is from true north there.
///
/// <para>
/// This exists so that placement is decided once per file and then asked for, rather than worked
/// out again by every stage that needs a position. Grid north is not true north anywhere but on the
/// projection's central meridian, and the difference grows with distance from the anchor — right in
/// the middle, wrong at the ends. The turn is applied here and recorded as already applied, so
/// there is no second place it can be applied, forgotten, or applied twice; over one sample cave it
/// is 3.3 m and over another 18.3 m, which is a discrepancy nothing on screen would explain.
/// </para>
///
/// <para>
/// Points come out as longitude, latitude and altitude by offsetting from the anchor along the
/// ellipsoid's own radii of curvature at that latitude. That is a first-order expansion, so it is
/// exact at the anchor and loses about a millimetre over five kilometres and a couple of
/// centimetres over twenty — far below survey accuracy, and unlike a constant metres-per-degree it
/// does not drift by hundreds of metres between latitudes. It is deliberately not a second
/// projection: the file's own coordinates are converted by the shared projector exactly once, at
/// the anchor, and everything else is a local offset from that answer.
/// </para>
///
/// <para>
/// A metre on a projected grid is not a metre on the ground, and that difference dwarfs the
/// expansion error above: a transverse Mercator is shrunk on its central meridian on purpose so
/// that it is not stretched too far at the edge of its zone, by 400 parts per million for UTM and
/// for the British grid alike. Left uncorrected that is 40 cm per kilometre from the anchor and
/// metres across a large system, all of it systematic. So the grid's own scale at the anchor is
/// measured — by asking the projector where a known step along the grid actually lands — and the
/// offsets are converted from grid metres into ground metres before they become degrees. A file in
/// plain metres about a fixed station is already in ground metres and is scaled by nothing.
/// </para>
/// </summary>
public sealed class SurveyPlacement
{
    // WGS 84, the system every stored geometry here is in.
    private const double EquatorialRadiusM = 6_378_137.0;
    private const double Flattening = 1.0 / 298.257223563;
    private const double EccentricitySquared = Flattening * (2.0 - Flattening);

    /// <summary>
    /// How far along the grid the scale of the grid is measured over. The same distance the
    /// convergence is read at, and for the same reasons: long enough that the transform's own
    /// rounding is negligible against it, short enough to stay inside one cave's footprint.
    /// </summary>
    private const double GridStepM = 100.0;

    /// <summary>
    /// How far outside a grid scale factor may plausibly be from one before it is a broken
    /// transform rather than a projection. Real projections stay within a few thousand parts per
    /// million; a percent is already far past any of them.
    /// </summary>
    private const double MaxGridScaleDeviation = 0.01;

    private readonly double turnCos;
    private readonly double turnSin;
    private readonly double degreesPerMetreNorth;
    private readonly double degreesPerMetreEast;
    private readonly double gridMetreInGroundMetres;

    private SurveyPlacement(
        (double X, double Y, double Z) origin,
        ProjectedPoint anchor,
        double originHeightM,
        ProjectedPoint? gridStepNorth = null)
    {
        Origin = origin;
        Anchor = anchor;
        OriginHeightM = originHeightM;

        var radians = AppliedRotationDeg * Math.PI / 180.0;
        turnCos = Math.Cos(radians);
        turnSin = Math.Sin(radians);

        var latitude = anchor.Latitude * Math.PI / 180.0;
        var sinLatitude = Math.Sin(latitude);
        var w = 1.0 - (EccentricitySquared * sinLatitude * sinLatitude);
        var meridianRadius = EquatorialRadiusM * (1.0 - EccentricitySquared) / (w * Math.Sqrt(w));
        var primeVerticalRadius = EquatorialRadiusM / Math.Sqrt(w);

        degreesPerMetreNorth = 180.0 / (Math.PI * meridianRadius);
        degreesPerMetreEast = 180.0 / (Math.PI * primeVerticalRadius * Math.Cos(latitude));
        gridMetreInGroundMetres = GroundPerGridMetre(gridStepNorth);
    }

    /// <summary>
    /// How many ground metres one grid metre spans at the anchor, read from where a known step
    /// along the grid actually came out. One for a file that was never on a grid, and one again for
    /// an answer so far from it that the transform, rather than the projection, is what produced
    /// it — leaving the coordinates as they were is a bounded error, while trusting a nonsense
    /// scale is not.
    /// </summary>
    private double GroundPerGridMetre(ProjectedPoint? gridStepNorth)
    {
        if (gridStepNorth is not { } step)
        {
            return 1.0;
        }

        var east = (step.Longitude - Anchor.Longitude) / degreesPerMetreEast;
        var north = (step.Latitude - Anchor.Latitude) / degreesPerMetreNorth;
        var ground = Math.Sqrt((east * east) + (north * north)) / GridStepM;

        return double.IsFinite(ground) && Math.Abs(ground - 1.0) <= MaxGridScaleDeviation ? ground : 1.0;
    }

    /// <summary>The point, in the file's own coordinates, that the file is written about.</summary>
    public (double X, double Y, double Z) Origin { get; }

    /// <summary>Where <see cref="Origin"/> is in the world.</summary>
    public ProjectedPoint Anchor { get; }

    /// <summary>The altitude the file's Z = 0 plane sits at.</summary>
    public double OriginHeightM { get; }

    /// <summary>
    /// The turn from grid north to true north that has already been applied to every point this
    /// placement produced. Recorded for the audit trail; applying it again turns the cave twice.
    /// </summary>
    public double AppliedRotationDeg => -Anchor.ConvergenceDeg;

    /// <summary>
    /// Decides where a file sits from what its uploader declared.
    /// </summary>
    /// <param name="footprintCentre">
    /// The middle of the file's own footprint, in its own coordinates. Asked for lazily because a
    /// local file does not need it, and because computing it means walking every point of the file.
    /// </param>
    /// <exception cref="SurveySourceException">
    /// The declaration cannot be used: a local file with no position for its zero point, a
    /// coordinate system this installation cannot resolve, or coordinates that fall off the world
    /// when read as the system declared.
    /// </exception>
    public static SurveyPlacement Resolve(
        ICoordinateProjector projector,
        SurveySourceDeclaration declaration,
        Func<(double X, double Y)> footprintCentre)
    {
        ArgumentNullException.ThrowIfNull(projector);
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(footprintCentre);

        if (declaration.SourceEpsg is not { } epsg)
        {
            // Local metres. The file's own zero is the fixed station the survey was tied to, so it
            // is the point the uploader gave a position for — not the middle of the file, which is
            // wherever the passages happen to average out to.
            if (declaration.OriginLongitude is not { } lon || declaration.OriginLatitude is not { } lat)
            {
                throw new SurveySourceException(
                    "This file's coordinates are local, so it needs the position its zero point sits at.");
            }

            return new SurveyPlacement((0, 0, 0), new ProjectedPoint(lon, lat, 0), declaration.OriginHeightM);
        }

        // Projected. The file already knows where it is; what it needs is a point close enough to
        // the geometry that everything else can be written as an offset from it without losing
        // precision. The centre of its own footprint is the closest such point to every part of it.
        var (centreX, centreY) = footprintCentre();

        var anchor = projector.ToWgs84(epsg, centreX, centreY)
            ?? throw new SurveySourceException(
                $"EPSG:{epsg} is not a coordinate system this installation can resolve, so the "
                + "file's coordinates cannot be placed.");

        if (Math.Abs(anchor.Latitude) > 90 || Math.Abs(anchor.Longitude) > 180)
        {
            throw new SurveySourceException(
                $"Read as EPSG:{epsg}, this file's coordinates fall outside the world. Check the "
                + "coordinate system the survey was exported in.");
        }

        // Where a step due grid-north from the anchor actually lands, which is what says how much
        // of a ground metre a grid metre is here. A projector that cannot answer for the stepped
        // point leaves the scale unmeasured rather than failing the file: the anchor itself is
        // already placed, and an uncorrected grid is the answer this had before it was measured
        // at all.
        var stepped = projector.ToWgs84(epsg, centreX, centreY + GridStepM);

        return new SurveyPlacement((centreX, centreY, 0), anchor, declaration.OriginHeightM, stepped);
    }

    /// <summary>
    /// The EPSG code a coordinate-system string names, or null when it names none that can be read
    /// here.
    ///
    /// <para>
    /// One of the two line-plot formats can state the system its coordinates are in, and it states
    /// it as free text meant for a projection library rather than as a number — real files carry
    /// both "EPSG:31700" and "+init=epsg:27700 +no_defs", and a string that describes a projection
    /// by its parameters instead of by a code names nothing this can use. So the code is picked out
    /// of the text where there is one, and the answer is null where there is not: a file that only
    /// describes its projection has to be told where it sits, the same as one that says nothing.
    /// </para>
    /// </summary>
    public static int? EpsgFrom(string? coordinateSystem)
    {
        if (string.IsNullOrWhiteSpace(coordinateSystem))
        {
            return null;
        }

        var text = coordinateSystem.AsSpan();
        var at = text.IndexOf("epsg", StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return null;
        }

        var rest = text[(at + 4)..];
        while (rest.Length > 0 && (rest[0] is ':' or '=' or ' '))
        {
            rest = rest[1..];
        }

        var digits = 0;
        while (digits < rest.Length && char.IsAsciiDigit(rest[digits]))
        {
            digits++;
        }

        return digits > 0 && int.TryParse(rest[..digits], out var code) && code > 0 ? code : null;
    }

    /// <summary>Turns a point about the origin, from grid north onto true north.</summary>
    public (double X, double Y) Turn(double x, double y)
    {
        var dx = x - Origin.X;
        var dy = y - Origin.Y;
        return (
            Origin.X + ((dx * turnCos) - (dy * turnSin)),
            Origin.Y + ((dx * turnSin) + (dy * turnCos)));
    }

    /// <summary>
    /// Where a point of the file is in the world: longitude, latitude, and altitude in metres.
    /// </summary>
    public (double Longitude, double Latitude, double AltitudeM) ToWorld(double x, double y, double z)
    {
        var (turnedX, turnedY) = Turn(x, y);

        // Grid metres out of the file, ground metres into the ellipsoid's radii. The two are not
        // the same unit on a projected grid, and the ratio between them is the same everywhere in
        // one cave, so it is measured once at the anchor and applied here.
        var east = (turnedX - Origin.X) * gridMetreInGroundMetres;
        var north = (turnedY - Origin.Y) * gridMetreInGroundMetres;

        return (
            Anchor.Longitude + (east * degreesPerMetreEast),
            Anchor.Latitude + (north * degreesPerMetreNorth),
            OriginHeightM + z);
    }
}
