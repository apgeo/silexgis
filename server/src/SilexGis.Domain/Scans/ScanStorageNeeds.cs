// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Scans;

/// <summary>
/// What a processing step of a point cloud's run is about to write, estimated before it starts, so
/// that a step which would not fit is refused rather than left half-done.
/// </summary>
/// <param name="OutputBytes">What the step's products will add to the file store (and to the caps).</param>
/// <param name="WorkerBytes">What the step needs on the worker's volume: its inputs are copied there and its outputs written there before they are stored.</param>
public sealed record ScanStepNeed(long OutputBytes, long WorkerBytes);

/// <summary>The estimate of <see cref="ScanStepNeed"/> for each step.</summary>
public static class ScanStorageNeeds
{
    /// <summary>
    /// How many times its input a tileset is taken to be when no estimate says better. Tiles were
    /// measured at two to six times the LAZ they were made from; the top of that range is used,
    /// because an estimate that is too low lets a run start that then fails half-way, which is the
    /// one outcome this check exists to prevent.
    /// </summary>
    public const int TileFactorWithoutEstimate = 6;

    /// <summary>Room allowed for a step whose products are a small report (the estimate's), in bytes: 1 MiB.</summary>
    public const long ReportBytes = 1024 * 1024;

    /// <summary>
    /// Room allowed for the plan and profile pictures, in bytes: 256 MiB. Each is at most 32 million
    /// pixels of four bytes, written compressed, so two of them never need more than this.
    /// </summary>
    public const long PictureBytes = 256L * 1024 * 1024;

    /// <summary>
    /// How many times the recording the odometry step needs on the worker's volume. Measured on two
    /// recordings, HDMapping's step 1 writes a cache of 17 to 20 times the raw size beside its input
    /// before it removes it; the copy of the recording and the session it leaves add about one more
    /// each.
    /// </summary>
    public const int OdometryWorkerFactor = 22;

    /// <summary>
    /// How many times the bytes it reads the export step needs on the worker's volume: the session
    /// archive copied in, the session unpacked beside it, and the cloud written from it — measured at
    /// about the size of the session each.
    /// </summary>
    public const int ExportWorkerFactor = 3;

    /// <summary>
    /// What <paramref name="stepId"/> is expected to write, given the bytes it reads.
    /// </summary>
    /// <param name="inputBytes">The size of what the step reads: the original, or the thinned copy when tiling one.</param>
    /// <param name="estimate">The estimate step's table, when it has run.</param>
    /// <param name="voxelCm">The voxel size the cloud was thinned at before tiling, or null when it was not thinned by voxel.</param>
    /// <remarks>
    /// The thinning step's product is taken to be as large as the cloud it thins — a thinned copy is
    /// never larger, and LAZ compression varies too much with density for anything closer to be
    /// safe. Tiling uses the estimate's own figure for the voxel size chosen (or for the full cloud),
    /// and the measured upper factor when the estimate has no row for it.
    /// </remarks>
    public static ScanStepNeed For(string stepId, long inputBytes, PointCloudEstimate? estimate, double? voxelCm)
    {
        var input = Math.Max(0, inputBytes);
        var output = stepId switch
        {
            PointCloudEstimateStep.StepId or PointCloudCheckStep.StepId or ScanCentrelineStep.StepId => ReportBytes,
            PointCloudRasterStep.StepId => PictureBytes,
            PointCloudDownsampleStep.StepId => input,
            Py3dtilesTileStep.StepId => TileBytes(input, estimate, voxelCm),

            // The session (without the cache) and the export were each measured at a little under
            // the size of what they were made from.
            _ => input,
        };
        var worker = stepId switch
        {
            HdmappingOdometryStep.StepId => SaturatingMultiply(input, OdometryWorkerFactor),
            HdmappingExportStep.StepId => SaturatingMultiply(input, ExportWorkerFactor),
            _ => SaturatingAdd(input, output),
        };
        return new ScanStepNeed(output, worker);
    }

    /// <summary>
    /// What the rest of a run is expected to write, from <paramref name="stepIds"/> on, so that a
    /// run whose later steps would not fit is refused before its first step writes anything rather
    /// than after a thinned copy has already taken part of the room.
    /// </summary>
    /// <param name="stepIds">The steps still to run, in order. Steps that write only a report count as such.</param>
    /// <param name="originalBytes">The size of the uploaded cloud, which the thinning step (and an unthinned tiling) reads; for a raw recording, the recording's size, which each of its steps is taken to read about as much of.</param>
    /// <param name="thinnedBytes">The size of a thinned copy an earlier run left and this one keeps, when the run tiles that without thinning again.</param>
    /// <param name="estimate">The estimate step's table, when it has run.</param>
    /// <param name="voxelCm">The voxel size the cloud is thinned at before tiling, or null when it is not thinned by voxel.</param>
    /// <returns>
    /// The products of every step added up — they all stay in the store — and, on the worker's
    /// volume, the largest single step's need: each step's job directory is cleared before the next starts.
    /// </returns>
    public static ScanStepNeed ForRun(
        IEnumerable<string> stepIds, long originalBytes, long? thinnedBytes, PointCloudEstimate? estimate, double? voxelCm)
    {
        ArgumentNullException.ThrowIfNull(stepIds);
        var original = Math.Max(0, originalBytes);
        var tilingReads = thinnedBytes is { } kept ? Math.Max(0, kept) : original;
        long output = 0;
        long worker = 0;
        foreach (var stepId in stepIds)
        {
            var need = For(stepId, stepId == Py3dtilesTileStep.StepId ? tilingReads : original, estimate, voxelCm);
            if (stepId == PointCloudDownsampleStep.StepId)
            {
                tilingReads = need.OutputBytes;
            }

            output = SaturatingAdd(output, need.OutputBytes);
            worker = Math.Max(worker, need.WorkerBytes);
        }

        return new ScanStepNeed(output, worker);
    }

    private static long TileBytes(long input, PointCloudEstimate? estimate, double? voxelCm)
    {
        var row = voxelCm is { } voxel
            ? estimate?.Voxels.FirstOrDefault(r => r.VoxelCm is { } v && Math.Abs(v - voxel) < 1e-9)
            : estimate?.Full;
        return row?.EstimatedTileBytes is { } estimated and > 0
            ? estimated
            : input > long.MaxValue / TileFactorWithoutEstimate ? long.MaxValue : input * TileFactorWithoutEstimate;
    }

    private static long SaturatingAdd(long a, long b) => a > long.MaxValue - b ? long.MaxValue : a + b;

    private static long SaturatingMultiply(long a, int factor) => a > long.MaxValue / factor ? long.MaxValue : a * factor;
}
