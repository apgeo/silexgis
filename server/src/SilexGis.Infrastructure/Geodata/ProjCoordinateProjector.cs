// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
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
            return source.ImportFromEPSG(epsgCode) != 0 ? null : Transform(source, easting, northing);
        }
        catch (Exception)
        {
            // The bindings are used in return-code mode across this assembly, but PROJ can still
            // raise on a definition it cannot build a transform from. An unusable code is an
            // answer of "unknown", never a failed upload.
            return null;
        }
    }

    public ProjectedPoint? ToWgs84(string wkt, double easting, double northing)
    {
        if (string.IsNullOrWhiteSpace(wkt))
        {
            return null;
        }

        try
        {
            using var source = new SpatialReference("");
            return source.ImportFromWkt(ref wkt) != 0 ? null : Transform(source, easting, northing);
        }
        catch (Exception)
        {
            // As above: text that is not a coordinate system is "unknown", not a failure.
            return null;
        }
    }

    public int? EpsgOf(string wkt)
    {
        if (string.IsNullOrWhiteSpace(wkt))
        {
            return null;
        }

        try
        {
            using var source = new SpatialReference("");
            if (source.ImportFromWkt(ref wkt) != 0)
            {
                return null;
            }

            // A definition that carries its authority is believed. One that does not — a UTM zone
            // or a national grid a writer spelled out by its parameters, which is how LAS files
            // usually carry theirs — is looked up in the register by what it defines. The older
            // identification (AutoIdentifyEPSG) answers "unsupported" for exactly these texts
            // under PROJ, measured on UTM 35N and Stereo70; FindMatches finds both at full
            // confidence. Only an exact or near-exact match is taken (90 and above: the same
            // system, at most differing in axis order, which the transform sets itself), and only
            // when every such match names the same code — a text two systems fit equally is no
            // system's. Either way the answer is only ever EPSG's: another authority's code would
            // be a different register's number stored where the EPSG one is read back.
            if (EpsgCode(source) is { } named)
            {
                return named;
            }

            var matches = source.FindMatches(null, out var count, out var confidence);
            int? found = null;
            try
            {
                for (var i = 0; i < count; i++)
                {
                    if (confidence[i] < 90 || EpsgCode(matches[i]) is not { } code)
                    {
                        continue;
                    }

                    if (found is { } other && other != code)
                    {
                        return null;
                    }

                    found = code;
                }
            }
            finally
            {
                for (var i = 0; i < count; i++)
                {
                    matches[i]?.Dispose();
                }
            }

            return found;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The EPSG code the definition itself names on its root node, or null.</summary>
    private static int? EpsgCode(SpatialReference srs) =>
        string.Equals(srs.GetAuthorityName(null), "EPSG", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(srs.GetAuthorityCode(null), NumberStyles.None, CultureInfo.InvariantCulture, out var code)
            ? code
            : null;

    /// <summary>
    /// The point and the grid's turn at it, for a system already read into <paramref name="source"/>.
    /// Throws what the bindings throw; the callers turn that into "unknown".
    /// </summary>
    private static ProjectedPoint? Transform(SpatialReference source, double easting, double northing)
    {
        // Only a projected system has an easting and a northing. A geographic code — a file
        // whose header names WGS 84 itself, say — would transform degrees into the same
        // degrees and then read the step below as a hundred degrees north, which is not a
        // convergence; a geocentric one has no north at all. Both are "cannot be placed from
        // this code", never a position.
        if (source.IsProjected() == 0)
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
