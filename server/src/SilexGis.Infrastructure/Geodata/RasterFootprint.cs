// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using OSGeo.GDAL;
using OSGeo.OSR;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// The ground a placed raster covers, in longitude and latitude — one answer for every raster the
/// map catalogue publishes, uploaded or drawn here.
/// </summary>
internal static class RasterFootprint
{
    static RasterFootprint() => GdalRuntime.Configure();

    /// <summary>The footprint of an open dataset from its own geotransform and system.</summary>
    public static Polygon To4326(Dataset source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var gt = new double[6];
        source.GetGeoTransform(gt);
        return To4326(source, gt, source.GetProjection());
    }

    /// <summary>How many points are taken along each edge of a raster to draw its footprint.</summary>
    private const int FootprintSamplesPerEdge = 16;

    /// <summary>
    /// The ground a raster covers, in longitude and latitude, drawn round points taken along its
    /// whole boundary.
    /// </summary>
    /// <remarks>
    /// Its four corners are not enough: an edge that is straight in the raster's own system is
    /// curved in degrees, and a box round the corners leaves the bulge of that curve outside the
    /// footprint. And this binding reports a point it could not transform in the values rather than
    /// in a return code — the coordinate comes back infinite — so every point is checked. A raster
    /// any of whose boundary will not transform is refused with that said: a footprint drawn round
    /// the part that did would be smaller than the map, and one carrying an infinite coordinate
    /// would break the catalogue for everyone who can see it.
    /// </remarks>
    public static Polygon To4326(Dataset source, double[] gt, string projection)
    {
        // Each of these holds native memory the garbage collector cannot see, so they are released
        // here rather than whenever a finalizer gets round to it.
        using var sourceSrs = new SpatialReference(projection);
        sourceSrs.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        using var target = new SpatialReference("");
        target.ImportFromEPSG(4326);
        target.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        using var transform = new CoordinateTransformation(sourceSrs, target);

        var width = source.RasterXSize;
        var height = source.RasterYSize;
        var envelope = new Envelope();
        var point = new double[3];
        for (var i = 0; i < FootprintSamplesPerEdge; i++)
        {
            var along = (double)i / FootprintSamplesPerEdge;
            foreach (var (pixelX, pixelY) in new[]
                     {
                         (along * width, 0d), (width, along * height),
                         ((1 - along) * width, height), (0d, (1 - along) * height),
                     })
            {
                var x = gt[0] + (pixelX * gt[1]) + (pixelY * gt[2]);
                var y = gt[3] + (pixelX * gt[4]) + (pixelY * gt[5]);
                try
                {
                    transform.TransformPoint(point, x, y, 0);
                }
                catch (ApplicationException)
                {
                    // The projection library raises for some failures where it reports others in
                    // the values; either way the point did not transform.
                    point[0] = double.NaN;
                }

                if (!double.IsFinite(point[0]) || !double.IsFinite(point[1])
                    || point[0] is < -180 or > 180 || point[1] is < -90 or > 90)
                {
                    throw new VectorIOException(
                        "Part of the raster's edge does not convert to longitude and latitude; "
                        + "its georeferencing places it outside the area its coordinate system covers.");
                }

                envelope.ExpandToInclude(point[0], point[1]);
            }
        }

        if (!(envelope.Width > 0 && envelope.Height > 0))
        {
            throw new VectorIOException("The raster's georeferencing gives it no area.");
        }

        var factory = new GeometryFactory(new PrecisionModel(), 4326);
        return (Polygon)factory.ToGeometry(envelope);
    }
}
