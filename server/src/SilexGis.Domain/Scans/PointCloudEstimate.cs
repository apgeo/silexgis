// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Scans;

/// <summary>One row of the estimate: what tiling the cloud at this voxel size would come to.</summary>
/// <param name="VoxelCm">The voxel size in centimetres, or null for the full cloud.</param>
/// <param name="Points">How many points would remain; null when the cloud's extent is too large for a grid of this size.</param>
/// <param name="EstimatedTileBytes">About how many bytes of tiles those points make, from figures measured on real recordings.</param>
/// <param name="EstimatedSeconds">About how long tiling them takes, likewise.</param>
public sealed record PointCloudEstimateRow(double? VoxelCm, long? Points, long? EstimatedTileBytes, int? EstimatedSeconds);

/// <summary>
/// What one pass over the cloud said: how many points remain at each of a few voxel sizes, with
/// the tile size and tiling time of each, beside the same figures for the full cloud. Kept on the
/// scan's facts so the table can be shown again, and so the choice it led to can be read beside it.
/// </summary>
/// <param name="Full">The full cloud's row.</param>
/// <param name="Voxels">One row per voxel size counted, in the order they were asked for.</param>
public sealed record PointCloudEstimate(PointCloudEstimateRow Full, IReadOnlyList<PointCloudEstimateRow> Voxels);

/// <summary>How strongly the installation advises thinning a cloud before it is shown.</summary>
public enum PointCloudDisplayAdviceLevel
{
    /// <summary>The full cloud is within every threshold.</summary>
    None = 0,

    /// <summary>The full cloud is above the warning threshold in points or in tile bytes: thinning is suggested.</summary>
    Warn = 1,

    /// <summary>The full cloud is above the advice threshold in points: thinning is strongly advised.</summary>
    Advise = 2,
}

/// <summary>
/// The installation's advice on a cloud, computed from the estimate and the thresholds in force
/// when the estimate was made. The thresholds are kept beside the verdict, so a later change of
/// them does not quietly change what a person was told.
/// </summary>
/// <param name="Level">How strongly thinning is advised.</param>
/// <param name="SuggestedVoxelCm">The smallest voxel size whose result fits within the warning thresholds, or null when none does or none is needed.</param>
/// <param name="WarnPoints">The point count above which thinning is suggested.</param>
/// <param name="AdvisePoints">The point count above which thinning is strongly advised.</param>
/// <param name="WarnTileBytes">The estimated tile size above which thinning is suggested.</param>
public sealed record PointCloudDisplayAdvice(
    PointCloudDisplayAdviceLevel Level, double? SuggestedVoxelCm, long WarnPoints, long AdvisePoints, long WarnTileBytes)
{
    /// <summary>
    /// The advice for an estimate under these thresholds. The suggestion is the smallest voxel
    /// (the most detail) whose points and tile bytes both sit under the warning thresholds; when
    /// even the coarsest voxel does not fit, the coarsest one that was counted is suggested, so a
    /// person is never advised to thin and offered nothing to thin to.
    /// </summary>
    public static PointCloudDisplayAdvice For(PointCloudEstimate estimate, long warnPoints, long advisePoints, long warnTileBytes)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        var full = estimate.Full;
        var level =
            full.Points >= advisePoints ? PointCloudDisplayAdviceLevel.Advise
            : full.Points >= warnPoints || full.EstimatedTileBytes >= warnTileBytes ? PointCloudDisplayAdviceLevel.Warn
            : PointCloudDisplayAdviceLevel.None;

        double? suggested = null;
        if (level != PointCloudDisplayAdviceLevel.None)
        {
            var counted = estimate.Voxels
                .Where(v => v.VoxelCm is not null && v.Points is not null)
                .OrderBy(v => v.VoxelCm)
                .ToList();
            suggested = counted
                .FirstOrDefault(v => v.Points < warnPoints && (v.EstimatedTileBytes ?? 0) < warnTileBytes)?.VoxelCm
                ?? counted.LastOrDefault()?.VoxelCm;
        }

        return new PointCloudDisplayAdvice(level, suggested, warnPoints, advisePoints, warnTileBytes);
    }
}

/// <summary>How much of a cloud is shown: the person's choice, which becomes the recipe's downsample step.</summary>
/// <param name="Method"><c>full</c> for the whole cloud, <c>voxel</c> for one point per cell, <c>everyNth</c> for every n-th point.</param>
/// <param name="VoxelCm">The cell size in centimetres, for <c>voxel</c>.</param>
/// <param name="EveryNth">The stride, for <c>everyNth</c>.</param>
public sealed record PointCloudDisplayChoice(string Method, double? VoxelCm, int? EveryNth)
{
    public const string Full = "full";
    public const string Voxel = "voxel";
    public const string EveryNthPoint = "everyNth";

    public static readonly IReadOnlyList<string> Methods = [Full, Voxel, EveryNthPoint];
}
