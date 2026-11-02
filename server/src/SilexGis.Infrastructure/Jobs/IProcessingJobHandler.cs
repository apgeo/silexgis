// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Executes one kind of <see cref="ProcessingJob"/>. Handlers are resolved from the
/// worker's scope per job, so they can take scoped dependencies (DbContext, …).
/// </summary>
public interface IProcessingJobHandler
{
    string Kind { get; }

    /// <summary>
    /// Whether a run that failed in passing — a dropped connection, a timeout, a service briefly
    /// not answering — may simply be run again from the start. Off unless a handler says so, and a
    /// handler says so only when running it twice cannot do anything twice: a kind that sends a
    /// message, writes a file a person will receive, or keeps its own account of attempts stays
    /// off. See <see cref="JobRetry"/> for what counts as passing and how often.
    /// </summary>
    bool RetriesPassingFailures => false;

    Task ExecuteAsync(ProcessingJob job, CancellationToken ct);

    /// <summary>
    /// Called once when the queue gives a job up without running it again: its runner was lost on
    /// the last attempt it was allowed — the process died under it, or was stopped — so
    /// <see cref="ExecuteAsync"/> never got to record how it ended. A handler that marks what it
    /// works on as in progress closes that mark here, or the record says "in progress" for good
    /// while the job says failed. Nothing by default: most kinds keep no state of their own.
    /// </summary>
    /// <remarks>
    /// Runs inside the sweep's transaction on the scope's own context, so the record's stop and
    /// the job's commit together. It writes only that stop: it is not the job being run again, and
    /// nothing the lost attempt did may be assumed to have finished.
    /// </remarks>
    Task AbandonedAsync(ProcessingJob job, CancellationToken ct) => Task.CompletedTask;
}
