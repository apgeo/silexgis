// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Import;

/// <summary>
/// Whether a batch an account commits is held for review, and how the held lines of an account
/// are found again — asked in one place by every boundary that creates an import batch (the phone
/// upload and each file import), so that marking an account means the same thing everywhere.
/// </summary>
/// <remarks>
/// The rule is about the account, not about what it sends or where: an account placed under review
/// has nothing it commits land unreviewed, whatever the source, and an account that is not has
/// every batch land as it always did. A phone upload is held line by line; every file import — a
/// vector file, a drop of photographs, a catalogue selection, a trip sheet, a SpeleoLoc archive or
/// a kept recording — is held as its commit request, with a waiting line per candidate
/// (<see cref="HeldFileImport"/>). Edits made in the browser create no import batch and are never
/// held.
/// </remarks>
public static class UploadReviewHold
{
    /// <summary>The value an upload's <c>clientFeatures</c> carries to say the device understands
    /// being answered that a row is held.</summary>
    public const string ClientFeature = "held";

    /// <summary>Whether what this account commits waits for a reviewer.</summary>
    public static Task<bool> AppliesToAsync(SilexGisDbContext db, Guid userId, CancellationToken ct = default) =>
        db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.UploadsNeedReview).FirstOrDefaultAsync(ct);

    /// <summary>
    /// The lines of this account's held batches from one source that still wait for a decision.
    /// </summary>
    public static IQueryable<ImportBatchItem> PendingLinesOf(
        SilexGisDbContext db, Guid userId, ImportSource source) =>
        from item in db.ImportBatchItems
        join batch in db.ImportBatches on item.ImportBatchId equals batch.Id
        where batch.ConfirmedByUserId == userId
            && batch.Source == source
            && batch.ReviewState == ImportReviewState.Pending
            && item.ReviewDecision == ImportReviewDecision.Pending
        select item;

    /// <summary>
    /// Restates where each touched batch stands from its lines — after a reviewer's decision, and
    /// after a later upload superseded lines of earlier batches. Called inside the transaction that
    /// changed the lines, so the queue never shows a batch decided while a line of it still waits.
    /// </summary>
    public static async Task<IReadOnlyList<ImportBatch>> SettleAsync(
        SilexGisDbContext db, IEnumerable<Guid> batchIds, Guid? reviewerId, DateTimeOffset now, CancellationToken ct)
    {
        var ids = batchIds.Distinct().ToList();
        var batches = await db.ImportBatches.Where(b => ids.Contains(b.Id)).ToListAsync(ct);
        var decisions = await db.ImportBatchItems.AsNoTracking()
            .Where(i => ids.Contains(i.ImportBatchId) && i.ReviewDecision != null)
            .Select(i => new { i.ImportBatchId, i.ReviewDecision })
            .ToListAsync(ct);
        foreach (var batch in batches.Where(b => b.IsHeld))
        {
            var state = UploadReviewDecisions.StateOf(
                decisions.Where(d => d.ImportBatchId == batch.Id).Select(d => d.ReviewDecision));
            if (state == batch.ReviewState)
            {
                continue;
            }

            batch.ReviewState = state;
            if (state != ImportReviewState.Pending)
            {
                batch.DecidedAt = now;
                batch.DecidedByUserId = reviewerId;
            }
        }

        await db.SaveChangesAsync(ct);
        return batches;
    }
}
