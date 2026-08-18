// SPDX-License-Identifier: AGPL-3.0-or-later
using OSGeo.GDAL;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Geodata;

namespace SilexGis.Infrastructure.Terrain;

/// <summary>
/// Turns a tile whose position is written in its file name into a raster that carries its position
/// inside it.
/// </summary>
/// <remarks>
/// <para>
/// A raw <c>.hgt</c> says where it is only by being called <c>N45E024.hgt</c>: the bytes are a
/// headerless square of samples and the raster library places them by matching that name. Anywhere
/// the name is not kept — a store that issues its own names because a name somebody outside chose is
/// a path — the file stops being placeable, and it stops being placeable silently, because the
/// library answers a name it does not recognise the same way it answers a file that is not elevation
/// data at all.
/// </para>
/// <para>
/// Rather than bend the store into keeping a name it deliberately does not keep, the position is
/// moved out of the name and into the file, once, at the edge where the file arrives. What is
/// written is the same samples with the same value standing for a hole; what is added is the grid
/// the name implied, now stated where every later step will look for it. From here on the tile is an
/// ordinary raster that the library identifies by its contents, and nothing downstream has to know
/// it ever was anything else.
/// </para>
/// <para>
/// The name the tile is read under is built by this application from two integers it parsed and
/// range-checked, never from the characters they were parsed out of, so no part of a name chosen
/// outside this server reaches a path.
/// </para>
/// </remarks>
public static class ElevationTileConversion
{
    /// <summary>The only driver a file offered as a tile is allowed to be read by.</summary>
    private static readonly string[] TileDrivers = ["SRTMHGT"];

    /// <summary>
    /// Whether a file arriving under this name is one that has to be converted before it can be
    /// stored under a name of this server's choosing.
    /// </summary>
    public static bool NeedsConversion(string? fileName) => SrtmTileName.IsTileExtension(fileName);

    /// <summary>The extension a converted tile is stored under.</summary>
    public const string ConvertedExtension = ".tif";

    /// <summary>
    /// Reads the tile at <paramref name="tilePath"/> — which must already be named canonically —
    /// and writes it to <paramref name="outputPath"/> as a georeferenced raster.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The file is not a tile the library can read. Nearly always a length that is not one of the
    /// square grids the format comes in, which is what a truncated upload looks like.
    /// </exception>
    public static void ToGeoTiff(string tilePath, string outputPath)
    {
        GdalRuntime.Configure();

        Gdal.ErrorReset();
        using var tile = Gdal.OpenEx(
            tilePath,
            (uint)(GdalConst.OF_RASTER | GdalConst.OF_READONLY),
            allowed_drivers: TileDrivers,
            open_options: null,
            sibling_files: null);

        if (tile is null)
        {
            throw new InvalidDataException(
                "This file is named like an elevation tile but does not hold one. A tile is a whole "
                + "square of samples and its length says how wide it is, so the usual cause is a "
                + $"file that arrived cut short. {Reason()}");
        }

        var driver = Gdal.GetDriverByName("GTiff")
            ?? throw new InvalidOperationException("The raster library has no GeoTIFF driver.");

        // Compressed because a tile is mostly slowly-varying ground and halves on disk for nothing,
        // and tiled because every later reader of it asks for a window rather than whole rows.
        string[] options = ["COMPRESS=DEFLATE", "TILED=YES"];

        Gdal.ErrorReset();
        using var written = driver.CreateCopy(outputPath, tile, 0, options, null, null)
            ?? throw new InvalidDataException(
                $"This elevation tile could not be stored as a georeferenced raster. {Reason()}");

        // What is written is not all on disk until the handle that wrote it has flushed.
        written.FlushCache();
    }

    /// <summary>
    /// What the library said, if it said anything. Read immediately after the call that failed and
    /// immediately after a reset, which is the only way a return-code call says why.
    /// </summary>
    private static string Reason()
    {
        var message = Gdal.GetLastErrorMsg();
        return string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();
    }
}
