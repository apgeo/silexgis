// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>
/// The two messages review sends: to the people who may decide, that an upload waits; and to the
/// uploader, that a reviewer decided. Queued, never saved here — each lands in the save that
/// commits what it reports, so a message exists exactly when its subject does.
/// </summary>
/// <remarks>
/// Neither names anything the batch holds. A held change can carry a cave, a place or a position
/// that nobody has yet decided may be written, and a message is read wherever its recipient's mail
/// is; both link to the batch's page, which asks the reader's rights before it shows a line.
/// </remarks>
public static class UploadReviewNotices
{
    /// <summary>The page a held batch is reviewed on, and its uploader follows it on.</summary>
    public static string PageOf(Guid batchId) => $"/review/batches/{batchId}";

    /// <summary>
    /// Tells everybody who may decide about uploads that <paramref name="batch"/> waits, except its
    /// own uploader — nobody decides about their own uploads.
    /// </summary>
    public static async Task AwaitingAsync(
        SilexGisDbContext db, ImportBatch batch, int lineCount, CancellationToken ct = default)
    {
        foreach (var reviewer in await UploadReviewers.WriteHolderIdsAsync(db, ct))
        {
            if (reviewer == batch.ConfirmedByUserId)
            {
                continue;
            }

            NotificationQueue.Enqueue(
                db,
                reviewer,
                NotificationCategory.UploadAwaitingReview,
                MessageTemplateCatalog.NotifyUploadAwaitingReview,
                new Dictionary<string, string>
                {
                    ["lineCount"] = lineCount.ToString(CultureInfo.InvariantCulture),
                    ["url"] = PageOf(batch.Id),
                });
        }
    }

    /// <summary>
    /// Tells the uploader of each batch a decision reached what it decided there: one message per
    /// batch, counting the lines this decision settled in it.
    /// </summary>
    public static void Reviewed(SilexGisDbContext db, IReadOnlyList<ImportBatchItem> decided, IReadOnlyList<ImportBatch> batches)
    {
        foreach (var batch in batches)
        {
            var lines = decided.Where(l => l.ImportBatchId == batch.Id).ToList();
            if (lines.Count == 0)
            {
                continue;
            }

            NotificationQueue.Enqueue(
                db,
                batch.ConfirmedByUserId,
                NotificationCategory.UploadReviewed,
                MessageTemplateCatalog.NotifyUploadReviewed,
                new Dictionary<string, string>
                {
                    ["acceptedCount"] = Count(lines, ImportReviewDecision.Accepted),
                    ["rejectedCount"] = Count(lines, ImportReviewDecision.Rejected),
                    ["failedCount"] = Count(lines, ImportReviewDecision.Failed),
                    ["url"] = PageOf(batch.Id),
                });
        }
    }

    private static string Count(IEnumerable<ImportBatchItem> lines, ImportReviewDecision decision) =>
        lines.Count(l => l.ReviewDecision == decision).ToString(CultureInfo.InvariantCulture);
}
