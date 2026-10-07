// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Removes trips whose time to be put back has run out, with everything that cannot mean anything
/// without them.
/// </summary>
/// <remarks>
/// <para>
/// This is the half of deleting a trip that makes it a deletion. Marking one takes it out of every
/// listing at once and keeps every row, so that a mistake can be taken back exactly; this is what
/// eventually makes the database agree that it is gone. Until it runs, a deleted trip's roster,
/// links, files, tags, rules and place in a camp are all still there, hidden — which is the state
/// a restore depends on and the reason nothing but this pass may end it.
/// </para>
/// <para>
/// <b>One trip is one unit of work, in a context of its own.</b> The pass is handed the worker's
/// context, and the worker saves on that same context once the pass returns — to record how the
/// job ended. Removing a trip stages tracked changes: the rules anchored on it are removed one by
/// one so the trail records each withdrawal. If the trip after it then failed, those staged
/// removals would still be sitting in the worker's context and its bookkeeping save would carry
/// them out, outside any transaction of this pass. So each trip is removed in a scope that is
/// thrown away with whatever it had staged, and one trip that will not go costs the rest of the
/// pass nothing.
/// </para>
/// <para>
/// <b>Each trip is claimed before anything of it is touched.</b> Somebody may restore a trip in
/// the instant the pass reaches it. The claim is a write to the row guarded by the same condition
/// that selected it, so it holds the row against that restore for the length of this trip's
/// transaction and comes back empty when the restore got there first — in which case the trip is
/// left exactly as it is. Without it the pass would remove a trip a person had just been told
/// was back.
/// </para>
/// <para>
/// Rows first, inside the transaction, then bytes. The only bytes a trip owns are its generated
/// write-up's; a row removed before its blob leaves an unreferenced file, which is untidy and
/// harmless, while a blob removed before its row leaves a document pointing at nothing.
/// </para>
/// </remarks>
public sealed class TripPurgeHandler(
    SilexGisDbContext db,
    IServiceScopeFactory scopes,
    IOptions<TripRetentionOptions> options,
    TimeProvider clock,
    ILogger<TripPurgeHandler> logger) : IProcessingJobHandler
{
    /// <summary>
    /// How many trips one pass takes. An undone import can leave thousands at once, and the pass
    /// shares its worker with every conversion and reading queued behind it; the schedule runs
    /// often enough to catch up.
    /// </summary>
    private const int BatchSize = 100;

    public string Kind => ProcessingJobKinds.TripPurge;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        // No window, no pass: an installation that keeps deleted trips until somebody says
        // otherwise has nothing for this to measure against. Asked here as well as by the
        // schedule, because the schedule that queued a pass is not always the host that runs it:
        // one left waiting by a host that had a window is picked up after a restart by one that
        // may have been told to keep everything.
        if (TripDeletionRules.PurgeCutoff(clock.GetUtcNow(), options.Value.Window) is not { } cutoff)
        {
            return;
        }

        // An indexed comparison over the filtered index, through the filter that hides exactly
        // the rows this exists to collect.
        var due = await db.TripLogs.AsNoTracking().IgnoreQueryFilters()
            .Where(t => t.DeletedAt != null && t.DeletedAt <= cutoff)
            .OrderBy(t => t.DeletedAt)
            .Select(t => t.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        var removed = 0;
        foreach (var tripId in due)
        {
            try
            {
                if (await PurgeAsync(tripId, cutoff, ct))
                {
                    removed++;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // One trip that will not go is a fact about that trip. The pass carries on; the
                // row stays marked and is tried again next time.
                logger.LogWarning(e, "Could not remove deleted trip {TripLogId}", tripId);
            }
        }

        if (removed > 0)
        {
            logger.LogInformation("Removed {Count} deleted trips past their restore window", removed);
        }
    }

    private async Task<bool> PurgeAsync(Guid tripId, DateTimeOffset cutoff, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var scoped = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var trips = scope.ServiceProvider.GetRequiredService<TripLogWriteService>();
        var content = scope.ServiceProvider.GetRequiredService<StoredContentRemover>();

        IReadOnlyList<StoredFile> files;
        await using (var transaction = await scoped.Database.BeginTransactionAsync(ct))
        {
            // The claim. It changes nothing a reader could see and is not there to: it is a write
            // to the row under the condition that selected it, so a restore racing this pass
            // either finished first — and nothing matches — or waits on the row until this
            // transaction ends and then finds it gone.
            var claimed = await scoped.TripLogs.IgnoreQueryFilters()
                .Where(t => t.Id == tripId && t.DeletedAt != null && t.DeletedAt <= cutoff)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.DeletedAt, t => t.DeletedAt), ct);
            if (claimed == 0)
            {
                return false;
            }

            var trip = await scoped.TripLogs.IgnoreQueryFilters().FirstAsync(t => t.Id == tripId, ct);
            files = await trips.PurgeAsync(trip, ct);
            await scoped.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        // Only now, and best effort: the rows are gone, so nothing can reach these bytes.
        await content.DropAsync(files);
        return true;
    }
}

/// <summary>How long a deleted trip stays restorable.</summary>
public sealed class TripRetentionOptions
{
    public const string SectionName = "Trips";

    /// <summary>
    /// Days a deleted trip can still be put back before it is removed for good. Zero or less
    /// switches the removal off entirely: deleted trips are then kept, and stay restorable,
    /// until this is set to a window again.
    /// </summary>
    /// <remarks>
    /// One number rather than a window and a separate switch, because they are one decision —
    /// "does anything here go without a person deciding, and after how long" — and two settings
    /// for it would be two places to answer it differently.
    /// </remarks>
    public int DeletedRetentionDays { get; set; } = TripDeletionRules.DefaultRetentionDays;

    /// <summary>The window those days stand for, or null when nothing is removed.</summary>
    public TimeSpan? Window => TripDeletionRules.Window(DeletedRetentionDays);

    /// <summary>
    /// How often the pass is queued. Not an operator's setting: the window is counted in days,
    /// and a pass four times a day leaves a trip at most a few hours past it — a figure nobody
    /// has a reason to tune, and one fewer thing for an installation to get wrong.
    /// </summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromHours(6);
}

/// <summary>
/// Queues the periodic removal of trips past their restore window.
/// </summary>
/// <remarks>
/// Scheduled rather than triggered, because what it responds to is time passing rather than
/// anything happening. Nothing is queued at startup: a restart loop would otherwise fill the
/// queue with passes.
/// </remarks>
public sealed class TripPurgeScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<TripRetentionOptions> options,
    ILogger<TripPurgeScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Window is null)
        {
            logger.LogInformation(
                "Deleted trips are kept: no restore window is set, so nothing is scheduled to remove them");
            return;
        }

        using var timer = new PeriodicTimer(TripRetentionOptions.SweepInterval);
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
                logger.LogError(e, "Could not queue the removal of deleted trips; will retry next tick");
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

    /// <summary>
    /// Queues one pass, unless one is already waiting or running.
    /// </summary>
    /// <remarks>
    /// Reachable without a live schedule on purpose: that a tick queues exactly one pass, and a
    /// second tick beside a pending one queues none, is provable by asking for the tick directly
    /// rather than by starting a timer against a database a test shares.
    /// </remarks>
    public async Task QueuePassAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var pending = await db.ProcessingJobs.AnyAsync(
            j => j.Kind == ProcessingJobKinds.TripPurge
                && (j.Status == ProcessingJobStatus.Queued || j.Status == ProcessingJobStatus.Running),
            ct);
        if (pending)
        {
            return;
        }

        // No requester: a scheduled pass notifies nobody.
        db.ProcessingJobs.Add(new ProcessingJob { Kind = ProcessingJobKinds.TripPurge });
        await db.SaveChangesAsync(ct);
    }
}
