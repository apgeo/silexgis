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
}
