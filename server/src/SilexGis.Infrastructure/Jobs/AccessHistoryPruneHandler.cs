// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Deletes access-history rows older than the retention window. Idempotent by shape — the
/// queue re-delivers a job after a restart, and a second pass over an already-swept table
/// deletes nothing.
/// </summary>
public sealed class AccessHistoryPruneHandler(
    SilexGisDbContext db,
    IOptions<AccessHistoryOptions> options,
    ILogger<AccessHistoryPruneHandler> logger) : IProcessingJobHandler
{
    public string Kind => ProcessingJobKinds.AccessHistoryPrune;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var retention = options.Value.Retention;
        if (retention <= TimeSpan.Zero)
        {
            // Recording is off. Everything already held is past a window of zero length, so
            // turning it off is also the way to be rid of what was collected before.
            var all = await db.FileAccessEvents.ExecuteDeleteAsync(ct);
            logger.LogInformation("Access history is disabled; deleted {Count} retained rows", all);
            return;
        }

        var cutoff = DateTimeOffset.UtcNow - retention;
        var deleted = await db.FileAccessEvents.Where(e => e.At < cutoff).ExecuteDeleteAsync(ct);
        logger.LogInformation("Deleted {Count} access-history rows older than {Cutoff:o}", deleted, cutoff);
    }
}
