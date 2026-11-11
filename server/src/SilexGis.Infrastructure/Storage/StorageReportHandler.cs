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

/// <summary>Writes one storage report. It only measures: nothing is deleted by it, ever.</summary>
public sealed class StorageReportHandler(StorageReportService reports) : IProcessingJobHandler
{
    public string Kind => ProcessingJobKinds.StorageReport;

    /// <summary>Safe to run again: a second report of the same day is one more measurement, not a wrong one.</summary>
    public bool RetriesPassingFailures => true;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct) => await reports.WriteAsync(ct);
}

/// <summary>
/// Keeps one storage report per <see cref="StorageOptions.ReportInterval"/>: it looks every hour
/// (or every interval, when that is shorter) and queues a report when the newest one kept is
/// older than the interval, or there is none.
/// </summary>
/// <remarks>
/// The age of the newest report decides, not the time since the process started: a server
/// restarted every day — by an update, a deploy or a reboot — would otherwise never reach its
/// first tick, and never write a report. Nothing is queued at the moment of start-up, and a look
/// is skipped while a report is still waiting or being written, so a restart loop cannot stack
/// reports either.
/// </remarks>
public sealed class StorageReportScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<StorageOptions> options,
    TimeProvider clock,
    ILogger<StorageReportScheduler> logger) : BackgroundService
{
    /// <summary>The longest wait between two looks at whether one is due.</summary>
    private static readonly TimeSpan MaxPoll = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.ReportInterval;
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
                logger.LogError(e, "Could not queue the storage report; will retry next tick");
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
            j => j.Kind == ProcessingJobKinds.StorageReport
                && (j.Status == ProcessingJobStatus.Queued || j.Status == ProcessingJobStatus.Running),
            ct);
        if (pending)
        {
            return;
        }

        // Due a look early rather than a look late: without the allowance every one would come
        // up to one look after the interval, and the schedule would drift by that much each time.
        var newest = await db.StorageReportSnapshots.AsNoTracking().MaxAsync(s => (DateTimeOffset?)s.TakenAt, ct);
        if (newest is { } last && clock.GetUtcNow() - last < interval - poll)
        {
            return;
        }

        // No requester: a scheduled report is nobody's job, and nobody is told it finished.
        db.ProcessingJobs.Add(new ProcessingJob { Kind = ProcessingJobKinds.StorageReport });
        await db.SaveChangesAsync(ct);
    }
}
