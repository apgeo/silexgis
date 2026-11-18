// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// How far an uploaded vector or raster file may expand once the geodata library reads it.
/// </summary>
/// <remarks>
/// An upload's own size says little about what reading it costs. A zipped shapefile or a KMZ is
/// compressed, and repeated records compress about a thousand to one; a GeoTIFF states its
/// dimensions in its header and may leave every tile empty. These are read in the server's own
/// job worker, so a file built to expand takes memory, disk and the job queue from every other
/// request. Each ceiling is checked from what the file states about itself before the expensive
/// part starts, and the file is refused by name when it is passed.
/// </remarks>
public sealed class UploadedGeodataOptions
{
    public const string SectionName = "UploadedGeodata";

    /// <summary>
    /// The most a zipped shapefile's entries may state they expand to, in bytes, all entries
    /// together. Shapefile records are read one at a time, so this bounds disk and read time
    /// rather than memory.
    /// </summary>
    public long MaxZippedShapefileBytes { get; set; } = 1L * 1024 * 1024 * 1024;

    /// <summary>
    /// The most the KML document inside a KMZ may state it expands to, in bytes. Lower than the
    /// shapefile figure because the KML reader builds the whole document in memory, at several
    /// times its size.
    /// </summary>
    public long MaxKmzDocumentBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>
    /// The most features one vector file may hold. A multi-part feature counts once per part,
    /// since each part is kept as a feature of its own.
    /// </summary>
    public int MaxFeatures { get; set; } = 500_000;

    /// <summary>
    /// The most pixel values an uploaded raster map may state — width × height × bands. The
    /// default is two gigapixels in three bands, about a 45,000-pixel square colour scan.
    /// </summary>
    public long MaxRasterPixelValues { get; set; } = 6_000_000_000;
}
