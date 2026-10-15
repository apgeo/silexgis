// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Terrain;

/// <summary>What asking to call a build off does, given where the build is.</summary>
public enum TerrainCancelOutcome
{
    /// <summary>Nothing has started, so the build stops at once and nothing runs.</summary>
    StopNow = 0,

    /// <summary>
    /// A worker has it. The request is recorded and the run stops at the first point it can — between
    /// two steps, between two files, or as soon as the tile-maker confirms it has stopped.
    /// </summary>
    AskToStop = 1,

    /// <summary>
    /// Already asked, or already stopped for that reason. Answered as done, because what was asked
    /// for is already true or on its way.
    /// </summary>
    AlreadyAsked = 2,

    /// <summary>The build has already stopped some other way; there is nothing left to call off.</summary>
    NotCancellable = 3,
}

/// <summary>
/// Calling a build off, and running a stopped one again: which states allow which.
/// </summary>
/// <remarks>
/// <para>
/// Kept here rather than in the route that asks, because the screen offers both actions from the
/// same facts and a handler deciding them on its own would be a second, drifting copy of the rule.
/// </para>
/// <para>
/// A called-off build is a <i>failed</i> build with its own reason, deliberately, rather than a
/// state of its own. It is exactly a build that stopped without a usable pyramid; everything that
/// already handles those — deleting one, running one again, the guard against building one area
/// twice — then handles it too, and the screen tells it apart by its reason the way it already
/// tells a build stopped for want of a tile-maker apart.
/// </para>
/// </remarks>
public static class TerrainBuildStopRules
{
    /// <summary>What a request to call this build off does.</summary>
    public static TerrainCancelOutcome Cancel(
        TerrainBuildStatus status, string? errorCode, DateTimeOffset? cancelRequestedAt) =>
        status switch
        {
            TerrainBuildStatus.Queued => TerrainCancelOutcome.StopNow,
            TerrainBuildStatus.Running => cancelRequestedAt is null
                ? TerrainCancelOutcome.AskToStop
                : TerrainCancelOutcome.AlreadyAsked,
            TerrainBuildStatus.Failed when IsCancelled(status, errorCode) => TerrainCancelOutcome.AlreadyAsked,
            _ => TerrainCancelOutcome.NotCancellable,
        };

    /// <summary>Whether this build stopped because somebody called it off.</summary>
    public static bool IsCancelled(TerrainBuildStatus status, string? errorCode) =>
        status == TerrainBuildStatus.Failed && errorCode == TerrainBuildFailures.Cancelled;

    /// <summary>
    /// Whether a build may be put back on the queue to carry on from where it stopped.
    /// </summary>
    /// <remarks>
    /// Only a stopped build, whichever way it stopped. A finished one has nothing left to do, and a
    /// queued or running one is already doing it — a second run of the same build would be two
    /// programs writing one directory.
    /// </remarks>
    public static bool MayRetry(TerrainBuildStatus status) => status == TerrainBuildStatus.Failed;
}
