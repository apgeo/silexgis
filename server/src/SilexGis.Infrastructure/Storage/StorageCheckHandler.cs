// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Storage;

/// <summary>Runs one storage check: measures, and tells administrators about any threshold crossed.</summary>
/// <remarks>
/// Not retried on a passing failure: the next scheduled pass measures afresh, and a crossing it
/// missed is still a crossing then, because the recorded level only moves when the message is queued.
/// </remarks>
public sealed class StorageCheckHandler(StorageAlerts alerts, ILogger<StorageCheckHandler> logger) : IProcessingJobHandler
{
    public string Kind => ProcessingJobKinds.StorageCheck;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var crossings = await alerts.CheckAsync(ct);
        foreach (var crossing in crossings)
        {
            logger.LogWarning(
                "Storage {Limit} climbed from {From} to {To}; administrators were told",
                crossing.Kind, crossing.From, crossing.To);
        }
    }
}

/// <summary>
/// Keeps a storage check running every <see cref="StorageOptions.CheckInterval"/>: it looks every
/// ten minutes (or every interval, when that is shorter) and queues a check when the newest one
/// queued is older than the interval, or there is none.
/// </summary>
/// <remarks>
/// The age of the last check decides, not the time since the process started, so a server
/// restarted more often than the interval is still checked. Nothing is queued at the moment of
/// start-up, and a look is skipped while a check is still waiting or running, so a restart loop
/// cannot fill the queue with checks.
/// </remarks>
public sealed class StorageCheckScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<StorageOptions> options,
    TimeProvider clock,
    ILogger<StorageCheckScheduler> logger) : BackgroundService
{
    /// <summary>The longest wait between two looks at whether one is due.</summary>
    private static readonly TimeSpan MaxPoll = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.CheckInterval;
        if (interval <= TimeSpan.Zero)
        {
            return;
        }

        var poll = interval < MaxPoll ? interval : MaxPoll;
        using var timer = new PeriodicTimer(poll);
        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                await QueueIfDueAsync(interval, poll, stoppingToken);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                // A scheduler that dies takes the schedule with it for the process's lifetime.
                logger.LogError(e, "Could not queue the storage check; will retry next tick");
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

    internal async Task QueueIfDueAsync(TimeSpan interval, TimeSpan poll, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var pending = await db.ProcessingJobs.AnyAsync(
            j => j.Kind == ProcessingJobKinds.StorageCheck
                && (j.Status == ProcessingJobStatus.Queued || j.Status == ProcessingJobStatus.Running),
            ct);
        if (pending)
        {
            return;
        }

        // Due a look early rather than a look late: without the allowance every one would come
        // up to one look after the interval, and the schedule would drift by that much each time.
        var newest = await db.ProcessingJobs.AsNoTracking().Where(j => j.Kind == ProcessingJobKinds.StorageCheck).MaxAsync(j => (DateTimeOffset?)j.CreatedAt, ct);
        if (newest is { } last && clock.GetUtcNow() - last < interval - poll)
        {
            return;
        }

        // No requester: a scheduled check is nobody's job, and its messages are its own.
        db.ProcessingJobs.Add(new ProcessingJob { Kind = ProcessingJobKinds.StorageCheck });
        await db.SaveChangesAsync(ct);
    }
}
