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
/// The bytes go first and the row second. A row deleted before its blob leaves a file nothing
/// knows the name of, which is the very state this exists to prevent; a blob deleted before
/// its row leaves a session that resumes onto missing content, which the next sweep clears
/// anyway.
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

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var expired = await db.UploadSessions
            .Where(s => s.ExpiresAt <= now)
            .OrderBy(s => s.ExpiresAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var session in expired)
        {
            try
            {
                await fileStore.DeleteAsync(session.StoragePath, ct);
            }
            catch (IOException e)
            {
                // A blob that will not delete is a stranded file an operator can sweep. The
                // row goes anyway: keeping it would mean retrying the same failure for ever
                // and never collecting anything behind it.
                logger.LogWarning(
                    e, "Could not delete the partial content of upload session {SessionId}", session.Id);
            }

            db.UploadSessions.Remove(session);
        }

        await db.SaveChangesAsync(ct);
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
