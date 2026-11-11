// SPDX-License-Identifier: AGPL-3.0-or-later
using OSGeo.GDAL;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>Band choices for the translations that make a picture into a sheet's drawing.</summary>
internal static class RasterBands
{
    /// <summary>
    /// The bands that give a picture a see-through channel: its own fourth band, or — for a
    /// picture stored without one because every pixel was opaque, which is what the WebP writer
    /// does with an opaque page — its mask, which then reads as fully opaque.
    /// </summary>
    public static string[] WithAlpha(Dataset picture) => picture.RasterCount >= 4
        ? ["-b", "1", "-b", "2", "-b", "3", "-b", "4"]
        : picture.RasterCount == 3
            ? ["-b", "1", "-b", "2", "-b", "3", "-b", "mask", "-colorinterp_4", "alpha"]
            : ["-b", "1", "-b", "1", "-b", "1", "-b", "mask", "-colorinterp_4", "alpha"];
}
