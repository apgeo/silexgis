// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>A direction or a position in a scene's own frame: metres east, north and up.</summary>
public readonly record struct SceneVector(double X, double Y, double Z)
{
    public double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

    public static SceneVector operator +(SceneVector a, SceneVector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static SceneVector operator -(SceneVector a, SceneVector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static SceneVector operator *(SceneVector a, double factor) => new(a.X * factor, a.Y * factor, a.Z * factor);

    public double Dot(SceneVector other) => (X * other.X) + (Y * other.Y) + (Z * other.Z);

    public SceneVector Cross(SceneVector other) => new(
        (Y * other.Z) - (Z * other.Y),
        (Z * other.X) - (X * other.Z),
        (X * other.Y) - (Y * other.X));

    /// <summary>The same direction, one metre long.</summary>
    public SceneVector Normalized()
    {
        var length = Length;
        return length > 0 ? this * (1 / length) : this;
    }
}

/// <summary>
/// A camera in a scene's frame: where it stands, the way it looks and where the top of its
/// picture points. The two directions are one metre long and at right angles.
/// </summary>
public readonly record struct SceneView(SceneVector Eye, SceneVector Forward, SceneVector Up);

/// <summary>
/// Where a model drawn about a point of its own stands in a scene: its zero point, and the turn
/// that brings its east, north and up onto the scene's — about <see cref="Axis"/>, by
/// <see cref="Degrees"/>, right-handed.
/// </summary>
public readonly record struct ScenePlacement(SceneVector Translation, SceneVector Axis, double Degrees);

/// <summary>
/// The frame a three-dimensional scene is laid out in when it leaves the globe: the east-north-up
/// plane touching the ellipsoid at one chosen point, in metres from that point.
///
/// <para>
/// A camera path over a globe is a list of longitudes, latitudes and heights with a compass heading
/// and a tilt at each; a renderer that knows no globe wants metres in one flat frame and a turn of
/// the camera in that frame. This is the one place the first is made into the second. It is exact
/// geodesy rather than a flat approximation, so it holds as well for a camera a thousand
/// kilometres up as for one inside a passage: what is far from the chosen point sinks below the
/// plane by the curve of the Earth, and its own "up" leans by the same curve — both of which are
/// what the globe showed.
/// </para>
///
/// <para>
/// <b>Relief.</b> A scene may be drawn with every height multiplied by one factor. The factor is
/// applied about the ellipsoid, as the globe applies it: a point at height <c>h</c> stands at
/// <c>factor × h</c> on its own vertical. A model keeps its true size along the ground and is
/// stretched along its own up by the same factor, which is why a placement carries no scale — the
/// caller applies the factor it built the frame with.
/// </para>
///
/// <para>
/// <b>The frame names no place.</b> Nothing it hands back says where on the Earth its zero is: the
/// origin is the zero vector, and positions are differences from it.
/// </para>
/// </summary>
public sealed class SceneTangentFrame
{
    private readonly LocalTangentFrame origin;

    /// <param name="longitude">Degrees east of the frame's zero, WGS 84.</param>
    /// <param name="latitude">Degrees north of it.</param>
    /// <param name="heightM">Its true height in metres.</param>
    /// <param name="relief">How many times taller than true every height is drawn.</param>
    public SceneTangentFrame(double longitude, double latitude, double heightM, double relief = 1)
    {
        if (!(relief > 0) || !double.IsFinite(relief))
        {
            throw new ArgumentOutOfRangeException(nameof(relief), relief, "The relief factor is a positive number.");
        }

        Relief = relief;
        origin = new LocalTangentFrame(longitude, latitude, heightM * relief);
    }

    /// <summary>How many times taller than true every height is drawn.</summary>
    public double Relief { get; }

    /// <summary>A point of the globe, at its true height, in the scene.</summary>
    public SceneVector PointAt(double longitude, double latitude, double heightM)
    {
        var (east, north, up) = origin.ToLocal(longitude, latitude, heightM * Relief);
        return new SceneVector(east, north, up);
    }

    /// <summary>
    /// A camera of the globe in the scene.
    /// </summary>
    /// <param name="headingDeg">Compass direction faced, degrees clockwise from north.</param>
    /// <param name="pitchDeg">Tilt, degrees above the horizon: negative looks down, −90 straight down.</param>
    /// <param name="rollDeg">
    /// Turn about the line of sight, degrees, right-handed about the direction looked in: positive
    /// lowers the right-hand side of the camera.
    /// </param>
    /// <remarks>
    /// The heading and the tilt are taken at the camera's own place — north and the horizon as they
    /// are where it stands — and then carried into the scene. Looking straight down, the heading is
    /// which way the top of the picture points, so nothing is lost there.
    /// </remarks>
    public SceneView ViewAt(
        double longitude, double latitude, double heightM, double headingDeg, double pitchDeg, double rollDeg)
    {
        var (sinH, cosH) = Math.SinCos(headingDeg * Math.PI / 180);
        var (sinP, cosP) = Math.SinCos(pitchDeg * Math.PI / 180);
        var (sinR, cosR) = Math.SinCos(rollDeg * Math.PI / 180);

        // In the camera's own east-north-up.
        var forward = new SceneVector(sinH * cosP, cosH * cosP, sinP);
        var level = new SceneVector(-sinH * sinP, -cosH * sinP, cosP);
        var right = forward.Cross(level);
        var up = (level * cosR) + (right * sinR);

        var here = new LocalTangentFrame(longitude, latitude, heightM * Relief);
        return new SceneView(
            PointAt(longitude, latitude, heightM),
            Carried(here, forward).Normalized(),
            Carried(here, up).Normalized());
    }

    /// <summary>
    /// Where a model stands whose own zero is at this point of the globe, drawn at
    /// <paramref name="drawnHeightM"/> metres of true height, and whose own axes are east, north and
    /// up there.
    /// </summary>
    public ScenePlacement PlacementAt(double longitude, double latitude, double drawnHeightM)
    {
        var there = new LocalTangentFrame(longitude, latitude, drawnHeightM * Relief);
        var x = Carried(there, new SceneVector(1, 0, 0));
        var y = Carried(there, new SceneVector(0, 1, 0));
        var z = Carried(there, new SceneVector(0, 0, 1));
        var (axis, degrees) = AxisAngle(x, y, z);
        return new ScenePlacement(PointAt(longitude, latitude, drawnHeightM), axis, degrees);
    }

    /// <summary>
    /// The angle a picture covers from its left edge to its right, in degrees, for a lens stated
    /// across the picture's wider side — which is how a globe states one.
    /// </summary>
    public static double HorizontalFieldOfView(double widerSideDegrees, int width, int height)
    {
        if (width >= height)
        {
            return widerSideDegrees;
        }

        var half = Math.Tan(widerSideDegrees * Math.PI / 360) * width / height;
        return 2 * Math.Atan(half) * 180 / Math.PI;
    }

    /// <summary>The focal length that covers a horizontal angle on a sensor of a given width, in the sensor's unit.</summary>
    public static double FocalLength(double horizontalDegrees, double sensorWidth) =>
        sensorWidth / (2 * Math.Tan(horizontalDegrees * Math.PI / 360));

    /// <summary>A direction of another frame, as a direction of this scene.</summary>
    private SceneVector Carried(LocalTangentFrame from, SceneVector direction)
    {
        var (x, y, z) = from.DirectionToEcef(direction.X, direction.Y, direction.Z);
        var (east, north, up) = origin.DirectionFromEcef(x, y, z);
        return new SceneVector(east, north, up);
    }

    /// <summary>The turn whose matrix has these three columns, as an axis and an angle.</summary>
    private static (SceneVector Axis, double Degrees) AxisAngle(SceneVector x, SceneVector y, SceneVector z)
    {
        // The antisymmetric part of the matrix is the axis times twice the sine of the angle; the
        // trace gives the cosine. Taking the angle from both keeps it exact for the very small
        // turns this is almost always asked about, where the cosine alone is flat.
        var axis = new SceneVector(y.Z - z.Y, z.X - x.Z, x.Y - y.X);
        var sine = axis.Length / 2;
        var cosine = (x.X + y.Y + z.Z - 1) / 2;
        var angle = Math.Atan2(sine, cosine);
        if (sine < 1e-15)
        {
            // No turn at all (two frames on the same vertical): any axis will do.
            return (new SceneVector(0, 0, 1), 0);
        }

        return (axis.Normalized(), angle * 180 / Math.PI);
    }
}

/// <summary>
/// How far above or below its surveyed altitude a cave is drawn in a three-dimensional scene.
///
/// <para>
/// A scene with relief draws a survey where it was surveyed, raised by whatever the elevation
/// model's own height datum asks. A scene on the bare ellipsoid has no hillside to put it in, and
/// hangs the cave from the surface by its highest point instead. Everything drawn of one cave has
/// to be moved by the same number, or its walls and its survey lines part company — so the number
/// is worked out here, once, for whatever is drawn.
/// </para>
/// </summary>
public static class SceneAltitude
{
    /// <summary>The metres added to every surveyed altitude of one cave.</summary>
    /// <param name="absolute">Whether the scene draws surveys where they were surveyed.</param>
    /// <param name="offsetM">What such a scene adds to a surveyed altitude.</param>
    /// <param name="surveyTopM">The surveyed altitude of the cave's highest point, which the other kind of scene hangs it from.</param>
    public static double DrawnShift(bool absolute, double offsetM, double surveyTopM) =>
        absolute ? offsetM : -surveyTopM;
}
