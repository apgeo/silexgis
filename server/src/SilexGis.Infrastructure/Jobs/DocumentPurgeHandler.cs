// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Domain;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Deletes documents whose restore window has run out, and the bytes they hold.
///
/// <para>
/// This is the half of soft deletion that makes it a deletion. Marking a document takes it out
/// of every listing at once, which is what the person clicking asked for; this is what
/// eventually makes the disk agree. Without it the store only ever grows — which is the state
/// the development archive is in, and the reason the requirement asks for a delete path at all.
/// </para>
/// <para>
/// The order is deliberate: rows first, inside one transaction, then the blobs. A blob deleted
/// before its rows would leave a document pointing at content that is not there, which every
/// reader would report as a fault; a row deleted before its blob leaves an unreferenced file,
/// which is untidy and harmless and is swept the next time an operator looks. Given the choice,
/// leave litter rather than a broken row.
/// </para>
/// </summary>
public sealed class DocumentPurgeHandler(
    SilexGisDbContext db,
    IFileStore fileStore,
    ThumbnailService thumbnails,
    PageRenderService pages,
    IOptions<DocumentRetentionOptions> options,
    ILogger<DocumentPurgeHandler> logger) : IProcessingJobHandler
{
    /// <summary>
    /// How many documents one pass takes. A club emptying a year of mistakes should not hold
    /// the queue while it does, and the sweep runs often enough to catch up.
    /// </summary>
    private const int BatchSize = 100;

    public string Kind => ProcessingJobKinds.DocumentPurge;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var retention = options.Value.Retention;
        var cutoff = SoftDeleteRules.PurgeCutoff(DateTimeOffset.UtcNow, retention);

        // An indexed comparison over the filtered index rather than a predicate per row: the
        // rule computes the cut-off precisely so the database can use one.
        // Through the model-wide filter, which hides exactly the rows this exists to collect.
        var due = await db.Documents.AsNoTracking().IgnoreQueryFilters()
            .Where(d => d.DeletedAt != null && d.DeletedAt <= cutoff)
            .OrderBy(d => d.DeletedAt)
            .Select(d => d.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var documentId in due)
        {
            try
            {
                await PurgeAsync(documentId, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // One document that will not go is a fact about that document — most often
                // something still holding a foreign key to one of its files. The sweep carries
                // on; the row stays marked and is tried again next time.
                logger.LogWarning(e, "Could not purge document {DocumentId}", documentId);
            }
        }
    }

    private async Task PurgeAsync(Guid documentId, CancellationToken ct)
    {
        var versionIds = await db.DocumentVersions.AsNoTracking()
            .Where(v => v.DocumentId == documentId)
            .Select(v => v.Id)
            .ToListAsync(ct);
        var files = await db.StoredFiles
            .Where(f => versionIds.Contains(f.DocumentVersionId))
            .ToListAsync(ct);

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            // Everything that names the document without a foreign key of its own. The rest —
            // versions, files, pages, memberships, taggings, attachments — goes by cascade from
            // the document row, which is what those cascades are for.
            await db.ResLinkMembers
                .Where(m => m.EntityType == AttachedEntityType.Document && m.EntityId == documentId)
                .ExecuteDeleteAsync(ct);

            // An album's cover pointing at this picture is set null by its own foreign key; the
            // membership rows go with it. Nothing else has to be unpicked by hand.
            await db.Documents.IgnoreQueryFilters()
                .Where(d => d.Id == documentId)
                .ExecuteDeleteAsync(ct);
            await transaction.CommitAsync(ct);
        }

        // Only now, and best effort. The rows are gone, so nothing can reach these bytes; a
        // blob that will not delete is litter rather than a fault.
        foreach (var file in files)
        {
            try
            {
                await fileStore.DeleteAsync(file.StoragePath, CancellationToken.None);
                thumbnails.Purge(file.Id);
                pages.Purge(file.Id);
            }
            catch (IOException e)
            {
                logger.LogWarning(e, "Could not delete the content of purged file {FileId}", file.Id);
            }
        }
    }
}

/// <summary>How long a deleted document stays restorable.</summary>
public sealed class DocumentRetentionOptions
{
    public const string SectionName = "DocumentRetention";

    /// <summary>
    /// Days a deleted document can still be got back. Zero is a real setting — an installation
    /// that wants deletion to mean deletion — and is why this is a number of days rather than a
    /// nullable one.
    /// </summary>
    public int RetentionDays { get; set; } = (int)SoftDeleteRules.DefaultRetention.TotalDays;

    /// <summary>How often the sweep runs. Well inside the window, so nothing waits long past it.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(6);

    public TimeSpan Retention => TimeSpan.FromDays(Math.Max(0, RetentionDays));
}

/// <summary>
/// Queues the periodic purge of documents past their restore window.
/// </summary>
/// <remarks>
/// Scheduled rather than triggered, because what it responds to is time passing rather than
/// anything happening. Nothing is queued at startup: a restart loop would otherwise fill the
/// queue with sweeps.
/// </remarks>
public sealed class DocumentPurgeScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<DocumentRetentionOptions> options,
    ILogger<DocumentPurgeScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.SweepInterval;
        if (interval <= TimeSpan.Zero)
        {
            logger.LogInformation("Scheduled document purging is disabled");
            return;
        }

        using var timer = new PeriodicTimer(interval);
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
                logger.LogError(e, "Could not queue the document purge; will retry next tick");
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
            j => j.Kind == ProcessingJobKinds.DocumentPurge
                && (j.Status == ProcessingJobStatus.Queued || j.Status == ProcessingJobStatus.Running),
            ct);
        if (pending)
        {
            return;
        }

        // No requester: a scheduled sweep notifies nobody.
        db.ProcessingJobs.Add(new ProcessingJob { Kind = ProcessingJobKinds.DocumentPurge });
        await db.SaveChangesAsync(ct);
    }
}
