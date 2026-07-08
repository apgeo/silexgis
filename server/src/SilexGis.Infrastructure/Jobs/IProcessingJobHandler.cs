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

    Task ExecuteAsync(ProcessingJob job, CancellationToken ct);
}
