// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using ImageMagick;
using MaxRev.Gdal.Core;
using OSGeo.GDAL;
using SilexGis.Domain;

namespace SilexGis.Infrastructure.Files;

/// <summary>
/// Pictures of the pages of a paged document, generated on first request and cached in the
/// file store under pages/. WebP output, bounded by a square box, aspect ratio preserved.
/// </summary>
/// <remarks>
/// <para>
/// This exists so a document can be read in the application rather than downloaded and opened
/// elsewhere, and so that reading it stays inside the same rule everything else obeys: what a
/// viewer receives is a rendering this application produced, never the stored bytes. A picture
/// of a page carries nothing the page did not show, which is why it can be handed to a caller
/// who may not have the file itself.
/// </para>
/// <para>
/// The pages are drawn by the raster library the installation already ships for its maps,
/// which reads a PDF page as an image. Nothing is added to the runtime for this, and in
/// particular nothing is shipped to the browser: the same rendering serves a phone, a desktop
/// and a print preview, and the one machine that has to be able to draw the page is the one
/// that already has the file.
/// </para>
/// <para>
/// Text is drawn with whatever fonts the host has. A machine with no fonts installed renders
/// the layout of a text-heavy page but can drop glyphs from it, so the extracted text — not
/// the picture — remains what search and accessibility read.
/// </para>
/// </remarks>
public sealed class PageRenderService(IFileStore fileStore)
{
    static PageRenderService() => GdalBase.ConfigureAll();

    /// <summary>
    /// Allowed bounding-box sizes (px) for a page picture — a fixed set keeps the cache small.
    /// </summary>
    /// <remarks>
    /// The three smaller ones are the sizes every other rendering in the application comes in,
    /// so a page strip and a gallery tile cost the same. The largest one exists because a page
    /// of a scanned report is meant to be *read*: at 1200px across, a dense A4 scan lands near
    /// 100 dots per inch, which is legible only just, and the whole point of showing the page
    /// is that someone can read the words on it.
    /// </remarks>
    public static readonly int[] AllowedSizes = [160, 480, 1200, 2400];

    /// <summary>
    /// Whether a page of a file in this format can be drawn at all.
    /// </summary>
    /// <remarks>
    /// Only a PDF, and deliberately so: it is the one format whose own pages are pages, and it
    /// is the one the bundled raster library reads. A word-processor document has pages only
    /// once something has chosen a paper size and a font, which is a decision no reader of the
    /// file makes — inventing one to draw a picture would put a page break somewhere the author
    /// never put one.
    /// </remarks>
    public static bool CanRender(string? mimeType) =>
        string.Equals(mimeType, "application/pdf", StringComparison.OrdinalIgnoreCase);

    private static string CachePath(Guid fileId, int page, int size) =>
        $"pages/{fileId:N}-p{page}-{size}.webp";

    /// <summary>Deletes every cached page picture of a file (no-op when none exist).</summary>
    /// <remarks>
    /// Walks the directory rather than the page numbers: the number of pages is read out of the
    /// file, so a file whose row is already gone could not be asked how many it had, and a
    /// cached picture nobody can name is a cached picture nobody deletes.
    /// </remarks>
    public void Purge(Guid fileId)
    {
        var directory = fileStore.GetAbsolutePath("pages");
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(directory, $"{fileId:N}-p*.webp"))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Returns the absolute path of the cached picture of one page, drawing it when missing, or
    /// null when the document has no such page. Page numbers are 1-based.
    /// </summary>
    /// <remarks>
    /// A page that is not there is answered as an absence rather than as a failure, and the two
    /// are kept apart on purpose: a request for page four hundred of a ten-page report is a
    /// question with an answer, while a file that will not open is a fault worth reporting as
    /// one. The number of pages is not read from the row here because the row learns it only
    /// once the document's text has been read, which may not have happened yet.
    /// </remarks>
    public async Task<string?> GetOrCreateAsync(
        Guid fileId,
        string sourceStoragePath,
        int page,
        int size,
        CancellationToken ct)
    {
        if (!AllowedSizes.Contains(size))
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Unsupported page render size.");
        }

        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(page), page, "Page numbers are 1-based.");
        }

        var cachePath = fileStore.GetAbsolutePath(CachePath(fileId, page, size));
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

        var sourcePath = fileStore.GetAbsolutePath(sourceStoragePath);
        using var image = Render(sourcePath, page, size);
        if (image is null)
        {
            return null;
        }


        image.Quality = 82;
        // Write to a temp name then move — concurrent first requests must not serve half files.
        var tempPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
        await image.WriteAsync(tempPath, MagickFormat.WebP, ct);
        try
        {
            File.Move(tempPath, cachePath, overwrite: false);
        }
        catch (IOException)
        {
            File.Delete(tempPath); // another request won the race; its result is fine
        }

        return cachePath;
    }

    /// <summary>
    /// Draws one page into an image bounded by <paramref name="size"/> in both directions.
    /// </summary>
    /// <remarks>
    /// The page is opened twice. The first open asks only how large the page is, at the
    /// resolution a PDF measures itself in, which costs nothing because nothing is drawn until
    /// the pixels are asked for. The resolution for the real draw is then chosen so the page
    /// comes out at about the size that was asked for — drawing a poster-sized page at a fixed
    /// high resolution and shrinking it afterwards would spend hundreds of megabytes to throw
    /// almost all of them away.
    /// </remarks>
    private static MagickImage? Render(string sourcePath, int page, int size)
    {
        const double pdfPointsPerInch = 72.0;

        using var probe = Open(sourcePath, page, pdfPointsPerInch);
        if (probe is null || probe.RasterXSize <= 0 || probe.RasterYSize <= 0)
        {
            // A page past the end and a file that will not open look the same from here, so the
            // first page is asked for as well: every document has one, and a document that
            // cannot produce it is a document that cannot be read at all.
            if (page > 1)
            {
                using var first = Open(sourcePath, 1, pdfPointsPerInch);
                if (first is not null && first.RasterXSize > 0)
                {
                    return null; // the file reads; it simply does not go up to this page
                }
            }

            throw new InvalidOperationException("The document could not be read as pages.");
        }

        var pointsWide = probe.RasterXSize;
        var pointsHigh = probe.RasterYSize;

        // The resolution is chosen so the longer side of the page lands on the box that was
        // asked for. There is deliberately no "never enlarge" rule here, which is the rule the
        // thumbnails obey: a photograph has a size of its own and blowing it up invents pixels,
        // whereas a page has none — it is a description of marks on paper, and asking for it at
        // a higher resolution draws the same marks more finely. A scan of a page is stored
        // inside it at the scanner's resolution, so the detail recovered by asking for more is
        // detail the file really holds. Capping this at the resolution a page measures itself
        // in would flatten every ordinary page to that resolution, and the largest sizes — the
        // ones that exist so the words can be read — would come back the same picture as the
        // smaller ones.
        //
        // The bounds are guards rather than policy. The lower one keeps a very large page — a
        // survey sheet metres across — from collapsing to a resolution that produces almost no
        // pixels at all; what it draws is then brought down to the box below. The upper one
        // keeps a page a few centimetres across from being blown up to fill a box far larger
        // than anything it could usefully show.
        var scale = (double)size / Math.Max(pointsWide, pointsHigh);
        var dpi = Math.Clamp(pdfPointsPerInch * scale, 18.0, 600.0);

        using var dataset = Open(sourcePath, page, dpi)
            ?? throw new InvalidOperationException("The page could not be opened.");
        var width = dataset.RasterXSize;
        var height = dataset.RasterYSize;
        var bands = Math.Min(dataset.RasterCount, 4);
        if (width <= 0 || height <= 0 || bands <= 0)
        {
            throw new InvalidOperationException("The page produced no pixels.");
        }

        var format = bands switch
        {
            1 => MagickFormat.Gray,
            4 => MagickFormat.Rgba,
            // Two bands is grey plus transparency, which no page produced here has ever been;
            // reading the first three of whatever is there keeps a surprise from being a crash.
            _ => MagickFormat.Rgb,
        };
        var readBands = format == MagickFormat.Rgb ? 3 : bands;
        var bandMap = Enumerable.Range(1, readBands).ToArray();

        var pixels = new byte[(long)width * height * readBands];
        // Band-interleaved-by-pixel, which is the order the image library reads raw bytes in.
        var error = dataset.ReadRaster(
            0, 0, width, height, pixels, width, height, readBands, bandMap,
            pixelSpace: readBands, lineSpace: width * readBands, bandSpace: 1);
        if (error != CPLErr.CE_None)
        {
            throw new InvalidOperationException("The page could not be drawn.");
        }

        var settings = new MagickReadSettings
        {
            Format = format,
            Width = (uint)width,
            Height = (uint)height,
            Depth = 8,
        };
        var image = new MagickImage(pixels, settings);
        try
        {
            // Ordinarily the resolution above already landed the page on the box, so this does
            // nothing. It fires where the resolution bounds took over — a page so large that
            // even the lowest useful resolution overshoots the box — and there the picture is
            // brought down to the size that was asked for rather than handed over oversized.
            if (image.Width > size || image.Height > size)
            {
                image.Thumbnail(new MagickGeometry((uint)size, (uint)size)); // keeps aspect ratio
            }

            // The pixels came out of a renderer, so there is nothing here that was ever in the
            // file's own metadata. Stripping anyway keeps one rule for every derivative this
            // application hands out rather than one rule per pipeline.
            image.Strip();
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens one page of a document as a raster at the given resolution.
    /// </summary>
    /// <remarks>
    /// A page is addressed the way the raster library addresses the parts of any multi-part
    /// dataset — by a name that carries the part number, not by an option on the file. Asking
    /// for the file itself and hoping an option picks the page out silently returns page one
    /// every time, which reads as a working page strip in which every page is the cover.
    /// </remarks>
    private static Dataset? Open(string sourcePath, int page, double dpi)
    {
        // A page the document does not have is reported by the library as a dataset it will not
        // open, which is also how it reports a file it cannot read. The caller tells the two
        // apart by having opened the file successfully once already.
        Gdal.PushErrorHandler("CPLQuietErrorHandler");
        try
        {
            return Gdal.OpenEx(
                $"PDF:{page}:{sourcePath}",
                (uint)(GdalConst.OF_RASTER | GdalConst.OF_READONLY),
                allowed_drivers: ["PDF"],
                open_options: [$"DPI={dpi.ToString("0.####", CultureInfo.InvariantCulture)}"],
                sibling_files: null);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The library reports a refusal to open either by returning nothing or by raising;
            // both mean the same thing to the caller, which decides what the refusal was about.
            return null;
        }
        finally
        {
            Gdal.PopErrorHandler();
        }
    }
}
