// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using SilexGis.Domain;
using SilexGis.Domain.Documents;

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
    /// More entries than a zipped shapefile set or a KMZ plausibly holds — a handful of sidecar
    /// files, or a document and its icons. Every reader of an uploaded geodata archive checks it
    /// from the archive's own records before the runtime builds an object for each of them.
    /// </summary>
    public const int MaxArchiveEntries = 10_000;

    /// <summary>
    /// The format of a zip archive, or null when it holds neither a KML nor a shapefile.
    /// A KML wins over a shapefile if an archive somehow carries both — a KMZ is only ever a
    /// KML, while a shapefile bundle regularly ships alongside a preview document.
    /// </summary>
    /// <remarks>
    /// This runs inside the upload request, so an archive stating more entries than
    /// <see cref="MaxArchiveEntries"/> is not opened here at all: it is answered null, the
    /// upload keeps the format its name gave it, and the import job's reader refuses it by name.
    /// </remarks>
    public static GeofileFormat? Detect(string absolutePath)
    {
        try
        {
            using var stream = File.OpenRead(absolutePath);
            if (ZipEntryCount.Exceeds(stream, MaxArchiveEntries))
            {
                return null;
            }

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
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
