// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Terrain;

/// <summary>
/// Which files in a pile of them are elevation rasters this pipeline will try to read.
/// </summary>
/// <remarks>
/// A directory an operator points at holds more than rasters: world files, projection sidecars,
/// overview and statistics files a previous tool wrote, a readme, a licence. Handing those to a
/// raster tool is at best a wasted error and at worst a tool that reads a sidecar as if it were
/// data. The list is deliberately short and deliberately by extension: it is a filter over what
/// somebody meant to give us, not an attempt to identify a format, which is the raster library's
/// job and is done when the file is actually opened.
/// </remarks>
public static class TerrainRasterFiles
{
    /// <remarks>
    /// Every one of these holds its own pixels. A virtual mosaic is deliberately absent even though
    /// the raster library reads one: it is a document naming other files, by paths that may be
    /// absolute and may point anywhere on the machine, so accepting one as an input would be
    /// accepting a request to read whatever it names — from an upload, or from a directory somebody
    /// dropped a file into. Virtual mosaics are something this pipeline writes, not something it is
    /// given.
    /// </remarks>
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tif", ".tiff", ".img", ".asc", ".hgt", ".dem",
    };

    /// <summary>What a file still being written is called, until it is whole.</summary>
    /// <remarks>
    /// One name for the whole pipeline, because the rule it enforces is one rule: bytes sitting at
    /// the name a finished file would have are indistinguishable from a finished file. Anything that
    /// produces a raster — a transfer from the network, a copy off the server's own disk, a
    /// reprojection — writes under this name and renames only once the result has been read back and
    /// found whole. Deliberately not one of <see cref="Accepted"/>, so a fragment left behind by an
    /// interrupted run is never picked up as an input.
    /// </remarks>
    public const string PartialSuffix = ".part";

    /// <summary>Whether this name looks like a raster worth opening.</summary>
    public static bool IsRaster(string fileName) =>
        !string.IsNullOrWhiteSpace(fileName) && Extensions.Contains(Path.GetExtension(fileName));

    /// <summary>The extensions accepted, for a message that has to name them.</summary>
    public static IReadOnlyCollection<string> Accepted { get; } = [.. Extensions.Order()];
}
