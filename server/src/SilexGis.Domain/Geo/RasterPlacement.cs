// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>
/// Whether a raster's six-number grid actually says where its pixels are.
/// </summary>
/// <remarks>
/// <para>
/// One home for the rule, because it is a judgement about data and not about a library: every path
/// that accepts a raster from somebody has to make the same call, and the two that existed made
/// different ones. The terrain pipeline refused an unplaced file and said why; the uploaded-raster
/// path refused only half the cases, so a scan re-saved out of a GeoTIFF by an image editor — a
/// file that keeps its projection tag and loses its tie points — was accepted and drawn one degree
/// per pixel off the west coast of Africa, with no warning anywhere.
/// </para>
/// <para>
/// The numbers are the standard six: origin x, pixel width, row rotation, origin y, column
/// rotation, pixel height. Nothing here needs a raster library, which is why the rule can live in
/// the domain and be reached from both sides.
/// </para>
/// </remarks>
public static class RasterPlacement
{
    /// <summary>The grid a raster reader reports for a file that carries no grid at all.</summary>
    /// <remarks>
    /// Reading the grid of an unplaced file does not fail — it answers with the identity, pixels
    /// one unit wide starting at zero, zero, and reports success. Anything reading the numbers
    /// alone therefore sees a perfectly well-formed placement at the origin of the coordinate
    /// system: for degrees, the Atlantic off the coast of Africa. Imagery covering exactly that
    /// square at exactly that scale does not exist, and a file that genuinely did would still be
    /// one nobody had placed.
    /// </remarks>
    private static readonly double[] Identity = [0, 1, 0, 0, 0, 1];

    /// <summary>Whether the grid says nothing about where the pixels are.</summary>
    /// <param name="geoTransform">
    /// The six-number grid as a raster library reports it. A shorter array is treated as unplaced
    /// rather than throwing: a caller holding fewer than six numbers has no placement either.
    /// </param>
    public static bool IsUnplaced(IReadOnlyList<double>? geoTransform)
    {
        if (geoTransform is null || geoTransform.Count < 6)
        {
            return true;
        }

        // A zero-width pixel is the degenerate case; the identity is the silent one.
        if (geoTransform[1] == 0 && geoTransform[2] == 0)
        {
            return true;
        }

        for (var i = 0; i < 6; i++)
        {
            if (geoTransform[i] != Identity[i])
            {
                return false;
            }
        }

        return true;
    }
}
