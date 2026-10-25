// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Deletes resumable uploads nobody came back to, and the partial bytes they hold.
///
/// <para>
/// A half-sent panorama is the one thing in the store that nothing points at: no document, no
/// attachment, no version. Without this it is permanent, and an installation whose members
/// upload over a bad connection would accumulate one per dropped transfer for ever. The
/// session is what makes the bytes findable, so it is what has to outlive them by exactly
/// nothing.
/// </para>
/// <para>
/// Each row is claimed before its bytes are touched: deleted in a statement of its own, which
/// commits at once, and the blob deleted only when that statement is what removed the row. Bytes
/// staged for a phone's upload are adopted by the upload that names them, which takes the row
/// away inside its own transaction and keeps the blob as the new document's file; a sweep that
/// deleted the blob first and the row second could do so while that transaction was still open,
/// and leave a document whose bytes are gone. The claim waits for that transaction instead, and
/// finds nothing left to claim. The price is a blob left behind if the process dies between the
/// two steps — a stranded file an operator can sweep, rather than a document that serves nothing.
/// </para>
/// </summary>
public sealed class UploadSessionSweepHandler(
    SilexGisDbContext db,
    IFileStore fileStore,
    ILogger<UploadSessionSweepHandler> logger) : IProcessingJobHandler
{
    /// <summary>How many are collected in one pass, so a long backlog does not hold the queue.</summary>
    private const int BatchSize = 200;

    public string Kind => ProcessingJobKinds.UploadSessionSweep;

    /// <summary>Safe to run again from the start: it removes only what has expired, and removing it twice is removing it once.</summary>
    public bool RetriesPassingFailures => true;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var expired = await db.UploadSessions.AsNoTracking()
            .Where(s => s.ExpiresAt <= now)
            .OrderBy(s => s.ExpiresAt)
            .Take(BatchSize)
            .Select(s => new { s.Id, s.StoragePath })
            .ToListAsync(ct);

        foreach (var session in expired)
        {
            // Re-checked against the expiry, so a session whose retention moved since it was read
            // is not claimed; zero rows means somebody else took it — an upload that adopted the
            // bytes, or another sweep — and its blob is not this pass's to delete.
            var claimed = await db.UploadSessions
                .Where(s => s.Id == session.Id && s.ExpiresAt <= now)
                .ExecuteDeleteAsync(ct);
            if (claimed == 0)
            {
                continue;
            }

            try
            {
                await fileStore.DeleteAsync(session.StoragePath, ct);
            }
            catch (IOException e)
            {
                // A blob that will not delete is a stranded file an operator can sweep; the row
                // is gone already, so the same failure is not retried for ever.
                logger.LogWarning(
                    e, "Could not delete the partial content of upload session {SessionId}", session.Id);
            }
        }
    }
}

/// <summary>
/// Queues the periodic collection of abandoned resumable uploads.
/// </summary>
/// <remarks>
/// Scheduled rather than triggered, because the event it responds to is somebody <em>not</em>
/// doing something — and nothing raises an event for that. Nothing is queued at startup: a
/// restart loop would otherwise fill the queue with sweeps.
/// </remarks>
public sealed class UploadSessionScheduler(
    IServiceScopeFactory scopeFactory,
    ILogger<UploadSessionScheduler> logger) : BackgroundService
{
    /// <summary>
    /// How often abandoned sessions are collected. Comfortably shorter than the lifetime they
    /// are measured against, so a session becomes collectable and is collected in the same
    /// day rather than surviving until whenever the next sweep happens to fall.
    /// </summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

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
                // A scheduler that dies takes the schedule with it for the process's lifetime,
                // so no failure here is worth stopping for.
                logger.LogError(e, "Could not queue the upload-session sweep; will retry next tick");
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
            j => j.Kind == ProcessingJobKinds.UploadSessionSweep
                && (j.Status == ProcessingJobStatus.Queued || j.Status == ProcessingJobStatus.Running),
            ct);
        if (pending)
        {
            return;
        }

        // No requester: a scheduled sweep notifies nobody.
        db.ProcessingJobs.Add(new ProcessingJob { Kind = ProcessingJobKinds.UploadSessionSweep });
        await db.SaveChangesAsync(ct);
    }
}
