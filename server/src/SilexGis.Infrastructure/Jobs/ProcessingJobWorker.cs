// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Polls the processing_jobs table for the jobs of one lane and dispatches to the registered
/// handler for the job's kind. One job at a time per worker; failures are recorded on the row and
/// never crash the host. A DB-backed queue keeps the seam open for external workers.
/// </summary>
/// <remarks>
/// There is one worker per lane and the lanes divide the table between them, so work measured in
/// hours cannot hold up the short work queued behind it. Nothing else about the two differs, which
/// is why the loop lives here and the lane is the whole of what a worker says about itself.
/// </remarks>
public abstract class ProcessingJobWorkerBase(
    JobLane lane,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger logger) : BackgroundService
{
    private const int DefaultPollSeconds = 2;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollSeconds = configuration.GetValue("Jobs:PollSeconds", DefaultPollSeconds);
        if (pollSeconds <= 0)
        {
            // The same switch the notification drain carries, for the same reason. A test host
            // that queues a job and then means to run it itself has two runners the moment this
            // loop is polling: the claim below is the only mutual exclusion there is, and a caller
            // that does not issue it takes part in none of it. That failure is intermittent by
            // construction — it needs the poll to land inside the gap between enqueueing a job and
            // reading it back, so it fires under load and passes when the machine is quiet, which
            // is the worst shape a test failure can have. A host that has no use for the queue
            // switches it off here; one that needs it claims the row before running anything.
            logger.LogInformation("Job processing is switched off (Jobs:PollSeconds <= 0)");
            return;
        }

        var pollInterval = TimeSpan.FromSeconds(pollSeconds);

        // Migrations run in the host startup path before the app starts serving,
        // but hosted services start concurrently — tolerate a briefly missing table.
        await WaitForQueueAsync(stoppingToken);

        using (var startupScope = scopeFactory.CreateScope())
        {
            var db = startupScope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await JobsSql.RequeueInterruptedAsync(db, lane, stoppingToken);
        }

        using var timer = new PeriodicTimer(pollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Drain the queue, then sleep until the next tick.
                while (await RunNextAsync(stoppingToken))
                {
                }

                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Job worker iteration failed; continuing");
                await Task.Delay(pollInterval, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Takes the next ready job of this worker's lane and runs it; false when none was ready. What
    /// one turn of the poll does — open to a subclass so a test can drive a lane of its own a turn
    /// at a time instead of waiting on the timer.
    /// </summary>
    protected async Task<bool> RunNextAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var jobId = await JobsSql.ClaimNextAsync(db, lane, ct);
        if (jobId is null)
        {
            return false;
        }

        var job = await db.ProcessingJobs.FindAsync([jobId.Value], ct)
            ?? throw new InvalidOperationException($"Claimed job {jobId} not found.");

        var handler = scope.ServiceProvider
            .GetServices<IProcessingJobHandler>()
            .FirstOrDefault(h => h.Kind == job.Kind);

        if (handler is null)
        {
            job.Status = ProcessingJobStatus.Failed;
            job.Error = $"No handler registered for job kind '{job.Kind}'.";
            job.CompletedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            logger.LogError("No handler for job {JobId} of kind {Kind}", job.Id, job.Kind);
            return true;
        }

        try
        {
            await handler.ExecuteAsync(job, ct);
            job.Status = ProcessingJobStatus.Succeeded;
            job.Error = null;
        }
        catch (Exception e) when (!ct.IsCancellationRequested
            && handler.RetriesPassingFailures
            && JobRetry.MayRetry(job.Attempts, e))
        {
            // Put back through a context of its own: this one may hold the attempt's half-done
            // changes, and they must not be saved on the way out. Nothing is told to the requester
            // yet — the job has not finished, it is waiting for its next turn.
            var delay = JobRetry.DelayAfter(job.Attempts);
            logger.LogWarning(
                e, "Job {JobId} ({Kind}) failed in passing on attempt {Attempt}; taken again in {Delay}",
                job.Id, job.Kind, job.Attempts, delay);
            using var putBack = scopeFactory.CreateScope();
            await JobsSql.PutBackAsync(
                putBack.ServiceProvider.GetRequiredService<SilexGisDbContext>(), job.Id, delay, Truncated(e.Message), ct);
            return true;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Job {JobId} ({Kind}) failed", job.Id, job.Kind);

            // An attempt that left rows added and never committed is discarded whole, and its
            // failure is recorded through a context of its own. Saved here, those rows would be
            // inserted outside the transaction that should have held them and past whatever it
            // would have computed over them — an import's features without the protection they
            // inherit, for one. An attempt that added nothing is recorded here as before, keeping
            // what a handler changed on existing rows to mark its own failure.
            if (db.ChangeTracker.Entries().Any(x => x.State == EntityState.Added))
            {
                using var apart = scopeFactory.CreateScope();
                var fresh = apart.ServiceProvider.GetRequiredService<SilexGisDbContext>();
                var failed = await fresh.ProcessingJobs.FirstAsync(j => j.Id == job.Id, ct);
                failed.Status = ProcessingJobStatus.Failed;
                failed.Error = Truncated(e.Message);
                failed.CompletedAt = clock.GetUtcNow();
                NotifyRequester(fresh, failed);
                await fresh.SaveChangesAsync(ct);
                return true;
            }

            job.Status = ProcessingJobStatus.Failed;
            job.Error = e.Message;
        }

        job.CompletedAt = clock.GetUtcNow();
        NotifyRequester(db, job);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Tells whoever asked for the job how it went. Queued on the same context as the job's own
    /// terminal save, so the notification and the outcome commit together.
    /// </summary>
    /// <remarks>
    /// The worker knows the job's kind but not its subject's name — only each handler does — so
    /// the message names nothing and links to where the result is. That is a deliberate trade
    /// against giving every handler a second responsibility, and against a per-kind noun that
    /// would arrive in one language and be untranslatable.
    /// </remarks>
    /// <summary>The error column's length, which a put-back writes without the model's help.</summary>
    private static string Truncated(string message) => message.Length <= 4000 ? message : message[..4000];

    private static void NotifyRequester(SilexGisDbContext db, ProcessingJob job)
    {
        // Nothing enforces a requester (the column is nullable), so a job queued by the system
        // itself simply notifies nobody.
        if (job.RequestedBy is not { } requester)
        {
            return;
        }

        var failed = job.Status == ProcessingJobStatus.Failed;
        NotificationQueue.Enqueue(
            db,
            requester,
            NotificationCategory.JobCompleted,
            failed ? MessageTemplateCatalog.NotifyJobFailed : MessageTemplateCatalog.NotifyJobCompleted,
            failed
                ? new Dictionary<string, string> { ["error"] = job.Error ?? string.Empty }
                : new Dictionary<string, string>());
    }

    private async Task WaitForQueueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
                _ = await db.ProcessingJobs.AnyAsync(ct);
                return;
            }
            catch (Exception)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
    }
}

/// <summary>
/// The worker for everything that does not have a lane of its own — which is every kind of work
/// this installation queues except the terrain builds below.
/// </summary>
public sealed class ProcessingJobWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ProcessingJobWorker> logger)
    : ProcessingJobWorkerBase(JobLane.General, scopeFactory, configuration, logger);

/// <summary>
/// The worker for terrain builds, which have an instance to themselves.
/// </summary>
/// <remarks>
/// A build runs for minutes to hours, and a worker takes one job at a time with no time limit. On
/// the general worker a single build would therefore stand in front of every conversion, reading,
/// import and sweep queued behind it until it finished.
/// </remarks>
public sealed class TerrainProcessingJobWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<TerrainProcessingJobWorker> logger)
    : ProcessingJobWorkerBase(JobLane.Terrain, scopeFactory, configuration, logger);
