// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using NetTopologySuite.Geometries;
using OSGeo.GDAL;
using OSGeo.OSR;
using SilexGis.Domain.Geo;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>One control point's misfit after the fit, in metres on the ground.</summary>
public sealed record ControlPointResidual(int Index, double Metres);

/// <summary>A placed sheet, published.</summary>
public sealed record WarpOutcome(Polygon Bbox4326, double? RmsMetres, IReadOnlyList<ControlPointResidual> Residuals);

/// <summary>A placement tried out on the small picture, for the editor.</summary>
public sealed record PreviewOutcome(
    byte[] Png,
    double[] Extent3857,
    double[] Extent4326,
    int Width,
    int Height,
    double? RmsMetres,
    IReadOnlyList<ControlPointResidual> Residuals);

/// <summary>
/// Puts a drawn sheet where its placement says, with the raster library's own transformations:
/// polynomial fits of order one to three, the perspective (homography) fit and the thin-plate
/// spline, each fitted to the control points by the library; and the two-point similarity, the one
/// fit the library does not offer, applied as a plain geotransform worked out in Domain.
/// </summary>
/// <remarks>
/// <para>
/// The same code serves the published map and the editor's preview, so what a person sees while
/// placing a sheet is what the map will draw — there is no second implementation of any
/// transformation anywhere, in the browser or here.
/// </para>
/// <para>
/// The published raster is always in web-mercator, the map's own system: a sheet placed in a
/// national grid is warped once, here, rather than reprojected by every browser that draws it.
/// </para>
/// </remarks>
public class MapWarpService(ICrsRegistry crsRegistry)
{
    static MapWarpService() => GdalRuntime.Configure();

    private const int ThinPlateSplineLeaveOneOutLimit = 60;

    /// <summary>Warps the master into a cloud-optimised GeoTIFF at <paramref name="outputPath"/>.</summary>
    public WarpOutcome Publish(
        string masterPath,
        MapPlacement placement,
        string outputPath,
        string workDirectory,
        GeoreferencedMapOptions options,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(options);
        var srsWkt = WktOf(placement.Crs);

        using var scratch = GdalScratchDirectory.At(workDirectory);
        using var sidecars = GdalSidecarsOff.OnThisThread();

        var source = placement.Mask is { } mask ? Masked(masterPath, mask, workDirectory) : masterPath;
        ct.ThrowIfCancellationRequested();

        using var input = Gdal.Open(source, Access.GA_ReadOnly)
            ?? throw new VectorIOException("The drawn sheet could not be opened.");
        using var placed = Placed(input, placement, srsWkt);

        var partial = outputPath + ".partial";
        var args = new List<string>
        {
            "-t_srs", "EPSG:3857", "-r", "bilinear", "-of", "COG",
            "-co", "BLOCKSIZE=512", "-co", "OVERVIEW_RESAMPLING=AVERAGE", "-co", "BIGTIFF=IF_SAFER",
            "-co", "NUM_THREADS=4", "-multi", "-wo", "NUM_THREADS=4", "-wm", "512",
        };
        foreach (var co in options.CogCreationOptions())
        {
            args.Add("-co");
            args.Add(co);
        }

        args.AddRange(MethodArguments(placement.Transformation));

        Polygon bbox;
        using (var warped = Run(() => Gdal.Warp(partial, [placed], new GDALWarpAppOptions([.. args]), null, null), "The sheet could not be warped"))
        {
            bbox = RasterFootprint.To4326(warped);
        }

        File.Move(partial, outputPath, overwrite: true);
        var (rms, residuals) = Residuals(placed, placement, srsWkt);
        return new WarpOutcome(bbox, rms, residuals);
    }

    /// <summary>
    /// Tries a placement on the stored small picture and answers a picture of the result in
    /// web-mercator, its extent, and how well the points fit.
    /// </summary>
    /// <remarks>
    /// The editor asks for a new preview at every change of the points and aborts the request the
    /// change supersedes. <paramref name="ct"/> is that request's own token: the library is told to
    /// stop at its next progress report once it fires, so a preview nobody waits for any more gives
    /// back its preview slot — and the processors a real placement needs — within moments instead
    /// of drawing on to the end. Stopped, it throws <see cref="OperationCanceledException"/>, never
    /// the failure a broken placement answers.
    /// <para>
    /// The token is required rather than defaulted, so that a caller cannot leave it out by
    /// accident and bring back previews that outlive their request. Virtual only so that a test
    /// can stand a drawing in for it that waits for its caller to go.
    /// </para>
    /// </remarks>
    public virtual PreviewOutcome Preview(
        string previewPath, MapPlacement placement, string workDirectory, int maxPixels, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(placement);
        try
        {
            return PreviewCore(previewPath, placement, workDirectory, maxPixels, StopWhenCancelled(ct));
        }
        catch (VectorIOException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException("The preview was abandoned by its caller.", ct);
        }
    }

    private PreviewOutcome PreviewCore(
        string previewPath,
        MapPlacement placement,
        string workDirectory,
        int maxPixels,
        Gdal.GDALProgressFuncDelegate? stop)
    {
        // The library's progress report is the one place the token is looked at: every step that
        // takes time — shrinking, warping, writing the picture — reports progress, and the steps
        // between them are moments. A check of the token of our own here would also hide a step
        // that had lost its report, because it would stop the preview just the same.
        var srsWkt = WktOf(placement.Crs);
        Directory.CreateDirectory(workDirectory);
        using var scratch = GdalScratchDirectory.At(workDirectory);
        using var sidecars = GdalSidecarsOff.OnThisThread();

        using var stored = Gdal.Open(previewPath, Access.GA_ReadOnly)
            ?? throw new VectorIOException("The sheet's picture could not be opened.");

        // Shrunk first: a preview is looked at, not measured, and warping fewer pixels is what
        // keeps it quick enough to follow a person dragging a corner.
        var scale = Math.Min(1d, (double)maxPixels / Math.Max(stored.RasterXSize, stored.RasterYSize));
        var small = Path.Combine(workDirectory, "small.tif");
        using (Run(() => Gdal.wrapper_GDALTranslate(small, stored, new GDALTranslateOptions(
        [
            "-of", "GTiff", "-outsize",
            Math.Max(1, (int)Math.Round(stored.RasterXSize * scale)).ToString(CultureInfo.InvariantCulture),
            Math.Max(1, (int)Math.Round(stored.RasterYSize * scale)).ToString(CultureInfo.InvariantCulture),
            "-r", "average", "-co", "ALPHA=YES",
            .. RasterBands.WithAlpha(stored),
        ]), stop, null), "The sheet's picture could not be read"))
        {
        }

        var source = placement.Mask is { } mask ? Masked(small, mask, workDirectory) : small;
        using var input = Gdal.Open(source, Access.GA_ReadOnly)
            ?? throw new VectorIOException("The sheet's picture could not be opened.");
        using var placed = Placed(input, placement, srsWkt);

        var warpedPath = Path.Combine(workDirectory, "warped.tif");
        var args = new List<string> { "-t_srs", "EPSG:3857", "-r", "bilinear", "-of", "GTiff", "-co", "ALPHA=YES" };
        args.AddRange(MethodArguments(placement.Transformation));
        args.AddRange(PreviewSize(placed, args, workDirectory, maxPixels));

        double[] extent3857;
        double[] extent4326;
        int width, height;
        using (var warped = Run(() => Gdal.Warp(warpedPath, [placed], new GDALWarpAppOptions([.. args]), stop, null), "The placement could not be tried"))
        {
            width = warped.RasterXSize;
            height = warped.RasterYSize;
            var gt = new double[6];
            warped.GetGeoTransform(gt);
            extent3857 = [gt[0], gt[3] + (height * gt[5]), gt[0] + (width * gt[1]), gt[3]];
            var footprint = RasterFootprint.To4326(warped).EnvelopeInternal;
            extent4326 = [footprint.MinX, footprint.MinY, footprint.MaxX, footprint.MaxY];

            var pngPath = Path.Combine(workDirectory, "preview.png");
            using (Run(() => Gdal.GetDriverByName("PNG").CreateCopy(pngPath, warped, 0, null, stop, null), "The preview could not be written"))
            {
            }

            var (rms, residuals) = Residuals(placed, placement, srsWkt);
            return new PreviewOutcome(File.ReadAllBytes(pngPath), extent3857, extent4326, width, height, rms, residuals);
        }
    }

    /// <summary>
    /// A progress report that answers "stop" (zero) once <paramref name="ct"/> fires, which makes
    /// the library abandon the step and fail it; none at all for a token that can never fire.
    /// </summary>
    private static Gdal.GDALProgressFuncDelegate? StopWhenCancelled(CancellationToken ct) =>
        ct.CanBeCanceled ? (_, _, _) => ct.IsCancellationRequested ? 0 : 1 : null;

    /// <summary>A system's own name and whether it is longitude and latitude; null when it cannot be defined.</summary>
    public (string Name, bool Geographic)? Describe(int epsgCode)
    {
        if (crsRegistry.Proj4(epsgCode) is not { } proj4)
        {
            return null;
        }

        using var srs = new SpatialReference("");
        var imported = false;
        try
        {
            imported = srs.ImportFromEPSG(epsgCode) == 0;
        }
        catch (ApplicationException)
        {
            imported = false;
        }

        if (!imported && srs.ImportFromProj4(proj4) != 0)
        {
            return null;
        }

        return (srs.GetName() ?? $"EPSG:{epsgCode}", srs.IsGeographic() == 1);
    }

    /// <summary>The placement's control points with their ground coordinates in longitude and latitude.</summary>
    public IReadOnlyList<MapControlPoint> PointsInWgs84(MapPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (placement.EpsgCode == 4326)
        {
            return placement.Gcps;
        }

        using var source = new SpatialReference(WktOf(placement.Crs));
        source.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        using var target = new SpatialReference("");
        target.ImportFromEPSG(4326);
        target.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        using var transform = new CoordinateTransformation(source, target);
        var point = new double[3];
        return [.. placement.Gcps.Select(p =>
        {
            transform.TransformPoint(point, p.X, p.Y, 0);
            return p with { X = point[0], Y = point[1] };
        })];
    }

    /// <summary>The coordinate system a placement's points are written in, as the library's text.</summary>
    public string WktOf(string crs)
    {
        if (crs.StartsWith("WKT:", StringComparison.Ordinal))
        {
            return crs[4..];
        }

        var code = MapPlacementRules.EpsgCodeOf(crs)
            ?? throw new VectorIOException("The placement names no coordinate system this server knows.");
        var proj4 = crsRegistry.Proj4(code)
            ?? throw new VectorIOException($"EPSG:{code} is not a coordinate system this server can define.");
        using var srs = new SpatialReference("");
        if (srs.ImportFromProj4(proj4) != 0)
        {
            throw new VectorIOException($"EPSG:{code} could not be defined.");
        }

        srs.ExportToWkt(out var wkt, null);
        return wkt;
    }

    /// <summary>The source with its control points (or, for a similarity, its geotransform) attached.</summary>
    private static Dataset Placed(Dataset input, MapPlacement placement, string srsWkt)
    {
        // A virtual copy: it reads the drawing where it lies and carries the placement, so even a
        // drawing of several gigabytes costs nothing to place.
        var vrt = Gdal.GetDriverByName("VRT").CreateCopy("", input, 0, null, null, null)
            ?? throw new VectorIOException("The drawn sheet could not be placed.");
        int width = input.RasterXSize, height = input.RasterYSize;
        if (placement.Transformation == MapTransformations.Similarity)
        {
            vrt.SetGeoTransform(MapPlacementMath.SimilarityGeoTransform(placement.Gcps, width, height));
            vrt.SetProjection(srsWkt);
        }
        else
        {
            var gcps = placement.Gcps
                .Select((p, i) => new GCP(p.X, p.Y, 0, p.U * width, p.V * height, p.Label ?? "", (i + 1).ToString(CultureInfo.InvariantCulture)))
                .ToArray();
            vrt.SetGCPs(gcps, srsWkt);
        }

        return vrt;
    }

    /// <summary>
    /// The output size a preview is held to: none when the warp's own choice is within
    /// <paramref name="maxPixels"/> on its longer side, otherwise that choice scaled down to it.
    /// </summary>
    /// <remarks>
    /// The warp picks its output resolution from the finest part of the placed picture. A
    /// placement half-way through being typed is easily lopsided — three corners already on a
    /// few hundred metres of ground, the fourth still where the editor first laid it tens of
    /// kilometres away — and the finest part then sets the resolution over the whole stretch:
    /// a picture hundreds of thousands of pixels across, which takes minutes. Nobody waits for
    /// it (the next keystroke asks for another preview), but the server goes on drawing it,
    /// holding a preview slot and the processors that publishing a map needs. Asking the warp
    /// for its choice through a virtual output computes no pixels, so it costs next to nothing.
    /// </remarks>
    private static string[] PreviewSize(Dataset placed, List<string> args, string workDirectory, int maxPixels)
    {
        var probeArgs = new List<string>(args);
        var format = probeArgs.IndexOf("-of");
        probeArgs[format + 1] = "VRT";
        probeArgs.RemoveRange(probeArgs.IndexOf("-co"), 2);
        var probePath = Path.Combine(workDirectory, "size.vrt");
        int width, height;
        using (var probe = Run(() => Gdal.Warp(probePath, [placed], new GDALWarpAppOptions([.. probeArgs]), null, null), "The placement could not be tried"))
        {
            width = probe.RasterXSize;
            height = probe.RasterYSize;
        }

        var longer = Math.Max(width, height);
        if (longer <= maxPixels)
        {
            return [];
        }

        var shrink = (double)maxPixels / longer;
        return
        [
            "-ts",
            Math.Max(1, (int)Math.Round(width * shrink)).ToString(CultureInfo.InvariantCulture),
            Math.Max(1, (int)Math.Round(height * shrink)).ToString(CultureInfo.InvariantCulture),
        ];
    }

    private static IEnumerable<string> MethodArguments(string transformation) => transformation switch
    {
        MapTransformations.Similarity => [],
        MapTransformations.Affine => ["-to", "SRC_METHOD=GCP_POLYNOMIAL", "-to", "MAX_GCP_ORDER=1"],
        MapTransformations.Polynomial2 => ["-to", "SRC_METHOD=GCP_POLYNOMIAL", "-to", "MAX_GCP_ORDER=2"],
        MapTransformations.Polynomial3 => ["-to", "SRC_METHOD=GCP_POLYNOMIAL", "-to", "MAX_GCP_ORDER=3"],
        MapTransformations.Homography => ["-to", "SRC_METHOD=GCP_HOMOGRAPHY"],
        MapTransformations.ThinPlateSpline => ["-tps"],
        _ => throw new VectorIOException($"'{transformation}' is not a transformation."),
    };

    private static string[] TransformerOptions(string transformation) => transformation switch
    {
        MapTransformations.Similarity => ["SRC_METHOD=GEOTRANSFORM"],
        MapTransformations.Affine => ["SRC_METHOD=GCP_POLYNOMIAL", "MAX_GCP_ORDER=1"],
        MapTransformations.Polynomial2 => ["SRC_METHOD=GCP_POLYNOMIAL", "MAX_GCP_ORDER=2"],
        MapTransformations.Polynomial3 => ["SRC_METHOD=GCP_POLYNOMIAL", "MAX_GCP_ORDER=3"],
        MapTransformations.Homography => ["SRC_METHOD=GCP_HOMOGRAPHY"],
        MapTransformations.ThinPlateSpline => ["SRC_METHOD=GCP_TPS"],
        _ => throw new VectorIOException($"'{transformation}' is not a transformation."),
    };

    /// <summary>
    /// How far each control point lands from where it was put, in metres on the ground, and the
    /// root mean square of those. The thin-plate spline passes through every point by
    /// construction, so its misfit is measured by leaving each point out in turn and asking where
    /// the others put it; above a few dozen points that costs more than it tells, and is not done.
    /// </summary>
    private (double? Rms, IReadOnlyList<ControlPointResidual> Residuals) Residuals(
        Dataset placed, MapPlacement placement, string srsWkt)
    {
        using var source = new SpatialReference(srsWkt);
        source.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        using var geographic = new SpatialReference("");
        geographic.ImportFromEPSG(4326);
        geographic.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        using var toDegrees = new CoordinateTransformation(source, geographic);

        int width = placed.RasterXSize, height = placed.RasterYSize;
        var residuals = new List<ControlPointResidual>();

        if (placement.Transformation == MapTransformations.ThinPlateSpline)
        {
            var n = placement.Gcps.Count;
            if (n <= 3 || n > ThinPlateSplineLeaveOneOutLimit)
            {
                return (null, residuals);
            }

            for (var i = 0; i < n; i++)
            {
                var others = placement with { Gcps = [.. placement.Gcps.Where((_, j) => j != i)] };
                using var vrt = Placed(placed, others, srsWkt);
                residuals.Add(new(i, Misfit(vrt, placement.Gcps[i], width, height, TransformerOptions(placement.Transformation), toDegrees)));
            }
        }
        else
        {
            for (var i = 0; i < placement.Gcps.Count; i++)
            {
                residuals.Add(new(i, Misfit(placed, placement.Gcps[i], width, height, TransformerOptions(placement.Transformation), toDegrees)));
            }
        }

        if (residuals.Count == 0 || residuals.Any(r => !double.IsFinite(r.Metres)))
        {
            return (null, residuals.Where(r => double.IsFinite(r.Metres)).ToList());
        }

        var rms = Math.Sqrt(residuals.Sum(r => r.Metres * r.Metres) / residuals.Count);
        return (rms, residuals);
    }

    private static double Misfit(
        Dataset placed, MapControlPoint point, int width, int height, string[] method, CoordinateTransformation toDegrees)
    {
        using var transformer = new Transformer(placed, null, method);
        var landed = new double[] { point.U * width, point.V * height, 0 };
        if (transformer.TransformPoint(0, landed) == 0)
        {
            return double.NaN;
        }

        var a = new double[3];
        var b = new double[3];
        toDegrees.TransformPoint(a, landed[0], landed[1], 0);
        toDegrees.TransformPoint(b, point.X, point.Y, 0);
        return Geodesy.DistanceMeters(a[0], a[1], b[0], b[1]);
    }

    /// <summary>
    /// A copy of the picture with everything outside the outline made transparent, by burning a
    /// zero into its alpha band outside the polygon — the polygon written in the picture's own
    /// pixels, which is what an unplaced picture's coordinates are.
    /// </summary>
    private static string Masked(string picturePath, IReadOnlyList<IReadOnlyList<double>> mask, string workDirectory)
    {
        using var picture = Gdal.Open(picturePath, Access.GA_ReadOnly)
            ?? throw new VectorIOException("The drawn sheet could not be opened.");
        int width = picture.RasterXSize, height = picture.RasterYSize;

        var maskedPath = Path.Combine(workDirectory, "masked-" + Path.GetFileNameWithoutExtension(picturePath) + ".tif");
        using var masked = Run(() => Gdal.wrapper_GDALTranslate(maskedPath, picture, new GDALTranslateOptions(
            ["-of", "GTiff", "-co", "TILED=YES", "-co", "COMPRESS=DEFLATE", "-co", "ALPHA=YES", "-co", "BIGTIFF=IF_SAFER"]), null, null),
            "The outline could not be applied");

        // The picture's pixels as plain coordinates: no geotransform and no system on the copy or
        // on the outline, so the library reads polygon coordinates as column and row.
        masked.SetGeoTransform([0, 1, 0, 0, 0, 1]);
        var ring = string.Join(",", mask.Concat([mask[0]]).Select(p => string.Create(
            CultureInfo.InvariantCulture, $"{p[0] * width} {p[1] * height}")));
        // A one-row table with the polygon as text: a vector source with no coordinate system,
        // which is what an outline in pixels is.
        var csvPath = Path.Combine(workDirectory, "mask.csv");
        File.WriteAllText(csvPath, $"WKT\n\"POLYGON(({ring}))\"\n");
        using var outline = Gdal.OpenEx(csvPath, (uint)GdalConst.OF_VECTOR, ["CSV"], null, null)
            ?? throw new VectorIOException("The outline could not be read.");
        Run(
            () => Gdal.wrapper_GDALRasterizeDestDS(
                masked, outline, new GDALRasterizeOptions(["-b", "4", "-burn", "0", "-i", "-l", "mask"]), null, null),
            "The outline could not be applied");
        masked.FlushCache();
        return maskedPath;
    }

    /// <summary>
    /// Runs one library call and turns a failure — no result, or an exception from the binding —
    /// into a sentence the map can show.
    /// </summary>
    private static T Run<T>(Func<T?> call, string what)
        where T : class
    {
        Gdal.ErrorReset();
        try
        {
            return call() ?? throw new VectorIOException($"{what}: {Gdal.GetLastErrorMsg()}");
        }
        catch (ApplicationException e)
        {
            throw new VectorIOException($"{what}: {e.Message}", e);
        }
    }

    /// <summary>The same for a call that answers whether it succeeded: one for yes, zero for no.</summary>
    private static void Run(Func<int> call, string what)
    {
        Gdal.ErrorReset();
        try
        {
            if (call() == 0)
            {
                throw new VectorIOException($"{what}: {Gdal.GetLastErrorMsg()}");
            }
        }
        catch (ApplicationException e)
        {
            throw new VectorIOException($"{what}: {e.Message}", e);
        }
    }
}
