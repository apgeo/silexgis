// SPDX-License-Identifier: AGPL-3.0-or-later
using OSGeo.GDAL;
using OSGeo.OGR;
using SilexGis.Domain.Terrain;

namespace SilexGis.Infrastructure.Terrain;

/// <content>
/// Contour lines, drawn as one more picture of the ground.
/// </content>
/// <remarks>
/// <para>
/// The raster library traces the lines and the raster library draws them: lines of equal height
/// are followed through the elevation, held in memory as geometry for exactly as long as it takes
/// to burn them into a raster, and thrown away. What is kept is the raster — the same kind of file
/// every other picture here is, on the footprint of the elevation it was traced from — so nothing
/// that stores, serves or draws a picture has to know this one began as lines.
/// </para>
/// <para>
/// Traced on the grid of degrees and needing no correction for it. A contour is a set of places,
/// not a measurement across the ground: where the height is six hundred metres does not depend on
/// how wide a cell is.
/// </para>
/// </remarks>
public sealed partial class GdalDemDerivatives
{
    // What a cell of the drawing holds before it is given a colour.
    private const byte Clear = 0;
    private const byte OrdinaryLine = 1;
    private const byte IndexLine = 2;

    /// <summary>The colour of an ordinary line: a mid grey-brown, opaque.</summary>
    /// <remarks>
    /// The brown printed maps draw relief in, greyed so that it reads over a satellite picture and
    /// over a pale street map alike without competing with a cave drawn on top of it.
    /// </remarks>
    private static readonly byte[] OrdinaryColour = [148, 124, 100, 255];

    /// <summary>The colour of every fifth line: the same brown, darker.</summary>
    private static readonly byte[] IndexColour = [92, 68, 44, 255];

    /// <summary>How many cells of the drawing are thickened at a time.</summary>
    private const int CellsThickenedAtOnce = 4_000_000;

    /// <summary>How many cells of elevation are traced at a time.</summary>
    /// <remarks>
    /// The lines traced through a run of rows are held in memory until they have been drawn, and
    /// how much that is depends on the ground and on the spacing asked for rather than on anything
    /// that can be bounded in advance: steep country traced at a metre is a dozen lines through
    /// every cell, and a whole one-degree raster of it is gigabytes of geometry on the machine
    /// that is also serving the application. Half a million cells of the same country is a few
    /// hundred megabytes at the very worst, and a few at the usual spacing.
    /// </remarks>
    private const int CellsTracedAtOnce = 500_000;

    /// <summary>
    /// Traces the contours of an elevation raster and writes them out as a picture: lines on a
    /// ground that is clear everywhere else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Drawn finer than the elevation, by as much as the rule for these pictures allows, and
    /// placed on the same ground corner to corner. Every line is traced and drawn one cell wide;
    /// then the lines at every fifth height are traced again by themselves, drawn over the first
    /// set and thickened to two cells. Tracing those twice costs a fifth as much again and spares
    /// visiting every line to ask which kind it is.
    /// </para>
    /// <para>
    /// The elevation is worked through a run of rows at a time, and each run draws only the rows
    /// of the picture that lie over it. It is read with a few rows of its neighbours on either
    /// side, because a line is traced between the middles of cells: without them the lines along
    /// the edge of a run would be continued past the last real height rather than traced, and the
    /// join between two runs would show as a kink through every line that crosses it.
    /// </para>
    /// <para>
    /// The drawing is one byte a cell saying which kind of line, if any, passes through it. It is
    /// given its colours only as it is written out, by a palette the writer expands into red,
    /// green, blue and transparency — so the picture in memory is a quarter of the picture on
    /// disk, and no second copy of it in colour is ever held.
    /// </para>
    /// </remarks>
    private static void DrawContours(
        Dataset source,
        string partial,
        TerrainDerivativeSettings settings,
        string sentence,
        CancellationToken ct)
    {
        var interval = settings.ContourIntervalMetres ?? TerrainContourLines.DefaultIntervalMetres;
        var (width, height) = TerrainContourLines.PictureSize(source.RasterXSize, source.RasterYSize);

        using var drawing = Produce(
            () => Gdal.GetDriverByName("MEM")?.Create(
                string.Empty, width, height, 1, DataType.GDT_Byte, null),
            sentence);

        // The same corner and the same extent, in more and smaller cells.
        var placed = new double[6];
        source.GetGeoTransform(placed);
        var across = (double)source.RasterXSize / width;
        var down = (double)source.RasterYSize / height;
        drawing.SetGeoTransform(
        [
            placed[0], placed[1] * across, placed[2] * down,
            placed[3], placed[4] * across, placed[5] * down,
        ]);
        drawing.SetProjection(source.GetProjection());

        using var cells = drawing.GetRasterBand(1);

        var rowsAtOnce = Math.Max(1, CellsTracedAtOnce / source.RasterXSize);

        // Enough neighbouring rows that every row of the picture a run draws lies between real
        // heights, including where the picture's rows do not start exactly on an elevation row.
        var neighbours = 2 + (int)Math.Ceiling(down);

        for (var first = 0; first < source.RasterYSize; first += rowsAtOnce)
        {
            ct.ThrowIfCancellationRequested();

            var last = Math.Min(source.RasterYSize, first + rowsAtOnce);
            var from = (int)Math.Round(first / down);
            var to = last == source.RasterYSize ? height : (int)Math.Round(last / down);
            if (to <= from)
            {
                continue;
            }

            var top = Math.Max(0, first - neighbours);
            var bottom = Math.Min(source.RasterYSize, last + neighbours);

            using var run = Produce(
                () => Rows(source, top, bottom - top, source.RasterXSize), sentence);
            using var strip = Produce(
                () => Gdal.GetDriverByName("MEM")?.Create(
                    string.Empty, width, to - from, 1, DataType.GDT_Byte, null),
                sentence);

            // The rows of the picture from this one down, and no others: whatever of a line falls
            // outside them belongs to the run that draws those rows.
            strip.SetGeoTransform(
            [
                placed[0] + (from * placed[2] * down), placed[1] * across, placed[2] * down,
                placed[3] + (from * placed[5] * down), placed[4] * across, placed[5] * down,
            ]);
            strip.SetProjection(source.GetProjection());

            using (var heights = run.GetRasterBand(1))
            {
                Trace(source, heights, strip, interval, OrdinaryLine, sentence);
                Trace(
                    source, heights, strip, interval * TerrainContourLines.IndexEvery, IndexLine, sentence);
            }

            Place(strip, cells, from, width, to - from, sentence);
        }

        ct.ThrowIfCancellationRequested();

        Thicken(cells, width, height, sentence);
        Colour(cells);

        // Coarser levels are averaged rather than sampled: sampled, a line one cell wide survives
        // in one cell out of two and is drawn as a row of dashes; averaged, it fades.
        using var coloured = new GDALTranslateOptions(
            ["-expand", "rgba", .. CloudOptimised, "-co", "OVERVIEW_RESAMPLING=AVERAGE"]);
        using var written = Produce(
            () => Gdal.wrapper_GDALTranslate(partial, drawing, coloured, null, null), sentence);

        written.FlushCache();
    }

    /// <summary>
    /// Follows every line of equal height at one spacing through a run of elevation rows and
    /// marks the cells of the drawing it passes through.
    /// </summary>
    /// <remarks>
    /// Counted from sea level, so that lines traced at five times a spacing fall exactly on every
    /// fifth line traced at that spacing. A cell holding the marker for a hole is told to the
    /// tracer as one: otherwise the edge of every gap in the elevation is ringed by all the lines
    /// between the ground and the marker, ten thousand metres below it.
    /// </remarks>
    private static void Trace(
        Dataset source, Band heights, Dataset drawing, double interval, byte mark, string sentence)
    {
        using var held = Produce(
            () => Ogr.GetDriverByName("MEM")?.CreateDataSource("contours", null), sentence);
        using var reference = source.GetSpatialRef();
        using var lines = Produce(
            () => held.CreateLayer("lines", reference, wkbGeometryType.wkbLineString, null), sentence);

        heights.GetNoDataValue(out var hole, out var marked);

        Gdal.ErrorReset();
        if (Gdal.ContourGenerate(heights, interval, 0d, 0, null, marked, hole, lines, -1, -1, null, null)
            != (int)CPLErr.CE_None)
        {
            throw new TerrainBuildException(
                TerrainBuildFailures.DerivativeFailed, sentence, Reason(null));
        }

        Gdal.ErrorReset();
        if (Gdal.RasterizeLayer(
                drawing, 1, [1], lines, IntPtr.Zero, IntPtr.Zero, 1, [mark], null, null, null)
            != (int)CPLErr.CE_None)
        {
            throw new TerrainBuildException(
                TerrainBuildFailures.DerivativeFailed, sentence, Reason(null));
        }
    }

    /// <summary>Puts the rows one run drew where they belong in the whole drawing.</summary>
    private static void Place(Dataset strip, Band cells, int firstRow, int width, int rows, string sentence)
    {
        var drawn = new byte[width * rows];
        using var from = strip.GetRasterBand(1);

        Gdal.ErrorReset();
        if (from.ReadRaster(0, 0, width, rows, drawn, width, rows, 0, 0) != CPLErr.CE_None
            || cells.WriteRaster(0, firstRow, width, rows, drawn, width, rows, 0, 0) != CPLErr.CE_None)
        {
            throw new TerrainBuildException(
                TerrainBuildFailures.DerivativeFailed, sentence, Reason(null));
        }
    }

    /// <summary>Widens every heavier line from one cell to two.</summary>
    /// <remarks>
    /// A cell becomes part of a heavier line if the cell to its left, the cell above it or the
    /// cell above that one is — each traced cell grows into the square of four it is the corner
    /// of. Decided from the drawing as it was traced, never from cells this has already widened,
    /// or a line would grow by another cell with every row it was carried down. Worked through a
    /// run of rows at a time so that what it holds does not grow with the picture.
    /// </remarks>
    private static void Thicken(Band cells, int width, int height, string sentence)
    {
        var rows = Math.Clamp(CellsThickenedAtOnce / width, 1, height);
        var traced = new byte[width * rows];
        var drawn = new byte[width * rows];

        // The last row of the run before, as it was traced. Clear above the first.
        var above = new byte[width];

        for (var first = 0; first < height; first += rows)
        {
            var count = Math.Min(rows, height - first);

            Gdal.ErrorReset();
            if (cells.ReadRaster(0, first, width, count, traced, width, count, 0, 0) != CPLErr.CE_None)
            {
                throw new TerrainBuildException(
                    TerrainBuildFailures.DerivativeFailed, sentence, Reason(null));
            }

            Array.Copy(traced, drawn, width * count);

            for (var row = 0; row < count; row++)
            {
                var here = row * width;
                var over = row == 0 ? above.AsSpan() : traced.AsSpan(here - width, width);

                for (var column = 0; column < width; column++)
                {
                    if (over[column] == IndexLine
                        || (column > 0
                            && (traced[here + column - 1] == IndexLine || over[column - 1] == IndexLine)))
                    {
                        drawn[here + column] = IndexLine;
                    }
                }
            }

            Array.Copy(traced, (count - 1) * width, above, 0, width);

            Gdal.ErrorReset();
            if (cells.WriteRaster(0, first, width, count, drawn, width, count, 0, 0) != CPLErr.CE_None)
            {
                throw new TerrainBuildException(
                    TerrainBuildFailures.DerivativeFailed, sentence, Reason(null));
            }
        }
    }

    /// <summary>
    /// Says what colour each kind of cell is: nothing at all where no line passes, and the two
    /// browns where one does.
    /// </summary>
    private static void Colour(Band cells)
    {
        using var palette = new ColorTable(PaletteInterp.GPI_RGB);
        Enter(palette, Clear, [0, 0, 0, 0]);
        Enter(palette, OrdinaryLine, OrdinaryColour);
        Enter(palette, IndexLine, IndexColour);

        cells.SetRasterColorTable(palette);
        cells.SetRasterColorInterpretation(ColorInterp.GCI_PaletteIndex);

        static void Enter(ColorTable table, byte kind, byte[] colour)
        {
            using var entry = new ColorEntry
            {
                c1 = colour[0],
                c2 = colour[1],
                c3 = colour[2],
                c4 = colour[3],
            };

            table.SetColorEntry(kind, entry);
        }
    }
}
