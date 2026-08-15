// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Options;

namespace SilexGis.Infrastructure.Terrain;

/// <summary>Where terrain builds do their work on disk.</summary>
public sealed class TerrainBuildOptions
{
    /// <summary>
    /// The same section the serving side reads, because both describe one installation's terrain.
    /// </summary>
    public const string SectionName = "Terrain";

    /// <summary>
    /// The directory each build gets a folder of its own under. Relative paths resolve against the
    /// application's own directory, the way the file store's root does.
    /// </summary>
    /// <remarks>
    /// Separate from the file store because what lives here is not a document: it is tens of
    /// gigabytes of rasters and tiles that belong to one build, are deleted with it, and are handed
    /// to outside tools by absolute path. Naming it separately also lets an operator put it on a
    /// disk chosen for size rather than on the one holding everything a person uploaded.
    /// </remarks>
    public string BuildRoot { get; set; } = Path.Combine("data", "terrain", "builds");

    /// <summary>
    /// How long one cell of elevation may take to arrive before the attempt is abandoned.
    /// </summary>
    /// <remarks>
    /// A cell is some tens of megabytes, so twenty minutes is generous on any link worth using and
    /// still short enough that a connection which has silently stopped delivering does not hold a
    /// build open until somebody notices. Values outside half a minute to two hours are brought
    /// back inside it: this is a safety net, and one set to a second would turn every download into
    /// a failure.
    /// </remarks>
    public int CellTimeoutSeconds { get; set; } = 1200;
}

/// <summary>
/// The directories one build works in.
/// </summary>
/// <param name="Root">Everything this build owns. Deleted when the build is.</param>
/// <param name="Input">
/// The rasters the build was given, whichever of the three ways they arrived by. One directory for
/// all of them: what happens next takes a directory of rasters and does not care where each came
/// from, and a separate tree per source kind would mean every later step learning the difference.
/// </param>
/// <param name="Scratch">
/// Working space for one run, emptied when the run ends however it ends. Everything else here is
/// deliberately kept — that is what makes resuming cheap — so anything that must <i>not</i>
/// survive a failure has to be somewhere that is swept, or a failed run leaves gigabytes behind
/// that nobody will ever look at and nothing will ever delete.
/// </param>
public sealed record TerrainBuildDirectories(string Root, string Input, string Scratch);

/// <summary>
/// Hands a build the directories it works in, and creates them.
/// </summary>
public sealed class TerrainWorkspace(IOptions<TerrainBuildOptions> options)
{
    private readonly string root = Path.GetFullPath(options.Value.BuildRoot, AppContext.BaseDirectory);

    /// <summary>The root every build's folder sits under.</summary>
    public string Root => root;

    /// <summary>
    /// This build's directories, created if they are not there yet.
    /// </summary>
    /// <remarks>
    /// Named by the build's own key rather than by anything a person typed, so no path here can be
    /// steered from outside. Creating is idempotent, which is what a step that may be run a second
    /// time after a restart needs it to be.
    /// </remarks>
    public TerrainBuildDirectories For(Guid buildId)
    {
        var buildRoot = Path.Combine(root, buildId.ToString("N"));
        var directories = new TerrainBuildDirectories(
            buildRoot,
            Path.Combine(buildRoot, "input"),
            Path.Combine(buildRoot, "scratch"));

        Directory.CreateDirectory(directories.Input);
        Directory.CreateDirectory(directories.Scratch);
        return directories;
    }
}
