// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SilexGis.Domain;
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
    /// <summary>
    /// What a job taken back from a lost runner on its last allowed attempt is failed with. Fixed
    /// words, never an exception's: nothing threw that this process saw.
    /// </summary>
    public const string InterruptedError =
        "The job was interrupted before it could finish on its last allowed attempt, so it is not run again.";

    private readonly JobQueueOptions options =
        configuration.GetSection(JobQueueOptions.SectionName).Get<JobQueueOptions>() ?? new JobQueueOptions();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollSeconds = options.PollSeconds;
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

        // Whatever a previous process was running when it died is taken back once its lease has
        // run out — at start, and from then on every so often, because a runner is lost by more
        // than a restart: an outcome that could not be saved strands its job just the same, and so
        // does a process that died while another one was already up.
        // Timed on a monotonic stopwatch: an interval, not a moment, so a clock set back or forward
        // under the process neither holds the sweep off nor runs it on every poll.
        var sweepInterval = options.SweepSeconds > 0 ? TimeSpan.FromSeconds(options.SweepSeconds) : (TimeSpan?)null;
        System.Diagnostics.Stopwatch? sinceSweep = null;

        using var timer = new PeriodicTimer(pollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (sinceSweep is null || (sweepInterval is { } every && sinceSweep.Elapsed >= every))
                {
                    await ReturnStaleAsync(stoppingToken);
                    sinceSweep = System.Diagnostics.Stopwatch.StartNew();
                }

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
        var jobId = await JobsSql.ClaimNextAsync(db, lane, options.Lease, ct);
        if (jobId is null)
        {
            return false;
        }

        var job = await db.ProcessingJobs.FindAsync([jobId.Value], ct)
            ?? throw new InvalidOperationException($"Claimed job {jobId} not found.");

        // The lease is renewed beside the handler rather than by it: a handler that spends an hour
        // inside one call into a conversion library says nothing for that hour, and must not lose
        // its job for it. It stops before the outcome is written, whatever the outcome, so a job
        // whose outcome could not be written is not kept alive by it either.
        using var renewal = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var renewing = RenewLeaseAsync(job.Id, job.Attempts, renewal.Token);
        try
        {
            await RunClaimedAsync(scope, db, job, clock, ct);
        }
        finally
        {
            await renewal.CancelAsync();
            await renewing;
        }

        return true;
    }

    private async Task RunClaimedAsync(
        IServiceScope scope, SilexGisDbContext db, ProcessingJob job, TimeProvider clock, CancellationToken ct)
    {
        // One job run is one change set, recorded as done for the account that asked for it: no
        // request is behind the work to say who the caller is, and a history row of "nobody"
        // would hide who caused it. A handler that starts an action of its own (confirming an
        // import) names that action itself and keeps the user.
        var changes = scope.ServiceProvider.GetRequiredService<IChangeContext>();
        changes.Begin(ChangeSource.Job);
        changes.ActFor(job.RequestedBy);

        var handler = scope.ServiceProvider
            .GetServices<IProcessingJobHandler>()
            .FirstOrDefault(h => h.Kind == job.Kind);

        if (handler is null)
        {
            job.Status = ProcessingJobStatus.Failed;
            job.Error = $"No handler registered for job kind '{job.Kind}'.";
            job.CompletedAt = clock.GetUtcNow();
            job.LeaseExpiresAt = null;
            await db.SaveChangesAsync(ct);
            logger.LogError("No handler for job {JobId} of kind {Kind}", job.Id, job.Kind);
            return;
        }

        // Every write of how this run ended is fenced on the attempt this worker claimed. A runner
        // can outlive its lease — renewals failing longer than the lease while the database was
        // unreachable — and by then the sweep may have handed the job to another runner as the next
        // attempt. The late runner's outcome is then about an attempt that no longer exists, and
        // written over the live one it would end a job that is still running.
        var attempt = job.Attempts;
        try
        {
            await handler.ExecuteAsync(job, ct);
            job.Status = ProcessingJobStatus.Succeeded;
            job.Error = null;
        }
        catch (Exception e) when (handler.RetriesPassingFailures && JobRetry.WillBeRetried(job, e, ct))
        {
            // Put back through a context of its own: this one may hold the attempt's half-done
            // changes, and they must not be saved on the way out. Nothing is told to the requester
            // yet — the job has not finished, it is waiting for its next turn. Should the put-back
            // itself fail (the database that dropped the handler's connection still restarting),
            // the job stays Running with a lease nothing renews, and the sweep takes it back.
            var delay = JobRetry.DelayAfter(job.Attempts);
            logger.LogWarning(
                e, "Job {JobId} ({Kind}) failed in passing on attempt {Attempt}; taken again in {Delay}",
                job.Id, job.Kind, job.Attempts, delay);
            using var putBack = scopeFactory.CreateScope();
            if (!await JobsSql.PutBackAsync(
                    putBack.ServiceProvider.GetRequiredService<SilexGisDbContext>(),
                    job.Id, attempt, delay, Truncated(e.Message), ct))
            {
                LogTakenOver(job.Id, attempt);
            }

            return;
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
                await RecordFailureApartAsync(job.Id, attempt, Truncated(e.Message), clock, ct);
                return;
            }

            job.Status = ProcessingJobStatus.Failed;
            job.Error = Truncated(e.Message);
        }

        job.CompletedAt = clock.GetUtcNow();
        job.LeaseExpiresAt = null;
        NotifyRequester(db, job);
        try
        {
            // The transaction ends inside this block, so a save that throws has rolled it back —
            // and let go of the job's row — before the failure is recorded on another connection.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            if (!await JobsSql.HoldIfStillOursAsync(db, job.Id, attempt, ct))
            {
                LogTakenOver(job.Id, attempt);
                return;
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            // The outcome did not commit, and neither did whatever the handler left on this context
            // with it. Left at that, the job would stay Running and every scheduled pass that waits
            // for its kind to finish would wait for good. It is recorded as failed apart from what
            // would not save — a success whose changes were refused did not succeed — and keeps the
            // failure that ended it, where there was one. A host that is stopping is a different
            // matter: the job was interrupted, not failed, and is taken back on the next start.
            logger.LogError(e, "The outcome of job {JobId} ({Kind}) could not be saved", job.Id, job.Kind);
            var error = job.Status == ProcessingJobStatus.Failed && job.Error is { } failure
                ? failure
                : Truncated(e.GetBaseException().Message);
            await RecordFailureApartAsync(job.Id, attempt, error, clock, ct);
        }
    }

    /// <summary>
    /// Records a job as failed through a context of its own, telling its requester — while the job
    /// is still on the attempt this worker claimed. If even that cannot be written — the database
    /// is unreachable — the job is left Running with a lease that nothing renews any more, and the
    /// sweep takes it back once the database answers again.
    /// </summary>
    private async Task RecordFailureApartAsync(
        long jobId, int attempt, string error, TimeProvider clock, CancellationToken ct)
    {
        try
        {
            using var apart = scopeFactory.CreateScope();
            var fresh = apart.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await using var transaction = await fresh.Database.BeginTransactionAsync(ct);
            if (!await JobsSql.HoldIfStillOursAsync(fresh, jobId, attempt, ct))
            {
                LogTakenOver(jobId, attempt);
                return;
            }

            var failed = await fresh.ProcessingJobs.FirstAsync(j => j.Id == jobId, ct);
            failed.Status = ProcessingJobStatus.Failed;
            failed.Error = error;
            failed.CompletedAt = clock.GetUtcNow();
            failed.LeaseExpiresAt = null;
            NotifyRequester(fresh, failed);
            await fresh.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            logger.LogError(e, "The failure of job {JobId} could not be recorded; its lease will run out", jobId);
        }
    }

    private void LogTakenOver(long jobId, int attempt) =>
        logger.LogWarning(
            "Job {JobId} is no longer on attempt {Attempt}, which this worker ran: its lease ran out and the "
            + "job was taken back, so how that attempt ended is not written over the job",
            jobId, attempt);

    /// <summary>
    /// Takes back this lane's jobs whose runner was lost, failing — and telling the requester of —
    /// those that have used up their attempts. One transaction, so a failed job, the stop its
    /// handler writes on what it was working on, and its notice commit together, and a lease that
    /// runs out between the two statements of the sweep cannot land the job on the wrong side of
    /// the ceiling. Open to a subclass so a test can drive it.
    /// </summary>
    protected async Task ReturnStaleAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var failed = await JobsSql.ReturnStaleAsync(db, lane, JobRetry.MaxAttempts, InterruptedError, ct);
        if (failed.Count > 0)
        {
            logger.LogWarning(
                "Stopped {Count} job(s) of this lane interrupted on their last allowed attempt: {JobIds}",
                failed.Count, failed);
            var jobs = await db.ProcessingJobs.Where(j => failed.Contains(j.Id)).ToListAsync(ct);

            // The handlers first, each behind a savepoint of its own: one whose stop cannot be
            // written must not keep the job — or the other jobs of this sweep — Running, which
            // would bring the sweep back to the same failure on every pass.
            var handlers = scope.ServiceProvider.GetServices<IProcessingJobHandler>().ToList();
            foreach (var job in jobs)
            {
                if (handlers.FirstOrDefault(h => h.Kind == job.Kind) is { } handler)
                {
                    await AbandonAsync(db, transaction, handler, job, ct);
                }
            }

            foreach (var job in jobs)
            {
                NotifyRequester(db, job);
            }

            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    private async Task AbandonAsync(
        SilexGisDbContext db,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        IProcessingJobHandler handler,
        ProcessingJob job,
        CancellationToken ct)
    {
        const string Savepoint = "abandon_job";
        await transaction.CreateSavepointAsync(Savepoint, ct);
        try
        {
            await handler.AbandonedAsync(job, ct);
            await transaction.ReleaseSavepointAsync(Savepoint, ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            logger.LogError(
                e, "What job {JobId} ({Kind}) was working on could not be marked stopped", job.Id, job.Kind);
            await transaction.RollbackToSavepointAsync(Savepoint, ct);

            // Whatever the handler left tracked goes with its savepoint; only the jobs themselves,
            // unchanged, stay attached for the notices below.
            foreach (var entry in db.ChangeTracker.Entries().Where(x => x.State != EntityState.Unchanged).ToList())
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private async Task RenewLeaseAsync(long jobId, int attempt, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(options.Heartbeat);
            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    await JobsSql.RenewLeaseAsync(
                        scope.ServiceProvider.GetRequiredService<SilexGisDbContext>(), jobId, attempt, options.Lease, ct);
                }
                catch (Exception e) when (!ct.IsCancellationRequested)
                {
                    // One missed renewal is what the lease is sized for; the next tick tries again.
                    logger.LogWarning(e, "The lease of job {JobId} could not be renewed", jobId);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
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
    private static void NotifyRequester(SilexGisDbContext db, ProcessingJob job)
    {
        // Nothing enforces a requester (the column is nullable), so a job queued by the system
        // itself simply notifies nobody.
        if (job.RequestedBy is not { } requester)
        {
            return;
        }

        var failed = job.Status == ProcessingJobStatus.Failed;
        var subject = ProtectedSubjectOf(job);
        NotificationQueue.Enqueue(
            db,
            requester,
            NotificationCategory.JobCompleted,
            failed ? MessageTemplateCatalog.NotifyJobFailed : MessageTemplateCatalog.NotifyJobCompleted,
            failed
                ? new Dictionary<string, string> { ["error"] = job.Error ?? string.Empty }
                : new Dictionary<string, string>(),
            subject?.Kind,
            subject?.Id);
    }

    /// <summary>
    /// The protected row a job is about, for the kinds whose subject is one: a geofile import
    /// names its geofile and a raster conversion its map. Every other kind (a terrain build, an
    /// account export, a basemap extract…) has no protected row, so its notice stays untargeted.
    /// </summary>
    /// <remarks>
    /// Read from the payload rather than a column of the job, so no schema change and no
    /// enqueuer has to remember to say it. A payload that does not parse simply yields no
    /// subject: the notice is still sent, to the same single account, as it was before.
    /// </remarks>
    public static (NotificationTargetKind Kind, Guid Id)? ProtectedSubjectOf(ProcessingJob job)
    {
        try
        {
            switch (job.Kind)
            {
                case ProcessingJobKinds.GeofileImport:
                    return JsonSerializer.Deserialize<GeofileImportPayload>(job.Payload, JsonSerializerOptions.Web) is { } g
                        && g.GeofileId != Guid.Empty
                        ? (NotificationTargetKind.Geofile, g.GeofileId)
                        : null;
                case ProcessingJobKinds.RasterCog:
                    return JsonSerializer.Deserialize<RasterCogPayload>(job.Payload, JsonSerializerOptions.Web) is { } r
                        && r.GeoreferencedMapId != Guid.Empty
                        ? (NotificationTargetKind.GeoreferencedMap, r.GeoreferencedMapId)
                        : null;
                case ProcessingJobKinds.MapSheetRender:
                case ProcessingJobKinds.MapPlacementWarp:
                    return JsonSerializer.Deserialize<MapSheetPayload>(job.Payload, JsonSerializerOptions.Web) is { } m
                        && m.GeoreferencedMapId != Guid.Empty
                        ? (NotificationTargetKind.GeoreferencedMap, m.GeoreferencedMapId)
                        : null;
                default:
                    return null;
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Cut to the error column's length. Every path that records a failure goes through this: a
    /// message the column refuses would fail the save that records the failure.
    /// </summary>
    private static string Truncated(string message) => message.Length <= 4000 ? message : message[..4000];

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
    : ProcessingJobWorkerBase(JobLane.GeneralFor(configuration), scopeFactory, configuration, logger);

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
    : ProcessingJobWorkerBase(JobLane.TerrainFor(configuration), scopeFactory, configuration, logger);
