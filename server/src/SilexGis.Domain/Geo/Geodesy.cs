// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;

namespace SilexGis.Domain.Geo;

/// <summary>
/// Distances on the ellipsoid, close enough for the ranges this application asks about.
///
/// <para>
/// Duplicate detection asks "is there something within fifty metres", not "how far is it to
/// the metre", and it asks it once per candidate against every nearby feature. The haversine
/// formula on a mean-radius sphere is accurate to about 0.3% — a sixth of a metre at fifty —
/// which is far inside the error of the GPS fix being compared, and it needs no projection,
/// no database round trip and no PROJ call.
/// </para>
/// </summary>
public static class Geodesy
{
    /// <summary>Mean Earth radius (IUGG), metres.</summary>
    public const double EarthRadiusMeters = 6_371_008.8;

    /// <summary>Great-circle distance between two lon/lat points, in metres.</summary>
    public static double DistanceMeters(double lon1, double lat1, double lon2, double lat2)
    {
        var phi1 = DegreesToRadians(lat1);
        var phi2 = DegreesToRadians(lat2);
        var deltaPhi = DegreesToRadians(lat2 - lat1);
        var deltaLambda = DegreesToRadians(lon2 - lon1);

        var a = (Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2))
            + (Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2));
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>Great-circle distance between two 4326 coordinates, in metres.</summary>
    public static double DistanceMeters(Coordinate a, Coordinate b) =>
        DistanceMeters(a.X, a.Y, b.X, b.Y);

    /// <summary>
    /// An envelope grown by a distance in metres. The longitude margin widens with latitude,
    /// because a degree of longitude is shorter the further from the equator it is measured —
    /// growing both axes by the same number of degrees would search a band too narrow to
    /// contain everything within the radius over Romanian karst, and far too narrow near the
    /// poles.
    /// </summary>
    public static Envelope ExpandedBy(Envelope envelope, double meters)
    {
        var latitudeMargin = meters / (EarthRadiusMeters * Math.PI / 180);
        var widestLatitude = Math.Min(89.5, Math.Max(Math.Abs(envelope.MinY), Math.Abs(envelope.MaxY)));
        var longitudeMargin = latitudeMargin / Math.Max(0.01, Math.Cos(DegreesToRadians(widestLatitude)));

        var grown = envelope.Copy();
        grown.ExpandBy(longitudeMargin, latitudeMargin);
        return grown;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
}
