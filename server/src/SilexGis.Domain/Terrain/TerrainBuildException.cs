// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Terrain;

/// <summary>Reasons a build stops, as short stable codes.</summary>
/// <remarks>
/// Short by obligation, not by taste. The column that stores one is a hundred characters, and the
/// tools this pipeline drives fail with stack dumps and kilobytes of native error text — so the
/// code is decided here, by us, and the tool's own words go into the bounded log tail instead. A
/// raw message written into the reason risks overflowing the column <i>while recording the
/// failure</i>, which leaves the build reading as running for ever.
/// </remarks>
public static class TerrainBuildFailures
{
    /// <summary>Something outside the pipeline's own vocabulary went wrong.</summary>
    public const string Unexpected = "terrain_build.unexpected";

    /// <summary>
    /// The build was handed to a worker again after already ending badly, and was stopped.
    /// </summary>
    public const string RepeatedFailure = "terrain_build.repeated_failure";

    /// <summary>Rasters could not be obtained for the area asked for.</summary>
    public const string FetchFailed = "terrain_build.fetch_failed";

    /// <summary>
    /// A source the build was given is no longer there, or is no longer one this installation may
    /// read.
    /// </summary>
    /// <remarks>
    /// Separate from a failed download because it is a different thing to act on: a transfer that
    /// broke is worth trying again, whereas a directory that has been moved — or one an operator
    /// has since taken off the list of places this installation may read — will answer the same way
    /// for ever until somebody changes something.
    /// </remarks>
    public const string SourceUnreadable = "terrain_build.source_unreadable";

    /// <summary>
    /// The build named nothing to build from, or everything it named turned out to hold no rasters.
    /// </summary>
    /// <remarks>
    /// Judged when the rasters are gathered and not only when the request arrives, because the
    /// answer can change in between: a rectangle drawn over open water is a perfectly well-formed
    /// request whose every cell is unpublished, and so is a directory somebody emptied while the
    /// build waited its turn.
    /// </remarks>
    public const string NoRasters = "terrain_build.no_rasters";

    /// <summary>
    /// A raster the build was given does not say where on the earth it is.
    /// </summary>
    /// <remarks>
    /// A fact about the file rather than about anything this pipeline did, and one only a person can
    /// settle: an image with no grid or no coordinate system attached could be placed anywhere, and
    /// guessing would put a mountain range in the wrong country. Kept apart from a failure of the
    /// raster tools because the answer is different — this one is fixed by supplying the missing
    /// georeferencing or by leaving the file out, and never by trying again.
    /// </remarks>
    public const string RasterNotGeoreferenced = "terrain_build.raster_not_georeferenced";

    /// <summary>
    /// Reprojecting or merging the rasters into the one form the rest of the chain reads failed.
    /// </summary>
    /// <remarks>
    /// The raster library reports its refusals by returning nothing and writing its reasons to a log
    /// of its own, so the words that explain this are fetched deliberately and put in the build's
    /// log tail. The code stays short because the column holding it is.
    /// </remarks>
    public const string PrepareFailed = "terrain_build.prepare_failed";

    /// <summary>
    /// This installation has nothing that can turn rasters into tiles.
    /// </summary>
    /// <remarks>
    /// A standing fact about the deployment rather than anything wrong with this build, and not
    /// worth trying again until somebody changes it: the tile-maker is a service of its own,
    /// deliberately absent unless an operator starts it, because it is a very large image and
    /// several gigabytes of memory that an installation which never bakes terrain should not carry.
    /// The message says which command starts it.
    /// </remarks>
    public const string BakeUnavailable = "terrain_build.bake_unavailable";

    /// <summary>
    /// This installation says it has a tile-maker, and nothing took the work.
    /// </summary>
    /// <remarks>
    /// Different from having none, and acted on differently: something is configured and is not
    /// answering — stopped, crashed, or never given the shared directory both sides need. Worth
    /// trying again, because a service that is coming back up looks exactly like this for a minute.
    /// </remarks>
    public const string BakeWorkerSilent = "terrain_build.bake_worker_silent";

    /// <summary>
    /// The tile-maker looked at what it was asked for and would not do it.
    /// </summary>
    /// <remarks>
    /// This one is ours. The two sides of the handover agree on a small vocabulary, and a refusal
    /// means this application asked for something outside it — a path outside the directory they
    /// share, a depth that is not a number. So is an answer that cannot be read at all. Neither is
    /// fixed by trying again, and neither is the operator's to fix.
    /// </remarks>
    public const string BakeRefused = "terrain_build.bake_refused";

    /// <summary>The tile-maker ran and ended badly.</summary>
    /// <remarks>
    /// Usually a fact about the rasters rather than about the machine, so repeating it will answer
    /// the same way. What the tool itself said is in the build's log.
    /// </remarks>
    public const string BakeFailed = "terrain_build.bake_failed";

    /// <summary>A bake was running and whatever was running it stopped.</summary>
    /// <remarks>
    /// Nothing is wrong with the data or the request; what is on disk is half a pyramid. Kept apart
    /// from a failure because the answer is simply to run it again, and kept apart from a silent
    /// service because this one did start.
    /// </remarks>
    public const string BakeInterrupted = "terrain_build.bake_interrupted";

    /// <summary>The bake ran for longer than this installation is willing to wait.</summary>
    /// <remarks>
    /// The queue this runs on has no time limit of its own, deliberately, so without this a bake
    /// that has silently stopped making progress holds its worker for ever and every build behind
    /// it waits on a thing that will never finish.
    /// </remarks>
    public const string BakeTimedOut = "terrain_build.bake_timed_out";

    /// <summary>
    /// The tile-maker ran short of memory, and quietly produced coarser tiles than were asked for.
    /// </summary>
    /// <remarks>
    /// It does not fail when this happens: it stops refining and finishes, and the pyramid it
    /// leaves is complete and valid and describes ground at the wrong resolution. Found by reading
    /// its own log, and treated as a failure, because the alternative is publishing degraded
    /// terrain with nothing anywhere saying so. The fix is to give the tile-maker's container more
    /// memory — its heap is a proportion of that limit and there is no other setting for it.
    /// </remarks>
    public const string BakeDegraded = "terrain_build.bake_degraded";

    /// <summary>The tile-maker ended cleanly and left no pyramid behind.</summary>
    /// <remarks>
    /// A pyramid with no manifest cannot be read at all, and one with a manifest and no tiles draws
    /// nothing — both while the tool reports success. Caught here because everything downstream
    /// treats what is on disk as a thing that exists.
    /// </remarks>
    public const string BakeIncomplete = "terrain_build.bake_incomplete";

    /// <summary>The pyramid's manifest is missing, or is not a manifest.</summary>
    /// <remarks>
    /// Without it nothing can find a single tile in the directory, however many are in it: the
    /// manifest is what says where the ground is and how deep the detail goes, and a viewer given
    /// none of that asks for nothing and draws a smooth empty globe.
    /// </remarks>
    public const string PyramidUnreadable = "terrain_build.pyramid_unreadable";

    /// <summary>The pyramid holds no tiles.</summary>
    /// <remarks>
    /// A manifest describing ground that is not there. Every request for it is answered with
    /// nothing, and nothing anywhere reports that — which is why an empty pyramid is stopped here
    /// rather than discovered by somebody looking at a blank hillside.
    /// </remarks>
    public const string PyramidEmpty = "terrain_build.pyramid_empty";

    /// <summary>Some of the pyramid's tiles are not tiles.</summary>
    /// <remarks>
    /// Truncated, empty, or something else entirely — the shapes a run that ran out of disk or was
    /// killed while writing leaves behind. The rest of the pyramid reads perfectly, so nothing else
    /// notices: a viewer asks for the broken tile, gets nothing back, and quietly draws the coarser
    /// tile above it instead.
    /// </remarks>
    public const string PyramidDamaged = "terrain_build.pyramid_damaged";

    /// <summary>The pyramid says it holds tiles at a level and holds none there.</summary>
    /// <remarks>
    /// A run that stopped part way through. What is drawn is the level above, at the wrong
    /// resolution, presented as the right one.
    /// </remarks>
    public const string PyramidLevelEmpty = "terrain_build.pyramid_level_empty";

    /// <summary>The pyramid holds tiles it does not say it holds.</summary>
    /// <remarks>
    /// The one shape that passes every other check and still draws nothing: a viewer reads the
    /// manifest, sees no coverage there, and never sends the request. It is what adding a raster to
    /// a finished pyramid in place produces — the new tiles are written correctly and the manifest
    /// comes back describing only the new raster, un-advertising everything that was there before.
    /// </remarks>
    public const string PyramidUnadvertised = "terrain_build.pyramid_unadvertised";

    /// <summary>The pyramid's tiles are not all written the same way.</summary>
    /// <remarks>
    /// Whatever serves them declares one encoding for the whole directory, so a directory holding
    /// two kinds cannot be described by any single rule: half of it would be labelled as bytes it
    /// does not contain, and a viewer handed those draws nothing and reports nothing.
    /// </remarks>
    public const string PyramidMixedEncoding = "terrain_build.pyramid_mixed_encoding";

    /// <summary>The pyramid's tiles are compressed, and this installation serves them as they are.</summary>
    /// <remarks>
    /// Kept apart from a mixed pyramid because what to do about it is different, and it is not the
    /// data's fault: the tile-maker this was built with writes tiles uncompressed — measured, not
    /// assumed, which is exactly why this is derived from the bytes every time rather than taken on
    /// trust — so tiles arriving compressed mean it now behaves differently, and the rule the files
    /// are served under has to change with it before any of this can be drawn.
    /// </remarks>
    public const string PyramidCompressed = "terrain_build.pyramid_compressed";

    /// <summary>The checked pyramid could not be moved to where it is served from.</summary>
    /// <remarks>
    /// A fact about the disk rather than about the data: no room, a directory nothing may write to,
    /// or the two roots sitting on different volumes with something in the way of copying between
    /// them. Everything the build made is still there, so the answer is to fix the disk and run it
    /// again rather than to build anything a second time.
    /// </remarks>
    public const string PublishFailed = "terrain_build.publish_failed";
}

/// <summary>
/// A step of the pipeline stopping for a reason that is worth telling an administrator.
/// </summary>
/// <remarks>
/// Carries the short code that goes on the build row and, separately, whatever the tool said, so
/// the two are never confused for one another: the code is the reason a screen shows and can
/// translate, the tail is diagnostic text that is truncated before it is stored and never reaches
/// the queue row at all.
/// </remarks>
public sealed class TerrainBuildException(string code, string message, string? logTail = null, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>The short stable reason, one of <see cref="TerrainBuildFailures"/>.</summary>
    public string Code { get; } = code;

    /// <summary>What the tool said, if anything did. Truncated before it is stored.</summary>
    public string? LogTail { get; } = logTail;
}
