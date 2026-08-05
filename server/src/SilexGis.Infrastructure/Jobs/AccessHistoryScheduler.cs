// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Queues the periodic pass that deletes access-history rows past their retention window.
/// </summary>
/// <remarks>
/// A record of who read what grows with use rather than with the archive, so it is the one
/// table in this system that would keep growing whether or not anybody added anything. Its
/// bound is a schedule, not a promise: personal data nobody has decided to keep is deleted
/// by something that runs on its own. This schedules; the pass itself rides the ordinary
/// processing queue so it is visible and retryable like any other job. Nothing is queued at
/// startup — a restart loop would otherwise fill the queue with sweeps.
/// </remarks>
public sealed class AccessHistoryScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<AccessHistoryOptions> options,
    ILogger<AccessHistoryScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        // Only the interval decides whether there is a schedule. A retention of zero is not a
        // reason to stop sweeping — it is the strongest possible reason to sweep, because it
        // says the installation would rather not hold any of this, and everything already
        // collected is past a window of zero length. Treating it as "nothing to do" would keep
        // the very rows the setting was turned to zero to be rid of.
        if (!settings.SweepIsScheduled)
        {
            logger.LogInformation("Scheduled access-history pruning is disabled");
            return;
        }

        using var timer = new PeriodicTimer(settings.SweepInterval);
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
                logger.LogError(e, "Could not queue the access-history prune; will retry next tick");
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

        var pending = await db.ProcessingJobs.AnyAsync(
            j => j.Kind == ProcessingJobKinds.AccessHistoryPrune
                && (j.Status == ProcessingJobStatus.Queued || j.Status == ProcessingJobStatus.Running),
            ct);
        if (pending)
        {
            return;
        }

        // No requester: a scheduled sweep notifies nobody.
        db.ProcessingJobs.Add(new ProcessingJob { Kind = ProcessingJobKinds.AccessHistoryPrune });
        await db.SaveChangesAsync(ct);
    }
}
