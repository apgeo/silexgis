// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Stands for a job whose runner died on the last attempt the queue allows, and for the worker's
/// sweep that then gives it up — so a test can see what the job's real handler leaves on its record
/// when <see cref="IProcessingJobHandler.ExecuteAsync"/> never got to say how the run ended.
/// </summary>
public static class StaleJobSweep
{
    /// <summary>
    /// Leaves a job as a process that died under it would: Running, on its last allowed attempt,
    /// its lease run out a minute ago.
    /// </summary>
    public static async Task LoseOnLastAttemptAsync(IServiceProvider services, long jobId)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var now = DateTimeOffset.UtcNow;
        await db.ProcessingJobs.Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, ProcessingJobStatus.Running)
                .SetProperty(j => j.StartedAt, now.AddMinutes(-10))
                .SetProperty(j => j.Attempts, JobRetry.MaxAttempts)
                .SetProperty(j => j.LeaseExpiresAt, now.AddMinutes(-1)));
    }

    /// <summary>One pass of the stale-lease sweep over a lane holding only <paramref name="kinds"/>.</summary>
    public static Task SweepAsync(IServiceProvider services, params string[] kinds) =>
        new Sweeper(
                new JobLane(kinds, Excluded: false),
                services.GetRequiredService<IServiceScopeFactory>(),
                services.GetRequiredService<IConfiguration>())
            .SweepAsync(CancellationToken.None);

    private sealed class Sweeper(JobLane lane, IServiceScopeFactory scopes, IConfiguration configuration)
        : ProcessingJobWorkerBase(lane, scopes, configuration, NullLogger.Instance)
    {
        public Task SweepAsync(CancellationToken ct) => ReturnStaleAsync(ct);
    }
}
