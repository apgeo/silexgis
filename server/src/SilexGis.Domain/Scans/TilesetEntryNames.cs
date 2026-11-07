// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Scans;

/// <summary>
/// Which names inside a scan's tileset archive may be asked for by address. The archive is
/// packed by the tiler from a directory it wrote: a <c>tileset.json</c> at the root, point tiles
/// (<c>.pnts</c>) and, for a large cloud, further <c>.json</c> tilesets the root one points at,
/// all named with plain characters and forward slashes. An address that names anything else is
/// not an entry this application serves, whether or not the archive happens to hold it.
/// </summary>
/// <remarks>
/// The rule is on the name alone and is decided before the archive is opened, so that a name
/// built to walk out of the archive (<c>..</c>, a leading slash, a backslash, an empty segment)
/// or to fish for something that is not a tile (a stray text file, the archive's own manifest)
/// gets exactly the answer an entry that does not exist gets. Nothing here reads the archive,
/// which keeps the question "is this name even askable" apart from "is it there".
/// </remarks>
public static class TilesetEntryNames
{
    /// <summary>The entry a viewer starts from; the one address a listing hands out.</summary>
    public const string Root = "tileset.json";

    /// <summary>
    /// The media type an entry is served as: tilesets are JSON, point tiles are opaque bytes.
    /// Null for a name this application does not serve.
    /// </summary>
    public static string? ContentTypeOf(string? name)
    {
        if (!IsServed(name))
        {
            return null;
        }

        return name!.EndsWith(".json", StringComparison.Ordinal) ? "application/json" : "application/octet-stream";
    }

    /// <summary>
    /// True for a relative path of one or more plain segments, the last of which ends in
    /// <c>.json</c> or <c>.pnts</c>. Segments hold ASCII letters, digits, dot, dash and underscore,
    /// never start with a dot, and are separated by single forward slashes.
    /// </summary>
    public static bool IsServed(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 512)
        {
            return false;
        }

        if (!(name.EndsWith(".json", StringComparison.Ordinal) || name.EndsWith(".pnts", StringComparison.Ordinal)))
        {
            return false;
        }

        foreach (var segment in name.Split('/'))
        {
            if (!IsPlainSegment(segment))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPlainSegment(string segment)
    {
        // An empty segment is a doubled, leading or trailing slash; a leading dot covers "." and
        // ".." as well as hidden files. The extension check above already ruled out a bare "x.".
        if (segment.Length == 0 || segment[0] == '.')
        {
            return false;
        }

        foreach (var c in segment)
        {
            var plain = c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '.' or '-' or '_';
            if (!plain)
            {
                return false;
            }
        }

        return true;
    }
}
