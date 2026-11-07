// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using OSGeo.GDAL;
using OSGeo.OSR;
using SilexGis.Domain.Geo;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>What a portable document's page says about itself before anything is drawn.</summary>
/// <param name="PageCount">Pages in the document.</param>
/// <param name="PointsWide">The page's width in points (1/72 inch).</param>
/// <param name="PointsHigh">The page's height in points.</param>
/// <param name="Layers">The page's optional-content layer names, in the document's order.</param>
/// <param name="Embedded">The georeferencing the page carries, or null.</param>
public sealed record PdfSheetInfo(
    int PageCount, double PointsWide, double PointsHigh, IReadOnlyList<string> Layers, EmbeddedGeoreference? Embedded);

/// <summary>
/// A page's own georeferencing, measured at 72 dots per inch (one pixel per point).
/// </summary>
/// <param name="GeoTransform">Pixel to ground at 72 dpi.</param>
/// <param name="Wkt">The coordinate system, as the document states it.</param>
/// <param name="Neatline">The map frame in that system, when the document names one.</param>
public sealed record EmbeddedGeoreference(double[] GeoTransform, string Wkt, Geometry? Neatline);

/// <summary>What to draw.</summary>
/// <param name="SourcePath">The document on disk.</param>
/// <param name="Page">From 1.</param>
/// <param name="Layers">Layer names to draw; null for all.</param>
/// <param name="Dpi">The resolution asked for; null to let the rule choose.</param>
/// <param name="WorkDirectory">An existing directory the drawing may write into.</param>
public sealed record SheetRenderRequest(
    string SourcePath, int Page, IReadOnlyList<string>? Layers, double? Dpi, string WorkDirectory);

/// <summary>The drawn page, waiting in the work directory to be stored.</summary>
public sealed record SheetRenderResult(
    string MasterPath,
    string PreviewPath,
    int Width,
    int Height,
    double Dpi,
    PdfSheetInfo Info,
    string? Warnings);

/// <summary>
/// Draws one page of a portable document into a plain picture with no place — the "master" every
/// placement is warped from — and a small picture of it for the placement editor.
/// </summary>
/// <remarks>
/// <para>
/// The library's document driver draws whatever part of the page it is asked for by drawing the
/// page's contents again, so the one thing never to do is let a writer read it a tile at a time: a
/// scanned sheet that draws whole in five seconds took ten minutes that way. The page is read in
/// one piece, or — when one piece would not fit the memory budget — in the fewest horizontal strips
/// that do, and written into a tiled picture from there.
/// </para>
/// <para>
/// The binding raises an exception whenever the library <em>recorded</em> a failure during a call,
/// even one the call recovered from: a survey sheet with one malformed marked-content operator is
/// drawn completely and still arrives here as an exception. So whether the page was drawn is decided
/// by looking at what was drawn — every pixel starts as a pattern no drawing produces, and a read
/// that leaves next to none of it behind drew the page, whatever it said. Its complaint is kept as a
/// warning on the map rather than thrown away.
/// </para>
/// </remarks>
public sealed class PdfSheetRenderService
{
    static PdfSheetRenderService() => GdalRuntime.Configure();

    /// <summary>The bytes of a pixel no drawing produces: an odd colour at an odd, half-transparent alpha.</summary>
    private static readonly byte[] Sentinel = [0x13, 0x57, 0x9B, 0x7F];

    /// <summary>A strip larger than this is split again whatever the budget: one managed buffer holds it.</summary>
    private const long LargestStripBytes = 1_500_000_000;

    /// <summary>Fewer untouched pixels than this share of a strip means the strip was drawn.</summary>
    private const double UndrawnTolerance = 0.0001;

    private const int MaxWarningLength = 2000;

    /// <summary>Reads what the page says about itself; draws nothing.</summary>
    public PdfSheetInfo Probe(string sourcePath, int page)
    {
        var pageCount = CountPages(sourcePath);
        if (page < 1 || page > pageCount)
        {
            throw new VectorIOException($"The document has {pageCount} page(s); page {page} does not exist.");
        }

        using var dataset = Open(sourcePath, page, 72, null)
            ?? throw new VectorIOException("The file could not be opened as a portable document.");

        var layers = new List<string>();
        foreach (var entry in dataset.GetMetadata("LAYERS") ?? [])
        {
            // LAYER_00_NAME=Walls
            var eq = entry.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0 && entry.AsSpan(0, eq).EndsWith("_NAME", StringComparison.Ordinal))
            {
                layers.Add(entry[(eq + 1)..]);
            }
        }

        return new PdfSheetInfo(
            pageCount, dataset.RasterXSize, dataset.RasterYSize, layers, EmbeddedOf(dataset));
    }

    /// <summary>The resolution a page is drawn at, before the pixel budget is applied.</summary>
    public static double ChooseDpi(PdfSheetInfo info, double? asked, GeoreferencedMapOptions options)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(options);
        double dpi;
        if (asked is { } a)
        {
            dpi = a;
        }
        else if (info.Embedded is { } embedded)
        {
            // At 72 dpi one pixel is one point; its ground size is the geotransform's pixel size,
            // and the resolution that makes a pixel the target size scales from there.
            var metresPerPoint = MetresPerPixel(embedded);
            dpi = metresPerPoint is { } m && m > 0
                ? Math.Min(72 * m / options.TargetGroundResolutionMetres, options.MaxAutoDpi)
                : options.DefaultDpiUnplaced;
        }
        else
        {
            dpi = options.DefaultDpiUnplaced;
        }

        return Math.Clamp(dpi, options.MinDpi, options.MaxDpi);
    }

    /// <summary>Draws the page into the work directory.</summary>
    public SheetRenderResult Render(SheetRenderRequest request, GeoreferencedMapOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        var info = Probe(request.SourcePath, request.Page);
        if (request.Layers is { } wanted)
        {
            var unknown = wanted.Where(l => !info.Layers.Contains(l, StringComparer.Ordinal)).ToList();
            if (unknown.Count > 0)
            {
                throw new VectorIOException($"The page has no layer named {string.Join(", ", unknown.Select(u => $"'{u}'"))}.");
            }
        }

        var warnings = new List<string>();
        var dpi = ChooseDpi(info, request.Dpi, options);
        var budget = options.PixelBudgetMegapixels * 1_000_000d;
        var pixels = info.PointsWide * info.PointsHigh * (dpi / 72) * (dpi / 72);
        if (pixels > budget)
        {
            dpi *= Math.Sqrt(budget / pixels);
            warnings.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"The resolution was lowered to {dpi:0} dpi to stay within the pixel budget."));
        }

        using var scratch = GdalScratchDirectory.At(request.WorkDirectory);
        using var sidecars = GdalSidecarsOff.OnThisThread();

        using var source = Open(request.SourcePath, request.Page, dpi, request.Layers)
            ?? throw new VectorIOException("The page could not be opened for drawing.");
        int width = source.RasterXSize, height = source.RasterYSize;
        if (width < 1 || height < 1)
        {
            throw new VectorIOException("The page has no area.");
        }

        var masterPath = Path.Combine(request.WorkDirectory, "master.tif");
        using (var master = Gdal.GetDriverByName("GTiff").Create(
            masterPath, width, height, 4, DataType.GDT_Byte,
            ["TILED=YES", "BLOCKXSIZE=512", "BLOCKYSIZE=512", "COMPRESS=DEFLATE", "PREDICTOR=2",
             "PHOTOMETRIC=RGB", "ALPHA=YES", "BIGTIFF=IF_SAFER"])
            ?? throw new VectorIOException("The drawing could not be written."))
        {
            var rowBytes = (long)width * 4;
            var budgetBytes = Math.Min((long)options.RenderBudgetMegabytes * 1024 * 1024, LargestStripBytes);
            var stripRows = (int)Math.Max(1, Math.Min(height, budgetBytes / rowBytes));
            var buffer = new byte[rowBytes * stripRows];
            int[] bands = [1, 2, 3, 4];

            for (var y = 0; y < height; y += stripRows)
            {
                ct.ThrowIfCancellationRequested();
                var rows = Math.Min(stripRows, height - y);
                var length = (int)(rowBytes * rows);
                for (var i = 0; i < length; i += 4)
                {
                    buffer[i] = Sentinel[0];
                    buffer[i + 1] = Sentinel[1];
                    buffer[i + 2] = Sentinel[2];
                    buffer[i + 3] = Sentinel[3];
                }

                string? complaint = null;
                Gdal.ErrorReset();
                try
                {
                    var status = source.ReadRaster(0, y, width, rows, buffer, width, rows, 4, bands, 4, width * 4, 1);
                    if (status != CPLErr.CE_None)
                    {
                        complaint = Gdal.GetLastErrorMsg();
                    }
                }
                catch (ApplicationException e)
                {
                    complaint = e.Message;
                }

                if (complaint is not null)
                {
                    if (UndrawnShare(buffer, length) > UndrawnTolerance)
                    {
                        throw new VectorIOException($"The page could not be drawn: {complaint}");
                    }

                    if (!warnings.Contains(complaint, StringComparer.Ordinal))
                    {
                        warnings.Add(complaint);
                    }
                }

                master.WriteRaster(0, y, width, rows, buffer, width, rows, 4, bands, 4, width * 4, 1);
            }

            master.FlushCache();
        }

        var previewPath = Path.Combine(request.WorkDirectory, "preview.webp");
        WritePreview(masterPath, previewPath, width, height);

        var joined = warnings.Count == 0 ? null : string.Join(" | ", warnings);
        if (joined is { Length: > MaxWarningLength })
        {
            joined = joined[..MaxWarningLength];
        }

        return new SheetRenderResult(masterPath, previewPath, width, height, dpi, info, joined);
    }

    /// <summary>The picture formats a scan with no place may be drawn from.</summary>
    private static readonly string[] ImageDrivers = ["GTiff", "COG", "PNG", "JPEG", "WEBP"];

    /// <summary>
    /// Makes a scan with no place — a picture rather than a document — into a sheet's drawing: the
    /// same RGBA master and preview a document's page becomes, scaled down only if it is larger
    /// than the pixel budget.
    /// </summary>
    public SheetRenderResult RenderImage(string sourcePath, string workDirectory, GeoreferencedMapOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        using var scratch = GdalScratchDirectory.At(workDirectory);
        using var sidecars = GdalSidecarsOff.OnThisThread();

        Dataset? source;
        Gdal.PushErrorHandler("CPLQuietErrorHandler");
        try
        {
            // Named drivers only: a "picture" that is really a virtual raster pointing at other
            // files on the server is refused rather than followed.
            source = Gdal.OpenEx(
                sourcePath, (uint)(GdalConst.OF_RASTER | GdalConst.OF_READONLY), ImageDrivers, null, null);
        }
        catch (ApplicationException)
        {
            source = null;
        }
        finally
        {
            Gdal.PopErrorHandler();
        }

        using var picture = source ?? throw new VectorIOException("The file could not be opened as a picture.");
        if (picture.GetRasterBand(1).DataType != DataType.GDT_Byte)
        {
            throw new VectorIOException("Only 8-bit pictures can be drawn as a sheet.");
        }

        int width = picture.RasterXSize, height = picture.RasterYSize;
        var budget = options.PixelBudgetMegapixels * 1_000_000d;
        string? warning = null;
        if ((double)width * height > budget)
        {
            var scale = Math.Sqrt(budget / ((double)width * height));
            width = Math.Max(1, (int)(width * scale));
            height = Math.Max(1, (int)(height * scale));
            warning = string.Create(CultureInfo.InvariantCulture, $"The picture was scaled to {width} × {height} pixels to stay within the pixel budget.");
        }

        var masterPath = Path.Combine(workDirectory, "master.tif");
        Gdal.ErrorReset();
        using (var master = Gdal.wrapper_GDALTranslate(masterPath, picture, new GDALTranslateOptions(
        [
            "-of", "GTiff", "-outsize", width.ToString(CultureInfo.InvariantCulture), height.ToString(CultureInfo.InvariantCulture),
            "-r", "average", "-co", "TILED=YES", "-co", "COMPRESS=DEFLATE", "-co", "PREDICTOR=2",
            "-co", "PHOTOMETRIC=RGB", "-co", "ALPHA=YES", "-co", "BIGTIFF=IF_SAFER",
            .. RasterBands.WithAlpha(picture),
        ]), null, null))
        {
            if (master is null)
            {
                throw new VectorIOException($"The picture could not be drawn: {Gdal.GetLastErrorMsg()}");
            }
        }

        var previewPath = Path.Combine(workDirectory, "preview.webp");
        WritePreview(masterPath, previewPath, width, height);
        var info = new PdfSheetInfo(1, width, height, [], null);
        return new SheetRenderResult(masterPath, previewPath, width, height, 72, info, warning);
    }

    /// <summary>
    /// The page's georeferencing as a placement: its four corners matched to where the document
    /// says they are, with the document's map frame as the outline to keep.
    /// </summary>
    public static MapPlacement EmbeddedPlacement(EmbeddedGeoreference embedded, double pointsWide, double pointsHigh)
    {
        ArgumentNullException.ThrowIfNull(embedded);
        var gt = embedded.GeoTransform;
        MapControlPoint Corner(double u, double v, string label)
        {
            var (x, y) = MapPlacementMath.Apply(gt, u, v, (int)Math.Round(pointsWide), (int)Math.Round(pointsHigh));
            return new MapControlPoint(u, v, x, y, label);
        }

        IReadOnlyList<IReadOnlyList<double>>? mask = null;
        if (embedded.Neatline is Polygon { IsEmpty: false } frame)
        {
            var inverse = new double[6];
            if (Gdal.InvGeoTransform(gt, inverse) != 0)
            {
                var ring = frame.ExteriorRing.Coordinates;
                var fractions = ring.Select(c => (IReadOnlyList<double>)
                [
                    (inverse[0] + (c.X * inverse[1]) + (c.Y * inverse[2])) / pointsWide,
                    (inverse[3] + (c.X * inverse[4]) + (c.Y * inverse[5])) / pointsHigh,
                ]).ToList();

                // A frame that covers the whole page cuts nothing; a frame the rules would refuse
                // (crossing itself, off the page) is not worth refusing the document's own place over.
                var candidate = new MapPlacement(MapPlacement.CurrentVersion, MapTransformations.Affine, "EPSG:4326", [], fractions);
                var maskProblem = MapPlacementRules.Validate(candidate, int.MaxValue)
                    .Any(p => p.Code == MapPlacementRules.MaskInvalid);
                if (!maskProblem)
                {
                    mask = fractions;
                }
            }
        }

        return new MapPlacement(
            MapPlacement.CurrentVersion,
            MapTransformations.Affine,
            CrsName(embedded.Wkt),
            [Corner(0, 0, "NW"), Corner(1, 0, "NE"), Corner(1, 1, "SE"), Corner(0, 1, "SW")],
            mask);
    }

    /// <summary><c>EPSG:&lt;code&gt;</c> when the library can name the system so, else <c>WKT:</c> and its definition.</summary>
    public static string CrsName(string wkt)
    {
        using var srs = new SpatialReference(wkt);
        try
        {
            srs.AutoIdentifyEPSG();
        }
        catch (ApplicationException)
        {
            // Not every system has a code; the definition is kept instead.
        }

        var authority = srs.GetAuthorityName(null);
        var code = srs.GetAuthorityCode(null);
        return authority == "EPSG" && int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? $"EPSG:{n}"
            : "WKT:" + wkt;
    }

    private static double? MetresPerPixel(EmbeddedGeoreference embedded)
    {
        var gt = embedded.GeoTransform;
        var pixel = Math.Sqrt((gt[1] * gt[1]) + (gt[4] * gt[4]));
        using var srs = new SpatialReference(embedded.Wkt);
        if (srs.IsProjected() == 1)
        {
            return pixel * srs.GetLinearUnits();
        }

        // Degrees: a metre-scale estimate at the sheet's latitude is enough to choose a resolution.
        return srs.IsGeographic() == 1 ? pixel * 111_320 * Math.Cos(gt[3] * Math.PI / 180) : null;
    }

    private static double UndrawnShare(byte[] buffer, int length)
    {
        var untouched = 0L;
        for (var i = 0; i < length; i += 4)
        {
            if (buffer[i] == Sentinel[0] && buffer[i + 1] == Sentinel[1]
                && buffer[i + 2] == Sentinel[2] && buffer[i + 3] == Sentinel[3])
            {
                untouched++;
            }
        }

        return untouched / (length / 4d);
    }

    private static void WritePreview(string masterPath, string previewPath, int width, int height)
    {
        var scale = Math.Min(1d, (double)GeoreferencedMapOptions.StoredPreviewPixels / Math.Max(width, height));
        var w = Math.Max(1, (int)Math.Round(width * scale));
        var h = Math.Max(1, (int)Math.Round(height * scale));
        using var master = Gdal.Open(masterPath, Access.GA_ReadOnly)
            ?? throw new VectorIOException("The drawing could not be read back.");
        using var preview = Gdal.wrapper_GDALTranslate(
            previewPath,
            master,
            new GDALTranslateOptions(
            [
                "-of", "WEBP", "-outsize", w.ToString(CultureInfo.InvariantCulture), h.ToString(CultureInfo.InvariantCulture),
                "-r", "average", "-co", "QUALITY=85",
            ]),
            null,
            null) ?? throw new VectorIOException("The preview picture could not be written.");
    }

    private static int CountPages(string sourcePath)
    {
        using var whole = OpenPdf(sourcePath, ["DPI=72"])
            ?? throw new VectorIOException("The file could not be opened as a portable document.");

        // A one-page document opens as that page; a longer one lists its pages as sub-datasets,
        // a name and a description each.
        var subdatasets = whole.GetMetadata("SUBDATASETS");
        return subdatasets is { Length: > 0 }
            ? subdatasets.Count(e => e.Contains("_NAME=", StringComparison.Ordinal))
            : 1;
    }

    private static Dataset? Open(string sourcePath, int page, double dpi, IReadOnlyList<string>? layers)
    {
        var options = new List<string>
        {
            "DPI=" + dpi.ToString("0.####", CultureInfo.InvariantCulture),
            "BANDS=4",
        };
        if (layers is not null)
        {
            // Naming layers turns those on and every other one off; an empty choice turns all of
            // them off, which draws only what the page holds outside any layer.
            options.Add(layers.Count == 0 ? "LAYERS_OFF=ALL" : "LAYERS=" + string.Join(",", layers));
        }

        return OpenPdf($"PDF:{page}:{sourcePath}", [.. options]);
    }

    /// <summary>
    /// Opens a portable document with the document driver only, quietly. A file that only claims
    /// to be one is refused rather than handed to whichever driver recognises it.
    /// </summary>
    private static Dataset? OpenPdf(string name, string[] openOptions)
    {
        Gdal.PushErrorHandler("CPLQuietErrorHandler");
        try
        {
            return Gdal.OpenEx(
                name,
                (uint)(GdalConst.OF_RASTER | GdalConst.OF_READONLY),
                allowed_drivers: ["PDF"],
                open_options: openOptions,
                sibling_files: null);
        }
        catch (ApplicationException)
        {
            return null;
        }
        finally
        {
            Gdal.PopErrorHandler();
        }
    }

    private static EmbeddedGeoreference? EmbeddedOf(Dataset dataset)
    {
        var gt = new double[6];
        dataset.GetGeoTransform(gt);
        var wkt = dataset.GetProjection();
        if (RasterPlacement.IsUnplaced(gt) || string.IsNullOrWhiteSpace(wkt))
        {
            return null;
        }

        Geometry? neatline = null;
        var neatlineWkt = dataset.GetMetadataItem("NEATLINE", "");
        if (!string.IsNullOrWhiteSpace(neatlineWkt))
        {
            try
            {
                neatline = new WKTReader().Read(neatlineWkt);
            }
            catch (ParseException)
            {
                neatline = null;
            }
        }

        return new EmbeddedGeoreference(gt, wkt, neatline);
    }
}
