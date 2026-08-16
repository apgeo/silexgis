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
