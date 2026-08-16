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
}
