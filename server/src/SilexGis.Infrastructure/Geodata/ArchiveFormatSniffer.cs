// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using SilexGis.Domain;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// Tells apart the two vector formats that arrive as a zip archive.
///
/// <para>
/// A shapefile has to be zipped to travel at all, and a KMZ is a zipped KML by definition, so
/// the extension cannot decide: a KMZ renamed <c>.zip</c> is common (mail servers and chat
/// clients strip the unfamiliar one), and reading it as a shapefile fails with a message about
/// a missing <c>.shp</c> that says nothing useful. Looking inside costs one directory read of
/// an archive that is about to be read in full anyway.
/// </para>
/// </summary>
public static class ArchiveFormatSniffer
{
    /// <summary>
    /// The format of a zip archive, or null when it holds neither a KML nor a shapefile.
    /// A KML wins over a shapefile if an archive somehow carries both — a KMZ is only ever a
    /// KML, while a shapefile bundle regularly ships alongside a preview document.
    /// </summary>
    public static GeofileFormat? Detect(string absolutePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(absolutePath);
            var hasKml = false;
            var hasShapefile = false;
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase))
                {
                    hasKml = true;
                }
                else if (entry.FullName.EndsWith(".shp", StringComparison.OrdinalIgnoreCase))
                {
                    hasShapefile = true;
                }
            }

            return hasKml ? GeofileFormat.Kmz : hasShapefile ? GeofileFormat.Shapefile : null;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
