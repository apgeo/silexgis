// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>
/// Conversion between the two kinds of height this system has to hold at once.
///
/// <para>
/// Cave survey altitudes are <b>orthometric</b> (strictly, in Romania, normal heights on the
/// Black Sea 1975 system): height above the geoid — the equipotential surface an altimeter,
/// a levelling run or a national map all measure against. A 3D globe, and any WGS84 GNSS fix
/// before its receiver applies a model, works in <b>ellipsoidal</b> height: height above the
/// mathematical WGS84 ellipsoid, which is not a physical surface at all.
/// </para>
///
/// <para>
/// The difference between the two is the geoid undulation, and it is not small. Measured
/// EGM2008 undulations over Romanian karst run +44.5 m in Banat, +43.5 m in the Apuseni and
/// +39.4 m in Piatra Craiului. Mixing the two without converting puts a cave entrance roughly
/// forty metres off the hillside it is actually on — high enough to float visibly above the
/// terrain, low enough that it looks like a data error rather than a units mistake.
/// </para>
///
/// <para>
/// The undulation varies smoothly across the country, so a single installation-wide offset is
/// an approximation: within Romania it is good to a few metres, which is well inside the
/// accuracy of a hand-recorded cave altitude, and it needs no grid file, no network lookup and
/// no dependency. An installation elsewhere sets its own value. The offset is a parameter here
/// rather than a constant because it is installation configuration, and this layer holds no
/// configuration of its own — but the arithmetic has exactly one home, this one, so no caller
/// has to remember which way the sign goes.
/// </para>
/// </summary>
public static class GeoidOffset
{
    /// <summary>
    /// Ellipsoidal (WGS84) height for an orthometric height, given the local geoid undulation.
    /// The geoid sits <i>above</i> the ellipsoid across Romania, so a positive offset raises the
    /// point: a 1200 m entrance with a +39.39 m undulation is at 1239.39 m ellipsoidal.
    /// </summary>
    public static double EllipsoidalFromOrthometric(double orthometricM, double geoidOffsetM) =>
        orthometricM + geoidOffsetM;

    /// <summary>
    /// Orthometric height for an ellipsoidal (WGS84) height — the direction a terrain sample or
    /// a raw GNSS fix has to travel before it can be compared with a surveyed cave altitude.
    /// </summary>
    public static double OrthometricFromEllipsoidal(double ellipsoidalM, double geoidOffsetM) =>
        ellipsoidalM - geoidOffsetM;
}
