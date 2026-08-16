// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Terrain;

namespace SilexGis.Infrastructure.Terrain;

/// <summary>
/// What a build's prepared rasters are: which sources they come from, and where they go.
/// </summary>
/// <remarks>
/// One home for it because two steps ask the same question for different reasons — the step that
/// writes them, and the step that will not start until they are all there. Asked two ways they
/// would drift, and the way they would drift is a meshing step that begins against a set the
/// preparing step does not consider finished.
/// </remarks>
internal static class TerrainPreparedSet
{
    /// <summary>The rasters this build gathered, in a fixed order.</summary>
    /// <remarks>
    /// Filtered by what counts as a raster rather than taken wholesale. The directory can also hold
    /// the leavings of a transfer that did not finish, under a name of its own precisely so that
    /// nothing mistakes one for data.
    /// </remarks>
    public static List<string> Sources(TerrainBuildContext context) =>
        [.. Directory.EnumerateFiles(context.Directories.Input)
            .Where(TerrainRasterFiles.IsRaster)
            .Order(StringComparer.Ordinal)];

    /// <summary>
    /// What this build asks the raster chain for — the same request whether it is being run, being
    /// asked whether it has already been run, or being asked by a later step whether it finished,
    /// because all three answers have to be about the same set of files.
    /// </summary>
    public static TerrainRasterPrepareRequest RequestFor(
        TerrainBuildContext context, IReadOnlyList<string> inputs)
    {
        var box = context.Build.Extent.EnvelopeInternal;

        return new TerrainRasterPrepareRequest(
            inputs,
            context.Directories.Prepared,
            new TerrainArea(box.MinX, box.MinY, box.MaxX, box.MaxY));
    }
}
