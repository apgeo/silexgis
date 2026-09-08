// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Terrain;

/// <summary>
/// Why a sample says what it says.
/// </summary>
/// <remarks>
/// Three answers rather than a number and a null, because the two ways of having no height are not
/// the same fact and confusing them hides a build that was never run behind a hole in one that was.
/// Neither of them is ever reported as zero metres: a passage drawn against a surface at sea level
/// is plausible, wrong, and indistinguishable downstream from a real reading.
/// </remarks>
public enum DemSampleOutcome
{
    /// <summary>A height was read, and <c>ElevationM</c> holds it.</summary>
    Sampled = 0,

    /// <summary>
    /// No raster in the coverage holds this point at all — nobody has ever built elevation here.
    /// </summary>
    OutsideCoverage = 1,

    /// <summary>
    /// A raster holds the point and says it does not know: a hole the source data never filled, or
    /// a pixel the harmonising step marked as nothing-known.
    /// </summary>
    NoData = 2,
}

/// <summary>A place to read the ground at, in degrees on the world datum.</summary>
public readonly record struct DemSamplePoint(double Longitude, double Latitude);

/// <summary>
/// What the ground was at one point, or why it could not be said.
/// </summary>
/// <param name="Point">The place asked about, echoed back so a batch answer can be matched up.</param>
/// <param name="Outcome">Whether a height was read, and if not, why not.</param>
/// <param name="ElevationM">
/// The height in metres above sea level, or null for either kind of no answer. Already reconciled
/// with the survey datum by the sampler, so nothing downstream applies the geoid correction a
/// second time — doing so is a forty-metre error wearing the clothes of a real altitude.
/// </param>
public sealed record DemSample(DemSamplePoint Point, DemSampleOutcome Outcome, double? ElevationM)
{
    /// <summary>The answer for a point no raster covers.</summary>
    public static DemSample OutsideCoverage(DemSamplePoint point) =>
        new(point, DemSampleOutcome.OutsideCoverage, null);

    /// <summary>The answer for a point a raster covers and has no value for.</summary>
    public static DemSample NoData(DemSamplePoint point) =>
        new(point, DemSampleOutcome.NoData, null);

    /// <summary>The answer for a point a height was read at.</summary>
    public static DemSample Sampled(DemSamplePoint point, double elevationM) =>
        new(point, DemSampleOutcome.Sampled, elevationM);
}

/// <summary>
/// The rasters one terrain build left behind, and what their heights are measured from.
/// </summary>
/// <remarks>
/// <para>
/// The datum travels with the rasters rather than being looked up beside them, because the
/// correction between what a raster holds and what a survey holds must be applied exactly once and
/// the only way to guarantee that is for exactly one thing to hold both halves at the same moment.
/// The preparation step performs no vertical conversion at all — a prepared raster carries the
/// source's own heights — so the datum recorded against the build is still the truth about them.
/// </para>
/// <para>
/// Plain records rather than a database shape, so the reading of rasters knows nothing about where
/// the list came from and can be built in a test out of files made a moment earlier.
/// </para>
/// </remarks>
/// <param name="Rasters">
/// Every prepared raster of the build, in no particular order. The reader prefers the finest of the
/// ones covering a point, which is what makes a coarse regional fill and a fine local survey usable
/// together without either spoiling the other.
/// </param>
/// <param name="HeightDatum">What the rasters' heights are measured from.</param>
/// <param name="GeoidHeightM">
/// The local geoid undulation in metres, used only when the rasters are ellipsoidal.
/// </param>
public sealed record TerrainCoverage(
    IReadOnlyList<PreparedTerrainRaster> Rasters,
    TerrainHeightDatum HeightDatum,
    double GeoidHeightM)
{
    /// <summary>A coverage holding nothing, which answers every point with no coverage.</summary>
    public static TerrainCoverage None { get; } =
        new([], TerrainHeightDatum.Orthometric, 0);

    /// <summary>
    /// Metres to add to a height read out of these rasters so it can be compared with a surveyed
    /// altitude. Resolved by the one function that owns the sign of that arithmetic.
    /// </summary>
    public double SampleToSurveyOffsetM =>
        GeoidOffset.SampleToSurveyOffsetM(HeightDatum, GeoidHeightM);
}

/// <summary>
/// Reads the ground height at places on it, out of the rasters a terrain build prepared.
/// </summary>
/// <remarks>
/// <para>
/// Points in, heights out. The implementation serialises its own access to the files — the raster
/// library's handles fault the whole process rather than throwing when two threads touch one — so a
/// caller may call it from anywhere without arranging that itself.
/// </para>
/// <para>
/// Asynchronous even though the reading itself is a native library and has nothing to await, and
/// that is the reason rather than an exception to it: waiting for one's turn is the part that
/// blocks, and a caller blocking on it holds the thread its request arrived on. A few requests each
/// asking about a few hundred points would then hold that many threads doing nothing, and the pool
/// grows slowly enough that the wait spreads to routes that read no elevation at all. Waiting is
/// awaited; the native work is not, because there is nothing there to await.
/// </para>
/// <para>
/// The coverage is passed in rather than resolved here, so that the cost of working out which
/// rasters a build has is paid once for a whole profile rather than once per point, and so that
/// this contract never has to know how that question is answered.
/// </para>
/// </remarks>
public interface IDemSampleService
{
    /// <summary>
    /// The ground at each point, in the same order as they were given, every one of them answered.
    /// </summary>
    /// <remarks>
    /// Never throws for a file it cannot read. A raster whose file has gone is passed over rather
    /// than believed: any other raster still covering the point answers, and a point nothing
    /// readable covers is answered as ground no build has reached. Deliberately not as a hole in
    /// data that was built — that reads as a statement about the terrain, and the statement being
    /// made is about this server's disk. A probe that failed outright would tell a caller more
    /// about that disk than about the ground.
    /// </remarks>
    Task<IReadOnlyList<DemSample>> SampleAsync(
        TerrainCoverage coverage, IReadOnlyList<DemSamplePoint> points, CancellationToken ct);
}
