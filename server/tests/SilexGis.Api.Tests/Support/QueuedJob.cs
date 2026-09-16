// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// The one sanctioned way for a test to make a job that is sitting in the queue run.
///
/// <para>
/// A test that wants a queued job carried through must not simply load the row and call its
/// handler. The queue's only mutual exclusion is the claiming UPDATE — <c>status = 1 … WHERE
/// status = 0</c>, guarded by <c>FOR UPDATE SKIP LOCKED</c> — and a caller that never issues it
/// takes part in none of it. Where a host leaves its background worker running, the worker's poll
/// can therefore claim and run the very row the test is about to run by hand, and both executions
/// then pass whatever "already done" read the handler makes before either of them has committed.
/// For a handler whose work lands under a key fixed by the caller that queued it, the loser of
/// that race dies on a duplicate key; for one whose work is a sweep it silently does everything
/// twice. Both failures need the poll to land inside a window measured in tens of milliseconds,
/// so they fire under load and pass on a quiet machine, which is the worst shape a test failure
/// can have.
/// </para>
/// <para>
/// So this claims the row exactly as a worker does, and the claim decides who runs the handler.
/// Winning it means this caller owns the job and runs it here, at the moment the test chose;
/// losing it means the queue already has the job in hand, and the only correct thing left is to
/// wait for it to finish. Either way the handler runs exactly once and the row ends in a terminal
/// state, so what the test asserts afterwards is true for the same reason every time.
/// </para>
/// </summary>
internal static class QueuedJob
{
    /// <summary>How long to wait for a run the queue took off us. Generous: a commit of a few
    /// hundred rows against a container eight test classes are sharing takes seconds.</summary>
    private static readonly TimeSpan CompletionDeadline = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Carries one queued job through, exactly once, and answers the row in its terminal state.
    /// </summary>
    /// <remarks>
    /// Throws whatever the handler threw when this caller ran it, which the background worker
    /// would have swallowed onto the row instead — a test asserting that a reading fails wants the
    /// exception, and one that does not want it fails anyway. A job the queue ran and recorded as
    /// failed throws too, so that the two paths cannot be told apart by whether a test passed.
    /// </remarks>
    public static async Task<ProcessingJob> RunAsync(
        IServiceProvider hostServices, long jobId, CancellationToken ct = default)
    {
        await using var scope = hostServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // The claim, and the whole of the mutual exclusion. One statement, so the status predicate
        // is evaluated under the row lock: a worker that got there first leaves this matching
        // nothing, and a worker that arrives while this is in flight finds the row no longer
        // queued. Written through the update-without-loading path rather than as raw SQL because
        // it is the same single UPDATE either way, and this one needs no string.
        var claimed = await db.ProcessingJobs
            .Where(j => j.Id == jobId && j.Status == ProcessingJobStatus.Queued)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(j => j.Status, ProcessingJobStatus.Running)
                    .SetProperty(j => j.StartedAt, DateTimeOffset.UtcNow)
                    .SetProperty(j => j.Attempts, j => j.Attempts + 1),
                ct);

        if (claimed == 0)
        {
            // Somebody else owns it — the host's own worker, in the classes that keep one. Waiting
            // is not a weaker answer than running it here: the job is the same job and the handler
            // is the same code, and by the time this returns it has been through.
            return await WaitForCompletionAsync(db, jobId, ct);
        }

        var job = await db.ProcessingJobs.SingleAsync(j => j.Id == jobId, ct);
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .SingleOrDefault(h => h.Kind == job.Kind)
            ?? throw new InvalidOperationException(
                $"No handler is registered for job kind '{job.Kind}'.");

        // Stamped the way the worker stamps it, so a row this ran and a row the queue ran are
        // indistinguishable afterwards — which is what lets a test assert on job status without
        // caring which of the two carried it. The requester's notification is deliberately not
        // queued here: that is the queue telling somebody their work finished, and nobody asked
        // this host for anything.
        try
        {
            await handler.ExecuteAsync(job, ct);
            job.Status = ProcessingJobStatus.Succeeded;
            job.Error = null;
        }
        catch (Exception e)
        {
            job.Status = ProcessingJobStatus.Failed;
            job.Error = e.Message;
            job.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            throw;
        }

        job.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return job;
    }

    /// <summary>
    /// Runs a job's handler a second time, for a test whose subject is the retry itself.
    /// </summary>
    /// <remarks>
    /// The queue re-delivers a job whose handler threw, and a handler that has already committed
    /// must survive being asked again — which is a real property and needs a test that drives it
    /// deliberately. What makes this safe where <see cref="RunAsync"/> is needed is that the first
    /// run is already over: this waits for the row to reach a terminal status before running
    /// anything, so no worker is inside the job at the moment the second run starts. A test that
    /// executed a handler by hand without that wait would not be retrying, it would be a second
    /// writer racing the first.
    /// </remarks>
    public static async Task RunAgainAsync(
        IServiceProvider hostServices, long jobId, CancellationToken ct = default)
    {
        await using var scope = hostServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var job = await WaitForCompletionAsync(db, jobId, ct);
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .SingleOrDefault(h => h.Kind == job.Kind)
            ?? throw new InvalidOperationException(
                $"No handler is registered for job kind '{job.Kind}'.");

        // No terminal stamp: the row already carries the outcome of the run that counted, and a
        // retry driven from here is not the queue changing its mind about it.
        await handler.ExecuteAsync(job, ct);
    }

    /// <summary>
    /// The jobs of one kind this host has, newest first, as snapshots a caller can read a payload
    /// out of to find the one it queued.
    /// </summary>
    /// <remarks>
    /// Deliberately not filtered to queued rows. A class that keeps its worker may have had the
    /// row claimed already, and a caller filtering on <c>Queued</c> would then find nothing, run
    /// nothing, and assert against work that has not happened — the same race by a quieter route.
    /// Untracked, so none of these can be handed to a handler behind
    /// <see cref="RunAsync"/>'s back.
    /// </remarks>
    public static async Task<IReadOnlyList<ProcessingJob>> OfKindAsync(
        IServiceProvider hostServices, string kind, CancellationToken ct = default)
    {
        await using var scope = hostServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        return await db.ProcessingJobs.AsNoTracking()
            .Where(j => j.Kind == kind)
            .OrderByDescending(j => j.Id)
            .ToListAsync(ct);
    }

    private static async Task<ProcessingJob> WaitForCompletionAsync(
        SilexGisDbContext db, long jobId, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + CompletionDeadline;
        while (true)
        {
            var job = await db.ProcessingJobs.AsNoTracking().SingleAsync(j => j.Id == jobId, ct);
            if (job.CompletedAt is not null)
            {
                if (job.Status == ProcessingJobStatus.Failed)
                {
                    throw new InvalidOperationException(
                        $"Job {jobId} ({job.Kind}) failed on the queue: {job.Error}");
                }

                return job;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException(
                    $"Job {jobId} ({job.Kind}) was claimed elsewhere and did not finish within "
                    + $"{CompletionDeadline.TotalSeconds:0} s (status {job.Status}).");
            }

            await Task.Delay(PollInterval, ct);
        }
    }
}
