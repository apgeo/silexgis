// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SilexGis.Domain.Terrain;

namespace SilexGis.Infrastructure.Terrain;

/// <summary>
/// The manifest at the root of a pyramid, read and written the one way everything here agrees on.
/// </summary>
/// <remarks>
/// One home for it because three steps touch the same file — the check reads and stamps it, the
/// bake rewrites it after an addition, and whatever publishes serves it — and the discipline of
/// writing it under another name and renaming into place has to hold at every one of them. A
/// manifest is read by a browser while tiles are being asked for, and half of one is a pyramid
/// that cannot be read at all, with the previous whole one already gone.
/// </remarks>
internal static class TerrainManifestFile
{
    /// <summary>
    /// The manifest, or nothing if it is absent or is not one.
    /// </summary>
    /// <remarks>
    /// Unreadable and absent are one answer on purpose: both mean nothing can find a single tile in
    /// this directory, whatever else is in it, and both are fixed the same way.
    /// </remarks>
    public static JsonObject? Read(string pyramidDirectory)
    {
        var path = Path.Combine(pyramidDirectory, TerrainPyramid.ManifestFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the manifest, replacing whatever is there.
    /// </summary>
    /// <remarks>
    /// Under another name and renamed into place, and with no mark on the front and single-character
    /// line endings, because what reads it next may be a browser or a shell script and neither
    /// forgives either.
    /// </remarks>
    public static void Write(string pyramidDirectory, JsonObject manifest)
    {
        var path = Path.Combine(pyramidDirectory, TerrainPyramid.ManifestFileName);
        var partial = path + TerrainRasterFiles.PartialSuffix;
        File.WriteAllText(
            partial,
            manifest.ToJsonString() + "\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(partial, path, overwrite: true);
    }
}
