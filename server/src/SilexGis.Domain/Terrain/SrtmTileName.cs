// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;

namespace SilexGis.Domain.Terrain;

/// <summary>
/// The south-west corner of an elevation tile whose file name is the only thing that says where it
/// is.
/// </summary>
/// <remarks>
/// <para>
/// A raw <c>.hgt</c> is a headerless square of big-endian 16-bit samples: no coordinate system, no
/// grid, no corner, nothing inside the file that places it on the earth. The one thing that does is
/// its name — <c>N45E024.hgt</c> is the degree square whose south-west corner is 45°N 24°E — and the
/// raster library identifies the format by matching that name and reads the corner straight out of
/// it. A tile under any other name is not a tile that is hard to place; it is bytes nothing can
/// place at all.
/// </para>
/// <para>
/// So the name is parsed rather than trusted, and what comes out is two integers. Whatever is built
/// from those integers afterwards is built by this application out of numbers it has checked, which
/// is what lets a file arrive from a browser — where the name is the one part of it somebody outside
/// chose — without any of the bytes of that name reaching a path.
/// </para>
/// </remarks>
/// <param name="South">Latitude of the tile's south edge, whole degrees, negative below the equator.</param>
/// <param name="West">Longitude of the tile's west edge, whole degrees, negative west of Greenwich.</param>
public readonly record struct SrtmTileName(int South, int West)
{
    /// <summary>The extension a tile named this way arrives under.</summary>
    public const string Extension = ".hgt";

    /// <summary>
    /// The corner this tile is at, read out of a file name, or <c>null</c> when the name does not
    /// name a tile.
    /// </summary>
    /// <remarks>
    /// The leading seven characters are what carries the corner, and the rest of the name has only
    /// to be an extension this recognises. Publishers do put something in between — a tile
    /// distributed as <c>N45E025.SRTMGL1.hgt</c> is the same square as <c>N45E025.hgt</c> — and
    /// refusing those would be refusing files that are perfectly placeable over a naming detail.
    /// Nothing from the middle is kept: <see cref="FileName"/> is rebuilt from the two integers.
    /// </remarks>
    public static SrtmTileName? Parse(string? fileName)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? null : Path.GetFileName(fileName);

        // Seven characters of corner, then a dot beginning whatever is left, which must end in the
        // extension. "N45E024.hgt" is the shortest thing that can pass.
        if (name is null || name.Length < 11 || name[7] != '.'
            || !name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var latitude = Degrees(name.AsSpan(1, 2));
        var longitude = Degrees(name.AsSpan(4, 3));
        if (latitude is null || longitude is null)
        {
            return null;
        }

        var south = char.ToUpperInvariant(name[0]) switch
        {
            'N' => latitude.Value,
            'S' => -latitude.Value,
            _ => (int?)null,
        };

        var west = char.ToUpperInvariant(name[3]) switch
        {
            'E' => longitude.Value,
            'W' => -longitude.Value,
            _ => (int?)null,
        };

        // A corner off the earth is a name that happens to have the right shape rather than a tile.
        // The south edge stops at 89 because a tile covers the degree above the corner it names.
        if (south is null || west is null || south is < -90 or > 89 || west is < -180 or > 179)
        {
            return null;
        }

        return new SrtmTileName(south.Value, west.Value);
    }

    /// <summary>
    /// The canonical name for this tile: the one the raster library recognises, built from the two
    /// integers and never from the characters they were read out of.
    /// </summary>
    public string FileName => string.Create(
        CultureInfo.InvariantCulture,
        $"{(South < 0 ? 'S' : 'N')}{Math.Abs(South):D2}{(West < 0 ? 'W' : 'E')}{Math.Abs(West):D3}{Extension}");

    /// <summary>Whether this name carries the extension whose georeferencing lives in the name.</summary>
    public static bool IsTileExtension(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && Extension.Equals(Path.GetExtension(fileName), StringComparison.OrdinalIgnoreCase);

    private static int? Degrees(ReadOnlySpan<char> digits) =>
        int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
