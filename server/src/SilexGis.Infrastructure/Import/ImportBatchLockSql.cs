// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Import;

/// <summary>The row lock every transaction that decides on a held upload, or undoes one, takes on its batches.</summary>
public static class ImportBatchLockSql
{
    /// <summary>
    /// Locks the given batch rows until the transaction ends, in id order so two transactions
    /// taking several of them cannot wait on each other.
    /// </summary>
    /// <remarks>
    /// Where a batch stands is worked out from all of its lines, and whether it may still be
    /// decided on or undone is read off its own row. Two transactions that touch different lines
    /// of one batch — two reviewers, a reviewer and a phone's resend, a reviewer and an undo —
    /// each read the other's lines as they were before it began, so both can conclude the batch
    /// still waits, or a reopen can land in a batch an undo has just closed. With the row locked
    /// the second waits, and every read it makes after the lock sees what the first committed.
    /// </remarks>
    public static async Task LockAsync(SilexGisDbContext db, IReadOnlyCollection<Guid> batchIds, CancellationToken ct)
    {
        if (batchIds.Count == 0)
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM import_batches WHERE id = ANY({0}) ORDER BY id FOR UPDATE",
            [batchIds.Distinct().ToArray()], ct);
    }
}
