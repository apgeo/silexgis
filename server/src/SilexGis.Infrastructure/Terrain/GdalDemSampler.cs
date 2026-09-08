// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Logging;
using OSGeo.GDAL;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Geodata;

namespace SilexGis.Infrastructure.Terrain;

/// <summary>
/// Reads ground heights out of a build's prepared rasters with the raster library.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why one call at a time.</b> The library's wrappers are thin covers over native memory, and a
/// dataset touched from two threads faults the whole process in a way this language cannot catch —
/// not an exception anything could recover from, an abort. Everywhere else in this application that
/// reads rasters runs on a worker taking one job at a time, which made the rule free. A route
/// answering requests does not, so the rule is kept here explicitly: every handle is opened, read
/// and closed inside a gate only one caller holds at a time, and no handle is ever visible outside
/// it. The cost is that heights are read one caller at a time; the alternative measured against it
/// was a child process per request, which moves the fault out of this process and costs a process
/// spawn per probe. That remains the upgrade path if a native fault is ever actually observed.
/// </para>
/// <para>
/// <b>Why the handles are kept.</b> Opening a cloud-optimised GeoTIFF reads its header and its
/// overview layout; a profile along a cave asks for hundreds of heights, nearly all of them out of
/// the same one or two files. Reopening per point would spend the whole request in header reads. So
/// finished datasets are kept open behind the gate, oldest evicted once there are more than a
/// handful — which bounds both file handles and the native memory none of it is accounted for in.
/// </para>
/// <para>
/// <b>Return-code mode.</b> Nothing in this assembly calls <c>Gdal.UseExceptions</c>, so a call
/// that fails answers with nothing and leaves its reason in a slot of the library's own. The slot
/// is cleared immediately before a call and read immediately after it, because any further call
/// overwrites it. A file that cannot be read never fails the request: a probe is asked about the
/// ground, and telling a caller about this server's disk instead is both unhelpful and more than
/// they asked. It is answered as ground nothing has been built over, which is what is true of it
/// as far as this server can still tell, and deliberately not as a hole in data that was built —
/// that would be a statement about the terrain rather than about the disk.
/// </para>
/// </remarks>
public sealed class GdalDemSampler : IDemSampleService, IDisposable
{
    /// <summary>The formats a prepared raster may be, which is the only thing this opens.</summary>
    /// <remarks>
    /// The same allow-list the preparation step writes with. It matters here for the same reason it
    /// matters there: among the formats the library can identify is a virtual mosaic — a small
    /// document naming other files, by absolute path, that it will then read on this server's
    /// behalf. Nothing under the prepared directory is one, and opening with the default set would
    /// mean that a file placed there could be.
    /// </remarks>
    private static readonly string[] PreparedDrivers = ["GTiff", "COG"];

    /// <summary>How many datasets are kept open at once.</summary>
    /// <remarks>
    /// A build's prepared set is a handful of rasters and a profile stays inside one or two of them,
    /// so this is generous for the case it exists for while staying far below any file-handle limit
    /// even if several builds are read in turn.
    /// </remarks>
    private const int OpenRasterLimit = 24;

    /// <summary>
    /// Held for the whole of a read, so that no two callers are inside the native library at once.
    /// </summary>
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>Datasets kept open, oldest use first. Only ever touched while holding the gate.</summary>
    private readonly Dictionary<string, Dataset> open = new(StringComparer.Ordinal);

    private readonly LinkedList<string> recency = new();

    private readonly ILogger<GdalDemSampler> logger;

    private bool disposed;

    static GdalDemSampler() => GdalRuntime.Configure();

    public GdalDemSampler(ILogger<GdalDemSampler> logger) => this.logger = logger;

    /// <inheritdoc />
    public async Task<IReadOnlyList<DemSample>> SampleAsync(
        TerrainCoverage coverage, IReadOnlyList<DemSamplePoint> points, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count == 0)
        {
            return [];
        }

        // Nothing is read before the token is looked at, so a caller who has already given up never
        // takes a turn in front of callers who are still listening.
        ct.ThrowIfCancellationRequested();

        // Nothing has been built over this ground. Said once, without opening anything.
        if (coverage.Rasters.Count == 0)
        {
            return [.. points.Select(DemSample.OutsideCoverage)];
        }

        // Finest first, once for the whole batch rather than once per point. Where a coarse
        // regional fill and a fine local survey overlap, the fine one is the answer — reading the
        // coarse one there would throw away the detail the build exists to hold.
        var candidates = coverage.Rasters
            .OrderBy(r => r.PixelSizeDegrees)
            .ToArray();

        var offsetM = coverage.SampleToSurveyOffsetM;
        var answers = new DemSample[points.Count];

        // Awaited rather than waited on: only one caller is inside the native library at a time, so
        // on a busy installation most of the time spent here is spent queueing, and a caller that
        // queued by blocking would hold the thread its request arrived on for somebody else's read.
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (var i = 0; i < points.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                answers[i] = At(candidates, points[i], offsetM);
            }
        }
        finally
        {
            gate.Release();
        }

        return answers;
    }

    /// <summary>The ground at one point, from the finest raster that has a value for it.</summary>
    /// <remarks>
    /// A raster covering the point but answering "nothing known" does not end the search: a hole in
    /// a fine survey over ground a coarse fill does describe is exactly the case the patchwork is
    /// for. Only when every raster covering the point has no value is the answer no data, and only
    /// when none of them covers it at all is the answer no coverage. The two are kept apart because
    /// one says a build never reached here and the other says one did and found nothing. A raster
    /// whose file cannot be read counts as neither: it is passed over as though it were not in the
    /// list, so a coarser one that can still be read answers, and a point only those unreadable
    /// files covered is no coverage.
    /// </remarks>
    private DemSample At(
        IReadOnlyList<PreparedTerrainRaster> candidates, DemSamplePoint point, double offsetM)
    {
        var covered = false;

        foreach (var raster in candidates)
        {
            if (!Holds(raster, point))
            {
                continue;
            }

            var reading = Read(raster, point);

            // A raster whose file could not be opened or read is not coverage. The description said
            // a build reached this ground, but the only thing that can still be checked says
            // nothing at all is there — a stored description outliving the files it names, a serving
            // host pointed at a different disk, a prepared directory cleared to reclaim space. "A
            // build looked here and found nothing" is a statement about the ground and would be
            // drawn as one; "nobody has built elevation here" is the truth about what this server
            // can say, and is the answer.
            if (reading.Unreadable)
            {
                continue;
            }

            covered = true;

            if (reading.ElevationM is { } elevationM)
            {
                return DemSample.Sampled(point, elevationM + offsetM);
            }
        }

        return covered ? DemSample.NoData(point) : DemSample.OutsideCoverage(point);
    }

    /// <summary>
    /// What one raster had to say about one point: a height, nothing known there, or nothing this
    /// server could read at all.
    /// </summary>
    /// <remarks>
    /// The third case is deliberately not folded into the second. A hole in the data is a fact
    /// about the ground and is worth reporting as one; a file that is not there is a fact about
    /// this installation, and reporting it as a hole puts a plausible-looking gap on a hillside
    /// nobody surveyed a gap in.
    /// </remarks>
    private readonly record struct Reading(double? ElevationM, bool Unreadable)
    {
        public static Reading Missing { get; } = new(null, true);

        public static Reading Unknown { get; } = new(null, false);

        public static Reading Height(double elevationM) => new(elevationM, false);
    }

    /// <summary>Whether the raster's pixels cover this point.</summary>
    /// <remarks>
    /// The footprint's outer edge, not its pixel centres: the half-pixel band around the rim is
    /// covered ground and reading it clamps to the edge pixel, which is what the data there says.
    /// </remarks>
    private static bool Holds(PreparedTerrainRaster raster, DemSamplePoint point) =>
        point.Longitude >= raster.West && point.Longitude <= raster.East
        && point.Latitude >= raster.South && point.Latitude <= raster.North;

    /// <summary>
    /// The height at one point of one raster, interpolated across the four pixels around it, or
    /// a reading saying the raster does not know there, or one saying it could not be read at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bilinear rather than nearest, because a profile read with nearest is a staircase: every
    /// pixel boundary is a vertical step in a curve that is supposed to be ground, and depths
    /// measured against it inherit the step. The pixel centre sits half a pixel in from the corner
    /// the placement names, which is what the half-pixel shift below is.
    /// </para>
    /// <para>
    /// Any of the four contributing pixels being "nothing known" makes the whole answer nothing
    /// known. Interpolating across a hole would blend a void marker into a real height and produce
    /// a number that is not wrong by a little; a corner that contributes nothing because the point
    /// sits exactly on a row or column is not consulted.
    /// </para>
    /// </remarks>
    private Reading Read(PreparedTerrainRaster raster, DemSamplePoint point)
    {
        var dataset = Dataset(raster.Path);
        if (dataset is null)
        {
            return Reading.Missing;
        }

        var pixelWidth = raster.PixelSizeDegrees;
        var pixelHeight = (raster.North - raster.South) / raster.Height;
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            // A description that cannot be turned into pixel positions describes nothing readable.
            return Reading.Missing;
        }

        var fx = ((point.Longitude - raster.West) / pixelWidth) - 0.5;
        var fy = ((raster.North - point.Latitude) / pixelHeight) - 0.5;

        var x0 = (int)Math.Floor(fx);
        var y0 = (int)Math.Floor(fy);
        var tx = fx - x0;
        var ty = fy - y0;

        // Outside the first and last half-pixel the neighbour does not exist; clamping reads the
        // edge pixel twice, which weights it fully and is what the data there actually says.
        var xa = Math.Clamp(x0, 0, raster.Width - 1);
        var xb = Math.Clamp(x0 + 1, 0, raster.Width - 1);
        var ya = Math.Clamp(y0, 0, raster.Height - 1);
        var yb = Math.Clamp(y0 + 1, 0, raster.Height - 1);

        var windowWidth = xb - xa + 1;
        var windowHeight = yb - ya + 1;
        var window = new float[windowWidth * windowHeight];

        CPLErr read;
        try
        {
            // Bands belong to the dataset that handed them out and are released with it.
            var band = dataset.GetRasterBand(1);
            Gdal.ErrorReset();
            read = band.ReadRaster(
                xa, ya, windowWidth, windowHeight, window, windowWidth, windowHeight, 0, 0);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A file truncated or removed under us can make the library raise rather than answer.
            Forget(raster.Path);
            logger.LogWarning(e, "An elevation raster could not be read: {Path}", raster.Path);
            return Reading.Missing;
        }

        if (read != CPLErr.CE_None)
        {
            var reason = Gdal.GetLastErrorMsg();
            Forget(raster.Path);
            logger.LogWarning(
                "An elevation raster could not be read: {Path}. {Reason}", raster.Path, reason);
            return Reading.Missing;
        }

        var w00 = (1 - tx) * (1 - ty);
        var w10 = tx * (1 - ty);
        var w01 = (1 - tx) * ty;
        var w11 = tx * ty;

        var v00 = window[Offset(xa, ya)];
        var v10 = window[Offset(xb, ya)];
        var v01 = window[Offset(xa, yb)];
        var v11 = window[Offset(xb, yb)];

        if (IsVoid(v00, w00, raster) || IsVoid(v10, w10, raster)
            || IsVoid(v01, w01, raster) || IsVoid(v11, w11, raster))
        {
            return Reading.Unknown;
        }

        var height = (w00 * v00) + (w10 * v10) + (w01 * v01) + (w11 * v11);
        return double.IsFinite(height) ? Reading.Height(height) : Reading.Unknown;

        int Offset(int x, int y) => ((y - ya) * windowWidth) + (x - xa);
    }

    /// <summary>Whether a pixel that actually contributes says nothing is known there.</summary>
    private static bool IsVoid(float value, double weight, PreparedTerrainRaster raster) =>
        weight > 0 && (!float.IsFinite(value) || (double)value == raster.VoidValue);

    /// <summary>
    /// The open dataset for a prepared raster, opening it if it is not already open, or null if it
    /// cannot be opened. Only ever called while holding the gate.
    /// </summary>
    private Dataset? Dataset(string path)
    {
        if (open.TryGetValue(path, out var already))
        {
            Touch(path);
            return already;
        }

        Dataset? opened;
        try
        {
            Gdal.ErrorReset();
            opened = Gdal.OpenEx(
                path,
                (uint)(GdalConst.OF_RASTER | GdalConst.OF_READONLY),
                allowed_drivers: PreparedDrivers,
                open_options: null,
                sibling_files: null);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "An elevation raster could not be opened: {Path}", path);
            return null;
        }

        if (opened is null || opened.RasterCount < 1)
        {
            var reason = Gdal.GetLastErrorMsg();
            opened?.Dispose();
            logger.LogWarning(
                "An elevation raster could not be opened: {Path}. {Reason}", path, reason);
            return null;
        }

        open[path] = opened;
        recency.AddLast(path);
        Evict();
        return opened;
    }

    private void Touch(string path)
    {
        recency.Remove(path);
        recency.AddLast(path);
    }

    private void Evict()
    {
        while (recency.Count > OpenRasterLimit && recency.First is { } oldest)
        {
            recency.RemoveFirst();
            if (open.Remove(oldest.Value, out var stale))
            {
                stale.Dispose();
            }
        }
    }

    /// <summary>
    /// Closes and forgets one raster, so that a file which has changed underneath a kept handle is
    /// opened again next time rather than answered out of a handle to bytes that are gone.
    /// </summary>
    private void Forget(string path)
    {
        recency.Remove(path);
        if (open.Remove(path, out var stale))
        {
            stale.Dispose();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        // Under the gate: a handle disposed while another caller is inside the library is the very
        // fault this whole class is arranged to avoid.
        gate.Wait();
        try
        {
            foreach (var dataset in open.Values)
            {
                dataset.Dispose();
            }

            open.Clear();
            recency.Clear();
        }
        finally
        {
            gate.Release();
            gate.Dispose();
        }
    }
}
