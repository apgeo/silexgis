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

    /// <summary>
    /// Whether this installation has anything that can turn rasters into tiles.
    /// </summary>
    /// <remarks>
    /// Off by default, and an installation that leaves it off is a supported installation rather
    /// than a broken one: making tiles needs a program of its own, which is a very large image and
    /// several gigabytes of memory, and terrain baked elsewhere is served here exactly as before
    /// without it. Set by the same deployment that starts that program, so that an application told
    /// to bake and a machine with nothing to bake with cannot be arranged separately.
    /// </remarks>
    public bool BakeEnabled { get; set; }

    /// <summary>
    /// The directory this application and the tile-maker meet in.
    /// </summary>
    /// <remarks>
    /// A directory rather than an address because the tile-maker is a command-line tool and not a
    /// server, and because what passes between them is gigabytes of files already sitting on a
    /// volume they share — not something to put in a request body. It must be a path both of them
    /// see under the same name.
    /// </remarks>
    public string SpoolRoot { get; set; } = Path.Combine("data", "terrain", "spool");

    /// <summary>
    /// How long to wait for something to pick a bake up before deciding nothing is going to.
    /// </summary>
    /// <remarks>
    /// This is what tells a service that is absent apart from one that is merely busy starting.
    /// Generous, because an image of this size takes a while to come up and a request left waiting
    /// costs nothing while it does; and bounded, because the alternative is a build that waits for
    /// ever on a container somebody stopped last week.
    /// </remarks>
    public int BakePickupSeconds { get; set; } = 300;

    /// <summary>How long one bake may run before the attempt is abandoned.</summary>
    /// <remarks>
    /// The queue a build runs on has no time limit of its own, deliberately, so this is the only
    /// one there is. Half a day is far beyond anything measured — a couple of hundred square
    /// kilometres with a fine island in it takes about two minutes — and is meant to catch a bake
    /// that has stopped making progress rather than to cap a large one.
    /// </remarks>
    public int BakeTimeoutSeconds { get; set; } = 43_200;
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
/// <param name="Prepared">
/// The rasters as everything after the first step reads them: one coordinate system, one value
/// standing for a hole, one pixel size. Kept rather than swept, for three reasons that each hold on
/// their own — building the mesh again over ground that did not change costs nothing if these are
/// still here; anything that later asks what a build was actually made from has one answer to read
/// rather than a pile of sources in whatever form they arrived in; and they are the form a
/// different way of serving this data would start from, so throwing them away would be closing a
/// door for the sake of disk that the sources themselves already cost.
/// </param>
/// <param name="Scratch">
/// Working space for one run, emptied when the run ends however it ends. Everything else here is
/// deliberately kept — that is what makes resuming cheap — so anything that must <i>not</i>
/// survive a failure has to be somewhere that is swept, or a failed run leaves gigabytes behind
/// that nobody will ever look at and nothing will ever delete.
/// </param>
/// <param name="Tiles">
/// The pyramid itself: the manifest describing what ground is covered and at what detail, and the
/// tiles under it. This is the only part of a build a browser ever asks for, and it is served as
/// plain files by whatever is in front of the application rather than through it.
/// </param>
public sealed record TerrainBuildDirectories(
    string Root, string Input, string Prepared, string Scratch, string Tiles);

/// <summary>
/// Hands a build the directories it works in, and creates them.
/// </summary>
public sealed class TerrainWorkspace(IOptions<TerrainBuildOptions> options)
{
    private readonly string root = Path.GetFullPath(options.Value.BuildRoot, AppContext.BaseDirectory);
    private readonly string spool = Path.GetFullPath(options.Value.SpoolRoot, AppContext.BaseDirectory);

    /// <summary>The root every build's folder sits under.</summary>
    public string Root => root;

    /// <summary>The directory this application and the tile-maker leave files for each other in.</summary>
    public string SpoolRoot => spool;

    /// <summary>
    /// Where this build's request for a bake, and the answer to it, are left.
    /// </summary>
    /// <remarks>
    /// Outside the build's own folder, because the other side of this handover is given the shared
    /// volume and nothing else about how builds are arranged: it watches one directory and does not
    /// need to know that a build has an identity, sources, or anywhere it keeps them. Named after
    /// the build all the same, so that an operator looking at a stalled bake can tell whose it is.
    /// </remarks>
    public string SpoolFor(Guid buildId) => Path.Combine(spool, buildId.ToString("N"));

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
            Path.Combine(buildRoot, "prepared"),
            Path.Combine(buildRoot, "scratch"),
            Path.Combine(buildRoot, "tiles"));

        Directory.CreateDirectory(directories.Input);

        // Beside the sources rather than inside them: what is written here is a raster with an
        // accepted extension, and the step that gathers sources reads its own directory back to see
        // whether anything arrived. Prepared output sitting in there would be read as a source on
        // the next run and prepared again from itself.
        Directory.CreateDirectory(directories.Prepared);
        Directory.CreateDirectory(directories.Scratch);

        // Created here rather than left to the tile-maker so that the directory belongs to this
        // application on a fresh volume. The two run as the same user, but the one that creates a
        // path owns it, and a pyramid nobody but the tile-maker can write to is a build that fails
        // at the very last step of a long run.
        Directory.CreateDirectory(directories.Tiles);
        return directories;
    }
}
