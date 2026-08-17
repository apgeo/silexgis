// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;
using SilexGis.Domain.Terrain;

namespace SilexGis.Infrastructure.Terrain;

/// <summary>
/// The last step: moving the checked pyramid to where it is served from.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is ever written into the directory the web server is already serving. A pyramid is baked
/// and checked in the build's own folder, which nothing outside this application can reach, and this
/// step brings it into the served directory with a single rename — because a half-written pyramid
/// passes the only probe a viewer makes (it reads the coarsest tile and nothing else) and then fails
/// at depth, drawing the coarse level's heights as though they were the fine ones. There is no
/// moment at which the published address holds half a pyramid.
/// </para>
/// <para>
/// Each build is published at an address of its own, and an address is never reused for different
/// tiles. That is not tidiness: the scene ignores a terrain source whose address has not changed, so
/// swapping which build is drawn has to move the address; and the tiles are cached for a week, which
/// is only safe because a tile at a given address never becomes a different tile.
/// </para>
/// <para>
/// The manifest is not touched here. It was stamped when the pyramid was checked — with the credit
/// of the data this build was made from and with a version taken from the tiles themselves — and a
/// second writer would either recompute the same string for nothing or hand every viewer a version
/// that disagrees with the one their cached tile addresses were built from.
/// </para>
/// </remarks>
public sealed class TerrainPublishPhase(TerrainWorkspace workspace) : ITerrainPhase
{
    public TerrainBuildPhase Phase => TerrainBuildPhase.Publish;

    /// <summary>
    /// Whether this build's pyramid is already at its address, asked of the disk.
    /// </summary>
    public Task<bool> IsAlreadyDoneAsync(TerrainBuildContext context, CancellationToken ct) =>
        Task.FromResult(workspace.HasPublishedPyramid(context.Build.Id));

    public async Task RunAsync(TerrainBuildContext context, CancellationToken ct)
    {
        await context.ReportAsync(5, "Publishing the tiles", ct);

        var built = context.Directories.Tiles;
        if (!HasManifest(built))
        {
            // Reached only if something removed the pyramid between the check and this step, since
            // the step before this one refuses a build whose tiles it could not read. Said plainly
            // all the same: publishing a directory with no manifest in it would put an address in
            // front of viewers that answers every request with nothing.
            throw new TerrainBuildException(
                TerrainBuildFailures.PyramidUnreadable,
                "This build's tiles are no longer there to publish. Nothing can be served from a "
                + "directory with no manifest in it: a viewer reads the manifest first and, given "
                + "none, asks for no tiles at all and draws bare ground.");
        }

        var target = workspace.PublishedFor(context.Build.Id);
        var staging = workspace.StagingFor(context.Build.Id);

        try
        {
            Directory.CreateDirectory(workspace.PublishRoot);
            Discard(staging);
            Stage(built, staging);

            try
            {
                SwapIn(staging, workspace.ReplacedFor(context.Build.Id), target);
            }
            catch
            {
                // Put back where it came from. Staging consumes the build's only copy of the
                // pyramid, so a failure here with nothing done about it would leave hours of
                // meshing under a name nothing looks for — and, on an installation whose tile
                // maker is not running, no way to produce it again. Whether this succeeded is
                // asked of the disk below rather than assumed, because what the failure says
                // about where the tiles are has to be true.
                Restore(staging, built);
                throw;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new TerrainBuildException(
                TerrainBuildFailures.PublishFailed,
                "This build's tiles were made and checked and could not be moved to where terrain "
                + $"is served from. {WhereTheTilesAre(built, staging)} Until the directory terrain "
                + "is served from can be written to, nothing will draw them.",
                e.Message,
                e);
        }

        await context.LogAsync($"Published at {TerrainPyramid.PublishedUrl(context.Build.Id)}", ct);
        await context.ReportAsync(100, null, ct);
    }

    /// <summary>Gets the pyramid beside its destination, leaving the build's folder without one.</summary>
    private static void Stage(string source, string staging) => TerrainWorkspace.Move(source, staging);

    /// <summary>
    /// Puts a staged pyramid back in the build's own folder after a publication that could not be
    /// finished.
    /// </summary>
    /// <remarks>
    /// Best-effort and silent, because it runs while a failure is already on its way and a second
    /// one raised from here would replace the account of what actually went wrong. What it could
    /// not do is visible in the sentence that failure carries, which is worked out from the disk.
    /// </remarks>
    private static void Restore(string staging, string built)
    {
        try
        {
            if (!Directory.Exists(staging))
            {
                return;
            }

            if (Directory.Exists(built) && !Directory.EnumerateFileSystemEntries(built).Any())
            {
                Directory.Delete(built);
            }

            if (!Directory.Exists(built))
            {
                TerrainWorkspace.Move(staging, built);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Swallowed on purpose: see above.
        }
    }

    /// <summary>Where this build's tiles actually are, for a failure that has to say so truthfully.</summary>
    private static string WhereTheTilesAre(string built, string staging) =>
        HasManifest(built)
            ? "Nothing has been lost — the pyramid is still in the build's own folder."
            : HasManifest(staging)
                ? "The tiles themselves are safe: they are beside the address they were going to, "
                    + "and running this build again finishes moving them."
                : "The tiles could not be found afterwards either, so this build has to be made "
                    + "again.";

    /// <summary>
    /// Puts what has been staged at the address, taking whatever was there away afterwards.
    /// </summary>
    /// <remarks>
    /// The old pyramid is moved aside rather than deleted in place, because deleting a large one
    /// takes time and the address answers nothing for as long as it takes. Renaming it away is
    /// instant, so the window in which the address is not there is one rename wide.
    /// </remarks>
    private static void SwapIn(string staging, string displaced, string target)
    {
        Discard(displaced);

        if (Directory.Exists(target))
        {
            Directory.Move(target, displaced);
        }

        try
        {
            Directory.Move(staging, target);
        }
        catch
        {
            // Whatever was being served is put back rather than left in a directory nothing knows
            // the name of: a failed republication should leave the installation drawing what it
            // was drawing a moment ago.
            if (Directory.Exists(displaced) && !Directory.Exists(target))
            {
                Directory.Move(displaced, target);
            }

            throw;
        }

        Discard(displaced);
    }

    private static void Discard(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static bool HasManifest(string directory) =>
        File.Exists(Path.Combine(directory, TerrainPyramid.ManifestFileName));
}
