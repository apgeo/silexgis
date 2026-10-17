// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Terrain;

namespace SilexGis.Infrastructure.Terrain;

/// <summary>
/// The rasters a build prepares, wherever each of them is, and the name each is prepared under.
/// </summary>
/// <param name="Paths">Absolute paths, in a fixed order.</param>
/// <param name="Names">
/// The name a raster is prepared under where it is not its own file name. Only rasters read in place
/// appear here; a copied raster was given a name of its own when it was copied.
/// </param>
public sealed record TerrainRasterInputs(
    IReadOnlyList<string> Paths,
    IReadOnlyDictionary<string, string> Names)
{
    public int Count => Paths.Count;
}

/// <summary>
/// What a build's prepared rasters are: which sources they come from, and where they go.
/// </summary>
/// <remarks>
/// One home for it because two steps ask the same question for different reasons — the step that
/// writes them, and the step that will not start until they are all there. Asked two ways they
/// would drift, and the way they would drift is a meshing step that begins against a set the
/// preparing step does not consider finished.
/// <para>
/// The sources are in two places. What was obtained or sent through the browser was copied into the
/// build's input directory; what an operator named on the server's own disk is read where it lies,
/// from the list the build recorded the first time it read those directories — and that list is
/// checked against the disk again every time it is asked for here.
/// </para>
/// </remarks>
public sealed class TerrainPreparedSet(TerrainInPlaceSources inPlace)
{
    /// <summary>The rasters this build gathered, in a fixed order.</summary>
    /// <remarks>
    /// The input directory is filtered by what counts as a raster rather than taken wholesale. It can
    /// also hold the leavings of a transfer that did not finish, under a name of its own precisely so
    /// that nothing mistakes one for data.
    /// </remarks>
    public TerrainRasterInputs Sources(TerrainBuildContext context, CancellationToken ct)
    {
        var copied = Directory.Exists(context.Directories.Input)
            ? Directory.EnumerateFiles(context.Directories.Input)
                .Where(TerrainRasterFiles.IsRaster)
                .Order(StringComparer.Ordinal)
                .ToList()
            : [];

        var inPlaceRasters = inPlace.Verified(context.Directories, ct);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raster in inPlaceRasters)
        {
            names[raster.Path] = raster.Name;
        }

        return new TerrainRasterInputs([.. copied, .. inPlaceRasters.Select(r => r.Path)], names);
    }

    /// <summary>
    /// What this build asks the raster chain for — the same request whether it is being run, being
    /// asked whether it has already been run, or being asked by a later step whether it finished,
    /// because all three answers have to be about the same set of files.
    /// </summary>
    public static TerrainRasterPrepareRequest RequestFor(
        TerrainBuildContext context, TerrainRasterInputs inputs)
    {
        var box = context.Build.Extent.EnvelopeInternal;

        return new TerrainRasterPrepareRequest(
            inputs.Paths,
            context.Directories.Prepared,
            new TerrainArea(box.MinX, box.MinY, box.MaxX, box.MaxY),
            Names: inputs.Names);
    }
}
