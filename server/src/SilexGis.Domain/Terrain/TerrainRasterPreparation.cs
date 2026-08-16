// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Terrain;

/// <summary>
/// What "prepared" means for an elevation raster in this pipeline: one coordinate system, one value
/// standing for a hole, and a pixel size somebody chose.
/// </summary>
/// <remarks>
/// These are the settled facts of the format, not options. The mesher downstream reads a directory
/// of rasters and mixes them; it has no way to be told that one of them says <c>-32768</c> for
/// nothing-known and another says <c>0</c>, so a value that was never harmonised is meshed as
/// ground — a cliff dropping thirty kilometres, or a sea-level plateau where a mountain was. The
/// harmonising is done once, here, on the way in.
/// </remarks>
public static class TerrainRasterPreparation
{
    /// <summary>The value every prepared raster uses for "no data here".</summary>
    /// <remarks>
    /// A single value rather than a setting. An option that can only be set wrongly is not an
    /// option: the number has to agree with what the mesher is told, so exposing it would only
    /// create a way for the two to disagree. It is far below any real elevation on earth and far
    /// above the floor of a 32-bit float, so it survives resampling without colliding with data.
    /// </remarks>
    public const double VoidValue = -9999d;

    /// <summary>The reference every prepared raster is in: plain longitude/latitude on WGS 84.</summary>
    /// <remarks>
    /// Fixed because the tiles produced from these rasters are addressed in it. Anything else would
    /// have to be reprojected again later, by something that no longer knows what the data was.
    /// </remarks>
    public const int TargetEpsg = 4326;

    /// <summary>
    /// How pixel sizes given in degrees are compared with pixel sizes given in metres.
    /// </summary>
    /// <remarks>
    /// A single number for the whole earth, which is wrong everywhere by up to a factor of the
    /// cosine of the latitude in the east-west direction. That is acceptable for what it is used
    /// for — deciding which of two rasters is the finer, and sizing a downsample — and it is not
    /// acceptable for anything measuring a distance. Nothing here measures a distance.
    /// </remarks>
    public const double MetresPerDegree = 111_320d;

    /// <summary>
    /// How far outside the area asked for a source is still worth keeping, in degrees.
    /// </summary>
    /// <remarks>
    /// Roughly two hundred metres. A raster cut exactly to the rectangle leaves the mesher with no
    /// data at all just outside it, and everything that interpolates across an edge — the meshing,
    /// and the join between one build and the next — then has nothing on one side to interpolate
    /// from, which shows as a lip along the boundary. Small enough that it cannot meaningfully
    /// enlarge what a build produces.
    /// </remarks>
    public const double ClipMarginDegrees = 0.002;

    /// <summary>What the prepared form of one source raster is called.</summary>
    /// <remarks>
    /// <para>
    /// Derived from the source's own name rather than being one fixed name, because a build
    /// prepares each of its rasters separately and keeps them side by side: a coarse regional fill
    /// and a fine local survey stay two rasters, each at its own pixel size, so that whatever
    /// meshes them can go deep over the survey and no deeper than the fill can support elsewhere.
    /// Merged into one sheet they could not — one output grid across a mixed set either resamples
    /// the fine data away or inflates the coarse data into pretended detail.
    /// </para>
    /// <para>
    /// Derived rather than invented, and so the same every time, because it is how a run that
    /// starts again after its host was restarted finds what the previous run had already produced.
    /// A name carrying a timestamp, an attempt number or a digest would be a different name every
    /// time and no interrupted build would ever find its own work.
    /// </para>
    /// <para>
    /// The extension is folded into the name for anything that is not already a TIFF, so that two
    /// sources whose names differ only by extension do not both want the same output.
    /// </para>
    /// </remarks>
    public static string PreparedNameFor(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant();

        return extension is "tif" or ""
            ? $"{stem}.tif"
            : $"{stem}-{extension}.tif";
    }

    /// <summary>How pixels are interpolated when a raster is reprojected or resized.</summary>
    /// <remarks>
    /// Bilinear, and never nearest-neighbour. Nearest is faster and it aliases ridge lines into
    /// staircases that are plainly visible once the ground is drawn in three dimensions, which is
    /// the one thing this data exists to do. Not a setting, for the same reason the void value is
    /// not one.
    /// </remarks>
    public const string Resampling = "bilinear";
}

/// <summary>
/// A rectangle of ground in degrees of longitude and latitude.
/// </summary>
/// <remarks>
/// Plain numbers rather than a geometry, because the only thing done with it here is deciding which
/// rasters are worth converting and how much of each of them, and a geometry type would drag a
/// spatial library into a contract that has no use for one.
/// </remarks>
public sealed record TerrainArea(double West, double South, double East, double North)
{
    /// <summary>The same rectangle grown by <paramref name="margin"/> degrees on every side.</summary>
    public TerrainArea Grown(double margin) =>
        new(West - margin, South - margin, East + margin, North + margin);

    /// <summary>The ground both rectangles cover, or null where they do not meet.</summary>
    public TerrainArea? Meeting(TerrainArea other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var west = Math.Max(West, other.West);
        var south = Math.Max(South, other.South);
        var east = Math.Min(East, other.East);
        var north = Math.Min(North, other.North);

        return east > west && north > south ? new TerrainArea(west, south, east, north) : null;
    }

    /// <summary>Whether this rectangle holds every corner of <paramref name="other"/>.</summary>
    public bool Holds(TerrainArea other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return West <= other.West && South <= other.South
            && East >= other.East && North >= other.North;
    }
}

/// <summary>
/// A request to prepare a build's elevation rasters.
/// </summary>
/// <param name="InputPaths">
/// Absolute paths to the rasters to convert, in no particular order. Each of them is converted on
/// its own and stays its own raster: they are not merged into one sheet.
/// </param>
/// <param name="OutputDirectory">
/// Absolute path to the directory the prepared rasters are written into. It is created if it is
/// missing, and nothing appears at a finished name until the whole file has been written and read
/// back.
/// </param>
/// <param name="Area">
/// The ground the build was asked for, or null to convert every source whole. Sources that do not
/// reach it are left out entirely, and one that reaches beyond it is cut down to it — otherwise a
/// directory of a country's rasters produces a country's worth of output for a rectangle over one
/// hillside.
/// </param>
/// <param name="TargetPixelSizeDegrees">
/// The pixel size to resample to, in degrees, or null to keep what each source already has. Asking
/// for a coarser pixel than the data has is the ordinary case; asking for a finer one invents
/// nothing and only makes the files larger.
/// <para>
/// No build sets it yet, and that is deliberate rather than unfinished: what is prepared is kept as
/// the record of what a build was made from, and coarsening it here throws detail away permanently,
/// so the choice belongs with whatever decides how deep the tiles are worth baking — which does not
/// exist yet. Until it does, every source keeps the pixel size it arrived with.
/// </para>
/// </param>
public sealed record TerrainRasterPrepareRequest(
    IReadOnlyList<string> InputPaths,
    string OutputDirectory,
    TerrainArea? Area = null,
    double? TargetPixelSizeDegrees = null);

/// <summary>
/// What came out: where it is, how big, what ground it covers and what it says for a hole.
/// </summary>
/// <remarks>
/// Read back out of the finished file rather than carried over from what was asked for. The two are
/// not the same thing — a requested pixel size is adjusted to a whole number of pixels and to an
/// aligned grid — and everything downstream needs what is actually on disk.
/// </remarks>
public sealed record PreparedTerrainRaster(
    string Path,
    int Width,
    int Height,
    double PixelSizeDegrees,
    double West,
    double South,
    double East,
    double North,
    double VoidValue,
    long SizeBytes);

/// <summary>
/// Turns the rasters a build gathered into the single form everything after it can read.
/// </summary>
/// <remarks>
/// <para>
/// Synchronous and path-in/path-out, because the work is a native library reading and writing files
/// and there is nothing here to await. Callers run it on a background worker, one build at a time.
/// </para>
/// <para>
/// Each source stays a raster of its own. Merging them into one sheet would force a single output
/// grid across a mixed set, which either resamples a fine survey away or inflates a coarse fill
/// into pretended detail — and it is precisely the patchwork, deep only where good data exists,
/// that the whole of this is for. Whatever meshes these reads the directory, prefers the finer
/// raster where two overlap, and goes no deeper than each one's pixels support.
/// </para>
/// </remarks>
public interface ITerrainRasterPreparer
{
    /// <summary>
    /// Prepares the rasters named in <paramref name="request"/> and answers what was written, one
    /// entry per source that survived. Throws <see cref="TerrainBuildException"/> with a short code
    /// for anything an administrator should be told about.
    /// </summary>
    IReadOnlyList<PreparedTerrainRaster> Prepare(
        TerrainRasterPrepareRequest request, CancellationToken ct);

    /// <summary>
    /// Everything <see cref="Prepare"/> would produce for this request, if all of it is already on
    /// disk and whole, or null if any of it is missing, half-written or not a raster.
    /// </summary>
    /// <remarks>
    /// The set-shaped form of the question below, and the one a build asks before deciding to skip
    /// this step. It has to be the set and not one file: a run killed with three of ten rasters
    /// converted leaves three perfectly good ones behind, and a check that finds a prepared raster
    /// and stops looking declares an hour of missing work finished. Never throws — a source that
    /// cannot even be opened is simply "not already prepared", and the run that follows says so
    /// properly.
    /// </remarks>
    IReadOnlyList<PreparedTerrainRaster>? DescribePrepared(TerrainRasterPrepareRequest request);

    /// <summary>
    /// What is at <paramref name="path"/>, if what is there is a whole prepared raster, or null if
    /// there is nothing there or what is there is not one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the question a build asks when it starts again after being interrupted: is the work
    /// of this step already done? Answering it by looking at a field, or at whether a file of that
    /// name exists, is the mistake that matters here. A file left behind by a run that died halfway
    /// is the same name and the same shape as a finished one, and terrain assembled from a raster
    /// with its tail missing draws as smooth ground exactly where the missing part was — no error,
    /// no gap, nothing to notice.
    /// </para>
    /// <para>
    /// So it is answered by opening the file, and by asking for a value out of the far corner of it
    /// rather than only reading the header: a truncated raster still describes itself perfectly
    /// well and only fails when something asks for the part that is not there. Never throws — "not
    /// a prepared raster" is an ordinary answer and every way of being one is the same answer.
    /// </para>
    /// </remarks>
    PreparedTerrainRaster? Describe(string path);
}
