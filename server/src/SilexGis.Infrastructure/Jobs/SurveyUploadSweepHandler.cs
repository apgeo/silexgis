// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Closes survey uploads whose device stopped sending before every file arrived.
/// </summary>
/// <remarks>
/// <para>
/// Only the session ends. The files that did arrive were archived on arrival and stay archived:
/// they are the caver's survey, and taking them away is an act of its own on the cave page, never
/// something a timer does. What closing does is say so — the upload reads as abandoned rather than
/// receiving for ever, its upload batch is closed, and a device that comes back is told to open a
/// new upload rather than sending into one nothing will ever run.
/// </para>
/// <para>
/// A tracked save rather than a direct update: closing an upload is a change of state worth its
/// line in the cave's history, as it is when the device abandons the upload itself.
/// </para>
/// </remarks>
public sealed class SurveyUploadSweepHandler(SilexGisDbContext db) : IProcessingJobHandler
{
    /// <summary>How many are closed in one pass, so a long backlog does not hold the queue.</summary>
    private const int BatchSize = 200;

    public string Kind => ProcessingJobKinds.SurveyUploadSweep;

    /// <summary>Safe to run again from the start: it closes only what has expired, and closing twice is closing once.</summary>
    public bool RetriesPassingFailures => true;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var expired = await db.SurveyUploads
            .Where(u => u.Status == SurveyUploadStatus.Receiving && u.ExpiresAt <= now)
            .OrderBy(u => u.ExpiresAt)
            .Take(BatchSize)
            .ToListAsync(ct);
        if (expired.Count == 0)
        {
            return;
        }

        var batchIds = expired.Select(u => u.UploadBatchId).ToList();
        var batches = await db.UploadBatches
            .Where(b => batchIds.Contains(b.Id) && b.Status == UploadBatchStatus.Open)
            .ToListAsync(ct);

        foreach (var upload in expired)
        {
            upload.Status = SurveyUploadStatus.Abandoned;
            upload.Message = "Closed: not every file arrived in time. What did arrive is archived.";
            upload.FinishedAt = now;
        }

        foreach (var batch in batches)
        {
            batch.Status = UploadBatchStatus.Completed;
            batch.CompletedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Queues the periodic closing of survey uploads that stopped receiving.</summary>
/// <remarks>
/// Scheduled rather than triggered, because the event it responds to is a device <em>not</em>
/// sending something. Nothing is queued at startup: a restart loop would otherwise fill the queue
/// with sweeps.
/// </remarks>
public sealed class SurveyUploadSweepScheduler(
    IServiceScopeFactory scopeFactory,
    ILogger<SurveyUploadSweepScheduler> logger) : BackgroundService
{
    /// <summary>
    /// How often expired uploads are closed — well inside the shortest session an installation may
    /// set, so an upload is closed within the hour it expired.
    /// </summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);
        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                await QueuePassAsync(stoppingToken);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                // A scheduler that dies takes the schedule with it for the process's lifetime.
                logger.LogError(e, "Could not queue the survey-upload sweep; will retry next tick");
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
            j => j.Kind == ProcessingJobKinds.SurveyUploadSweep
                && (j.Status == ProcessingJobStatus.Queued || j.Status == ProcessingJobStatus.Running),
            ct);
        if (pending)
        {
            return;
        }

        // No requester: a scheduled sweep notifies nobody.
        db.ProcessingJobs.Add(new ProcessingJob { Kind = ProcessingJobKinds.SurveyUploadSweep });
        await db.SaveChangesAsync(ct);
    }
}
