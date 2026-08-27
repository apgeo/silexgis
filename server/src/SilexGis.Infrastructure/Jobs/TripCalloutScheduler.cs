// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Queues the periodic pass that looks for overdue parties and for trips that are nearly here.
/// </summary>
/// <remarks>
/// <para>
/// This schedules; it does not look. The pass itself rides the ordinary processing queue, so that
/// it is visible, retryable and attributable like any other job — and so that its rows are the
/// record of when the check last ran, which is what a page showing an armed alarm needs in order
/// to say <i>unchecked</i> rather than imply <i>nothing wrong</i>.
/// </para>
/// <para>
/// Nothing is queued at startup: a restart loop would otherwise fill the queue with passes, and the
/// first tick is soon enough for a check whose period is measured in minutes.
/// </para>
/// </remarks>
public sealed class TripCalloutScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<TripCalloutOptions> options,
    ILogger<TripCalloutScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.SweepIsScheduled)
        {
            logger.LogInformation("The scheduled overdue-party check is switched off");
            return;
        }

        // Never longer than the ceiling, because a trip page reports a check nothing has run
        // lately as unchecked and a permanent warning is one nobody reads.
        if (settings.EffectiveSweepInterval < settings.SweepInterval)
        {
            logger.LogInformation(
                "The overdue-party check is configured every {Configured} and will run every {Actual}",
                settings.SweepInterval, settings.EffectiveSweepInterval);
        }

        using var timer = new PeriodicTimer(settings.EffectiveSweepInterval);
        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                await QueuePassAsync(stoppingToken);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                // A scheduler that dies takes the schedule with it for the process's lifetime, so
                // no failure here is worth stopping for.
                logger.LogError(e, "Could not queue the overdue-party check; will retry next tick");
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

    /// <summary>
    /// Queues one pass, unless one is already waiting or running.
    /// </summary>
    /// <remarks>
    /// Reachable without a live schedule on purpose: a schedule started for a test is a schedule
    /// writing to whatever database that test shares, and the thing most worth proving here — that
    /// a tick queues exactly one pass, and a second tick beside a pending one queues none — is
    /// provable by asking for the tick directly.
    /// </remarks>
    public async Task QueuePassAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // One pass at a time. This is about load, not about correctness: a second pass running
        // beside the first would raise no second alarm, because each trip leaves the armed state in
        // the same save that queues the message about it. What it would do is read the same rows
        // twice for nothing.
        var pending = await db.ProcessingJobs.AnyAsync(
            j => j.Kind == ProcessingJobKinds.TripCalloutSweep
                && (j.Status == ProcessingJobStatus.Queued || j.Status == ProcessingJobStatus.Running),
            ct);
        if (pending)
        {
            return;
        }

        // No requester: a scheduled pass notifies nobody about itself. Who it does notify is the
        // people the trips it finds concern.
        db.ProcessingJobs.Add(new ProcessingJob { Kind = ProcessingJobKinds.TripCalloutSweep });
        await db.SaveChangesAsync(ct);
    }
}
