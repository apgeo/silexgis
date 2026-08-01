// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

public sealed class FeatureIntegrityOptions
{
    public const string SectionName = "FeatureIntegrity";

    /// <summary>
    /// How often to queue a verification pass. Zero or negative disables the schedule
    /// entirely; an operator can still queue a pass by hand from the admin jobs view.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);
}

/// <summary>
/// Queues a periodic feature-integrity pass. The derived state the aggregate write
/// service maintains (ancestor caches, effective protection, delegated access copies) is
/// security-bearing, so its correctness is checked on a schedule rather than trusted.
/// </summary>
/// <remarks>
/// This schedules; it does not verify. The work rides the existing processing queue so a
/// pass is visible, retryable and attributable like any other job, and so a future
/// external worker can take it. Nothing is queued at startup: a restart loop would
/// otherwise fill the queue with passes, and the first tick is soon enough for a check
/// whose period is measured in hours.
/// </remarks>
public sealed class FeatureIntegrityScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<FeatureIntegrityOptions> options,
    ILogger<FeatureIntegrityScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.Interval;
        if (interval <= TimeSpan.Zero)
        {
            logger.LogInformation("Scheduled feature integrity verification is disabled");
            return;
        }

        using var timer = new PeriodicTimer(interval);
        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                await QueuePassAsync(stoppingToken);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                // A scheduler that dies takes the schedule with it for the process's
                // lifetime, so no failure here is worth stopping for.
                logger.LogError(e, "Could not queue the feature integrity pass; will retry next tick");
            }
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task QueuePassAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // One pass at a time: on a big instance a pass can outlast the interval, and
        // stacking them would multiply the load rather than report anything new.
        var pending = await db.ProcessingJobs.AnyAsync(
            j => j.Kind == ProcessingJobKinds.FeatureIntegrityVerify
                && (j.Status == ProcessingJobStatus.Queued || j.Status == ProcessingJobStatus.Running),
            ct);
        if (pending)
        {
            logger.LogInformation("A feature integrity pass is still pending; skipping this tick");
            return;
        }

        // No requester: a scheduled pass notifies nobody. Its outcome belongs in the log
        // and the admin jobs view, not in somebody's notification list.
        db.ProcessingJobs.Add(new ProcessingJob { Kind = ProcessingJobKinds.FeatureIntegrityVerify });
        await db.SaveChangesAsync(ct);
    }
}
