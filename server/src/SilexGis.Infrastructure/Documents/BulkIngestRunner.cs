// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Files;

namespace SilexGis.Infrastructure.Documents;

/// <summary>
/// One file a bulk source is offering: what it is called, how big it says it is, and how to
/// read it. The stream is opened lazily so a walk over ten thousand files holds one open at a
/// time rather than ten thousand.
/// </summary>
public sealed record BulkSourceFile(
    string SourcePath, long SizeBytes, Func<CancellationToken, Task<Stream>> OpenAsync);

/// <summary>
/// Walks a bulk source — an expanded archive, a directory on the server's disk — and files
/// every file in it, writing a line of the batch's report for each.
///
/// <para>
/// Shared between the two because they differ only in where the bytes come from. Everything
/// that makes the walk correct is the same for both: refusing a path before it can name
/// somewhere it should not, checking the limits per file rather than once at the start,
/// removing the bytes again when the file turns out to be a duplicate, and carrying on after
/// a failure instead of abandoning four hundred good files because the two hundredth was
/// corrupt.
/// </para>
/// <para>
/// One file's failure is recorded and stepped over. That is the whole difference between a
/// tool a club will hand its archive to and one it will not: an import that stops dead on the
/// first unreadable scan has to be restarted by hand, and restarting it re-imports everything
/// that already worked.
/// </para>
/// </summary>
public sealed class BulkIngestRunner(
    ContentIntake intake,
    UploadIngestService ingest,
    UploadBatchService batches,
    UploadAllowanceService allowances,
    IFileStore fileStore)
{
    /// <summary>
    /// Files everything the source offers into <paramref name="batch"/>, and closes it.
    /// </summary>
    /// <param name="ctx">
    /// The access context of whoever asked for the import. Every filing decision is made
    /// against it, so a queued walk grants nothing its requester did not already hold.
    /// </param>
    public async Task RunAsync(
        UploadBatch batch,
        AccessContext ctx,
        IAsyncEnumerable<BulkSourceFile> source,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(source);

        // Read once and then kept up to date by hand. Re-reading it per file would be an
        // aggregate over the whole store for every one of five hundred files; the running
        // figure is exact for this walk, and drifts only if somebody uploads through another
        // route while it runs — in which case the next file's own size check still binds and
        // the drift is one file's worth, not a bypass.
        var allowance = await allowances.ForAsync(ctx.UserId, ct);
        var userUsed = allowance.UserUsedBytes;
        var storeUsed = allowance.StoreUsedBytes;

        await foreach (var file in source.WithCancellation(ct))
        {
            var current = allowance with { UserUsedBytes = userUsed, StoreUsedBytes = storeUsed };
            var outcome = await IngestOneAsync(batch, ctx, current, file, ct);

            if (outcome.Outcome == UploadItemOutcome.Stored)
            {
                userUsed += file.SizeBytes;
                storeUsed += file.SizeBytes;
            }

            await batches.RecordAsync(batch.Id, file.SourcePath, file.SizeBytes, outcome, ct);
        }
    }

    private async Task<IngestOutcome> IngestOneAsync(
        UploadBatch batch,
        AccessContext ctx,
        UploadAllowance allowance,
        BulkSourceFile file,
        CancellationToken ct)
    {
        // Refused before a byte is read, both because there is no point transferring what will
        // not be kept and because the path check is what stops an entry naming somewhere
        // outside the tree from ever reaching code that opens things.
        var segments = FilingPaths.FolderSegmentsOf(file.SourcePath);
        if (segments is null)
        {
            return new IngestOutcome(UploadItemOutcome.Skipped, UploadItemReasons.PathRefused);
        }

        if (UploadLimits.Refuse(allowance, FilingPaths.FileNameOf(file.SourcePath), file.SizeBytes) is { } refusal)
        {
            return new IngestOutcome(UploadItemOutcome.Skipped, refusal);
        }

        StoredContent content;
        try
        {
            await using var stream = await file.OpenAsync(ct);
            content = await intake.FromStreamAsync(
                stream, FilingPaths.FileNameOf(file.SourcePath), declaredMediaType: null, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A single unreadable file is a fact about that file. The walk carries on.
            return new IngestOutcome(UploadItemOutcome.Failed, UploadItemReasons.Unreadable);
        }

        IngestOutcome outcome;
        try
        {
            outcome = await ingest.RecordAsync(
                content,
                file.SourcePath,
                ctx,
                new UploadDestination(batch.CabinetId, segments),
                batch.Id,

                // A bulk source cannot answer a duplicate warning — there is nobody at the
                // other end to ask — so a duplicate the importer can already see is skipped
                // and named in the report. That is the answer they would have given: an
                // archive re-imported after a partial run should add what is missing, not
                // make a second copy of everything.
                allowDuplicate: false,
                ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await DiscardAsync(content);
            return new IngestOutcome(UploadItemOutcome.Failed, UploadItemReasons.Unreadable);
        }

        // Bytes land before the write path can decide whether they belong to a document at
        // all, so anything that did not become one takes its bytes back out. Left behind, a
        // re-imported archive would grow the store by its whole size every time.
        if (outcome.Outcome != UploadItemOutcome.Stored)
        {
            await DiscardAsync(content);
        }

        return outcome;
    }

    private async Task DiscardAsync(StoredContent content)
    {
        try
        {
            await fileStore.DeleteAsync(content.StoragePath, CancellationToken.None);
        }
        catch (IOException)
        {
            // A blob that will not delete is a stranded file, not a failed import. The import
            // is what the person is waiting on; the disk is something an operator can sweep.
        }
    }
}
