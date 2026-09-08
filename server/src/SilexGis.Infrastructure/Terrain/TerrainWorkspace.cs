// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Terrain;

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
    /// The directory a finished, checked pyramid is moved into to be served from.
    /// </summary>
    /// <remarks>
    /// Deliberately not inside a build's own folder, and this is a security boundary rather than a
    /// matter of tidiness. A build keeps the rasters it was given and the intermediates it made of
    /// them beside its tiles, and whatever serves the tiles is pointed at a directory and serves
    /// everything under it to anyone who can reach the site, with no account and no request ever
    /// reaching this application. A rule aimed one directory too high would therefore publish an
    /// operator's own source data. Only pyramids are ever moved here, so only pyramids can be
    /// served.
    /// </remarks>
    public string PublishRoot { get; set; } = Path.Combine("data", "terrain", "published");

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
public sealed class TerrainWorkspace(IOptions<TerrainBuildOptions> options, ILogger<TerrainWorkspace> logger)
{
    /// <summary>What a pyramid being moved into place is called until it is in place.</summary>
    private const string StagingSuffix = ".partial";

    /// <summary>What the pyramid being replaced is called while it is being taken away.</summary>
    private const string DisplacedSuffix = ".replaced";

    /// <summary>What a build's own pyramid directory is called inside its folder.</summary>
    private const string TilesDirectoryName = "tiles";

    /// <summary>What a build's prepared rasters are kept in, inside its folder.</summary>
    private const string PreparedDirectoryName = "prepared";

    /// <summary>What the pictures drawn from those rasters are kept in, inside its folder.</summary>
    /// <remarks>
    /// Beside the prepared rasters and never among them. Everything that reads a build's elevation
    /// enumerates that one directory and describes whatever raster it finds there, so a shaded
    /// relief written into it would be read back as ground and sampled for heights — a picture of
    /// brightness answering questions about metres, with nothing anywhere saying so.
    /// </remarks>
    private const string DerivativesDirectoryName = "derivatives";

    private readonly string root = Path.GetFullPath(options.Value.BuildRoot, AppContext.BaseDirectory);
    private readonly string spool = Path.GetFullPath(options.Value.SpoolRoot, AppContext.BaseDirectory);
    private readonly string published = Path.GetFullPath(options.Value.PublishRoot, AppContext.BaseDirectory);

    /// <summary>The root every build's folder sits under.</summary>
    public string Root => root;

    /// <summary>The directory this application and the tile-maker leave files for each other in.</summary>
    public string SpoolRoot => spool;

    /// <summary>The one directory whatever serves terrain is pointed at.</summary>
    public string PublishRoot => published;

    /// <summary>Everything one build owns, whether or not any of it has been created yet.</summary>
    /// <remarks>
    /// Separate from <see cref="For"/> because that one creates what it names, which is right for a
    /// step about to write and exactly wrong for anything asking where a build's files were so it
    /// can remove them — asking would put them back.
    /// </remarks>
    public string RootFor(Guid buildId) => Path.Combine(root, TerrainPyramid.PublishedName(buildId));

    /// <summary>
    /// Where this build's pyramid is served from once it has one, named the same as the address a
    /// viewer asks for it at.
    /// </summary>
    public string PublishedFor(Guid buildId) =>
        Path.Combine(published, TerrainPyramid.PublishedName(buildId));

    /// <summary>
    /// Where a pyramid on its way to the served address waits until it is in place.
    /// </summary>
    /// <remarks>
    /// Beside its destination rather than anywhere else, so that the last act of publishing is
    /// always a rename within one directory — which is the only move that cannot fail halfway on
    /// any file system, and the only one that stays atomic when an operator has put the served
    /// directory on a disk of its own. Named here rather than inside the step that does the moving,
    /// because these are directories on the served volume that outlive an interrupted run and
    /// something has to be able to find them again to take them away.
    /// </remarks>
    public string StagingFor(Guid buildId) => PublishedFor(buildId) + StagingSuffix;

    /// <summary>Where the pyramid being replaced waits while it is being taken away.</summary>
    public string ReplacedFor(Guid buildId) => PublishedFor(buildId) + DisplacedSuffix;

    /// <summary>This build's pyramid inside its own folder, whether or not it has been made yet.</summary>
    public string TilesFor(Guid buildId) => Path.Combine(RootFor(buildId), TilesDirectoryName);

    /// <summary>
    /// Where this build's prepared rasters are, whether or not any have been written yet.
    /// </summary>
    /// <remarks>
    /// Named without being created, for the same reason the root is: anything asking where a
    /// build's rasters were so that it can read or forget them must not put the directory back by
    /// asking. The one name lives here rather than at each caller, because a second spelling of it
    /// is a reader looking in an empty directory beside a full one and reporting no coverage.
    /// </remarks>
    public string PreparedFor(Guid buildId) =>
        Path.Combine(RootFor(buildId), PreparedDirectoryName);

    /// <summary>
    /// Where the pictures drawn from this build's rasters are, whether or not any have been drawn.
    /// </summary>
    /// <remarks>
    /// Inside the build's own folder rather than in the store uploaded files go to, for three
    /// reasons that point the same way. They live exactly as long as the build does, and the sweep
    /// that takes a deleted build's folder away therefore takes them with it rather than leaving
    /// gigabytes no row names. Every file in the upload store is expected to have a catalogue row
    /// naming the revision and the person it belongs to, and a picture the server drew from public
    /// elevation has neither. And they are large, regenerable and per build, which is what the
    /// terrain volume is sized for and the upload disk is not.
    /// </remarks>
    public string DerivativesFor(Guid buildId) =>
        Path.Combine(RootFor(buildId), DerivativesDirectoryName);

    /// <summary>Takes one computed picture's rasters away, leaving the build's own alone.</summary>
    /// <remarks>
    /// Named here rather than spelled out at the place that deletes a row, because where a
    /// picture's files sit is this type's knowledge and a second spelling of it is a delete that
    /// silently removes nothing while reporting success.
    /// </remarks>
    public void RemoveDerivative(Guid buildId, Guid layerId) =>
        Discard(Path.Combine(DerivativesFor(buildId), layerId.ToString("N")));

    /// <summary>Whether this build's pyramid is where terrain is served from, asked of the disk.</summary>
    /// <remarks>
    /// The manifest rather than the directory, and this is the one place that question is answered:
    /// a directory can exist because a move was interrupted part way, and treating that as published
    /// is how a build reports success over a pyramid nothing can read.
    /// </remarks>
    public bool HasPublishedPyramid(Guid buildId) => HasManifest(PublishedFor(buildId));

    /// <summary>
    /// Takes everything a build left on disk away: the published pyramid first, then the build's
    /// own folder, then whatever it left in the handover directory.
    /// </summary>
    /// <remarks>
    /// The published pyramid goes first so that the address stops answering before the rest is
    /// pulled out from under it, and every step is best-effort: the row is already gone by the time
    /// this runs, so nothing can reach any of these files any more and a file some other process
    /// still holds open is litter rather than a failure to report to whoever pressed delete.
    /// <para>
    /// The half-finished siblings of the published directory go too. A publication interrupted
    /// between its two renames leaves a whole pyramid beside the address it was going to, under a
    /// name no row mentions — so a build deleted rather than retried would otherwise leave a
    /// pyramid's worth of bytes that nothing will ever name, on the volume disk is tightest on, and
    /// reachable at an address whatever serves the published root answers.
    /// </para>
    /// </remarks>
    public void Remove(Guid buildId)
    {
        Discard(PublishedFor(buildId));
        Discard(StagingFor(buildId));
        Discard(ReplacedFor(buildId));
        Discard(RootFor(buildId));
        Discard(SpoolFor(buildId));
    }

    /// <summary>
    /// Puts back a pyramid that publishing was interrupted in the middle of moving out of the
    /// build's own folder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Publishing moves rather than copies, because a pyramid is tens of gigabytes and the machine
    /// this runs on runs out of disk before it runs out of anything else. That leaves one moment —
    /// between the pyramid arriving beside the served address and being renamed into it — in which
    /// the build's own folder no longer holds it and the address does not hold it yet. A process
    /// killed in that moment leaves a complete pyramid under a name nothing looks for, and the run
    /// that resumes finds an empty tiles directory, decides the meshing was never done, and starts
    /// hours of it again — or, on an installation with no tile maker of its own, fails outright and
    /// says so about a build that had actually finished.
    /// </para>
    /// <para>
    /// So the first thing a run does, before any step asks the disk what is already there, is undo
    /// that. Only ever a pyramid with a manifest in it, and only when neither the address nor the
    /// build's own folder already holds one: a staging directory left by a copy that stopped part
    /// way is worth nothing and is left for the publishing step to discard.
    /// </para>
    /// </remarks>
    public void RecoverInterruptedPublication(Guid buildId)
    {
        var staging = StagingFor(buildId);
        var tiles = TilesFor(buildId);

        if (!HasManifest(staging) || HasManifest(tiles) || HasPublishedPyramid(buildId))
        {
            return;
        }

        try
        {
            // Created empty by the call that hands a build its directories, so it is in the way of
            // the move rather than holding anything.
            if (Directory.Exists(tiles) && !Directory.EnumerateFileSystemEntries(tiles).Any())
            {
                Directory.Delete(tiles);
            }

            Move(staging, tiles);
            logger.LogWarning(
                "Terrain build {BuildId} had a publication interrupted; its tiles were put back",
                buildId);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left where it is. The run that follows will fail at the meshing step or ask for it
            // again, which is what would have happened without this, and the pyramid is still on
            // disk for an operator to move by hand.
            logger.LogWarning(e, "Could not put back the interrupted publication of {BuildId}", buildId);
        }
    }

    /// <summary>
    /// Gets a directory from one place to another, by renaming it if the two are on one volume and
    /// by copying it across if they are not.
    /// </summary>
    /// <remarks>
    /// Renaming is what this wants: it is instant whatever the pyramid weighs, and it leaves one
    /// copy rather than two on a machine where disk is the thing that runs out first. It is also
    /// only possible within a volume, and an operator may well have put the served directory on a
    /// disk chosen for size — so the copy is there to keep that arrangement working rather than to
    /// fail a build at the very last step of an hours-long run.
    /// </remarks>
    public static void Move(string source, string destination)
    {
        try
        {
            Directory.Move(source, destination);
            return;
        }
        catch (IOException)
        {
            // Both platforms report a rename between volumes this way and no other way; anything
            // else wrong with the source or the destination fails the copy below just as loudly.
        }

        Copy(source, destination);
        Directory.Delete(source, recursive: true);
    }

    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
        }
    }

    private static bool HasManifest(string directory) =>
        File.Exists(Path.Combine(directory, TerrainPyramid.ManifestFileName));

    private void Discard(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not remove the terrain directory {Directory}", directory);
        }
    }

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
        var buildRoot = RootFor(buildId);
        var directories = new TerrainBuildDirectories(
            buildRoot,
            Path.Combine(buildRoot, "input"),
            Path.Combine(buildRoot, PreparedDirectoryName),
            Path.Combine(buildRoot, "scratch"),
            Path.Combine(buildRoot, TilesDirectoryName));

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
