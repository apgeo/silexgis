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
/// external worker can take it.
/// <para>
/// A pass is due when the last one was queued at least <see cref="FeatureIntegrityOptions.Interval"/>
/// ago, or none ever was — read from the queue itself, not from how long this process has run. A
/// timer of one whole interval from start never fires on an instance restarted more often than
/// that (a nightly update, a daily reset), and that instance would never be checked at all. So the
/// schedule looks every <see cref="CheckEvery"/> instead, the first time one such period after
/// start; a restart loop queues nothing extra, because a pass queued less than an interval ago is
/// not due.
/// </para>
/// </remarks>
public sealed class FeatureIntegrityScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<FeatureIntegrityOptions> options,
    TimeProvider clock,
    ILogger<FeatureIntegrityScheduler> logger) : BackgroundService
{
    /// <summary>How often the schedule looks whether a pass is due, and how long after start it first looks.</summary>
    internal static readonly TimeSpan CheckEvery = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.Interval;
        if (interval <= TimeSpan.Zero)
        {
            logger.LogInformation("Scheduled feature integrity verification is disabled");
            return;
        }

        var period = interval < CheckEvery ? interval : CheckEvery;
        while (await SafeDelayAsync(period, stoppingToken))
        {
            try
            {
                await QueueIfDueAsync(stoppingToken);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                // A scheduler that dies takes the schedule with it for the process's
                // lifetime, so no failure here is worth stopping for.
                logger.LogError(e, "Could not queue the feature integrity pass; will retry next tick");
            }
        }
    }

    private async Task<bool> SafeDelayAsync(TimeSpan period, CancellationToken ct)
    {
        try
        {
            await Task.Delay(period, clock, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Queues a pass when one is due and none is waiting or running; says whether it did.</summary>
    internal async Task<bool> QueueIfDueAsync(CancellationToken ct)
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
            return false;
        }

        var now = clock.GetUtcNow();
        var last = await db.ProcessingJobs.AsNoTracking()
            .Where(j => j.Kind == ProcessingJobKinds.FeatureIntegrityVerify)
            .MaxAsync(j => (DateTimeOffset?)j.CreatedAt, ct);
        if (last is { } queued && now - queued < options.Value.Interval)
        {
            return false;
        }

        // No requester: a scheduled pass notifies nobody. Its outcome belongs in the log
        // and the admin jobs view, not in somebody's notification list.
        db.ProcessingJobs.Add(new ProcessingJob { Kind = ProcessingJobKinds.FeatureIntegrityVerify, CreatedAt = now });
        await db.SaveChangesAsync(ct);
        return true;
    }
}
