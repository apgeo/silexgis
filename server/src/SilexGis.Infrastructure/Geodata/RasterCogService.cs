// SPDX-License-Identifier: AGPL-3.0-or-later
using MaxRev.Gdal.Core;
using NetTopologySuite.Geometries;
using OSGeo.GDAL;
using OSGeo.OSR;

namespace SilexGis.Infrastructure.Geodata;

public sealed record RasterInfo(Polygon Bbox4326, int Width, int Height);

/// <summary>
/// Raster normalization to Cloud-Optimized GeoTIFF using the in-process GDAL COG driver
/// (bundled build ships it — no gdal_translate binary needed). Also extracts the 4326
/// footprint for the map catalog.
/// </summary>
public sealed class RasterCogService
{
    static RasterCogService() => GdalBase.ConfigureAll();

    /// <summary>True when the bundled GDAL exposes the COG driver (sanity check for tests).</summary>
    public static bool CogDriverAvailable => Gdal.GetDriverByName("COG") is not null;

    /// <summary>
    /// Converts any GDAL-readable georeferenced raster to a COG at <paramref name="outputPath"/>
    /// and returns its footprint. Throws <see cref="VectorIOException"/> on bad input.
    /// </summary>
    public RasterInfo ConvertToCog(string inputPath, string outputPath)
    {
        using var source = Gdal.Open(inputPath, Access.GA_ReadOnly);
        if (source is null)
        {
            throw new VectorIOException("The file could not be opened as a raster dataset.");
        }

        var geoTransform = new double[6];
        source.GetGeoTransform(geoTransform);
        if (geoTransform[1] == 0 && geoTransform[2] == 0)
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

    private static Polygon FootprintTo4326(Dataset source, double[] gt, string projection)
    {
        // Corner coordinates in the source CRS from the geotransform.
        var corners = new (double X, double Y)[]
        {
            (gt[0], gt[3]),
            (gt[0] + gt[1] * source.RasterXSize, gt[3] + gt[4] * source.RasterXSize),
            (gt[0] + gt[2] * source.RasterYSize, gt[3] + gt[5] * source.RasterYSize),
            (gt[0] + gt[1] * source.RasterXSize + gt[2] * source.RasterYSize,
             gt[3] + gt[4] * source.RasterXSize + gt[5] * source.RasterYSize),
        };

        var sourceSrs = new SpatialReference(projection);
        sourceSrs.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        var target = new SpatialReference("");
        target.ImportFromEPSG(4326);
        target.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        var transform = new CoordinateTransformation(sourceSrs, target);

        var envelope = new Envelope();
        foreach (var (x, y) in corners)
        {
            var point = new double[3];
            transform.TransformPoint(point, x, y, 0);
            envelope.ExpandToInclude(point[0], point[1]);
        }

        var factory = new GeometryFactory(new PrecisionModel(), 4326);
        return (Polygon)factory.ToGeometry(envelope);
    }
}
