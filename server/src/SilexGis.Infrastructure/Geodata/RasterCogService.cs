// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using OSGeo.GDAL;
using OSGeo.OSR;
using SilexGis.Domain.Geo;

namespace SilexGis.Infrastructure.Geodata;

public sealed record RasterInfo(Polygon Bbox4326, int Width, int Height);

/// <summary>
/// Raster normalization to Cloud-Optimized GeoTIFF using the in-process GDAL COG driver
/// (bundled build ships it — no gdal_translate binary needed). Also extracts the 4326
/// footprint for the map catalog.
/// </summary>
public sealed class RasterCogService
{
    static RasterCogService() => GdalRuntime.Configure();

    /// <summary>True when the bundled GDAL exposes the COG driver (sanity check for tests).</summary>
    public static bool CogDriverAvailable => Gdal.GetDriverByName("COG") is not null;

    /// <summary>
    /// Converts any GDAL-readable georeferenced raster to a COG at <paramref name="outputPath"/>
    /// and returns its footprint. Throws <see cref="VectorIOException"/> on bad input.
    /// </summary>
    public RasterInfo ConvertToCog(string inputPath, string outputPath)
    {
        using var source = OpenUpload(inputPath);

        var geoTransform = new double[6];
        source.GetGeoTransform(geoTransform);
        if (RasterPlacement.IsUnplaced(geoTransform))
        {
            throw new VectorIOException("The raster carries no georeferencing (geotransform missing).");
        }

        var projection = source.GetProjection();
        if (string.IsNullOrEmpty(projection))
        {
            throw new VectorIOException("The raster carries no spatial reference.");
        }

        var bbox = FootprintTo4326(source, geoTransform, projection);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var cogDriver = Gdal.GetDriverByName("COG")
            ?? throw new VectorIOException("GDAL COG driver is unavailable in this build.");

        using var cog = cogDriver.CreateCopy(
            outputPath, source, 0, ["COMPRESS=DEFLATE", "BLOCKSIZE=512"], null, null);
        if (cog is null)
        {
            throw new VectorIOException("COG conversion failed.");
        }

        return new RasterInfo(bbox, source.RasterXSize, source.RasterYSize);
    }

    /// <summary>
    /// The drivers an uploaded map may be opened with: it is accepted only as a GeoTIFF, and a
    /// cloud-optimised GeoTIFF is one.
    /// </summary>
    private static readonly string[] UploadDrivers = ["GTiff", "COG"];

    /// <summary>Opens an uploaded map with only the GeoTIFF drivers.</summary>
    /// <remarks>
    /// Left to itself the library chooses a driver from the content, whatever the file is called,
    /// and a few lines of "virtual raster" XML saved as <c>.tif</c> would be opened as that: a
    /// document whose pixels are read from any file the server can open, or from an address only
    /// the server can reach, and which the conversion below would then publish as the uploader's
    /// map. With the drivers named, such a file is refused rather than followed.
    /// </remarks>
    private static Dataset OpenUpload(string inputPath)
    {
        Dataset? source;
        try
        {
            source = Gdal.OpenEx(
                inputPath,
                (uint)(GdalConst.OF_RASTER | GdalConst.OF_READONLY),
                allowed_drivers: UploadDrivers,
                open_options: null,
                sibling_files: null);
        }
        catch (ApplicationException e)
        {
            throw new VectorIOException("The file could not be opened as a raster dataset.", e);
        }

        return source ?? throw new VectorIOException("The file could not be opened as a raster dataset.");
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
    private static Polygon FootprintTo4326(Dataset source, double[] gt, string projection)
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
