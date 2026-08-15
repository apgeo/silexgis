// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Terrain;

/// <summary>
/// Everything one step of a build is given: the row it is working for, where it may write, and the
/// way it says how far it has got.
/// </summary>
/// <param name="Build">
/// The build as it was read when the run started. A snapshot, not a tracked entity: every write to
/// this row goes through a direct update statement, because the row is audited and a worker
/// nudging a number every few seconds would bury the acts a person actually took.
/// </param>
/// <param name="Directories">Where this build's files live.</param>
/// <param name="Requester">
/// What whoever asked for this build may do <b>as of now</b> — rebuilt when the run starts rather
/// than carried from the request that queued it, and null when the queue row names nobody.
/// </param>
/// <remarks>
/// Rights are re-read because a run happens minutes or hours after the request that started it, and
/// a right can be taken away in between. A step that reads the server's own disk asks this rather
/// than trusting the request: any other answer lets somebody who has since been stopped from doing
/// a thing still have it done for them, on a delay, by work they queued while they could.
/// </remarks>
/// <param name="Log">
/// Adds a line to the build's log tail while the run is going on. Null in a context built by
/// something that does not keep one, in which case the lines are dropped rather than refused.
/// </param>
public sealed record TerrainBuildContext(
    TerrainBuild Build,
    TerrainBuildDirectories Directories,
    Func<int, string?, CancellationToken, Task> Report,
    Domain.Access.AccessContext? Requester = null,
    Func<string, CancellationToken, Task>? Log = null)
{
    /// <summary>
    /// Says how far this step has got, on its own nought-to-a-hundred scale, and what it is doing.
    /// </summary>
    /// <remarks>
    /// A step reports its own progress and knows nothing about where it sits in the whole build;
    /// the walk maps it. Out-of-range numbers are safe — the mapping clamps — because a percentage
    /// derived from a tool's own count against an estimate is wrong often enough that a hundred and
    /// three is an ordinary thing to be handed.
    /// </remarks>
    public Task ReportAsync(int percent, string? message, CancellationToken ct) =>
        Report(percent, message, ct);

    /// <summary>
    /// Adds a line to what this build has said, kept and shown while it is still running.
    /// </summary>
    /// <remarks>
    /// Separate from progress because the two answer different questions. Progress is one sentence
    /// about now, overwritten every few seconds; this accumulates, and is where the words of the
    /// outside tools a build drives belong — a tool that degrades quietly says so once, in a line
    /// nothing else was watching for.
    /// </remarks>
    public Task LogAsync(string line, CancellationToken ct) =>
        Log is null ? Task.CompletedTask : Log(line, ct);
}

/// <summary>
/// One step of the terrain pipeline: obtaining rasters, preparing them, meshing them, checking the
/// mesh, publishing it.
/// </summary>
/// <remarks>
/// <para>
/// One implementation per step, resolved by the step it says it is — the same shape the processing
/// queue resolves a handler for a job kind by. It is what lets the chain be built out a step at a
/// time without the walk that drives it changing at all.
/// </para>
/// <para>
/// A step must be safe to run twice. Every row a crashed process left running is put back on the
/// queue when the process starts again, and nothing counts those restarts down, so being handed
/// the same build a second time is ordinary rather than exceptional.
/// </para>
/// </remarks>
public interface ITerrainPhase
{
    /// <summary>Which step of the chain this is.</summary>
    TerrainBuildPhase Phase { get; }

    /// <summary>
    /// Whether this step's output is already there and checks out, so the step can be skipped.
    /// </summary>
    /// <remarks>
    /// The question is asked of the disk, not of the row: a build that reached the meshing step and
    /// then had its host restarted must not fetch and reproject everything again to get back to
    /// where it was. It has to be a real check and not merely "the directory exists" — a half-
    /// written intermediate that is accepted as finished is how a build produces a pyramid with a
    /// hole in it and reports success.
    /// </remarks>
    Task<bool> IsAlreadyDoneAsync(TerrainBuildContext context, CancellationToken ct);

    /// <summary>
    /// Does the step. Throws <see cref="Domain.Terrain.TerrainBuildException"/> with a short code
    /// for anything an administrator should be told about.
    /// </summary>
    Task RunAsync(TerrainBuildContext context, CancellationToken ct);
}
