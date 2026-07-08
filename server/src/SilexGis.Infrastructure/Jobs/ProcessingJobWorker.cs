// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Polls the processing_jobs table and dispatches to the registered handler for the
/// job's kind. One job at a time per worker; failures are recorded on the row and
/// never crash the host. A DB-backed queue keeps the seam open for external workers.
/// </summary>
public sealed class ProcessingJobWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ProcessingJobWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Migrations run in the host startup path before the app starts serving,
        // but hosted services start concurrently — tolerate a briefly missing table.
        await WaitForQueueAsync(stoppingToken);

        using (var startupScope = scopeFactory.CreateScope())
        {
            var db = startupScope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await JobsSql.RequeueInterruptedAsync(db, stoppingToken);
        }

        using var timer = new PeriodicTimer(PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Drain the queue, then sleep until the next tick.
                while (await RunNextAsync(stoppingToken))
                {
                }

                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Job worker iteration failed; continuing");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    private async Task<bool> RunNextAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var jobId = await JobsSql.ClaimNextAsync(db, ct);
        if (jobId is null)
        {
            return false;
        }

        var job = await db.ProcessingJobs.FindAsync([jobId.Value], ct)
            ?? throw new InvalidOperationException($"Claimed job {jobId} not found.");

        var handler = scope.ServiceProvider
            .GetServices<IProcessingJobHandler>()
            .FirstOrDefault(h => h.Kind == job.Kind);

        if (handler is null)
        {
            job.Status = ProcessingJobStatus.Failed;
            job.Error = $"No handler registered for job kind '{job.Kind}'.";
            job.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            logger.LogError("No handler for job {JobId} of kind {Kind}", job.Id, job.Kind);
            return true;
        }

        try
        {
            await handler.ExecuteAsync(job, ct);
            job.Status = ProcessingJobStatus.Succeeded;
            job.Error = null;
        }
        catch (Exception e)
        {
            job.Status = ProcessingJobStatus.Failed;
            job.Error = e.Message;
            logger.LogError(e, "Job {JobId} ({Kind}) failed", job.Id, job.Kind);
        }

        job.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task WaitForQueueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
                _ = await db.ProcessingJobs.AnyAsync(ct);
                return;
            }
            catch (Exception)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
    }
}
