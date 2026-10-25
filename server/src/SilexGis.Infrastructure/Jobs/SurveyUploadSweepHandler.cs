// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Closes survey uploads whose device stopped sending before every file arrived, and removes what
/// older versions of a survey produced when the installation keeps outputs for its newest versions
/// only.
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
/// <para>
/// Pruning takes only what a compile produced — models, drawings, the log — never what it was
/// compiled from, so a pruned version can be compiled again with a rerun.
/// </para>
/// </remarks>
public sealed class SurveyUploadSweepHandler(
    SilexGisDbContext db,
    SurveyUploadArtefactStore artefacts,
    IOptions<SurveyUploadOptions> options) : IProcessingJobHandler
{
    /// <summary>How many are closed in one pass, so a long backlog does not hold the queue.</summary>
    private const int BatchSize = 200;

    /// <summary>How many older versions one pass prunes, so a newly lowered setting is worked off over several passes.</summary>
    private const int PruneBatchSize = 50;

    public string Kind => ProcessingJobKinds.SurveyUploadSweep;

    /// <summary>Safe to run again from the start: it closes only what has expired, and closing twice is closing once.</summary>
    public bool RetriesPassingFailures => true;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        await CloseExpiredAsync(ct);
        await PruneOlderVersionsAsync(ct);
    }

    /// <summary>
    /// Removes what older versions of each survey produced, beyond the newest versions the
    /// installation keeps outputs for. Nothing when it keeps every version's, which is the default.
    /// </summary>
    private async Task PruneOlderVersionsAsync(CancellationToken ct)
    {
        var keep = options.Value.KeepArtefactVersions;
        if (keep <= 0)
        {
            return;
        }

        // Only surveys holding more versions than are kept can have anything to prune.
        var surveys = await db.SurveyUploads.AsNoTracking()
            .GroupBy(u => u.CaveSurveyId)
            .Where(g => g.Count() > keep)
            .Select(g => g.Key)
            .ToListAsync(ct);

        var pruned = 0;
        foreach (var surveyId in surveys)
        {
            var versions = await db.SurveyUploads.AsNoTracking()
                .Where(u => u.CaveSurveyId == surveyId)
                .Select(u => new
                {
                    u.Id,
                    u.VersionAt,
                    Finished = u.Status == SurveyUploadStatus.Succeeded || u.Status == SurveyUploadStatus.Failed
                        || u.Status == SurveyUploadStatus.Abandoned,
                    // A stored output, not merely a log: a version whose compile failed keeps its
                    // log and no output, and must not count as one of the newest versions kept in
                    // place of one that produced something.
                    HasOutputs = db.SurveyUploadFiles.Any(f => f.SurveyUploadId == u.Id
                        && f.Role == SurveyUploadFileRole.Output && f.Status == SurveyUploadFileStatus.Stored),
                })
                .ToListAsync(ct);
            foreach (var uploadId in SurveyUploadArtefacts.Prunable(
                versions.Select(v => (v.Id, v.VersionAt, v.Finished, v.HasOutputs)), keep))
            {
                await artefacts.PruneAsync(uploadId, ct);
                if (++pruned >= PruneBatchSize)
                {
                    return;
                }
            }
        }
    }

    private async Task CloseExpiredAsync(CancellationToken ct)
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
