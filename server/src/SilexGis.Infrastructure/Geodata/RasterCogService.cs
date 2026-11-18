// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using OSGeo.GDAL;
using SilexGis.Domain.Geo;

namespace SilexGis.Infrastructure.Geodata;

public sealed record RasterInfo(Polygon Bbox4326, int Width, int Height);

/// <summary>
/// Raster normalization to Cloud-Optimized GeoTIFF using the in-process GDAL COG driver
/// (bundled build ships it — no gdal_translate binary needed). Also extracts the 4326
/// footprint for the map catalog.
/// </summary>
public sealed class RasterCogService(IOptions<UploadedGeodataOptions>? options = null)
{
    static RasterCogService() => GdalRuntime.Configure();

    private readonly long maxPixelValues = (options?.Value ?? new UploadedGeodataOptions()).MaxRasterPixelValues;

    /// <summary>True when the bundled GDAL exposes the COG driver (sanity check for tests).</summary>
    public static bool CogDriverAvailable => Gdal.GetDriverByName("COG") is not null;

    /// <summary>
    /// Whether an uploaded GeoTIFF opens but carries no place — a scan that becomes a sheet to be
    /// placed by hand rather than a failed upload. False for a file that does not open as a GeoTIFF
    /// at all, and for one larger than this server converts, both of which the conversion then
    /// refuses by name.
    /// </summary>
    public bool IsUnplacedRaster(string inputPath)
    {
        Dataset source;
        try
        {
            source = OpenUpload(inputPath);
        }
        catch (VectorIOException)
        {
            return false;
        }

        using (source)
        {
            if (PixelValues(source) > maxPixelValues)
            {
                return false;
            }

            var geoTransform = new double[6];
            source.GetGeoTransform(geoTransform);
            return RasterPlacement.IsUnplaced(geoTransform) || string.IsNullOrEmpty(source.GetProjection());
        }
    }

    /// <summary>
    /// Converts any GDAL-readable georeferenced raster to a COG at <paramref name="outputPath"/>
    /// and returns its footprint. Throws <see cref="VectorIOException"/> on bad input.
    /// </summary>
    public RasterInfo ConvertToCog(string inputPath, string outputPath)
    {
        using var source = OpenUpload(inputPath);

        // The header states the dimensions, and a GeoTIFF may leave every tile empty: a file of
        // a few megabytes can declare a million pixels square, which the conversion below would
        // compress and build overviews for, pixel by pixel, for hours, on the one job lane every
        // other upload waits behind.
        if (PixelValues(source) > maxPixelValues)
        {
            throw new VectorIOException(
                $"The raster is {source.RasterXSize:N0} × {source.RasterYSize:N0} pixels in {source.RasterCount} band(s), "
                + $"more than this server converts ({maxPixelValues:N0} pixel values at most).");
        }

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

        var bbox = RasterFootprint.To4326(source, geoTransform, projection);

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
    /// Width × height × bands, as the file states them — in floating point, because three
    /// stated figures multiplied together can pass what a long holds.
    /// </summary>
    private static double PixelValues(Dataset source) =>
        (double)source.RasterXSize * source.RasterYSize * Math.Max(1, source.RasterCount);

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
}
