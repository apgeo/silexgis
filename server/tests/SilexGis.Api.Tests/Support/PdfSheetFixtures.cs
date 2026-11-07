// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using MaxRev.Gdal.Core;
using OSGeo.GDAL;
using OSGeo.OSR;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Small invented map sheets as portable documents, made in-process: one the raster library writes
/// with georeferencing of its own, and hand-written ones whose structure a test needs exactly —
/// optional-content layers, a second page, and a malformed operator the drawing library complains
/// about while still drawing the page.
/// </summary>
internal static class PdfSheetFixtures
{
    /// <summary>
    /// A georeferenced document: a 400 × 300 picture whose top-left corner is at
    /// (<paramref name="x0"/>, <paramref name="y0"/>) in <paramref name="epsg"/>, one ground unit per
    /// <paramref name="unitsPerPixel"/>, written by the library with its georeferencing.
    /// </summary>
    public static byte[] GeoPdf(int epsg, double x0, double y0, double unitsPerPixel) =>
        LibraryPdf(epsg, [x0, unitsPerPixel, 0, y0, 0, -unitsPerPixel], "ISO32000");

    /// <summary>The same picture written with no georeferencing at all.</summary>
    public static byte[] PlainPdf() => LibraryPdf(4326, [24, 0.0001, 0, 45.5, 0, -0.0001], "NONE");

    /// <summary>A one-page sheet with two optional-content layers, "Walls" and "Labels".</summary>
    public static byte[] LayeredPdf() => Pdf(
        [Page("/OC /walls BDC 0 0 0 RG 2 w 20 20 m 380 280 l S EMC\n/OC /labels BDC 0 0 1 rg 50 200 40 40 re f EMC\n",
            "/Properties << /walls 10 0 R /labels 11 0 R >>")],
        "/OCProperties << /OCGs [10 0 R 11 0 R] /D << /Order [10 0 R 11 0 R] >> >>",
        [(10, "<< /Type /OCG /Name (Walls) >>"), (11, "<< /Type /OCG /Name (Labels) >>")]);

    /// <summary>A sheet of two pages, a line on each.</summary>
    public static byte[] TwoPagePdf() => Pdf(
        [Page("0 0 0 RG 2 w 20 20 m 380 280 l S\n", ""), Page("1 0 0 RG 2 w 20 280 m 380 20 l S\n", "")],
        "",
        []);

    /// <summary>
    /// A sheet whose content starts a marked-content section with too few operands. The drawing
    /// library reports it and still draws the rest of the page — what survey sheets exported by some
    /// programs do.
    /// </summary>
    public static byte[] WarningPdf() => Pdf(
        [Page("/OC BDC\n1 0 0 rg 20 20 200 150 re f\nEMC\n0 0 0 RG 2 w 20 20 m 380 280 l S\n", "")],
        "",
        []);

    private static (string Content, string Resources) Page(string content, string resources) => (content, resources);

    private static byte[] LibraryPdf(int epsg, double[] geoTransform, string encoding)
    {
        GdalBase.ConfigureAll();
        var path = Path.Combine(TestScratch.Root, $"silexgis-sheet-{Guid.NewGuid():N}.pdf");
        try
        {
            using (var picture = Gdal.GetDriverByName("MEM").Create("", 400, 300, 3, DataType.GDT_Byte, null))
            {
                picture.SetGeoTransform(geoTransform);
                using var srs = new SpatialReference("");
                srs.ImportFromEPSG(epsg);
                srs.ExportToWkt(out var wkt, null);
                picture.SetProjection(wkt);
                var row = new byte[400 * 300];
                for (var band = 1; band <= 3; band++)
                {
                    for (var i = 0; i < row.Length; i++)
                    {
                        row[i] = (byte)((i % 400) * band % 256);
                    }

                    picture.GetRasterBand(band).WriteRaster(0, 0, 400, 300, row, 400, 300, 0, 0);
                }

                Gdal.SetThreadLocalConfigOption("GDAL_PAM_ENABLED", "NO");
                try
                {
                    using var written = Gdal.GetDriverByName("PDF").CreateCopy(
                        path, picture, 0, [$"GEO_ENCODING={encoding}", "DPI=72"], null, null);
                }
                finally
                {
                    Gdal.SetThreadLocalConfigOption("GDAL_PAM_ENABLED", null);
                }
            }

            return File.ReadAllBytes(path);
        }
        finally
        {
            foreach (var leftover in new[] { path, path + ".aux.xml" })
            {
                if (File.Exists(leftover))
                {
                    File.Delete(leftover);
                }
            }
        }
    }

    /// <summary>
    /// A minimal portable document: catalogue, page tree, the pages (400 × 300 points each) with
    /// their content, and any extra objects at the numbers they name.
    /// </summary>
    private static byte[] Pdf(
        IReadOnlyList<(string Content, string Resources)> pages,
        string catalogueExtra,
        IReadOnlyList<(int Number, string Body)> extras)
    {
        var objects = new SortedDictionary<int, string>();
        var kids = string.Join(" ", pages.Select((_, i) => $"{3 + (2 * i)} 0 R"));
        objects[1] = $"<< /Type /Catalog /Pages 2 0 R {catalogueExtra} >>";
        objects[2] = $"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>";
        for (var i = 0; i < pages.Count; i++)
        {
            var pageNumber = 3 + (2 * i);
            objects[pageNumber] =
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 300] /Contents {pageNumber + 1} 0 R /Resources << {pages[i].Resources} >> >>";
            var content = pages[i].Content;
            objects[pageNumber + 1] = $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream";
        }

        foreach (var (number, body) in extras)
        {
            objects[number] = body;
        }

        var size = objects.Keys.Max() + 1;
        var output = new StringBuilder("%PDF-1.7\n");
        var offsets = new Dictionary<int, int>();
        foreach (var (number, body) in objects)
        {
            offsets[number] = Encoding.ASCII.GetByteCount(output.ToString());
            output.Append($"{number} 0 obj\n{body}\nendobj\n");
        }

        var xref = Encoding.ASCII.GetByteCount(output.ToString());
        output.Append($"xref\n0 {size}\n0000000000 65535 f \n");
        for (var n = 1; n < size; n++)
        {
            output.Append(offsets.TryGetValue(n, out var offset) ? $"{offset:D10} 00000 n \n" : "0000000000 65535 f \n");
        }

        output.Append($"trailer\n<< /Size {size} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(output.ToString());
    }
}
