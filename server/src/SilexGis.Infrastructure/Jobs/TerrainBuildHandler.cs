// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>Payload contract for <see cref="ProcessingJobKinds.TerrainBuild"/> jobs.</summary>
/// <remarks>
/// The key of the build row and nothing else. Everything the run needs — the area, the depth, the
/// datum, the sources — is read back off that row, because a build resumed hours later must be the
/// build that was asked for and the row is the only copy of that answer which survives an edit.
/// </remarks>
public sealed record TerrainBuildPayload(Guid TerrainBuildId);

/// <summary>
/// Walks one terrain build through the chain: obtain rasters, prepare them, mesh them, check the
/// mesh, publish it.
/// </summary>
/// <remarks>
/// <para>
/// One job row drives the whole chain rather than one job per step. The steps share large
/// intermediate files on disk, a half-built pyramid is not a meaningful unit of work to resume from
/// on its own, and the queue's own retry would replay a step against inputs the previous attempt
/// had already consumed.
/// </para>
/// <para>
/// The walk runs the steps this installation has an implementation for, in order, and stops at the
/// first one nobody implements. Stopping there is a <b>success</b>, at the phase actually reached:
/// the chain is being built out a step at a time, and a build that did everything there is to do
/// is not a failure. It is also not the terrain the scene draws — that is a third question, asked
/// of the active flag, and nothing here answers it.
/// </para>
/// <para>
/// Being handed the same build twice is ordinary. Every row a crashed process left running is put
/// back on the queue when the process starts again and nothing counts those restarts down, so a
/// step whose output is already there and checks out is skipped rather than redone, and a build
/// that has already ended badly is stopped rather than started again.
/// </para>
/// </remarks>
public sealed class TerrainBuildHandler(
    SilexGisDbContext db,
    IEnumerable<ITerrainPhase> phases,
    TerrainWorkspace workspace,
    ILogger<TerrainBuildHandler> logger) : IProcessingJobHandler
{
    /// <summary>
    /// How many times one build may be handed to a worker before it is stopped for good.
    /// </summary>
    /// <remarks>
    /// The queue counts attempts up and never reads them, and it re-queues everything a dead
    /// process left running — so a build that brings its host down brings it down again on every
    /// restart, for ever, and nothing in the queue would look wrong. Three is enough for a genuine
    /// one-off (a machine restarted under it, a disk briefly full) and few enough that a loop stops
    /// the same day it starts.
    /// </remarks>
    private const int MaxAttempts = 3;

    public string Kind => ProcessingJobKinds.TerrainBuild;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<TerrainBuildPayload>(job.Payload, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Terrain build payload is not readable.");

        // Read rather than tracked: every write to this row is a direct update statement, and a
        // tracked copy sitting in the change tracker alongside them would be a second, stale
        // opinion about the row waiting for something to save it.
        var build = await db.TerrainBuilds.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == payload.TerrainBuildId, ct);
        if (build is null)
        {
            // Deleted while it waited. An ordinary outcome for queued work, not a failure.
            return;
        }

        if (build.Status == TerrainBuildStatus.Succeeded)
        {
            // Re-delivered after it had already finished. One query and nothing else.
            return;
        }

        if (StoppedForRepeating(build, job) is { } reason)
        {
            logger.LogWarning(
                "Terrain build {BuildId} stopped after {Attempts} attempts ({Reason})",
                build.Id, job.Attempts, reason);
            // Nothing added to the tail, so the earlier attempt's own words stay. This row is
            // stopped *because* it already failed once, and what that failure said is the one
            // thing somebody diagnosing it needs — a build is kept, but the reason one broke
            // cannot be worked out again once it has been written over.
            await TerrainBuildWrites.FailAsync(
                db, build.Id, build.Phase, TerrainBuildFailures.RepeatedFailure, reason, null,
                CancellationToken.None);

            // Returned rather than thrown: there is nothing here to retry, and a job row left red
            // would invite exactly the retry this refused.
            return;
        }

        await TerrainBuildWrites.StartAsync(db, build.Id, TerrainBuildPhase.Pending, ct);

        // Two different phases, and confusing them is how a failure reports the wrong step. One is
        // the last step that finished, which is where a successful run stops; the other is the step
        // being attempted, which is what a failure has to name — a build that broke while obtaining
        // rasters and reported the step before it sends whoever reads it to the wrong place.
        var reached = TerrainBuildPhase.Pending;
        var attempting = TerrainBuildPhase.Pending;

        // Declared out here only so the sweep below can see it. Null means the working space was
        // never made, and then there is nothing to sweep.
        TerrainBuildDirectories? directories = null;
        try
        {
            // Inside the guard, and after the row says it is running, because both of these can
            // fail: the build root may be unwritable — a disk that filled, a directory the service
            // account does not own, a volume that was never mounted — and resolving who asked is a
            // query. Done before either, a failure here would throw with the row still saying
            // queued, and a build stuck at queued for ever says nothing at all about why.
            directories = workspace.For(build.Id);

            // What whoever asked for this may do now, rather than what they could when they asked.
            // The build row deliberately names nobody — who started one is answered from the audit
            // trail — so the queue row is where the requester is, and a build queued by the
            // installation itself has none. Steps that read the server's own disk refuse without one.
            var requester = job.RequestedBy is { } userId
                ? await AccessContextResolver.ResolveAsync(db, userId, ct)
                : null;

            foreach (var phase in TerrainPhases.Order)
            {
                attempting = phase;
                var step = phases.FirstOrDefault(p => p.Phase == phase);
                if (step is null)
                {
                    // Nothing here does this step yet. The chain stops where it stops, and what ran
                    // before it ran properly, so this is where a successful run ends today.
                    break;
                }

                var context = new TerrainBuildContext(
                    build,
                    directories,
                    (percent, message, token) => TerrainBuildWrites.ProgressAsync(
                        db, build.Id, phase, TerrainPhases.Overall(phase, percent), message, token),
                    requester,
                    (line, token) => TerrainBuildWrites.AppendLogAsync(db, build.Id, line, token));

                await context.ReportAsync(0, null, ct);

                if (await step.IsAlreadyDoneAsync(context, ct))
                {
                    logger.LogInformation(
                        "Terrain build {BuildId} skips {Phase}: its output is already there",
                        build.Id, phase);
                }
                else
                {
                    await step.RunAsync(context, ct);
                }

                reached = phase;
                await context.ReportAsync(100, null, ct);
            }

            await TerrainBuildWrites.SucceedAsync(
                db, build.Id, reached, TerrainPhases.Overall(reached, 100), null, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The host is shutting down, which is not this build's fault and not its verdict. The
            // row is left as it is so the sweep that runs at the next start picks it up again.
            throw;
        }
        catch (TerrainBuildException e)
        {
            await RecordAsync(build.Id, attempting, e.Code, e.Message, e.LogTail);
            throw BoundedForTheQueue(build.Id, e.Code, e);
        }
        catch (Exception e)
        {
            // A defect rather than something a tool said. The whole of it — the stack, the file
            // names, the line numbers, whatever a driver put in its message — goes to the log,
            // which is the operator's to read; the build row carries only the kind of failure it
            // was, because the row is served to anyone holding the terrain read right and the
            // inside of this server is not theirs.
            logger.LogError(e, "Terrain build {BuildId} failed unexpectedly at {Phase}", build.Id, attempting);
            await RecordAsync(
                build.Id,
                attempting,
                TerrainBuildFailures.Unexpected,
                "Something went wrong that this build did not expect. The server's log has it.",
                $"Stopped unexpectedly: {e.GetType().Name}.");
            throw BoundedForTheQueue(build.Id, TerrainBuildFailures.Unexpected, e);
        }
        finally
        {
            // Everything else this build owns is deliberately kept — that is what makes resuming
            // cheap — but a run's own working space must not outlive the run, or a failure leaves
            // gigabytes behind that nothing will ever look at or delete.
            if (directories is not null)
            {
                Sweep(directories.Scratch);
            }
        }
    }

    /// <summary>
    /// Why this build is not being started again, or null when there is no reason not to.
    /// </summary>
    /// <remarks>
    /// Two facts, and the second is the one that matters. A run beyond the attempt ceiling has been
    /// tried enough times. A run that already carries a recorded reason for stopping and is being
    /// handed over <i>again</i> failed once and would fail the same way now: the only thing that
    /// re-queues a finished row is a process dying, and dying twice over one build is a fact about
    /// the build.
    /// </remarks>
    private static string? StoppedForRepeating(TerrainBuild build, ProcessingJob job) =>
        job.Attempts > MaxAttempts
            ? $"Stopped after {MaxAttempts} attempts."
            : build.ErrorCode is { } previous && job.Attempts > 1
                ? $"Stopped: an earlier attempt already ended with {previous}."
                : null;

    private Task RecordAsync(
        Guid buildId, TerrainBuildPhase phase, string code, string message, string? logTail) =>
        // Not the caller's token: a build cancelled halfway still has to be able to say so, and a
        // cancelled token would take the recording down with the run.
        TerrainBuildWrites.FailAsync(db, buildId, phase, code, message, logTail, CancellationToken.None);

    /// <summary>
    /// The failure as the queue row may hold it: the build's key and a short code, never the tool's
    /// own words.
    /// </summary>
    /// <remarks>
    /// The queue stores whatever a handler throws, in a column of four thousand characters, and the
    /// tools this pipeline drives fail with stack dumps far longer than that. An unbounded message
    /// would be refused by the database <i>while the failure was being recorded</i>, which leaves
    /// the queue row reading as running for ever and never re-claimed. The long version is already
    /// on the build row's log tail, truncated, which is where somebody diagnosing this looks.
    /// </remarks>
    private static InvalidOperationException BoundedForTheQueue(Guid buildId, string code, Exception inner) =>
        new($"Terrain build {buildId} failed: {code}.", inner);

    private void Sweep(string scratch)
    {
        try
        {
            if (Directory.Exists(scratch))
            {
                Directory.Delete(scratch, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Worth knowing about and not worth losing the run's real outcome over: a file another
            // process still holds open is a disk problem, not a verdict on the build.
            logger.LogWarning(e, "Could not clear the working directory {Scratch}", scratch);
        }
    }
}
