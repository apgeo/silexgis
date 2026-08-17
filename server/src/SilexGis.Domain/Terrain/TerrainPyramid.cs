// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Terrain;

/// <summary>
/// The shape of a finished pyramid on disk, as everything that writes, checks or serves one has to
/// agree it is.
/// </summary>
/// <remarks>
/// A pyramid is a manifest naming what ground is covered and at what detail, and a tree of tiles
/// under it addressed by level, column and row. Both halves have to be there: a manifest without
/// tiles draws nothing, and tiles without a manifest cannot be found at all — and neither of those
/// shows as an error anywhere. What is drawn instead is a smooth, plausible, empty globe.
/// </remarks>
public static class TerrainPyramid
{
    /// <summary>The manifest, at the root of the pyramid.</summary>
    public const string ManifestFileName = "layer.json";

    /// <summary>What every tile file is called at the end.</summary>
    public const string TileExtension = ".terrain";

    /// <summary>
    /// The path under this installation's own host that published pyramids are addressed from.
    /// </summary>
    /// <remarks>
    /// Its own prefix rather than the bare terrain path, because an installation may also have a
    /// pyramid an operator baked by hand and mounted for the web server to serve — and the two
    /// would otherwise be two things at one address, with whichever rule happened to be more
    /// specific deciding which was reachable.
    /// </remarks>
    public const string PublishedRoute = "/terrain/builds/";

    /// <summary>
    /// The single name a build's published pyramid is known by: the directory it is moved into and
    /// the segment of the address it is fetched from are the same string, deliberately, so that
    /// what is on disk and what a viewer asks for cannot drift apart.
    /// </summary>
    public static string PublishedName(Guid buildId) => buildId.ToString("N");

    /// <summary>
    /// Where a viewer reads this build's pyramid from, with the trailing slash the manifest is
    /// resolved against.
    /// </summary>
    /// <remarks>
    /// An address of its own per build, and never reused: the scene ignores a terrain source whose
    /// address has not changed, so making a different build the terrain has to move the address
    /// rather than change what sits behind one. It is also what makes a week-long cache on the
    /// tiles safe — a build's tiles never become different tiles, they are replaced by another
    /// build's, at another address.
    /// </remarks>
    public static string PublishedUrl(Guid buildId) => PublishedRoute + PublishedName(buildId) + "/";
}
