// SPDX-License-Identifier: AGPL-3.0-or-later
using OSGeo.OSR;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// Projected-to-geographic transforms from the PROJ database bundled with GDAL — offline, like
/// every other coordinate lookup here.
/// </summary>
public sealed class ProjCoordinateProjector : ICoordinateProjector
{
    static ProjCoordinateProjector() => GdalRuntime.Configure();

    /// <summary>
    /// How far north a step is taken to measure which way the grid's north points.
    ///
    /// <para>
    /// Convergence is read from the grid rather than from a formula, so it holds for any projection
    /// PROJ knows instead of only for the transverse Mercators. That makes the step size a real
    /// choice: too short and the answer is dominated by the transform's own rounding, too long and
    /// it averages the convergence over ground where it is genuinely changing. A hundred metres is
    /// far enough that a centimetre of rounding is a millidegree, and short enough to stay inside
    /// any cave's own footprint.
    /// </para>
    /// </summary>
    private const double ConvergenceStepM = 100.0;

    public ProjectedPoint? ToWgs84(int epsgCode, double easting, double northing)
    {
        try
        {
            using var source = new SpatialReference("");
            if (source.ImportFromEPSG(epsgCode) != 0)
            {
                return null;
            }

            using var target = new SpatialReference("");
            if (target.ImportFromEPSG(4326) != 0)
            {
                return null;
            }

            // Both sides are asked for longitude-first ordering explicitly. EPSG defines 4326 as
            // latitude-first, and GDAL honours that unless told otherwise, so leaving it unsaid
            // returns the pair swapped — which is not an error anywhere, just a cave on the wrong
            // continent.
            source.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
            target.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);

            using var transform = new CoordinateTransformation(source, target);

            var origin = new double[3];
            var northOfIt = new double[3];
            transform.TransformPoint(origin, easting, northing, 0);
            transform.TransformPoint(northOfIt, easting, northing + ConvergenceStepM, 0);

            // This binding reports a point it could not transform in the values rather than in a
            // return code: the coordinate comes back infinite. Unchecked, that infinity travels on
            // into the anchor and is stored as the cave's position.
            if (!IsUsable(origin) || !IsUsable(northOfIt))
            {
                return null;
            }

            return new ProjectedPoint(origin[0], origin[1], Convergence(origin, northOfIt));
        }
        catch (Exception)
        {
            // The bindings are used in return-code mode across this assembly, but PROJ can still
            // raise on a definition it cannot build a transform from. An unusable code is an
            // answer of "unknown", never a failed upload.
            return null;
        }
    }

    private static bool IsUsable(double[] point) =>
        double.IsFinite(point[0]) && double.IsFinite(point[1]);

    /// <summary>
    /// The angle from true north to grid north, from where a step due grid-north actually came out.
    /// </summary>
    private static double Convergence(double[] origin, double[] northOfIt)
    {
        // Longitude differences shrink towards the poles; without the cosine the convergence would
        // be overstated by a third at these latitudes.
        var east = (northOfIt[0] - origin[0]) * Math.Cos(origin[1] * Math.PI / 180.0);
        var north = northOfIt[1] - origin[1];
        return Math.Atan2(east, north) * 180.0 / Math.PI;
    }
}
