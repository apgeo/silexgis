// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Documents;

/// <summary>
/// The record of a drop: opening one, writing a line per file, and closing it with counts
/// that match the lines.
///
/// <para>
/// The counts are maintained alongside the lines rather than computed from them on the way
/// out, because the report is read far more often than it is written and a batch of five
/// hundred files should not be a five-hundred-row aggregate every time somebody opens the
/// list. They are only ever moved by <see cref="RecordAsync"/>, which writes the line and the
/// count in the same save — so the two cannot come apart the way they would if any caller
/// could add a line directly.
/// </para>
/// </summary>
public sealed class UploadBatchService(SilexGisDbContext db)
{
    /// <summary>Opens a batch. Tracked and saved, because the id is handed straight back.</summary>
    public async Task<UploadBatch> OpenAsync(
        Guid userId,
        UploadSource source,
        string? label,
        Guid? cabinetId,
        long? tagId,
        string? sourceDescription,
        CancellationToken ct = default)
    {
        var batch = new UploadBatch
        {
            StartedByUserId = userId,
            Source = source,
            Status = source == UploadSource.Interactive ? UploadBatchStatus.Open : UploadBatchStatus.Running,
            Label = label,
            CabinetId = cabinetId,
            TagId = tagId,
            SourceDescription = sourceDescription,
        };

        db.UploadBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        return batch;
    }

    /// <summary>
    /// Writes one line and moves the batch's counts to match it. Saves.
    /// </summary>
    /// <remarks>
    /// A line is written for every outcome including failures, which is the point: a report
    /// that only listed what worked would answer "did my archive import?" with silence about
    /// the twelve files that did not.
    /// </remarks>
    public async Task RecordAsync(
        Guid batchId,
        string sourcePath,
        long sizeBytes,
        IngestOutcome outcome,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        var batch = await db.UploadBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null)
        {
            // The batch was pruned while its own work was still running. The document that was
            // created is unaffected — it exists, it is filed, and it has simply lost its
            // provenance. Refusing here would undo a successful upload to protect a report.
            return;
        }

        db.UploadBatchItems.Add(new UploadBatchItem
        {
            UploadBatchId = batchId,
            SourcePath = Fit(sourcePath),
            SizeBytes = sizeBytes,
            Outcome = outcome.Outcome,
            DocumentId = outcome.DocumentId,
            CabinetId = outcome.CabinetId,
            Reason = outcome.Reason,
            DuplicateOfDocumentId = outcome.DuplicateOfDocumentId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        batch.TotalCount++;
        switch (outcome.Outcome)
        {
            case UploadItemOutcome.Stored:
                batch.StoredCount++;
                break;
            case UploadItemOutcome.Skipped:
                batch.SkippedCount++;
                break;
            case UploadItemOutcome.Failed:
                batch.FailedCount++;
                break;
            default:
                break;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Closes a batch: nothing more will be added. Saves. An <paramref name="error"/> means
    /// the batch as a whole failed — the archive would not open, the directory was not
    /// there — as distinct from a batch that ran to the end with some files failing.
    /// </summary>
    public async Task CompleteAsync(Guid batchId, string? error = null, CancellationToken ct = default)
    {
        var batch = await db.UploadBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null)
        {
            return;
        }

        batch.Status = error is null ? UploadBatchStatus.Completed : UploadBatchStatus.Failed;
        batch.Error = error is null ? null : Fit(error, 2000);
        batch.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Fits a value into its column. Source paths come from archives and from disks, so
    /// nothing bounds them: an over-long one is truncated rather than being allowed to fail
    /// the write of a line describing an upload that already succeeded.
    /// </summary>
    private static string Fit(string value, int maxLength = 2000) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
