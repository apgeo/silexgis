// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>A point in earth-centred, earth-fixed coordinates on the WGS 84 ellipsoid, in metres.</summary>
public readonly record struct EcefPoint(double X, double Y, double Z)
{
    /// <summary>The straight-line distance to another point, in metres.</summary>
    public double DistanceTo(EcefPoint other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        var dz = Z - other.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }
}

/// <summary>
/// The east-north-up frame touching the WGS 84 ellipsoid at one point: metres east, metres north
/// and metres up along the ellipsoid's normal, from that point.
///
/// <para>
/// This is the frame a 3D globe draws a local model in — a scan's tiles are offsets in metres, and
/// the globe stands them on the earth by exactly this construction at the scan's zero point — so a
/// fit that has to agree with what the globe draws works here, with the ellipsoid's own figures,
/// rather than on a sphere or in a projected grid. A grid would add its scale factor and its
/// convergence to every distance and bearing; a sphere puts the up direction a fraction of a degree
/// off the globe's, which over a hundred-metre cave is centimetres of disagreement for no reason.
/// </para>
///
/// <para>
/// Heights pass through unchanged: whatever kind of height the caller gives (an orthometric survey
/// altitude, as a rule), it gets back the same kind. Across the extent of one cave the geoid is
/// flat to millimetres, so treating an altitude as a height above the ellipsoid for the purpose of
/// a local frame moves nothing that matters, provided the caller converts back the same way.
/// </para>
/// </summary>
public sealed class LocalTangentFrame
{
    /// <summary>WGS 84 semi-major axis, metres.</summary>
    public const double SemiMajorAxis = 6_378_137.0;

    /// <summary>WGS 84 flattening.</summary>
    public const double Flattening = 1 / 298.257223563;

    private static readonly double EccentricitySquared = Flattening * (2 - Flattening);

    private readonly EcefPoint origin;
    private readonly double eastX, eastY;
    private readonly double northX, northY, northZ;
    private readonly double upX, upY, upZ;

    /// <summary>The frame touching the ellipsoid below a point, with its origin at that point.</summary>
    public LocalTangentFrame(double longitude, double latitude, double heightM)
    {
        Longitude = longitude;
        Latitude = latitude;
        HeightM = heightM;
        origin = ToEcef(longitude, latitude, heightM);

        var lambda = longitude * Math.PI / 180;
        var phi = latitude * Math.PI / 180;
        var (sinL, cosL) = Math.SinCos(lambda);
        var (sinP, cosP) = Math.SinCos(phi);

        eastX = -sinL;
        eastY = cosL;
        northX = -sinP * cosL;
        northY = -sinP * sinL;
        northZ = cosP;
        upX = cosP * cosL;
        upY = cosP * sinL;
        upZ = sinP;
    }

    /// <summary>Degrees east of the origin, WGS 84.</summary>
    public double Longitude { get; }

    /// <summary>Degrees north of the origin, WGS 84.</summary>
    public double Latitude { get; }

    /// <summary>The origin's height, in whatever kind the caller gave.</summary>
    public double HeightM { get; }

    /// <summary>A geodetic position in earth-centred coordinates.</summary>
    public static EcefPoint ToEcef(double longitude, double latitude, double heightM)
    {
        var (sinL, cosL) = Math.SinCos(longitude * Math.PI / 180);
        var (sinP, cosP) = Math.SinCos(latitude * Math.PI / 180);
        var n = SemiMajorAxis / Math.Sqrt(1 - (EccentricitySquared * sinP * sinP));
        return new(
            (n + heightM) * cosP * cosL,
            (n + heightM) * cosP * sinL,
            ((n * (1 - EccentricitySquared)) + heightM) * sinP);
    }

    /// <summary>
    /// An earth-centred point back to longitude, latitude and height. Iterated to well under a
    /// micrometre for anything within a few kilometres of the surface; the height is taken from
    /// whichever of the two formulas is well conditioned at the latitude, so the poles and the
    /// equator both come back exactly.
    /// </summary>
    public static (double Longitude, double Latitude, double HeightM) ToGeodetic(EcefPoint point)
    {
        var p = Math.Sqrt((point.X * point.X) + (point.Y * point.Y));
        var longitude = Math.Atan2(point.Y, point.X);
        var latitude = Math.Atan2(point.Z, p * (1 - EccentricitySquared));
        var height = 0.0;

        for (var i = 0; i < 10; i++)
        {
            var (sinP, cosP) = Math.SinCos(latitude);
            var n = SemiMajorAxis / Math.Sqrt(1 - (EccentricitySquared * sinP * sinP));
            height = Math.Abs(cosP) > Math.Abs(sinP)
                ? (p / cosP) - n
                : (point.Z / sinP) - (n * (1 - EccentricitySquared));
            var next = Math.Atan2(point.Z, p * (1 - (EccentricitySquared * n / (n + height))));
            var settled = Math.Abs(next - latitude) < 1e-15;
            latitude = next;
            if (settled)
            {
                break;
            }
        }

        return (longitude * 180 / Math.PI, latitude * 180 / Math.PI, height);
    }

    /// <summary>An earth-centred point in this frame: metres east, north and up of the origin.</summary>
    public (double East, double North, double Up) ToLocal(EcefPoint point)
    {
        var dx = point.X - origin.X;
        var dy = point.Y - origin.Y;
        var dz = point.Z - origin.Z;
        return (
            (eastX * dx) + (eastY * dy),
            (northX * dx) + (northY * dy) + (northZ * dz),
            (upX * dx) + (upY * dy) + (upZ * dz));
    }

    /// <summary>A geodetic position in this frame.</summary>
    public (double East, double North, double Up) ToLocal(double longitude, double latitude, double heightM) =>
        ToLocal(ToEcef(longitude, latitude, heightM));

    /// <summary>A point of this frame as an earth-centred point.</summary>
    public EcefPoint EcefAt(double east, double north, double up) =>
        new(
            origin.X + (eastX * east) + (northX * north) + (upX * up),
            origin.Y + (eastY * east) + (northY * north) + (upY * up),
            origin.Z + (northZ * north) + (upZ * up));

    /// <summary>A point of this frame as longitude, latitude and height.</summary>
    public (double Longitude, double Latitude, double HeightM) GeodeticAt(double east, double north, double up) =>
        ToGeodetic(EcefAt(east, north, up));
}
